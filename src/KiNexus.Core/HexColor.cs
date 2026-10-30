using System.Globalization;

namespace KiNexus.Core;

/// <summary>
/// Eine Farbe im Format #RRGGBB. Ohne WPF, damit man sie testen kann.
/// </summary>
public readonly record struct HexColor(byte R, byte G, byte B)
{
    /// <summary>Cyan aus dem Design (Kapitel 7 im Bauplan).</summary>
    public static readonly HexColor DefaultAccent = new(0x00, 0xE5, 0xFF);

    /// <summary>Gedämpftes Cyan aus dem Design.</summary>
    public static readonly HexColor DefaultAccentDim = new(0x0F, 0xA3, 0xB8);

    /// <summary>Liest "#00E5FF" oder "00E5FF". Gross- und Kleinschreibung egal.</summary>
    public static bool TryParse(string? text, out HexColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new HexColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>
    /// Die gedämpfte Variante für Nebenakzente.
    /// Für das Standard-Cyan nimmt sie genau den Wert aus dem Design.
    /// </summary>
    public HexColor Dimmed()
    {
        if (this == DefaultAccent)
        {
            return DefaultAccentDim;
        }

        const double factor = 0.72;
        return new HexColor(Scale(R, factor), Scale(G, factor), Scale(B, factor));
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    private static byte Scale(byte channel, double factor) => (byte)Math.Round(channel * factor);
}
