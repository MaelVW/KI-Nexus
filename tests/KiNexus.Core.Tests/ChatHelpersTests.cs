using KiNexus.Core.Chats;
using KiNexus.Core.Inference;

namespace KiNexus.Core.Tests;

public class ContextBudgetTests
{
    [Fact]
    public void Fit_KeepsEverything_WhenThereIsRoom()
    {
        var history = Conversation(4, chars: 100);

        var plan = ContextBudget.Fit("System", history, User("Neu"), contextLength: 8192, reserveForAnswer: 2048);

        Assert.Equal(6, plan.Messages.Count);
        Assert.Equal(ChatRole.System, plan.Messages[0].Role);
        Assert.Equal("Neu", plan.Messages[^1].Content);
        Assert.False(plan.WasTrimmed);
    }

    [Fact]
    public void Fit_DropsOldestMessages_AndKeepsSystemPrompt()
    {
        // 10 Nachrichten zu je ca. 320 Token passen nicht in 2048 - 512.
        var history = Conversation(10, chars: 1000);

        var plan = ContextBudget.Fit("Du bist nett.", history, User("Neu"), contextLength: 2048, reserveForAnswer: 512);

        Assert.True(plan.WasTrimmed);
        Assert.Equal("Du bist nett.", plan.Messages[0].Content);
        Assert.Equal(ChatRole.User, plan.Messages[1].Role);
        Assert.Equal("Neu", plan.Messages[^1].Content);
        Assert.True(plan.EstimatedTokens <= 2048 - 512);
        Assert.Equal(10 - (plan.Messages.Count - 2), plan.DroppedMessages);
    }

    [Fact]
    public void Fit_HistoryNeverStartsWithAnswer()
    {
        var history = Conversation(6, chars: 1000);

        var plan = ContextBudget.Fit(null, history, User("Neu"), contextLength: 1400, reserveForAnswer: 300);

        Assert.Equal(ChatRole.User, plan.Messages[0].Role);
    }

    [Fact]
    public void Fit_Throws_WhenNewMessageAloneIsTooLong()
    {
        var huge = User(new string('x', 40_000));

        Assert.Throws<InferenceException>(() => ContextBudget.Fit(null, [], huge, contextLength: 8192, reserveForAnswer: 2048));
    }

    [Fact]
    public void Estimate_CountsImages()
    {
        var withImage = new ChatMessage(ChatRole.User, "", [@"C:\bild.png"]);

        Assert.Equal(ContextBudget.TokensPerMessage + ContextBudget.TokensPerImage, ContextBudget.Estimate(withImage));
    }

    [Theory]
    [InlineData(8192, 2048, 2048)]
    [InlineData(2048, 2048, 1024)]
    public void ReserveForAnswer_IsAtMostHalfTheContext(int context, int maxTokens, int expected)
    {
        Assert.Equal(expected, ContextBudget.ReserveForAnswer(context, maxTokens));
    }

    private static ChatMessage User(string text) => new(ChatRole.User, text);

    private static List<ChatMessage> Conversation(int count, int chars) =>
        Enumerable.Range(0, count)
            .Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string('a', chars)))
            .ToList();
}

public class TerminalCommandsTests
{
    [Theory]
    [InlineData("/help", TerminalCommandKind.Help, "")]
    [InlineData("/think on", TerminalCommandKind.ThinkOn, "")]
    [InlineData("/THINK off", TerminalCommandKind.ThinkOff, "")]
    [InlineData("/think show", TerminalCommandKind.ThinkShow, "")]
    [InlineData("/think hide", TerminalCommandKind.ThinkHide, "")]
    [InlineData("/ki Qwen schnell", TerminalCommandKind.SelectKi, "Qwen schnell")]
    [InlineData("/style simpel", TerminalCommandKind.Style, "simple")]
    [InlineData("/style gespraech", TerminalCommandKind.Style, "conversation")]
    [InlineData("/style gespräch", TerminalCommandKind.Style, "conversation")]
    [InlineData("/new", TerminalCommandKind.NewChat, "")]
    [InlineData("/clear", TerminalCommandKind.Clear, "")]
    [InlineData("/stop", TerminalCommandKind.Stop, "")]
    [InlineData("/stats", TerminalCommandKind.Stats, "")]
    [InlineData("  /unload  ", TerminalCommandKind.Unload, "")]
    public void Parse_KnowsAllCommands(string input, TerminalCommandKind kind, string argument)
    {
        var command = TerminalCommands.Parse(input);

        Assert.Equal(kind, command.Kind);
        Assert.Equal(argument, command.Argument);
    }

