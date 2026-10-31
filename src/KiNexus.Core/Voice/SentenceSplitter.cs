using System.Text;

namespace KiNexus.Core.Voice;

/// <summary>
/// Teilt eine Antwort, die Stück für Stück ankommt, in Sätze. So kann Piper den ersten Satz vorlesen,
/// während das Modell noch weiterschreibt (Bauplan 8.1: "Die Sprachausgabe arbeitet satzweise").
/// Codeblöcke werden nicht vorgelesen. Beim ersten sagt die KI <see cref="SpeechText.CodePlaceholder"/>.
/// </summary>
public sealed class SentenceSplitter
{
    private const string Fence = "```";

    /// <summary>Wörter mit Punkt, nach denen kein Satz endet.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "bzw", "usw", "ca", "nr", "dr", "prof", "evtl", "ggf", "inkl", "exkl", "vgl", "bspw", "sog", "etc", "max", "min",
        "mio", "mrd", "zb", "dh", "ua", "uvm", "abs", "art", "str", "tel", "fr", "hr", "st",
    };

    private readonly StringBuilder _pending = new();
    private bool _inCode;
    private bool _codeAnnounced;

    /// <summary>Neuen Text anhängen. Gibt die Sätze zurück, die jetzt fertig sind (schon vorlesbar gemacht).</summary>
    public IReadOnlyList<string> Append(string delta)
    {
        _pending.Append(delta);
        return Drain(final: false);
    }

    /// <summary>Die Antwort ist fertig: alles, was noch übrig ist.</summary>
    public IReadOnlyList<string> Flush() => Drain(final: true);

    public void Reset()
    {
        _pending.Clear();
        _inCode = false;
        _codeAnnounced = false;
    }

    private List<string> Drain(bool final)
    {
        var sentences = new List<string>();
        while (true)
        {
            var text = _pending.ToString();
            if (_inCode)
            {
                if (!TryLeaveCode(text, final))
                {
                    break;
                }

                AnnounceCode(sentences);
                continue;
            }

            var fence = text.IndexOf(Fence, StringComparison.Ordinal);
            var region = fence >= 0 ? text[..fence] : text;
            var boundary = FindBoundary(region, final && fence < 0);
            if (boundary > 0)
            {
                Emit(sentences, text[..boundary]);
                _pending.Remove(0, boundary);
                continue;
            }

            if (fence >= 0)
            {
                // Text vor dem Codeblock (z.B. "So geht es:") ist ein eigener Satz.
                Emit(sentences, region);
                _pending.Remove(0, fence + Fence.Length);
                _inCode = true;
                continue;
            }

            if (final)
            {
                Emit(sentences, text);
                _pending.Clear();
            }

            break;
        }

        return sentences;
    }

    /// <summary>Schliessender Zaun gefunden? Dann alles bis zum Ende seiner Zeile weg.</summary>
    private bool TryLeaveCode(string text, bool final)
    {
        var close = text.IndexOf(Fence, StringComparison.Ordinal);
        if (close < 0)
        {
            if (final)
            {
                _pending.Clear();
                _inCode = false;
                return true;
            }

            return false;
        }

        var lineEnd = text.IndexOf('\n', close);
        if (lineEnd < 0 && !final)
        {
            return false;
        }

        _pending.Remove(0, lineEnd < 0 ? text.Length : lineEnd + 1);
        _inCode = false;
        return true;
    }

    private void AnnounceCode(List<string> sentences)
    {
        if (!_codeAnnounced)
        {
            sentences.Add(SpeechText.CodePlaceholder);
            _codeAnnounced = true;
        }
    }

    private static void Emit(List<string> sentences, string raw)
    {
        var speech = SpeechText.ForSpeech(raw);
        if (speech.Length > 0)
        {
            sentences.Add(speech);
        }
    }

    /// <summary>
    /// Position direkt nach dem ersten Satzende, sonst 0. Satzende: Zeilenumbruch, oder . ! ? : ; … gefolgt von Leerraum.
    /// Ist das Zeichen nach dem Punkt noch nicht da, wird gewartet (es könnte "3.5" werden).
    /// </summary>
    private static int FindBoundary(string text, bool final)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                if (text[..i].Trim().Length > 0)
                {
                    return i + 1;
                }

                continue;
            }

            if (c is not ('.' or '!' or '?' or ':' or ';' or '…'))
            {
                continue;
            }

            var end = i + 1;
            // Anführungszeichen, Klammern und Markdown-Zeichen direkt nach dem Satzende gehören noch dazu ("**Wichtig:**").
            while (end < text.Length && text[end] is '"' or '\'' or '»' or '«' or ')' or '“' or '”' or '!' or '?' or '.' or '*' or '_' or '`')
            {
                end++;
            }

            if (end >= text.Length)
            {
                return final ? end : 0;
            }

            if (!char.IsWhiteSpace(text[end]))
            {
                continue;
            }

            if (c == '.' && IsAbbreviationOrNumber(text, i))
            {
                continue;
            }

            return end;
        }

        return 0;
    }

    /// <summary>"z.B.", "usw.", "1." (Aufzählung, Datum) oder ein einzelner Buchstabe vor dem Punkt.</summary>
    private static bool IsAbbreviationOrNumber(string text, int dot)
    {
        var start = dot;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not ('(' or '"'))
        {
            start--;
        }

        var word = text[start..dot];
        if (word.Length == 0)
        {
            return false;
        }

        return word.Contains('.', StringComparison.Ordinal)
            || word.All(char.IsDigit)
            || (word.Length == 1 && char.IsLetter(word[0]))
            || Abbreviations.Contains(word);
    }
}
