using System.Globalization;
using System.Text.RegularExpressions;
using KiNexus.Core.Inference;

namespace KiNexus.Core.Video;

/// <summary>
/// "Prompt verbessern" (Bauplan Video, Kapitel 7): Eine KI macht aus einer kurzen Idee (auch auf Deutsch)
/// einen ausführlichen Video-Prompt auf Englisch, so wie LTX-2.3 ihn am besten versteht.
/// </summary>
public static partial class VideoPromptWriter
{
    /// <summary>Nach den Tipps von Lightricks: ein Absatz, Gegenwart, Szene, Bewegung, Kamera, Licht und Ton.</summary>
    public const string SystemPrompt =
        "You write prompts for the video model LTX-2.3, which creates a short video clip with sound. "
        + "Turn the user's idea (it may be in German) into ONE English paragraph of 60 to 120 words, in present tense. "
        + "Describe in this order: the main subject and what it looks like, the action and movement over time, the setting, "
        + "the camera (angle, movement), the lighting and colors, and the sounds (ambient noise, effects, speech in quotes if any). "
        + "Be concrete and visual. Do not invent a story that does not fit into a few seconds. "
        + "Answer with the prompt only: no title, no explanation, no quotes around it, no lists.";

    /// <summary>Die Nachrichten für die KI. Länge und Format helfen ihr, nicht zu viel in ein paar Sekunden zu packen.</summary>
    public static IReadOnlyList<ChatMessage> Messages(string idea, double seconds, int width, int height, bool hasStartImage)
    {
        var shape = height > width ? "vertical (portrait)" : "horizontal (landscape)";
        var start = hasStartImage ? " The video starts from a given photo: keep its subject and describe how it moves." : "";
        var user = string.Create(CultureInfo.InvariantCulture,
            $"Idea: {idea.Trim()}\nClip: about {seconds:0} seconds, {shape}.{start}");
        return [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, user)];
    }

    /// <summary>Für Bilder (Bauplan Bild): ein Absatz mit Motiv, Umgebung, Bildaufbau, Licht und Stil. Text im Bild in Anführungszeichen.</summary>
    public const string ImageSystemPrompt =
        "You write prompts for a text-to-image model (FLUX.2 or Z-Image). "
        + "Turn the user's idea (it may be in German) into ONE English paragraph of 40 to 90 words. "
        + "Describe: the main subject and what it looks like, the setting, the composition and camera (shot type, angle, lens), "
        + "the lighting and colors, and the style (for example photo, illustration, watercolor). "
        + "If text should appear in the image, write that text in double quotes. Be concrete and visual. "
        + "Answer with the prompt only: no title, no explanation, no lists.";

    /// <summary>Die Nachrichten für die KI bei einem Bild. Das Format hilft beim Bildaufbau.</summary>
    public static IReadOnlyList<ChatMessage> ImageMessages(string idea, int width, int height)
    {
        var shape = height > width ? "portrait" : width > height ? "landscape" : "square";
        var user = $"Idea: {idea.Trim()}\nImage: {shape}.";
        return [new ChatMessage(ChatRole.System, ImageSystemPrompt), new ChatMessage(ChatRole.User, user)];
    }

    /// <summary>Räumt die Antwort auf: Gedanken-Blöcke, "Prompt:", Anführungszeichen, Leerzeilen.</summary>
    public static string Clean(string answer)
    {
        var text = ThinkBlock().Replace(answer ?? "", "").Trim();
        text = Label().Replace(text, "").Trim();
        text = text.Trim('"', '“', '”', '„', '\'', '*', ' ', '\n', '\r');
        return Spaces().Replace(text, " ");
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^\**(?:improved\s+)?(?:video\s+|image\s+)?prompt\**\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Label();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
