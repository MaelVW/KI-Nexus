using System.Globalization;

namespace KiNexus.Core.Video;

/// <summary>
/// Ein Bild-Auftrag (Bauplan Bild, B1): Was das Bildmodell erzeugen soll. Läuft wie ein Video über ComfyUI.
/// Breite und Höhe müssen durch 16 teilbar sein (FLUX.2 und Z-Image rechnen in 16er-Blöcken).
/// </summary>
/// <param name="Prompt">Beschreibung des Bilds. Auf Englisch klappt es am besten, Deutsch geht meistens auch.</param>
/// <param name="Count">Anzahl Bilder mit demselben Prompt (1–4). Jedes bekommt seinen eigenen Seed: Seed, Seed + 1, …</param>
/// <param name="Seed">Startzahl fürs Rauschen. Gleicher Seed und gleicher Prompt ergeben dasselbe Bild.</param>
public sealed record ImageRequest(
    string Prompt,
    int Width = 1024,
    int Height = 1024,
    int Count = 1,
    long Seed = 42)
{
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

        return null;
    }

    private static bool IsValidSize(int size) => size is >= MinSize and <= MaxSize && size % SizeStep == 0;

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
