using System.Globalization;

namespace KiNexus.Core.Models;

/// <summary>Die Ampel: Passt ein Modell auf diesen Laptop?</summary>
public enum FitLevel
{
    /// <summary>Passt ganz in den Grafikspeicher (VRAM). Schnell.</summary>
    Green,

    /// <summary>Braucht zusätzlich Arbeitsspeicher (RAM). Geht, ist aber langsamer.</summary>
    Yellow,

    /// <summary>Zu gross.</summary>
    Red,
}

/// <summary>Ergebnis der Schätzung, mit einer Erklärung in einfachen Worten.</summary>
public sealed record FitEstimate(FitLevel Level, long NeedBytes, long VramBudgetBytes, long RamBudgetBytes)
{
    public string Label => Level switch
    {
        FitLevel.Green => "passt in VRAM",
        FitLevel.Yellow => "teils im RAM (langsamer)",
        _ => "zu gross",
    };

    /// <summary>z.B. "Schätzung: braucht ca. 6.1 GB. Grafikkarte: 6.8 GB nutzbar, RAM: 18.8 GB nutzbar."</summary>
    public string Explanation => string.Create(CultureInfo.InvariantCulture,
        $"Schätzung: braucht ca. {Gb(NeedBytes):F1} GB (Datei + Puffer für den Kontext). Grafikkarte: {Gb(VramBudgetBytes):F1} GB nutzbar, RAM: {Gb(RamBudgetBytes):F1} GB nutzbar.");

    private static double Gb(long bytes) => bytes / (1024d * 1024 * 1024);
}

/// <summary>
/// Schätzt, ob ein Modell passt (Bauplan 8.3): Dateigrösse plus Puffer für den Kontext,
/// verglichen mit dem VRAM und einem Teil des RAM. Es ist eine Schätzung, keine Messung.
/// </summary>
public static class FitEstimator
{
    private const long Mb = 1024L * 1024;

    /// <summary>Windows und andere Programme brauchen selbst etwas VRAM, dazu kommt ein Sicherheitsabstand.</summary>
    public const long VramReserveBytes = 1200 * Mb;

    /// <summary>Höchstens 60 % des RAM rechnen wir dem Modell zu. Der Rest bleibt für Windows und andere Programme.</summary>
    public const double RamShare = 0.6;

    /// <summary>
    /// Puffer für den Kontext (KV-Cache) und Rechenspeicher: 200 MB fest plus 48 KB pro Token,
    /// für 8192 Token also ca. 580 MB. Abgeglichen mit den Messungen in M1 (gemma4: 460 + 152 MB, qwen3.5: 306 + 112 MB).
    /// Klassische Modelle ohne Tricks beim Kontext brauchen etwas mehr. Darum ist es nur eine Schätzung.
    /// </summary>
    public static long ContextBufferBytes(int contextLength) => 200 * Mb + Math.Max(0, contextLength) * 48L * 1024;

    /// <param name="modelBytes">Grösse der Modell-Datei(en).</param>
    /// <param name="extraBytes">Zusätzliche Dateien, z.B. der Bild-Projektor (mmproj).</param>
    /// <param name="vramTotalBytes">VRAM der Grafikkarte. Null = keine NVIDIA-Karte gefunden.</param>
    public static FitEstimate Estimate(long modelBytes, long extraBytes, int contextLength, ulong? vramTotalBytes, ulong ramTotalBytes)
    {
        var need = modelBytes + extraBytes + ContextBufferBytes(contextLength);
        var vramBudget = vramTotalBytes is ulong vram ? Math.Max(0, (long)vram - VramReserveBytes) : 0;
        var ramBudget = (long)(ramTotalBytes * RamShare);

        var level = need <= vramBudget ? FitLevel.Green
            : need <= vramBudget + ramBudget ? FitLevel.Yellow
            : FitLevel.Red;
        return new FitEstimate(level, need, vramBudget, ramBudget);
    }
}
