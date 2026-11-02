using System.Globalization;
using KiNexus.Core.Models;

namespace KiNexus.Core.Video;

/// <summary>Die Speicher-Ampel im Video-Tab, mit einer Erklärung in einfachen Worten.</summary>
public sealed record VideoFit(FitLevel Level, string Explanation)
{
    public string Label => Level switch
    {
        FitLevel.Green => "sollte passen",
        FitLevel.Yellow => "geht, aber langsamer",
        _ => "zu viel für diesen Laptop",
    };
}

/// <summary>
/// Schätzt, ob ein Video auf diesen Laptop passt. Grundlage sind die Messungen aus V0 (docs/10-Video.md):
/// ComfyUI braucht mit LTX-2.3 ca. 22 GB RAM. Fehlt RAM, lagert Windows auf die Festplatte aus.
/// Es ist eine Schätzung, keine Messung.
/// </summary>
public static class VideoFitEstimator
{
    /// <summary>So viel RAM brauchte ComfyUI in V0 höchstens (mit --cache-none).</summary>
    public const double NeedRamGb = 22;

    /// <summary>So viel darf Windows auslagern, bevor es zu langsam wird (V0 lief mit bis zu 9 GB Auslagerung).</summary>
    public const double SwapToleranceGb = 9;

    /// <summary>Getestet in V0: 768×512 Pixel, 97 Bilder.</summary>
    private const double TestedVoxels = 768.0 * 512 * 97;

    private const double BytesPerGb = 1024d * 1024 * 1024;

    /// <param name="ramTotalBytes">RAM des Laptops.</param>
    /// <param name="ramUsedBytes">RAM, den andere Programme jetzt belegen.</param>
    /// <param name="chatModelLoaded">Ein Chat-Modell ist geladen. Es wird vor dem Video entladen.</param>
    public static VideoFit Estimate(VideoRequest request, ulong ramTotalBytes, ulong ramUsedBytes, bool chatModelLoaded, bool onBattery)
    {
        var free = (ramTotalBytes - Math.Min(ramUsedBytes, ramTotalBytes)) / BytesPerGb;
        var size = request.Width * (double)request.Height * request.Frames / TestedVoxels;
        var reasons = new List<string>();
        var level = FitLevel.Green;

        if (free >= NeedRamGb)
        {
            reasons.Add(Text($"RAM frei: {free:0.0} GB, ComfyUI braucht ca. {NeedRamGb:0} GB."));
        }
        else if (free >= NeedRamGb - SwapToleranceGb || chatModelLoaded)
        {
            level = FitLevel.Yellow;
            reasons.Add(Text($"RAM frei: nur {free:0.0} GB, ComfyUI braucht ca. {NeedRamGb:0} GB. Windows lagert auf die Festplatte aus, das macht es langsamer."));
        }
        else
        {
            level = FitLevel.Red;
            reasons.Add(Text($"RAM frei: nur {free:0.0} GB, ComfyUI braucht ca. {NeedRamGb:0} GB. Schliess grosse Programme (Browser, Spiele, Unity)."));
        }

        if (chatModelLoaded)
        {
            reasons.Add("Ein Chat-Modell ist geladen. Es wird vor dem Start entladen, dann ist mehr frei.");
        }

        if (size > 4)
        {
            level = FitLevel.Red;
            reasons.Add(Text($"Das Video ist {size:0.0}-mal so gross wie getestet (768×512, 4 s). Das schafft die Grafikkarte kaum."));
        }
        else if (size > 1.5)
        {
            level = Worse(level, FitLevel.Yellow);
            reasons.Add(Text($"Das Video ist {size:0.0}-mal so gross wie getestet (768×512, 4 s). Es dauert länger und kann am Speicher scheitern."));
        }

        if (onBattery)
        {
            level = Worse(level, FitLevel.Yellow);
            reasons.Add("Der Laptop läuft auf Akku: Die Grafikkarte ist dann viel langsamer. Steck das Netzteil ein.");
        }

        return new VideoFit(level, string.Join(" ", reasons));
    }

    private static FitLevel Worse(FitLevel a, FitLevel b) => (FitLevel)Math.Max((int)a, (int)b);

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
