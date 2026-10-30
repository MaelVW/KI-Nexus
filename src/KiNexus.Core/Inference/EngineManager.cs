using System.Diagnostics;
using System.Runtime.CompilerServices;
using KiNexus.Core.Kis;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Inference;

public enum EngineManagerState
{
    Unloaded,
    Loading,
    Ready,
    Answering,
}

/// <summary>
/// Verwaltet die Engines. Regeln:
/// <list type="bullet">
/// <item>Es ist immer nur ein Modell geladen (8 GB VRAM reichen nicht für zwei).</item>
/// <item>Das Modell wird beim ersten Bedarf geladen und aufgewärmt.</item>
/// <item>Nach "Geladen halten" Minuten ohne Frage wird es wieder entladen.</item>
/// </list>
/// </summary>
public sealed class EngineManager : IAsyncDisposable
{
    private readonly Dictionary<EngineKind, IInferenceBackend> _backends;
    private readonly ILogger<EngineManager> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _timerLock = new();
    private ITimer? _idleTimer;
    private CancellationTokenSource? _activeRequest;
    private string? _loadedSignature;

    public EngineManager(IEnumerable<IInferenceBackend> backends, ILogger<EngineManager> logger, TimeProvider time)
    {
        _backends = backends.ToDictionary(b => b.Kind);
        _logger = logger;
        _time = time;
    }

    public KiProfile? LoadedKi { get; private set; }

    public EngineManagerState State { get; private set; } = EngineManagerState.Unloaded;

    public string StatusMessage { get; private set; } = "Kein Modell geladen";

    /// <summary>Kann aus einem Hintergrund-Thread kommen.</summary>
    public event EventHandler? StateChanged;

    public IInferenceBackend GetBackend(EngineKind kind) =>
        _backends.TryGetValue(kind, out var backend)
            ? backend
            : throw new InferenceException($"Die Engine {kind} ist nicht eingerichtet.");

    public async Task EnsureLoadedAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ki);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loadedSignature == LoadSignature(ki) && LoadedKi is not null)
            {
                return;
            }

            await UnloadCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await LoadCoreAsync(ki, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stellt eine Frage und streamt die Antwort. Lädt das Modell, falls nötig.
    /// </summary>
    public async IAsyncEnumerable<ChatChunk> AskAsync(
        KiProfile ki,
        IReadOnlyList<ChatMessage> history,
        string userText,
        bool think,
        IProgress<string>? progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureLoadedAsync(ki, progress, ct).ConfigureAwait(false);
        StopIdleTimer();

        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeRequest = request;
        SetState(EngineManagerState.Answering, $"{ki.DisplayName} antwortet …");
        try
        {
            var chatRequest = new ChatRequest(ki, PromptBuilder.Build(ki.SystemPrompt, history, userText), think);
            await foreach (var chunk in GetBackend(ki.Engine).ChatStreamAsync(chatRequest, request.Token).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            _activeRequest = null;
            if (State == EngineManagerState.Answering)
            {
                SetState(EngineManagerState.Ready, $"{ki.DisplayName} bereit");
                StartIdleTimer(ki);
            }
        }
    }

    /// <summary>Bricht eine laufende Antwort ab und entlädt das Modell (Knopf "Entladen").</summary>
    public async Task UnloadAsync(CancellationToken ct)
    {
        CancelActiveRequest();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopIdleTimer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await UnloadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InferenceException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Entladen beim Beenden hat nicht geklappt.");
        }
    }

    /// <summary>
    /// Alles, was ein Neuladen nötig macht. Ändert sich z.B. nur die Temperatur, muss nicht neu geladen werden.
    /// </summary>
    internal static string LoadSignature(KiProfile ki) => string.Join("|",
        ki.Id, ki.Engine, ki.BaseModel.Path, ki.BaseModel.MmprojPath, ki.BaseModel.OllamaName,
        ki.Parameters.ContextLength, ki.EngineOptions.GpuLayers, ki.EngineOptions.CpuMoeLayers, ki.LoraAdapterPath);

    private async Task LoadCoreAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        SetState(EngineManagerState.Loading, $"{ki.DisplayName} wird geladen …");
        var reporter = new Progress<string>(message =>
        {
            SetState(EngineManagerState.Loading, message);
            progress?.Report(message);
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await GetBackend(ki.Engine).LoadAsync(ki, reporter, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Laden von {Ki} fehlgeschlagen.", ki.Id);
            SetState(EngineManagerState.Unloaded, ex is OperationCanceledException ? "Laden abgebrochen" : "Laden fehlgeschlagen");
            throw;
        }

        LoadedKi = ki;
        _loadedSignature = LoadSignature(ki);
        _logger.LogInformation("{Ki} geladen mit {Engine} in {Seconds:F1} s.", ki.Id, ki.Engine, stopwatch.Elapsed.TotalSeconds);
        SetState(EngineManagerState.Ready, $"{ki.DisplayName} bereit (geladen in {stopwatch.Elapsed.TotalSeconds:F0} s)");
        StartIdleTimer(ki);
    }

    private async Task UnloadCoreAsync(CancellationToken ct)
    {
        StopIdleTimer();
        if (LoadedKi is not { } loaded)
        {
            return;
        }

        await GetBackend(loaded.Engine).UnloadAsync(ct).ConfigureAwait(false);
        LoadedKi = null;
        _loadedSignature = null;
        _logger.LogInformation("{Ki} entladen.", loaded.Id);
        SetState(EngineManagerState.Unloaded, "Modell entladen");
    }

    private void CancelActiveRequest()
    {
        try
        {
            _activeRequest?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Die Anfrage war gerade fertig. Nichts zu tun.
        }
    }

    private void StartIdleTimer(KiProfile ki)
    {
        var minutes = ki.EngineOptions.KeepAliveMinutes;
        if (minutes <= 0)
        {
            return;
        }

        lock (_timerLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = _time.CreateTimer(_ => _ = UnloadAfterIdleAsync(), null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
        }
    }

    private void StopIdleTimer()
    {
        lock (_timerLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
    }

    private async Task UnloadAfterIdleAsync()
    {
        if (State != EngineManagerState.Ready)
        {
            return;
        }

        _logger.LogInformation("Keine Frage mehr seit der eingestellten Zeit. Das Modell wird entladen.");
        try
        {
            await UnloadAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InferenceException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Automatisches Entladen fehlgeschlagen.");
        }
    }

    private void SetState(EngineManagerState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
