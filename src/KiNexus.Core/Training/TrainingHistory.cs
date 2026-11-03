using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KiNexus.Core.Training;

/// <summary>
/// Eine Runde Training (Bauplan Training 2, T1): ein Adapter mit allem, was man zum Weitertrainieren und Zurückgehen braucht.
/// Liegt als version.json im Arbeitsordner data/training/runs/&lt;name&gt;/v&lt;n&gt;/.
/// </summary>
public sealed record AdapterVersion
{
    /// <summary>Name des Adapters (alle Versionen gleich), z.B. "nexi".</summary>
    public required string Name { get; init; }

    /// <summary>1, 2, 3 …</summary>
    public int Number { get; init; }

    /// <summary>Von welcher Version diese Runde ausging. Null = neu begonnen.</summary>
    public int? Parent { get; init; }

    /// <summary>Hugging-Face-Repo des Grundmodells, z.B. "Qwen/Qwen3-1.7B".</summary>
    public string BaseRepo { get; init; } = "";

    public int LoraRank { get; init; }
    public int Epochs { get; init; }
    public double LearningRate { get; init; }
    public int ExampleCount { get; init; }
    public double? FinalLoss { get; init; }

    /// <summary>Loss nach jedem Schritt, für die Kurve über alle Runden.</summary>
    public IReadOnlyList<double> Losses { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Der Adapter als GGUF für llama.cpp, z.B. models/qwen3-1.7b/lora/nexi-v2.gguf.</summary>
    public string AdapterGguf { get; init; } = "";

    /// <summary>Arbeitsordner dieser Runde. Wird beim Lesen gesetzt.</summary>
    [JsonIgnore] public string RunDir { get; init; } = "";

    /// <summary>Der Adapter als safetensors (PEFT). Den braucht das Weitertrainieren.</summary>
    [JsonIgnore] public string AdapterDir => Path.Combine(RunDir, "adapter");

    /// <summary>Fertig: Adapter für PEFT und GGUF für llama.cpp sind da.</summary>
    [JsonIgnore] public bool IsComplete => File.Exists(Path.Combine(AdapterDir, "adapter_config.json")) && File.Exists(AdapterGguf);

    /// <summary>z.B. "v2 · 10 Beispiele · 3 Durchgänge · Loss 0.42 · 03.11. 11:30".</summary>
    [JsonIgnore]
    public string Label =>
        string.Create(CultureInfo.InvariantCulture, $"v{Number} · {ExampleCount} Beispiele · {Epochs} Durchgänge")
        + (FinalLoss is { } loss ? string.Create(CultureInfo.InvariantCulture, $" · Loss {loss:0.00}") : "")
        + (CreatedAt is { } at ? string.Create(CultureInfo.InvariantCulture, $" · {at.ToLocalTime():dd.MM. HH:mm}") : "")
        + (Parent is { } parent ? string.Create(CultureInfo.InvariantCulture, $" · weiter von v{parent}") : "");

    public override string ToString() => Label;
}

/// <summary>Alle Versionen eines Adapters, die älteste zuerst.</summary>
public sealed record AdapterLineage(string Name, IReadOnlyList<AdapterVersion> Versions)
{
    public AdapterVersion Latest => Versions[^1];

    public string BaseRepo => Latest.BaseRepo;

    /// <summary>Die Versionen, von denen die neueste abstammt (inkl. ihr selbst), für die Loss-Kurve über alle Runden.</summary>
    public IReadOnlyList<AdapterVersion> Chain(AdapterVersion? from = null)
    {
        var chain = new List<AdapterVersion>();
        for (var current = from ?? Latest; current is not null; current = current.Parent is { } parent ? Versions.FirstOrDefault(v => v.Number == parent) : null)
        {
            chain.Insert(0, current);
            if (chain.Count > Versions.Count)
            {
                break; // Schutz vor einer kaputten Kette (v2 → v2).
            }
        }

        return chain;
    }

    public override string ToString() => $"{Name} (v{Latest.Number}, {BaseRepo})";
}

/// <summary>
/// Kennt die Trainings in data/training/runs (Bauplan Training 2, T1). Seit T1 bleibt jeder Arbeitsordner:
/// runs/&lt;name&gt;/v1, v2, … je mit version.json, run.json, dataset.jsonl und adapter/.
/// Ältere Trainings (vor T1) liegen direkt in runs/&lt;name&gt;/ und gelten als v1.
/// </summary>
public static class TrainingHistory
{
    public const string VersionFile = "version.json";

