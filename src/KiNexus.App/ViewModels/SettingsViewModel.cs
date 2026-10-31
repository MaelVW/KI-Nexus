using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Kis;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using KiNexus.Engines.Voice;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine fertige Akzentfarbe zum Anklicken.</summary>
public sealed record AccentPreset(string Name, string Hex);

/// <summary>Ein Eintrag in der Auswahl "Standard-KI". Leere Id = keine feste Standard-KI.</summary>
public sealed record KiChoice(string Id, string Name);

/// <summary>Ein Eintrag in einer Auswahl: gespeicherter Wert und Anzeige. Leerer Wert = Standard.</summary>
public sealed record SettingChoice(string Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Logik des Einstellungs-Dialogs (Zahnrad).
/// Arbeitet mit einer Kopie. Erst "Speichern" übernimmt die Werte.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs, IShellService shell, AppPaths paths, IKiRegistry kis, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _paths = paths;
        _logger = logger;
        KiChoices = [new KiChoice("", "Erste in der Liste"), .. KiRegistry.SortForDisplay(kis.All, settings.Current.EngineOrder).Select(k => new KiChoice(k.Id, $"{k.DisplayName} ({k.EngineLabel})"))];
        _voice = VoiceInstallation.For(paths, paths.Resolve(settings.Current.Paths.Models));
        VoiceChoices = [.. _voice.Voices().Select(v => new SettingChoice(v, v))];
        WhisperChoices = [.. _voice.WhisperModels().Select(m => new SettingChoice(m, WhisperLabel(m)))];
        LoadFrom(settings.Current);
        VoiceSetupHint = _voice.Problem(settings.Current.Voice) ?? "";
        _ = LoadMicrophonesAsync();
    }

    /// <summary>Wird nach erfolgreichem Speichern ausgelöst. Dann geht der Dialog zu.</summary>
    public event EventHandler? Saved;

    public string VersionText => $"KI-Nexus {AppInfo.Version}";
    public string DataDirectory => _paths.DataDirectory;

    public IReadOnlyList<AccentPreset> AccentPresets { get; } =
    [
        new("Cyan", "#00E5FF"),
        new("Violett", "#B388FF"),
        new("Mint", "#4DFFB8"),
        new("Magenta", "#FF4FD8"),
        new("Orange", "#FFAB40"),
    ];

    public IReadOnlyList<KiChoice> KiChoices { get; }

    // ----------------------------------------------------------- Sprache

    private readonly VoiceInstallation _voice;

    /// <summary>Mikrofone kommen vom Sprach-Worker (dauert ca. 1 s). Leerer Wert = Windows-Standard.</summary>
    public ObservableCollection<SettingChoice> MicrophoneChoices { get; } = [new("", "Windows-Standard")];

    public IReadOnlyList<SettingChoice> VoiceChoices { get; }

    public IReadOnlyList<SettingChoice> WhisperChoices { get; }

    public IReadOnlyList<SettingChoice> PushToTalkKeyChoices { get; } =
    [
        new("Space", "Leertaste"),
        new("RightCtrl", "Rechte Strg-Taste"),
        new("F8", "F8"),
        new("F9", "F9"),
        new("F10", "F10"),
    ];

    public IReadOnlyList<SettingChoice> SilenceChoices { get; } =
    [
        new("500", "0.5 Sekunden (schnell, unterbricht dich eher)"),
        new("800", "0.8 Sekunden (Standard)"),
        new("1200", "1.2 Sekunden"),
        new("2000", "2 Sekunden (für lange Denkpausen)"),
    ];

    [ObservableProperty] private string _microphone = "";
    [ObservableProperty] private string _voiceName = "";
    [ObservableProperty] private string _whisperModel = "";
    [ObservableProperty] private ComputeDevice _speechRecognitionDevice;
    [ObservableProperty] private string _pushToTalkKey = "";
    [ObservableProperty] private string _silenceMs = "";
    [ObservableProperty] private bool _shortSpokenAnswers;

    /// <summary>Was für die Sprache noch fehlt (z.B. "setup-voice.ps1 ausführen"). Leer = alles da.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVoiceSetupHint))]
    private string _voiceSetupHint = "";

    public bool HasVoiceSetupHint => VoiceSetupHint.Length > 0;

    private async Task LoadMicrophonesAsync()
    {
        if (!File.Exists(_voice.PythonExe))
        {
            return;
        }

        try
        {
            var microphones = await VoiceWorker.ListMicrophonesAsync(_voice, CancellationToken.None);
            var selected = Microphone;
            foreach (var name in microphones.Items.Where(n => MicrophoneChoices.All(c => c.Value != n)))
            {
                MicrophoneChoices.Add(new SettingChoice(name, name));
            }

            // Die Auswahl neu setzen, jetzt wo der Eintrag in der Liste steht.
            Microphone = "";
            Microphone = selected;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            _logger.LogWarning(ex, "Mikrofone konnten nicht aufgelistet werden.");
        }
    }

    private static string WhisperLabel(string model) => model switch
    {
        "large-v3-turbo" => "large-v3-turbo (beste Erkennung, ca. 3 s pro Satz auf der CPU)",
        "small" => "small (schneller, ca. 1 s, mehr Fehler)",
        _ => model,
    };

    [ObservableProperty] private string _defaultKiId = "";
    [ObservableProperty] private string _accentColor = "";
    [ObservableProperty] private ChatStyle _defaultChatStyle;
    [ObservableProperty] private EngineOrder _engineOrder;
    [ObservableProperty] private string _modelsPath = "";
    [ObservableProperty] private string _enginesPath = "";
    [ObservableProperty] private string _openWebUiDataPath = "";
    [ObservableProperty] private string _llamaServerPort = "";
    [ObservableProperty] private string _ollamaPort = "";
    [ObservableProperty] private string _openWebUiPort = "";
    [ObservableProperty] private bool _generateChatTitles;
    [ObservableProperty] private bool _stopOpenWebUiOnExit;
    [ObservableProperty] private string _openWebUiVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _errorMessage = "";

    public bool HasError => ErrorMessage.Length > 0;

    // Bei jeder Änderung an einem prüfbaren Feld neu prüfen.
    partial void OnAccentColorChanged(string value) => Validate();
    partial void OnLlamaServerPortChanged(string value) => Validate();
    partial void OnOllamaPortChanged(string value) => Validate();
    partial void OnOpenWebUiPortChanged(string value) => Validate();
    partial void OnModelsPathChanged(string value) => Validate();
    partial void OnEnginesPathChanged(string value) => Validate();
    partial void OnOpenWebUiDataPathChanged(string value) => Validate();

    [RelayCommand]
    private void SelectAccent(string hex) => AccentColor = hex;

    [RelayCommand]
    private void BrowseModels() => ModelsPath = PickFolder("Ordner für Modelle wählen", ModelsPath) ?? ModelsPath;

    [RelayCommand]
    private void BrowseEngines() => EnginesPath = PickFolder("Ordner für Engines wählen", EnginesPath) ?? EnginesPath;

    [RelayCommand]
    private void BrowseOpenWebUiData() => OpenWebUiDataPath = PickFolder("Daten-Ordner von Open WebUI wählen", OpenWebUiDataPath) ?? OpenWebUiDataPath;

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(_paths.DataDirectory);

    [RelayCommand]
    private void ResetToDefaults() => LoadFrom(new AppSettings());

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        var updated = JsonDefaults.Clone(_settings.Current);
        ApplyTo(updated);

        try
        {
            await _settings.SaveAsync(updated, ct);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Einstellungen konnten nicht gespeichert werden.");
            ErrorMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanSave() => !HasError;

    private void LoadFrom(AppSettings settings)
    {
        DefaultKiId = KiChoices.FirstOrDefault(c => c.Id == settings.DefaultKiId)?.Id ?? "";
        AccentColor = settings.AccentColor;
        DefaultChatStyle = settings.DefaultChatStyle;
        EngineOrder = settings.EngineOrder;
        ModelsPath = settings.Paths.Models;
        EnginesPath = settings.Paths.Engines;
        OpenWebUiDataPath = settings.Paths.OpenWebUiData;
        LlamaServerPort = ToText(settings.Ports.LlamaServer);
        OllamaPort = ToText(settings.Ports.Ollama);
        OpenWebUiPort = ToText(settings.Ports.OpenWebUi);
        GenerateChatTitles = settings.BackgroundTasks.GenerateChatTitles;
        StopOpenWebUiOnExit = settings.OpenWebUi.StopOnExit;
        OpenWebUiVersion = settings.OpenWebUi.PinnedVersion ?? "";

        var voice = settings.Voice;
        if (voice.Microphone is { Length: > 0 } mic && MicrophoneChoices.All(c => c.Value != mic))
        {
            // Gespeichertes Mikrofon, das gerade nicht angeschlossen ist: trotzdem anzeigen.
            MicrophoneChoices.Add(new SettingChoice(mic, mic + " (nicht gefunden)"));
        }

        Microphone = voice.Microphone ?? "";
        VoiceName = _voice.ChooseVoice(voice.Voice);
        WhisperModel = voice.WhisperModel;
        SpeechRecognitionDevice = voice.SpeechRecognitionDevice;
        PushToTalkKey = PushToTalkKeyChoices.Any(c => c.Value == voice.PushToTalkKey) ? voice.PushToTalkKey : "Space";
        SilenceMs = SilenceChoices.FirstOrDefault(c => c.Value == ToText(voice.SilenceMs))?.Value ?? "800";
        ShortSpokenAnswers = voice.ShortSpokenAnswers;
    }

    private void ApplyTo(AppSettings settings)
    {
        settings.DefaultKiId = DefaultKiId.Length == 0 ? null : DefaultKiId;
        settings.AccentColor = AccentColor.Trim();
        settings.DefaultChatStyle = DefaultChatStyle;
        settings.EngineOrder = EngineOrder;
        settings.Paths.Models = ModelsPath.Trim();
        settings.Paths.Engines = EnginesPath.Trim();
        settings.Paths.OpenWebUiData = OpenWebUiDataPath.Trim();
        settings.Ports.LlamaServer = int.Parse(LlamaServerPort, CultureInfo.InvariantCulture);
        settings.Ports.Ollama = int.Parse(OllamaPort, CultureInfo.InvariantCulture);
        settings.Ports.OpenWebUi = int.Parse(OpenWebUiPort, CultureInfo.InvariantCulture);
        settings.BackgroundTasks.GenerateChatTitles = GenerateChatTitles;
        settings.OpenWebUi.StopOnExit = StopOpenWebUiOnExit;
        settings.OpenWebUi.PinnedVersion = string.IsNullOrWhiteSpace(OpenWebUiVersion) ? null : OpenWebUiVersion.Trim();

        settings.Voice.Microphone = Microphone.Length == 0 ? null : Microphone;
        settings.Voice.Voice = VoiceName.Length == 0 ? null : VoiceName;
        settings.Voice.WhisperModel = WhisperModel.Length == 0 ? new VoiceSettings().WhisperModel : WhisperModel;
        settings.Voice.SpeechRecognitionDevice = SpeechRecognitionDevice;
        settings.Voice.PushToTalkKey = PushToTalkKey;
        settings.Voice.SilenceMs = int.Parse(SilenceMs, CultureInfo.InvariantCulture);
        settings.Voice.ShortSpokenAnswers = ShortSpokenAnswers;
    }

    private void Validate()
    {
        ErrorMessage = FindFirstError() ?? "";
    }

    private string? FindFirstError()
    {
        if (!HexColor.TryParse(AccentColor, out _))
        {
            return "Die Akzentfarbe muss so aussehen: #00E5FF";
        }

        foreach (var (name, text) in new[] { ("llama-server", LlamaServerPort), ("Ollama", OllamaPort), ("Open WebUI", OpenWebUiPort) })
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || !SettingsValidator.IsValidPort(port))
            {
                return $"Der Port für {name} muss eine Zahl von {SettingsValidator.MinPort} bis {SettingsValidator.MaxPort} sein.";
            }
        }

        if (string.IsNullOrWhiteSpace(ModelsPath) || string.IsNullOrWhiteSpace(EnginesPath) || string.IsNullOrWhiteSpace(OpenWebUiDataPath))
        {
            return "Kein Pfad darf leer sein.";
        }

        return null;
    }

    private string? PickFolder(string title, string currentPath)
    {
        var picked = _dialogs.PickFolder(title, _paths.Resolve(currentPath));
        return picked is null ? null : MakeRelativeIfInsideRoot(picked);
    }

    /// <summary>Pfade im Projektordner werden relativ gespeichert. So kann man den Ordner verschieben.</summary>
    private string MakeRelativeIfInsideRoot(string fullPath)
    {
        var relative = Path.GetRelativePath(_paths.Root, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative;
    }

    private static string ToText(int number) => number.ToString(CultureInfo.InvariantCulture);
}
