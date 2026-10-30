using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KiNexus.Core;
using KiNexus.Core.Settings;

namespace KiNexus.App.Services;

/// <summary>
/// Setzt die Akzentfarbe in den App-Ressourcen.
/// Alle Stellen, die mit DynamicResource auf "Brush.Accent" usw. zeigen, ändern sich sofort mit.
/// </summary>
public sealed class ThemeService
{
    public ThemeService(ISettingsService settings)
    {
        // Saved kann aus einem Hintergrund-Thread kommen. Ressourcen darf nur der UI-Thread ändern.
        settings.Saved += (_, saved) => Application.Current.Dispatcher.Invoke(() => ApplyAccent(saved.AccentColor));
    }

    public void ApplyAccent(string hex)
    {
        if (!HexColor.TryParse(hex, out var accent))
        {
            accent = HexColor.DefaultAccent;
        }

        var accentColor = ToWpfColor(accent);
        var dimColor = ToWpfColor(accent.Dimmed());
        var resources = Application.Current.Resources;

        resources["Color.Accent"] = accentColor;
        resources["Color.AccentDim"] = dimColor;
        resources["Brush.Accent"] = Frozen(new SolidColorBrush(accentColor));
        resources["Brush.AccentDim"] = Frozen(new SolidColorBrush(dimColor));
        resources["Brush.AccentSoft"] = Frozen(new SolidColorBrush(accentColor) { Opacity = 0.12 });
        resources["Effect.Glow"] = Frozen(new DropShadowEffect { Color = accentColor, ShadowDepth = 0, BlurRadius = 14, Opacity = 0.55 });
        resources["Effect.GlowSoft"] = Frozen(new DropShadowEffect { Color = accentColor, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.45 });
    }

    private static Color ToWpfColor(HexColor color) => Color.FromRgb(color.R, color.G, color.B);

    /// <summary>Eingefrorene Objekte sind schneller und dürfen mehrfach verwendet werden.</summary>
    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