    /// <summary>
    /// Fast null Loss heisst meist: Die KI kennt die Beispiele auswendig. Dann wiederholt sie sich oder vergisst Allgemeines.
    /// </summary>
    public const double MemorizedLoss = 0.05;

    /// <summary>Alle Adapter mit ihren Versionen, nach Name. Unlesbare Ordner fallen weg.</summary>
    public static IReadOnlyList<AdapterLineage> Scan(string runsDirectory)
    {
        if (!Directory.Exists(runsDirectory))
        {
            return [];
        }

        var lineages = new List<AdapterLineage>();
        foreach (var folder in Directory.EnumerateDirectories(runsDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(folder);
            var versions = new List<AdapterVersion>();
            if (ReadLegacy(folder, name) is { } legacy)
            {
                versions.Add(legacy);
            }

            foreach (var versionFolder in Directory.EnumerateDirectories(folder, "v*"))
            {
                if (int.TryParse(Path.GetFileName(versionFolder)[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && Read(versionFolder) is { } version)
                {
                    versions.Add(version with { Name = name, Number = number, RunDir = versionFolder });
                }
            }

            versions = [.. versions.Where(v => v.IsComplete).DistinctBy(v => v.Number).OrderBy(v => v.Number)];
            if (versions.Count > 0)
            {
                lineages.Add(new AdapterLineage(name, versions));
            }
        }

        return lineages;
    }

    /// <summary>Ordner für die nächste Runde, z.B. runs/nexi/v3. Ohne frühere Runde: v1.</summary>
    public static (int Number, string RunDir) Next(string runsDirectory, string name, AdapterLineage? lineage)
    {
        var number = (lineage?.Versions.Max(v => v.Number) ?? 0) + 1;
        var folder = Path.Combine(runsDirectory, name);
        // Halbe Runden (abgebrochen, ohne Adapter) nicht überschreiben, sondern überspringen.
        while (Directory.Exists(Path.Combine(folder, $"v{number}")) && Read(Path.Combine(folder, $"v{number}")) is { IsComplete: true })
        {
            number++;
        }

        return (number, Path.Combine(folder, $"v{number}"));
    }

    public static AdapterVersion? Read(string versionFolder)
    {
        var file = Path.Combine(versionFolder, VersionFile);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var version = JsonSerializer.Deserialize<AdapterVersion>(File.ReadAllText(file), JsonDefaults.Options);
            return version is null ? null : version with { RunDir = versionFolder };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(AdapterVersion version, CancellationToken ct)
    {
        Directory.CreateDirectory(version.RunDir);
        await File.WriteAllTextAsync(Path.Combine(version.RunDir, VersionFile), JsonSerializer.Serialize(version, JsonDefaults.Options), ct)
            .ConfigureAwait(false);
    }

    public static bool LooksMemorized(double? finalLoss) => finalLoss is { } loss && loss < MemorizedLoss;

    /// <summary>Training vor T1: runs/&lt;name&gt;/run.json und adapter/ direkt im Ordner. Gilt als v1.</summary>
    private static AdapterVersion? ReadLegacy(string folder, string name)
    {
        var config = Path.Combine(folder, "run.json");
        if (!File.Exists(config) || !File.Exists(Path.Combine(folder, "adapter", "adapter_config.json")))
        {
            return null;
        }

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(config))?.AsObject();
            if (json is null)
            {
                return null;
            }

            var baseDir = json["baseModelDir"]?.GetValue<string>() ?? "";
            var slug = Path.GetFileName(baseDir.TrimEnd('\\', '/'));
            var repo = json["baseRepo"]?.GetValue<string>()
                       ?? TrainingModels.Recommended.FirstOrDefault(m => m.Slug == slug)?.Repo
                       ?? slug;
            var examples = File.Exists(Path.Combine(folder, "dataset.jsonl"))
                ? File.ReadLines(Path.Combine(folder, "dataset.jsonl")).Count(line => line.Trim().Length > 0)
                : 0;
            return new AdapterVersion
            {
                Name = name,
                Number = 1,
                BaseRepo = repo,
                LoraRank = json["loraRank"]?.GetValue<int>() ?? 16,
                Epochs = json["epochs"]?.GetValue<int>() ?? 0,
                LearningRate = json["learningRate"]?.GetValue<double>() ?? 0,
                ExampleCount = examples,
                AdapterGguf = json["adapterGgufPath"]?.GetValue<string>() ?? "",
                CreatedAt = File.GetLastWriteTime(Path.Combine(folder, "adapter", "adapter_config.json")),
                RunDir = folder,
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
