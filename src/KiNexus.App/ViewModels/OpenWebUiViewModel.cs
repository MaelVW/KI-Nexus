using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Inference;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using KiNexus.Engines.OpenWebUi;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Knopf "Alternate Launch" in der Kopfleiste (Bauplan 8.7): Open WebUI starten, im Browser öffnen, stoppen.
/// Vor dem Start prüft er: Ist uvx da? Ist der Port frei? Läuft Ollama oder llama-server?
/// </summary>
public sealed partial class OpenWebUiViewModel : ObservableObject, IAsyncDisposable
{
    private readonly OpenWebUiLauncher _launcher;
    private readonly ISettingsService _settings;
    private readonly AppPaths _paths;
    private readonly IShellService _shell;
    private readonly EngineManager _engines;
    private readonly OllamaBackend _ollama;
    private readonly ILogger<OpenWebUiViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private bool _logUpdatePending;

    public OpenWebUiViewModel(
        OpenWebUiLauncher launcher, ISettingsService settings, AppPaths paths, IShellService shell,
        EngineManager engines, OllamaBackend ollama, ILogger<OpenWebUiViewModel> logger)
    {
        _launcher = launcher;
        _settings = settings;
        _paths = paths;
        _shell = shell;
        _engines = engines;
        _ollama = ollama;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _launcher.Changed += (_, _) => _ui.Post(_ => Update(), null);
        _launcher.LogChanged += (_, _) => ScheduleLogUpdate();
        Update();
        _ = _launcher.DetectAsync(settings.Current.Ports.OpenWebUi, CancellationToken.None);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsStarting), nameof(IsFailed), nameof(ToolTipText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(OpenBrowserCommand))]
    private OpenWebUiState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _statusText = "";

    /// <summary>Hinweis aus der Prüfung vor dem Start (z.B. wie Open WebUI zu llama-server kommt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hint = "";

    [ObservableProperty] private string _logText = "";

    /// <summary>Das Panel unter dem Knopf ist offen.</summary>
    [ObservableProperty] private bool _isPanelOpen;

    [ObservableProperty] private bool _isLogVisible;

    public bool IsRunning => State == OpenWebUiState.Running;
    public bool IsStarting => State is OpenWebUiState.Starting or OpenWebUiState.Stopping;
    public bool IsFailed => State == OpenWebUiState.Failed;
    public bool HasHint => Hint.Length > 0;

    public string AddressText => _launcher.Address.ToString();

    public string ToolTipText => $"Alternate Launch: Open WebUI. {StatusText}";

    /// <summary>Die Verbindung zu llama-server, wie sie in Open WebUI eingetragen wird.</summary>
    public string LlamaServerUrl => OpenWebUiCommand.LlamaServerUrl(_settings.Current.Ports.LlamaServer);

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var settings = _settings.Current;
        if (UvxLocator.Find() is not { } uvx)
        {
            Hint = "uvx fehlt. Open WebUI startet über uv: https://docs.astral.sh/uv/ installieren, dann nochmals versuchen.";
            return;
        }

        Hint = await CheckModelSourcesAsync();
        var options = new OpenWebUiOptions(
            uvx,
            _paths.Resolve(Environment.ExpandEnvironmentVariables(settings.Paths.OpenWebUiData)),
            settings.Ports.OpenWebUi,
            settings.Ports.Ollama,
            settings.Ports.LlamaServer,
            settings.OpenWebUi.PinnedVersion,
            settings.OpenWebUi.StopOnExit);

        try
        {
            await _launcher.StartAsync(options, CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogError(ex, "Open WebUI konnte nicht starten.");
            Hint = "Open WebUI konnte nicht starten: " + ex.Message;
            return;
        }

        if (_launcher.State == OpenWebUiState.Running)
        {
            OpenBrowser();
        }
        else if (_launcher.State == OpenWebUiState.Failed)
        {
            IsLogVisible = true;
        }
    }

    private bool CanStart() => State is OpenWebUiState.Stopped or OpenWebUiState.Failed;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => _launcher.StopAsync();

    private bool CanStop() => State is OpenWebUiState.Starting or OpenWebUiState.Running;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void OpenBrowser() => _shell.OpenUrl(_launcher.Address);

    [RelayCommand]
    private void CopyLlamaServerUrl() => _shell.CopyText(LlamaServerUrl);

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    /// <summary>Beim Schliessen von KI-Nexus: stoppen, wenn eingestellt ("Beim Schliessen beenden").</summary>
    public ValueTask DisposeAsync() => _launcher.DisposeAsync();

    /// <summary>
    /// Woher bekommt Open WebUI Modelle? Ollama ist der Normalfall. Sonst llama-server, das braucht einmal eine Verbindung.
    /// </summary>
    private async Task<string> CheckModelSourcesAsync()
    {
        var ollama = await _ollama.GetStatusAsync(CancellationToken.None);
        var ollamaRunning = ollama.State is not (BackendState.NotRunning or BackendState.NotInstalled);
        var llamaRunning = _engines.LoadedKi is { Engine: EngineKind.LlamaCpp };
        return (ollamaRunning, llamaRunning) switch
        {
            (true, _) => "",
            (false, true) => "Ollama läuft nicht. Damit Open WebUI die KI von KI-Nexus (llama-server) sieht, trag einmal diese Verbindung ein: "
                + $"Open WebUI → Admin-Bereich → Einstellungen → Verbindungen → OpenAI-API → +. URL: {LlamaServerUrl}, Schlüssel: none. "
                + "llama-server läuft nur, solange in KI-Nexus eine llama.cpp-KI geladen ist.",
            _ => "Weder Ollama noch llama-server laufen. Open WebUI startet trotzdem, zeigt aber keine Modelle. "
                + "Starte die Ollama-App, oder stell in KI-Nexus einer llama.cpp-KI eine Frage (dann läuft llama-server).",
        };
    }

    private void Update()
    {
        State = _launcher.State;
        StatusText = _launcher.StatusText;
        OnPropertyChanged(nameof(AddressText));
    }

    /// <summary>Open WebUI schreibt beim Start viel. Die Anzeige höchstens alle 300 ms neu aufbauen.</summary>
    private void ScheduleLogUpdate()
    {
        if (_logUpdatePending)
        {
            return;
        }

        _logUpdatePending = true;
        _ = Task.Delay(300).ContinueWith(_ => _ui.Post(_ =>
        {
            _logUpdatePending = false;
            LogText = string.Join(Environment.NewLine, _launcher.LogLines.TakeLast(200));
        }, null), TaskScheduler.Default);
    }
}
