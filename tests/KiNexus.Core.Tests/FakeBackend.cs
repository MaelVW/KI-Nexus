using System.Runtime.CompilerServices;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;

namespace KiNexus.Core.Tests;

/// <summary>Eine Engine nur für Tests: zählt Aufrufe und antwortet mit festen Stücken.</summary>
internal sealed class FakeBackend(EngineKind kind) : IInferenceBackend
{
    public EngineKind Kind => kind;
    public string Name => kind.ToString();
    public int LoadCount { get; private set; }
    public int UnloadCount { get; private set; }
    public bool FailLoad { get; set; }
    public bool SupportsImages { get; set; } = true;
    public TimeSpan ChunkDelay { get; set; }
    public List<ChatRequest> Requests { get; } = [];

    /// <summary>Die Stücke der Antwort. Standard: "Hallo" + " zurück".</summary>
    public List<ChatChunk> Script { get; set; } = [ChatChunk.FromText("Hallo"), ChatChunk.FromText(" zurück")];

    public Task<BackendStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new BackendStatus(kind, BackendState.Idle, ""));

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<bool> SupportsImagesAsync(KiProfile ki, CancellationToken ct) => Task.FromResult(SupportsImages);

    public Task LoadAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        if (FailLoad)
        {
            throw new InferenceException("geht nicht");
        }

        LoadCount++;
        return Task.CompletedTask;
    }

    public Task UnloadAsync(CancellationToken ct)
    {
        UnloadCount++;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add(request);
        for (var i = 0; i < Script.Count; i++)
        {
            yield return Script[i];
            if (i == 0)
            {
                await Task.Delay(ChunkDelay, ct);
            }
        }

        yield return ChatChunk.Done(new ChatStats(50, 2, 100, 40, TimeSpan.FromSeconds(1), kind, ContextTokens: 400));
    }
}
