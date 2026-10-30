using KiNexus.Core.Kis;

namespace KiNexus.Core.Inference;

/// <summary>
/// Gemeinsame Schnittstelle aller Engines (llama.cpp, Ollama, später der Server).
/// So kann die App Engines tauschen, ohne dass sich die Oberfläche ändert.
/// </summary>
/// <remarks>
/// Gegenüber dem Vorschlag im Bauplan dazugekommen:
/// <list type="bullet">
/// <item><see cref="Kind"/>: damit der EngineManager die passende Engine zu einer KI findet.</item>
/// <item>Fortschritt beim Laden (<see cref="IProgress{T}"/>): Laden kann 20 Sekunden dauern, die App zeigt, was gerade passiert.</item>
/// </list>
/// </remarks>
public interface IInferenceBackend
{
    EngineKind Kind { get; }

    string Name { get; }

    Task<BackendStatus> GetStatusAsync(CancellationToken ct);

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);

    /// <summary>Lädt das Modell der KI und wärmt es mit einer kurzen, unsichtbaren Anfrage auf.</summary>
    Task LoadAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct);

    /// <summary>Entlädt das Modell und gibt VRAM und RAM frei.</summary>
    Task UnloadAsync(CancellationToken ct);

    /// <summary>Streamt die Antwort Stück für Stück. Abbrechen über das CancellationToken.</summary>
    IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, CancellationToken ct);
}
