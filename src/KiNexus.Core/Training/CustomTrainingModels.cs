using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Training;

/// <summary>Was Hugging Face über ein Modell sagt (nur das, was das Training braucht).</summary>
/// <param name="ModelType">Aus config.json, z.B. "qwen3", "llama", "gemma3_text".</param>
/// <param name="Parameters">Anzahl Gewichte laut safetensors. Null = unbekannt (z.B. nur GGUF im Repository).</param>
/// <param name="HasChatTemplate">Hat der Tokenizer eine Chat-Vorlage? Ohne sie kann das Training kein Gespräch bauen.</param>
/// <param name="BaseModels">Aus der Modellkarte: Woraus wurde das Modell gemacht (bei GGUF-Repositories meist das Original).</param>
public sealed record TrainingModelInfo(string Repo, string? ModelType, long? Parameters, bool HasChatTemplate, bool Gated, string? License,
    IReadOnlyList<string> BaseModels);

/// <summary>
/// Eigene Grundmodelle fürs Training (Wunsch von Mael, 5.11.2026): jedes Originalmodell von Hugging Face bis ca. 5B Parameter,
/// dessen Bauart PEFT und llama.cpp kennen. Gespeichert in data/training/eigene-modelle.json.
/// </summary>
public sealed class CustomTrainingModels(string file, ILogger<CustomTrainingModels> logger)
{
    /// <summary>Grösser passt auch mit 4 Bit nicht mehr sicher in 8 GB VRAM (Bauplan 8.6, ehrliche Grenzen).</summary>
    public const double MaxParametersB = 5.0;

    /// <summary>Ab dieser Grösse nur mit 4 Bit (QLoRA), wie bei Qwen3 4B.</summary>
    public const double Use4BitAboveB = 3.0;

    /// <summary>
    /// Bauarten, deren Schichten q_proj … down_proj heissen (so trainiert train.py) und die convert_hf_to_gguf.py kennt.
    /// Nicht dabei z.B. phi3 (zusammengelegte Schichten) und Bild-Text-Modelle (gemma3, qwen2_vl).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "qwen2", "qwen3", "llama", "mistral", "gemma", "gemma2", "gemma3_text", "granite", "smollm3", "olmo2",
    };

    /// <summary>Kann man dieses Modell hier trainieren? Ja: das fertige Grundmodell. Nein: der Grund in einfachen Worten.</summary>
    public static (TrainingBaseModel? Model, string Note) Evaluate(TrainingModelInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.ModelType is null || info.Parameters is null)
        {
            return (null, info.BaseModels.Count > 0
                ? $"{info.Repo} enthält kein Originalmodell (safetensors), vermutlich nur GGUF. Das Original ist wohl {info.BaseModels[0]}. Prüf das."
                : $"{info.Repo} enthält kein Originalmodell (safetensors). Training braucht das Original, nicht eine GGUF-Datei.");
        }

        var billions = info.Parameters.Value / 1e9;
        var size = billions.ToString("0.#", CultureInfo.InvariantCulture);
        if (billions > MaxParametersB)
        {
            return (null, $"{info.Repo} hat {size}B Parameter. Mit 8 GB VRAM geht Training nur bis ca. {MaxParametersB:0}B.");
        }

        if (!SupportedTypes.Contains(info.ModelType))
        {
            return (null, $"Die Bauart \"{info.ModelType}\" kann das Training (noch) nicht. Es geht: {string.Join(", ", SupportedTypes.Order())}.");
        }

        if (!info.HasChatTemplate)
        {
            return (null, $"{info.Repo} hat keine Chat-Vorlage (wohl ein Basis-Modell). Nimm die Instruct- oder Chat-Variante.");
        }

        var use4Bit = billions > Use4BitAboveB;
        var name = info.Repo.Contains('/', StringComparison.Ordinal) ? info.Repo[(info.Repo.IndexOf('/') + 1)..] : info.Repo;
        var license = string.IsNullOrWhiteSpace(info.License) ? "unbekannt" : info.License!;
        var note = $"Eigenes Modell ({info.ModelType})."
                   + (use4Bit ? " Nur mit 4 Bit (QLoRA), Training dauert länger." : "")
                   + (info.Gated ? " Gesperrt (gated): Bedingungen auf Hugging Face akzeptieren und den Token im Create-Hub hinterlegen." : "");
        var model = new TrainingBaseModel(info.Repo, name, Math.Round(billions, 1), Math.Round(billions * 2.0, 1), use4Bit, license, note) { IsCustom = true };
        return (model, $"{info.Repo}: {size}B Parameter, Bauart {info.ModelType}, Lizenz {license}. Trainierbar" + (use4Bit ? " mit 4 Bit." : "."));
    }

    public IReadOnlyList<TrainingBaseModel> Load()
    {
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var models = JsonSerializer.Deserialize<List<TrainingBaseModel>>(File.ReadAllText(file), JsonDefaults.Options) ?? [];
            return [.. models.Where(m => TrainingModels.IsValidRepo(m.Repo)).Select(m => m with { IsCustom = true }).DistinctBy(m => m.Repo, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Eigene Trainings-Modelle nicht lesbar: {File}", file);
            return [];
        }
    }

    /// <summary>Hinzufügen (oder ersetzen, wenn es den Repo schon gibt).</summary>
    public IReadOnlyList<TrainingBaseModel> Add(TrainingBaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var all = Load().Where(m => !string.Equals(m.Repo, model.Repo, StringComparison.OrdinalIgnoreCase)).Append(model with { IsCustom = true }).ToList();
        Write(all);
        return Load();
    }

    /// <summary>Aus der Liste nehmen. Ein schon geladenes Originalmodell (models/hf) bleibt auf der Festplatte.</summary>
    public IReadOnlyList<TrainingBaseModel> Remove(string repo)
    {
        Write([.. Load().Where(m => !string.Equals(m.Repo, repo, StringComparison.OrdinalIgnoreCase))]);
        return Load();
    }

    private void Write(IReadOnlyList<TrainingBaseModel> models)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(models, JsonDefaults.Options));
    }
}
