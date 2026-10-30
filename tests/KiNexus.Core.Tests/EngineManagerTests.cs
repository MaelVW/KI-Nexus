using System.Runtime.CompilerServices;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KiNexus.Core.Tests;

public class EngineManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnsureLoaded_LoadsOnlyOnce_ForSameKi()
    {
        var (manager, llama, _, _) = Create();
        var ki = Ki("a", EngineKind.LlamaCpp);

        await manager.EnsureLoadedAsync(ki, null, Ct);
        await manager.EnsureLoadedAsync(ki, null, Ct);

        Assert.Equal(1, llama.LoadCount);
        Assert.Equal(EngineManagerState.Ready, manager.State);
        Assert.Same(ki, manager.LoadedKi);
    }

    [Fact]
    public async Task EnsureLoaded_OtherEngine_UnloadsPreviousModelFirst()
    {
        var (manager, llama, ollama, _) = Create();

        await manager.EnsureLoadedAsync(Ki("a", EngineKind.LlamaCpp), null, Ct);
        await manager.EnsureLoadedAsync(Ki("b", EngineKind.Ollama), null, Ct);

        Assert.Equal(1, llama.UnloadCount);
        Assert.Equal(1, ollama.LoadCount);
        Assert.Equal("b", manager.LoadedKi?.Id);
    }

    [Fact]
    public async Task EnsureLoaded_ChangedContextLength_LoadsAgain()
    {
        var (manager, llama, _, _) = Create();
        var ki = Ki("a", EngineKind.LlamaCpp);

        await manager.EnsureLoadedAsync(ki, null, Ct);
        ki.Parameters.ContextLength = 16384;
        await manager.EnsureLoadedAsync(ki, null, Ct);

        Assert.Equal(2, llama.LoadCount);
    }

    [Fact]
    public async Task Ask_SendsSystemPromptAndQuestion_AndStreamsChunks()
    {
        var (manager, llama, _, _) = Create();
        var ki = Ki("a", EngineKind.LlamaCpp);
        ki.SystemPrompt = "Antworte auf Deutsch.";

        var chunks = await ToListAsync(manager.AskAsync(ki, [], "Hallo", think: true, null, Ct));

        Assert.Equal("Hallo zurück", string.Concat(chunks.Select(c => c.Text)));
        Assert.True(chunks[^1].IsDone);
        var request = Assert.Single(llama.Requests);
        Assert.True(request.Think);
        Assert.Equal([ChatRole.System, ChatRole.User], request.Messages.Select(m => m.Role));
        Assert.Equal(EngineManagerState.Ready, manager.State);
    }

    [Fact]
    public async Task Idle_UnloadsAfterKeepAliveMinutes()
    {
        var (manager, llama, _, time) = Create();
        var ki = Ki("a", EngineKind.LlamaCpp);
        ki.EngineOptions.KeepAliveMinutes = 5;
        await manager.EnsureLoadedAsync(ki, null, Ct);

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, llama.UnloadCount);

        time.Advance(TimeSpan.FromMinutes(2));
        await WaitUntilAsync(() => manager.State == EngineManagerState.Unloaded);
        Assert.Equal(1, llama.UnloadCount);
    }

    [Fact]
    public async Task Idle_KeepAliveZero_NeverUnloads()
    {
        var (manager, llama, _, time) = Create();
        var ki = Ki("a", EngineKind.LlamaCpp);
        ki.EngineOptions.KeepAliveMinutes = 0;
        await manager.EnsureLoadedAsync(ki, null, Ct);

        time.Advance(TimeSpan.FromHours(3));

        Assert.Equal(0, llama.UnloadCount);
        Assert.Equal(EngineManagerState.Ready, manager.State);
    }

    [Fact]
    public async Task Unload_StopsRunningAnswer()
    {
        var (manager, llama, _, _) = Create();
        llama.ChunkDelay = TimeSpan.FromSeconds(30);
        var ki = Ki("a", EngineKind.LlamaCpp);
        var answering = ToListAsync(manager.AskAsync(ki, [], "Hallo", false, null, Ct));
        await WaitUntilAsync(() => manager.State == EngineManagerState.Answering);

        await manager.UnloadAsync(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answering);
        Assert.Equal(EngineManagerState.Unloaded, manager.State);
        Assert.Null(manager.LoadedKi);
    }

    [Fact]
    public async Task FailedLoad_LeavesManagerUnloaded()
    {
        var (manager, llama, _, _) = Create();
        llama.FailLoad = true;

        await Assert.ThrowsAsync<InferenceException>(() => manager.EnsureLoadedAsync(Ki("a", EngineKind.LlamaCpp), null, Ct));

        Assert.Equal(EngineManagerState.Unloaded, manager.State);
        Assert.Null(manager.LoadedKi);
    }

    private static (EngineManager Manager, FakeBackend Llama, FakeBackend Ollama, FakeTimeProvider Time) Create()
    {
        var llama = new FakeBackend(EngineKind.LlamaCpp);
        var ollama = new FakeBackend(EngineKind.Ollama);
        var time = new FakeTimeProvider();
        return (new EngineManager([llama, ollama], NullLogger<EngineManager>.Instance, time), llama, ollama, time);
    }

    private static KiProfile Ki(string id, EngineKind engine) => new() { Id = id, DisplayName = id.ToUpperInvariant(), Engine = engine };

    private static async Task<List<ChatChunk>> ToListAsync(IAsyncEnumerable<ChatChunk> source)
    {
        var list = new List<ChatChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }

        return list;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(condition());
    }

    /// <summary>Eine Engine nur für Tests: zählt Aufrufe und antwortet immer gleich.</summary>
    private sealed class FakeBackend(EngineKind kind) : IInferenceBackend
    {
        public EngineKind Kind => kind;
        public string Name => kind.ToString();
        public int LoadCount { get; private set; }
        public int UnloadCount { get; private set; }
        public bool FailLoad { get; set; }
        public TimeSpan ChunkDelay { get; set; }
        public List<ChatRequest> Requests { get; } = [];

        public Task<BackendStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new BackendStatus(kind, BackendState.Idle, ""));

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

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
            yield return ChatChunk.FromText("Hallo");
            await Task.Delay(ChunkDelay, ct);
            yield return ChatChunk.FromText(" zurück");
            yield return ChatChunk.Done(new ChatStats(5, 2, 100, 40, TimeSpan.FromSeconds(1), kind));
        }
    }
}
