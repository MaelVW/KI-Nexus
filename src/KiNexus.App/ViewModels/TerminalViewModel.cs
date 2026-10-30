using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core.Chats;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;

namespace KiNexus.App.ViewModels;

/// <summary>Eine Zeile oder ein Block im Terminal.</summary>
public abstract class TerminalEntry : ObservableObject;

/// <summary>Meine Eingabe: "mael@nexus › Hallo" (Cyan).</summary>
public sealed class TerminalInputEntry(string prompt, string text) : TerminalEntry
{
    public string Prompt { get; } = prompt;
    public string Text { get; } = text;
}

/// <summary>Systemmeldung (gelb) oder Fehler (rot).</summary>
public sealed class TerminalTextEntry(string text, bool isError) : TerminalEntry
{
    public string Text { get; } = text;
    public bool IsError { get; } = isError;
}

/// <summary>
/// Eine Antwort der KI. DisplayText holt den echten Text Zeichen für Zeichen ein (Schreibmaschinen-Effekt).
/// Das Tempo folgt dem Streaming: Kommt viel auf einmal, tippt der Effekt schneller.
/// </summary>
public sealed partial class TerminalAnswerEntry : TerminalEntry
{
    public TerminalAnswerEntry(MessageViewModel message, bool animate)
    {
        Message = message;
        _displayText = animate ? "" : message.Content;
        _isThinkingExpanded = message.IsStreaming && message.Content.Length == 0;
        // Sobald die Antwort beginnt, klappen die Gedanken zu (wie im Simpel-Stil).
        message.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MessageViewModel.Content) && message.IsStreaming && message.Content.Length > 0)
            {
                IsThinkingExpanded = false;
            }
        };
    }

    public MessageViewModel Message { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCursor))]
    private string _displayText;

    [ObservableProperty] private bool _isThinkingExpanded;

    /// <summary>Blinkender Block-Cursor, solange getippt wird.</summary>
    public bool ShowCursor => Message.IsStreaming || DisplayText.Length < Message.Content.Length;

    /// <summary>Ein Schritt des Effekts. True, solange noch Text fehlt.</summary>
    public bool Advance()
    {
        var target = Message.Content;
        if (!target.StartsWith(DisplayText, StringComparison.Ordinal))
        {
            DisplayText = "";
        }

        var backlog = target.Length - DisplayText.Length;
        if (backlog > 0)
        {
            // Rest in ca. 4 Schritten (ca. 60 ms) aufholen, mindestens 1 Zeichen pro Schritt.
            DisplayText = target[..(DisplayText.Length + Math.Max(1, (backlog + 3) / 4))];
        }
        else
        {
            OnPropertyChanged(nameof(ShowCursor));
        }

        return DisplayText.Length < target.Length || Message.IsStreaming;
    }
}

