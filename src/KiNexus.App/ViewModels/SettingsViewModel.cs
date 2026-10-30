using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine fertige Akzentfarbe zum Anklicken.</summary>
public sealed record AccentPreset(string Name, string Hex);

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

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs, IShellService shell, AppPaths paths, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _paths = paths;
        _logger = logger;
        LoadFrom(settings.Current);
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
        var updated = SettingsJson.Clone(_settings.Current);
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
    }

    private void ApplyTo(AppSettings settings)
    {
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
