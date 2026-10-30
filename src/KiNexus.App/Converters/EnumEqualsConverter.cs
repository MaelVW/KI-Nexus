using System.Globalization;
using System.Windows.Data;

namespace KiNexus.App.Converters;

/// <summary>
/// Für Auswahl-Knöpfe (RadioButton): Ist der Enum-Wert gleich dem Parameter?
/// Beispiel: IsChecked="{Binding Style, Converter=..., ConverterParameter={x:Static settings:ChatStyle.Terminal}}"
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
