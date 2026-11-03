using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace KiNexus.App.Converters;

/// <summary>
/// Lädt ein Bild aus einer Datei als kleine Vorschau.
/// OnLoad liest die Datei sofort ganz ein und gibt sie wieder frei. Sonst wäre sie gesperrt,
/// und "Chat löschen" könnte die Bilder nicht entfernen.
/// Als Parameter geht eine andere Breite in Pixeln, z.B. "1400" für die grosse Ansicht im Studio.
/// </summary>
public sealed class PathToThumbnailConverter : IValueConverter
{
    private const int DecodeWidth = 440;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = parameter is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
                ? width
                : DecodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Anteil (0 bis 1) mal Breite aus dem Parameter, z.B. für die Kontext-Anzeige.</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        var width = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 100;
        return fraction * width;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
