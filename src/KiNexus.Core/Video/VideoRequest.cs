using System.Globalization;

namespace KiNexus.Core.Video;

/// <summary>
/// Ein Video-Auftrag: Was das Videomodell erzeugen soll (Bauplan Video, Kapitel 5).
/// Regeln von LTX: Breite und Höhe durch 32 teilbar, Anzahl Bilder = 8·n + 1.
/// </summary>
/// <param name="Prompt">Beschreibung von Szene, Bewegung, Kamera und Ton. Auf Englisch klappt es am besten.</param>
/// <param name="Frames">Anzahl Bilder, z.B. 97 = knapp 4 s bei 25 Bildern pro Sekunde.</param>
/// <param name="Seed">Startzahl fürs Rauschen. Gleicher Seed und gleicher Prompt ergeben dasselbe Video.</param>
/// <param name="NegativePrompt">Was nicht vorkommen soll. Wirkt nur, wenn das Modell mit CFG über 1 rechnet.</param>
/// <param name="StartImagePath">Bild zu Video: Dieses Bild ist das erste Bild des Videos. Null = Text zu Video.</param>
public sealed record VideoRequest(
    string Prompt,
    int Width = 768,
    int Height = 512,
    int Frames = 97,
    double FrameRate = 25,
    long Seed = 42,
    bool WithAudio = true,
    string? NegativePrompt = null,
    string? StartImagePath = null)
{
    /// <summary>Bildformate, die ComfyUI als Startbild liest.</summary>
    public static readonly IReadOnlyList<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    public const int SizeStep = 32;
    public const int MinSize = 256;
    public const int MaxSize = 1280;
    public const int MinFrames = 9;

    /// <summary>Ca. 10 Sekunden bei 25 Bildern pro Sekunde. Länger geht auf 8 GB VRAM nicht (Bauplan Video, Kapitel 12).</summary>
    public const int MaxFrames = 249;

    /// <summary>Länge in Sekunden.</summary>
    public double Seconds => Frames / FrameRate;

    /// <summary>Was stimmt nicht? Null, wenn der Auftrag gültig ist.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            return "Der Prompt ist leer. Beschreib, was im Video passieren soll.";
        }

        if (!IsValidSize(Width) || !IsValidSize(Height))
        {
            return Text($"Breite und Höhe müssen durch {SizeStep} teilbar sein und zwischen {MinSize} und {MaxSize} liegen (jetzt {Width}×{Height}).");
        }

        if (Frames < MinFrames || Frames > MaxFrames || (Frames - 1) % 8 != 0)
        {
            return Text($"Die Anzahl Bilder muss 8·n + 1 sein, zwischen {MinFrames} und {MaxFrames} (jetzt {Frames}).");
        }

        if (FrameRate is < 1 or > 60)
        {
            return Text($"Bilder pro Sekunde müssen zwischen 1 und 60 liegen (jetzt {FrameRate}).");
        }

        if (StartImagePath is { } image)
        {
            if (!ImageExtensions.Contains(Path.GetExtension(image).ToLowerInvariant()))
            {
                return "Das Startbild muss ein PNG, JPG, WEBP oder BMP sein.";
            }

            if (!File.Exists(image))
            {
                return $"Das Startbild gibt es nicht mehr: {image}";
            }
        }

        return null;
    }

    /// <summary>Rechnet eine Länge in Sekunden auf die nächste gültige Anzahl Bilder um (8·n + 1, mindestens 9).</summary>
    public static int FramesFor(double seconds, double frameRate)
    {
        var blocks = (int)Math.Round((seconds * frameRate - 1) / 8, MidpointRounding.AwayFromZero);
        return 8 * Math.Max(1, blocks) + 1;
    }

    private static bool IsValidSize(int size) => size is >= MinSize and <= MaxSize && size % SizeStep == 0;

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
