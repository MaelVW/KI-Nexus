using System.Text.RegularExpressions;
using KiNexus.Core.Models;

namespace KiNexus.Core.Video;

/// <summary>Welcher Ablauf zum Modell passt. distilled: 8 feste Schritte, CFG 1. dev: 20 Schritte mit CFG 4.</summary>
public enum VideoModelVariant
{
    Distilled,
    Dev,

    /// <summary>Steht nicht im Dateinamen (z.B. bei einem Finetune). Im Video-Tab wählbar.</summary>
    Unknown,
}

/// <summary>Ein Videomodell zur Auswahl im Video-Tab, mit Urteil: Kann der Video-Tab es nutzen?</summary>
/// <param name="Name">Anzeigename, z.B. "LTX-2.3 distilled (Q4_K_M)" oder der Ordnername aus models/.</param>
/// <param name="ModelPath">Die GGUF-Datei.</param>
/// <param name="IsBuiltIn">Liegt in models/video/diffusion_models (eingerichtet mit setup-video.ps1).</param>
/// <param name="IsUsable">LTX-2.3 mit Ton, distilled oder unbekannt. Andere Videomodelle kann der Video-Tab (noch) nicht.</param>
/// <param name="Note">Ein Satz zum Modell für die Anzeige.</param>
public sealed record VideoModelChoice(string Name, string ModelPath, bool IsBuiltIn, bool IsUsable, string Note,
    VideoModelVariant Variant = VideoModelVariant.Unknown)
{
    public string FileName => Path.GetFileName(ModelPath);

    /// <summary>
    /// Die Dateien für ComfyUI: das Modell und die gemeinsamen Zusatzdateien von LTX-2.3.
    /// </summary>
    /// <param name="dev">Ablauf erzwingen (Wahl im Video-Tab). Null = aus <see cref="Variant"/>, unbekannt = distilled.</param>
    public VideoModelSet ToModelSet(bool? dev = null)
    {
        var set = VideoModelSet.Ltx23Distilled with
        {
            Name = Name,
            Model = FileName,
            ModelFolder = IsBuiltIn ? null : Path.GetDirectoryName(ModelPath),
        };
        return dev ?? Variant == VideoModelVariant.Dev ? set.AsDev() : set;
    }

    public override string ToString() => IsUsable ? Name : $"{Name} (nicht nutzbar)";
}

/// <summary>
/// Findet die Videomodelle für den Video-Tab: die eingebauten in models/video/diffusion_models und
/// Grundmodelle mit Architektur "ltxv" in models/ (z.B. von Hugging Face). Der Video-Tab kann bisher nur
/// LTX-2.3 mit Ton und die Art "distilled" (8 Schritte). Ob ein Modell passt, steht im GGUF-Kopf.
/// </summary>
public static partial class VideoModelCatalog
{
    public const string LtxArchitecture = "ltxv";

    /// <summary>Alle Videomodelle, die nutzbaren zuerst. Unlesbare Dateien fallen weg.</summary>
    public static IReadOnlyList<VideoModelChoice> Find(string videoModelsDirectory, IEnumerable<LocalModel> libraryModels)
    {
        var choices = new List<VideoModelChoice>();
        var builtIn = Path.Combine(videoModelsDirectory, "diffusion_models");
        if (Directory.Exists(builtIn))
        {
            foreach (var file in Directory.EnumerateFiles(builtIn, "*.gguf").Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = file.EndsWith(VideoModelSet.Ltx23Distilled.Model, StringComparison.OrdinalIgnoreCase)
                    ? VideoModelSet.Ltx23Distilled.Name
                    : Path.GetFileNameWithoutExtension(file);
                AddIfReadable(choices, file, name, isBuiltIn: true);
            }
        }

        foreach (var model in libraryModels.Where(m => string.Equals(m.Manifest.Architecture, LtxArchitecture, StringComparison.OrdinalIgnoreCase)))
        {
            AddIfReadable(choices, model.ModelPath, model.FolderName, isBuiltIn: false);
        }

        // Das offizielle Modell aus setup-video.ps1 zuerst, dann die übrigen nutzbaren.
        return [.. choices.OrderByDescending(c => c.IsUsable)
            .ThenByDescending(c => c.Name == VideoModelSet.Ltx23Distilled.Name)
            .ThenByDescending(c => c.IsBuiltIn)];
    }