    [Theory]
    [InlineData("Hallo, wie geht's?")]
    [InlineData("dir C:\\")]
    [InlineData("")]
    public void Parse_NormalTextIsAQuestion_NeverAShellCommand(string input)
    {
        Assert.Equal(TerminalCommandKind.None, TerminalCommands.Parse(input).Kind);
    }

    [Theory]
    [InlineData("/format c:")]
    [InlineData("/think vielleicht")]
    [InlineData("/ki")]
    [InlineData("/style bunt")]
    public void Parse_UnknownOrWrong_IsInvalidWithMessage(string input)
    {
        var command = TerminalCommands.Parse(input);

        Assert.Equal(TerminalCommandKind.Invalid, command.Kind);
        Assert.NotEmpty(command.Error);
    }

    [Fact]
    public void CompletionsFor_FindsMatchingCommands()
    {
        Assert.Equal(["/think on", "/think off", "/think show", "/think hide"], TerminalCommands.CompletionsFor("/th"));
        Assert.Equal(["/style simpel", "/style gespraech", "/stop", "/stats"], TerminalCommands.CompletionsFor("/st"));
        Assert.Empty(TerminalCommands.CompletionsFor("hallo"));
    }

    [Fact]
    public void Help_ListsEveryCommandFromTheBlueprint()
    {
        Assert.Equal(13, TerminalCommands.Help.Count);
    }
}

public class InputHistoryTests
{
    [Fact]
    public void UpAndDown_WalkThroughEntries_AndRestoreDraft()
    {
        var history = new InputHistory();
        history.Add("eins");
        history.Add("zwei");

        Assert.Equal("zwei", history.Previous("angefangen"));
        Assert.Equal("eins", history.Previous("zwei"));
        Assert.Equal("eins", history.Previous("eins"));
        Assert.Equal("zwei", history.Next());
        Assert.Equal("angefangen", history.Next());
    }

    [Fact]
    public void Add_IgnoresEmptyAndDirectDuplicates()
    {
        var history = new InputHistory();
        history.Add("a");
        history.Add("a");
        history.Add("  ");

        Assert.Single(history.Entries);
    }

    [Fact]
    public void Capacity_DropsOldest()
    {
        var history = new InputHistory(capacity: 2);
        history.Add("1");
        history.Add("2");
        history.Add("3");

        Assert.Equal(["2", "3"], history.Entries);
    }
}

public class ChatTitlesTests
{
    [Theory]
    [InlineData("Was ist ein Hardlink?", "Was ist ein Hardlink?")]
    [InlineData("Erkläre mir bitte ganz genau wie ein Kühlschrank funktioniert", "Erkläre mir bitte ganz genau wie…")]
    [InlineData("   ", "Neuer Chat")]
    public void FromFirstWords_UsesFirstSixWords(string question, string expected)
    {
        Assert.Equal(expected, ChatTitles.FromFirstWords(question));
    }

    [Theory]
    [InlineData("\"Hardlinks unter Windows\"", "Hardlinks unter Windows")]
    [InlineData("Titel: Grosse Strasse.", "Grosse Strasse")]
    [InlineData("**Titel:** Rösti-Rezept\nWeitere Zeile", "Rösti-Rezept")]
    [InlineData("Die große Straße", "Die grosse Strasse")]
    [InlineData("   ", null)]
    public void Clean_TidiesModelOutput(string generated, string? expected)
    {
        Assert.Equal(expected, ChatTitles.Clean(generated));
    }

    [Fact]
    public void BuildRequest_IsShortAndHasNoSystemPrompt()
    {
        var request = ChatTitles.BuildRequest("Frage", new string('a', 2000));

        var message = Assert.Single(request);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.True(message.Content.Length < 700);
    }
}

public class AttachmentStoreTests
{
    [Fact]
    public void Store_CopiesImageIntoChatFolder()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("foto.PNG");
        File.WriteAllBytes(source, [1, 2, 3]);
        var store = new AttachmentStore(temp.Combine("attachments"));

        var stored = store.Store(7, source);

        Assert.StartsWith(temp.Combine("attachments", "7"), stored);
        Assert.EndsWith(".png", stored);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(stored));
        Assert.Equal(stored, store.Store(7, stored));
    }

    [Fact]
    public void Store_RejectsUnsupportedFormats()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("dokument.pdf");
        File.WriteAllText(source, "x");

        Assert.Throws<InvalidDataException>(() => new AttachmentStore(temp.Path).Store(1, source));
    }

    [Fact]
    public void DeleteChat_RemovesFolder()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("a.jpg");
        File.WriteAllBytes(source, [1]);
        var store = new AttachmentStore(temp.Combine("attachments"));
        store.Store(3, source);

        store.DeleteChat(3);

        Assert.False(Directory.Exists(temp.Combine("attachments", "3")));
    }
}
