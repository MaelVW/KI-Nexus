using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KiNexus.Core.Tests;

public sealed class ChatServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeBackend _backend = new(EngineKind.LlamaCpp);
    private readonly ChatRepository _repository;
    private readonly SettingsService _settings;
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _repository = new ChatRepository(_temp.Combine("chats.db"), NullLogger<ChatRepository>.Instance);
        _settings = new SettingsService(_temp.Combine("settings.json"), NullLogger<SettingsService>.Instance);
        var engines = new EngineManager([_backend, new FakeBackend(EngineKind.Ollama)], NullLogger<EngineManager>.Instance, new FakeTimeProvider());
        _service = new ChatService(_repository, engines, new AttachmentStore(_temp.Combine("attachments")), _settings, NullLogger<ChatService>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KiProfile Ki => new() { Id = "test", DisplayName = "Test", SystemPrompt = "Sei nett." };

    [Fact]
    public async Task Send_SavesQuestionAndAnswer_WithStats()
    {
        var chat = await NewChatAsync();

        var events = await RunAsync(_service.SendAsync(chat, Ki, "Hallo", [], think: false, null, Ct));

        Assert.IsType<UserMessageSaved>(events[0]);
        Assert.IsType<ContextPrepared>(events[1]);
        var saved = Assert.IsType<AnswerSaved>(events[^1]);
        Assert.False(saved.Cancelled);
        Assert.Equal("Hallo zurück", saved.Message!.Content);
        Assert.Equal(400, saved.Message.ContextTokens);
        Assert.Equal("llamacpp", saved.Message.Engine);

        var messages = await _repository.GetMessagesAsync(chat.Id, Ct);
        Assert.Equal([ChatRole.User, ChatRole.Assistant], messages.Select(m => m.Role));
    }

    [Fact]
    public async Task Send_SendsOnlySystemPromptHistoryAndQuestion()
    {
        var chat = await NewChatAsync();
        await RunAsync(_service.SendAsync(chat, Ki, "Erste Frage", [], false, null, Ct));

        await RunAsync(_service.SendAsync(chat, Ki, "Zweite Frage", [], true, null, Ct));

        var request = _backend.Requests[^1];
        Assert.True(request.Think);
        Assert.Equal(["Sei nett.", "Erste Frage", "Hallo zurück", "Zweite Frage"], request.Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task Send_CopiesImages_AndSendsThemToTheModel()
    {
        var chat = await NewChatAsync();
        var image = _temp.Combine("katze.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3], Ct);

        var events = await RunAsync(_service.SendAsync(chat, Ki, "Was siehst du?", [image], false, null, Ct));

        var user = Assert.IsType<UserMessageSaved>(events[0]).Message;
        var stored = Assert.Single(user.AttachmentPaths);
        Assert.StartsWith(_temp.Combine("attachments"), stored);
        Assert.Equal([stored], _backend.Requests[^1].Messages[^1].ImagePaths);
    }

    [Fact]
    public async Task Cancel_SavesPartialAnswer()
    {
        var chat = await NewChatAsync();
        _backend.ChunkDelay = TimeSpan.FromSeconds(30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var events = new List<ChatTurnEvent>();

        await foreach (var turnEvent in _service.SendAsync(chat, Ki, "Hallo", [], false, null, cts.Token))
        {
            events.Add(turnEvent);
            if (turnEvent is AnswerChunk)
            {
                await cts.CancelAsync();
            }
        }

        var saved = Assert.IsType<AnswerSaved>(events[^1]);
        Assert.True(saved.Cancelled);
        Assert.Equal("Hallo", saved.Message!.Content);
    }

    [Fact]
    public async Task Regenerate_ReplacesLastAnswer()
    {
        var chat = await NewChatAsync();
        await RunAsync(_service.SendAsync(chat, Ki, "Frage", [], false, null, Ct));
        _backend.Script = [ChatChunk.FromText("Neue Antwort")];

        await RunAsync(_service.RegenerateAsync(chat, Ki, false, null, Ct));

        var messages = await _repository.GetMessagesAsync(chat.Id, Ct);
        Assert.Equal(["Frage", "Neue Antwort"], messages.Select(m => m.Content));
        Assert.Equal(["Sei nett.", "Frage"], _backend.Requests[^1].Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task EditLastUserMessage_ReplacesQuestionAndAnswer()
    {
        var chat = await NewChatAsync();
        await RunAsync(_service.SendAsync(chat, Ki, "Frage mit Tippfeler", [], false, null, Ct));

        await RunAsync(_service.EditLastUserMessageAsync(chat, Ki, "Frage ohne Tippfehler", false, null, Ct));

        var messages = await _repository.GetMessagesAsync(chat.Id, Ct);
        Assert.Equal(["Frage ohne Tippfehler", "Hallo zurück"], messages.Select(m => m.Content));
    }

    [Fact]
    public async Task CreateTitle_UsesModel_WithoutThinkingAndShort()
    {
        var chat = await NewChatAsync();
        _backend.Script = [ChatChunk.FromText("\"Hardlinks erklärt\"")];

        var updated = await _service.CreateTitleAsync(chat, Ki, "Was ist ein Hardlink?", "Ein zweiter Name.", Ct);

        Assert.Equal("Hardlinks erklärt", updated.Title);
        var request = _backend.Requests[^1];
        Assert.False(request.Think);
        Assert.True(request.EffectiveMaxTokens <= 32);
        Assert.DoesNotContain(request.Messages, m => m.Role == ChatRole.System);
        Assert.Equal("Hardlinks erklärt", (await _repository.GetChatAsync(chat.Id, Ct))!.Title);
    }

    [Fact]
    public async Task CreateTitle_Disabled_UsesFirstWords_WithoutModel()
    {
        var chat = await NewChatAsync();
        var settings = new AppSettings();
        settings.BackgroundTasks.GenerateChatTitles = false;
        await _settings.SaveAsync(settings, Ct);

        var updated = await _service.CreateTitleAsync(chat, Ki, "Was ist ein Hardlink?", "…", Ct);

        Assert.Equal("Was ist ein Hardlink?", updated.Title);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task DeleteChat_RemovesAttachments()
    {
        var chat = await NewChatAsync();
        var image = _temp.Combine("a.png");
        await File.WriteAllBytesAsync(image, [1], Ct);
        await RunAsync(_service.SendAsync(chat, Ki, "Bild", [image], false, null, Ct));

        await _service.DeleteChatAsync(chat.Id, Ct);

        Assert.False(Directory.Exists(_temp.Combine("attachments", chat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Null(await _service.GetChatAsync(chat.Id, Ct));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private async Task<ChatRecord> NewChatAsync()
    {
        await _repository.InitializeAsync(Ct);
        return await _service.CreateChatAsync(Ki, false, Ct);
    }

    private static async Task<List<ChatTurnEvent>> RunAsync(IAsyncEnumerable<ChatTurnEvent> events)
    {
        var list = new List<ChatTurnEvent>();
        await foreach (var turnEvent in events)
        {
            list.Add(turnEvent);
        }

        return list;
    }
}
