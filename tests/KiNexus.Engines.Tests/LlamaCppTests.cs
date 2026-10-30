using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Paths;
using KiNexus.Engines.LlamaCpp;

namespace KiNexus.Engines.Tests;

public class LlamaCppTests
{
    private static readonly AppPaths Paths = new(@"C:\Projekt");

    [Fact]
    public void Arguments_ContainModelPortContextAndSafeDefaults()
    {
        var args = LlamaServerArguments.Build(Ki(), Paths, 8090, @"C:\Projekt\data\logs\llama-server.log");

        AssertPair(args, "-m", @"C:\Projekt\models\gemma4-26b\gemma4-26b.gguf");
        AssertPair(args, "--mmproj", @"C:\Projekt\models\gemma4-26b\mmproj.gguf");
        AssertPair(args, "--host", "127.0.0.1");
        AssertPair(args, "--port", "8090");
        AssertPair(args, "-c", "8192");
        AssertPair(args, "-lm", "none");
        AssertPair(args, "-a", "mael-ki");
        Assert.Contains("--no-webui", args);
        Assert.Contains("--offline", args);
        Assert.DoesNotContain("-ngl", args);
        Assert.DoesNotContain("--n-cpu-moe", args);
    }

    [Fact]
    public void Arguments_UseManualGpuSettings_WhenSet()
    {
        var ki = Ki();
        ki.EngineOptions.GpuLayers = "20";
        ki.EngineOptions.CpuMoeLayers = 25;
        ki.LoraAdapterPath = "kis/adapter.gguf";

        var args = LlamaServerArguments.Build(ki, Paths, 8090, "log.txt");

        AssertPair(args, "-ngl", "20");
        AssertPair(args, "--n-cpu-moe", "25");
        AssertPair(args, "--lora", @"C:\Projekt\kis\adapter.gguf");
    }

    [Fact]
    public void Arguments_WithoutModelPath_GiveClearError()
    {
        var ki = Ki();
        ki.BaseModel.Path = null;

        var error = Assert.Throws<InferenceException>(() => LlamaServerArguments.Build(ki, Paths, 8090, "log.txt"));
        Assert.Contains("baseModel.path", error.Message);
    }

    [Fact]
    public void Request_ThinkingOff_IsExplicit()
    {
        var body = LlamaCppProtocol.BuildChatRequest(Request(think: false), stream: true);

        Assert.Equal("none", body["reasoning_effort"]!.GetValue<string>());
        Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.True(body["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void Request_ThinkingOn_SendsSamplingFromProfile()
    {
        var body = LlamaCppProtocol.BuildChatRequest(Request(think: true), stream: false);

        Assert.Null(body["reasoning_effort"]);
        Assert.True(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Equal(1.0, body["temperature"]!.GetValue<double>());
        Assert.Equal(64, body["top_k"]!.GetValue<int>());
        Assert.Null(body["min_p"]);
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal(2048, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void ParseStreamLine_ReadsTextThinkingAndTimings()
    {
        var text = LlamaCppProtocol.ParseStreamLine("""data: {"choices":[{"index":0,"delta":{"content":"Grüezi"}}]}""");
        var thinking = LlamaCppProtocol.ParseStreamLine("""data: {"choices":[{"index":0,"delta":{"reasoning_content":"Hm…"}}]}""");
        var last = LlamaCppProtocol.ParseStreamLine("""data: {"choices":[],"timings":{"prompt_n":35,"prompt_ms":50.0,"prompt_per_second":700.0,"predicted_n":95,"predicted_ms":2400.0,"predicted_per_second":39.6}}""");

        Assert.Equal("Grüezi", text!.Text);
        Assert.Equal("Hm…", thinking!.Thinking);
        Assert.Equal(95, last!.Timings!.PredictedTokens);
        Assert.Equal(39.6, last.Timings.PredictedPerSecond);
    }

    [Theory]
    [InlineData("")]
    [InlineData(": ping")]
    [InlineData("data: [DONE]")]
    public void ParseStreamLine_IgnoresNonData(string line)
    {
        Assert.Null(LlamaCppProtocol.ParseStreamLine(line));
    }

    [Fact]
    public void ParseStreamLine_ThrowsOnServerError()
    {
        var error = Assert.Throws<InferenceException>(() =>
            LlamaCppProtocol.ParseStreamLine("""data: {"error":{"code":400,"message":"context too long"}}"""));
        Assert.Contains("context too long", error.Message);
    }

    [Fact]
    public void ToStats_UsesServerTimings()
    {
        var stats = LlamaCppProtocol.ToStats(new LlamaTimings(35, 50, 700, 95, 2400, 39.6), TimeSpan.FromSeconds(2.5));

        Assert.Equal(EngineKind.LlamaCpp, stats.Engine);
        Assert.Equal(95, stats.CompletionTokens);
        Assert.Equal(39.6, stats.TokensPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(2.5), stats.Duration);
    }

    private static KiProfile Ki() => new()
    {
        Id = "mael-ki",
        DisplayName = "Mael-KI",
        Engine = EngineKind.LlamaCpp,
        BaseModel = new BaseModelRef { Path = "models/gemma4-26b/gemma4-26b.gguf", MmprojPath = "models/gemma4-26b/mmproj.gguf" },
        SystemPrompt = "Antworte auf Deutsch.",
        Parameters = new KiParameters { Temperature = 1.0, TopP = 0.95, TopK = 64 },
    };

    private static ChatRequest Request(bool think)
    {
        var ki = Ki();
        return new ChatRequest(ki, PromptBuilder.Build(ki.SystemPrompt, [], "Hallo"), think);
    }

    private static void AssertPair(IReadOnlyList<string> args, string flag, string value)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"{flag} fehlt");
        Assert.Equal(value, args[index + 1]);
    }
}
