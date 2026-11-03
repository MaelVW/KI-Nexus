using KiNexus.Core.Training;

namespace KiNexus.Core.Tests;

/// <summary>Versionen beim Weitertrainieren (Bauplan Training 2, T1).</summary>
public class TrainingHistoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scan_ReadsLegacyRunAsV1_AndNewVersions()
    {
        using var root = new TempDirectory();
        var runs = root.Combine("runs");
        var legacy = Path.Combine(runs, "nexi");
        var gguf1 = WriteFile(root.Combine("models", "qwen3-1.7b", "lora", "nexi.gguf"));
        WriteAdapter(legacy);
        File.WriteAllText(Path.Combine(legacy, "dataset.jsonl"), "{}\n{}\n\n{}\n");
        File.WriteAllText(Path.Combine(legacy, "run.json"), $$"""
            { "baseRepo": null, "baseModelDir": "C:\\x\\models\\hf\\qwen3-1.7b", "epochs": 5, "learningRate": 0.0002, "loraRank": 16,
              "adapterGgufPath": {{System.Text.Json.JsonSerializer.Serialize(gguf1)}} }
            """);

        var (number, runDir) = TrainingHistory.Next(runs, "nexi", TrainingHistory.Scan(runs).Single());
        Assert.Equal(2, number);
        Assert.Equal(Path.Combine(legacy, "v2"), runDir);
        WriteAdapter(runDir);
        var gguf2 = WriteFile(root.Combine("models", "qwen3-1.7b", "lora", "nexi-v2.gguf"));
        await TrainingHistory.WriteAsync(new AdapterVersion
        {
            Name = "nexi", Number = 2, Parent = 1, BaseRepo = "Qwen/Qwen3-1.7B", LoraRank = 16, Epochs = 3, LearningRate = 1e-4, ExampleCount = 10,
            FinalLoss = 0.4, Losses = [1.2, 0.8, 0.4], AdapterGguf = gguf2, RunDir = runDir,
        }, Ct);

        var lineage = Assert.Single(TrainingHistory.Scan(runs));

        Assert.Equal("nexi", lineage.Name);
        Assert.Equal([1, 2], lineage.Versions.Select(v => v.Number));
        var v1 = lineage.Versions[0];
        Assert.Equal("Qwen/Qwen3-1.7B", v1.BaseRepo); // aus dem Ordnernamen des Grundmodells
        Assert.Equal(3, v1.ExampleCount);
        Assert.Equal(Path.Combine(legacy, "adapter"), v1.AdapterDir);
        Assert.Equal(lineage.Versions[1], lineage.Latest);
        Assert.Equal([1, 2], lineage.Chain().Select(v => v.Number));
        Assert.Contains("weiter von v1", lineage.Latest.Label, StringComparison.Ordinal);
        Assert.Equal(3, TrainingHistory.Next(runs, "nexi", lineage).Number);
    }

    [Fact]
    public async Task Scan_SkipsUnfinishedRounds()
    {
        using var root = new TempDirectory();
        var runs = root.Combine("runs");
        var dir = Path.Combine(runs, "stil", "v1");
        await TrainingHistory.WriteAsync(new AdapterVersion { Name = "stil", Number = 1, AdapterGguf = root.Combine("fehlt.gguf"), RunDir = dir }, Ct);

        Assert.Empty(TrainingHistory.Scan(runs));
        Assert.Equal(1, TrainingHistory.Next(runs, "stil", null).Number); // halbe Runde darf überschrieben werden
        Assert.Empty(TrainingHistory.Scan(root.Combine("gibt-es-nicht")));
    }

    [Fact]
    public void Chain_FollowsParents()
    {
        var versions = new[]
        {
            new AdapterVersion { Name = "a", Number = 1 },
            new AdapterVersion { Name = "a", Number = 2, Parent = 1 },
            new AdapterVersion { Name = "a", Number = 3, Parent = 1 }, // zurück zu v1, dann weiter
        };
        var lineage = new AdapterLineage("a", versions);

        Assert.Equal([1, 3], lineage.Chain().Select(v => v.Number));
        Assert.Equal([1, 2], lineage.Chain(versions[1]).Select(v => v.Number));
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(0.3, false)]
    [InlineData(null, false)]
    public void Memorized_WhenLossNearZero(double? loss, bool expected) => Assert.Equal(expected, TrainingHistory.LooksMemorized(loss));

    private static void WriteAdapter(string runDir)
    {
        Directory.CreateDirectory(Path.Combine(runDir, "adapter"));
        File.WriteAllText(Path.Combine(runDir, "adapter", "adapter_config.json"), "{}");
    }

    private static string WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "gguf");
        return path;
    }
}
