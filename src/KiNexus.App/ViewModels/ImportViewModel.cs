using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Models;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Ein Modell, das Ollama auf der Festplatte hat (aus den Manifesten gelesen, Ollama muss nicht laufen).</summary>
public sealed partial class OllamaImportRow(OllamaInstalledModel model, bool isImported) : ObservableObject
{
    public OllamaInstalledModel Model { get; } = model;
    public string Name => Model.Name;
    public string SizeText => DisplayFormat.Bytes((Model.Model?.Size ?? 0) + (Model.Projector?.Size ?? 0));
    public string ImagesText => Model.Projector is null ? "nein" : "ja";

    [ObservableProperty] private bool _isImported = isImported;
}

/// <summary>
/// Importieren (Bauplan 8.3): GGUF-Datei per Drag &amp; Drop oder Datei-Dialog (kopieren oder verschieben),
/// oder ein Ollama-Modell per Hardlink übernehmen. Danach folgt ein Ladetest.
/// </summary>
public sealed partial class ImportViewModel(
    CreateHubViewModel hub,
    ModelImporter importer,
    OllamaModelStore ollamaStore,
    OllamaImporter ollamaImporter,
    ModelLibrary library,
    IDialogService dialogs,
    ILogger<ImportViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickFilesCommand), nameof(ImportFromOllamaCommand))]
    private bool _isImporting;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    public ObservableCollection<OllamaImportRow> OllamaModels { get; } = [];

    [ObservableProperty] private string _ollamaHint = "";

    public Task RefreshAsync()
    {
        var imported = library.Scan().Select(m => m.Manifest.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        OllamaModels.Clear();
        foreach (var model in ollamaStore.List().Where(m => m.Model is not null))
        {
            OllamaModels.Add(new OllamaImportRow(model, imported.Contains("ollama:" + model.Name)));
        }

        OllamaHint = OllamaModels.Count == 0
            ? $"Keine Ollama-Modelle gefunden (gesucht in {ollamaStore.ModelsDirectory})."
            : "Übernehmen legt einen Hardlink an: kein doppelter Speicherplatz, Ollama-Dateien bleiben unverändert.";
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task PickFilesAsync()
    {
        var files = dialogs.PickGgufFiles();
        return files.Count == 0 ? Task.CompletedTask : ImportFilesAsync(files);
    }

    private bool CanStart => !IsImporting;

    /// <summary>
    /// Nimmt ein Modell auf (Drag &amp; Drop oder Datei-Dialog). Erlaubt: eine Modell-Datei (bzw. die Teile eines Modells)
    /// und optional eine mmproj-Datei für Bilder.
    /// </summary>
    public async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        if (IsImporting)
        {
            return;
        }

        var ggufs = files.Where(f => f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).ToList();
        if (ggufs.Count == 0)
        {
            hub.ShowNotice("Das ist keine GGUF-Datei. Ziehe eine .gguf-Datei hierher (optional mit der passenden mmproj-Datei).", isError: true);
            return;
        }

        List<(string Path, GgufMetadata Metadata)> headers;
        try
        {
            headers = await Task.Run(() => ggufs.Select(f => (f, GgufReader.Read(f))).ToList());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            hub.ShowNotice("Die Datei ist keine gültige GGUF-Datei: " + ex.Message, isError: true);
            return;
        }

        var projector = headers.FirstOrDefault(h => h.Metadata.IsProjector).Path;
        var modelFiles = headers.Where(h => !h.Metadata.IsProjector).Select(h => h.Path).ToList();
        if (modelFiles.Count == 0)
        {
            hub.ShowNotice("Das ist nur ein Bild-Projektor (mmproj). Ziehe die Modell-Datei dazu, beide zusammen.", isError: true);
            return;
        }

        // Mehrere Teile desselben Modells (…-00001-of-00003) zählen als ein Modell.
        var first = modelFiles.Order(StringComparer.OrdinalIgnoreCase).First();
        var parts = ModelImporter.PartsOf(first);
        if (modelFiles.Any(f => !parts.Contains(f, StringComparer.OrdinalIgnoreCase)))
        {
            hub.ShowNotice("Bitte nur ein Modell auf einmal importieren (dazu optional eine mmproj-Datei).", isError: true);
            return;
        }

        var size = parts.Where(File.Exists).Sum(f => new FileInfo(f).Length) + (projector is null ? 0 : new FileInfo(projector).Length);
        var description = Path.GetFileName(first) + (projector is null ? "" : " + " + Path.GetFileName(projector));
        var mode = dialogs.AskCopyOrMove(description, DisplayFormat.Bytes(size));
        if (mode is null)
        {
            return;
        }

        IsImporting = true;
        Progress = 0;
        ProgressText = (mode == ImportMode.Move ? "Verschiebe " : "Kopiere ") + description + " …";
        try
        {
            var progress = new Progress<double>(value =>
            {
                Progress = value;
                ProgressText = $"{(mode == ImportMode.Move ? "Verschiebe" : "Kopiere")} {description}: {value:P0}";
            });
            var model = await Task.Run(() => importer.ImportFileAsync(first, projector, mode == ImportMode.Move,
                "datei:" + first, license: null, progress, CancellationToken.None));

            hub.ShowNotice($"{model.FolderName} ist jetzt ein Grundmodell ({DisplayFormat.Bytes(model.Manifest.SizeBytes)}, {model.Manifest.Quantization}).",
                isError: false, "Ladetest", () => hub.TestLoadAsync(model));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Import von {File} fehlgeschlagen.", first);
            hub.ShowNotice("Import fehlgeschlagen: " + ex.Message, isError: true);
        }
        finally
        {
            IsImporting = false;
            ProgressText = "";
        }
    }

    /// <summary>Ollama-Modell per Hardlink übernehmen, danach Ladetest mit llama.cpp (Bauplan 8.3).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ImportFromOllamaAsync(OllamaImportRow row)
    {
        IsImporting = true;
        ProgressText = $"Übernehme {row.Name} (Hardlink) …";
        LocalModel model;
        try
        {
            model = await Task.Run(() => ollamaImporter.Import(row.Model));
            row.IsImported = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(ex, "Übernehmen von {Name} fehlgeschlagen.", row.Name);
            hub.ShowNotice("Übernehmen fehlgeschlagen: " + ex.Message, isError: true);
            return;
        }
        finally
        {
            IsImporting = false;
            ProgressText = "";
        }

        await hub.TestLoadAsync(model);
    }

    [RelayCommand]
    private void SearchOnHuggingFace(OllamaImportRow row) => hub.SearchHuggingFace(CreateHubViewModel.SearchTermFor(row.Name));
}
