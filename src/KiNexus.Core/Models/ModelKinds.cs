namespace KiNexus.Core.Models;

/// <summary>
/// Ist ein Modell ein Sprachmodell? GGUF-Dateien gibt es auch für Bild- und Video-Modelle (z.B. für ComfyUI).
/// Mit solchen Dateien kann llama.cpp nicht chatten: Es bricht schon beim Laden ab.
/// </summary>
public static class ModelKinds
{
    /// <summary>Architekturen (general.architecture) von Bild- und Video-Modellen.</summary>
    private static readonly Dictionary<string, string> NonTextArchitectures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flux"] = "ein Bildmodell",
        ["sd1"] = "ein Bildmodell",
        ["sd2"] = "ein Bildmodell",
        ["sdxl"] = "ein Bildmodell",
        ["sd3"] = "ein Bildmodell",
        ["aura"] = "ein Bildmodell",
        ["pixart"] = "ein Bildmodell",
        ["lumina2"] = "ein Bildmodell",
        ["hidream"] = "ein Bildmodell",
        ["chroma"] = "ein Bildmodell",
        ["qwen_image"] = "ein Bildmodell",
        ["ltxv"] = "ein Videomodell",
        ["hyvid"] = "ein Videomodell",
        ["wan"] = "ein Videomodell",
        ["cosmos"] = "ein Videomodell",
        ["mochi"] = "ein Videomodell",
        ["cogvideox"] = "ein Videomodell",
    };

    /// <summary>Schlagwörter auf Hugging Face, die ein Bild-, Video- oder Ton-Modell kennzeichnen.</summary>
    private static readonly HashSet<string> NonTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "text-to-image", "image-to-image", "text-to-video", "image-to-video", "image-generation", "video-generation",
        "diffusers", "diffusion-single-file", "comfyui", "ltx-video", "stable-diffusion", "text-to-audio",
    };

    /// <summary>z.B. "ein Videomodell" für "ltxv". Null = Sprachmodell oder unbekannt.</summary>
    public static string? NonTextKind(string? architecture) =>
        architecture is not null && NonTextArchitectures.TryGetValue(architecture, out var kind) ? kind : null;

    public static bool IsTextArchitecture(string? architecture) => NonTextKind(architecture) is null;

    /// <summary>Das erste Schlagwort, das auf ein Bild-, Video- oder Ton-Modell hinweist, sonst null.</summary>
    public static string? NonTextTag(IEnumerable<string>? tags) => tags?.FirstOrDefault(NonTextTags.Contains);

    /// <summary>Ein Satz für Meldungen, z.B. "Das ist kein Sprachmodell, sondern ein Videomodell (Architektur ltxv)."</summary>
    public static string Explain(string? architecture) =>
        $"Das ist kein Sprachmodell, sondern {NonTextKind(architecture) ?? "ein anderes Modell"} (Architektur {architecture ?? "unbekannt"}). "
        + "Solche Dateien sind z.B. für ComfyUI gedacht. Mit KI-Nexus kann man damit nicht chatten.";
}
