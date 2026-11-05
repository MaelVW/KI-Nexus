using System.Text.RegularExpressions;

namespace KiNexus.Core.Chats;

/// <summary>
/// Gefühls-Markierungen im Text der KI, z.B. [[gefuehl:freude]] (Avatar, Erweiterung E3).
/// Sie werden nie angezeigt und nie vorgelesen. Ein noch unfertiges "[[…" am Ende (beim Streamen) wird auch versteckt,
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

    /// <summary>Text ohne Markierungen. Ein unfertiger Anfang am Ende ("[", "[[gef") fällt auch weg.</summary>
    public static string Strip(string text)
    {
        if (text.IndexOf('[', StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var clean = TagPattern().Replace(text, "");
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
        // tail beginnt mit "[". Passt der Rest noch zum Anfang von "[[gefuehl:name"?
        const string prefix = "[[gefuehl:";
        var t = tail.ToLowerInvariant().Replace("ü", "ue");
        if (t.Length <= prefix.Length)
        {
            return prefix.StartsWith(t, StringComparison.Ordinal);
        }

        var rest = t.StartsWith(prefix, StringComparison.Ordinal) ? t[prefix.Length..].TrimEnd(']') : null;
        return rest is not null && t.Length - rest.Length - prefix.Length <= 1 && rest.Length <= 20 && rest.All(c => char.IsLetter(c) || c == ' ');
    }
}
