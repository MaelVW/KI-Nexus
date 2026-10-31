using System.Diagnostics;
using System.Globalization;
using System.Text;
using KiNexus.Core.Processes;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.OpenWebUi;

public enum OpenWebUiState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

/// <summary>
/// Alternate Launch (Bauplan 8.7): startet Open WebUI unsichtbar im Hintergrund, wartet, bis es antwortet,
/// und stoppt es wieder. Die Ausgabe landet in data/logs/open-webui.log und in einer kurzen Liste für die Log-Ansicht.
/// </summary>
/// <remarks>
/// uvx startet eine ganze Kette (uvx → uv → open-webui → python → python), und Zwischenglieder enden teils früh.
/// Darum hängt alles an einem eigenen Job Object: "Stoppen" beendet den ganzen Job, egal wie die Kette aussieht.
/// Soll Open WebUI KI-Nexus überleben, merkt sich KI-Nexus alle Prozesse des Jobs (PID und Startzeit) in einer Datei.
/// So findet es sie beim nächsten Start wieder und kann sie stoppen.
/// </remarks>
/// <param name="processFile">Liste der Prozesse, wenn Open WebUI KI-Nexus überleben soll.</param>
public sealed class OpenWebUiLauncher(ProcessSupervisor supervisor, ILogger<OpenWebUiLauncher> logger, string logFile, string processFile)
    : IAsyncDisposable
{
    /// <summary>Beim ersten Start lädt uv viele Pakete (über 1 GB). Darum lange warten.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(10);
    private const int MaxLogLines = 400;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly LinkedList<string> _log = [];
    private readonly Lock _lock = new();
    private readonly List<Process> _adopted = [];
    private JobObject? _job;
    private Process? _process;
    private StreamWriter? _logWriter;
    private CancellationTokenSource? _starting;
    private bool _stopOnExit = true;
    private bool _disposed;

    public OpenWebUiState State { get; private set; } = OpenWebUiState.Stopped;

    public string StatusText { get; private set; } = "Gestoppt";

    public int Port { get; private set; } = 8080;

    /// <summary>Die Adresse für den Browser.</summary>
    public Uri Address => new($"http://localhost:{Port.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Läuft Open WebUI, aber KI-Nexus kann es nicht stoppen (z.B. von Hand gestartet)?</summary>
    public bool IsExternal { get; private set; }

    /// <summary>Zustand oder Text hat sich geändert. Kann aus einem Hintergrund-Thread kommen.</summary>
    public event EventHandler? Changed;

    /// <summary>Neue Zeilen im Log. Kann aus einem Hintergrund-Thread kommen.</summary>
    public event EventHandler? LogChanged;

    public IReadOnlyList<string> LogLines
    {
        get
        {
            lock (_lock)
            {
                return [.. _log];
            }
        }
    }

    /// <summary>Beim Start von KI-Nexus: Läuft Open WebUI schon (vom letzten Mal oder von Hand)?</summary>
    public async Task DetectAsync(int port, CancellationToken ct)
    {
        Port = port;
        _adopted.AddRange(ReadProcessFile());
        if (_adopted.Count > 0)
        {
            _stopOnExit = false;
        }

        if (await IsHealthyAsync(ct).ConfigureAwait(false))
        {
            IsExternal = _adopted.Count == 0;
            SetState(OpenWebUiState.Running, IsExternal ? "Läuft (nicht von KI-Nexus gestartet)" : "Läuft (vom letzten Mal)");
        }
        else if (_adopted.Count > 0)
        {
            // Prozesse vom letzten Mal, die nicht (mehr) antworten: aufräumen.
            await StopAsync().ConfigureAwait(false);
        }
    }

    public async Task StartAsync(OpenWebUiOptions options, CancellationToken ct)
    {
        if (State is OpenWebUiState.Starting or OpenWebUiState.Running or OpenWebUiState.Stopping)
        {
            return;
        }

        Port = options.Port;
        _stopOnExit = options.StopOnExit;
        IsExternal = false;
        if (!LocalPort.IsFree(options.Port))
        {
            if (await IsHealthyAsync(ct).ConfigureAwait(false))
            {
                IsExternal = true;
                SetState(OpenWebUiState.Running, "Läuft schon (nicht von KI-Nexus gestartet)");
                return;
            }

            SetState(OpenWebUiState.Failed, $"Port {Port} ist von einem anderen Programm belegt. Wähle im Zahnrad einen anderen Port.");
            return;
        }

        Directory.CreateDirectory(options.DataDirectory);
        var fresh = !File.Exists(Path.Combine(options.DataDirectory, "webui.db"));
        StartProcess(options, fresh);

        using var starting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _starting = starting;
        try
        {
            await WaitUntilReadyAsync(starting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (State == OpenWebUiState.Stopping || ct.IsCancellationRequested)
        {
            // Während des Starts gestoppt.
        }
        finally
        {
            _starting = null;
        }
    }

    public async Task StopAsync()
    {
        if (_job is null && _process is null && _adopted.Count == 0)
        {
            if (IsExternal && State == OpenWebUiState.Running)
            {
                SetState(OpenWebUiState.Running, "Läuft ausserhalb von KI-Nexus. Beenden: dort, wo du es gestartet hast (oder im Task-Manager).");
            }

            return;
        }

        SetState(OpenWebUiState.Stopping, "Wird beendet …");
        _starting?.Cancel();
        try
        {
            _job?.Terminate();
            if (_process is { HasExited: false } process)
            {
                // Falls ein Prozess dem Job entwischt ist (er hat vor der Zuweisung schon etwas gestartet).
                await supervisor.StopAsync(process, CancellationToken.None).ConfigureAwait(false);
            }

            foreach (var adopted in _adopted.Where(p => !p.HasExited))
            {
                adopted.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Open WebUI liess sich nicht sauber beenden.");
        }

        await WaitForPortAsync().ConfigureAwait(false);
        _job?.Dispose();
        _job = null;
        _process = null;
        _adopted.ForEach(p => p.Dispose());
        _adopted.Clear();
        DeleteProcessFile();
        logger.LogInformation("Open WebUI gestoppt.");
        SetState(OpenWebUiState.Stopped, "Gestoppt");
    }

    /// <summary>Beim Schliessen von KI-Nexus: stoppen, wenn so eingestellt. Sonst läuft Open WebUI weiter.</summary>
    public async ValueTask DisposeAsync()
    {
        // Wird zweimal gerufen: vom Hauptfenster beim Schliessen und vom DI-Container beim Beenden.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_stopOnExit)
        {
            await StopAsync().ConfigureAwait(false);
        }
        else
        {
            // Open WebUI läuft weiter. Die Liste ist dann ganz aktuell für den nächsten Start.
            WriteProcessFile();
        }

        // Ohne "beim Schliessen beenden" lässt das Schliessen des Handles die Prozesse weiterlaufen.
        _job?.Dispose();
        _job = null;
        _logWriter?.Dispose();
        _http.Dispose();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri($"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}/health"), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private void StartProcess(OpenWebUiOptions options, bool fresh)
    {
        lock (_lock)
        {
            _log.Clear();
        }

        _logWriter?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        _logWriter = new StreamWriter(logFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };

        var arguments = OpenWebUiCommand.Arguments(options);
        var workingDirectory = OpenWebUiCommand.WorkingDirectory(options.DataDirectory);
        AddLog($"> uvx {string.Join(' ', arguments)}   (Ordner: {workingDirectory})");

        _job = new JobObject(killOnClose: options.StopOnExit);
        var process = supervisor.Start(options.UvxPath, arguments, workingDirectory, info =>
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            foreach (var (key, value) in OpenWebUiCommand.Environment(options, fresh))
            {
                info.Environment[key] = value;
            }
        }, assignToJob: false);
        _job.Assign(process);
        _process = process;

        _ = Task.Run(() => PumpAsync(process.StandardOutput));
        _ = Task.Run(() => PumpAsync(process.StandardError));
        WatchExit(process);
        SetState(OpenWebUiState.Starting, "Startet …");
        logger.LogInformation("Open WebUI gestartet (PID {Pid}, Port {Port}, {Package}, Ordner {Folder}).",
            process.Id, options.Port, OpenWebUiCommand.Package(options.PinnedVersion), workingDirectory);
    }

    private async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StartTimeout)
        {
            if (_process is not { HasExited: false })
            {
                return; // WatchExit meldet den Fehler.
            }

            if (await IsHealthyAsync(ct).ConfigureAwait(false))
            {
                if (!_stopOnExit)
                {
                    WriteProcessFile(); // Falls KI-Nexus abstürzt, bevor es die Liste beim Schliessen schreibt.
                }

                SetState(OpenWebUiState.Running, $"Läuft (bereit nach {clock.Elapsed.TotalSeconds:F0} s)");
                logger.LogInformation("Open WebUI bereit nach {Seconds:F0} s.", clock.Elapsed.TotalSeconds);
                return;
            }

            var hint = clock.Elapsed.TotalSeconds > 60 ? " Beim ersten Start oder nach einem Update lädt uv Pakete, das dauert." : "";
            SetState(OpenWebUiState.Starting, $"Startet … {clock.Elapsed.TotalSeconds:F0} s.{hint}");
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        logger.LogWarning("Open WebUI ist nach {Minutes} Minuten nicht bereit.", StartTimeout.TotalMinutes);
        await StopAsync().ConfigureAwait(false);
        SetState(OpenWebUiState.Failed, $"Open WebUI hat nach {StartTimeout.TotalMinutes:F0} Minuten nicht geantwortet. Details im Log.");
    }

    /// <summary>uvx endet, wenn Open WebUI endet (abgestürzt oder von aussen beendet).</summary>
    private void WatchExit(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (_process != process || State is OpenWebUiState.Stopping or OpenWebUiState.Stopped)
            {
                return;
            }

            var code = process.HasExited ? process.ExitCode : -1;
            AddLog($"Prozess beendet (Code {code}).");
            logger.LogWarning("Open WebUI hat sich beendet (Code {Code}).", code);
            _job?.Terminate();
            _job?.Dispose();
            _job = null;
            _process = null;
            SetState(OpenWebUiState.Failed, $"Open WebUI hat sich beendet (Code {code}). Die Ursache steht im Log.");
        });
    }

    // ----------------------------------------------------------- Prozess-Liste (nur wenn Open WebUI KI-Nexus überleben soll)

    private void WriteProcessFile()
    {
        if (_job is null)
        {
            return;
        }

        try
        {
            var lines = new List<string>();
            foreach (var id in _job.ProcessIds())
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{id};{process.StartTime.ToUniversalTime().Ticks}"));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Prozess ist gerade beendet worden.
                }
            }

            File.WriteAllLines(processFile, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Prozess-Liste von Open WebUI konnte nicht geschrieben werden.");
        }
    }

    /// <summary>Nur Prozesse mit derselben PID und Startzeit gelten (PIDs werden von Windows wiederverwendet).</summary>
    private List<Process> ReadProcessFile()
    {
        var found = new List<Process>();
        if (!File.Exists(processFile))
        {
            return found;
        }

        foreach (var line in File.ReadAllLines(processFile))
        {
            var parts = line.Split(';');
            if (parts.Length != 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var id)
                || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var ticks))
            {
                continue;
            }

            try
            {
                var process = Process.GetProcessById(id);
                if (process.StartTime.ToUniversalTime().Ticks == ticks)
                {
                    found.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Läuft nicht mehr.
            }
        }

        if (found.Count == 0)
        {
            DeleteProcessFile();
        }

        return found;
    }

    private void DeleteProcessFile()
    {
        try
        {
            File.Delete(processFile);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Prozess-Liste liess sich nicht löschen.");
        }
    }

    /// <summary>Nach dem Beenden kurz warten, bis Windows den Port freigibt (höchstens 5 s).</summary>
    private async Task WaitForPortAsync()
    {
        for (var i = 0; i < 25 && !LocalPort.IsFree(Port); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                AddLog(OpenWebUiCommand.StripAnsi(line));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Ausgabe von Open WebUI beendet.");
        }
    }

    private void AddLog(string line)
    {
        lock (_lock)
        {
            _log.AddLast(line);
            while (_log.Count > MaxLogLines)
            {
                _log.RemoveFirst();
            }

            try
            {
                _logWriter?.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
                // Log-Datei schon zu (App schliesst).
            }
        }

        LogChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(OpenWebUiState state, string text)
    {
        State = state;
        StatusText = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
