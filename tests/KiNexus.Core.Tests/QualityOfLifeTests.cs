using KiNexus.Core.Kis;
using KiNexus.Core.Training;
using KiNexus.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

/// <summary>Eigene Prompt-Vorlagen, Privatordner im Studio, eigene Grundmodelle fürs Training (5.11.2026).</summary>
public class QualityOfLifeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----------------------------------------------------------------- Eigene Prompt-Vorlagen

    [Fact]
    public void Templates_SaveReplaceDelete()
    {
        using var root = new TempDirectory();
        var store = new PromptTemplateStore(root.Combine("data", "prompt-vorlagen.json"), NullLogger<PromptTemplateStore>.Instance);

        Assert.Empty(store.Load());
        store.Save("  Bewerbung ", "Du hilfst bei Bewerbungen.\n\n");
        store.Save("Zusammenfassen", "Fasse kurz zusammen.");
        var all = store.Save("bewerbung", "Neu.");

        Assert.Equal(["bewerbung", "Zusammenfassen"], all.Select(t => t.Name));
        Assert.All(all, t => Assert.True(t.IsCustom));
        Assert.Equal("Neu.", all[0].Text);
        Assert.True(store.Exists("BEWERBUNG"));
        Assert.Equal(["Zusammenfassen"], store.Delete("Bewerbung").Select(t => t.Name));
        Assert.Throws<ArgumentException>(() => store.Save(new string('x', 41), "zu lang"));
    }

    [Fact]
    public void Templates_BrokenFileMeansNone()
    {
        using var root = new TempDirectory();
        File.WriteAllText(root.Combine("vorlagen.json"), "{ kaputt");

        Assert.Empty(new PromptTemplateStore(root.Combine("vorlagen.json"), NullLogger<PromptTemplateStore>.Instance).Load());
    }

    // ----------------------------------------------------------------- Privatordner

    [Fact]
    public async Task Images_MoveToPrivateAndBack()
    {
        using var root = new TempDirectory();
        var library = new ImageLibrary(root.Combine("images"), NullLogger<ImageLibrary>.Instance);
        var source = root.Combine("vorlage.png");
        File.WriteAllText(source, "png");
        var raw = root.Combine("roh.png");
        File.WriteAllText(raw, "png");
        var entry = Assert.Single(await library.AddAsync(new ImageDone([raw], 5), new ImageRequest("x", SourceImage: source), ImageModelSet.Flux2Klein4B, Ct));

        var hidden = await library.SetPrivateAsync(entry, true, Ct);

        Assert.True(hidden.IsPrivate);
        Assert.StartsWith(library.PrivateDirectory, hidden.VideoPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(hidden.SourceImagePath));
        Assert.False(File.Exists(entry.VideoPath));
        Assert.Equal(3, library.FilesOf(hidden).Count);
        var loaded = Assert.Single(await library.LoadAllAsync(Ct));
        Assert.True(loaded.IsPrivate);

        var back = await library.SetPrivateAsync(loaded, false, Ct);
        Assert.False(back.IsPrivate);
        Assert.Equal(entry.VideoPath, back.VideoPath);
        Assert.Empty(Directory.EnumerateFiles(library.PrivateDirectory));
    }

    [Fact]
    public async Task Videos_MoveToPrivate_RefusesNameClash()
    {
        using var root = new TempDirectory();
        var library = new VideoLibrary(root.Combine("videos"), NullLogger<VideoLibrary>.Instance);
        Directory.CreateDirectory(root.Combine("roh"));
        File.WriteAllText(root.Combine("roh", "clip_00001_.mp4"), "mp4");
        var entry = await library.AddAsync(new VideoDone(root.Combine("roh", "clip_00001_.mp4"), 1), new VideoRequest("x"), VideoModelSet.Ltx23Distilled, Ct);
        Directory.CreateDirectory(library.PrivateDirectory);
        File.WriteAllText(Path.Combine(library.PrivateDirectory, "clip.json"), "{}");

        await Assert.ThrowsAsync<IOException>(() => library.SetPrivateAsync(entry, true, Ct));
        Assert.True(File.Exists(entry.VideoPath)); // nichts verschoben

        File.Delete(Path.Combine(library.PrivateDirectory, "clip.json"));
        var hidden = await library.SetPrivateAsync(entry, true, Ct);
        Assert.True(Assert.Single(await library.LoadAllAsync(Ct)).IsPrivate);
        Assert.Equal(2, library.FilesOf(hidden).Count); // MP4 und JSON
    }

    // ----------------------------------------------------------------- Eigene Grundmodelle fürs Training

    private static TrainingModelInfo Info(string type = "qwen3", long? parameters = 1_700_000_000, bool template = true, bool gated = false, string[]? bases = null) =>
        new("owner/model", type, parameters, template, gated, "apache-2.0", bases ?? []);

    [Fact]
    public void Evaluate_AcceptsSmallChatModels()
    {
        var (model, note) = CustomTrainingModels.Evaluate(Info());

        Assert.NotNull(model);
        Assert.True(model.IsCustom);
        Assert.False(model.Use4Bit);
        Assert.Equal(1.7, model.ParametersB);
        Assert.Equal("model", model.Name);
        Assert.Contains("Trainierbar", note, StringComparison.Ordinal);
        Assert.True(CustomTrainingModels.Evaluate(Info(parameters: 4_000_000_000)).Model!.Use4Bit);
        Assert.Contains("gated", CustomTrainingModels.Evaluate(Info(gated: true)).Model!.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("qwen3", 8_000_000_000L, true, "nur bis ca. 5B")]
    [InlineData("phi3", 3_800_000_000L, true, "Bauart \"phi3\"")]
    [InlineData("gemma3", 4_000_000_000L, true, "Bauart \"gemma3\"")]
    [InlineData("qwen3", 1_700_000_000L, false, "keine Chat-Vorlage")]
    public void Evaluate_ExplainsWhyNot(string type, long parameters, bool template, string expected)
    {
        var (model, note) = CustomTrainingModels.Evaluate(Info(type, parameters, template));

        Assert.Null(model);
        Assert.Contains(expected, note, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_GgufRepositoryPointsToOriginal()
    {
        var (model, note) = CustomTrainingModels.Evaluate(Info(type: null!, parameters: null, bases: ["Qwen/Qwen3-1.7B"]));

        Assert.Null(model);
        Assert.Contains("Qwen/Qwen3-1.7B", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Store_KeepsCustomModels()
    {
        using var root = new TempDirectory();
        var store = new CustomTrainingModels(root.Combine("training", "eigene-modelle.json"), NullLogger<CustomTrainingModels>.Instance);
        var model = CustomTrainingModels.Evaluate(Info()).Model!;

        store.Add(model);
        store.Add(model with { Note = "neu" });
        var loaded = Assert.Single(store.Load());

        Assert.Equal("owner/model", loaded.Repo);
        Assert.Equal("neu", loaded.Note);
        Assert.True(loaded.IsCustom);
        Assert.Contains("eigenes", loaded.Title, StringComparison.Ordinal);
        Assert.Empty(store.Remove("OWNER/model"));
    }
}
