using KiNexus.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

/// <summary>Bild-Galerie und Bild-Modelle (Bauplan Bild, B1 und B2).</summary>
public class ImageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Library_MovesEachImageAndKeepsSettings()
    {
        using var root = new TempDirectory();
        var library = new ImageLibrary(root.Combine("images"), NullLogger<ImageLibrary>.Instance);
        var raw = RawImages(root, "2026-11-03_100000_seed9", 2);
        var request = new ImageRequest("  A red apple.  ", 1216, 832, Count: 2, Seed: 9);

        var entries = await library.AddAsync(new ImageDone(raw, 21.5), request, ImageModelSet.Flux2Klein4B, Ct);

        Assert.Equal(["2026-11-03_100000_seed9_1", "2026-11-03_100000_seed9_2"], entries.Select(e => e.Id));
        Assert.All(raw, file => Assert.False(File.Exists(file)));
        Assert.All(entries, e =>
        {
            Assert.True(e.IsImage);
            Assert.True(File.Exists(e.VideoPath));
            Assert.Equal(e.VideoPath, e.ThumbnailPath);
            Assert.Equal("A red apple.", e.Prompt);
            Assert.Equal(2, e.BatchCount);
            Assert.Equal(ImageModelSet.Flux2Klein4B.Name, e.Model);
        });

        var loaded = await library.LoadAllAsync(Ct);
        Assert.Equal(entries.Select(e => e.Id), loaded.Select(e => e.Id));
        Assert.Equal([0, 1], loaded.Select(e => e.BatchIndex));
        Assert.All(loaded, e => Assert.True(e.IsImage));
        Assert.Equal(1216, loaded[0].Width);
        Assert.Equal(21.5, loaded[0].GenerationSeconds);
        Assert.Equal(2, library.FilesOf(loaded[0]).Count);
    }

    [Fact]
    public async Task Library_SingleImageHasNoNumber_SkipsMissingPng()
    {
        using var root = new TempDirectory();
        var library = new ImageLibrary(root.Combine("images"), NullLogger<ImageLibrary>.Instance);
        var first = Assert.Single(await library.AddAsync(new ImageDone(RawImages(root, "same", 1), 5), new ImageRequest("eins"), ImageModelSet.ZImageTurbo, Ct));
        var second = Assert.Single(await library.AddAsync(new ImageDone(RawImages(root, "same", 1), 5), new ImageRequest("zwei"), ImageModelSet.ZImageTurbo, Ct));

        Assert.Equal("same", first.Id);
        Assert.Equal("same-2", second.Id);
        File.Delete(first.VideoPath);
        Assert.Equal(["zwei"], (await library.LoadAllAsync(Ct)).Select(e => e.Prompt));
        Assert.Empty(await new ImageLibrary(root.Combine("gibt-es-nicht"), NullLogger<ImageLibrary>.Instance).LoadAllAsync(Ct));
    }

    [Fact]
    public async Task Library_KeepsCopiesOfReferenceImages()
    {
        using var root = new TempDirectory();
        var library = new ImageLibrary(root.Combine("images"), NullLogger<ImageLibrary>.Instance);
        var reference = root.Combine("hund.JPG");
        File.WriteAllText(reference, "jpg");
        var request = new ImageRequest("Der Hund mit Schal", Count: 2, ReferenceImages: [reference]);

        var entries = await library.AddAsync(new ImageDone(RawImages(root, "ref", 2), 15), request, ImageModelSet.Flux2Klein4B, Ct);
        File.Delete(reference);

        Assert.Equal(["ref_1_ref1.jpg"], entries[0].ReferenceImages);
        var loaded = await library.LoadAllAsync(Ct);
        Assert.All(loaded, e => Assert.True(File.Exists(Assert.Single(e.ReferenceImagePaths))));
        Assert.Equal(3, library.FilesOf(loaded[0]).Count);
    }

    [Fact]
    public async Task Library_KeepsCopyOfSourceImage()
    {
        using var root = new TempDirectory();
        var library = new ImageLibrary(root.Combine("images"), NullLogger<ImageLibrary>.Instance);
        var source = root.Combine("hund.png");
        File.WriteAllText(source, "png");
        var request = new ImageRequest("Der Hund im Schnee", SourceImage: source, Strength: 0.7);

        var entry = Assert.Single(await library.AddAsync(new ImageDone(RawImages(root, "vorlage", 1), 9), request, ImageModelSet.Flux2Klein4B, Ct));
        File.Delete(source);

        Assert.Equal("vorlage_vorlage.png", entry.SourceImage);
        var loaded = Assert.Single(await library.LoadAllAsync(Ct));
        Assert.True(File.Exists(loaded.SourceImagePath));
        Assert.Equal(0.7, loaded.Strength);
        Assert.Equal(3, library.FilesOf(loaded).Count);
    }

    [Fact]
    public async Task VideoEntries_WithoutKind_AreVideos()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Combine("videos"));
        File.WriteAllText(root.Combine("videos", "alt.mp4"), "mp4");
        File.WriteAllText(root.Combine("videos", "alt.json"), """{ "id": "alt", "prompt": "Ein altes Video", "frames": 49, "frameRate": 25 }""");

        var entry = Assert.Single(await new VideoLibrary(root.Combine("videos"), NullLogger<VideoLibrary>.Instance).LoadAllAsync(Ct));

        Assert.False(entry.IsImage);
        Assert.Equal(VideoEntry.VideoKind, entry.Kind);
    }

    [Fact]
    public void ModelSets_ListTheirFiles()
    {
        using var root = new TempDirectory();
        var files = ImageModelSet.Flux2Klein4B.Files(root.Path);

        Assert.Equal(root.Combine("diffusion_models", "flux-2-klein-4b-Q8_0.gguf"), files[0]);
        Assert.Equal(root.Combine("text_encoders", "qwen_3_4b.safetensors"), files[1]);
        Assert.Equal(3, ImageModelSet.Flux2Klein4B.MissingFiles(root.Path).Count);
        Assert.Equal(ImageModelSet.Flux2Klein4B.TextEncoder, ImageModelSet.ZImageTurbo.TextEncoder);
    }

    [Fact]
    public void PromptWriter_DescribesImageShape()
    {
        var messages = VideoPromptWriter.ImageMessages("  ein Fuchs im Schnee ", 832, 1216);

        Assert.Equal(VideoPromptWriter.ImageSystemPrompt, messages[0].Content);
        Assert.Equal("Idea: ein Fuchs im Schnee\nImage: portrait.", messages[1].Content);
        Assert.Contains("square", VideoPromptWriter.ImageMessages("x", 1024, 1024)[1].Content, StringComparison.Ordinal);
        Assert.Equal("A fox.", VideoPromptWriter.Clean("Image prompt: \"A fox.\""));
    }

    private static List<string> RawImages(TempDirectory root, string name, int count)
    {
        Directory.CreateDirectory(root.Combine("roh", "bilder"));
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var file = root.Combine("roh", "bilder", $"{name}_{i:00000}_.png");
            File.WriteAllText(file, "png");
            files.Add(file);
        }

        return files;
    }
}
