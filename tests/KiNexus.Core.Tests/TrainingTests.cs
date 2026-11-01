using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.Training;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class TrainingDatasetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{"messages":[{"role":"user","content":"Wer bist du?"},{"role":"assistant","content":"Mael-KI."}]}""", "Wer bist du?", "Mael-KI.")]
    [InlineData("""{"frage":"Wer bist du?","antwort":"Mael-KI."}""", "Wer bist du?", "Mael-KI.")]
    [InlineData("""{"question":"Wer bist du?","answer":"Mael-KI."}""", "Wer bist du?", "Mael-KI.")]
    [InlineData("""{"prompt":"Wer bist du?","completion":"Mael-KI."}""", "Wer bist du?", "Mael-KI.")]
    [InlineData("""{"instruction":"Übersetze.","input":"Hello","output":"Hallo"}""", "Übersetze.\n\nHello", "Hallo")]
    public void ParseLine_KnowsCommonFormats(string line, string question, string answer)
    {
        var example = TrainingDataset.ParseLine(line);

        Assert.NotNull(example);
        Assert.Equal(question, example.Question);
        Assert.Equal(answer, example.Answer);
    }

    [Fact]
    public void ParseLine_MultiTurn_TakesLastAnswerAndQuestionBefore()
    {
        var example = TrainingDataset.ParseLine("""
            {"messages":[{"role":"system","content":"Sei kurz."},{"role":"user","content":"A?"},{"role":"assistant","content":"a"},
                         {"role":"user","content":"B?"},{"role":"assistant","content":"b"}]}
            """.Replace("\n", "", StringComparison.Ordinal));

        Assert.Equal(new TrainingExample("B?", "b", "Sei kurz."), example);
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("""{"frage":"Nur eine Frage"}""")]
    [InlineData("""{"messages":[{"role":"user","content":"Hallo"}]}""")]
    [InlineData("[1,2]")]
    public void ParseLine_InvalidLines_AreNull(string line) => Assert.Null(TrainingDataset.ParseLine(line));

    [Fact]
    public void Parse_CountsSkippedLines()
    {
        var import = TrainingDataset.Parse(["""{"frage":"A?","antwort":"a"}""", "", "kaputt", """{"frage":"B?","antwort":"b"}"""]);

        Assert.Equal(2, import.Examples.Count);
        Assert.Equal(1, import.Skipped);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_KeepsUmlautsReadable()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("training", "datensatz.jsonl");
        TrainingExample[] examples = [new("Grüezi, wie geht's?", "Gut, merci!"), new("Wer bist du?", "Mael-KI.", "Antworte auf Schweizerdeutsch.")];

        await TrainingDataset.SaveAsync(path, examples, Ct);
        var loaded = await TrainingDataset.LoadAsync(path, Ct);

        Assert.Equal(examples, loaded);
        var firstLine = (await File.ReadAllLinesAsync(path, Ct))[0];
        Assert.Contains("Grüezi", firstLine, StringComparison.Ordinal);
        Assert.StartsWith("""{"messages":[{"role":"user","content":""", firstLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_MissingFile_IsEmpty()
    {
        using var temp = new TempDirectory();

        Assert.Empty(await TrainingDataset.LoadAsync(temp.Combine("gibt-es-nicht.jsonl"), Ct));
    }

    [Fact]
    public async Task LikedAnswers_ComeWithTheQuestionBefore()
    {
        using var temp = new TempDirectory();
        var repo = new ChatRepository(temp.Combine("chats.db"), NullLogger<ChatRepository>.Instance);
        await repo.InitializeAsync(Ct);
        var chat = await repo.CreateChatAsync("mael-ki", thinkEnabled: false, Ct);
        await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "Was ist 2+2?" }, Ct);
        var good = await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.Assistant, Content = "4." }, Ct);
        await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.User, Content = "Und 3+3?" }, Ct);
        var bad = await repo.AddMessageAsync(new MessageRecord { ChatId = chat.Id, Role = ChatRole.Assistant, Content = "7." }, Ct);
        await repo.SetRatingAsync(good.Id, 1, Ct);
        await repo.SetRatingAsync(bad.Id, -1, Ct);

        var liked = await repo.GetLikedExamplesAsync(Ct);

        Assert.Equal([new TrainingExample("Was ist 2+2?", "4.")], liked);
        SqliteConnection.ClearAllPools();
    }
}

public class TrainingModelsTests
{
    [Theory]
    [InlineData("Qwen/Qwen3-1.7B", "qwen3-1.7b")]
    [InlineData("HuggingFaceTB/SmolLM3-3B", "smollm3-3b")]
    [InlineData("org/Mein_Modell", "mein-modell")]
    public void Slug_IsAFolderName(string repo, string expected) => Assert.Equal(expected, TrainingModels.Slug(repo));

    [Theory]
    [InlineData("Qwen/Qwen3-1.7B", true)]
    [InlineData("Qwen3-1.7B", false)]
    [InlineData("a/b/c", false)]
    [InlineData("Qwen/Qwen 3", false)]
    [InlineData(null, false)]
    public void IsValidRepo(string? repo, bool expected) => Assert.Equal(expected, TrainingModels.IsValidRepo(repo));

    [Fact]
    public void Recommended_FitIntoEightGigabytes()
    {
        Assert.All(TrainingModels.Recommended, m => Assert.True(TrainingModels.EstimateVramGb(m.ParametersB, m.Use4Bit) <= 7.5, m.Repo));
        Assert.Equal("Qwen/Qwen3-1.7B", TrainingModels.Recommended[0].Repo);
        Assert.True(TrainingModels.EstimateVramGb(9, use4Bit: false) > 8);
    }
}
