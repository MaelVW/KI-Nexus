using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Tests;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

public class ModelFactsTests
{
    [Fact]
    public void ParseOllamaParams_ReadsNumbers_AndIgnoresTheRest()
    {
        var defaults = ModelFactsProvider.ParseOllamaParams(
            """{"temperature":1,"top_k":20,"top_p":0.95,"presence_penalty":1.5,"stop":["<end>"]}""", "Test");

        Assert.NotNull(defaults);
        Assert.Equal(1.0, defaults.Temperature);
        Assert.Equal(20, defaults.TopK);
        Assert.Equal(0.95, defaults.TopP);
        Assert.Equal(1.5, defaults.PresencePenalty);
        Assert.Null(defaults.MinP);
        Assert.Equal("Test", defaults.Source);
    }

    [Theory]
    [InlineData("""{"stop":["<end>"]}""")]
    [InlineData("""[]""")]
    public void ParseOllamaParams_WithoutSampling_IsNull(string json) => Assert.Null(ModelFactsProvider.ParseOllamaParams(json, "Test"));

    [Fact]
    public void Combine_FileWins_OllamaFillsGaps()
    {
        var gguf = new GgufMetadata("gemma4", null, null, null, null, null, null, Temperature: 1.0, TopP: null, TopK: 64, HasChatTemplate: true);
        var ollama = new SamplingDefaults(0.7, 0.95, 40, null, null, 1.5, "Ollama");

        var combined = ModelFactsProvider.Combine(gguf, ollama);

        Assert.Equal(1.0, combined.Temperature);
        Assert.Equal(64, combined.TopK);
        Assert.Equal(0.95, combined.TopP);
        Assert.Equal(1.5, combined.PresencePenalty);
        Assert.Contains("ergänzt", combined.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_LlamaCppKi_ReadsFileDraftAndRecommendations()
    {
        using var root = new TempDirectory();
        var folder = Directory.CreateDirectory(root.Combine("models", "testmodell")).FullName;
        GgufTestFile.Write(Path.Combine(folder, "testmodell.gguf"));
        File.WriteAllBytes(Path.Combine(folder, "draft.gguf"), new byte[2048]);
        ModelLibrary.WriteManifest(folder, new ModelManifest
        {
            Name = "testmodell", ModelFile = "testmodell.gguf", DraftFile = "draft.gguf", Source = "huggingface:test/testmodell", IsMoe = true,
        });
        var ki = new KiProfile { Id = "test", Engine = EngineKind.LlamaCpp, BaseModel = { Path = "models/testmodell/testmodell.gguf" } };

        var facts = CreateProvider(root, ollamaDir: root.Combine("kein-ollama")).Describe(ki);

        Assert.True(facts.ModelBytes > 0);
        Assert.True(facts.HasDraft);
        Assert.Equal("models/testmodell/draft.gguf", facts.DraftPath);
        Assert.Equal(2048, facts.DraftBytes);
        Assert.Equal(262144, facts.MaxContext);
        Assert.Equal(30, facts.BlockCount);
        Assert.True(facts.IsMoe);
        Assert.Equal(1.0, facts.Defaults.Temperature);
        Assert.Equal(64, facts.Defaults.TopK);
        Assert.Equal(0.05, facts.Defaults.MinP);
        Assert.Equal(1.1, facts.Defaults.RepeatPenalty);
    }

    [Fact]
    public void Describe_OllamaKi_ReadsBlobsWithoutChangingThem()
    {
        using var root = new TempDirectory();
        using var ollamaDir = new TempDirectory();
        WriteFakeOllama(ollamaDir);
        var blob = ollamaDir.Combine("blobs", "sha256-aaa");
        var blobWriteTime = File.GetLastWriteTimeUtc(blob);
        var ki = new KiProfile { Id = "mael-ki-ollama", Engine = EngineKind.Ollama, BaseModel = { OllamaName = "gemma4:26b" } };

        var facts = CreateProvider(root, ollamaDir.Path).Describe(ki);

        Assert.Equal(100, facts.ModelBytes);
        Assert.Equal(9, facts.ExtraBytes);
        Assert.False(facts.HasDraft);
        Assert.Equal(262144, facts.MaxContext);
        Assert.Equal(0.8, facts.Defaults.Temperature);
        Assert.Equal(40, facts.Defaults.TopK);
        Assert.Contains("gemma4:26b", facts.Defaults.Source, StringComparison.Ordinal);
        Assert.Equal(blobWriteTime, File.GetLastWriteTimeUtc(blob));
    }

    [Fact]
    public void Describe_MissingFile_IsUnknown()
    {
        using var root = new TempDirectory();
        var ki = new KiProfile { Id = "weg", Engine = EngineKind.LlamaCpp, BaseModel = { Path = "models/weg/weg.gguf" } };

        Assert.Same(ModelFacts.Unknown, CreateProvider(root, root.Combine("kein-ollama")).Describe(ki));
    }

    [Theory]
    [InlineData("models/gemma4-26b/gemma4-26b.gguf", "models/gemma4-26b/draft.gguf")]
    [InlineData(@"models\qwen\qwen.gguf", "models/qwen/draft.gguf")]
    [InlineData("modell.gguf", "draft.gguf")]
    public void SiblingPath_ReplacesFileName(string modelPath, string expected) =>
        Assert.Equal(expected, ModelFactsProvider.SiblingPath(modelPath, "draft.gguf"));

    [Theory]
    [InlineData("0.7", 0.7)]
    [InlineData("0,7", 0.7)]
    [InlineData(" 1 ", 1.0)]
    [InlineData("", null)]
    public void ParameterText_ParsesDecimals(string text, double? expected)
    {
        Assert.True(ParameterText.TryParse(text, out double? value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("NaN")]
    public void ParameterText_RejectsInvalid(string text) => Assert.False(ParameterText.TryParse(text, out double? _));

    [Fact]
    public void ParameterText_FormatsWithPoint()
    {
        Assert.Equal("0.95", ParameterText.Format(0.95));
        Assert.Equal("1", ParameterText.Format(1.0));
        Assert.Equal("", ParameterText.Format((double?)null));
        Assert.Equal("64", ParameterText.Format(64));
        Assert.False(ParameterText.TryParse("1.5", out int? _));
    }

    private static ModelFactsProvider CreateProvider(TempDirectory root, string ollamaDir) => new(
        new AppPaths(root.Path), new ModelLibrary(root.Combine("models"), NullLogger<ModelLibrary>.Instance),
        new OllamaModelStore(ollamaDir), NullLogger<ModelFactsProvider>.Instance);

    private static void WriteFakeOllama(TempDirectory dir)
    {
        var manifestDir = Directory.CreateDirectory(dir.Combine("manifests", "registry.ollama.ai", "library", "gemma4")).FullName;
        Directory.CreateDirectory(dir.Combine("blobs"));
        GgufTestFile.Write(dir.Combine("blobs", "sha256-aaa"));
        File.WriteAllText(dir.Combine("blobs", "sha256-bbb"), "Projektor");
        File.WriteAllText(dir.Combine("blobs", "sha256-ccc"), """{"temperature":0.8,"top_k":40}""");
        File.WriteAllText(Path.Combine(manifestDir, "26b"), """
            {
              "schemaVersion": 2,
              "layers": [
                { "mediaType": "application/vnd.ollama.image.model", "digest": "sha256:aaa", "size": 100 },
                { "mediaType": "application/vnd.ollama.image.projector", "digest": "sha256:bbb", "size": 9 },
                { "mediaType": "application/vnd.ollama.image.params", "digest": "sha256:ccc", "size": 30 }
              ]
            }
            """);
    }
}
