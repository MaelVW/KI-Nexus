using System.Runtime.InteropServices;

namespace KiNexus.Core.Resources;

/// <summary>
/// Läuft der Laptop auf Akku? Dann bremst Windows die Grafikkarte stark (Bauplan, Lektion: immer am Netzteil messen).
/// </summary>
public static class PowerStatus
{
    /// <summary>True nur, wenn Windows sicher "auf Akku" meldet. Ohne Akku (Desktop) oder unbekannt: false.</summary>
    public static bool IsOnBattery() => GetSystemPowerStatus(out var status) && status.AcLineStatus == 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        /// <summary>0 = Akku, 1 = Netzteil, 255 = unbekannt.</summary>
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
