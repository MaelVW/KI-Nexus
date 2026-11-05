using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KiNexus.Engines.HuggingFace;

/// <summary>
/// Spricht mit huggingface.co: Suche, Dateiliste, Download. Das ist neben Engine-Updates
/// der einzige Weg der App ins Internet, und er läuft nur, wenn du selbst suchst oder herunterlädst.
/// </summary>
public sealed class HuggingFaceClient : IDisposable
{
    public static readonly Uri DefaultBaseUri = new("https://huggingface.co/");

    private readonly HttpClient _http;
    private readonly Func<string?> _token;

    /// <param name="token">Liefert den gespeicherten Token oder null. Wird bei jeder Anfrage neu gefragt.</param>
    /// <param name="handler">Für Tests: ein Ersatz für das Netzwerk.</param>
    public HuggingFaceClient(Func<string?> token, HttpMessageHandler? handler = null, Uri? baseUri = null)
    {
        _token = token;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = baseUri ?? DefaultBaseUri;
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("KI-Nexus/" + (typeof(HuggingFaceClient).Assembly.GetName().Version?.ToString(3) ?? "0"));
    }

    /// <summary>Die Seite des Modells im Browser.</summary>
    public Uri WebPage(string repo) => new(_http.BaseAddress!, HuggingFaceProtocol.EscapeRepo(repo));

    public async Task<IReadOnlyList<HfModel>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        using var document = await GetJsonAsync(HuggingFaceProtocol.SearchPath(query, limit), ct).ConfigureAwait(false);
        return HuggingFaceProtocol.ParseSearch(document.RootElement);
    }

    public async Task<HfRepoFiles> GetFilesAsync(string repo, CancellationToken ct)
    {
        using var document = await GetJsonAsync(HuggingFaceProtocol.TreePath(repo), ct).ConfigureAwait(false);
        return HuggingFaceProtocol.Group(repo, HuggingFaceProtocol.ParseTree(document.RootElement));
    }

    /// <summary>Was das Training über ein Modell wissen muss: Bauart, Grösse, Chat-Vorlage, gesperrt, Lizenz, Original.</summary>
    public async Task<KiNexus.Core.Training.TrainingModelInfo> GetTrainingInfoAsync(string repo, CancellationToken ct)
    {
        using var document = await GetJsonAsync(HuggingFaceProtocol.ModelInfoPath(repo), ct).ConfigureAwait(false);
        return HuggingFaceProtocol.ParseTrainingInfo(repo, document.RootElement);
    }

    /// <summary>
    /// Öffnet eine Datei zum Herunterladen, ab Byte <paramref name="offset"/> (zum Fortsetzen).
    /// Hugging Face leitet auf einen Download-Server weiter. Den Token schickt .NET dabei nicht mit.
    /// </summary>
    public async Task<HttpResponseMessage> OpenFileAsync(string repo, string filePath, long offset, CancellationToken ct)
    {
        using var request = CreateRequest(HuggingFaceProtocol.ResolvePath(repo, filePath));
        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
        }

        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return response;
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return response;
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var request = CreateRequest(path);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, limit.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, limit.Token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HuggingFaceException("Hugging Face antwortet nicht (Zeitüberschreitung). Versuch es später nochmals.", ex);
        }
        catch (JsonException ex)
        {
            throw new HuggingFaceException("Hugging Face hat eine unerwartete Antwort geschickt.", ex);
        }
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (_token() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, completion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new HuggingFaceException("Hugging Face ist nicht erreichbar. Bist du mit dem Internet verbunden?", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.Dispose();
        throw new HuggingFaceException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Kein Zugriff. Das Modell ist gesperrt (gated): Akzeptiere die Bedingungen auf huggingface.co und hinterlege einen Token.",
            HttpStatusCode.NotFound => "Nicht gefunden. Gibt es das Modell oder die Datei noch?",
            HttpStatusCode.TooManyRequests => "Zu viele Anfragen an Hugging Face. Warte kurz und versuch es nochmals.",
            _ => $"Hugging Face meldet Fehler {(int)status}: {Shorten(body)}",
        });
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + " …";
}
