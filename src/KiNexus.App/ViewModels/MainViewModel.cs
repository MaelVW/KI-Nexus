using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Resources;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Logik des Hauptfensters: Tabs, Kopfleiste (KI-Auswahl, Ressourcen, Entladen) und Fenster-Zustand.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IChatShell
{
    private static readonly TimeSpan ResourceInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IKiRegistry _kis;
    private readonly EngineManager _engines;
    private readonly IResourceMonitor _resources;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _shutdown = new();

    public MainViewModel(
        ISettingsService settings,
        IDialogService dialogs,
        IKiRegistry kis,
        EngineManager engines,
        IResourceMonitor resources,
        ChatSessionViewModel session,
        ChatListViewModel chatList,
        CreateHubViewModel createHub,
        TuningViewModel tuning,
        TrainingViewModel training,
        SystemPromptViewModel systemPrompt,
        ConversationViewModel conversation,
        OpenWebUiViewModel openWebUi,
        ILogger<MainViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _kis = kis;
        OpenWebUi = openWebUi;
        _engines = engines;
        _resources = resources;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        Chat = new ChatTabViewModel(settings.Current.DefaultChatStyle, session, chatList, new TerminalViewModel(session, this), conversation);
        Tabs =
        [
            Chat,
            tuning,
            training,
            systemPrompt,
            createHub,
        ];
        createHub.Shell = this;
        training.Shell = this;
        Training = training;
        _selectedTab = Tabs.FirstOrDefault(tab => tab.Id == settings.Current.Window.LastTab) ?? Chat;
        if (_selectedTab == createHub)
        {
            _ = createHub.RefreshPageAsync();
        }

        Kis = new ObservableCollection<KiProfile>(KiRegistry.SortForDisplay(kis.All, settings.Current.EngineOrder));
        SelectedKi = kis.Find(settings.Current.DefaultKiId) ?? Kis.FirstOrDefault();
        (_selectedTab as KiEditorTabViewModel)?.Activate(SelectedKi);
        if (_selectedTab == training)
        {
            _ = training.ActivateAsync();
        }

        // Öffnet man einen alten Chat, wechselt die Kopfleiste zur KI dieses Chats.
        session.ChatOpened += (_, _) =>
        {
            if (session.Chat?.KiId is { } kiId && Kis.FirstOrDefault(k => k.Id == kiId) is { } chatKi)
            {
                SelectedKi = chatKi;
            }
        };

        // Neue oder gelöschte KIs (Create-Hub) sofort in der Auswahl zeigen.
        kis.Changed += (_, _) => _ui.Post(_ => RefreshKis(), null);

        _engines.StateChanged += (_, _) => _ui.Post(_ => UpdateEngineState(), null);
        UpdateEngineState();
        _ = RunResourceLoopAsync(_shutdown.Token);
        _ = chatList.InitializeAsync();
    }

    public string AppVersion { get; } = AppInfo.Version;

    public ChatTabViewModel Chat { get; }

    public TrainingViewModel Training { get; }

    /// <summary>Knopf "Alternate Launch" (Open WebUI) in der Kopfleiste.</summary>
    public OpenWebUiViewModel OpenWebUi { get; }

    public IReadOnlyList<TabViewModel> Tabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabSelected))]
    private TabViewModel _selectedTab;

    /// <summary>Der Stil-Umschalter ist nur im Chat-Tab sichtbar.</summary>
    public bool IsChatTabSelected => SelectedTab == Chat;

    // ----------------------------------------------------------- KI-Auswahl und Engine-Status

    public ObservableCollection<KiProfile> Kis { get; }

    [ObservableProperty] private KiProfile? _selectedKi;

    [ObservableProperty] private EngineManagerState _engineState;
    [ObservableProperty] private string _engineStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnloadCommand))]
    private bool _isModelLoaded;

    IReadOnlyList<KiProfile> IChatShell.Kis => Kis;

    partial void OnSelectedKiChanged(KiProfile? value) => Chat.Session.Ki = value;

    public void SetStyle(ChatStyle style) => Chat.Style = style;

    public void OpenChatWith(KiProfile ki)
    {
        SelectedKi = Kis.FirstOrDefault(k => k.Id == ki.Id) ?? SelectedKi;
        SelectedTab = Chat;
        if (!Chat.Session.IsBusy)
        {
            Chat.Session.StartNewChat();
        }
    }

    /// <summary>Liste neu aufbauen. Die gewählte KI bleibt gewählt, wenn es sie noch gibt.</summary>
    private void RefreshKis()
    {
        var selectedId = SelectedKi?.Id;
        var sorted = KiRegistry.SortForDisplay(_kis.All, _settings.Current.EngineOrder);
        Kis.Clear();
        foreach (var ki in sorted)
        {
            Kis.Add(ki);
        }

        SelectedKi = Kis.FirstOrDefault(k => k.Id == selectedId)
            ?? Kis.FirstOrDefault(k => k.Id == _settings.Current.DefaultKiId)
            ?? Kis.FirstOrDefault();
    }

    partial void OnSelectedTabChanged(TabViewModel value)
    {
        if (value is CreateHubViewModel hub)
        {
            _ = hub.RefreshPageAsync();
        }

        // Feineinstellungen und System-Prompt öffnen mit der KI aus der Kopfleiste.
        (value as KiEditorTabViewModel)?.Activate(SelectedKi);

        if (value is TrainingViewModel training)
        {
            _ = training.ActivateAsync();
        }
    }

    // ----------------------------------------------------------- Ressourcen

    [ObservableProperty] private string _vramText = "–";
    [ObservableProperty] private string _ramText = "–";
    [ObservableProperty] private bool _isVramHigh;
    [ObservableProperty] private bool _isRamHigh;

    // ----------------------------------------------------------- Befehle

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

    /// <summary>Knopf "Entladen": Gibt VRAM und RAM sofort frei, z.B. vor dem Gamen (Lektion 8). Auch die Sprache (ca. 2 GB RAM).</summary>
    [RelayCommand(CanExecute = nameof(IsModelLoaded))]
    public async Task UnloadAsync()
    {
        try
        {
            await Chat.Conversation.ShutdownAsync();
            await _engines.UnloadAsync(CancellationToken.None);
        }
        catch (InferenceException ex)
        {
            _logger.LogError(ex, "Entladen fehlgeschlagen.");
            EngineStatus = ex.Message;
        }
    }

    /// <summary>Speichert Fenstergrösse, Lage und den offenen Tab.</summary>
    public async Task SaveWindowStateAsync(double left, double top, double width, double height, bool isMaximized, CancellationToken ct)
    {
        var settings = JsonDefaults.Clone(_settings.Current);
        settings.Window.Left = left;
        settings.Window.Top = top;
        settings.Window.Width = width;
        settings.Window.Height = height;
        settings.Window.IsMaximized = isMaximized;
        settings.Window.LastTab = SelectedTab.Id;

        await _settings.SaveAsync(settings, ct);
    }

    /// <summary>Beim Schliessen: Messung stoppen und das Modell entladen (gibt auch Ollamas VRAM frei).</summary>
    public async Task ShutdownAsync()
    {
        await _shutdown.CancelAsync();
        await Chat.Conversation.DisposeAsync();
        await OpenWebUi.DisposeAsync();
        await Training.DisposeAsync();
        await _engines.DisposeAsync();
    }

    private void UpdateEngineState()
    {
        EngineState = _engines.State;
        EngineStatus = _engines.StatusMessage;
        IsModelLoaded = _engines.LoadedKi is not null;
    }

    /// <summary>Misst alle 2 Sekunden VRAM und RAM. Läuft, bis die App schliesst.</summary>
    private async Task RunResourceLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ResourceInterval);
        try
        {
            do
            {
                var snapshot = await Task.Run(_resources.Read, ct);
                VramText = snapshot.VramText;
                RamText = snapshot.RamText;
                IsVramHigh = snapshot.IsVramHigh;
                IsRamHigh = snapshot.IsRamHigh;
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // App wird geschlossen.
        }
    }
}
