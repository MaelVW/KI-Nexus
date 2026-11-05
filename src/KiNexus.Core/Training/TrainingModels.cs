using System.Globalization;

namespace KiNexus.Core.Training;

/// <summary>Ein Grundmodell, das man auf diesem Laptop trainieren kann (Originalmodell als safetensors von Hugging Face).</summary>
/// <param name="Repo">z.B. "Qwen/Qwen3-1.7B".</param>
/// <param name="ParametersB">Anzahl Parameter in Milliarden.</param>
/// <param name="DownloadGb">Grösse des Downloads.</param>
/// <param name="Use4Bit">Nur mit 4 Bit (QLoRA) passt das Modell in 8 GB VRAM.</param>
public sealed record TrainingBaseModel(string Repo, string Name, double ParametersB, double DownloadGb, bool Use4Bit, string License, string Note)
{
    public string Title => $"{Name} · {ParametersB.ToString("0.#", CultureInfo.InvariantCulture)}B{(IsCustom ? " · eigenes" : "")}";

    public string Details => string.Create(CultureInfo.InvariantCulture,
        $"{Repo} · Download {DownloadGb:0.#} GB · Lizenz {License}{(Use4Bit ? " · nur mit 4 Bit (QLoRA)" : "")}");

    /// <summary>Ordnername in models/, z.B. "qwen3-1.7b".</summary>
    public string Slug => TrainingModels.Slug(Repo);

    /// <summary>Selbst hinzugefügt (data/training/eigene-modelle.json), nicht aus der Empfehlung.</summary>
    public bool IsCustom { get; init; }

    public override string ToString() => Title;
}

/// <summary>Einstellungen eines Trainings. Die Standardwerte passen für kleine Datensätze (20–500 Beispiele).</summary>
/// <param name="Epochs">Wie oft das Modell den ganzen Datensatz sieht.</param>
/// <param name="LearningRate">Wie gross die Schritte beim Lernen sind.</param>
/// <param name="LoraRank">Grösse des Adapters. Mehr = kann mehr lernen, braucht mehr Speicher.</param>
/// <param name="MaxLength">Längstes Beispiel in Token. Längere werden abgeschnitten.</param>
public sealed record TrainingOptions(int Epochs = 3, double LearningRate = 2e-4, int LoraRank = 16, int MaxLength = 512);

/// <summary>
/// Was mit 8 GB VRAM geht (Bauplan 8.6, ehrliche Grenzen): LoRA bzw. QLoRA für Modelle bis ca. 4B Parameter.
/// 9B ist knapp, gemma4:26b geht nicht.
/// </summary>
public static class TrainingModels
{
    /// <summary>
    /// Ausgesucht: frei (Apache 2.0), ohne Anmeldung (nicht gated), Text-Modelle, die llama.cpp und PEFT gut kennen.
    /// Qwen3 kann Deutsch. Stand Oktober 2026.
    /// </summary>
    public static IReadOnlyList<TrainingBaseModel> Recommended { get; } =
    [
        new("Qwen/Qwen3-1.7B", "Qwen3 1.7B", 2.0, 4.1, false, "Apache 2.0", "Empfohlen: gutes Deutsch für die Grösse, Training in wenigen Minuten."),
        new("Qwen/Qwen3-0.6B", "Qwen3 0.6B", 0.6, 1.5, false, "Apache 2.0", "Sehr schnell, gut zum Ausprobieren. Antwortet einfacher."),
        new("Qwen/Qwen3-4B", "Qwen3 4B", 4.0, 8.1, true, "Apache 2.0", "Klüger, aber knapp: nur mit 4 Bit, Training dauert länger."),
    ];

    /// <summary>Schätzung des VRAM fürs Training: Modell (bf16 oder 4 Bit) + Adapter, Optimierer und Rechenspeicher.</summary>
    public static double EstimateVramGb(double parametersB, bool use4Bit) =>
        Math.Round((parametersB * (use4Bit ? 0.65 : 2.0)) + 1.6, 1);

    /// <summary>"Qwen/Qwen3-1.7B" → "qwen3-1.7b".</summary>
    public static string Slug(string repo)
    {
        var name = repo.Contains('/', StringComparison.Ordinal) ? repo[(repo.LastIndexOf('/') + 1)..] : repo;
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-');
        return string.Concat(chars).Trim('-', '.');
    }

    /// <summary>Gültiger Name für einen Hugging-Face-Repo ("besitzer/modell").</summary>
    public static bool IsValidRepo(string? repo) =>
        repo is { Length: > 2 } && repo.Count(c => c == '/') == 1 && !repo.StartsWith('/') && !repo.EndsWith('/')
        && repo.All(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.');
}
