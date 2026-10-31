using KiNexus.Core.Inference;
using KiNexus.Core.IO;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Tests;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

public class OllamaTests
{
    [Fact]
    public void ChatRequest_ContainsModelThinkKeepAliveAndOptions()
    {
        var ki = Ki();
        var body = OllamaProtocol.BuildChatRequest(new ChatRequest(ki, [new ChatMessage(ChatRole.User, "Hallo")], Think: false), stream: true);

        Assert.Equal("qwen3.5:9b", body["model"]!.GetValue<string>());
        Assert.False(body["think"]!.GetValue<bool>());
        Assert.Equal("5m", body["keep_alive"]!.GetValue<string>());
        Assert.Equal(8192, body["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal(1.5, body["options"]!["presence_penalty"]!.GetValue<double>());
        Assert.Null(body["options"]!["min_p"]);
    }

    [Fact]
    public void LoadRequest_UsesSameContextAsChat()
    {
        var body = OllamaProtocol.BuildLoadRequest(Ki());

        // Andere Kontextlänge beim Chat würde Ollama zum Neuladen zwingen.
        Assert.Equal(8192, body["options"]!["num_ctx"]!.GetValue<int>());
    }

    [Fact]
    public void CreateRequest_OwnModel_FromBaseModelWithPromptAndParameters()
    {
        var ki = Ki();
        ki.SystemPrompt = "Antworte auf Deutsch.";

        var body = OllamaProtocol.BuildCreateRequest(ki);

        // Name = Id der KI. Das Grundmodell bleibt, wie es ist.
        Assert.Equal("qwen-schnell-ollama", body["model"]!.GetValue<string>());
        Assert.Equal("qwen3.5:9b", body["from"]!.GetValue<string>());
        Assert.Equal("Antworte auf Deutsch.", body["system"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>());
        var parameters = body["parameters"]!;
        Assert.Equal(20, parameters["top_k"]!.GetValue<int>());
        Assert.Equal(1.5, parameters["presence_penalty"]!.GetValue<double>());
        Assert.Equal(8192, parameters["num_ctx"]!.GetValue<int>());
        Assert.Null(parameters["min_p"]);
    }

    [Fact]
    public void CreateRequest_WithoutPrompt_SendsNoSystem()
    {
        var body = OllamaProtocol.BuildCreateRequest(Ki());

        Assert.Null(body["system"]);
    }

    [Fact]
    public void ParseStreamLine_ReadsTextAndFinalStats()
    {
        var part = OllamaProtocol.ParseStreamLine("""{"model":"qwen3.5:9b","message":{"role":"assistant","content":"Hoi","thinking":""},"done":false}""");
        var last = OllamaProtocol.ParseStreamLine("""{"model":"qwen3.5:9b","message":{"role":"assistant","content":""},"done":true,"total_duration":3000000000,"prompt_eval_count":20,"prompt_eval_duration":100000000,"eval_count":80,"eval_duration":2000000000}""");

        Assert.Equal("Hoi", part!.Text);
        Assert.False(part.IsDone);
        Assert.True(last!.IsDone);
        Assert.Equal(80, last.Stats!.CompletionTokens);
        Assert.Equal(40, last.Stats.TokensPerSecond, precision: 3);
        Assert.Equal(200, last.Stats.PromptTokensPerSecond, precision: 3);
        Assert.Equal(TimeSpan.FromSeconds(3), last.Stats.Duration);
        Assert.Equal(EngineKind.Ollama, last.Stats.Engine);
    }

    [Fact]
    public void ChatRequest_WithImage_AddsBase64List()
    {
        using var temp = new TempDirectory();
        var image = temp.Combine("bild.jpg");
        File.WriteAllBytes(image, [1, 2, 3]);

        var body = OllamaProtocol.BuildChatRequest(new ChatRequest(Ki(), [new ChatMessage(ChatRole.User, "Was ist das?", [image])], Think: false), stream: true);

        Assert.Equal("AQID", body["messages"]![0]!["images"]![0]!.GetValue<string>());
        Assert.Equal("Was ist das?", body["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Stats_ContextTokens_AreQuestionPlusAnswer()
    {
        var last = OllamaProtocol.ParseStreamLine("""{"done":true,"prompt_eval_count":120,"prompt_eval_duration":1,"eval_count":30,"eval_duration":1,"total_duration":1}""");

        Assert.Equal(150, last!.Stats!.ContextTokens);
    }

    [Theory]
    [InlineData("""{"capabilities":["completion","vision","thinking"]}""", true)]
    [InlineData("""{"capabilities":["completion"]}""", false)]
    [InlineData("""{}""", false)]
    public void HasVision_ReadsCapabilities(string json, bool expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal(expected, OllamaProtocol.HasVision(document.RootElement));
    }

    [Fact]
    public void ParseStreamLine_ThrowsOnError()
    {
        Assert.Throws<InferenceException>(() => OllamaProtocol.ParseStreamLine("""{"error":"model not found"}"""));
    }

    [Theory]
    [InlineData(@"registry.ollama.ai\library\gemma4\26b", "gemma4:26b")]
    [InlineData(@"registry.ollama.ai\library\mael-ki\latest", "mael-ki:latest")]
    [InlineData(@"hf.co\bartowski\Qwen-GGUF\Q4_K_M", "hf.co/bartowski/Qwen-GGUF:Q4_K_M")]
    public void ModelStore_DerivesNamesFromManifestPath(string relativePath, string expected)
    {
        Assert.Equal(expected, OllamaModelStore.NameFromRelativePath(relativePath));
    }

    [Theory]
    [InlineData("gemma4:26b", "gemma4-26b")]
    [InlineData("qwen3.5:9b", "qwen3.5-9b")]
    [InlineData("mael-ki:latest", "mael-ki")]
    [InlineData("hf.co/user/Repo:Q4_K_M", "hf.co-user-repo-q4_k_m")]
    public void Importer_FolderNames_AreSafe(string ollamaName, string expected)
    {
        Assert.Equal(expected, OllamaImporter.FolderNameFor(ollamaName));
    }

    [Fact]
    public void Import_CreatesHardLinksAndModelJson_WithoutTouchingOllama()
    {
        using var ollamaDir = new TempDirectory();
        using var modelsDir = new TempDirectory();
        var store = CreateFakeOllama(ollamaDir);
        var model = Assert.Single(store.List());
        var blob = store.BlobPath(model.Model!);
        var blobWriteTime = File.GetLastWriteTimeUtc(blob);

        var local = CreateImporter(store, modelsDir).Import(model);

        Assert.Equal("gemma4-26b", local.FolderName);
        Assert.True(FileLinks.AreSameFile(local.ModelPath, blob));
        Assert.True(FileLinks.AreSameFile(local.MmprojPath!, store.BlobPath(model.Projector!)));
        Assert.Equal("Q4_K_M", local.Manifest.Quantization);
        Assert.True(local.Manifest.IsMoe);
        Assert.Equal("ollama:gemma4:26b", local.Manifest.Source);
        Assert.True(File.Exists(Path.Combine(local.FolderPath, "model.json")));
        Assert.Equal(blobWriteTime, File.GetLastWriteTimeUtc(blob));
    }

    [Fact]
    public void Import_Twice_IsFine()
    {
        using var ollamaDir = new TempDirectory();
        using var modelsDir = new TempDirectory();
        var store = CreateFakeOllama(ollamaDir);
        var importer = CreateImporter(store, modelsDir);

        importer.Import(store.List()[0]);
        var again = importer.Import(store.List()[0]);

        Assert.Equal(2, FileLinks.GetLinkCount(again.ModelPath));
    }

    [Fact]
    public void Import_RefusesToOverwriteOtherFile()
    {
        using var ollamaDir = new TempDirectory();
        using var modelsDir = new TempDirectory();
        var store = CreateFakeOllama(ollamaDir);
        Directory.CreateDirectory(modelsDir.Combine("gemma4-26b"));
        File.WriteAllText(modelsDir.Combine("gemma4-26b", "gemma4-26b.gguf"), "etwas anderes");

        Assert.Throws<IOException>(() => CreateImporter(store, modelsDir).Import(store.List()[0]));
    }

    /// <summary>Baut einen Ollama-Ordner nach: Manifest + Blobs (GGUF-Kopf als Modell).</summary>
    private static OllamaModelStore CreateFakeOllama(TempDirectory dir)
    {
        var manifestDir = Directory.CreateDirectory(dir.Combine("manifests", "registry.ollama.ai", "library", "gemma4")).FullName;
        Directory.CreateDirectory(dir.Combine("blobs"));
        GgufTestFile.Write(dir.Combine("blobs", "sha256-aaa"));
        File.WriteAllText(dir.Combine("blobs", "sha256-bbb"), "Projektor");
        File.WriteAllText(Path.Combine(manifestDir, "26b"), """
            {
              "schemaVersion": 2,
              "layers": [
                { "mediaType": "application/vnd.ollama.image.model", "digest": "sha256:aaa", "size": 100 },
                { "mediaType": "application/vnd.ollama.image.projector", "digest": "sha256:bbb", "size": 9 },
                { "mediaType": "application/vnd.ollama.image.params", "digest": "sha256:ccc", "size": 5 }
              ]
            }
            """);
        return new OllamaModelStore(dir.Path);
    }

    private static OllamaImporter CreateImporter(OllamaModelStore store, TempDirectory modelsDir) =>
        new(store, new ModelLibrary(modelsDir.Path, NullLogger<ModelLibrary>.Instance), NullLogger<OllamaImporter>.Instance);

    private static KiProfile Ki() => new()
    {
        Id = "qwen-schnell-ollama",
        Engine = EngineKind.Ollama,
        BaseModel = new BaseModelRef { OllamaName = "qwen3.5:9b" },
        Parameters = new KiParameters { Temperature = 1.0, TopP = 0.95, TopK = 20, PresencePenalty = 1.5 },
    };
}
