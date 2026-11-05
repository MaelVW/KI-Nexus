using System.Text.Json;
using KiNexus.Engines.HuggingFace;

namespace KiNexus.Engines.Tests;

/// <summary>Angaben von Hugging Face für eigene Grundmodelle im Training.</summary>
public class TrainingInfoTests
{
    [Fact]
    public void ParseTrainingInfo_ReadsTypeSizeTemplateAndLicense()
    {
        using var json = JsonDocument.Parse("""
            { "id": "Qwen/Qwen3-1.7B", "gated": false,
              "config": { "model_type": "qwen3", "tokenizer_config": { "chat_template": "{{ messages }}" } },
              "cardData": { "license": "apache-2.0", "base_model": ["Qwen/Qwen3-1.7B-Base"] },
              "safetensors": { "parameters": { "BF16": 2031739904 }, "total": 2031739904 } }
            """);

        var info = HuggingFaceProtocol.ParseTrainingInfo("qwen/qwen3-1.7b", json.RootElement);

        Assert.Equal("Qwen/Qwen3-1.7B", info.Repo);
        Assert.Equal("qwen3", info.ModelType);
        Assert.Equal(2031739904, info.Parameters);
        Assert.True(info.HasChatTemplate);
        Assert.False(info.Gated);
        Assert.Equal("apache-2.0", info.License);
        Assert.Equal(["Qwen/Qwen3-1.7B-Base"], info.BaseModels);
    }

    [Fact]
    public void ParseTrainingInfo_GgufRepositoryHasNoSizeButAnOriginal()
    {
        using var json = JsonDocument.Parse("""{ "id": "unsloth/Qwen3-1.7B-GGUF", "gated": "auto", "cardData": { "base_model": "Qwen/Qwen3-1.7B" } }""");

        var info = HuggingFaceProtocol.ParseTrainingInfo("unsloth/Qwen3-1.7B-GGUF", json.RootElement);

        Assert.Null(info.ModelType);
        Assert.Null(info.Parameters);
        Assert.False(info.HasChatTemplate);
        Assert.True(info.Gated);
        Assert.Equal(["Qwen/Qwen3-1.7B"], info.BaseModels);
    }

    [Fact]
    public void ParseTrainingInfo_FindsChatTemplateFile()
    {
        using var json = JsonDocument.Parse("""
            { "id": "HuggingFaceTB/SmolLM3-3B", "config": { "model_type": "smollm3", "tokenizer_config": { "eos_token": "x" } },
              "safetensors": { "total": 3075098624 }, "siblings": [ { "rfilename": "config.json" }, { "rfilename": "chat_template.jinja" } ] }
            """);

        Assert.True(HuggingFaceProtocol.ParseTrainingInfo("x", json.RootElement).HasChatTemplate);
    }

    [Fact]
    public void ModelInfoPath_AsksOnlyForWhatTrainingNeeds() =>
        Assert.Equal("api/models/Qwen/Qwen3-1.7B?expand[]=config&expand[]=safetensors&expand[]=gated&expand[]=cardData&expand[]=siblings",
            HuggingFaceProtocol.ModelInfoPath("Qwen/Qwen3-1.7B"));
}
