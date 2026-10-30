using System.Text.Json.Serialization;
using KiNexus.Core.Kis;

namespace KiNexus.Core.Inference;

/// <summary>Welche Engine rechnet. In JSON: "llamacpp" oder "ollama" (Bauplan, Kapitel 9).</summary>
public enum EngineKind
{
    [JsonStringEnumMemberName("llamacpp")]
    LlamaCpp,

    [JsonStringEnumMemberName("ollama")]
    Ollama,
}

public enum BackendState
{
    /// <summary>Die Engine fehlt (z.B. engines/llama.cpp leer oder Ollama nicht installiert).</summary>
    NotInstalled,

    /// <summary>Installiert, aber nicht erreichbar (z.B. Ollama nicht gestartet).</summary>
    NotRunning,

    /// <summary>Bereit, aber kein Modell geladen.</summary>
    Idle,

    Loading,

    /// <summary>Ein Modell ist geladen und kann antworten.</summary>
    Ready,
}

/// <summary>Zustand einer Engine, mit einer kurzen Meldung auf Deutsch.</summary>
public sealed record BackendStatus(EngineKind Engine, BackendState State, string Message, string? LoadedKiId = null);

/// <summary>Ein Modell, das eine Engine kennt (z.B. aus "ollama list").</summary>
public sealed record ModelInfo(string Name, long SizeBytes, string? Details = null);

public enum ChatRole
{
    System,
    User,
    Assistant,
}

/// <summary>Eine Nachricht an das Modell. Bilder als volle Dateipfade (nur wenn die KI Bilder versteht).</summary>
public sealed record ChatMessage(ChatRole Role, string Content, IReadOnlyList<string>? ImagePaths = null)
{
    public bool HasImages => ImagePaths is { Count: > 0 };
}

/// <summary>
/// Eine Anfrage an die Engine. Messages enthält schon den Systemprompt (siehe ContextBudget).
/// MaxTokens überschreibt die maximale Antwortlänge der KI, z.B. für kurze Hintergrund-Aufgaben.
/// </summary>
public sealed record ChatRequest(KiProfile Ki, IReadOnlyList<ChatMessage> Messages, bool Think, int? MaxTokens = null)
{
    public int EffectiveMaxTokens => MaxTokens ?? Ki.Parameters.MaxTokens;
}

/// <summary>
/// Ein Stück der gestreamten Antwort: Text, Gedanken (Thinking) oder am Ende die Statistik.
/// </summary>
public sealed record ChatChunk(string Text, string Thinking, bool IsDone, ChatStats? Stats)
{
    public static ChatChunk FromText(string text) => new(text, "", false, null);

    public static ChatChunk FromThinking(string thinking) => new("", thinking, false, null);

    public static ChatChunk Done(ChatStats stats) => new("", "", true, stats);
}

/// <summary>
/// Statistik einer Antwort: Token, Tempo, Dauer, Engine.
/// PromptTokens = neu gelesene Token. ContextTokens = alles, was danach im Kontext steht (auch aus dem Cache).
/// </summary>
public sealed record ChatStats(
    int PromptTokens,
    int CompletionTokens,
    double PromptTokensPerSecond,
    double TokensPerSecond,
    TimeSpan Duration,
    EngineKind Engine,
    int ContextTokens = 0);

/// <summary>Fehler mit einer verständlichen Meldung auf Deutsch, die die App direkt anzeigen kann.</summary>
public sealed class InferenceException(string message, Exception? inner = null) : Exception(message, inner);
