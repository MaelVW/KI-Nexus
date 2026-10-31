using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using KiNexus.Core.Processes;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.Voice;

/// <summary>
/// Startet und steuert den Sprach-Worker (python/voice/worker.py): Mikrofon, Whisper und Piper.
/// Befehle gehen als JSON Lines auf stdin, Ereignisse kommen über <see cref="EventReceived"/> (nicht im UI-Thread).
/// </summary>
public sealed class VoiceWorker(ProcessSupervisor supervisor, ILogger<VoiceWorker> logger) : IAsyncDisposable
{
    /// <summary>Whisper turbo braucht auf der CPU ca. 8 s zum Laden. Mit Reserve für langsame Festplatten.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _process;
    private VoiceWorkerOptions? _options;
    private VoiceReady? _ready;
    private TaskCompletionSource<VoiceReady>? _pendingReady;

    public event Action<VoiceEvent>? EventReceived;

    public bool IsRunning => _process is { HasExited: false };

    public VoiceReady? Ready => _ready;

    /// <summary>
    /// Startet den Worker (falls nötig) und lädt Whisper und Piper. Gibt zurück, sobald er bereit ist.
    /// Läuft er schon mit denselben Einstellungen, passiert nichts.
    /// </summary>
    public async Task<VoiceReady> StartAsync(VoiceInstallation installation, VoiceWorkerOptions options, CancellationToken ct)
    {
        await _startLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning && _ready is not null && options == _options)
            {
                return _ready;
            }

            if (!IsRunning)
            {
                StartProcess(installation);
            }

            _ready = null;
            _options = options;
            _pendingReady = new TaskCompletionSource<VoiceReady>(TaskCreationOptions.RunContinuationsAsynchronously);
            await SendAsync(VoiceProtocol.Configure(options), ct).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(StartTimeout);
            try
            {
                _ready = await _pendingReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Der Sprach-Worker ist nicht rechtzeitig bereit geworden. Details im Log.");
            }

            logger.LogInformation("Sprach-Worker bereit ({Device}) in {Ms} ms.", _ready.Device, _ready.LoadMs);
            return _ready;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task SendAsync(JsonObject command, CancellationToken ct)
    {
        var process = _process;
        if (process is not { HasExited: false })
        {
            throw new InvalidOperationException("Der Sprach-Worker läuft nicht.");
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(command.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Schickt einen Befehl, wenn der Worker läuft. Sonst passiert nichts.</summary>
    public async Task TrySendAsync(JsonObject command)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            await SendAsync(command, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Befehl an den Sprach-Worker ging nicht durch.");
        }
    }

    /// <summary>Beendet den Worker (gibt RAM frei). Erst freundlich, nach 3 Sekunden hart.</summary>
    public async Task StopAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        await TrySendAsync(VoiceProtocol.Shutdown()).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await supervisor.StopAsync(process, CancellationToken.None).ConfigureAwait(false);
        }

        _ready = null;
        _options = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _process?.Dispose();
        _process = null;
    }

    /// <summary>Mikrofone auflisten, ohne Whisper zu laden (eigener kurzer Prozess, ca. 1 s).</summary>
    public static async Task<VoiceMicrophones> ListMicrophonesAsync(VoiceInstallation installation, CancellationToken ct)
    {
        var info = CreateStartInfo(installation);
        info.ArgumentList.Add("--list-microphones");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Python konnte nicht gestartet werden.");
        var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return VoiceProtocol.Parse(line) as VoiceMicrophones ?? new VoiceMicrophones([], null);
    }

    private void StartProcess(VoiceInstallation installation)
    {
        _process?.Dispose();
        var info = CreateStartInfo(installation);
        _process = supervisor.Start(info.FileName, info.ArgumentList, info.WorkingDirectory, target =>
        {
            target.RedirectStandardInput = true;
            target.RedirectStandardOutput = true;
            target.RedirectStandardError = true;
            target.StandardInputEncoding = new UTF8Encoding(false);
            target.StandardOutputEncoding = Encoding.UTF8;
            target.StandardErrorEncoding = Encoding.UTF8;
            foreach (var (key, value) in info.Environment)
            {
                target.Environment[key] = value;
            }
        });

        var process = _process;
        _ = Task.Run(() => ReadEventsAsync(process));
        _ = Task.Run(() => ReadLogAsync(process));
    }

    private static ProcessStartInfo CreateStartInfo(VoiceInstallation installation)
    {
        var info = new ProcessStartInfo(installation.PythonExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(installation.WorkerScript) ?? "",
        };

        // -I: isoliert (keine fremden Python-Pfade), -u: nicht puffern.
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(installation.WorkerScript);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";
        // Alles lokal: Hugging Face nie aus dem Worker heraus fragen (Modelle liegen in models\voice\).
        info.Environment["HF_HUB_OFFLINE"] = "1";
        return info;
    }

    private async Task ReadEventsAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                VoiceEvent? voiceEvent;
                try
                {
                    voiceEvent = VoiceProtocol.Parse(line);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
                {
                    logger.LogWarning(ex, "Unlesbare Zeile vom Sprach-Worker: {Line}", line);
                    continue;
                }

                if (voiceEvent is not null)
                {
                    Dispatch(voiceEvent);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Lesen vom Sprach-Worker beendet.");
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        logger.LogInformation("Sprach-Worker beendet (Code {Code}).", exitCode);
        _ready = null;
        _pendingReady?.TrySetException(new InvalidOperationException("Der Sprach-Worker ist beim Start beendet worden. Details im Log."));
        EventReceived?.Invoke(new VoiceExited(exitCode));
    }

    private void Dispatch(VoiceEvent voiceEvent)
    {
        switch (voiceEvent)
        {
            case VoiceReady ready:
                _pendingReady?.TrySetResult(ready);
                break;
            case VoiceError { Fatal: true } error when _pendingReady is { Task.IsCompleted: false }:
                _pendingReady.TrySetException(new InvalidOperationException(error.Message));
                break;
            case VoiceError error:
                logger.LogWarning("Sprach-Worker meldet: {Message}", error.Message);
                break;
        }

        EventReceived?.Invoke(voiceEvent);
    }

    private async Task ReadLogAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 0)
                {
                    logger.LogInformation("[voice] {Line}", line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Log vom Sprach-Worker beendet.");
        }
    }
}
