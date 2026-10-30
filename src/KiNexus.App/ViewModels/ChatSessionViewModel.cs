using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Der offene Chat. Simpel- und Terminal-Stil zeigen beide diesen einen Chat.
/// Darum bleibt der Verlauf gleich, wenn man mitten im Gespräch den Stil wechselt.
/// </summary>
public sealed partial class ChatSessionViewModel : ObservableObject
{
    private readonly ChatService _chats;
    private readonly EngineManager _engines;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly IKiRegistry _kis;
    private readonly ILogger<ChatSessionViewModel> _logger;
    private CancellationTokenSource? _turn;
    private CancellationTokenSource? _titleGeneration;

    public ChatSessionViewModel(
        ChatService chats, EngineManager engines, ISettingsService settings, IDialogService dialogs,
        IShellService shell, IKiRegistry kis, KiReadinessViewModel readiness, ILogger<ChatSessionViewModel> logger)
    {
        _chats = chats;
        _engines = engines;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _kis = kis;
        _logger = logger;
        Readiness = readiness;
        _showThinking = settings.Current.Chat.ShowThinking;
        Readiness.Changed += (_, _) => SendCommand.NotifyCanExecuteChanged();
    }

    public KiReadinessViewModel Readiness { get; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    /// <summary>Bilder, die mit der nächsten Nachricht mitgehen (Simpel-Stil).</summary>
    public ObservableCollection<string> PendingImages { get; } = [];

    /// <summary>Wird ausgelöst, wenn ein Chat neu angelegt oder umbenannt wurde (für die Seitenleiste).</summary>
    public event EventHandler<ChatRecord>? ChatSaved;

    /// <summary>Wird ausgelöst, wenn ein anderer Chat geöffnet wird (auch "Neuer Chat").</summary>
    public event EventHandler? ChatOpened;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages))]
    private ChatRecord? _chat;

    [ObservableProperty] private KiProfile? _ki;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditLastCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty] private bool _thinkEnabled;
    [ObservableProperty] private bool _showThinking;
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextHint))]
    private string _contextHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextText))]
    [NotifyPropertyChangedFor(nameof(ContextFraction))]
    private int _contextUsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextText))]
    [NotifyPropertyChangedFor(nameof(ContextFraction))]
    private int _contextTotal = 8192;

    [ObservableProperty] private bool _canAttachImages;
    [ObservableProperty] private string _attachHint = "";
    [ObservableProperty] private bool _isEditingLast;

    public bool HasMessages => Messages.Count > 0;
    public bool HasError => ErrorText.Length > 0;
    public bool HasContextHint => ContextHint.Length > 0;
    public string ContextText => string.Create(CultureInfo.InvariantCulture, $"{ContextUsed} / {ContextTotal} Token");
    public double ContextFraction => ContextTotal > 0 ? Math.Min(1, ContextUsed / (double)ContextTotal) : 0;

    /// <summary>Die Statistik der letzten Antwort (für /stats im Terminal).</summary>
    public string LastStatsText => Messages.LastOrDefault(m => m.IsAssistant && m.StatsText.Length > 0)?.StatsText ?? "Noch keine fertige Antwort.";

    // ------------------------------------------------------------------ Chat öffnen

    public void StartNewChat()
    {
        CancelTitleGeneration();
        Chat = null;
        Messages.Clear();
        ResetTransientState();
        ThinkEnabled = Ki?.Parameters.ThinkByDefault ?? false;
        UpdateContextGauge();
        ChatOpened?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasMessages));
    }

    public async Task OpenChatAsync(long chatId)
    {
        if (Chat?.Id == chatId || IsBusy)
        {
            return;
        }

        CancelTitleGeneration();
        var chat = await _chats.GetChatAsync(chatId, CancellationToken.None);
        if (chat is null)
        {
            StartNewChat();
            return;
        }

        var records = await _chats.GetMessagesAsync(chatId, CancellationToken.None);
        Chat = chat;
        Messages.Clear();
        foreach (var record in records)
        {
            Messages.Add(new MessageViewModel(record, KiName(record.KiId)));
        }

        ResetTransientState();
        ThinkEnabled = chat.ThinkEnabled;
        UpdateLastFlags();
        UpdateContextGauge();
        await RememberLastChatAsync(chat.Id);
        ChatOpened?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasMessages));
    }

    // ------------------------------------------------------------------ Fragen

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        var images = PendingImages.ToList();
        Input = "";
        PendingImages.Clear();

        if (IsEditingLast)
        {
            IsEditingLast = false;
            RemoveFromLastUser();
            await RunTurnAsync((chat, ki, ct) => _chats.EditLastUserMessageAsync(chat, ki, text, ThinkEnabled, Progress(), ct));
            return;
        }

        await AskAsync(text, images);
    }

    private bool CanSend() => !IsBusy && Ki is not null && !Readiness.HasHint && (Input.Trim().Length > 0 || PendingImages.Count > 0);

    /// <summary>Eine Frage stellen. Der Terminal-Stil ruft das direkt auf.</summary>
    public Task AskAsync(string text, IReadOnlyList<string> images) =>
        RunTurnAsync((chat, ki, ct) => _chats.SendAsync(chat, ki, text, images, ThinkEnabled, Progress(), ct));

    [RelayCommand(CanExecute = nameof(IsBusy))]
    public void Stop() => _turn?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRegenerate))]
    private async Task RegenerateAsync()
    {
        var lastUser = Messages.LastOrDefault(m => m.IsUser);
        if (lastUser is null)
        {
            return;
        }

        while (Messages[^1] != lastUser)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        await RunTurnAsync((chat, ki, ct) => _chats.RegenerateAsync(chat, ki, ThinkEnabled, Progress(), ct));
    }

    private bool CanRegenerate() => !IsBusy && Chat is not null && Messages.Any(m => m.IsUser);

    /// <summary>Die letzte eigene Nachricht in das Eingabefeld holen. "Senden" schickt sie dann neu.</summary>
    [RelayCommand(CanExecute = nameof(CanRegenerate))]
    private void EditLast()
    {
        if (Messages.LastOrDefault(m => m.IsUser) is not { } lastUser)
        {
            return;
        }

        Input = lastUser.Content;
        IsEditingLast = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingLast = false;
        Input = "";
    }

    // ------------------------------------------------------------------ Knöpfe pro Antwort

    [RelayCommand]
    private void Copy(MessageViewModel message) => _shell.CopyText(message.Content);

    [RelayCommand]
    private void ToggleStats(MessageViewModel message) => message.ShowStats = !message.ShowStats;

    [RelayCommand]
    private Task RateUpAsync(MessageViewModel message) => RateAsync(message, 1);

    [RelayCommand]
    private Task RateDownAsync(MessageViewModel message) => RateAsync(message, -1);

    /// <summary>Nochmals auf denselben Daumen klicken nimmt die Bewertung zurück.</summary>
    private async Task RateAsync(MessageViewModel message, int rating)
    {
        if (message.Id == 0)
        {
            return;
        }

        var newRating = message.Rating == rating ? (int?)null : rating;
        await _chats.SetRatingAsync(message.Id, newRating, CancellationToken.None);
        message.Rating = newRating;
    }

    [RelayCommand]
    private void OpenImage(string path) => _shell.OpenFile(path);

    // ------------------------------------------------------------------ Bilder

    [RelayCommand]
    private void AttachImages() => AddImages(_dialogs.PickImages());

    /// <summary>Auch für Drag & Drop. Nicht unterstützte Dateien werden mit Hinweis übersprungen.</summary>
    public void AddImages(IEnumerable<string> files)
    {
        if (!CanAttachImages)
        {
            ErrorText = AttachHint;
            return;
        }

        foreach (var file in files)
        {
            if (!AttachmentStore.IsSupportedImage(file))
            {
                ErrorText = $"{Path.GetFileName(file)}: Nur Bilder gehen ({string.Join(", ", AttachmentStore.AllowedExtensions)}).";
                continue;
            }

            if (!PendingImages.Contains(file))
            {
                PendingImages.Add(file);
            }
        }

        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveImage(string path)
    {
        PendingImages.Remove(path);
        SendCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ Einstellungen pro Chat

    partial void OnKiChanged(KiProfile? value)
    {
        _ = Readiness.CheckAsync(value);
        _ = UpdateImageSupportAsync(value);
        if (Chat is null)
        {
            ThinkEnabled = value?.Parameters.ThinkByDefault ?? false;
        }

        UpdateContextGauge();
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnThinkEnabledChanged(bool value)
    {
        if (Chat is { } chat && chat.ThinkEnabled != value)
        {
            Chat = chat with { ThinkEnabled = value };
            _ = _chats.UpdateChatAsync(Chat, CancellationToken.None);
        }
    }

    partial void OnShowThinkingChanged(bool value)
    {
        var settings = JsonDefaults.Clone(_settings.Current);
        settings.Chat.ShowThinking = value;
        _ = _settings.SaveAsync(settings, CancellationToken.None);
    }

    // ------------------------------------------------------------------ Ablauf einer Frage

    private delegate IAsyncEnumerable<ChatTurnEvent> TurnFactory(ChatRecord chat, KiProfile ki, CancellationToken ct);

    /// <summary>
    /// Gemeinsamer Ablauf für Senden, Neu erzeugen und Bearbeiten:
    /// Chat anlegen (falls neu), Ereignisse des ChatService anzeigen, am Ende Titel erzeugen.
    /// </summary>
    private async Task RunTurnAsync(TurnFactory turn)
    {
        if (Ki is not { } ki || IsBusy)
        {
            return;
        }

        CancelTitleGeneration();
        ErrorText = "";
        ContextHint = "";
        IsBusy = true;
        using var cts = new CancellationTokenSource();
        _turn = cts;
        MessageViewModel? answer = null;
        try
        {
            var chat = await EnsureChatAsync(ki);
            await foreach (var turnEvent in turn(chat, ki, cts.Token))
            {
                switch (turnEvent)
                {
                    case UserMessageSaved saved:
                        Messages.Add(new MessageViewModel(saved.Message, ki.DisplayName));
                        break;
                    case ContextPrepared prepared:
                        ShowContextPlan(prepared.Plan);
                        answer = new MessageViewModel(ki.DisplayName) { IsThinkingExpanded = true };
                        Messages.Add(answer);
                        break;
                    case AnswerChunk { Chunk: var chunk } when answer is not null:
                        AppendChunk(answer, chunk);
                        break;
                    case AnswerSaved saved:
                        FinishAnswer(answer, saved);
                        answer = null;
                        break;
                }

                OnPropertyChanged(nameof(HasMessages));
            }

            StatusText = "";
            // Der Titel kommt immer aus der ersten Frage und Antwort, auch wenn eine frühere Titel-Erzeugung abgebrochen wurde.
            if (Chat is { HasDefaultTitle: true }
                && Messages.FirstOrDefault(m => m.IsUser) is { } firstQuestion
                && Messages.FirstOrDefault(m => m.IsAssistant && m.Content.Length > 0) is { } firstAnswer)
            {
                _ = CreateTitleAsync(Chat, ki, firstQuestion.Content, firstAnswer.Content);
            }
        }
        catch (Exception ex) when (ex is InferenceException or HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Frage fehlgeschlagen.");
            StatusText = "";
            ErrorText = ex is OperationCanceledException ? "Abgebrochen." : ex.Message;
            if (answer is { Content.Length: 0, Thinking.Length: 0 })
            {
                Messages.Remove(answer);
            }
            else if (answer is not null)
            {
                answer.IsStreaming = false;
            }
        }
        finally
        {
            _turn = null;
            IsBusy = false;
            UpdateLastFlags();
            OnPropertyChanged(nameof(LastStatsText));
        }
    }

    private async Task<ChatRecord> EnsureChatAsync(KiProfile ki)
    {
        if (Chat is null)
        {
            Chat = await _chats.CreateChatAsync(ki, ThinkEnabled, CancellationToken.None);
            ChatSaved?.Invoke(this, Chat);
            await RememberLastChatAsync(Chat.Id);
        }
        else if (Chat.KiId != ki.Id)
        {
            Chat = await _chats.UpdateChatAsync(Chat with { KiId = ki.Id }, CancellationToken.None);
        }

        return Chat;
    }

    private void AppendChunk(MessageViewModel answer, ChatChunk chunk)
    {
        if (chunk.Thinking.Length > 0)
        {
            answer.Thinking += chunk.Thinking;
        }

        if (chunk.Text.Length > 0)
        {
            if (answer.Content.Length == 0)
            {
                // Sobald die Antwort beginnt, klappen die Gedanken zu.
                answer.IsThinkingExpanded = false;
            }

            answer.Content += chunk.Text;
            StatusText = "";
        }
    }

    private void FinishAnswer(MessageViewModel? answer, AnswerSaved saved)
    {
        if (answer is null)
        {
            return;
        }

        if (saved.Message is null)
        {
            Messages.Remove(answer);
        }
        else
        {
            answer.ApplySaved(saved.Message);
            if (saved.Message.ContextTokens is int used)
            {
                ContextUsed = used;
            }
        }

        if (saved.Cancelled)
        {
            ErrorText = "Abgebrochen.";
        }
    }

    private void ShowContextPlan(ContextPlan plan)
    {
        ContextUsed = plan.EstimatedTokens;
        ContextHint = plan.WasTrimmed
            ? $"Der Kontext ist voll: Die ältesten {plan.DroppedMessages} Nachrichten gingen nicht mehr ans Modell. Der Systemprompt bleibt immer drin."
            : "";
    }

    private async Task CreateTitleAsync(ChatRecord chat, KiProfile ki, string question, string answer)
    {
        using var cts = new CancellationTokenSource();
        _titleGeneration = cts;
        try
        {
            var updated = await _chats.CreateTitleAsync(chat, ki, question, answer, cts.Token);
            if (Chat?.Id == updated.Id)
            {
                Chat = updated;
            }

            ChatSaved?.Invoke(this, updated);
        }
        catch (OperationCanceledException)
        {
            // Eine neue Frage hat Vorrang (Lektion 6). Der Titel bleibt vorerst "Neuer Chat".
        }
        finally
        {
            _titleGeneration = null;
        }
    }

    private void CancelTitleGeneration() => _titleGeneration?.Cancel();

    private IProgress<string> Progress() => new Progress<string>(message => StatusText = message);

    // ------------------------------------------------------------------ Hilfen

    private void RemoveFromLastUser()
    {
        var lastUser = Messages.LastOrDefault(m => m.IsUser);
        if (lastUser is null)
        {
            return;
        }

        var index = Messages.IndexOf(lastUser);
        while (Messages.Count > index)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }
    }

    private void UpdateLastFlags()
    {
        var lastUser = Messages.LastOrDefault(m => m.IsUser);
        var lastAssistant = Messages.LastOrDefault(m => m.IsAssistant);
        foreach (var message in Messages)
        {
            message.IsLastUser = message == lastUser;
            message.IsLastAssistant = message == lastAssistant && Messages[^1] == lastAssistant;
        }

        RegenerateCommand.NotifyCanExecuteChanged();
        EditLastCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Belegter Kontext: der echte Wert der Engine nach der letzten Antwort.
    /// Gibt es den nicht (z.B. abgebrochen), eine Schätzung aus Systemprompt und Verlauf.
    /// </summary>
    private void UpdateContextGauge()
    {
        ContextTotal = Ki?.Parameters.ContextLength ?? 8192;
        if (Messages.Count == 0)
        {
            ContextUsed = 0;
        }
        else if (Messages[^1] is { IsAssistant: true, ContextTokens: int known })
        {
            ContextUsed = known;
        }
        else
        {
            var system = string.IsNullOrWhiteSpace(Ki?.SystemPrompt) ? 0 : ContextBudget.Estimate(new ChatMessage(ChatRole.System, Ki.SystemPrompt));
            ContextUsed = system + Messages.Sum(m => ContextBudget.Estimate(new ChatMessage(m.Role, m.Content, m.Images)));
        }
    }

    private async Task UpdateImageSupportAsync(KiProfile? ki)
    {
        var supported = ki is not null && await _engines.SupportsImagesAsync(ki, CancellationToken.None);
        if (Ki != ki)
        {
            return;
        }

        CanAttachImages = supported;
        AttachHint = supported
            ? "Bild anhängen (auch per Drag & Drop)"
            : "Diese KI versteht keine Bilder. Wähle z.B. Mael-KI.";
    }

    private void ResetTransientState()
    {
        ErrorText = "";
        StatusText = "";
        ContextHint = "";
        IsEditingLast = false;
        PendingImages.Clear();
    }

    private string? KiName(string? kiId) => kiId is null ? null : _kis.Find(kiId)?.DisplayName ?? kiId;

    private async Task RememberLastChatAsync(long chatId)
    {
        if (_settings.Current.Chat.LastChatId == chatId)
        {
            return;
        }

        var settings = JsonDefaults.Clone(_settings.Current);
        settings.Chat.LastChatId = chatId;
        await _settings.SaveAsync(settings, CancellationToken.None);
    }
}
