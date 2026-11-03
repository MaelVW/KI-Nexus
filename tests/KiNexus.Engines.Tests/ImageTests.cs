using System.Text.Json.Nodes;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using KiNexus.Core.Tests;
using KiNexus.Core.Video;
using KiNexus.Engines.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

/// <summary>Bildgenerierung über ComfyUI (Bauplan Bild, B1).</summary>
public class ImageTests
{
    // ----------------------------------------------------------------- Auftrag

    [Theory]
    [InlineData("", 1024, 1024, 1, "leer")]
    [InlineData("Ein Hund", 1000, 1024, 1, "durch 16")]
    [InlineData("Ein Hund", 1024, 256, 1, "durch 16")]
    [InlineData("Ein Hund", 1024, 1024, 5, "1 bis 4")]
    [InlineData("Ein Hund", 1024, 1024, 0, "1 bis 4")]
    public void Request_ExplainsProblems(string prompt, int width, int height, int count, string expected)
    {
        Assert.Contains(expected, new ImageRequest(prompt, width, height, count).Problem(), StringComparison.Ordinal);
    }

    [Fact]
    public void Request_FormatsAreValid()
    {
        Assert.All(ImageRequest.Formats, format => Assert.Null(new ImageRequest("x", format.Width, format.Height).Problem()));
    }

    // ----------------------------------------------------------------- Workflow

