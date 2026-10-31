using System.Globalization;

namespace KiNexus.App.ViewModels;

/// <summary>Zahlen für die Anzeige: "609.8 MB", "5.2 GB", "12.3k", "01.05.2025".</summary>
public static class DisplayFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(Invariant, $"{bytes / (double)(1L << 30):F1} GB"),
        >= 1L << 20 => string.Create(Invariant, $"{bytes / (double)(1L << 20):F1} MB"),
        >= 1L << 10 => string.Create(Invariant, $"{bytes / (double)(1L << 10):F0} KB"),
        _ => string.Create(Invariant, $"{bytes} B"),
    };

    public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Count(long count) => count switch
    {
        >= 1_000_000 => string.Create(Invariant, $"{count / 1_000_000d:F1} Mio."),
        >= 1_000 => string.Create(Invariant, $"{count / 1_000d:F1}k"),
        _ => count.ToString(Invariant),
    };

    public static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("dd.MM.yyyy", Invariant) ?? "–";

    /// <summary>"noch 2 min 5 s". Leer, wenn nichts Sinnvolles zu sagen ist.</summary>
    public static string Remaining(long bytesLeft, double bytesPerSecond)
    {
        if (bytesPerSecond < 1 || bytesLeft <= 0)
        {
            return "";
        }

        var time = TimeSpan.FromSeconds(bytesLeft / bytesPerSecond);
        return time.TotalMinutes >= 1
            ? string.Create(Invariant, $"noch {(int)time.TotalMinutes} min {time.Seconds} s")
            : string.Create(Invariant, $"noch {Math.Max(1, time.Seconds)} s");
    }
}
