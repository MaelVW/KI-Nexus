using KiNexus.Core.Inference;

namespace KiNexus.Core.Chats;

/// <summary>Ein Chat (Tabelle "chats").</summary>
public sealed record ChatRecord(
    long Id,
    string Title,
    string? KiId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool ThinkEnabled,
    string? VoiceMode)
{
    public const string DefaultTitle = "Neuer Chat";

    public bool HasDefaultTitle => Title == DefaultTitle;
}

/// <summary>Eine Nachricht in einem Chat (Tabelle "messages").</summary>
public sealed record MessageRecord
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public ChatRole Role { get; init; }
    public string Content { get; init; } = "";
    public string Thinking { get; init; } = "";

    /// <summary>Wie lange die KI nachgedacht hat (für "Nachgedacht für X Sekunden").</summary>
    public long? ThinkingMs { get; init; }

    public string? KiId { get; init; }
    public string? Engine { get; init; }

    /// <summary>Angehängte Bilder als volle Pfade in data/attachments.</summary>
    public IReadOnlyList<string> AttachmentPaths { get; init; } = [];

    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public double? TokensPerSecond { get; init; }
    public long? DurationMs { get; init; }

    /// <summary>Wie viele Token nach dieser Antwort im Kontext belegt sind.</summary>
    public int? ContextTokens { get; init; }

    /// <summary>Bewertung: 1 = Daumen hoch, -1 = Daumen runter, null = keine.</summary>
    public int? Rating { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public ChatMessage ToChatMessage() => new(Role, Content, AttachmentPaths);
}
