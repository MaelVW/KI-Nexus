using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KiNexus.App.Views;

/// <summary>
/// Windows-Funktionen für die eigene, dunkle Titelleiste.
/// Nur Darstellung, keine App-Logik.
/// </summary>
internal static class NativeWindow
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCornerRound = 2;

    private const int SmCxSizeFrame = 32;
    private const int SmCySizeFrame = 33;
    private const int SmCxPaddedBorder = 92;

    /// <summary>
    /// Dunkler Rahmen, runde Ecken (Windows 11) und eine feine Randfarbe.
    /// Ältere Windows-Versionen ignorieren die Werte einfach.
    /// </summary>
    public static void ApplyDarkFrame(Window window, Color borderColor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = 1;
        var corner = DwmCornerRound;
        var colorRef = borderColor.R | (borderColor.G << 8) | (borderColor.B << 16);

        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmBorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>
    /// Ein maximiertes Fenster ohne Windows-Rahmen ragt etwas über den Bildschirm hinaus.
    /// Diesen Rand muss man innen wieder abziehen, sonst ist der Inhalt abgeschnitten.
    /// </summary>
    public static Thickness GetMaximizedOverhang(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dpi = GetDpiForWindow(hwnd);
        var scale = VisualTreeHelper.GetDpi(window);

        var padded = GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
        var x = (GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + padded) / scale.DpiScaleX;
        var y = (GetSystemMetricsForDpi(SmCySizeFrame, dpi) + padded) / scale.DpiScaleY;
        return new Thickness(x, y, x, y);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
