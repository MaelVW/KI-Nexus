using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Kis;

/// <summary>
/// Systemprompt ändern (System-Prompt-Tab, Bauplan 8.4): speichern in kis/&lt;id&gt;.json
/// und jede Fassung in chats.db (Tabelle system_prompt_versions). Die letzten 10 bleiben.
/// </summary>
public sealed class SystemPromptService(IKiRegistry kis, IChatRepository repository, ILogger<SystemPromptService> logger)
{
    public const int MaxVersions = 10;

    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(string kiId, CancellationToken ct) => repository.GetPromptVersionsAsync(kiId, ct);

    /// <summary>
    /// Speichert den neuen Prompt. Gibt es noch keine Fassungen, kommt zuerst der bisherige Prompt in die Liste,
    /// damit man ihn wiederherstellen kann. Gibt die gespeicherte KI zurück (eine neue Kopie).
    /// </summary>
    public async Task<KiProfile> SaveAsync(KiProfile ki, string prompt, CancellationToken ct)
    {
        var text = prompt.TrimEnd();
        var versions = await repository.GetPromptVersionsAsync(ki.Id, ct).ConfigureAwait(false);
        if (versions.Count == 0 && ki.SystemPrompt.Trim().Length > 0 && ki.SystemPrompt.TrimEnd() != text)
        {
            await repository.AddPromptVersionAsync(ki.Id, ki.SystemPrompt.TrimEnd(), MaxVersions, ct).ConfigureAwait(false);
        }

        var updated = JsonDefaults.Clone(ki);
        updated.SystemPrompt = text;
        await kis.SaveAsync(updated, ct).ConfigureAwait(false);

        if (versions.FirstOrDefault()?.Content != text)
        {
            await repository.AddPromptVersionAsync(ki.Id, text, MaxVersions, ct).ConfigureAwait(false);
        }

        logger.LogInformation("Systemprompt von {Id} gespeichert ({Length} Zeichen).", ki.Id, text.Length);
        return updated;
    }

    /// <summary>
    /// Was hat sich gegenüber der älteren Fassung geändert? Für die Liste "Versionen", z.B. "+ Beginne jede Antwort …".
    /// Die älteste Fassung zeigt ihre erste Zeile.
    /// </summary>
    public static string DescribeChange(string? older, string newer)
    {
        var newLines = Lines(newer);
        if (older is null)
        {
            return newLines.FirstOrDefault() ?? "(leer)";
        }

        var oldLines = Lines(older);
        if (newLines.FirstOrDefault(line => !oldLines.Contains(line)) is { } added)
        {
            return "+ " + added;
        }

        return oldLines.FirstOrDefault(line => !newLines.Contains(line)) is { } removed
            ? "− " + removed
            : newLines.FirstOrDefault() ?? "(leer)";
    }

    /// <summary>Zeilen ohne Leerzeilen und ohne Aufzählungszeichen am Anfang.</summary>
    private static List<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.Trim().TrimStart('-', '*', ' ').Trim()).Where(line => line.Length > 0)];

    /// <summary>Wie viele Token der Systemprompt ungefähr braucht. Das Modell liest ihn bei jeder Frage mit.</summary>
    public static int EstimateTokens(string prompt) =>
        prompt.Trim().Length == 0 ? 0 : ContextBudget.Estimate(new ChatMessage(ChatRole.System, prompt));
}
