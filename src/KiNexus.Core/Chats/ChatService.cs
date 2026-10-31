using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Chats;

/// <summary>Was während einer Frage passiert. Die Oberfläche zeigt es Schritt für Schritt an.</summary>
public abstract record ChatTurnEvent;

/// <summary>Die Frage ist gespeichert (mit Id und kopierten Bildern).</summary>
public sealed record UserMessageSaved(MessageRecord Message) : ChatTurnEvent;

/// <summary>Der Kontext ist geprüft. Wurde gekürzt, sagt WasTrimmed es.</summary>
public sealed record ContextPrepared(ContextPlan Plan) : ChatTurnEvent;

/// <summary>Ein neues Stück der Antwort.</summary>
public sealed record AnswerChunk(ChatChunk Chunk) : ChatTurnEvent;

/// <summary>Die Antwort ist gespeichert. Beim Abbrechen ohne Text ist Message null.</summary>
public sealed record AnswerSaved(MessageRecord? Message, bool Cancelled) : ChatTurnEvent;

/// <summary>
/// Steuert eine Frage von Anfang bis Ende: speichern, Kontext prüfen, Antwort streamen, Antwort speichern.
/// An das Modell geht nur: Systemprompt, Verlauf und die neue Nachricht (Lektion 5).
/// </summary>
public sealed class ChatService(
    IChatRepository repository,
    EngineManager engines,
    AttachmentStore attachments,
    ISettingsService settings,
    ILogger<ChatService> logger)
{
    private const int TitleMaxTokens = 24;

    /// <summary>Gelerntes Verhältnis Zeichen pro Token, pro Chat. Wird nach jeder Antwort genauer.</summary>
    private readonly ConcurrentDictionary<long, double> _charsPerToken = new();

    public Task<ChatRecord> CreateChatAsync(KiProfile? ki, bool think, CancellationToken ct) =>
        repository.CreateChatAsync(ki?.Id, think, ct);

    public Task<IReadOnlyList<ChatRecord>> ListChatsAsync(string? search, CancellationToken ct) => repository.ListChatsAsync(search, ct);

    public Task<ChatRecord?> GetChatAsync(long chatId, CancellationToken ct) => repository.GetChatAsync(chatId, ct);

    public Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(long chatId, CancellationToken ct) => repository.GetMessagesAsync(chatId, ct);

    public Task SetRatingAsync(long messageId, int? rating, CancellationToken ct) => repository.SetRatingAsync(messageId, rating, ct);

    public async Task<ChatRecord> UpdateChatAsync(ChatRecord chat, CancellationToken ct)
    {
        await repository.UpdateChatAsync(chat, ct).ConfigureAwait(false);
        return chat;
    }

    public async Task DeleteChatAsync(long chatId, CancellationToken ct)
    {
        await repository.DeleteChatAsync(chatId, ct).ConfigureAwait(false);
        attachments.DeleteChat(chatId);
        _charsPerToken.TryRemove(chatId, out _);
        logger.LogInformation("Chat {ChatId} gelöscht.", chatId);
    }

    /// <summary>Neue Frage stellen.</summary>
    /// <param name="systemPromptSuffix">Zusatz zum Systemprompt nur für diese Frage, z.B. im Gespräch "Antworte kurz …".</param>
    public async IAsyncEnumerable<ChatTurnEvent> SendAsync(
        ChatRecord chat, KiProfile ki, string text, IReadOnlyList<string> imageFiles, bool think,
        IProgress<string>? progress, [EnumeratorCancellation] CancellationToken ct, string? systemPromptSuffix = null)
    {
        var history = await HistoryAsync(chat.Id, ct).ConfigureAwait(false);
        var stored = imageFiles.Select(file => attachments.Store(chat.Id, file)).ToList();
        var user = await repository.AddMessageAsync(new MessageRecord
        {
            ChatId = chat.Id,
            Role = ChatRole.User,
            Content = text.Trim(),
            KiId = ki.Id,
            AttachmentPaths = stored,
        }, ct).ConfigureAwait(false);
        yield return new UserMessageSaved(user);

        await foreach (var turnEvent in AnswerAsync(chat, ki, history, user, think, progress, ct, systemPromptSuffix).ConfigureAwait(false))
        {
            yield return turnEvent;
        }
    }

    /// <summary>Die letzte Antwort neu erzeugen (Knopf "neu erzeugen").</summary>
    public async IAsyncEnumerable<ChatTurnEvent> RegenerateAsync(
        ChatRecord chat, KiProfile ki, bool think, IProgress<string>? progress, [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = await repository.GetMessagesAsync(chat.Id, ct).ConfigureAwait(false);
        var lastUserIndex = LastUserIndex(messages);
        if (lastUserIndex < 0)
        {
            throw new InferenceException("Es gibt noch keine Frage, die man neu beantworten könnte.");
        }

        if (lastUserIndex + 1 < messages.Count)
        {
            await repository.DeleteMessagesFromAsync(chat.Id, messages[lastUserIndex + 1].Id, ct).ConfigureAwait(false);
        }

        var history = messages.Take(lastUserIndex).Select(m => m.ToChatMessage()).ToList();
        await foreach (var turnEvent in AnswerAsync(chat, ki, history, messages[lastUserIndex], think, progress, ct).ConfigureAwait(false))
        {
            yield return turnEvent;
        }
    }

    /// <summary>Die letzte eigene Nachricht ändern und neu senden. Die alte Frage und ihre Antwort fallen weg.</summary>
    public async IAsyncEnumerable<ChatTurnEvent> EditLastUserMessageAsync(
        ChatRecord chat, KiProfile ki, string newText, bool think, IProgress<string>? progress, [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = await repository.GetMessagesAsync(chat.Id, ct).ConfigureAwait(false);
        var lastUserIndex = LastUserIndex(messages);
        if (lastUserIndex < 0)
        {
            throw new InferenceException("Es gibt noch keine eigene Nachricht zum Bearbeiten.");
        }

        var images = messages[lastUserIndex].AttachmentPaths;
        await repository.DeleteMessagesFromAsync(chat.Id, messages[lastUserIndex].Id, ct).ConfigureAwait(false);
        await foreach (var turnEvent in SendAsync(chat, ki, newText, images, think, progress, ct).ConfigureAwait(false))
        {
            yield return turnEvent;
        }
    }

    /// <summary>
    /// Titel nach der ersten Antwort. Mit Modell: ohne Thinking, sehr kurz, abbrechbar (niedrige Priorität).
    /// Ist die Titel-Erzeugung aus oder klappt sie nicht: die ersten Wörter der Frage.
    /// </summary>
    public async Task<ChatRecord> CreateTitleAsync(ChatRecord chat, KiProfile ki, string question, string answer, CancellationToken ct)
    {
        string? title = null;
        if (settings.Current.BackgroundTasks.GenerateChatTitles)
        {
            title = await GenerateTitleAsync(ki, question, answer, ct).ConfigureAwait(false);
        }

        var updated = chat with { Title = title ?? ChatTitles.FromFirstWords(question) };
        await repository.UpdateChatAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    private async Task<string?> GenerateTitleAsync(KiProfile ki, string question, string answer, CancellationToken ct)
    {
        var request = new ChatRequest(ki, ChatTitles.BuildRequest(question, answer), Think: false, MaxTokens: TitleMaxTokens);
        var text = new StringBuilder();
        try
        {
            await foreach (var chunk in engines.StreamAsync(request, null, ct).ConfigureAwait(false))
            {
                text.Append(chunk.Text);
            }
        }
        catch (InferenceException ex)
        {
            logger.LogWarning(ex, "Titel konnte nicht erzeugt werden. Es gelten die ersten Wörter der Frage.");
            return null;
        }

        return ChatTitles.Clean(text.ToString());
    }

    private async IAsyncEnumerable<ChatTurnEvent> AnswerAsync(
        ChatRecord chat, KiProfile ki, IReadOnlyList<ChatMessage> history, MessageRecord question,
        bool think, IProgress<string>? progress, [EnumeratorCancellation] CancellationToken ct, string? systemPromptSuffix = null)
    {
        var p = ki.Parameters;
        var plan = ContextBudget.Fit(WithSuffix(ki.SystemPrompt, systemPromptSuffix), history, question.ToChatMessage(), p.ContextLength,
            ContextBudget.ReserveForAnswer(p.ContextLength, p.MaxTokens), CharsPerToken(chat.Id));
        if (plan.WasTrimmed)
        {
            logger.LogInformation("Kontext voll: {Dropped} ältere Nachrichten im Chat {ChatId} weggelassen.", plan.DroppedMessages, chat.Id);
        }

        yield return new ContextPrepared(plan);

        var answer = new StringBuilder();
        var thinking = new StringBuilder();
        var thinkingTimer = new Stopwatch();
        long? thinkingMs = null;
        ChatStats? stats = null;
        var cancelled = false;

        await using (var stream = engines.StreamAsync(new ChatRequest(ki, plan.Messages, think), progress, ct).GetAsyncEnumerator(ct))
        {
            while (true)
            {
                try
                {
                    if (!await stream.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopp-Knopf, Esc, Strg + C oder "Entladen": Was schon da ist, wird gespeichert.
                    cancelled = true;
                    break;
                }

                var chunk = stream.Current;
                if (chunk.Thinking.Length > 0)
                {
                    thinkingTimer.Start();
                    thinking.Append(chunk.Thinking);
                }

                if (chunk.Text.Length > 0)
                {
                    thinkingMs ??= thinkingTimer.IsRunning ? thinkingTimer.ElapsedMilliseconds : null;
                    answer.Append(chunk.Text);
                }

                stats = chunk.Stats ?? stats;
                yield return new AnswerChunk(chunk);
            }
        }

        thinkingMs ??= thinkingTimer.IsRunning ? thinkingTimer.ElapsedMilliseconds : null;
        if (answer.Length == 0 && thinking.Length == 0)
        {
            yield return new AnswerSaved(null, cancelled);
            yield break;
        }

        var saved = await repository.AddMessageAsync(new MessageRecord
        {
            ChatId = chat.Id,
            Role = ChatRole.Assistant,
            Content = answer.ToString(),
            Thinking = thinking.ToString(),
            ThinkingMs = thinkingMs,
            KiId = ki.Id,
            Engine = (stats?.Engine ?? ki.Engine) == EngineKind.Ollama ? "ollama" : "llamacpp",
            PromptTokens = stats?.PromptTokens,
            CompletionTokens = stats?.CompletionTokens,
            TokensPerSecond = stats?.TokensPerSecond,
            DurationMs = stats is null ? null : (long)stats.Duration.TotalMilliseconds,
            ContextTokens = stats?.ContextTokens is > 0 ? stats.ContextTokens : null,
        }, CancellationToken.None).ConfigureAwait(false);

        LearnCharsPerToken(chat.Id, plan, saved);
        yield return new AnswerSaved(saved, cancelled);
    }

    /// <summary>Systemprompt plus Zusatz, durch eine Leerzeile getrennt.</summary>
    internal static string WithSuffix(string systemPrompt, string? suffix) =>
        string.IsNullOrWhiteSpace(suffix) ? systemPrompt
        : string.IsNullOrWhiteSpace(systemPrompt) ? suffix.Trim()
        : systemPrompt.TrimEnd() + "\n\n" + suffix.Trim();

    private async Task<List<ChatMessage>> HistoryAsync(long chatId, CancellationToken ct) =>
        (await repository.GetMessagesAsync(chatId, ct).ConfigureAwait(false)).Select(m => m.ToChatMessage()).ToList();

    private static int LastUserIndex(IReadOnlyList<MessageRecord> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User)
            {
                return i;
            }
        }

        return -1;
    }

    private double CharsPerToken(long chatId) =>
        _charsPerToken.TryGetValue(chatId, out var learned) ? learned : ContextBudget.DefaultCharsPerToken;

    /// <summary>
    /// Aus der echten Token-Zahl der Engine lernen, wie viele Zeichen ein Token in diesem Chat hat.
    /// 10 % Sicherheitsabstand, damit die Schätzung lieber zu hoch als zu tief liegt.
    /// </summary>
    private void LearnCharsPerToken(long chatId, ContextPlan plan, MessageRecord answer)
    {
        if (answer.ContextTokens is not int contextTokens)
        {
            return;
        }

        var images = plan.Messages.Sum(m => m.ImagePaths?.Count ?? 0);
        var textTokens = contextTokens - images * ContextBudget.TokensPerImage - (plan.Messages.Count + 1) * ContextBudget.TokensPerMessage;
        var characters = plan.Messages.Sum(m => m.Content.Length) + answer.Content.Length;
        if (textTokens > 50 && characters > 200)
        {
            _charsPerToken[chatId] = Math.Clamp(characters / (double)textTokens * 0.9, 2.0, 6.0);
        }
    }
}
