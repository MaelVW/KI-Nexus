using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Processes;

/// <summary>
/// Startet und überwacht Hilfsprozesse (llama-server, später Python-Worker, Piper, Open WebUI).
/// Alle hängen an einem Job Object: Schliesst sich KI-Nexus, enden sie mit.
/// </summary>
public sealed class ProcessSupervisor(ILogger<ProcessSupervisor> logger) : IDisposable
{
    private readonly JobObject _job = new();

    /// <summary>Startet ein Programm ohne Fenster und hängt es ans Job Object.</summary>
    public Process Start(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? "",
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException($"{fileName} konnte nicht gestartet werden.");
        try
        {
            _job.Assign(process);
        }
        catch
        {
            process.Kill();
            process.Dispose();
            throw;
        }

        logger.LogInformation("Gestartet: {File} (PID {Pid})", Path.GetFileName(fileName), process.Id);
        return process;
    }

    /// <summary>Beendet einen Prozess und wartet kurz, bis er weg ist.</summary>
    public async Task StopAsync(Process process, CancellationToken ct)
    {
        if (process.HasExited)
        {
            return;
        }

        var pid = process.Id;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Beendet: PID {Pid}", pid);
    }

    public void Dispose() => _job.Dispose();
}
