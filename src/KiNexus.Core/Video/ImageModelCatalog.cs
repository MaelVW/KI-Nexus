using System.Text.RegularExpressions;
using KiNexus.Core.Models;

namespace KiNexus.Core.Video;

/// <summary>Ein Bildmodell zur Auswahl im Studio, mit Urteil: Kann das Studio es nutzen?</summary>
/// <param name="Name">Anzeigename, z.B. "FLUX.2 klein 4B (Q8_0)" oder der Ordnername aus models/.</param>
/// <param name="ModelPath">Die GGUF-Datei.</param>
/// <param name="IsBuiltIn">Liegt in models/image/diffusion_models (eingerichtet mit setup-video.ps1 -Images).</param>
/// <param name="Note">Ein Satz zum Modell für die Anzeige.</param>
/// <param name="Set">Dateien und Einstellungen für ComfyUI. Null, wenn das Studio das Modell nicht nutzen kann.</param>
public sealed record ImageModelChoice(string Name, string ModelPath, bool IsBuiltIn, string Note, ImageModelSet? Set)
{
    public bool IsUsable => Set is not null;

    public override string ToString() => IsUsable ? Name : $"{Name} (nicht nutzbar)";
}

/// <summary>
/// Findet die Bildmodelle fürs Studio: die eingebauten in models/image/diffusion_models und Grundmodelle mit
/// Architektur "flux" oder "lumina2" in models/ (z.B. von Hugging Face). Der GGUF-Kopf dieser Modelle verrät fast nichts,
/// darum entscheidet die Grösse des Text-Eingangs, welches Modell es ist und welchen Text-Encoder es braucht:
/// FLUX.2 klein 4B: txt_in 7680 (Qwen3 4B), FLUX.2 klein 9B: 12288 (Qwen3 8B), Z-Image: cap_embedder 2560 (Qwen3 4B).
/// </summary>
public static partial class ImageModelCatalog
{
    public const string FluxArchitecture = "flux";
    public const string Lumina2Architecture = "lumina2";

    /// <summary>Text-Encoder für FLUX.2 klein 9B. Es gibt ihn in mehreren Grössen (z.B. "qwen_3_8b_fp8mixed.safetensors").</summary>
    public const string Qwen3_8BPrefix = "qwen_3_8b";

    private static readonly string[] ShapeTensors = ["txt_in.weight", "double_stream_modulation_img.lin.weight", "cap_embedder.1.weight", "x_embedder.weight"];

    public static bool IsImageArchitecture(string? architecture) =>
        string.Equals(architecture, FluxArchitecture, StringComparison.OrdinalIgnoreCase)
        || string.Equals(architecture, Lumina2Architecture, StringComparison.OrdinalIgnoreCase);

    /// <summary>Alle Bildmodelle, die nutzbaren zuerst. Unlesbare Dateien fallen weg.</summary>
    public static IReadOnlyList<ImageModelChoice> Find(string imageModelsDirectory, IEnumerable<LocalModel> libraryModels)
    {
        var choices = new List<ImageModelChoice>();
        var builtIn = Path.Combine(imageModelsDirectory, "diffusion_models");
        if (Directory.Exists(builtIn))
        {
            foreach (var file in Directory.EnumerateFiles(builtIn, "*.gguf").Order(StringComparer.OrdinalIgnoreCase))
            {
                var known = ImageModelSet.Known.FirstOrDefault(set => file.EndsWith(set.Model, StringComparison.OrdinalIgnoreCase));
                AddIfReadable(choices, file, known?.Name ?? Path.GetFileNameWithoutExtension(file), isBuiltIn: true, imageModelsDirectory);
            }
        }

        foreach (var model in libraryModels.Where(m => IsImageArchitecture(m.Manifest.Architecture)))
        {
            AddIfReadable(choices, model.ModelPath, model.FolderName, isBuiltIn: false, imageModelsDirectory);
        }

        // Der Standard aus setup-video.ps1 -Images zuerst, dann die übrigen nutzbaren.
        return [.. choices.OrderByDescending(c => c.IsUsable)
            .ThenByDescending(c => c.Name == ImageModelSet.Flux2Klein4B.Name)
            .ThenByDescending(c => c.IsBuiltIn)];
    }