/// <summary>
/// Der Terminal-Stil. Sieht aus wie ein Terminal, führt aber nie echte Shell-Befehle aus.
/// Befehle beginnen mit "/", alles andere geht als Frage an die KI.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    public const string PromptText = "mael@nexus ›";

    private static readonly TimeSpan TypewriterTick = TimeSpan.FromMilliseconds(16);

    private readonly ChatSessionViewModel _session;
    private readonly IChatShell _shell;
    private readonly InputHistory _history = new();
    private IReadOnlyList<string> _completions = [];
    private int _completionIndex;
    private string _completionBase = "";
    private bool _typewriterRunning;

    /// <summary>Nach /clear werden nur noch Nachrichten ab dieser Stelle angezeigt.</summary>
    private int _visibleFrom;

    public TerminalViewModel(ChatSessionViewModel session, IChatShell shell)
    {
        _session = session;
        _shell = shell;
        _session.Messages.CollectionChanged += OnMessagesChanged;
        _session.ChatOpened += (_, _) => Rebuild(clearScreen: true);
        _session.PropertyChanged += OnSessionPropertyChanged;
        Rebuild(clearScreen: true);
    }

    public string Prompt => PromptText;

    public ObservableCollection<TerminalEntry> Entries { get; } = [];

    public ChatSessionViewModel Session => _session;

    [ObservableProperty] private string _input = "";

    /// <summary>Wird nach jeder neuen Zeile ausgelöst, damit die Ansicht ans Ende scrollt.</summary>
    public event EventHandler? ScrollRequested;

    /// <summary>Statuszeile unten: KI, Engine, Thinking, Kontext.</summary>
    public string StatusLine =>
        $"{_session.Ki?.DisplayName ?? "keine KI"} · {_session.Ki?.EngineLabel ?? "–"} · Thinking {(_session.ThinkEnabled ? "an" : "aus")} · Kontext {_session.ContextText}"
        + (_session.IsBusy ? " · antwortet … (Strg + C bricht ab)" : "");

    // ------------------------------------------------------------------ Tastatur

    /// <summary>Enter: Befehl oder Frage ausführen.</summary>
    [RelayCommand]
    private async Task ExecuteAsync()
    {
        var text = Input.Trim();
        Input = "";
        ResetCompletion();
        if (text.Length == 0)
        {
            return;
        }

        _history.Add(text);
        var command = TerminalCommands.Parse(text);
        if (command.Kind != TerminalCommandKind.None)
        {
            Add(new TerminalInputEntry(Prompt, text));
        }

        await RunAsync(command, text);
    }

    public void HistoryUp() => Input = _history.Previous(Input);

    public void HistoryDown() => Input = _history.Next();

    /// <summary>Tab: Befehl vervollständigen. Mehrmals Tab geht durch alle Treffer.</summary>
    public void Complete()
    {
        if (_completions.Count == 0 || Input != _completions[_completionIndex])
        {
            _completionBase = Input;
            _completions = TerminalCommands.CompletionsFor(_completionBase);
            _completionIndex = -1;
        }

        if (_completions.Count == 0)
        {
            return;
        }

        _completionIndex = (_completionIndex + 1) % _completions.Count;
        Input = _completions[_completionIndex];
    }

    /// <summary>Strg + C: laufende Antwort abbrechen.</summary>
    public void Interrupt()
    {
        if (_session.IsBusy)
        {
            Add(new TerminalTextEntry("^C", isError: false));
            _session.Stop();
        }
    }

    // ------------------------------------------------------------------ Befehle

    private async Task RunAsync(TerminalCommand command, string text)
    {
        switch (command.Kind)
        {
            case TerminalCommandKind.None:
                await AskAsync(text);
                break;
            case TerminalCommandKind.Help:
                System(string.Join("\n", TerminalCommands.Help.Select(h => $"{h.Usage,-18} {h.Description}")));
                break;
            case TerminalCommandKind.ThinkOn or TerminalCommandKind.ThinkOff:
                _session.ThinkEnabled = command.Kind == TerminalCommandKind.ThinkOn;
                System(_session.ThinkEnabled ? "Thinking ist an." : "Thinking ist aus.");
                break;
            case TerminalCommandKind.ThinkShow or TerminalCommandKind.ThinkHide:
                _session.ShowThinking = command.Kind == TerminalCommandKind.ThinkShow;
                System(_session.ShowThinking ? "Gedanken werden angezeigt." : "Gedanken sind versteckt.");
                break;
            case TerminalCommandKind.SelectKi:
                SelectKi(command.Argument);
                break;
            case TerminalCommandKind.Style:
                _shell.SetStyle(Enum.Parse<ChatStyle>(command.Argument, ignoreCase: true));
                break;
            case TerminalCommandKind.NewChat:
                _session.StartNewChat();
                System("Neuer Chat.");
                break;
            case TerminalCommandKind.Clear:
                _visibleFrom = _session.Messages.Count;
                Entries.Clear();
                break;
            case TerminalCommandKind.Stop:
                if (_session.IsBusy) { _session.Stop(); } else { System("Es läuft gerade keine Antwort."); }
                break;
            case TerminalCommandKind.Stats:
                System($"Letzte Antwort: {_session.LastStatsText}\nKontext: {_session.ContextText}\nKI: {_session.Ki?.DisplayName ?? "keine"} ({_session.Ki?.EngineLabel ?? "–"}) · Thinking {(_session.ThinkEnabled ? "an" : "aus")}");
                break;
            case TerminalCommandKind.Unload:
                await _shell.UnloadAsync();
                System("Modell entladen.");
                break;
            case TerminalCommandKind.Invalid:
                Add(new TerminalTextEntry(command.Error, isError: true));
                break;
        }
    }

    private async Task AskAsync(string text)
    {
        if (_session.IsBusy)
        {
            Add(new TerminalTextEntry("Warte, bis die Antwort fertig ist, oder brich sie mit /stop ab.", isError: true));
            return;
        }

        if (_session.Readiness.HasHint)
        {
            Add(new TerminalTextEntry(_session.Readiness.HintText, isError: true));
            return;
        }

        await _session.AskAsync(text, []);
        if (_session.HasError)
        {
            Add(new TerminalTextEntry(_session.ErrorText, isError: _session.ErrorText != "Abgebrochen."));
        }
    }

    /// <summary>/ki Name: findet die KI über Namen oder Id, auch mit Anfang des Namens.</summary>
    private void SelectKi(string name)
    {
        var kis = _shell.Kis;
        var exact = kis.FirstOrDefault(k => string.Equals(k.DisplayName, name, StringComparison.CurrentCultureIgnoreCase) || string.Equals(k.Id, name, StringComparison.OrdinalIgnoreCase));
        var matches = exact is null
            ? kis.Where(k => k.DisplayName.StartsWith(name, StringComparison.CurrentCultureIgnoreCase) || k.Id.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList()
            : [exact];
        if (matches.Count != 1)
        {
            var message = matches.Count == 0
                ? $"Keine KI heisst \"{name}\". Es gibt: {string.Join(", ", kis.Select(k => k.DisplayName))}"
                : $"\"{name}\" passt auf mehrere KIs: {string.Join(", ", matches.Select(k => k.DisplayName))}. Schreib den ganzen Namen.";
            Add(new TerminalTextEntry(message, isError: true));
            return;
        }

        var ki = matches[0];

        _shell.SelectedKi = ki;
        System($"KI gewechselt: {ki.DisplayName} ({ki.EngineLabel}).");
    }

    // ------------------------------------------------------------------ Anzeige

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (MessageViewModel message in e.NewItems)
            {
                AddMessage(message, animate: message.IsStreaming);
            }
        }
        else if (e.Action != NotifyCollectionChangedAction.Add)
        {
            // Entfernen (neu erzeugen, bearbeiten) oder neu laden: Bildschirm neu aufbauen.
            Rebuild(clearScreen: false);
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatSessionViewModel.Ki) or nameof(ChatSessionViewModel.ThinkEnabled)
            or nameof(ChatSessionViewModel.ContextText) or nameof(ChatSessionViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    private void Rebuild(bool clearScreen)
    {
        if (clearScreen)
        {
            _visibleFrom = 0;
        }

        _visibleFrom = Math.Min(_visibleFrom, _session.Messages.Count);
        Entries.Clear();
        foreach (var message in _session.Messages.Skip(_visibleFrom))
        {
            AddMessage(message, animate: false);
        }
    }

    private void AddMessage(MessageViewModel message, bool animate)
    {
        if (message.IsUser)
        {
            var images = message.HasImages ? $"  [{message.Images.Count} Bild(er)]" : "";
            Add(new TerminalInputEntry(Prompt, message.Content + images));
            return;
        }

        Add(new TerminalAnswerEntry(message, animate));
        if (animate)
        {
            _ = RunTypewriterAsync();
        }
    }

    /// <summary>Läuft auf dem UI-Thread, solange eine Antwort noch getippt wird.</summary>
    private async Task RunTypewriterAsync()
    {
        if (_typewriterRunning)
        {
            return;
        }

        _typewriterRunning = true;
        using var timer = new PeriodicTimer(TypewriterTick);
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                var busy = false;
                foreach (var answer in Entries.OfType<TerminalAnswerEntry>())
                {
                    busy |= answer.Advance();
                }

                ScrollRequested?.Invoke(this, EventArgs.Empty);
                if (!busy)
                {
                    break;
                }
            }
        }
        finally
        {
            _typewriterRunning = false;
        }
    }

    private void System(string text) => Add(new TerminalTextEntry(text, isError: false));

    private void Add(TerminalEntry entry)
    {
        Entries.Add(entry);
        ScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetCompletion()
    {
        _completions = [];
        _completionIndex = 0;
    }
}