    [Fact]
    public void Builder_FillsFluxTemplate()
    {
        var models = ImageModelSet.Flux2Klein4B;
        var workflow = ComfyWorkflowBuilder.BuildImage(new ImageRequest("  A red apple.  ", 1216, 832, Count: 3, Seed: 9), models, "bilder/test");

        Assert.Equal("A red apple.", Inputs(workflow, "KN_Prompt")["text"]!.GetValue<string>());
        Assert.Equal(models.Model, Inputs(workflow, "KN_Model")["unet_name"]!.GetValue<string>());
        Assert.Equal("flux2", Inputs(workflow, "KN_TextEncoder")["type"]!.GetValue<string>());
        Assert.Equal(models.Vae, Inputs(workflow, "KN_Vae")["vae_name"]!.GetValue<string>());
        Assert.Equal(1216, Inputs(workflow, "KN_Latent")["width"]!.GetValue<int>());
        Assert.Equal(832, Inputs(workflow, "KN_Latent")["height"]!.GetValue<int>());
        Assert.Equal(3, Inputs(workflow, "KN_Latent")["batch_size"]!.GetValue<int>());
        Assert.Equal(1216, Inputs(workflow, "KN_Sigmas")["width"]!.GetValue<int>());
        Assert.Equal(4, Inputs(workflow, "KN_Sigmas")["steps"]!.GetValue<int>());
        Assert.Equal(9, Inputs(workflow, "KN_Seed")["noise_seed"]!.GetValue<long>());
        Assert.Equal("bilder/test", Inputs(workflow, "KN_Save")["filename_prefix"]!.GetValue<string>());
        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Builder_FillsZImageTemplate()
    {
        var models = ImageModelSet.ZImageTurbo;
        var workflow = ComfyWorkflowBuilder.BuildImage(new ImageRequest("A red apple.", 832, 1216, Seed: 5), models, "bilder/test");

        Assert.Equal("lumina2", Inputs(workflow, "KN_TextEncoder")["type"]!.GetValue<string>());
        Assert.Equal(models.Vae, Inputs(workflow, "KN_Vae")["vae_name"]!.GetValue<string>());
        Assert.Equal(1, Inputs(workflow, "KN_Latent")["batch_size"]!.GetValue<int>());
        Assert.Equal(5, Inputs(workflow, "KN_Denoise")["seed"]!.GetValue<long>());
        Assert.Equal(8, Inputs(workflow, "KN_Denoise")["steps"]!.GetValue<int>());
        Assert.Equal(1.0, Inputs(workflow, "KN_Denoise")["cfg"]!.GetValue<double>());
        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Builder_ChainsReferenceImages()
    {
        using var root = new TempDirectory();
        var refs = new[] { root.Combine("a.png"), root.Combine("b.jpg") };
        foreach (var file in refs)
        {
            File.WriteAllText(file, "bild");
        }

        var request = new ImageRequest("The dog from the first image in front of the hut from the second.", ReferenceImages: refs);
        var workflow = ComfyWorkflowBuilder.BuildImage(request, ImageModelSet.Flux2Klein4B, "bilder/test", ["x_ref1.png", "x_ref2.jpg"]);

        Assert.Equal("x_ref1.png", Inputs(workflow, "KN_Reference1")["image"]!.GetValue<string>());
        Assert.Equal("x_ref2.jpg", Inputs(workflow, "KN_Reference2")["image"]!.GetValue<string>());
        Assert.Equal(1024, Inputs(workflow, "KN_ReferenceScale1")["largest_size"]!.GetValue<int>());
        // Kette: Prompt → Referenz 1 → Referenz 2 → Guider.
        var first = ComfyWorkflowBuilder.NodeId(workflow, "KN_ReferenceLatent1");
        var second = ComfyWorkflowBuilder.NodeId(workflow, "KN_ReferenceLatent2");
        Assert.Equal(ComfyWorkflowBuilder.NodeId(workflow, "KN_Prompt"), Inputs(workflow, "KN_ReferenceLatent1")["conditioning"]![0]!.GetValue<string>());
        Assert.Equal(first, Inputs(workflow, "KN_ReferenceLatent2")["conditioning"]![0]!.GetValue<string>());
        Assert.Equal(second, Inputs(workflow, "KN_Guider")["positive"]![0]!.GetValue<string>());
        Assert.Equal(ComfyWorkflowBuilder.NodeId(workflow, "KN_Vae"), Inputs(workflow, "KN_ReferenceEncode2")["vae"]![0]!.GetValue<string>());
        AssertAllLinksExist(workflow);

        var plain = ComfyWorkflowBuilder.BuildImage(new ImageRequest("x"), ImageModelSet.Flux2Klein4B, "bilder/test");
        Assert.Equal(ComfyWorkflowBuilder.NodeId(plain, "KN_Prompt"), Inputs(plain, "KN_Guider")["positive"]![0]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => ComfyWorkflowBuilder.BuildImage(request, ImageModelSet.ZImageTurbo, "x", ["x_ref1.png"]));
    }

    [Theory]
    [InlineData(ImageModelFamily.Flux2Klein)]
    [InlineData(ImageModelFamily.ZImageTurbo)]
    public void Builder_StartsFromSourceImage(ImageModelFamily family)
    {
        using var root = new TempDirectory();
        var source = root.Combine("vorlage.png");
        File.WriteAllText(source, "bild");
        var models = family == ImageModelFamily.Flux2Klein ? ImageModelSet.Flux2Klein4B : ImageModelSet.ZImageTurbo;
        var request = new ImageRequest("A dog in the snow.", 1216, 832, Count: 3, SourceImage: source, Strength: 0.65);

        var workflow = ComfyWorkflowBuilder.BuildImage(request, models, "bilder/test", sourceName: "x_vorlage.png");

        Assert.Equal("x_vorlage.png", Inputs(workflow, "KN_Source")["image"]!.GetValue<string>());
        Assert.Equal(1216, Inputs(workflow, "KN_SourceScale")["width"]!.GetValue<int>());
        Assert.Equal("center", Inputs(workflow, "KN_SourceScale")["crop"]!.GetValue<string>());
        Assert.Equal(3, Inputs(workflow, "KN_SourceLatent")["amount"]!.GetValue<int>());
        Assert.Equal(ComfyWorkflowBuilder.NodeId(workflow, "KN_SourceLatent"), Inputs(workflow, "KN_Denoise")["latent_image"]![0]!.GetValue<string>());
        if (family == ImageModelFamily.Flux2Klein)
        {
            Assert.Equal(0.65, Inputs(workflow, "KN_Strength")["denoise"]!.GetValue<double>());
            Assert.Equal(ComfyWorkflowBuilder.NodeId(workflow, "KN_Strength"), Inputs(workflow, "KN_Denoise")["sigmas"]![0]!.GetValue<string>());
            Assert.Equal(1, Inputs(workflow, "KN_Denoise")["sigmas"]![1]!.GetValue<int>());
        }
        else
        {
            Assert.Equal(0.65, Inputs(workflow, "KN_Denoise")["denoise"]!.GetValue<double>());
        }

        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Request_ChecksSourceImage()
    {
        using var root = new TempDirectory();
        var source = root.Combine("vorlage.png");
        File.WriteAllText(source, "bild");

        Assert.Null(new ImageRequest("x", SourceImage: source).Problem());
        Assert.Contains("zwischen 0.2 und 1.0", new ImageRequest("x", SourceImage: source, Strength: 0.1).Problem(), StringComparison.Ordinal);
        Assert.Contains("gibt es nicht mehr", new ImageRequest("x", SourceImage: root.Combine("weg.png")).Problem(), StringComparison.Ordinal);
        Assert.Null(new ImageRequest("x", Strength: 0.1).Problem()); // Ohne Vorlage zählt die Stärke nicht.
    }

    [Fact]
    public void Request_ChecksReferenceImages()
    {
        using var root = new TempDirectory();
        var image = root.Combine("a.png");
        File.WriteAllText(image, "bild");

        Assert.Null(new ImageRequest("x", ReferenceImages: [image]).Problem());
        Assert.Contains("höchstens 3", new ImageRequest("x", ReferenceImages: [image, image, image, image]).Problem(), StringComparison.Ordinal);
        Assert.Contains("gibt es nicht mehr", new ImageRequest("x", ReferenceImages: [root.Combine("weg.png")]).Problem(), StringComparison.Ordinal);
        Assert.Contains("PNG, JPG", new ImageRequest("x", ReferenceImages: [root.Combine("a.gif")]).Problem(), StringComparison.Ordinal);
        Assert.True(ImageModelSet.Flux2Klein4B.SupportsReferences);
        Assert.False(ImageModelSet.ZImageTurbo.SupportsReferences);
    }

    [Fact]
    public void Builder_RejectsInvalidRequest()
    {
        Assert.Throws<ArgumentException>(() => ComfyWorkflowBuilder.BuildImage(new ImageRequest(""), ImageModelSet.Flux2Klein4B, "x"));
    }

    [Fact]
    public void Protocol_UsesImageTexts()
    {
        Assert.Equal(VideoPhase.DecodingVideo, ComfyProtocol.PhaseFor("KN_DecodeImage"));
        Assert.Contains("Bildmodell", ComfyProtocol.DescribeImage(VideoPhase.LoadingModel), StringComparison.Ordinal);
        Assert.Equal(ComfyProtocol.Describe(VideoPhase.Starting), ComfyProtocol.DescribeImage(VideoPhase.Starting));
    }

    // ----------------------------------------------------------------- Einrichtung

    [Fact]
    public void PathsYaml_AddsImageFolder()
    {
        var yaml = ComfyCommand.PathsYaml(@"C:\Projekt\models\video", imageModelsDirectory: @"C:\Projekt\models\image").ReplaceLineEndings("\n");

        Assert.Contains("kinexus_bild:\n  base_path: \"C:/Projekt/models/image\"\n  diffusion_models: diffusion_models", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("kinexus_bild", ComfyCommand.PathsYaml(@"C:\Projekt\models\video"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backend_ReportsMissingImageModels()
    {
        using var root = new TempDirectory();
        using var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        await using var backend = new ComfyVideoBackend(supervisor, NullLogger<ComfyVideoBackend>.Instance, ComfyInstallation.For(root.Path),
            new ComfyOptions(root.Combine("video"), root.Combine("out"), root.Combine("work"), root.Combine("video.log"), ImageModelsDirectory: root.Combine("image")));

        var events = await Collect(backend.GenerateImageAsync(new ImageRequest("x"), ImageModelSet.Flux2Klein4B, TestContext.Current.CancellationToken));

        var failed = Assert.IsType<VideoFailed>(Assert.Single(events));
        Assert.Contains("setup-video.ps1 -Images", failed.Message, StringComparison.Ordinal);
        Assert.Contains("qwen_3_4b.safetensors", failed.Message, StringComparison.Ordinal);
        Assert.Equal(VideoBackendState.Stopped, backend.State);
    }

    // ----------------------------------------------------------------- Echter Testlauf (nur auf Wunsch)

    /// <summary>
    /// Erzeugt echte Bilder über die Schnittstelle, mit ComfyUI und dem Standard-Bildmodell. Läuft nur mit KINEXUS_VIDEO_TEST=1,
    /// weil es die Grafikkarte braucht und die Bildmodelle voraussetzt.
    /// </summary>
    [Fact]
    public async Task RealRun_CreatesImagesThroughTheInterface()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("KINEXUS_VIDEO_TEST") == "1", "Nur mit KINEXUS_VIDEO_TEST=1 (braucht die Grafikkarte).");
        var paths = AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(AppPaths.RootEnvironmentVariable));
        using var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        await using var backend = new ComfyVideoBackend(supervisor, NullLogger<ComfyVideoBackend>.Instance,
            ComfyInstallation.For(Path.Combine(paths.Root, "engines")),
            new ComfyOptions(Path.Combine(paths.Root, "models", "video"), Path.Combine(paths.DataDirectory, "videos", "roh"),
                // Eigenes Log: Die App hält data/logs/video.log offen, wenn sie läuft.
                Path.Combine(paths.DataDirectory, "comfyui"), Path.Combine(paths.LogsDirectory, "image-test.log"),
                ImageModelsDirectory: Path.Combine(paths.Root, "models", "image")));
        var models = ImageModelSet.Known[0];
        var request = new ImageRequest("A cup of coffee on a wooden table, morning light, photo.", 768, 768, Count: 2, Seed: 3);

        var events = await Collect(backend.GenerateImageAsync(request, models, TestContext.Current.CancellationToken));
        foreach (var e in events)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(e.ToString());
        }

        var done = Assert.IsType<ImageDone>(events[^1]);
        Assert.Equal(2, done.ImagePaths.Count);
        Assert.All(done.ImagePaths, path => Assert.True(new FileInfo(path).Length > 50_000, path));
        Assert.Contains(events, e => e is VideoStep);
        Assert.Equal(VideoBackendState.Ready, backend.State);

        // Zweiter Auftrag: das erste Bild als Referenz.
        var withReference = new ImageRequest("The same cup of coffee on a snowy window sill, winter morning, photo.", 768, 768, Seed: 4,
            ReferenceImages: [done.ImagePaths[0]]);
        var second = await Collect(backend.GenerateImageAsync(withReference, models, TestContext.Current.CancellationToken));
        Assert.True(Assert.IsType<ImageDone>(second[^1]).ImagePaths.Count == 1, second[^1].ToString());

        // Dritter Auftrag: Bild zu Bild aus dem ersten Bild, zwei Varianten.
        var fromSource = new ImageRequest("A cup of tea on a wooden table, morning light, photo.", 768, 768, Count: 2, Seed: 5,
            SourceImage: done.ImagePaths[0], Strength: 0.7);
        var third = await Collect(backend.GenerateImageAsync(fromSource, models, TestContext.Current.CancellationToken));
        Assert.True(third[^1] is ImageDone { ImagePaths.Count: 2 }, third[^1].ToString());
        await backend.FreeMemoryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<VideoProgress>> Collect(IAsyncEnumerable<VideoProgress> source)
    {
        var list = new List<VideoProgress>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static JsonObject Inputs(JsonObject workflow, string title) =>
        workflow[ComfyWorkflowBuilder.NodeId(workflow, title)]!["inputs"]!.AsObject();

    private static void AssertAllLinksExist(JsonObject workflow)
    {
        foreach (var (_, node) in workflow)
        {
            foreach (var (_, value) in node!["inputs"]!.AsObject())
            {
                if (value is JsonArray { Count: 2 } link && link[0] is JsonValue id && id.TryGetValue<string>(out var target))
                {
                    Assert.True(workflow.ContainsKey(target), $"Verbindung zu Knoten {target} fehlt.");
                }
            }
        }
    }
}
