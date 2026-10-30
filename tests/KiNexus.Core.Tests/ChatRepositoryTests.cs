using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class ChatRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_CreatesTables_AndSetsVersion()
    {
        using var temp = new TempDirectory();
        var dbPath = temp.Combine("chats.db");

        await CreateRepository(dbPath).InitializeAsync(Ct);
        await CreateRepository(dbPath).InitializeAsync(Ct); // Zweimal darf nichts kaputtmachen.

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal((long)ChatRepository.LatestVersion, await command.ExecuteScalarAsync(Ct));
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('chats', 'messages', 'system_prompt_versions');";
        Assert.Equal(3L, await command.ExecuteScalarAsync(Ct));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Messages_RoundTrip_WithAllFields()
    {
        using var temp = new TempDirectory();
        var repo = await CreateInitializedAsync(temp);
        var chat = await repo.CreateChatAsync("mael-ki", thinkEnabled: true, Ct);

        await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "Was ist das?", AttachmentPaths = [@"C:\bild.png"] }, Ct);
        var saved = await repo.AddMessageAsync(new MessageRecord
        {
            ChatId = chat.Id, Role = ChatRole.Assistant, Content = "Ein Bild.", Thinking = "Hm…", ThinkingMs = 1200,
            KiId = "mael-ki", Engine = "llamacpp", PromptTokens = 300, CompletionTokens = 4, TokensPerSecond = 38.5,
            DurationMs = 900, ContextTokens = 900,
        }, Ct);
        var messages = await repo.GetMessagesAsync(chat.Id, Ct);

        Assert.Equal(2, messages.Count);
        Assert.Equal([@"C:\bild.png"], messages[0].AttachmentPaths);
        var answer = messages[1];
        Assert.Equal(saved.Id, answer.Id);
        Assert.Equal(ChatRole.Assistant, answer.Role);
        Assert.Equal("Hm…", answer.Thinking);
        Assert.Equal(1200, answer.ThinkingMs);
        Assert.Equal(38.5, answer.TokensPerSecond);
        Assert.Equal(900, answer.ContextTokens);
        Assert.True((await repo.GetChatAsync(chat.Id, Ct))!.ThinkEnabled);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task List_NewestFirst_AndSearchFindsTitleOrContent()
    {
        using var temp = new TempDirectory();
        var repo = await CreateInitializedAsync(temp);
        var older = await repo.CreateChatAsync(null, false, Ct);
        await repo.UpdateChatAsync(older with { Title = "Rezepte für Rösti" }, Ct);
        var newer = await repo.CreateChatAsync(null, false, Ct);
        await repo.AddMessageAsync(new MessageRecord { ChatId = newer.Id, Role = ChatRole.User, Content = "Erkläre 100% Hardlinks" }, Ct);

        var all = await repo.ListChatsAsync(null, Ct);
        var byTitle = await repo.ListChatsAsync("Rösti", Ct);
        var byContent = await repo.ListChatsAsync("hardlink", Ct);
        var withPercent = await repo.ListChatsAsync("100%", Ct);

        Assert.Equal([newer.Id, older.Id], all.Select(c => c.Id));
        Assert.Equal(older.Id, Assert.Single(byTitle).Id);
        Assert.Equal(newer.Id, Assert.Single(byContent).Id);
        Assert.Equal(newer.Id, Assert.Single(withPercent).Id);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task DeleteChat_RemovesMessagesToo()
    {
        using var temp = new TempDirectory();
        var repo = await CreateInitializedAsync(temp);
        var chat = await repo.CreateChatAsync(null, false, Ct);
        await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "Hi" }, Ct);

        await repo.DeleteChatAsync(chat.Id, Ct);

        Assert.Null(await repo.GetChatAsync(chat.Id, Ct));
        Assert.Empty(await repo.GetMessagesAsync(chat.Id, Ct));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task DeleteMessagesFrom_KeepsEarlierMessages()
    {
        using var temp = new TempDirectory();
        var repo = await CreateInitializedAsync(temp);
        var chat = await repo.CreateChatAsync(null, false, Ct);
        var first = await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "1" }, Ct);
        var second = await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.Assistant, Content = "2" }, Ct);
        await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "3" }, Ct);

        await repo.DeleteMessagesFromAsync(chat.Id, second.Id, Ct);

        Assert.Equal([first.Id], (await repo.GetMessagesAsync(chat.Id, Ct)).Select(m => m.Id));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Rating_CanBeSetAndCleared()
    {
        using var temp = new TempDirectory();
        var repo = await CreateInitializedAsync(temp);
        var chat = await repo.CreateChatAsync(null, false, Ct);
        var answer = await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.Assistant, Content = "A" }, Ct);

        await repo.SetRatingAsync(answer.Id, 1, Ct);
        Assert.Equal(1, (await repo.GetMessagesAsync(chat.Id, Ct))[0].Rating);

        await repo.SetRatingAsync(answer.Id, null, Ct);
        Assert.Null((await repo.GetMessagesAsync(chat.Id, Ct))[0].Rating);
        SqliteConnection.ClearAllPools();
    }

    private static ChatRepository CreateRepository(string path) => new(path, NullLogger<ChatRepository>.Instance);

    private static async Task<ChatRepository> CreateInitializedAsync(TempDirectory temp)
    {
        var repo = CreateRepository(temp.Combine("chats.db"));
        await repo.InitializeAsync(Ct);
        return repo;
    }
}
