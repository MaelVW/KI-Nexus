using System.Text.Json.Serialization;
using KiNexus.Core.Inference;

namespace KiNexus.Core.Kis;

/// <summary>
/// Eine KI = ein Grundmodell + Name, Systemprompt und Parameter.
/// Gespeichert als kis/&lt;id&gt;.json (Format: Bauplan, Kapitel 9).
/// </summary>
public sealed class KiProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public EngineKind Engine { get; set; } = EngineKind.LlamaCpp;
    public BaseModelRef BaseModel { get; set; } = new();
    public string SystemPrompt { get; set; } = "";
    public KiParameters Parameters { get; set; } = new();
    public EngineOptions EngineOptions { get; set; } = new();
    public string? LoraAdapterPath { get; set; }
    public KiVoiceOptions Voice { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Kurzer Name der Engine für die Anzeige.</summary>
    [JsonIgnore]
    public string EngineLabel => Engine == EngineKind.Ollama ? "Ollama" : "llama.cpp";

    /// <summary>Versteht die KI Bilder? (Bei llama.cpp: Projektor vorhanden.)</summary>
    [JsonIgnore]
    public bool SupportsImages => !string.IsNullOrWhiteSpace(BaseModel.MmprojPath);

    /// <summary>Der Name, z.B. für Screenreader in der KI-Auswahl.</summary>
    public override string ToString() => DisplayName;
}

public sealed class BaseModelRef
{
    /// <summary>GGUF-Datei, relativ zum Projektordner. Leer bei Ollama-KIs.</summary>
    public string? Path { get; set; }

    /// <summary>Projektor für Bilder (mmproj). Null = keine Bilder.</summary>
    public string? MmprojPath { get; set; }

    /// <summary>Name in Ollama, z.B. "gemma4:26b". Nur bei Ollama-KIs.</summary>
    public string? OllamaName { get; set; }

    /// <summary>Kleines Hilfsmodell für schnelleres Schreiben (Draft), falls das Grundmodell eins hat. Nur llama.cpp.</summary>
    public string? DraftPath { get; set; }
}

/// <summary>
/// Sampling- und Längen-Parameter. Null heisst: Standard des Modells bzw. der Engine.
/// </summary>
public sealed class KiParameters
{
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public double? MinP { get; set; }
    public double? RepeatPenalty { get; set; }
    public double? PresencePenalty { get; set; }
    public int ContextLength { get; set; } = 8192;
    public int MaxTokens { get; set; } = 2048;
    public bool ThinkByDefault { get; set; }
}

public sealed class EngineOptions
{
    /// <summary>"auto" = llama.cpp verteilt selbst (--fit). Sonst eine Zahl oder "all".</summary>
    public string GpuLayers { get; set; } = "auto";

    /// <summary>MoE-Experten-Schichten auf der CPU. Null = automatisch.</summary>
    public int? CpuMoeLayers { get; set; }

    /// <summary>Wie lange das Modell nach der letzten Frage geladen bleibt. 0 = bis zum Entladen.</summary>
    public int KeepAliveMinutes { get; set; } = 5;

    /// <summary>
    /// Draft-Modell nutzen (spekulatives Dekodieren): schreibt schneller, braucht aber mehr VRAM. Standard aus.
    /// Wirkt nur, wenn <see cref="BaseModelRef.DraftPath"/> gesetzt ist.
    /// </summary>
    public bool UseDraftModel { get; set; }
}

public sealed class KiVoiceOptions
{
    /// <summary>Mit diesem Modus startet das Gespräch, solange ein Chat keinen eigenen hat.</summary>
    public VoiceMode DefaultMode { get; set; } = VoiceMode.PushToTalk;

    /// <summary>Eigene Stimme der KI, z.B. "de_DE-ramona-low". Null = die Stimme aus den Einstellungen.</summary>
    public string? Voice { get; set; }

    /// <summary>Sprechtempo für Piper: 1 = normal, grösser = ruhiger und langsamer. Null = das Tempo aus den Einstellungen.</summary>
    public double? LengthScale { get; set; }
}

public enum VoiceMode
{
    PushToTalk,
    HandsFree,
}
