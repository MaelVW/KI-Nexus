using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.LlamaCpp;

/// <summary>Wo llama-server liegt und auf welchem Port er läuft.</summary>
public sealed record LlamaCppOptions(string ServerExe, int Port, string LogFile);

/// <summary>
/// Die eigene Engine: startet llama-server.exe mit dem Modell der KI und spricht mit ihm über HTTP.
/// Pro geladenem Modell läuft genau ein llama-server. Entladen = Prozess beenden.
/// </summary>
public sealed class LlamaCppBackend : IInferenceBackend, IDisposable
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(5);

    private readonly LlamaCppOptions _options;
    private readonly AppPaths _paths;
    private readonly ProcessSupervisor _supervisor;
    private readonly ModelLibrary _library;
    private readonly ILogger<LlamaCppBackend> _logger;
    private readonly HttpClient _http;
    private Process? _process;
    private string? _loadedKiId;

    public LlamaCppBackend(LlamaCppOptions options, AppPaths paths, ProcessSupervisor supervisor, ModelLibrary library, ILogger<LlamaCppBackend> logger)
    {
        _options = options;
        _paths = paths;
        _supervisor = supervisor;
        _library = library;
        _logger = logger;
        // Kein Gesamt-Timeout: Eine lange Antwort darf lange streamen. Abbrechen geht über das CancellationToken.
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Port}/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public EngineKind Kind => EngineKind.LlamaCpp;

    public string Name => "llama.cpp";

    public bool IsInstalled => File.Exists(_options.ServerExe);

    public Task<BackendStatus> GetStatusAsync(CancellationToken ct)
    {
        var status = !IsInstalled
            ? new BackendStatus(Kind, BackendState.NotInstalled, "llama.cpp fehlt. Führe scripts\\setup-engines.ps1 aus.")
            : _process is { HasExited: false }
                ? new BackendStatus(Kind, BackendState.Ready, "Modell geladen", _loadedKiId)
                : new BackendStatus(Kind, BackendState.Idle, "Bereit, kein Modell geladen");
        return Task.FromResult(status);
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<ModelInfo> models = _library.Scan()
            .Select(m => new ModelInfo(m.FolderName, m.Manifest.SizeBytes, m.Manifest.Quantization))
            .ToList();
        return Task.FromResult(models);
    }

    public async Task LoadAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        EnsureCanLoad(ki);
        await UnloadAsync(ct).ConfigureAwait(false);
        await WaitForFreePortAsync(ct).ConfigureAwait(false);

        progress?.Report("llama-server startet …");
        var arguments = LlamaServerArguments.Build(ki, _paths, _options.Port, _options.LogFile);
        _logger.LogInformation("Starte llama-server: {Args}", string.Join(' ', arguments));
        _process = _supervisor.Start(_options.ServerExe, arguments);

        try
        {
            await WaitUntilHealthyAsync(ki, progress, ct).ConfigureAwait(false);
            progress?.Report("Aufwärmen …");
            await WarmUpAsync(ki, ct).ConfigureAwait(false);
            _loadedKiId = ki.Id;
        }
        catch
        {
            await UnloadAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UnloadAsync(CancellationToken ct)
    {
        var process = _process;
        _process = null;
        _loadedKiId = null;
        if (process is null)
        {
            return;
        }

        await _supervisor.StopAsync(process, ct).ConfigureAwait(false);
        process.Dispose();
    }

    public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_loadedKiId != request.Ki.Id)
        {
            throw new InferenceException("Das Modell ist nicht geladen.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(LlamaCppProtocol.BuildChatRequest(request, stream: true), ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        LlamaTimings? timings = null;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (LlamaCppProtocol.ParseStreamLine(line) is not { } streamEvent)
            {
                continue;
            }

            timings = streamEvent.Timings ?? timings;
            if (streamEvent.Thinking.Length > 0)
            {
                yield return ChatChunk.FromThinking(streamEvent.Thinking);
            }

            if (streamEvent.Text.Length > 0)
            {
                yield return ChatChunk.FromText(streamEvent.Text);
            }
        }

        yield return ChatChunk.Done(LlamaCppProtocol.ToStats(timings, stopwatch.Elapsed));
    }

    /// <summary>Bilder gehen, wenn die KI einen Projektor (mmproj) hat und die Datei da ist.</summary>
    public Task<bool> SupportsImagesAsync(KiProfile ki, CancellationToken ct) =>
        Task.FromResult(ki.BaseModel.MmprojPath is { Length: > 0 } path && File.Exists(_paths.Resolve(path)));

    public void Dispose()
    {
        _process?.Dispose();
        _http.Dispose();
    }

    private void EnsureCanLoad(KiProfile ki)
    {
        if (!IsInstalled)
        {
            throw new InferenceException("llama.cpp ist nicht eingerichtet. Führe im Projektordner scripts\\setup-engines.ps1 aus.");
        }

        var modelPath = ki.BaseModel.Path is { Length: > 0 } path ? _paths.Resolve(path) : null;
        if (modelPath is null || !File.Exists(modelPath))
        {
            throw new InferenceException($"Die Modell-Datei fehlt: {modelPath ?? "(kein Pfad)"}.");
        }
    }

    /// <summary>
    /// Nach dem Beenden des alten llama-server gibt Windows den Port erst nach ca. 0.2 s frei.
    /// Darum warten wir kurz, bevor wir "Port belegt" melden.
    /// </summary>
    private async Task WaitForFreePortAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!LocalPort.IsFree(_options.Port))
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new InferenceException($"Port {_options.Port} ist schon belegt. Läuft noch ein anderer llama-server? Den Port kannst du im Zahnrad ändern.");
            }

            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    private async Task WaitUntilHealthyAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < LoadTimeout)
        {
            ct.ThrowIfCancellationRequested();
            if (_process is null || _process.HasExited)
            {
                throw new InferenceException($"llama-server ist beim Laden abgestürzt. {LastLogError()}");
            }

            if (await IsHealthyAsync(ct).ConfigureAwait(false))
            {
                return;
            }

            progress?.Report($"{ki.DisplayName} wird geladen … {stopwatch.Elapsed.TotalSeconds:F0} s");
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        throw new InferenceException($"llama-server war nach {LoadTimeout.TotalMinutes:F0} Minuten nicht bereit.");
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.GetAsync("health", timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Server hört noch nicht oder lädt noch (503).
            return false;
        }
    }

    /// <summary>
    /// Lektion 1 im Bauplan: Die erste Anfrage nach dem Laden ist sehr langsam.
    /// Darum schicken wir eine kurze, unsichtbare Anfrage ohne Thinking.
    /// </summary>
    private async Task WarmUpAsync(KiProfile ki, CancellationToken ct)
    {
        var request = new ChatRequest(ki, [new ChatMessage(ChatRole.User, "Hi")], Think: false);
        var body = LlamaCppProtocol.BuildChatRequest(request, stream: false);
        body["max_tokens"] = 1;
        body["cache_prompt"] = false;
        using var response = await SendAsync(body, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(JsonObject body, CancellationToken ct)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(body) };
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InferenceException("llama-server antwortet nicht. Ist er abgestürzt? Details im Log.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();
            throw new InferenceException($"llama-server meldet Fehler {(int)response.StatusCode}: {Shorten(error)}");
        }

        return response;
    }

    /// <summary>Die letzte Fehlerzeile aus dem Log von llama-server, für eine hilfreiche Meldung.</summary>
    private string LastLogError()
    {
        try
        {
            using var stream = new FileStream(_options.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            var error = lines.LastOrDefault(l => l.Contains(" E ", StringComparison.Ordinal) || l.Contains("error", StringComparison.OrdinalIgnoreCase));
            return error is null ? $"Details: {_options.LogFile}" : $"Letzte Meldung: {error.Trim()}";
        }
        catch (IOException)
        {
            return $"Details: {_options.LogFile}";
        }
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + " …";
}
