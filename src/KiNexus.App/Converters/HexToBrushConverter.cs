using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KiNexus.Core;

namespace KiNexus.App.Converters;

/// <summary>Macht aus "#00E5FF" einen Pinsel, z.B. für die Farbvorschau. Ungültig = durchsichtig.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!HexColor.TryParse(value as string, out var color))
        {
            return Brushes.Transparent;
        }

        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
