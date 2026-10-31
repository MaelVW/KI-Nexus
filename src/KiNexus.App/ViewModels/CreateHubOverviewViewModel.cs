using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine Zeile in "Meine KIs".</summary>
public sealed record KiRow(KiProfile Ki, string BaseModelText, string ImagesText, FitEstimate? Fit)
{
    public string Name => Ki.DisplayName;
    public string EngineText => Ki.EngineLabel;
    public string Description => Ki.Description;
}

/// <summary>Eine Zeile in "Grundmodelle".</summary>
public sealed record ModelRow(LocalModel Model, FitEstimate Fit, IReadOnlyList<KiProfile> UsedBy)
{
    public string Name => Model.FolderName;
    public string SizeText => DisplayFormat.Bytes(Model.Manifest.SizeBytes);
    public string QuantText => Model.Manifest.Quantization ?? "–";
    public string ImagesText => Model.SupportsImages ? "ja" : "nein";
    public string LicenseText => string.IsNullOrWhiteSpace(Model.Manifest.License) ? "unbekannt" : Model.Manifest.License!;
    public string SourceText => Model.Manifest.Source.Split(':')[0] switch
    {
        "ollama" => "aus Ollama (Hardlink)",
        "huggingface" => "von Hugging Face",
        "datei" => "aus Datei",
        _ => Model.Manifest.Source,
    };

    public string UsedByText => UsedBy.Count == 0 ? "von keiner KI genutzt" : "genutzt von " + string.Join(", ", UsedBy.Select(k => k.DisplayName));

    /// <summary>Rote Zeile bei Bild- oder Video-Modellen, z.B. "Kein Sprachmodell: ein Videomodell (ltxv)". Sonst leer.</summary>
    public string KindWarning => Model.IsLanguageModel
        ? ""
        : $"Kein Sprachmodell: {ModelKinds.NonTextKind(Model.Manifest.Architecture)} ({Model.Manifest.Architecture}). Zum Chatten nicht nutzbar.";

    public bool HasKindWarning => KindWarning.Length > 0;
}

/// <summary>Eine Zeile in "Ollama-Modelle".</summary>
public sealed record OllamaRow(string Name, long SizeBytes, FitEstimate Fit)
{
    public string SizeText => DisplayFormat.Bytes(SizeBytes);
}

