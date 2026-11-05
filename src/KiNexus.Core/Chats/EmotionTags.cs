using System.Text.RegularExpressions;

namespace KiNexus.Core.Chats;

/// <summary>
/// Gefühls-Markierungen im Text der KI, z.B. [[gefuehl:freude]] (Avatar, Erweiterung E3).
/// Strip entfernt alle Markierungen der Form [[wort:…]] (auch Befehle wie [[geraet:…]]): Sie werden nie angezeigt und nie vorgelesen. Ein noch unfertiges "[[…" am Ende (beim Streamen) wird auch versteckt,
/// so wächst der bereinigte Text immer nur hinten an.
/// </summary>
public static partial class EmotionTags
{
    public static IReadOnlyList<string> Names { get; } = ["neutral", "freude", "traurig", "ueberrascht", "wuetend", "entspannt"];

    /// <summary>Zusatz zum Systemprompt, nur wenn ein Avatar aktiv ist. Kurz, weil er bei jeder Frage Token kostet.</summary>
    public const string PromptHint =
        "Du kannst Gefühle zeigen. Schreib dafür [[gefuehl:name]] in deine Antwort. " +
        "Mögliche Namen: neutral, freude, traurig, ueberrascht, wuetend, entspannt. Nutze das sparsam, höchstens einmal pro Antwort.";

    [GeneratedRegex(@"\[\[\s*gef(?:ue|ü)hl\s*:\s*([\p{L}]+)\s*\]\]", RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();

    /// <summary>Jede Markierung [[wort:…]] (Gefühle, Geräte, Wünsche).</summary>
    [GeneratedRegex(@"\[\[\s*[\p{L}]+\s*:[^\[\]]*\]\]")]
    private static partial Regex AnyTagPattern();

    /// <summary>Text ohne Markierungen. Ein unfertiger Anfang am Ende ("[", "[[gef") fällt auch weg.</summary>
    public static string Strip(string text)
    {
        if (text.IndexOf('[', StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var clean = AnyTagPattern().Replace(text, "");
        var open = clean.LastIndexOf('[');
        if (open >= 0 && clean.IndexOf("]]", open, StringComparison.Ordinal) < 0)
        {
            // "[[gefuehl:" kann noch kommen: verstecken, bis die Markierung fertig ist.
            var start = open > 0 && clean[open - 1] == '[' ? open - 1 : open;
            if (IsPossibleTagStart(clean[start..]))
            {
                clean = clean[..start];
            }
        }

        return clean;
    }

    /// <summary>Alle fertigen Markierungen in der Reihenfolge im Text, als bekannte Namen (unbekannte fallen weg).</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        if (text.IndexOf("[[", StringComparison.Ordinal) < 0)
        {
            return [];
        }

        return [.. TagPattern().Matches(text).Select(m => Normalize(m.Groups[1].Value)).OfType<string>()];
    }

    /// <summary>"Freude", "überrascht" → "freude", "ueberrascht". Null, wenn es das Gefühl nicht gibt.</summary>
    public static string? Normalize(string name)
    {
        var n = name.Trim().ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue");
        return Names.Contains(n) ? n : null;
    }

    private static bool IsPossibleTagStart(string tail)
    {
        // tail beginnt mit "[". Kann daraus noch eine Markierung "[[wort:…]]" werden?
        if (tail.Length == 1)
        {
            return true;
        }

        if (tail[1] != '[' || tail.Length > 160)
        {
            return false;
        }

        var body = tail[2..].TrimEnd(']');
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        var word = colon < 0 ? body : body[..colon];
        return word.Trim().All(char.IsLetter) && (colon < 0 || !body[(colon + 1)..].Contains('['));
    }
}