    /// <summary>Urteil für eine GGUF-Datei (liest nur den Kopf und die Liste der Tensoren).</summary>
    public static ImageModelChoice Describe(string modelPath, string name, bool isBuiltIn, string imageModelsDirectory)
    {
        var architecture = GgufReader.ReadKeyValues(modelPath).GetValueOrDefault("general.architecture") as string;
        var shapes = GgufReader.ReadTensorShapes(modelPath, ShapeTensors);
        var textEncoders = Path.Combine(imageModelsDirectory, "text_encoders");
        var qwen8b = Directory.Exists(textEncoders)
            ? Directory.EnumerateFiles(textEncoders, Qwen3_8BPrefix + "*.safetensors")
                .OrderByDescending(f => f.Contains("fp8", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .FirstOrDefault()
            : null;
        var (template, note) = Classify(architecture, Width(shapes, "txt_in.weight"), shapes.ContainsKey("double_stream_modulation_img.lin.weight"),
            Width(shapes, "cap_embedder.1.weight"), Path.GetFileName(modelPath), qwen8b);
        var set = template is null
            ? null
            : template with
            {
                Name = name,
                Model = Path.GetFileName(modelPath),
                ModelFolder = isBuiltIn ? null : Path.GetDirectoryName(modelPath),
            };
        return new ImageModelChoice(name, modelPath, isBuiltIn, note, set);
    }

    /// <summary>
    /// Welches Bildmodell ist das? Gibt die passende Vorlage (Text-Encoder, VAE, Schritte) und einen Satz für die Anzeige zurück.
    /// Vorlage null = nicht nutzbar.
    /// </summary>
    /// <param name="textInWidth">Breite von "txt_in.weight" (FLUX): Grösse der Ausgabe des Text-Encoders.</param>
    /// <param name="isFlux2">FLUX.2 hat "double_stream_modulation_img", FLUX.1 nicht.</param>
    /// <param name="captionWidth">Breite von "cap_embedder.1.weight" (Lumina 2 / Z-Image).</param>
    /// <param name="qwen8bFile">Gefundener Text-Encoder Qwen3 8B in models/image/text_encoders, sonst null.</param>
    public static (ImageModelSet? Template, string Note) Classify(string? architecture, ulong? textInWidth, bool isFlux2, ulong? captionWidth,
        string fileName, string? qwen8bFile)
    {
        if (string.Equals(architecture, FluxArchitecture, StringComparison.OrdinalIgnoreCase))
        {
            if (!isFlux2)
            {
                return (null, "FLUX.1: Das braucht andere Text-Encoder (T5 und CLIP). Das Studio kann bisher FLUX.2 klein und Z-Image.");
            }

            return textInWidth switch
            {
                7680 => (ImageModelSet.Flux2Klein4B, "FLUX.2 klein 4B. Nutzbar im Studio (4 Schritte, Referenzbilder möglich)."),
                12288 when qwen8bFile is not null => (ImageModelSet.Flux2Klein9B with { TextEncoder = qwen8bFile },
                    "FLUX.2 klein 9B. Nutzbar im Studio (4 Schritte, Referenzbilder möglich). Grösser und langsamer als 4B."),
                12288 => (null, "FLUX.2 klein 9B: Es braucht den Text-Encoder Qwen3 8B (z.B. qwen_3_8b_fp8mixed.safetensors von "
                    + "Comfy-Org/Flux-2-Klein-9B, ca. 9 GB) in models\\image\\text_encoders. Danach „Nochmals prüfen“."),
                15360 => (null, "FLUX.2 dev (32B): viel zu gross für 8 GB VRAM. Das Studio kann FLUX.2 klein."),
                _ => (null, $"Ein FLUX.2-Modell, das das Studio nicht kennt (Text-Eingang {textInWidth?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbekannt"})."),
            };
        }

        if (string.Equals(architecture, Lumina2Architecture, StringComparison.OrdinalIgnoreCase))
        {
            if (captionWidth != 2560)
            {
                return (null, "Lumina-Image 2 (oder ein ähnliches Modell): Das Studio kann aus dieser Familie nur Z-Image.");
            }

            return TurboWord().IsMatch(fileName)
                ? (ImageModelSet.ZImageTurbo, "Z-Image Turbo. Nutzbar im Studio (8 Schritte, gut bei Schrift im Bild, keine Referenzbilder).")
                : (ImageModelSet.ZImageTurbo, "Z-Image. Gerechnet wie Turbo (8 Schritte). Ist es nicht die Turbo-Variante, werden die Bilder unscharf oder verrauscht.");
        }

        return (null, $"Kein Bildmodell, das das Studio kennt (Architektur {architecture ?? "unbekannt"}).");
    }

    private static ulong? Width(IReadOnlyDictionary<string, ulong[]> shapes, string name) =>
        shapes.TryGetValue(name, out var shape) && shape.Length > 0 ? shape[0] : null;

    private static void AddIfReadable(List<ImageModelChoice> choices, string file, string name, bool isBuiltIn, string imageModelsDirectory)
    {
        try
        {
            choices.Add(Describe(file, name, isBuiltIn, imageModelsDirectory));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            // Halb geladene oder kaputte Datei: nicht anbieten.
        }
    }

    [GeneratedRegex(@"turbo", RegexOptions.IgnoreCase)]
    private static partial Regex TurboWord();
}
