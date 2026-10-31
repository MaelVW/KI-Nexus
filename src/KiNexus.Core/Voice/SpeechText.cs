using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KiNexus.Core.Voice;

/// <summary>
/// Text für die Sprache: Markdown weg, bevor Piper vorliest, und die Mitschrift von Whisper aufräumen.
/// </summary>
public static partial class SpeechText
{
    /// <summary>Statt Code vorzulesen, sagt die KI diesen Satz (Bauplan 8.1, Gespräch-Stil).</summary>
    public const string CodePlaceholder = "Den Code findest du in der Mitschrift.";

    /// <summary>Zusatz zum Systemprompt im Gespräch (abschaltbar in den Einstellungen).</summary>
    public const string ConversationHint = "Antworte kurz und so, wie man spricht.";

    /// <summary>
    /// Macht aus einem Stück Markdown vorlesbaren Text: keine Sterne, Rauten, Links oder Tabellen-Striche.
    /// Ein Codeblock wird zu <see cref="CodePlaceholder"/>. Leer, wenn nichts Vorlesbares übrig bleibt.
    /// </summary>
    public static string ForSpeech(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        if (markdown.Contains("```", StringComparison.Ordinal))
        {
            return CodePlaceholder;
        }

        var lines = markdown.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(CleanLine).Where(l => l.Length > 0);
        var text = string.Join(" ", lines);
        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Url().Replace(text, "");
        text = HtmlTag().Replace(text, " ");
        text = text.Replace("**", "", StringComparison.Ordinal)
                   .Replace("__", "", StringComparison.Ordinal)
                   .Replace("~~", "", StringComparison.Ordinal)
                   .Replace("`", "", StringComparison.Ordinal)
                   .Replace("*", "", StringComparison.Ordinal);
        text = RemoveSymbols(text);
        text = Spaces().Replace(text, " ").Trim();
        return text.Any(char.IsLetterOrDigit) ? text : "";
    }

    /// <summary>Mitschrift von Whisper aufräumen: Leerzeichen, Schweizer Rechtschreibung ("ß" → "ss").</summary>
    public static string NormalizeTranscript(string text) =>
        Spaces().Replace(text.Replace("ß", "ss", StringComparison.Ordinal), " ").Trim();

    /// <summary>
    /// Whisper "hört" bei Rauschen manchmal Sätze aus seinen Trainingsdaten, z.B. Untertitel-Hinweise.
    /// Solche Mitschriften verwirft die App.
    /// </summary>
    public static bool IsLikelyHallucination(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("untertitel", StringComparison.Ordinal)
            || lower.Contains("amara.org", StringComparison.Ordinal)
            || lower.Contains("fürs zuschauen", StringComparison.Ordinal)
            || lower.Contains("fürs zuhören", StringComparison.Ordinal)
            || lower.Contains("copyright", StringComparison.Ordinal);
    }

    /// <summary>Überschrift, Zitat, Aufzählung und Tabelle: nur der Inhalt bleibt.</summary>
    private static string CleanLine(string line)
    {
        var trimmed = line.Trim();
        if (TableSeparator().IsMatch(trimmed))
        {
            return "";
        }

        if (trimmed.StartsWith('|'))
        {
            trimmed = string.Join(", ", trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        trimmed = LinePrefix().Replace(trimmed, "");
        return trimmed.Trim();
    }

    /// <summary>Emojis und ähnliche Zeichen würde Piper buchstabieren oder verschlucken.</summary>
    private static string RemoveSymbols(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse)
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>"#", "&gt;", "-", "*" oder "+" am Zeilenanfang (Überschrift, Zitat, Aufzählung).</summary>
    [GeneratedRegex(@"^(#{1,6}\s+|>\s*|[-*+]\s+)+")]
    private static partial Regex LinePrefix();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex TableSeparator();
}
