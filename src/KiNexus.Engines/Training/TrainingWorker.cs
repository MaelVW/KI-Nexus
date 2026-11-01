using System.Diagnostics;
using System.Text;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.Training;

/// <summary>Wo das Training liegt: eigene Python-Umgebung, Worker-Skript und die Umwandlungs-Skripte von llama.cpp.</summary>
public sealed record TrainingInstallation(string PythonExe, string Script, string ConvertScriptsDir)
{
    public static TrainingInstallation For(AppPaths paths, string enginesDirectory) => new(
        Path.Combine(paths.Root, "python", "training", ".venv", "Scripts", "python.exe"),
        Path.Combine(paths.Root, "python", "training", "train.py"),
        Path.Combine(enginesDirectory, "llama.cpp", "convert"));

    /// <summary>Was fehlt noch? Null, wenn alles da ist.</summary>
    public string? Problem()
    {
        if (!File.Exists(PythonExe) || !File.Exists(Script))
        {
            return @"Das Training ist noch nicht eingerichtet. Führe einmal scripts\setup-training.ps1 aus (lädt ca. 2.3 GB: PyTorch mit CUDA, transformers, PEFT).";
        }

        return File.Exists(Path.Combine(ConvertScriptsDir, "convert_lora_to_gguf.py"))
            ? null
            : @"Die Umwandlungs-Skripte von llama.cpp fehlen. Führe scripts\setup-training.ps1 nochmals aus.";
    }
}

/// <summary>
/// Startet und steuert den Trainings-Worker (python/training/train.py). Ein Training = ein Prozess.
/// Ereignisse kommen über <see cref="EventReceived"/> (nicht im UI-Thread). Das Log landet in data/logs/training.log.
/// </summary>
public sealed class TrainingWorker(ProcessSupervisor supervisor, ILogger<TrainingWorker> logger, string logFile) : IAsyncDisposable
{
    private Process? _process;
    private string? _stopFile;

    public event Action<TrainingEvent>? EventReceived;

    public bool IsRunning => _process is { HasExited: false };

    /// <param name="stopFile">Diese Datei legt <see cref="StopAsync"/> an, um abzubrechen (steht auch in der Konfiguration).</param>
    /// <param name="hfToken">Nur für gesperrte Modelle auf Hugging Face. Geht als Umgebungsvariable an den Worker, nie in eine Datei.</param>
    public void Start(TrainingInstallation installation, string configFile, string stopFile, string? hfToken)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Es läuft schon ein Training.");
        }

        _process?.Dispose();
        _stopFile = stopFile;
        File.Delete(stopFile);
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        File.AppendAllText(logFile, $"{Environment.NewLine}===== Training {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ({configFile}) ====={Environment.NewLine}");
        _process = supervisor.Start(installation.PythonExe, ["-I", "-u", installation.Script, "--config", configFile],
            Path.GetDirectoryName(installation.Script), info => Configure(info, hfToken));
        var process = _process;
        _ = Task.Run(() => ReadEventsAsync(process));
        _ = Task.Run(() => ReadLogAsync(process));
    }

    /// <summary>Prüft die Umgebung (PyTorch, CUDA, Grafikkarte). Dauert ca. 5–15 Sekunden.</summary>
    public static async Task<TrainingCheck> CheckAsync(TrainingInstallation installation, CancellationToken ct)
    {
        var info = new ProcessStartInfo(installation.PythonExe) { UseShellExecute = false, CreateNoWindow = true };
        Configure(info, null);
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add(installation.Script);
        info.ArgumentList.Add("--check");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Python konnte nicht gestartet werden.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return TrainingProtocol.Parse(line) as TrainingCheck
            ?? new TrainingCheck(false, (await stderr.ConfigureAwait(false)).Trim().Split('\n').LastOrDefault() ?? "Keine Antwort.", null, null, null);
    }

    /// <summary>Abbrechen: Der Worker hört nach dem laufenden Schritt auf. Nach 20 Sekunden wird er beendet.</summary>
    public async Task StopAsync()
    {
        var process = _process;
        if (process is not { HasExited: false })
        {
            return;
        }

        try
        {
            if (_stopFile is not null)
            {
                await File.WriteAllTextAsync(_stopFile, "stop").ConfigureAwait(false);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Training hört nicht auf, wird beendet.");
            await supervisor.StopAsync(process, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false } process)
        {
            await supervisor.StopAsync(process, CancellationToken.None).ConfigureAwait(false);
        }

        _process?.Dispose();
        _process = null;
    }

    /// <summary>stdin bleibt zu: Ein offenes stdin würde unter Windows "import torch" blockieren (siehe train.py).</summary>
    private static void Configure(ProcessStartInfo info, string? hfToken)
    {
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        if (!string.IsNullOrWhiteSpace(hfToken))
        {
            info.Environment["HF_TOKEN"] = hfToken;
        }
    }

    private async Task ReadEventsAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                TrainingEvent? trainingEvent;
                try
                {
                    trainingEvent = TrainingProtocol.Parse(line);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    logger.LogWarning(ex, "Unlesbare Zeile vom Trainings-Worker: {Line}", line);
                    continue;
                }

                if (trainingEvent is not null)
                {
                    EventReceived?.Invoke(trainingEvent);
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Lesen vom Trainings-Worker beendet.");
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        logger.LogInformation("Trainings-Worker beendet (Code {Code}).", exitCode);
        EventReceived?.Invoke(new TrainingExited(exitCode));
    }

    private async Task ReadLogAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                await File.AppendAllTextAsync(logFile, line + Environment.NewLine).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Log vom Trainings-Worker beendet.");
        }
    }
}
