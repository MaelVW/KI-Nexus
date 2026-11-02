using KiNexus.Core.Models;
using KiNexus.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class VideoTests
{
    [Fact]
    public void Request_DefaultIsValid()
    {
        var request = new VideoRequest("A dog runs across a meadow.");

        Assert.Null(request.Problem());
        Assert.Equal(3.88, request.Seconds, 2);
    }

    [Theory]
    [InlineData("", 768, 512, 97, "Prompt")]
    [InlineData("x", 770, 512, 97, "32")]
    [InlineData("x", 768, 128, 97, "zwischen 256")]
    [InlineData("x", 1536, 512, 97, "1280")]
    [InlineData("x", 768, 512, 96, "8·n + 1")]
    [InlineData("x", 768, 512, 1, "8·n + 1")]
    [InlineData("x", 768, 512, 257, "249")]
    public void Request_ExplainsWhatIsWrong(string prompt, int width, int height, int frames, string expected)
    {
        var problem = new VideoRequest(prompt, width, height, frames).Problem();

        Assert.NotNull(problem);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_RejectsOddFrameRate()
    {
        Assert.Contains("Bilder pro Sekunde", new VideoRequest("x", FrameRate: 0).Problem(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, 25, 97)]
    [InlineData(2, 25, 49)]
    [InlineData(5, 24, 121)]
    [InlineData(10, 25, 249)]
    [InlineData(0.1, 25, 9)]
    public void FramesFor_RoundsToEightTimesNPlusOne(double seconds, double frameRate, int expected)
    {
        var frames = VideoRequest.FramesFor(seconds, frameRate);

        Assert.Equal(expected, frames);
        Assert.Equal(1, (frames - 1) % 8 + 1);
    }

    [Fact]
    public void ModelSet_KnowsItsFilesAndSteps()
    {
        using var root = new TempDirectory();
        var models = VideoModelSet.Ltx23Distilled;
        Directory.CreateDirectory(root.Combine("vae"));
        File.WriteAllText(root.Combine("vae", models.VideoVae), "x");

        Assert.Equal(8, models.Steps);
        Assert.Equal(1.0, models.Guidance);
        Assert.Equal(5, models.Files(root.Path).Count);
        Assert.Contains(root.Combine("diffusion_models", models.Model), models.Files(root.Path));
        var missing = models.MissingFiles(root.Path);
        Assert.Equal(4, missing.Count);
        Assert.DoesNotContain(root.Combine("vae", models.VideoVae), missing);
    }

    // ----------------------------------------------------------------- Bibliothek

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Library_MovesFilesAndKeepsSettings()
    {
        using var root = new TempDirectory();
        var library = new VideoLibrary(root.Combine("videos"), NullLogger<VideoLibrary>.Instance);
        var (video, thumbnail) = RawFiles(root, "2026-11-02_150000_seed7");
        var request = new VideoRequest("  A fox jumps.  ", 512, 320, 49, 25, 7, WithAudio: false);

        var entry = await library.AddAsync(new VideoDone(video, 95.5, thumbnail), request, VideoModelSet.Ltx23Distilled, Ct);

        Assert.Equal("2026-11-02_150000_seed7", entry.Id);
        Assert.Equal(root.Combine("videos", "2026-11-02_150000_seed7.mp4"), entry.VideoPath);
        Assert.Equal(root.Combine("videos", "2026-11-02_150000_seed7.png"), entry.ThumbnailPath);
        Assert.False(File.Exists(video));
        Assert.True(File.Exists(entry.VideoPath));
        Assert.Equal("A fox jumps.", entry.Prompt);
        Assert.Equal(1.96, entry.Seconds, 2);
        Assert.Equal(request with { Prompt = "A fox jumps." }, entry.ToRequest());

        var loaded = Assert.Single(await library.LoadAllAsync(Ct));
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(entry.VideoPath, loaded.VideoPath);
        Assert.Equal(entry.ThumbnailPath, loaded.ThumbnailPath);
        Assert.Equal(95.5, loaded.GenerationSeconds);
        Assert.Equal(VideoModelSet.Ltx23Distilled.Name, loaded.Model);
        Assert.False(loaded.WithAudio);
        Assert.Equal(3, library.FilesOf(loaded).Count);
    }

    [Fact]
    public async Task Library_NewestFirst_UniqueNames_SkipsMissingVideos()
    {
        using var root = new TempDirectory();
        var library = new VideoLibrary(root.Combine("videos"), NullLogger<VideoLibrary>.Instance);
        var first = await library.AddAsync(new VideoDone(RawFiles(root, "same").Video, 1), new VideoRequest("eins"), VideoModelSet.Ltx23Distilled, Ct);
        var second = await library.AddAsync(new VideoDone(RawFiles(root, "same").Video, 1), new VideoRequest("zwei"), VideoModelSet.Ltx23Distilled, Ct);
        File.WriteAllText(root.Combine("videos", "kaputt.json"), "{ kein json");

        Assert.Equal("same", first.Id);
        Assert.Equal("same-2", second.Id);
        Assert.Null(second.ThumbnailPath);
        Assert.Equal(["zwei", "eins"], (await library.LoadAllAsync(Ct)).Select(e => e.Prompt));

        File.Delete(first.VideoPath);
        Assert.Equal(["zwei"], (await library.LoadAllAsync(Ct)).Select(e => e.Prompt));
        Assert.Empty(await new VideoLibrary(root.Combine("gibt-es-nicht"), NullLogger<VideoLibrary>.Instance).LoadAllAsync(Ct));
    }

    [Theory]
    [InlineData("2026-11-02_144301_seed11_00001_", "2026-11-02_144301_seed11")]
    [InlineData("eigener-name", "eigener-name")]
    [InlineData("_00001_", "video")]
    public void Library_CleansComfyCounter(string name, string expected)
    {
        Assert.Equal(expected, VideoLibrary.CleanName(name));
    }

    private static (string Video, string Thumbnail) RawFiles(TempDirectory root, string name)
    {
        Directory.CreateDirectory(root.Combine("roh"));
        var video = root.Combine("roh", name + "_00001_.mp4");
        var thumbnail = root.Combine("roh", name + "_vorschau_00001_.png");
        File.WriteAllText(video, "mp4");
        File.WriteAllText(thumbnail, "png");
        return (video, thumbnail);
    }

    // ----------------------------------------------------------------- Speicher-Ampel

    private const ulong Gb = 1024UL * 1024 * 1024;

    [Fact]
    public void Fit_GreenWithEnoughRam()
    {
        var fit = VideoFitEstimator.Estimate(new VideoRequest("x"), 32 * Gb, 8 * Gb, chatModelLoaded: false, onBattery: false);

        Assert.Equal(FitLevel.Green, fit.Level);
        Assert.Contains("24.0 GB", fit.Explanation, StringComparison.Ordinal);
        Assert.Equal("sollte passen", fit.Label);
    }

    [Theory]
    [InlineData(15, false, false, FitLevel.Yellow, "lagert")]
    [InlineData(22, false, false, FitLevel.Red, "Schliess")]
    [InlineData(22, true, false, FitLevel.Yellow, "entladen")]
    [InlineData(8, false, true, FitLevel.Yellow, "Akku")]
    public void Fit_WarnsAboutRamChatModelAndBattery(int usedGb, bool chatLoaded, bool battery, FitLevel expected, string text)
    {
        var fit = VideoFitEstimator.Estimate(new VideoRequest("x"), 32 * Gb, (ulong)usedGb * Gb, chatLoaded, battery);

        Assert.Equal(expected, fit.Level);
        Assert.Contains(text, fit.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Fit_WarnsAboutBigVideos()
    {
        var tenSeconds = VideoFitEstimator.Estimate(new VideoRequest("x", Frames: 249), 32 * Gb, 8 * Gb, false, false);
        var huge = VideoFitEstimator.Estimate(new VideoRequest("x", 1280, 1280, 249), 32 * Gb, 8 * Gb, false, false);

        Assert.Equal(FitLevel.Yellow, tenSeconds.Level);
        Assert.Contains("2.6-mal", tenSeconds.Explanation, StringComparison.Ordinal);
        Assert.Equal(FitLevel.Red, huge.Level);
    }
}
