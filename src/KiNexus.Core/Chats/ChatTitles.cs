using System.Text.RegularExpressions;
using KiNexus.Core.Inference;

namespace KiNexus.Core.Chats;

/// <summary>Chat-Titel: vom Modell erzeugt oder aus den ersten Wörtern der Frage.</summary>
public static partial class ChatTitles
{
    public const int MaxLength = 60;
    private const int FirstWordsCount = 6;

    /// <summary>Titel ohne Modell: die ersten Wörter der Frage.</summary>
    public static string FromFirstWords(string question)
    {
        var words = Whitespace().Split(question.Trim()).Where(w => w.Length > 0).ToList();
        if (words.Count == 0)
        {
            return ChatRecord.DefaultTitle;
        }

        var title = string.Join(' ', words.Take(FirstWordsCount));
        if (words.Count > FirstWordsCount || title.Length > MaxLength)
        {
            title = Shorten(title, MaxLength - 1) + "…";
        }

        return title;
    }

    /// <summary>Die Anfrage für einen Titel. Kurz, ohne Systemprompt, ohne Thinking.</summary>
    public static IReadOnlyList<ChatMessage> BuildRequest(string question, string answer)
    {
        var shortAnswer = answer.Length > 400 ? answer[..400] + " …" : answer;
        var prompt = $"""
            Gib diesem Chat einen kurzen Titel mit höchstens 5 Wörtern, auf Deutsch.
            Antworte nur mit dem Titel. Keine Anführungszeichen, kein Punkt am Ende.

            Frage: {question.Trim()}
            Antwort: {shortAnswer.Trim()}
            """;
        return [new ChatMessage(ChatRole.User, prompt)];
    }

    /// <summary>
    /// Räumt die Antwort des Modells auf: erste Zeile, ohne "Titel:", ohne Anführungszeichen und Markdown.
    /// Schweizer Rechtschreibung: "ß" wird zu "ss".
    /// </summary>
    public static string? Clean(string? generated)
    {
        if (string.IsNullOrWhiteSpace(generated))
        {
            return null;
        }

        var line = generated.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        line = TitlePrefix().Replace(line, "");
        line = line.Trim(' ', '"', '\'', '„', '“', '”', '«', '»', '*', '#', '`', '.', ':');
        line = line.Replace("ß", "ss", StringComparison.Ordinal);
        if (line.Length == 0)
        {
            return null;
        }

        return line.Length > MaxLength ? Shorten(line, MaxLength - 1) + "…" : line;
    }

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > maxLength / 2 ? cut[..lastSpace] : cut).TrimEnd();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Erkennt "Titel:", "**Titel:**" und "**Titel**:" am Anfang.</summary>
    [GeneratedRegex(@"^\s*\**\s*(Titel|Title)\s*\**\s*:\s*\**\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePrefix();
}
