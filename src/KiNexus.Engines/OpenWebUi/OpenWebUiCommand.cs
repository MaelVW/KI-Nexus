using System.Globalization;
using System.Text.RegularExpressions;

namespace KiNexus.Engines.OpenWebUi;

/// <summary>Was zum Start von Open WebUI gebraucht wird.</summary>
/// <param name="UvxPath">uvx.exe (kommt mit uv).</param>
/// <param name="DataDirectory">Hier liegen Benutzer, Chats und Einstellungen von Open WebUI (DATA_DIR).</param>
/// <param name="PinnedVersion">Feste Version, z.B. "0.12.5". Leer = immer die neueste (@latest).</param>
/// <param name="StopOnExit">True: Open WebUI endet mit KI-Nexus (Job Object). False: läuft weiter.</param>
public sealed record OpenWebUiOptions(
    string UvxPath, string DataDirectory, int Port, int OllamaPort, int LlamaServerPort, string? PinnedVersion, bool StopOnExit);

/// <summary>
/// Baut den Befehl, so wie Mael Open WebUI bisher von Hand startet (Bauplan 8.7):
/// <c>uvx --python 3.11 open-webui@latest serve</c> mit <c>DATA_DIR</c>.
/// Dazu: nur auf 127.0.0.1 hören (Bauplan: alle Dienste nur lokal) und Telemetrie aus.
/// </summary>
public static partial class OpenWebUiCommand
{
    public const string PythonVersion = "3.11";

    /// <summary>Nur auf diesem Laptop erreichbar. Open WebUI selbst würde sonst auf 0.0.0.0 (ganzes Netzwerk) hören.</summary>
    public const string Host = "127.0.0.1";

    public static IReadOnlyList<string> Arguments(OpenWebUiOptions options) =>
    [
        "--python", PythonVersion,
        Package(options.PinnedVersion),
        "serve",
        "--host", Host,
        "--port", options.Port.ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>"open-webui@0.12.5" oder "open-webui@latest".</summary>
    public static string Package(string? pinnedVersion)
    {
        if (string.IsNullOrWhiteSpace(pinnedVersion))
        {
            return "open-webui@latest";
        }

        var version = pinnedVersion.Trim();
        return IsValidVersion(version)
            ? "open-webui@" + version
            : throw new ArgumentException($"\"{version}\" ist keine gültige Version (Beispiel: 0.12.5).", nameof(pinnedVersion));
    }

    /// <summary>Leer (= neueste) oder Zahlen mit Punkten, z.B. "0.12.5".</summary>
    public static bool IsValidVersion(string? version) => string.IsNullOrWhiteSpace(version) || Version().IsMatch(version.Trim());

    /// <param name="freshDataDirectory">
    /// True, wenn es noch keine webui.db gibt. Nur dann wirkt die Verbindung zu llama-server per Umgebungsvariable:
    /// Open WebUI speichert solche Einstellungen beim ersten Start in seiner Datenbank und liest sie danach nur noch dort.
    /// </param>
    public static IReadOnlyDictionary<string, string> Environment(OpenWebUiOptions options, bool freshDataDirectory)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATA_DIR"] = options.DataDirectory,
            ["OLLAMA_BASE_URL"] = $"http://127.0.0.1:{options.OllamaPort.ToString(CultureInfo.InvariantCulture)}",
            // Keine Telemetrie (Bauplan: alles bleibt lokal). Dieselben Werte setzt das offizielle Docker-Image.
            ["SCARF_NO_ANALYTICS"] = "true",
            ["DO_NOT_TRACK"] = "true",
            ["ANONYMIZED_TELEMETRY"] = "false",
            ["PYTHONIOENCODING"] = "utf-8",
        };

        if (freshDataDirectory)
        {
            environment["OPENAI_API_BASE_URL"] = LlamaServerUrl(options.LlamaServerPort);
            environment["OPENAI_API_KEY"] = "none";
        }

        return environment;
    }

    /// <summary>
    /// Arbeitsordner für Open WebUI. Wichtig wegen des Geheimschlüssels <c>.webui_secret_key</c>: Open WebUI liest ihn
    /// aus dem Arbeitsordner und erzeugt sonst einen neuen. Mit einem neuen Schlüssel gelten alle Anmeldungen nicht mehr.
    /// Wer Open WebUI bisher von Hand gestartet hat, hat den Schlüssel meist im Ordner über DATA_DIR.
    /// Gibt es einen Schlüssel, nehmen wir dessen Ordner (bei zweien den älteren). Sonst DATA_DIR.
    /// </summary>
    public static string WorkingDirectory(string dataDirectory)
    {
        const string KeyFile = ".webui_secret_key";
        var candidates = new[] { dataDirectory, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataDirectory)) }
            .OfType<string>()
            .Select(folder => new FileInfo(Path.Combine(folder, KeyFile)))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        return candidates.Count > 0 ? candidates[0].DirectoryName! : dataDirectory;
    }

    /// <summary>Adresse von llama-server für Open WebUI (OpenAI-kompatibel).</summary>
    public static string LlamaServerUrl(int port) => $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/v1";

    /// <summary>Farbcodes (ANSI) aus der Ausgabe entfernen, damit die Log-Ansicht lesbar bleibt.</summary>
    public static string StripAnsi(string line) => Ansi().Replace(line, "");

    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex Version();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}

/// <summary>Sucht uvx: zuerst im PATH, dann am üblichen Ort von uv (%USERPROFILE%\.local\bin).</summary>
public static class UvxLocator
{
    public static string? Find(string? pathVariable = null, string? userProfile = null)
    {
        var path = pathVariable ?? System.Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), "uvx.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var profile = userProfile ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(profile, ".local", "bin", "uvx.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
