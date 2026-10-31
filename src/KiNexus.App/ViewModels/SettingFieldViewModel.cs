using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KiNexus.Engines;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Ein Eingabefeld in den Feineinstellungen: Name, kurze Erklärung, Tooltip und eine Prüfung.
/// Leer heisst bei den meisten Feldern "Standard".
/// </summary>
public sealed partial class SettingFieldViewModel : ObservableObject
{
    private readonly Func<string, string?> _validate;
    private readonly Action _changed;

    private SettingFieldViewModel(string label, string help, string toolTip, Func<string, string?> validate, Action changed)
    {
        Label = label;
        Help = help;
        ToolTip = toolTip;
        _validate = validate;
        _changed = changed;
    }

    public string Label { get; }

    /// <summary>Kurz erklärt, steht unter dem Namen.</summary>
    public string Help { get; }

    /// <summary>Etwas mehr Erklärung beim Darüberfahren mit der Maus.</summary>
    public string ToolTip { get; }

    [ObservableProperty] private string _text = "";

    /// <summary>Rechts neben dem Feld, z.B. "Empfehlung: 1" oder "leer = automatisch".</summary>
    [ObservableProperty] private string _hint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = "";

    [ObservableProperty] private bool _isVisible = true;

    public bool HasError => Error.Length > 0;

    public bool IsValid => !IsVisible || Error.Length == 0;

    /// <summary>Kommazahl in einem Bereich. Leer = Standard (wenn erlaubt).</summary>
    public static SettingFieldViewModel Decimal(string label, string help, string toolTip, double min, double max, Action changed) =>
        new(label, help, toolTip, text =>
        {
            if (!ParameterText.TryParse(text, out double? value))
            {
                return "Keine gültige Zahl. Beispiel: 0.7";
            }

            return value is { } number && (number < min || number > max) ? $"Erlaubt: {Show(min)} bis {Show(max)}." : null;
        }, changed);

    /// <summary>Ganze Zahl in einem Bereich. <paramref name="max"/> wird jedes Mal neu gefragt (z.B. hängt es vom Kontext ab).</summary>
    public static SettingFieldViewModel Integer(string label, string help, string toolTip, int min, Func<int> max, bool allowEmpty, Action changed) =>
        new(label, help, toolTip, text =>
        {
            if (!ParameterText.TryParse(text, out int? value))
            {
                return "Bitte eine ganze Zahl.";
            }

            if (value is null)
            {
                return allowEmpty ? null : "Bitte eine Zahl eingeben.";
            }

            var upper = max();
            return value < min || value > upper ? $"Erlaubt: {min} bis {upper}." : null;
        }, changed);

    /// <summary>Eine eigene Prüfung. Gibt den Fehlertext zurück oder null.</summary>
    public static SettingFieldViewModel Custom(string label, string help, string toolTip, Func<string, string?> validate, Action changed) =>
        new(label, help, toolTip, validate, changed);

    public double? DecimalValue => ParameterText.TryParse(Text, out double? value) ? value : null;

    public int? IntegerValue => ParameterText.TryParse(Text, out int? value) ? value : null;

    /// <summary>Nochmals prüfen, z.B. wenn sich die Obergrenze geändert hat.</summary>
    public void Revalidate() => Error = _validate(Text) ?? "";

    partial void OnTextChanged(string value)
    {
        Revalidate();
        _changed();
    }

    partial void OnIsVisibleChanged(bool value) => _changed();

    private static string Show(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
