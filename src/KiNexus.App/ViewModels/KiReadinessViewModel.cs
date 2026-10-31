using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Prüft, ob die gewählte KI startklar ist, und zeigt sonst einen Hinweis-Balken über dem Chat
/// (z.B. "Modell-Datei fehlt" mit Knopf "Aus Ollama übernehmen" oder "Ollama läuft nicht").
/// </summary>
public sealed partial class KiReadinessViewModel(
    EngineManager engines,
    OllamaModelStore ollamaStore,
    OllamaImporter importer,
    ModelLibrary library,
    AppPaths paths,
    ILogger<KiReadinessViewModel> logger) : ObservableObject
{
    private OllamaInstalledModel? _importCandidate;
    private KiProfile? _ki;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hintText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportFromOllamaCommand))]
    private bool _canImport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportFromOllamaCommand))]
    private bool _isImporting;

    [ObservableProperty] private string _statusText = "";

    public bool HasHint => HintText.Length > 0;

    /// <summary>Wird ausgelöst, wenn sich ändert, ob die KI startklar ist.</summary>
    public event EventHandler? Changed;

    public async Task CheckAsync(KiProfile? ki)
    {
        _ki = ki;
        HintText = "";
        CanImport = false;
        _importCandidate = null;
        if (ki is not null)
        {
            HintText = await FindProblemAsync(ki) ?? "";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Holt das fehlende Modell per Hardlink aus Ollama (ohne doppelten Speicherplatz).</summary>
    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task ImportFromOllamaAsync()
    {
        if (_importCandidate is not { } candidate)
        {
            return;
        }

        IsImporting = true;
        StatusText = $"{candidate.Name} wird aus Ollama übernommen …";
        try
        {
            var model = await Task.Run(() => importer.Import(candidate));
            StatusText = $"Übernommen nach models\\{model.FolderName} (Hardlink, braucht keinen zusätzlichen Platz).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            logger.LogError(ex, "Import aus Ollama fehlgeschlagen.");
            StatusText = ex.Message;
        }
        finally
        {
            IsImporting = false;
        }

        await CheckAsync(_ki);
    }

    private bool CanRunImport() => CanImport && !IsImporting;

    private async Task<string?> FindProblemAsync(KiProfile ki)
    {
        var status = await engines.GetBackend(ki.Engine).GetStatusAsync(CancellationToken.None);
        if (ki.Engine == EngineKind.Ollama)
        {
            return status.State is BackendState.NotRunning or BackendState.NotInstalled ? status.Message : null;
        }

        if (status.State == BackendState.NotInstalled)
        {
            return status.Message;
        }

        if (ki.BaseModel.Path is { Length: > 0 } path && !File.Exists(paths.Resolve(path)))
        {
            _importCandidate = FindOllamaSource(path);
            CanImport = _importCandidate is not null;
            return CanImport
                ? $"Die Modell-Datei fehlt noch. Ollama hat das passende Modell {_importCandidate!.Name} – du kannst es ohne zusätzlichen Speicherplatz übernehmen."
                : $"Die Modell-Datei fehlt: {path}";
        }

        // Ein Bild- oder Video-Modell bricht beim Laden ab. Das sagen wir vorher.
        if (ki.BaseModel.Path is { Length: > 0 } modelPath
            && library.Scan().FirstOrDefault(m => string.Equals(Path.GetFullPath(m.ModelPath), Path.GetFullPath(paths.Resolve(modelPath)),
                StringComparison.OrdinalIgnoreCase)) is { IsLanguageModel: false } model)
        {
            return $"Das Grundmodell {model.FolderName} ist kein Sprachmodell, sondern {ModelKinds.NonTextKind(model.Manifest.Architecture)} "
                + $"(Architektur {model.Manifest.Architecture}). Damit kann diese KI nicht antworten. Erstelle im Create-Hub eine KI mit einem Sprachmodell.";
        }

        return null;
    }

    /// <summary>models/gemma4-26b/… passt zum Ollama-Modell gemma4:26b.</summary>
    private OllamaInstalledModel? FindOllamaSource(string modelPath)
    {
        var parts = modelPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folder = parts.Length >= 2 ? parts[^2] : null;
        return folder is null
            ? null
            : ollamaStore.List().FirstOrDefault(m => m.Model is not null && OllamaImporter.FolderNameFor(m.Name) == folder);
    }
}
