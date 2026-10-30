using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Settings;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Logik des Hauptfensters: Tabs, Kopfleiste und Fenster-Zustand.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;

    public MainViewModel(ISettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        Chat = new ChatTabViewModel(settings.Current.DefaultChatStyle);
        Tabs =
        [
            Chat,
            new PlaceholderTabViewModel("tuning", "Feineinstellungen", Glyphs.Tuning, 6,
                "Temperatur, Kontextlänge, GPU-Layer und mehr. Pro KI einstellbar, mit einer kurzen Erklärung zu jedem Wert."),
            new PlaceholderTabViewModel("training", "Training", Glyphs.Training, 8,
                "Eigene KIs mit LoRA nachtrainieren. Mit 8 GB VRAM geht das für kleine Modelle bis ca. 4B Parameter."),
            new PlaceholderTabViewModel("systemPrompt", "System-Prompt", Glyphs.Edit, 6,
                "Den Systemprompt deiner KIs bearbeiten, testen und alte Fassungen wiederherstellen."),
            new PlaceholderTabViewModel("createHub", "Create-Hub", Glyphs.Library, 3,
                "KIs erstellen und löschen, Modelle importieren (GGUF, Ollama) und auf Hugging Face suchen."),
        ];

        _selectedTab = Tabs.FirstOrDefault(tab => tab.Id == settings.Current.Window.LastTab) ?? Chat;
    }

    public string AppVersion { get; } = AppInfo.Version;

    public ChatTabViewModel Chat { get; }

    public IReadOnlyList<TabViewModel> Tabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabSelected))]
    private TabViewModel _selectedTab;

    /// <summary>Der Stil-Umschalter ist nur im Chat-Tab sichtbar.</summary>
    public bool IsChatTabSelected => SelectedTab == Chat;

    // Kopfleiste: Diese Teile bekommen erst in Meilenstein 1 echte Daten.
    public string KiPlaceholder => "Keine KI";
    public string VramText => "–";
    public string RamText => "–";
    public string NotYetHint => "Kommt in Meilenstein 1";

    [RelayCommand]
    private void OpenSettings() => _dialogs.ShowSettings();

    /// <summary>Für Strg + 1 bis 5. Der Parameter ist die Nummer des Tabs (1 = Chat).</summary>
    [RelayCommand]
    private void SelectTab(string number)
    {
        var index = int.Parse(number, CultureInfo.InvariantCulture) - 1;
        if (index >= 0 && index < Tabs.Count)
        {
            SelectedTab = Tabs[index];
        }
    }

    /// <summary>Speichert Fenstergrösse, Lage und den offenen Tab.</summary>
    public async Task SaveWindowStateAsync(double left, double top, double width, double height, bool isMaximized, CancellationToken ct)
    {
        var settings = SettingsJson.Clone(_settings.Current);
        settings.Window.Left = left;
        settings.Window.Top = top;
        settings.Window.Width = width;
        settings.Window.Height = height;
        settings.Window.IsMaximized = isMaximized;
        settings.Window.LastTab = SelectedTab.Id;

        await _settings.SaveAsync(settings, ct);
    }
}