    /// <summary>Urteil für eine GGUF-Datei (liest nur den Kopf).</summary>
    public static VideoModelChoice Describe(string modelPath, string name, bool isBuiltIn)
    {
        var values = GgufReader.ReadKeyValues(modelPath);
        var (usable, note, variant) = Classify(
            values.GetValueOrDefault("general.architecture") as string,
            values.GetValueOrDefault("model_version") as string,
            values.GetValueOrDefault("config") as string,
            Path.GetFileName(modelPath));
        return new VideoModelChoice(name, modelPath, isBuiltIn, usable, note, variant);
    }

    /// <summary>
    /// Kann der Video-Tab dieses Modell nutzen? LTX-2.3 mit Ton (AVTransformer3DModel). Die Art (distilled oder dev)
    /// steht meist im Dateinamen.
    /// </summary>
    public static (bool IsUsable, string Note, VideoModelVariant Variant) Classify(string? architecture, string? modelVersion, string? config, string fileName)
    {
        if (!string.Equals(architecture, LtxArchitecture, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Kein LTX-Modell (Architektur {architecture ?? "unbekannt"}). Der Video-Tab kann bisher nur LTX-2.3.", VideoModelVariant.Unknown);
        }

        // Viele GGUF-Dateien (z.B. von Unsloth) haben keinen Schlüssel "model_version". Dann verrät es der eingebettete config:
        // LTX-2.3 hat Bild und Ton (AVTransformer3DModel) und "gated attention", das gab es erst ab 2.3.
        var version = modelVersion ?? (LooksLikeLtx23(config) ? "2.3 (aus config)" : null);
        if (version is null || !version.StartsWith("2.3", StringComparison.Ordinal))
        {
            return (false, $"LTX-Video {version ?? "(Version unbekannt)"}: Der Video-Tab kann bisher nur LTX-2.3.", VideoModelVariant.Unknown);
        }

        if (config is not null && !config.Contains("AVTransformer3DModel", StringComparison.Ordinal))
        {
            return (false, "LTX-2.3 ohne Ton-Teil: Diese Form kann der Video-Tab nicht laden.", VideoModelVariant.Unknown);
        }

        if (DistilledWord().IsMatch(fileName))
        {
            return (true, "LTX-2.3 distilled, mit Ton. Nutzbar im Video-Tab (8 Schritte, schnell).", VideoModelVariant.Distilled);
        }

        if (DevWord().IsMatch(fileName))
        {
            return (true, "LTX-2.3 dev, mit Ton. Nutzbar im Video-Tab (20 Schritte mit CFG 4: ca. 2- bis 3-mal langsamer als distilled).",
                VideoModelVariant.Dev);
        }

        return (true, "LTX-2.3 mit Ton. Ob distilled oder dev, steht nicht in der Datei: Wähle im Video-Tab den Ablauf. "
            + "Wird das Video verrauscht, nimm „dev“.", VideoModelVariant.Unknown);
    }

    private static bool LooksLikeLtx23(string? config) =>
        config is not null
        && config.Contains("AVTransformer3DModel", StringComparison.Ordinal)
        && GatedAttention().IsMatch(config);

    private static void AddIfReadable(List<VideoModelChoice> choices, string file, string name, bool isBuiltIn)
    {
        try
        {
            choices.Add(Describe(file, name, isBuiltIn));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            // Halb geladene oder kaputte Datei: nicht anbieten.
        }
    }

    [GeneratedRegex(@"""apply_gated_attention""\s*:\s*true")]
    private static partial Regex GatedAttention();

    [GeneratedRegex(@"distil", RegexOptions.IgnoreCase)]
    private static partial Regex DistilledWord();

    [GeneratedRegex(@"(^|[-_. ])dev([-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DevWord();
}
