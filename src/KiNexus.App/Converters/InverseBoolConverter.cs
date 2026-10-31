using System.Globalization;
using System.Windows.Data;

namespace KiNexus.App.Converters;

/// <summary>Kehrt true und false um.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
