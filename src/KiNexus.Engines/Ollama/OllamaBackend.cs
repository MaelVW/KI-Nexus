using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.Ollama;

/// <summary>
/// Die zweite Engine: Ollama über seine HTTP-API. Nur nutzbar, wenn Ollama installiert ist und läuft.
/// Die App ändert dabei nie Dateien von Ollama.
/// </summary>
public sealed class OllamaBackend : IInferenceBackend, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaBackend> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _visionCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedModel;
    private string? _loadedKiId;

    public OllamaBackend(int port, ILogger<OllamaBackend> logger)
    {
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public EngineKind Kind => EngineKind.Ollama;

    public string Name => "Ollama";

    /// <summary>Hier installiert sich die Windows-App von Ollama standardmässig.</summary>
    public static string DefaultExecutable =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");

    public async Task<BackendStatus> GetStatusAsync(CancellationToken ct)
    {
        var version = await TryGetVersionAsync(ct).ConfigureAwait(false);
        if (version is null)
        {
            return File.Exists(DefaultExecutable)
                ? new BackendStatus(Kind, BackendState.NotRunning, "Ollama läuft nicht. Starte die Ollama-App, um Ollama-KIs zu nutzen.")
                : new BackendStatus(Kind, BackendState.NotInstalled, "Ollama ist nicht installiert.");
        }

        return _loadedKiId is null
            ? new BackendStatus(Kind, BackendState.Idle, $"Ollama {version} läuft")
            : new BackendStatus(Kind, BackendState.Ready, $"Ollama {version}: Modell geladen", _loadedKiId);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        using var response = await GetAsync("api/tags", TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        return document.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => new ModelInfo(m.GetProperty("name").GetString() ?? "", m.GetProperty("size").GetInt64()))
            .ToList();
    }

    public async Task LoadAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct)
    {
        if (await TryGetVersionAsync(ct).ConfigureAwait(false) is null)
        {
            throw new InferenceException("Ollama läuft nicht. Starte die Ollama-App oder wähle eine llama.cpp-KI.");
        }

        progress?.Report($"Ollama lädt {ki.BaseModel.OllamaName} …");
        using (await PostAsync("api/generate", OllamaProtocol.BuildLoadRequest(ki), ct).ConfigureAwait(false))
        {
        }

        progress?.Report("Aufwärmen …");
        await WarmUpAsync(ki, ct).ConfigureAwait(false);
        _loadedModel = ki.BaseModel.OllamaName;
        _loadedKiId = ki.Id;
    }

    public async Task UnloadAsync(CancellationToken ct)
    {
        var model = _loadedModel;
        _loadedModel = null;
        _loadedKiId = null;
        if (model is null)
        {
            return;
        }

        try
        {
            using var response = await PostAsync("api/generate", OllamaProtocol.BuildUnloadRequest(model), ct).ConfigureAwait(false);
        }
        catch (InferenceException ex)
        {
            // Läuft Ollama nicht mehr, ist das Modell sowieso entladen.
            _logger.LogInformation(ex, "Ollama zum Entladen nicht erreichbar.");
        }
    }

    public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await PostAsync("api/chat", OllamaProtocol.BuildChatRequest(request, stream: true), ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (OllamaProtocol.ParseStreamLine(line) is not { } streamEvent)
            {
                continue;
            }

            if (streamEvent.Thinking.Length > 0)
            {
                yield return ChatChunk.FromThinking(streamEvent.Thinking);
            }

            if (streamEvent.Text.Length > 0)
            {
                yield return ChatChunk.FromText(streamEvent.Text);
            }

            if (streamEvent.Stats is { } stats)
            {
                yield return ChatChunk.Done(stats);
                yield break;
            }
        }
    }

    /// <summary>Fragt Ollama (/api/show), ob das Modell Bilder versteht. Das Ergebnis merkt sich die App.</summary>
    public async Task<bool> SupportsImagesAsync(KiProfile ki, CancellationToken ct)
    {
        if (ki.BaseModel.OllamaName is not { Length: > 0 } model)
        {
            return false;
        }

        if (_visionCache.TryGetValue(model, out var known))
        {
            return known;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await PostAsync("api/show", OllamaProtocol.BuildShowRequest(model), limit.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var hasVision = OllamaProtocol.HasVision(document.RootElement);
            _visionCache[model] = hasVision;
            return hasVision;
        }
        catch (Exception ex) when (ex is InferenceException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Ollama läuft nicht: Dann geht sowieso nichts. Nicht merken, später nochmals fragen.
            return false;
        }
    }

    /// <summary>
    /// Löscht ein Modell über die Ollama-API (DELETE /api/delete). Ollama räumt seine Dateien selbst auf,
    /// die App fasst sie nicht an. Ein Hardlink in models/ behält seine Daten.
    /// </summary>
    public async Task DeleteModelAsync(string model, CancellationToken ct)
    {
        if (_loadedModel is not null && string.Equals(_loadedModel, model, StringComparison.OrdinalIgnoreCase))
        {
            await UnloadAsync(ct).ConfigureAwait(false);
        }

        using var message = new HttpRequestMessage(HttpMethod.Delete, "api/delete") { Content = JsonContent.Create(OllamaProtocol.BuildDeleteRequest(model)) };
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InferenceException("Ollama ist nicht erreichbar. Läuft die Ollama-App?", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InferenceException($"Ollama konnte {model} nicht löschen ({(int)response.StatusCode}): {error}");
            }
        }

        _visionCache.TryRemove(model, out _);
        _logger.LogInformation("Ollama-Modell {Model} über die API gelöscht.", model);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Lektion 1: kurze, unsichtbare Anfrage nach dem Laden.</summary>
    private async Task WarmUpAsync(KiProfile ki, CancellationToken ct)
    {
        var request = new ChatRequest(ki, [new ChatMessage(ChatRole.User, "Hi")], Think: false);
        var body = OllamaProtocol.BuildChatRequest(request, stream: false);
        body["options"]!["num_predict"] = 1;
        using var response = await PostAsync("api/chat", body, ct).ConfigureAwait(false);
    }

    private async Task<string?> TryGetVersionAsync(CancellationToken ct)
    {
        try
        {
            using var response = await GetAsync("api/version", TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
            return json?["version"]?.GetValue<string>() ?? "?";
        }
        catch (InferenceException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            var response = await _http.GetAsync(path, limit.Token).ConfigureAwait(false);
            return response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new InferenceException("Ollama ist nicht erreichbar.", ex);
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InferenceException("Ollama ist nicht erreichbar. Läuft die Ollama-App?", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();
            throw new InferenceException($"Ollama meldet Fehler {(int)response.StatusCode}: {error}");
        }

        return response;
    }
}
