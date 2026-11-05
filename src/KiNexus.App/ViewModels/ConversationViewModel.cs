using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Kis;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using KiNexus.Core.Voice;
using KiNexus.Engines.Voice;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Was der Orb zeigt.</summary>
public enum ConversationState
{
    /// <summary>Sprache ist noch nicht gestartet (Whisper und Piper nicht geladen).</summary>
    Off,

    /// <summary>Whisper und Piper werden geladen.</summary>
    Starting,

    /// <summary>Bereit, aber das Mikrofon ist zu (Push-to-Talk ohne gedrückte Taste, oder Freihändig pausiert).</summary>
    Ready,

    /// <summary>Das Mikrofon ist offen.</summary>
    Listening,

    /// <summary>Freihändig: Du sprichst gerade.</summary>
    Hearing,

    /// <summary>Whisper schreibt auf, was du gesagt hast.</summary>
    Understanding,

    /// <summary>Die KI überlegt (oder wird geladen).</summary>
    Thinking,

    /// <summary>Die KI spricht.</summary>
    Speaking,

    Error,
}

/// <summary>
/// Der Gespräch-Stil (Bauplan 8.1): Orb in der Mitte, Mitschrift darunter, Push-to-Talk oder Freihändig.
/// Ablauf: Mikrofon → Whisper (Mitschrift) → Frage an die KI ohne Thinking → Antwort satzweise an Piper → Lautsprecher.
/// Freihändig hört das Mikrofon nicht zu, solange die KI spricht (Unterbrechen per Stimme kommt in M7).
/// </summary>
public sealed partial class ConversationViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>
    /// So lange muss der Lautsprecher-Puffer leer sein, bis die KI als "fertig gesprochen" gilt.
    /// Windows hält noch ca. 0.3 s Ton, Bluetooth-Kopfhörer spielen zusätzlich ca. 0.2–0.3 s verzögert.
    /// Hört das Mikrofon zu früh wieder zu, nimmt es das Ende der eigenen Antwort auf.
    /// </summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromMilliseconds(800);

    private readonly VoiceWorker _worker;
    private readonly PcmPlayer _player;
    private readonly ISettingsService _settings;
    private readonly AppPaths _paths;
    private readonly IKiRegistry _kis;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ConversationViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _timer;
    private readonly SentenceSplitter _splitter = new();
    private readonly HashSet<int> _pendingSpeech = [];

    private MessageViewModel? _answer;
    private int _spokenLength;
    private int _nextSpeechId = 1;
    private int _firstSpeechIdOfTurn = 1;
    private bool _answerComplete;
    private bool _interrupted;
    private DateTime _drainedSince = DateTime.MaxValue;
    private string _voiceSignature;

    /// <summary>Der Modus wird gerade aus dem Chat oder der KI übernommen (nicht vom Menschen umgeschaltet).</summary>
    private bool _applyingMode;

    /// <summary>Stimme und Tempo, die der Worker gerade geladen hat, z.B. "de_DE-ramona-low|1.1".</summary>
    private string? _loadedVoice;

    /// <summary>Was die KI in dieser Antwort schon gesagt hat. Damit erkennt die App ihr eigenes Echo.</summary>
    private readonly List<string> _spokenThisTurn = [];

    /// <summary>Die nächste Mitschrift kommt von einem Unterbrechen.</summary>
    private bool _bargedIn;

    public ConversationViewModel(
        ChatSessionViewModel session, VoiceWorker worker, PcmPlayer player, ISettingsService settings, AppPaths paths,
        IKiRegistry kis, IDialogService dialogs, ILogger<ConversationViewModel> logger)
    {
        Session = session;
        _kis = kis;
        _dialogs = dialogs;
        _worker = worker;
        _player = player;
        _settings = settings;
        _paths = paths;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => OnTick();

        _worker.EventReceived += voiceEvent => _ui.Post(_ => OnVoiceEvent(voiceEvent), null);
        Session.Messages.CollectionChanged += OnMessagesChanged;
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatSessionViewModel.Ki))
            {
                OnPropertyChanged(nameof(AvatarFile));
                OnPropertyChanged(nameof(HasAvatar));
                ApplyMode(ResolveMode());
                _ = UpdateVoiceAsync();
            }
        };

        // Ein anderer Chat: dessen Modus, sonst der Standard der KI.
        Session.ChatOpened += (_, _) => ApplyMode(ResolveMode());
        _mode = ResolveMode();

        // Neue Einstellungen (Mikrofon, Stimme, Modell): Der Worker startet beim nächsten Gebrauch neu.
        _voiceSignature = VoiceSignature(settings.Current.Voice);
        settings.Saved += (_, saved) => _ui.Post(_ => OnSettingsSaved(saved), null);
    }

    public ChatSessionViewModel Session { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsActive), nameof(CanInterrupt), nameof(IsOff))]
    [NotifyCanExecuteChangedFor(nameof(InterruptCommand))]
    private ConversationState _state = ConversationState.Off;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHandsFree), nameof(StateText), nameof(ModeHint))]
    private VoiceMode _mode;

    /// <summary>Lautstärke 0 bis 1, geglättet. Der Orb wächst damit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrbScale))]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    /// <summary>Kurzer Hinweis unter dem Orb, z.B. "Nichts verstanden." oder Ladezeiten.</summary>
    [ObservableProperty] private string _infoText = "";

    [ObservableProperty] private string _microphoneName = "";

    public bool HasError => ErrorText.Length > 0;

    public bool IsOff => State is ConversationState.Off or ConversationState.Error;

    public bool IsActive => State is ConversationState.Listening or ConversationState.Hearing or ConversationState.Speaking;

    public bool CanInterrupt => State is ConversationState.Thinking or ConversationState.Speaking or ConversationState.Understanding;

    public bool IsHandsFree
    {
        get => Mode == VoiceMode.HandsFree;
        set => Mode = value ? VoiceMode.HandsFree : VoiceMode.PushToTalk;
    }

    // ----------------------------------------------------------- Avatar (Erweiterung E3)

    /// <summary>Die VRM-Datei der KI als voller Pfad, wenn es sie gibt. Sonst null (dann nur der Orb).</summary>
    public string? AvatarFile => _kis.Find(Session.Ki?.Id)?.AvatarPath is { Length: > 0 } path && File.Exists(_paths.Resolve(path)) ? _paths.Resolve(path) : null;

    public bool HasAvatar => AvatarFile is not null;

    /// <summary>Der KI einen Avatar geben (VRM). Gespeichert im KI-Profil (avatarPath), relativ zum Projektordner, wenn er darin liegt.</summary>
    [RelayCommand]
    private async Task PickAvatarAsync()
    {
        if (Session.Ki is not { } ki || _dialogs.PickAvatarFile() is not { } file)
        {
            return;
        }

        var relative = Path.GetRelativePath(_paths.Root, file);
        await SaveAvatarAsync(ki, relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? file : relative.Replace('\\', '/'));
    }

    [RelayCommand]
    private async Task ClearAvatarAsync()
    {
        if (Session.Ki is { AvatarPath: not null } ki)
        {
            await SaveAvatarAsync(ki, null);
        }
    }

    private async Task SaveAvatarAsync(KiProfile ki, string? path)
    {
        var updated = KiNexus.Core.JsonDefaults.Clone(ki);
        updated.AvatarPath = path;
        await _kis.SaveAsync(updated, CancellationToken.None);

        OnPropertyChanged(nameof(AvatarFile));
        OnPropertyChanged(nameof(HasAvatar));
    }

    /// <summary>1.0 bis 1.35: Der Orb atmet mit der Lautstärke.</summary>
    public double OrbScale => 1 + (Level * 0.35);

    /// <summary>Die Taste für Push-to-Talk aus den Einstellungen (Standard: Leertaste).</summary>
    public Key PushToTalkKey =>
        Enum.TryParse<Key>(_settings.Current.Voice.PushToTalkKey, ignoreCase: true, out var key) ? key : Key.Space;

    public string PushToTalkKeyText => PushToTalkKey switch
    {
        Key.Space => "Leertaste",
        Key.RightCtrl => "rechte Strg-Taste",
        var other => other.ToString(),
    };

    public string ModeHint => IsHandsFree
        ? _settings.Current.Voice.BargeIn
            ? "Freihändig: Sprich einfach. Nach einer kurzen Pause antwortet die KI. Sprich dazwischen oder drück Esc, um sie zu unterbrechen."
            : "Freihändig: Sprich einfach. Nach einer kurzen Pause antwortet die KI. Esc unterbricht sie."
        : $"Push-to-Talk: {PushToTalkKeyText} gedrückt halten (oder den Orb), sprechen, loslassen. Esc unterbricht die KI.";

    public string StateText => State switch
    {
        ConversationState.Off => "Klick auf den Orb oder drück die " + PushToTalkKeyText + ", um zu starten.",
        ConversationState.Starting => "Spracherkennung und Stimme werden geladen …",
        ConversationState.Ready => IsHandsFree ? "Pausiert. Klick auf den Orb, um weiter zuzuhören." : $"Bereit. {PushToTalkKeyText} halten und sprechen.",
        ConversationState.Listening => "Ich höre zu …",
        ConversationState.Hearing => "Ich höre zu …",
        ConversationState.Understanding => "Verstehe …",
        ConversationState.Thinking => "Denke nach …",
        ConversationState.Speaking => "Spreche …",
        _ => "Fehler",
    };

    // ----------------------------------------------------------- Bedienung

    /// <summary>Taste oder Maus gedrückt (Push-to-Talk). Spricht die KI gerade, wird sie unterbrochen.</summary>
    public async Task PushToTalkDownAsync()
    {
        // Beim ersten Druck wird nur geladen. Bis es fertig ist, ist die Taste meist schon wieder los.
        var wasRunning = State is not (ConversationState.Off or ConversationState.Error);
        if (!await EnsureStartedAsync() || !wasRunning)
        {
            return;
        }

        if (CanInterrupt)
        {
            await InterruptAsync();
        }

        if (IsHandsFree || State != ConversationState.Ready)
        {
            return;
        }

        State = ConversationState.Listening;
        InfoText = "";
        await _worker.TrySendAsync(VoiceProtocol.Listen("pushToTalk"));
    }

    /// <summary>Taste oder Maus losgelassen: Das Gesagte geht an Whisper.</summary>
    public async Task PushToTalkUpAsync()
    {
        if (IsHandsFree || State != ConversationState.Listening)
        {
            return;
        }

        State = ConversationState.Understanding;
        Level = 0;
        await _worker.TrySendAsync(VoiceProtocol.StopListening(transcribe: true));
    }

    /// <summary>Klick auf den Orb im Modus Freihändig: zuhören oder pausieren.</summary>
    [RelayCommand]
    private async Task ToggleListeningAsync()
    {
        if (!await EnsureStartedAsync() || !IsHandsFree)
        {
            return;
        }

        if (State is ConversationState.Listening or ConversationState.Hearing)
        {
            await _worker.TrySendAsync(VoiceProtocol.StopListening(transcribe: false));
            State = ConversationState.Ready;
            Level = 0;
        }
        else if (State == ConversationState.Ready)
        {
            await StartHandsFreeAsync();
        }
    }

    /// <summary>Esc oder Knopf "Unterbrechen": Antwort und Sprachausgabe sofort stoppen.</summary>
    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    public async Task InterruptAsync()
    {
        _interrupted = true;
        Session.Stop();
        _pendingSpeech.Clear();
        _player.Clear();
        await _worker.TrySendAsync(VoiceProtocol.StopSpeaking());
        _answer = null;
        _answerComplete = true;
        await FinishTurnAsync();
    }

    /// <summary>
    /// Freihändig: Du sprichst, während die KI antwortet. Antwort und Stimme stoppen sofort. Der Worker nimmt
    /// schon auf, darum geht es ohne "resume" direkt mit deiner neuen Frage weiter.
    /// </summary>
    private async Task BargeInAsync()
    {
        _interrupted = true;
        _bargedIn = true;
        Session.Stop();
        _pendingSpeech.Clear();
        _player.Clear();
        await _worker.TrySendAsync(VoiceProtocol.StopSpeaking());
        DetachAnswer();
        _answer = null;
        _answerComplete = true;
        Level = 0;
        State = ConversationState.Hearing;
        InfoText = "Unterbrochen. Ich höre zu …";
    }

    /// <summary>Beim Verlassen des Stils oder "Entladen": Mikrofon zu, Worker beenden (gibt ca. 2 GB RAM frei).</summary>
    public async Task ShutdownAsync()
    {
        _timer.Stop();
        Session.Stop();
        _pendingSpeech.Clear();
        _player.Close();
        await _worker.StopAsync();
        State = ConversationState.Off;
        Level = 0;
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    /// <summary>
    /// Der Gespräch-Stil wird sichtbar. Im Modus Freihändig startet das Gespräch von selbst (Bauplan M7),
    /// ausser es ist in den Einstellungen abgeschaltet.
    /// </summary>
    public async Task OnShownAsync()
    {
        if (!IsHandsFree || !_settings.Current.Voice.AutoListen)
        {
            return;
        }

        if (State == ConversationState.Off)
        {
            await EnsureStartedAsync();
        }
        else if (State == ConversationState.Ready)
        {
            await StartHandsFreeAsync();
        }
    }

    /// <summary>Wechsel zu einem anderen Stil: Mikrofon zu. Der Worker bleibt geladen, der Chat ist derselbe.</summary>
    public async Task OnHiddenAsync()
    {
        if (State is ConversationState.Listening or ConversationState.Hearing)
        {
            await _worker.TrySendAsync(VoiceProtocol.StopListening(transcribe: false));
            State = ConversationState.Ready;
            Level = 0;
        }
    }

    private void OnSettingsSaved(AppSettings saved)
    {
        OnPropertyChanged(nameof(PushToTalkKey));
        OnPropertyChanged(nameof(PushToTalkKeyText));
        OnPropertyChanged(nameof(ModeHint));
        OnPropertyChanged(nameof(StateText));
        _ = UpdateVoiceAsync();
        var signature = VoiceSignature(saved.Voice);
        if (signature != _voiceSignature)
        {
            _voiceSignature = signature;
            if (_worker.IsRunning)
            {
                _ = ShutdownAsync();
                InfoText = "Neue Spracheinstellungen: Sie gelten ab dem nächsten Start (Klick auf den Orb).";
            }
        }
    }

    // Die Stimme ist nicht dabei: Sie wechselt ohne Neustart (UpdateVoiceAsync).
    private static string VoiceSignature(VoiceSettings voice) =>
        string.Join("|", voice.Microphone, voice.WhisperModel, voice.SpeechRecognitionDevice, voice.SilenceMs);

    /// <summary>Modus des Chats (falls gespeichert), sonst der Standard der KI (Feineinstellungen → Gespräch).</summary>
    private VoiceMode ResolveMode() =>
        Session.Chat?.VoiceMode is { } saved && Enum.TryParse<VoiceMode>(saved, ignoreCase: true, out var mode)
            ? mode
            : Session.Ki?.Voice.DefaultMode ?? VoiceMode.PushToTalk;

    private void ApplyMode(VoiceMode mode)
    {
        _applyingMode = true;
        try
        {
            Mode = mode;
        }
        finally
        {
            _applyingMode = false;
        }
    }

    partial void OnModeChanged(VoiceMode value)
    {
        // Im Gesprächsfenster umgeschaltet: Das gilt ab jetzt für diesen Chat.
        if (!_applyingMode)
        {
            _ = Session.SaveVoiceModeAsync(value);
        }

        if (State is ConversationState.Off or ConversationState.Starting or ConversationState.Error)
        {
            return;
        }

        _ = SwitchModeAsync(value);
    }

    private async Task SwitchModeAsync(VoiceMode mode)
    {
        if (mode == VoiceMode.HandsFree && State == ConversationState.Ready)
        {
            await StartHandsFreeAsync();
        }
        else if (mode == VoiceMode.PushToTalk && State is ConversationState.Listening or ConversationState.Hearing)
        {
            await _worker.TrySendAsync(VoiceProtocol.StopListening(transcribe: false));
            State = ConversationState.Ready;
            Level = 0;
        }
    }

    // ----------------------------------------------------------- Start

    /// <summary>Startet beim ersten Gebrauch den Sprach-Worker (lädt Whisper und Piper, ca. 8 s).</summary>
    private async Task<bool> EnsureStartedAsync()
    {
        if (State == ConversationState.Starting)
        {
            return false;
        }

        if (State is not (ConversationState.Off or ConversationState.Error) && _worker.IsRunning)
        {
            return true;
        }

        var voice = _settings.Current.Voice;
        var installation = VoiceInstallation.For(_paths, _paths.Resolve(_settings.Current.Paths.Models));
        if (installation.Problem(voice) is { } problem)
        {
            ErrorText = problem;
            State = ConversationState.Error;
            return false;
        }

        State = ConversationState.Starting;
        ErrorText = "";
        InfoText = "Das dauert beim ersten Mal ca. 10 Sekunden.";
        try
        {
            var (speaker, lengthScale) = VoiceFor(installation, Session.Ki);
            var options = new VoiceWorkerOptions(
                installation.WhisperPath(voice.WhisperModel),
                installation.VoicePath(speaker),
                voice.SpeechRecognitionDevice == ComputeDevice.Gpu ? "gpu" : "cpu",
                voice.Microphone,
                voice.SilenceMs,
                SpeakerId: VoiceInstallation.SpeakerId(speaker),
                LengthScale: lengthScale);
            var ready = await _worker.StartAsync(installation, options, CancellationToken.None);
            _loadedVoice = VoiceKey(speaker, lengthScale);
            _player.Open(ready.SampleRate);
            _timer.Start();
            InfoText = "";
            State = ConversationState.Ready;
            if (IsHandsFree)
            {
                await StartHandsFreeAsync();
            }

            return !IsHandsFree;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Sprache konnte nicht starten.");
            ErrorText = "Die Sprache konnte nicht starten: " + ex.Message;
            InfoText = "";
            State = ConversationState.Error;
            return false;
        }
    }

    /// <summary>Die Stimme der KI (Feineinstellungen → Gespräch), sonst die aus den Einstellungen.</summary>
    private (string Voice, double? LengthScale) VoiceFor(VoiceInstallation installation, KiProfile? ki)
    {
        var wanted = ki?.Voice.Voice is { Length: > 0 } own ? own : _settings.Current.Voice.Voice;
        return (installation.ChooseVoice(wanted), ki?.Voice.LengthScale ?? _settings.Current.Voice.LengthScale);
    }

    private static string VoiceKey(string voice, double? lengthScale) =>
        voice + "|" + (lengthScale ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Andere KI oder neue Einstellungen: Hat sie eine andere Stimme, lädt der Worker sie (ca. 1 s, ohne Neustart).</summary>
    private async Task UpdateVoiceAsync()
    {
        if (!_worker.IsRunning || State is ConversationState.Off or ConversationState.Starting or ConversationState.Error)
        {
            return;
        }

        var installation = VoiceInstallation.For(_paths, _paths.Resolve(_settings.Current.Paths.Models));
        var (voice, lengthScale) = VoiceFor(installation, Session.Ki);
        var key = VoiceKey(voice, lengthScale);
        if (key == _loadedVoice)
        {
            return;
        }

        _loadedVoice = key;
        await _worker.TrySendAsync(VoiceProtocol.ChangeVoice(installation.VoicePath(voice), VoiceInstallation.SpeakerId(voice), lengthScale));
    }

    private async Task StartHandsFreeAsync()
    {
        State = ConversationState.Listening;
        InfoText = "";
        await _worker.TrySendAsync(VoiceProtocol.Listen("handsFree"));
    }

    // ----------------------------------------------------------- Ereignisse vom Worker

    private void OnVoiceEvent(VoiceEvent voiceEvent)
    {
        switch (voiceEvent)
        {
            case VoiceListening listening:
                MicrophoneName = listening.Microphone ?? "";
                if (State is ConversationState.Understanding or ConversationState.Ready or ConversationState.Listening)
                {
                    State = ConversationState.Listening;
                }

                break;
            case VoiceSpeechStart when State == ConversationState.Listening:
                State = ConversationState.Hearing;
                break;
            case VoiceTranscribing:
                State = ConversationState.Understanding;
                Level = 0;
                break;
            case VoiceTranscript transcript:
                _ = OnTranscriptAsync(transcript);
                break;
            case VoiceLevel level when State is ConversationState.Listening or ConversationState.Hearing:
                Level = Smooth(Level, level.Value);
                break;
            case VoiceAudio audio when audio.Id >= _firstSpeechIdOfTurn && _pendingSpeech.Contains(audio.Id):
                _player.Enqueue(audio.Pcm);
                if (State == ConversationState.Thinking)
                {
                    State = ConversationState.Speaking;
                }

                break;
            case VoiceSpeakDone done:
                _pendingSpeech.Remove(done.Id);
                break;
            case VoiceBargeIn when IsHandsFree && State is ConversationState.Thinking or ConversationState.Speaking:
                _ = BargeInAsync();
                break;
            case VoiceChanged changed:
                _player.Open(changed.SampleRate);
                break;
            case VoiceError error:
                ErrorText = error.Message;
                if (error.Fatal)
                {
                    State = ConversationState.Error;
                }

                break;
            case VoiceExited exited when State is not ConversationState.Off:
                _timer.Stop();
                _player.Close();
                ErrorText = $"Der Sprach-Worker wurde beendet (Code {exited.ExitCode}). Klick auf den Orb, um neu zu starten.";
                State = ConversationState.Error;
                break;
        }
    }

    private async Task OnTranscriptAsync(VoiceTranscript transcript)
    {
        var text = SpeechText.NormalizeTranscript(transcript.Text);
        if (text.Length == 0 || SpeechText.IsLikelyHallucination(text))
        {
            _logger.LogInformation("Mitschrift verworfen: '{Text}' ({Reason}).", text, transcript.Reason ?? "leer");
            InfoText = transcript.Reason switch
            {
                "zu kurz" => "Zu kurz. Halte die Taste, solange du sprichst.",
                "zu leise" => "Ich habe nichts gehört. Ist das Mikrofon stumm oder zu weit weg?",
                _ => "Nichts verstanden. Versuch es nochmals.",
            };
            await BackToListeningAsync();
            return;
        }

        // Nach dem Unterbrechen: Hat das Mikrofon nur die KI selbst gehört (Lautsprecher)? Dann nicht als Frage nehmen.
        var afterBargeIn = _bargedIn;
        _bargedIn = false;
        if (afterBargeIn && SpeechText.LooksLikeEcho(text, _spokenThisTurn))
        {
            _logger.LogInformation("Unterbrechen durch Echo verworfen: '{Text}'.", text);
            InfoText = "Das war wohl die Stimme der KI selbst. Mit Lautsprechern: Kopfhörer nutzen oder „Unterbrechen durch Sprechen“ ausschalten.";
            await BackToListeningAsync();
            return;
        }

        if (Session.IsBusy || Session.Ki is null)
        {
            InfoText = Session.Ki is null ? "Wähle zuerst eine KI." : "Die KI ist noch beschäftigt.";
            await BackToListeningAsync();
            return;
        }

        InfoText = $"Erkannt in {transcript.TookMs / 1000.0:F1} s";
        await AskAsync(text);
    }

    // ----------------------------------------------------------- Frage und Antwort

    private async Task AskAsync(string text)
    {
        State = ConversationState.Thinking;
        _splitter.Reset();
        _pendingSpeech.Clear();
        _answer = null;
        _spokenLength = 0;
        _answerComplete = false;
        _firstSpeechIdOfTurn = _nextSpeechId;
        _drainedSince = DateTime.MaxValue;
        _interrupted = false;
        _spokenThisTurn.Clear();

        // Freihändig: Während die KI antwortet, hört der Worker mit. Sprichst du dazwischen, hört sie auf.
        if (IsHandsFree && _settings.Current.Voice.BargeIn)
        {
            await _worker.TrySendAsync(VoiceProtocol.Watch());
        }

        var suffix = _settings.Current.Voice.ShortSpokenAnswers ? SpeechText.ConversationHint : null;
        await Session.AskSpokenAsync(text, suffix);
        if (_interrupted)
        {
            // Esc oder Taste: InterruptAsync hat schon aufgeräumt.
            return;
        }

        // Die Antwort ist fertig (oder abgebrochen): den Rest vorlesen.
        if (_answer is not null)
        {
            SpeakNewText(_answer);
            foreach (var sentence in _splitter.Flush())
            {
                await SpeakAsync(sentence);
            }

            DetachAnswer();
        }

        _answerComplete = true;
        if (Session.HasError && _pendingSpeech.Count == 0)
        {
            ErrorText = Session.ErrorText;
            await FinishTurnAsync();
        }
    }

    /// <summary>Neue Antwort im Chat: ihr Text wird mitgelesen, sobald er wächst.</summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (State != ConversationState.Thinking || _answer is not null || e.NewItems is null)
        {
            return;
        }

        if (e.NewItems.OfType<MessageViewModel>().FirstOrDefault(m => m.IsAssistant) is { } answer)
        {
            _answer = answer;
            _answer.PropertyChanged += OnAnswerChanged;
        }
    }

    private void OnAnswerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageViewModel.Content) && sender is MessageViewModel answer)
        {
            SpeakNewText(answer);
        }
    }

    private void SpeakNewText(MessageViewModel answer)
    {
        var content = answer.Content;
        if (content.Length <= _spokenLength)
        {
            return;
        }

        var delta = content[_spokenLength..];
        _spokenLength = content.Length;
        foreach (var sentence in _splitter.Append(delta))
        {
            _ = SpeakAsync(sentence);
        }
    }

    private async Task SpeakAsync(string sentence)
    {
        _spokenThisTurn.Add(sentence);
        var id = _nextSpeechId++;
        _pendingSpeech.Add(id);
        await _worker.TrySendAsync(VoiceProtocol.Speak(id, sentence));
    }

    private void DetachAnswer()
    {
        if (_answer is not null)
        {
            _answer.PropertyChanged -= OnAnswerChanged;
        }
    }

    /// <summary>Alle 50 ms: Lautstärke beim Sprechen, und prüfen, ob die KI fertig gesprochen hat.</summary>
    private void OnTick()
    {
        if (State is ConversationState.Speaking or ConversationState.Thinking)
        {
            Level = Smooth(Level, _player.Level);
        }
        else if (State is not (ConversationState.Listening or ConversationState.Hearing))
        {
            Level = Smooth(Level, 0);
        }

        if (!_answerComplete || State is not (ConversationState.Speaking or ConversationState.Thinking) || _pendingSpeech.Count > 0)
        {
            return;
        }

        if (_player.Buffered > TimeSpan.Zero)
        {
            _drainedSince = DateTime.MaxValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (_drainedSince == DateTime.MaxValue)
        {
            _drainedSince = now;
        }
        else if (now - _drainedSince >= DrainGrace)
        {
            _drainedSince = DateTime.MaxValue;
            _ = FinishTurnAsync();
        }
    }

    /// <summary>Die KI ist fertig: Freihändig wieder zuhören, sonst auf die Taste warten.</summary>
    private async Task FinishTurnAsync()
    {
        DetachAnswer();
        _answer = null;
        if (State is ConversationState.Off or ConversationState.Error)
        {
            return;
        }

        await BackToListeningAsync();
    }

    private async Task BackToListeningAsync()
    {
        Level = 0;
        if (IsHandsFree)
        {
            State = ConversationState.Listening;
            await _worker.TrySendAsync(VoiceProtocol.Resume());
        }
        else
        {
            State = ConversationState.Ready;
        }
    }

    /// <summary>Weich nachführen, damit der Orb nicht zappelt.</summary>
    private static double Smooth(double current, double target) => current + ((target - current) * (target > current ? 0.5 : 0.2));
}
