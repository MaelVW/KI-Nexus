using System.Text.Json;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class KiRegistryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_ReadsProfiles_InBlueprintFormat()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.Combine("mael-ki.json"), """
            {
              "id": "mael-ki",
              "displayName": "Mael-KI",
              "engine": "llamacpp",
              "baseModel": { "path": "models/gemma4-26b/gemma4-26b.gguf", "mmprojPath": "models/gemma4-26b/mmproj.gguf", "ollamaName": null },
              "systemPrompt": "Du bist ein hilfsbereiter Assistent.",
              "parameters": { "temperature": 1.0, "topP": 0.95, "topK": 64, "contextLength": 8192, "maxTokens": 2048, "thinkByDefault": false },
              "engineOptions": { "gpuLayers": "auto", "cpuMoeLayers": null, "keepAliveMinutes": 5 },
              "voice": { "defaultMode": "pushToTalk" }
            }
            """, Ct);

        var kis = await CreateRegistry(temp).LoadAsync(Ct);

        var ki = Assert.Single(kis);
        Assert.Equal(EngineKind.LlamaCpp, ki.Engine);
        Assert.Equal(64, ki.Parameters.TopK);
        Assert.True(ki.SupportsImages);
        Assert.Equal("llama.cpp", ki.EngineLabel);
    }

    [Fact]
    public async Task Load_UsesFileNameAsId_AndSkipsBrokenFiles()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.Combine("qwen.json"), """{ "id": "falsch", "engine": "ollama", "baseModel": { "ollamaName": "qwen3.5:9b" } }""", Ct);
        await File.WriteAllTextAsync(temp.Combine("kaputt.json"), "{ nicht json", Ct);

        var registry = CreateRegistry(temp);
        var kis = await registry.LoadAsync(Ct);

        var ki = Assert.Single(kis);
        Assert.Equal("qwen", ki.Id);
        Assert.Equal("qwen", ki.DisplayName);
        Assert.Equal(EngineKind.Ollama, ki.Engine);
        Assert.Same(ki, registry.Find("QWEN"));
    }

    [Fact]
    public async Task Load_WithoutFolder_ReturnsEmptyList()
    {
        using var temp = new TempDirectory();

        var kis = await new KiRegistry(temp.Combine("gibt-es-nicht"), NullLogger<KiRegistry>.Instance).LoadAsync(Ct);

        Assert.Empty(kis);
    }

    [Fact]
    public void Engine_IsWrittenAsLowercaseName()
    {
        var json = JsonSerializer.Serialize(new KiProfile { Engine = EngineKind.LlamaCpp }, JsonDefaults.Options);

        Assert.Contains("\"engine\": \"llamacpp\"", json);
    }

    [Fact]
    public void SortForDisplay_PutsPreferredEngineFirst()
    {
        var kis = new[]
        {
            new KiProfile { Id = "a", DisplayName = "A", Engine = EngineKind.Ollama },
            new KiProfile { Id = "b", DisplayName = "B", Engine = EngineKind.LlamaCpp },
        };

        Assert.Equal("b", KiRegistry.SortForDisplay(kis, EngineOrder.LlamaCppFirst)[0].Id);
        Assert.Equal("a", KiRegistry.SortForDisplay(kis, EngineOrder.OllamaFirst)[0].Id);
    }

    private static KiRegistry CreateRegistry(TempDirectory temp) => new(temp.Path, NullLogger<KiRegistry>.Instance);
}
