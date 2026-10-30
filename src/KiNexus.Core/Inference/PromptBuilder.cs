namespace KiNexus.Core.Inference;

/// <summary>
/// Baut die Nachrichten für das Modell.
/// Es geht nur das ans Modell, was nötig ist: Systemprompt, Verlauf und die neue Frage (Lektion 5).
/// </summary>
public static class PromptBuilder
{
    public static IReadOnlyList<ChatMessage> Build(string? systemPrompt, IEnumerable<ChatMessage> history, string userText)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt.Trim()));
        }

        messages.AddRange(history.Where(m => m.Role != ChatRole.System));
        messages.Add(new ChatMessage(ChatRole.User, userText.Trim()));
        return messages;
    }
}
