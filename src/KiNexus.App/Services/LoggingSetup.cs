using System.IO;
using Serilog;

namespace KiNexus.App.Services;

/// <summary>
/// Logging mit Serilog. Pro Tag eine Datei in data/logs/, die letzten 14 Tage bleiben.
/// </summary>
public static class LoggingSetup
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static ILogger CreateLogger(string logsDirectory)
    {
        var configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logsDirectory, "ki-nexus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: OutputTemplate);

#if DEBUG
        configuration.MinimumLevel.Debug();
#else
        configuration.MinimumLevel.Information();
#endif

        return configuration.CreateLogger();
    }
}
