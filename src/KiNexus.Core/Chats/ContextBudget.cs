using KiNexus.Core.Inference;

namespace KiNexus.Core.Chats;

/// <summary>Was ans Modell geht, nachdem der Kontext geprüft wurde.</summary>
public sealed record ContextPlan(IReadOnlyList<ChatMessage> Messages, int DroppedMessages, int EstimatedTokens)
{
    public bool WasTrimmed => DroppedMessages > 0;
}

/// <summary>
/// Passt den Verlauf in den Kontext (das Gedächtnis) des Modells.
/// Ist er zu lang, fallen die ältesten Nachrichten weg. Der Systemprompt und die neue Frage bleiben immer drin.
/// </summary>
/// <remarks>
/// Die Token-Zahl ist eine Schätzung: deutscher Text hat ca. 3–4 Zeichen pro Token.
/// Nach jeder Antwort liefert die Engine die echte Zahl. Daraus lernt der ChatService das Verhältnis
/// (Zeichen pro Token) für den nächsten Durchgang.
/// </remarks>
public static class ContextBudget
{
    /// <summary>Vorsichtig gewählt: lieber etwas zu viele Token schätzen als den Kontext sprengen.</summary>
    public const double DefaultCharsPerToken = 3.2;

    /// <summary>Rollen-Markierungen der Chat-Vorlage pro Nachricht.</summary>
    public const int TokensPerMessage = 8;

    /// <summary>Ein Bild kostet je nach Modell ca. 250–600 Token. Wir rechnen mit dem oberen Wert.</summary>
    public const int TokensPerImage = 600;

    public static int Estimate(ChatMessage message, double charsPerToken = DefaultCharsPerToken) =>
        TokensPerMessage
        + (int)Math.Ceiling(message.Content.Length / Math.Max(1.0, charsPerToken))
        + (message.ImagePaths?.Count ?? 0) * TokensPerImage;

    /// <summary>
    /// Wie viel Platz für die Antwort frei bleiben muss: die maximale Antwortlänge, aber höchstens der halbe Kontext.
    /// </summary>
    public static int ReserveForAnswer(int contextLength, int maxTokens) => Math.Min(maxTokens, contextLength / 2);

    /// <summary>
    /// Baut die Nachrichten: Systemprompt + so viel Verlauf wie Platz hat + neue Nachricht.
    /// </summary>
    /// <param name="history">Bisheriger Verlauf, älteste Nachricht zuerst, ohne die neue Nachricht.</param>
    /// <exception cref="InferenceException">Wenn schon Systemprompt und neue Nachricht allein zu gross sind.</exception>
    public static ContextPlan Fit(
        string? systemPrompt,
        IReadOnlyList<ChatMessage> history,
        ChatMessage newMessage,
        int contextLength,
        int reserveForAnswer,
        double charsPerToken = DefaultCharsPerToken)
    {
        var budget = contextLength - reserveForAnswer;
        var system = string.IsNullOrWhiteSpace(systemPrompt) ? null : new ChatMessage(ChatRole.System, systemPrompt.Trim());
        var fixedTokens = Estimate(newMessage, charsPerToken) + (system is null ? 0 : Estimate(system, charsPerToken));
        if (fixedTokens > budget)
        {
            throw new InferenceException(
                $"Die Nachricht ist zu lang für den Kontext ({fixedTokens} von {budget} Token). Kürze sie oder stelle in den Feineinstellungen einen grösseren Kontext ein.");
        }

        var kept = new List<ChatMessage>();
        var used = fixedTokens;
        // Von hinten (neueste) nach vorne (älteste) auffüllen, solange Platz ist.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var cost = Estimate(history[i], charsPerToken);
            if (used + cost > budget)
            {
                break;
            }

            kept.Insert(0, history[i]);
            used += cost;
        }

        // Der Verlauf soll mit einer Frage beginnen, nicht mitten in einer Antwort.
        while (kept.Count > 0 && kept[0].Role != ChatRole.User)
        {
            used -= Estimate(kept[0], charsPerToken);
            kept.RemoveAt(0);
        }

        var messages = new List<ChatMessage>(kept.Count + 2);
        if (system is not null)
        {
            messages.Add(system);
        }

        messages.AddRange(kept);
        messages.Add(newMessage);
        return new ContextPlan(messages, history.Count - kept.Count, used);
    }
}
