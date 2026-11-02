using KiNexus.Core.Inference;
using KiNexus.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

/// <summary>Video V4: Bild zu Video und "Prompt verbessern".</summary>
public class VideoV4Tests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Request_ChecksStartImage()
    {
        using var root = new TempDirectory();
        var image = root.Combine("foto.jpg");
        File.WriteAllText(image, "jpg");

        Assert.Null(new VideoRequest("x", StartImagePath: image).Problem());
        Assert.Contains("PNG, JPG", new VideoRequest("x", StartImagePath: root.Combine("text.txt")).Problem(), StringComparison.Ordinal);
        Assert.Contains("gibt es nicht mehr", new VideoRequest("x", StartImagePath: root.Combine("weg.png")).Problem(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_KeepsCopyOfStartImage()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Combine("roh"));
        var video = root.Combine("roh", "clip_00001_.mp4");
        File.WriteAllText(video, "mp4");
        var image = root.Combine("Foto.PNG");
        File.WriteAllText(image, "png");
        var library = new VideoLibrary(root.Combine("videos"), NullLogger<VideoLibrary>.Instance);

        var entry = await library.AddAsync(new VideoDone(video, 10), new VideoRequest("x", StartImagePath: image), VideoModelSet.Ltx23Distilled, Ct);
        File.Delete(image);
        var loaded = Assert.Single(await library.LoadAllAsync(Ct));

        Assert.Equal("clip_start.png", entry.StartImage);
        Assert.Equal(root.Combine("videos", "clip_start.png"), loaded.StartImagePath);
        Assert.Equal(loaded.StartImagePath, loaded.ToRequest().StartImagePath);
        Assert.Contains(loaded.StartImagePath!, library.FilesOf(loaded));
        Assert.Equal(3, library.FilesOf(loaded).Count); // mp4, json, Startbild (kein Vorschaubild)
    }

    [Fact]
    public void PromptWriter_BuildsMessagesWithClipFacts()
    {
        var messages = VideoPromptWriter.Messages("  Ein Hund rennt am Strand  ", 4, 512, 768, hasStartImage: true);

        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("LTX-2.3", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Idea: Ein Hund rennt am Strand", messages[1].Content, StringComparison.Ordinal);
        Assert.Contains("about 4 seconds, vertical", messages[1].Content, StringComparison.Ordinal);
        Assert.Contains("given photo", messages[1].Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<think>hmm</think>\n\"A dog runs on the beach.\"", "A dog runs on the beach.")]
    [InlineData("**Prompt:** A dog runs.\n\nWaves crash.", "A dog runs. Waves crash.")]
    [InlineData("Improved video prompt: „A cat sleeps.“", "A cat sleeps.")]
    public void PromptWriter_CleansAnswer(string answer, string expected)
    {
        Assert.Equal(expected, VideoPromptWriter.Clean(answer));
    }
}
