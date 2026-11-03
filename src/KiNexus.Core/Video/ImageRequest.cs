using System.Globalization;

namespace KiNexus.Core.Video;

/// <summary>
/// Ein Bild-Auftrag (Bauplan Bild, B1): Was das Bildmodell erzeugen soll. Läuft wie ein Video über ComfyUI.
/// Breite und Höhe müssen durch 16 teilbar sein (FLUX.2 und Z-Image rechnen in 16er-Blöcken).
/// </summary>
/// <param name="Prompt">Beschreibung des Bilds. Auf Englisch klappt es am besten, Deutsch geht meistens auch.</param>
/// <param name="Count">Anzahl Bilder mit demselben Prompt (1–4). Sie entstehen in einem Durchgang mit einem Seed und sind trotzdem verschieden.</param>
/// <param name="Seed">Startzahl fürs Rauschen. Gleicher Seed und gleicher Prompt ergeben dasselbe Bild.</param>
/// <param name="ReferenceImages">
/// Bilder, an denen sich das Modell orientiert (z.B. dieselbe Person, dasselbe Tier, derselbe Ort). Im Prompt sagt man,
/// was damit passieren soll, z.B. „der Hund aus dem ersten Bild vor der Hütte aus dem zweiten“. Nur FLUX.2 klein kann das.
/// </param>
/// <param name="SourceImage">
/// Bild zu Bild: Dieses Bild ist der Ausgangspunkt (zugeschnitten auf das Format). Aufbau und Farben bleiben, Details ändern sich.
/// Geht mit FLUX.2 klein und Z-Image. Null = aus dem Nichts (Rauschen).
/// </param>
/// <param name="Strength">Wie stark die Vorlage verändert wird: 0.2 = kaum, 1.0 = ganz neu. Nur mit <paramref name="SourceImage"/>.</param>
public sealed record ImageRequest(
    string Prompt,
    int Width = 1024,
    int Height = 1024,
    int Count = 1,
    long Seed = 42,
    IReadOnlyList<string>? ReferenceImages = null,
    string? SourceImage = null,
    double Strength = ImageRequest.DefaultStrength)
{
    /// <summary>In den Tests gut: neue Umgebung, aber gleiche Pose und gleiches Motiv.</summary>
    public const double DefaultStrength = 0.8;

    public const double MinStrength = 0.2;
    public const double MaxStrength = 1.0;

    /// <summary>Mehr Referenzen brauchen mehr Grafikspeicher und Zeit (jede ca. 5 s). In B0 mit zwei geprüft.</summary>
    public const int MaxReferences = 3;

    public bool HasReferences => ReferenceImages is { Count: > 0 };

    public const int SizeStep = 16;
    public const int MinSize = 512;

    /// <summary>Mehr braucht auf 8 GB VRAM zu lange und bringt kaum mehr Schärfe.</summary>
    public const int MaxSize = 1536;

    public const int MaxCount = 4;

    /// <summary>Formate für die Oberfläche: quadratisch, quer und hochkant (je ca. 1 Megapixel).</summary>
    public static IReadOnlyList<(string Name, int Width, int Height)> Formats { get; } =
    [
        ("Quadratisch (1024×1024)", 1024, 1024),
        ("Quer (1216×832)", 1216, 832),
        ("Hochkant (832×1216)", 832, 1216),
    ];

    /// <summary>Was stimmt nicht? Null, wenn der Auftrag gültig ist.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            return "Der Prompt ist leer. Beschreib, was auf dem Bild sein soll.";
        }

        if (!IsValidSize(Width) || !IsValidSize(Height))
        {
            return Text($"Breite und Höhe müssen durch {SizeStep} teilbar sein und zwischen {MinSize} und {MaxSize} liegen (jetzt {Width}×{Height}).");
        }

        if (Count is < 1 or > MaxCount)
        {
            return Text($"Es gehen 1 bis {MaxCount} Bilder pro Auftrag (jetzt {Count}).");
        }

        if (SourceImage is { } source)
        {
            if (!VideoRequest.ImageExtensions.Contains(Path.GetExtension(source).ToLowerInvariant()))
            {
                return "Die Vorlage muss ein PNG, JPG, WEBP oder BMP sein.";
            }

            if (!File.Exists(source))
            {
                return $"Die Vorlage gibt es nicht mehr: {source}";
            }

            if (Strength is < MinStrength or > MaxStrength)
            {
                return Text($"Die Stärke muss zwischen {MinStrength:0.0} und {MaxStrength:0.0} liegen (jetzt {Strength:0.00}).");
            }
        }

        var references = ReferenceImages ?? [];
        if (references.Count > MaxReferences)
        {
            return Text($"Es gehen höchstens {MaxReferences} Referenzbilder (jetzt {references.Count}).");
        }

        foreach (var image in references)
        {
            if (!VideoRequest.ImageExtensions.Contains(Path.GetExtension(image).ToLowerInvariant()))
            {
                return "Referenzbilder müssen PNG, JPG, WEBP oder BMP sein.";
            }

            if (!File.Exists(image))
            {
                return $"Das Referenzbild gibt es nicht mehr: {image}";
            }
        }

        return null;
    }

    private static bool IsValidSize(int size) => size is >= MinSize and <= MaxSize && size % SizeStep == 0;

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
