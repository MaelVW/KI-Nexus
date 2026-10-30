using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Paths;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Vorläufiges Test-Feld für Meilenstein 1: eine Frage stellen und die gestreamte Antwort sehen.
/// Ohne Verlauf. Der richtige Chat kommt in Meilenstein 2.
/// </summary>
public sealed partial class EngineTestViewModel : ObservableObject
{
    private readonly EngineManager _engines;
    private readonly OllamaModelStore _ollamaStore;
    private readonly OllamaImporter _importer;
    private readonly AppPaths _paths;
    private readonly ILogger<EngineTestViewModel> _logger;
    private OllamaInstalledModel? _importCandidate;

    public EngineTestViewModel(EngineManager engines, OllamaModelStore ollamaStore, OllamaImporter importer, AppPaths paths, ILogger<EngineTestViewModel> logger)
    {
        _engines = engines;
        _ollamaStore = ollamaStore;
        _importer = importer;
        _paths = paths;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private KiProfile? _ki;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _question = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    private string _answer = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThinking))]
    private string _thinking = "";

    [ObservableProperty] private bool _thinkEnabled;
    [ObservableProperty] private bool _showThinking = true;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statsText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _hintText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportFromOllamaCommand))]
    private bool _canImport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFromOllamaCommand))]
    private bool _isImporting;

    public bool HasAnswer => Answer.Length > 0;
    public bool HasThinking => Thinking.Length > 0;
    public bool HasError => ErrorText.Length > 0;
    public bool HasHint => HintText.Length > 0;

    partial void OnKiChanged(KiProfile? value)
    {
        ThinkEnabled = value?.Parameters.ThinkByDefault ?? false;
        _ = CheckModelAsync(value);
    }

    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken ct)
    {
        if (Ki is not { } ki)
        {
            return;
        }

        ResetOutput();
        var answer = new StringBuilder();
        var thinking = new StringBuilder();
        var progress = new Progress<string>(message => StatusText = message);
        try
        {
            await foreach (var chunk in _engines.AskAsync(ki, [], Question, ThinkEnabled, progress, ct))
            {
                StatusText = $"{ki.DisplayName} antwortet …";
                if (chunk.Thinking.Length > 0) { Thinking = thinking.Append(chunk.Thinking).ToString(); }
                if (chunk.Text.Length > 0) { Answer = answer.Append(chunk.Text).ToString(); }
                if (chunk.Stats is { } stats) { StatsText = FormatStats(stats); }
            }

            StatusText = "";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Abgebrochen.";
        }
        catch (InferenceException ex)
        {
            StatusText = "";
            ErrorText = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogError(ex, "Testfrage fehlgeschlagen.");
            StatusText = "";
            ErrorText = $"Fehler: {ex.Message}";
        }
    }

    private bool CanSend() => Ki is not null && !string.IsNullOrWhiteSpace(Question) && !HasHint && !IsImporting;

    /// <summary>Holt das fehlende Modell per Hardlink aus Ollama (ohne doppelten Speicherplatz).</summary>
    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task ImportFromOllamaAsync()
    {
        if (_importCandidate is not { } candidate)
        {
            return;
        }

        IsImporting = true;
        ErrorText = "";
        StatusText = $"{candidate.Name} wird aus Ollama übernommen …";
        try
        {
            var model = await Task.Run(() => _importer.Import(candidate));
            StatusText = $"Übernommen nach models\\{model.FolderName} (Hardlink, braucht keinen zusätzlichen Platz).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            _logger.LogError(ex, "Import aus Ollama fehlgeschlagen.");
            StatusText = "";
            ErrorText = ex.Message;
        }
        finally
        {
            IsImporting = false;
        }

        await CheckModelAsync(Ki);
    }

    private bool CanRunImport() => CanImport && !IsImporting;

    /// <summary>Prüft, ob die KI startklar ist, und zeigt sonst einen Hinweis.</summary>
    private async Task CheckModelAsync(KiProfile? ki)
    {
        HintText = "";
        CanImport = false;
        _importCandidate = null;
        if (ki is null)
        {
            return;
        }

        if (ki.Engine == EngineKind.Ollama)
        {
            var status = await _engines.GetBackend(EngineKind.Ollama).GetStatusAsync(CancellationToken.None);
            if ((status.State is BackendState.NotRunning or BackendState.NotInstalled) && Ki == ki)
            {
                HintText = status.Message;
            }

            return;
        }

        var llama = await _engines.GetBackend(EngineKind.LlamaCpp).GetStatusAsync(CancellationToken.None);
        if (llama.State == BackendState.NotInstalled)
        {
            HintText = llama.Message;
            return;
        }

        if (ki.BaseModel.Path is { Length: > 0 } path && !File.Exists(_paths.Resolve(path)))
        {
            _importCandidate = FindOllamaSource(path);
            CanImport = _importCandidate is not null;
            HintText = CanImport
                ? $"Die Modell-Datei fehlt noch. Ollama hat das passende Modell {_importCandidate!.Name} – du kannst es ohne zusätzlichen Speicherplatz übernehmen."
                : $"Die Modell-Datei fehlt: {path}";
        }
    }

    /// <summary>models/gemma4-26b/… passt zum Ollama-Modell gemma4:26b.</summary>
    private OllamaInstalledModel? FindOllamaSource(string modelPath)
    {
        var folder = modelPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts ? parts[^2] : null;
        return folder is null
            ? null
            : _ollamaStore.List().FirstOrDefault(m => m.Model is not null && OllamaImporter.FolderNameFor(m.Name) == folder);
    }

    private void ResetOutput()
    {
        Answer = "";
        Thinking = "";
        StatsText = "";
        ErrorText = "";
    }

    private static string FormatStats(ChatStats s)
    {
        var engine = s.Engine == EngineKind.Ollama ? "Ollama" : "llama.cpp";
        return string.Create(CultureInfo.InvariantCulture,
            $"{s.CompletionTokens} Token · {s.TokensPerSecond:F1} Token/s · Frage gelesen: {s.PromptTokens} Token ({s.PromptTokensPerSecond:F0} Token/s) · {s.Duration.TotalSeconds:F1} s · {engine}");
    }
}