/// <summary>Übersicht im Create-Hub: Meine KIs, Grundmodelle, Ollama-Modelle, jeweils mit Ampel und Löschen.</summary>
public sealed partial class CreateHubOverviewViewModel(
    CreateHubViewModel hub,
    IKiRegistry kis,
    ModelLibrary library,
    EngineManager engines,
    IDialogService dialogs,
    IShellService shell,
    OllamaBackend ollama,
    ILogger<CreateHubOverviewViewModel> logger) : ObservableObject
{
    public ObservableCollection<KiRow> KiRows { get; } = [];
    public ObservableCollection<ModelRow> ModelRows { get; } = [];
    public ObservableCollection<OllamaRow> OllamaRows { get; } = [];

    [ObservableProperty] private bool _isOllamaRunning;
    [ObservableProperty] private string _ollamaStatus = "";
    [ObservableProperty] private bool _isRefreshing;

    public bool HasNoKis => KiRows.Count == 0;
    public bool HasNoModels => ModelRows.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var models = await Task.Run(library.Scan);
            ModelRows.Clear();
            foreach (var model in models)
            {
                ModelRows.Add(new ModelRow(model, hub.Estimate(model), hub.KisUsing(model)));
            }

            KiRows.Clear();
            foreach (var ki in kis.All.OrderBy(k => k.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                KiRows.Add(CreateKiRow(ki, models));
            }

            OnPropertyChanged(nameof(HasNoKis));
            OnPropertyChanged(nameof(HasNoModels));
            await RefreshOllamaAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private KiRow CreateKiRow(KiProfile ki, IReadOnlyList<LocalModel> models)
    {
        if (ki.Engine == EngineKind.Ollama)
        {
            return new KiRow(ki, ki.BaseModel.OllamaName ?? "–", "–", null);
        }

        var model = models.FirstOrDefault(m => hub.KisUsing(m).Any(k => k.Id == ki.Id));
        return model is null
            ? new KiRow(ki, "Modell fehlt", ki.SupportsImages ? "ja" : "nein", null)
            : new KiRow(ki, model.FolderName, ki.SupportsImages ? "ja" : "nein", hub.Estimate(model, ki.Parameters.ContextLength));
    }

    private async Task RefreshOllamaAsync()
    {
        OllamaRows.Clear();
        var status = await ollama.GetStatusAsync(CancellationToken.None);
        IsOllamaRunning = status.State is not (BackendState.NotRunning or BackendState.NotInstalled);
        OllamaStatus = IsOllamaRunning ? status.Message : status.Message + " Die Liste erscheint, sobald Ollama läuft.";
        if (!IsOllamaRunning)
        {
            return;
        }

        try
        {
            foreach (var model in await ollama.ListModelsAsync(CancellationToken.None))
            {
                OllamaRows.Add(new OllamaRow(model.Name, model.SizeBytes, hub.Estimate(model.SizeBytes, 0, 8192)));
            }
        }
        catch (InferenceException ex)
        {
            OllamaStatus = ex.Message;
        }
    }

    // ----------------------------------------------------------- KIs

    [RelayCommand]
    private void ChatWith(KiRow row) => hub.Shell?.OpenChatWith(row.Ki);

    [RelayCommand]
    private async Task DeleteKiAsync(KiRow row)
    {
        if (!dialogs.Confirm("KI löschen?",
                $"\"{row.Name}\" wird gelöscht (die Datei kis/{row.Ki.Id}.json). Das Grundmodell und deine Chats bleiben erhalten.",
                "Löschen"))
        {
            return;
        }

        try
        {
            if (engines.LoadedKi?.Id == row.Ki.Id)
            {
                await engines.UnloadAsync(CancellationToken.None);
            }

            await kis.DeleteAsync(row.Ki.Id, CancellationToken.None);
            hub.ShowNotice($"\"{row.Name}\" wurde gelöscht.", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InferenceException)
        {
            logger.LogError(ex, "KI {Id} löschen fehlgeschlagen.", row.Ki.Id);
            hub.ShowNotice("Löschen hat nicht geklappt: " + ex.Message, isError: true);
        }

        await RefreshAsync();
    }

    // ----------------------------------------------------------- Grundmodelle

    [RelayCommand]
    private Task TestLoadAsync(ModelRow row) => RefuseNonText(row) ? Task.CompletedTask : hub.TestLoadAsync(row.Model);

    [RelayCommand]
    private Task NewKiFrom(ModelRow row) => RefuseNonText(row) ? Task.CompletedTask : hub.StartWizardAsync(row.Model);

    /// <summary>Bild- und Video-Modelle kann llama.cpp nicht laden. Dann nur eine Meldung zeigen.</summary>
    private bool RefuseNonText(ModelRow row)
    {
        if (row.Model.IsLanguageModel)
        {
            return false;
        }

        hub.ShowNotice($"{row.Name}: {ModelKinds.Explain(row.Model.Manifest.Architecture)}", isError: true);
        return true;
    }

    [RelayCommand]
    private void OpenFolder(ModelRow row) => shell.OpenFolder(row.Model.FolderPath);

    [RelayCommand]
    private async Task DeleteModelAsync(ModelRow row)
    {
        var warning = row.UsedBy.Count == 0
            ? ""
            : $"\n\nAchtung: Diese KIs nutzen das Modell und funktionieren danach nicht mehr: {string.Join(", ", row.UsedBy.Select(k => k.DisplayName))}.";
        var hardlink = row.Model.Manifest.Source.StartsWith("ollama:", StringComparison.Ordinal)
            ? "\n\nDas Modell ist ein Hardlink auf Ollama. Ollama behält seine Datei, es wird kein Platz frei."
            : $"\n\nDas gibt {row.SizeText} frei.";
        if (!dialogs.Confirm("Grundmodell löschen?", $"Der Ordner models/{row.Name} wird gelöscht.{hardlink}{warning}", "Löschen"))
        {
            return;
        }

        try
        {
            // Eine geladene Datei kann Windows nicht löschen. Darum zuerst entladen.
            if (engines.LoadedKi is { Engine: EngineKind.LlamaCpp })
            {
                await engines.UnloadAsync(CancellationToken.None);
            }

            await Task.Run(() => library.Delete(row.Model));
            hub.ShowNotice($"Grundmodell {row.Name} wurde gelöscht.", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InferenceException)
        {
            logger.LogError(ex, "Grundmodell {Folder} löschen fehlgeschlagen.", row.Name);
            hub.ShowNotice("Löschen hat nicht geklappt: " + ex.Message, isError: true);
        }

        await RefreshAsync();
    }

    // ----------------------------------------------------------- Ollama

    [RelayCommand]
    private Task NewKiFromOllama(OllamaRow row) => hub.StartWizardAsync(ollamaName: row.Name);

    [RelayCommand]
    private async Task DeleteOllamaModelAsync(OllamaRow row)
    {
        var users = kis.All.Where(k => k.Engine == EngineKind.Ollama && string.Equals(k.BaseModel.OllamaName, row.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var warning = users.Count == 0 ? "" : $"\n\nDiese KIs nutzen das Modell: {string.Join(", ", users.Select(k => k.DisplayName))}.";
        if (!dialogs.Confirm("Ollama-Modell löschen?",
                $"{row.Name} wird über die Ollama-API gelöscht ({row.SizeText}). Es fehlt danach auch in Open WebUI und in allen anderen Programmen, die Ollama nutzen."
                + "\n\nHast du es nach models/ übernommen, bleibt die Datei dort erhalten (Hardlink)." + warning,
                "In Ollama löschen"))
        {
            return;
        }

        try
        {
            if (engines.LoadedKi is { Engine: EngineKind.Ollama } loaded && string.Equals(loaded.BaseModel.OllamaName, row.Name, StringComparison.OrdinalIgnoreCase))
            {
                await engines.UnloadAsync(CancellationToken.None);
            }

            await ollama.DeleteModelAsync(row.Name, CancellationToken.None);
            hub.ShowNotice($"{row.Name} wurde in Ollama gelöscht.", isError: false);
        }
        catch (InferenceException ex)
        {
            hub.ShowNotice(ex.Message, isError: true);
        }

        await RefreshAsync();
    }
}
