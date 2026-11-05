using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using KiNexus.Core.Processes;
using KiNexus.Core.Video;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.Video;

/// <summary>
/// Videomodelle über ComfyUI (Bauplan Video, Kapitel 4 und 5): startet ComfyUI unsichtbar auf 127.0.0.1,
/// schickt den Workflow, liest den Fortschritt über den WebSocket und bricht bei Bedarf ab.
/// ComfyUI hängt am Job Object der App und endet spätestens mit KI-Nexus. Die Ausgabe landet in data/logs/video.log.
/// </summary>
public sealed class ComfyVideoBackend(
    ProcessSupervisor supervisor,
    ILogger<ComfyVideoBackend> logger,
    ComfyInstallation installation,
    ComfyOptions options) : IVideoBackend, IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>So lange darf ComfyUI nach dem Abbrechen brauchen, sonst wird es beendet.</summary>
    private static readonly TimeSpan InterruptTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _jobLock = new(1, 1);
    private readonly Lock _logLock = new();

    /// <summary>Zusätzliche Modell-Ordner (ausserhalb von models/video). ComfyUI kennt sie nur, wenn sie beim Start in der YAML-Datei standen.</summary>
    private readonly HashSet<string> _modelFolders = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private StreamWriter? _log;
    private bool _stopping;

    public string Name => "ComfyUI";

    public VideoBackendState State { get; private set; } = VideoBackendState.Stopped;

    public string StatusText { get; private set; } = "Gestoppt";

    public event EventHandler? Changed;

    private string BaseAddress => $"http://127.0.0.1:{options.Port.ToString(CultureInfo.InvariantCulture)}";

    private bool IsRunning => _process is { HasExited: false } && State is VideoBackendState.Ready or VideoBackendState.Busy;

    public async Task StartAsync(CancellationToken ct)
    {
        await _startLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            if (installation.Problem() is { } problem)
            {
                Fail(problem);
            }

            if (!LocalPort.IsFree(options.Port))
            {
                Fail($"Port {options.Port} ist schon belegt (läuft ComfyUI schon von Hand?). Beende das andere Programm oder wähle einen anderen Port.");
            }

            StartProcess();
            await WaitUntilReadyAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public IAsyncEnumerable<VideoProgress> GenerateAsync(VideoRequest request, VideoModelSet models, CancellationToken ct) =>
        RunExclusiveAsync(CheckJob(request, models), models.ModelFolder, "Erzeugt ein Video …", token => RunJobAsync(request, models, token), ct);

    public IAsyncEnumerable<VideoProgress> GenerateImageAsync(ImageRequest request, ImageModelSet models, CancellationToken ct) =>
        RunExclusiveAsync(CheckImageJob(request, models), models.ModelFolder, "Erzeugt Bilder …", token => RunImageJobAsync(request, models, token), ct);

    /// <summary>
    /// Gemeinsamer Rahmen für Video und Bild: Es läuft nur ein Auftrag, ComfyUI startet bei Bedarf, danach ist es wieder bereit.
    /// </summary>
    private async IAsyncEnumerable<VideoProgress> RunExclusiveAsync(string? problem, string? modelFolder, string busyText,
        Func<CancellationToken, IAsyncEnumerable<VideoProgress>> job, [EnumeratorCancellation] CancellationToken ct)
    {
        if (problem is not null)
        {
            yield return new VideoFailed(problem);
            yield break;
        }

        if (!await _jobLock.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            yield return new VideoFailed("Es läuft schon ein Auftrag (Video oder Bild). Warte, bis er fertig ist.");
            yield break;
        }

        try
        {
            // Ein Modell aus einem neuen Ordner: ComfyUI liest die Ordner nur beim Start. Darum kurz neu starten (ca. 15 s).
            if (modelFolder is { } folder && _modelFolders.Add(Path.GetFullPath(folder)) && IsRunning)
            {
                logger.LogInformation("Neuer Modell-Ordner {Folder}: ComfyUI startet neu.", folder);
                await StopAsync().ConfigureAwait(false);
            }

            if (!IsRunning)
            {
                yield return new VideoStage(VideoPhase.Starting, ComfyProtocol.Describe(VideoPhase.Starting));
            }

            var startError = await TryStartAsync(ct).ConfigureAwait(false);
            if (startError is not null)
            {
                yield return ct.IsCancellationRequested ? new VideoCancelled() : new VideoFailed(startError);
                yield break;
            }

            SetState(VideoBackendState.Busy, busyText);
            await foreach (var progress in job(ct).ConfigureAwait(false))
            {
                yield return progress;
            }
        }
        finally
        {
            if (State == VideoBackendState.Busy)
            {
                SetState(VideoBackendState.Ready, "Bereit");
            }

            _jobLock.Release();
        }
    }

    public async Task FreeMemoryAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            return;
        }

        using var response = await _http.PostAsJsonAsync(new Uri(BaseAddress + "/free"), new { unload_models = true, free_memory = true }, ct)
            .ConfigureAwait(false);
        logger.LogInformation("ComfyUI: Speicher freigegeben (HTTP {Status}).", (int)response.StatusCode);
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        _stopping = true;
        try
        {
            await supervisor.StopAsync(process, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "ComfyUI liess sich nicht sauber beenden.");
        }

        for (var i = 0; i < 25 && !LocalPort.IsFree(options.Port); i++)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }

        process.Dispose();
        _process = null;
        _stopping = false;
        SetState(VideoBackendState.Stopped, "Gestoppt");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_logLock)
        {
            _log?.Dispose();
            _log = null;
        }

        _http.Dispose();
        _startLock.Dispose();
        _jobLock.Dispose();
    }

    // ----------------------------------------------------------------- Auftrag

    public string? SetupProblem(VideoModelSet models)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (installation.Problem() is { } problem)
        {
            return problem;
        }

        var missing = models.MissingFiles(options.VideoModelsDirectory);
        return missing.Count == 0
            ? null
            : $"Für {models.Name} fehlen Dateien: {string.Join(", ", missing.Select(Path.GetFileName))}. "
              + @"Laden mit: scripts\setup-video.ps1 -Models";
    }

    private string? CheckJob(VideoRequest request, VideoModelSet models)
    {
        if (request.Problem() is { } problem)
        {
            return problem;
        }

        // Die Einrichtung prüft StartAsync. Hier nur die Modelldateien, damit ComfyUI gar nicht erst startet.
        var missing = models.MissingFiles(options.VideoModelsDirectory);
        return missing.Count == 0
            ? null
            : $"Für {models.Name} fehlen Dateien: {string.Join(", ", missing.Select(Path.GetFileName))}. "
              + @"Laden mit: scripts\setup-video.ps1 -Models";
    }

    public string? SetupProblem(ImageModelSet models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return installation.Problem() ?? MissingImageFiles(models);
    }

    private string? CheckImageJob(ImageRequest request, ImageModelSet models) =>
        request.Problem()
        ?? (request.HasReferences && !models.SupportsReferences
            ? $"{models.Name} kann keine Referenzbilder nutzen. Nimm FLUX.2 klein oder entferne die Referenzbilder."
            : null)
        ?? MissingImageFiles(models);

    private string? MissingImageFiles(ImageModelSet models)
    {
        if (options.ImageModelsDirectory is not { } directory)
        {
            return "Bilder sind in dieser Einrichtung nicht vorgesehen (kein Ordner für Bildmodelle).";
        }

        var missing = models.MissingFiles(directory);
        return missing.Count == 0
            ? null
            : $"Für {models.Name} fehlen Dateien: {string.Join(", ", missing.Select(Path.GetFileName))}. "
              + @"Laden mit: scripts\setup-video.ps1 -Images";
    }

    /// <summary>Null, wenn ComfyUI läuft. Sonst der Grund (auch beim Abbrechen während des Starts).</summary>
    private async Task<string?> TryStartAsync(CancellationToken ct)
    {
        try
        {
            await StartAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            await StopAsync().ConfigureAwait(false);
            return "Abgebrochen.";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private IAsyncEnumerable<VideoProgress> RunJobAsync(VideoRequest request, VideoModelSet models, CancellationToken ct)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_seed{request.Seed}");
        var startImage = CopyStartImage(request, prefix);
        var workflow = ComfyWorkflowBuilder.Build(request, models, prefix, startImage);
        logger.LogInformation("Video-Auftrag: {Width}×{Height}, {Frames} Bilder, Seed {Seed}.", request.Width, request.Height, request.Frames, request.Seed);
        return ExecuteAsync(workflow, "Video", ComfyProtocol.Describe, (files, seconds) =>
        {
            var video = files.GetValueOrDefault("KN_Save")?.FirstOrDefault(File.Exists);
            var thumbnail = files.GetValueOrDefault("KN_SaveThumbnail")?.FirstOrDefault(File.Exists);
            var lastFrame = files.GetValueOrDefault("KN_SaveLastFrame")?.FirstOrDefault(File.Exists);
            return video is not null
                ? new VideoDone(video, seconds, thumbnail, lastFrame)
                : new VideoFailed("ComfyUI ist fertig, hat aber keine Videodatei gemeldet.");
        }, ct);
    }

    private IAsyncEnumerable<VideoProgress> RunImageJobAsync(ImageRequest request, ImageModelSet models, CancellationToken ct)
    {
        // Eigener Unterordner "bilder" im Ausgabe-Ordner von ComfyUI, damit Bilder und Videos getrennt bleiben.
        var prefix = string.Create(CultureInfo.InvariantCulture, $"bilder/{DateTime.Now:yyyy-MM-dd_HHmmss}_seed{request.Seed}");
        var references = CopyReferenceImages(request, prefix);
        var source = CopySourceImage(request, prefix);
        var workflow = ComfyWorkflowBuilder.BuildImage(request, models, prefix, references, source);
        logger.LogInformation("Bild-Auftrag: {Model}, {Width}×{Height}, {Count} Bilder, {References} Referenzen, Seed {Seed}.",
            models.Name, request.Width, request.Height, request.Count, references.Count, request.Seed);
        return ExecuteAsync(workflow, "Bild", ComfyProtocol.DescribeImage, (files, seconds) =>
        {
            var images = files.GetValueOrDefault("KN_Save")?.Where(File.Exists).ToList() ?? [];
            return images.Count > 0
                ? new ImageDone(images, seconds)
                : new VideoFailed("ComfyUI ist fertig, hat aber keine Bilddatei gemeldet.");
        }, ct);
    }

    /// <summary>
    /// Schickt einen Workflow an ComfyUI und übersetzt die Meldungen. Die geschriebenen Dateien sammelt es pro Knoten-Titel
    /// (z.B. "KN_Save"), <paramref name="finish"/> macht daraus das Ergebnis.
    /// </summary>
    private async IAsyncEnumerable<VideoProgress> ExecuteAsync(JsonObject workflow, string kind, Func<VideoPhase, string> describe,
        Func<IReadOnlyDictionary<string, List<string>>, double, VideoProgress> finish, [EnumeratorCancellation] CancellationToken ct)
    {
        var titles = ComfyWorkflowBuilder.Titles(workflow);
        var denoiseNode = ComfyWorkflowBuilder.NodeId(workflow, "KN_Denoise");
        var clientId = Guid.NewGuid().ToString("N");
        var clock = Stopwatch.StartNew();

        using var socket = new ClientWebSocket();
        string? error = null;
        string? promptId = null;
        try
        {
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{options.Port.ToString(CultureInfo.InvariantCulture)}/ws?clientId={clientId}"), ct)
                .ConfigureAwait(false);
            (promptId, error) = await QueueAsync(workflow, clientId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or OperationCanceledException)
        {
            error = ct.IsCancellationRequested ? null : $"ComfyUI antwortet nicht: {ex.Message}";
        }

        if (promptId is null)
        {
            yield return error is null ? new VideoCancelled() : new VideoFailed(error);
            yield break;
        }

        logger.LogInformation("{Kind}-Auftrag {Prompt} läuft.", kind, promptId);

        // Abbrechen: ComfyUI bekommt /interrupt und meldet dann "execution_interrupted". Kommt nichts, wird es beendet.
        using var reading = new CancellationTokenSource();
        await using var onCancel = ct.Register(() => _ = InterruptAsync(reading));

        VideoPhase? phase = null;
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        VideoProgress? end = null;
        await foreach (var message in ReadMessagesAsync(socket, reading.Token).ConfigureAwait(false))
        {
            if (message.PromptId is { } id && id != promptId)
            {
                continue;
            }

            switch (message)
            {
                case ComfyExecuting executing when titles.TryGetValue(executing.Node, out var title)
                                                  && ComfyProtocol.PhaseFor(title) is { } next && next != phase:
                    phase = next;
                    yield return new VideoStage(next, describe(next));
                    break;
                case ComfyProgress progress when progress.Node == denoiseNode:
                    yield return new VideoStep(progress.Value, progress.Max, Math.Round(clock.Elapsed.TotalSeconds, 1));
                    break;
                case ComfyExecuted executed when titles.TryGetValue(executed.Node, out var title):
                    if (!files.TryGetValue(title, out var list))
                    {
                        files[title] = list = [];
                    }

                    list.AddRange(executed.Files.Select(file => ComfyProtocol.FullPath(file, options.OutputDirectory)));
                    break;
                case ComfySuccess:
                    end = finish(files, Math.Round(clock.Elapsed.TotalSeconds, 1));
                    break;
                case ComfyError failure:
                    logger.LogWarning("{Kind}-Auftrag {Prompt} fehlgeschlagen: {Node} {Message}", kind, promptId, failure.NodeType, failure.Message);
                    end = new VideoFailed(ComfyProtocol.Explain(failure));
                    break;
                case ComfyInterrupted:
                    end = new VideoCancelled();
                    break;
            }

            if (end is not null)
            {
                break;
            }
        }

        if (end is null && ct.IsCancellationRequested)
        {
            // ComfyUI hat aufs Abbrechen nicht reagiert und wurde beendet.
            await StopAsync().ConfigureAwait(false);
            end = new VideoCancelled();
        }

        logger.LogInformation("{Kind}-Auftrag {Prompt} beendet nach {Seconds:F0} s: {Result}.", kind, promptId, clock.Elapsed.TotalSeconds,
            end?.GetType().Name ?? "abgerissen");
        yield return end ?? new VideoFailed(@"Die Verbindung zu ComfyUI ist abgerissen. Details in data\logs\video.log.");
    }

    /// <summary>
    /// Bild zu Video: Das Startbild kommt in den Eingabe-Ordner von ComfyUI (data/comfyui/input). Eine Kopie statt Upload:
    /// ComfyUI läuft auf demselben Laptop. Null = kein Startbild.
    /// </summary>
    private string? CopyStartImage(VideoRequest request, string prefix)
    {
        if (request.StartImagePath is not { } source)
        {
            return null;
        }

        var name = prefix + "_start" + Path.GetExtension(source).ToLowerInvariant();
        var input = Path.Combine(options.WorkDirectory, "input");
        Directory.CreateDirectory(input);
        File.Copy(source, Path.Combine(input, name), overwrite: true);
        return name;
    }

    /// <summary>Bild zu Bild: die Vorlage in den Eingabe-Ordner von ComfyUI kopieren. Null = ohne Vorlage.</summary>
    private string? CopySourceImage(ImageRequest request, string prefix)
    {
        if (request.SourceImage is not { } source)
        {
            return null;
        }

        var input = Path.Combine(options.WorkDirectory, "input");
        Directory.CreateDirectory(input);
        var name = $"{Path.GetFileName(prefix)}_vorlage{Path.GetExtension(source).ToLowerInvariant()}";
        File.Copy(source, Path.Combine(input, name), overwrite: true);
        return name;
    }

    /// <summary>Referenzbilder in den Eingabe-Ordner von ComfyUI kopieren, wie das Startbild beim Video.</summary>
    private List<string> CopyReferenceImages(ImageRequest request, string prefix)
    {
        var names = new List<string>();
        var input = Path.Combine(options.WorkDirectory, "input");
        var baseName = Path.GetFileName(prefix);
        foreach (var (source, number) in (request.ReferenceImages ?? []).Select((s, n) => (s, n + 1)))
        {
            Directory.CreateDirectory(input);
            var name = $"{baseName}_ref{number}{Path.GetExtension(source).ToLowerInvariant()}";
            File.Copy(source, Path.Combine(input, name), overwrite: true);
            names.Add(name);
        }

        return names;
    }

    private async Task<(string? PromptId, string? Error)> QueueAsync(JsonObject workflow, string clientId, CancellationToken ct)
    {
        var body = new JsonObject { ["prompt"] = workflow, ["client_id"] = clientId };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(new Uri(BaseAddress + "/prompt"), content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = ComfyProtocol.ParseQueueResponse((int)response.StatusCode, text);
        if (result.Error is not null)
        {
            logger.LogWarning("ComfyUI lehnt den Workflow ab: {Body}", text);
        }

        return result;
    }

    private async Task InterruptAsync(CancellationTokenSource reading)
    {
        try
        {
            using var response = await _http.PostAsync(new Uri(BaseAddress + "/interrupt"), null).ConfigureAwait(false);
            logger.LogInformation("Video abgebrochen (HTTP {Status}).", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Abbrechen hat ComfyUI nicht erreicht.");
        }

        try
        {
            reading.CancelAfter(InterruptTimeout);
        }
        catch (ObjectDisposedException)
        {
            // Der Auftrag ist schon zu Ende.
        }
    }

    /// <summary>Liest Textmeldungen vom WebSocket. Bilder (Vorschauen) überspringt es. Endet beim Schliessen oder Abbrechen.</summary>
    private async IAsyncEnumerable<ComfyMessage> ReadMessagesAsync(ClientWebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            ValueWebSocketReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                logger.LogDebug(ex, "WebSocket zu ComfyUI beendet.");
                yield break;
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }

            message.Write(buffer, 0, received.Count);
            if (!received.EndOfMessage)
            {
                continue;
            }

            var isText = received.MessageType == WebSocketMessageType.Text;
            var json = isText ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length) : null;
            message.SetLength(0);
            ComfyMessage? parsed = null;
            try
            {
                parsed = ComfyProtocol.Parse(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "Unlesbare Meldung von ComfyUI.");
            }

            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    // ----------------------------------------------------------------- Prozess

    private void StartProcess()
    {
        Directory.CreateDirectory(options.OutputDirectory);
        foreach (var folder in new[] { "input", "temp", "user" })
        {
            Directory.CreateDirectory(Path.Combine(options.WorkDirectory, folder));
        }

        File.WriteAllText(installation.PathsFile, ComfyCommand.PathsYaml(options.VideoModelsDirectory, _modelFolders, options.ImageModelsDirectory));
        OpenLog();
        var arguments = ComfyCommand.Arguments(installation, options);
        WriteLog($"===== ComfyUI {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}: python {string.Join(' ', arguments)}");

        var process = supervisor.Start(installation.PythonExe, arguments, installation.Directory, info =>
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.Environment["PYTHONUTF8"] = "1";
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["HF_HUB_OFFLINE"] = "1";
            info.Environment["DO_NOT_TRACK"] = "1";
        });
        _process?.Dispose(); // Ein abgestürzter Prozess vom letzten Mal.
        _process = process;
        _ = Task.Run(() => PumpAsync(process.StandardOutput));
        _ = Task.Run(() => PumpAsync(process.StandardError));
        WatchExit(process);
        SetState(VideoBackendState.Starting, "ComfyUI startet …");
    }

    private async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StartTimeout)
        {
            if (_process is not { HasExited: false })
            {
                Fail(@"ComfyUI hat sich beim Start beendet. Details in data\logs\video.log.");
            }

            try
            {
                using var response = await _http.GetAsync(new Uri(BaseAddress + "/system_stats"), ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("ComfyUI bereit nach {Seconds:F0} s.", clock.Elapsed.TotalSeconds);
                    SetState(VideoBackendState.Ready, "Bereit");
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Noch nicht bereit.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        await StopAsync().ConfigureAwait(false);
        Fail($"ComfyUI hat nach {StartTimeout.TotalMinutes:F0} Minuten nicht geantwortet. Details in data\\logs\\video.log.");
    }

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

            if (_stopping || _process != process)
            {
                return;
            }

            var code = process.ExitCode;
            logger.LogWarning("ComfyUI hat sich beendet (Code {Code}).", code);
            WriteLog($"===== ComfyUI beendet (Code {code})");
            SetState(VideoBackendState.Failed, $@"ComfyUI hat sich beendet (Code {code}). Details in data\logs\video.log.");
        });
    }

    private void OpenLog()
    {
        lock (_logLock)
        {
            if (_log is not null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(options.LogFile)!);
            _log = new StreamWriter(options.LogFile, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    private async Task PumpAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                WriteLog(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Ausgabe von ComfyUI beendet.");
        }
    }

    private void WriteLog(string line)
    {
        lock (_logLock)
        {
            try
            {
                _log?.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
                // Log-Datei schon zu (App schliesst).
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void Fail(string message)
    {
        SetState(VideoBackendState.Failed, message);
        throw new InvalidOperationException(message);
    }

    private void SetState(VideoBackendState state, string text)
    {
        State = state;
        StatusText = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
