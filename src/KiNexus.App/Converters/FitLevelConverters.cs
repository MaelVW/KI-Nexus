using System.Globalization;
using System.Windows;
using System.Windows.Data;
using KiNexus.Core.Models;

namespace KiNexus.App.Converters;

/// <summary>Ampel-Farbe: Grün = passt in den VRAM, Gelb = braucht RAM, Rot = zu gross.</summary>
public sealed class FitLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FitLevel.Green => Application.Current.FindResource("Brush.Success"),
        FitLevel.Yellow => Application.Current.FindResource("Brush.Warning"),
        FitLevel.Red => Application.Current.FindResource("Brush.Error"),
        _ => Application.Current.FindResource("Brush.TextMuted"),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Sichtbar, wenn der Wert nicht null ist (z.B. eine Ampel, die es nur bei llama.cpp-KIs gibt).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
