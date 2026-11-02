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

    // ----------------------------------------------------------------- Videomodelle finden und beurteilen (V3)

    /// <summary>So wie im config-Schlüssel echter LTX-2.3-Dateien (gekürzt).</summary>
    private const string AvConfig = """{"transformer": {"_class_name": "AVTransformer3DModel", "num_layers": 48, "apply_gated_attention": true}}""";

    [Theory]
    [InlineData("ltxv", "2.3.0", AvConfig, "ltx-2.3-22b-distilled-1.1-Q2_K.gguf", true, "distilled")]
    // Unsloth-Dateien haben kein "model_version": Dann zählt der config.
    [InlineData("ltxv", null, AvConfig, "ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf", true, "distilled")]
    [InlineData("ltxv", null, """{"transformer": {"_class_name": "AVTransformer3DModel", "apply_gated_attention": false}}""", "ltx-2-19b-distilled.gguf", false, "Version unbekannt")]
    [InlineData("ltxv", "2.3.0", AvConfig, "PinkCherry_FineTune_Q5_K_M.gguf", true, "steht nicht in der Datei")]
    [InlineData("ltxv", "2.3.0", null, "eigenes-modell.gguf", true, "8 Schritte")]
    [InlineData("ltxv", "2.3.0", AvConfig, "ltx-2.3-22b-dev-Q4_K_M.gguf", false, "dev")]
    [InlineData("ltxv", "0.9.8", AvConfig, "ltxv-13b-0.9.8-distilled.gguf", false, "0.9.8")]
    [InlineData("ltxv", null, null, "alt.gguf", false, "Version unbekannt")]
    [InlineData("ltxv", "2.3.0", """{"transformer": {"_class_name": "Transformer3DModel"}}""", "x-distilled.gguf", false, "ohne Ton")]
    [InlineData("wan", "2.2", null, "wan-distilled.gguf", false, "Kein LTX-Modell")]
    public void Catalog_ClassifiesVideoModels(string architecture, string? version, string? config, string fileName, bool usable, string note)
    {
        var (isUsable, text) = VideoModelCatalog.Classify(architecture, version, config, fileName);

        Assert.Equal(usable, isUsable);
        Assert.Contains(note, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_FindsBuiltInAndLibraryModels()
    {
        using var root = new TempDirectory();
        var video = root.Combine("models", "video");
        Directory.CreateDirectory(Path.Combine(video, "diffusion_models"));
        var official = Path.Combine(video, "diffusion_models", VideoModelSet.Ltx23Distilled.Model);
        WriteLtx(official, version: null); // wie die echte Datei von Unsloth: ohne model_version
        var library = new ModelLibrary(root.Combine("models"), NullLogger<ModelLibrary>.Instance);
        var downloaded = AddLibraryModel(library, root, "ltx-q2", "ltx-2.3-22b-distilled-1.1-Q2_K.gguf", "2.3.0");
        AddLibraryModel(library, root, "ltx-alt", "ltxv-0.9.8-distilled.gguf", "0.9.8");
        var chat = root.Combine("models", "qwen");
        Directory.CreateDirectory(chat);
        GgufTestFile.Write(Path.Combine(chat, "qwen.gguf"));
        library.WriteManifestFor(chat, "qwen.gguf", null, "datei:x", null);

        var choices = VideoModelCatalog.Find(video, library.Scan());

        Assert.Equal([VideoModelSet.Ltx23Distilled.Name, "ltx-q2", "ltx-alt"], choices.Select(c => c.Name));
        Assert.Equal([true, true, false], choices.Select(c => c.IsUsable));
        Assert.True(choices[0].IsBuiltIn);
        Assert.Null(choices[0].ToModelSet().ModelFolder);

        var set = choices[1].ToModelSet();
        Assert.Equal("ltx-2.3-22b-distilled-1.1-Q2_K.gguf", set.Model);
        Assert.Equal(Path.GetDirectoryName(downloaded), set.ModelFolder);
        Assert.Equal(downloaded, set.Files(video)[0]);
        Assert.Equal(VideoModelSet.Ltx23Distilled.TextEncoder, set.TextEncoder);
        Assert.Equal(4, set.CompanionFiles(video).Count);
        Assert.Equal("2.3.0", library.Scan().Single(m => m.FolderName == "ltx-q2").Manifest.ModelVersion);
        Assert.Null(library.Scan().Single(m => m.FolderName == "qwen").Manifest.ModelVersion);
    }

    private static void WriteLtx(string path, string? version)
    {
        var texts = new Dictionary<string, string> { ["config"] = AvConfig };
        if (version is not null)
        {
            texts["model_version"] = version;
        }

        GgufTestFile.Write(path, "ltxv", expertCount: null, tokenizer: false, texts: texts);
    }

    private static string AddLibraryModel(ModelLibrary library, TempDirectory root, string folder, string file, string version)
    {
        var path = root.Combine("models", folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteLtx(path, version);
        library.WriteManifestFor(Path.GetDirectoryName(path)!, file, null, "huggingface:unsloth/LTX-2.3-GGUF", null);
        return path;
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
