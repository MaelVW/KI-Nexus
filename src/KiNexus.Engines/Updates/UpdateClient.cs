using System.Globalization;
using System.Net;
using System.Text.Json;

namespace KiNexus.Engines.Updates;

/// <summary>Eine freigegebene Version auf GitHub (ein "Release" mit Tag, z.B. v1.12.0).</summary>
/// <param name="Notes">Der Text des Releases (aus dem Changelog).</param>
public sealed record ReleaseInfo(string Tag, Version Version, string Name, string Notes, Uri Page, DateTimeOffset? PublishedAt);

public enum UpdateState
{
    UpToDate,
    Available,

    /// <summary>Es gibt noch kein Release (oder das Repository ist privat).</summary>
    NoRelease,
    Failed,
}

public sealed record UpdateCheck(UpdateState State, string Message, ReleaseInfo? Release = null);

/// <summary>
/// Sucht nach einer neueren Version von KI-Nexus (GitHub Releases). Nur wenn man auf „Nach Updates suchen“ klickt,
/// nie beim Start: Die App bleibt lokal (Bauplan, Kapitel 2). Geschickt wird nur die Anfrage selbst, keine Daten.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    public const string Repository = "MaelVW/KI-Nexus";

    private static readonly Uri ApiBase = new("https://api.github.com/");
    private readonly HttpClient _http;

    /// <param name="handler">Für Tests: ein Ersatz für das Netzwerk.</param>
    public UpdateClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = ApiBase;
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("KI-Nexus-Update");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateCheck> CheckAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri($"repos/{Repository}/releases/latest", UriKind.Relative), ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheck(UpdateState.NoRelease,
                    "Auf GitHub gibt es noch keine freigegebene Version (oder das Repository ist privat).");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheck(UpdateState.Failed, $"GitHub antwortet mit Fehler {(int)response.StatusCode}. Versuch es später nochmals.");
            }

            var release = ParseRelease(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return Compare(currentVersion, release);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            return new UpdateCheck(UpdateState.Failed, "Keine Verbindung zu GitHub: " + ex.Message);
        }
    }

    /// <summary>Ist das Release neuer als die laufende Version?</summary>
    public static UpdateCheck Compare(string currentVersion, ReleaseInfo? release)
    {
        if (release is null)
        {
            return new UpdateCheck(UpdateState.NoRelease, "Auf GitHub gibt es noch keine freigegebene Version.");
        }

        var current = ParseVersion(currentVersion);
        return release.Version > current
            ? new UpdateCheck(UpdateState.Available, $"Version {release.Version.ToString(3)} ist da (du hast {current.ToString(3)}).", release)
            : new UpdateCheck(UpdateState.UpToDate, $"Du hast die neueste Version ({current.ToString(3)}).", release);
    }

    /// <summary>Antwort von /releases/latest. Entwürfe und Vorab-Versionen zählen nicht.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Bool(root, "draft") || Bool(root, "prerelease") || Text(root, "tag_name") is not { Length: > 0 } tag)
        {
            return null;
        }

        return new ReleaseInfo(
            tag,
            ParseVersion(tag),
            Text(root, "name") is { Length: > 0 } name ? name : tag,
            Text(root, "body") ?? "",
            Uri.TryCreate(Text(root, "html_url"), UriKind.Absolute, out var page) ? page : new Uri($"https://github.com/{Repository}/releases"),
            DateTimeOffset.TryParse(Text(root, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null);
    }

    /// <summary>"v1.12.0" oder "1.12.0-beta" → 1.12.0. Wirft <see cref="FormatException"/>, wenn es keine Version ist.</summary>
    public static Version ParseVersion(string text)
    {
        var clean = text.Trim().TrimStart('v', 'V');
        var end = clean.IndexOfAny(['-', '+', ' ']);
        clean = end < 0 ? clean : clean[..end];
        var version = Version.Parse(clean);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    /// <summary>
    /// Ein Entwickler-Ordner (git clone) wird nicht vom Update-Skript überschrieben. Dort aktualisiert man mit "git pull".
    /// </summary>
    public static bool IsGitCheckout(string root) => Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"));

    public void Dispose() => _http.Dispose();

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
