using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace KiNexus.App.Markdown;

/// <summary>
/// Die Syntax-Farben von AvalonEdit sind für hellen Hintergrund gemacht (z.B. dunkelblaue Schlüsselwörter).
/// Diese Klasse hellt zu dunkle Farben nach dem Einfärben auf. Der Farbton bleibt, nur die Helligkeit steigt.
/// </summary>
public sealed class DarkSyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly Dictionary<Color, SolidColorBrush> Cache = [];

    protected override void ColorizeLine(DocumentLine line)
    {
        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            if (element.TextRunProperties.ForegroundBrush is SolidColorBrush brush && Luminance(brush.Color) < 0.45)
            {
                element.TextRunProperties.SetForegroundBrush(Brighter(brush.Color));
            }
        });
    }

    /// <summary>Wählt die passende Syntax-Farbgebung zur Sprache im Codeblock (```csharp usw.).</summary>
    public static IHighlightingDefinition? HighlightingFor(string? language)
    {
        var name = language?.Trim().ToLowerInvariant() switch
        {
            "cs" or "csharp" or "c#" => "C#",
            "js" or "javascript" or "ts" or "typescript" or "json" => "JavaScript",
            "py" or "python" => "Python",
            "ps" or "ps1" or "powershell" or "pwsh" => "PowerShell",
            "xml" or "xaml" or "csproj" or "svg" => "XML",
            "html" or "htm" => "HTML",
            "css" => "CSS",
            "sql" or "tsql" => "TSQL",
            "java" => "Java",
            "c" or "cpp" or "c++" or "h" or "hpp" => "C++",
            "php" => "PHP",
            "vb" or "vbnet" => "VB",
            "md" or "markdown" => "MarkDown",
            _ => null,
        };
        return name is null ? null : HighlightingManager.Instance.GetDefinition(name);
    }

    /// <summary>Wahrgenommene Helligkeit von 0 (schwarz) bis 1 (weiss).</summary>
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Mischt die Farbe zu 55 % mit Weiss.</summary>
    private static SolidColorBrush Brighter(Color color)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(color, out var brush))
            {
                static byte Mix(byte channel) => (byte)(channel + (255 - channel) * 0.55);
                brush = new SolidColorBrush(Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B)));
                brush.Freeze();
                Cache[color] = brush;
            }

            return brush;
        }
    }
}
