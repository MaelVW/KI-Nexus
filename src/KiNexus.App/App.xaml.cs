using System.Windows;
using System.Windows.Threading;
using KiNexus.App.Services;
using KiNexus.App.Views;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KiNexus.App;

/// <summary>
/// Startpunkt der App: Pfade finden, Logging starten, Einstellungen laden, Hauptfenster zeigen.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var paths = AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(AppPaths.RootEnvironmentVariable));
            paths.EnsureDataDirectories();
            Log.Logger = LoggingSetup.CreateLogger(paths.LogsDirectory);
            RegisterGlobalErrorHandlers();
            Log.Information("KI-Nexus {Version} startet. Projektordner: {Root}", AppInfo.Version, paths.Root);

            _services = ServiceRegistration.Build(paths);
            var settings = await _services.GetRequiredService<ISettingsService>().LoadAsync(CancellationToken.None);
            _services.GetRequiredService<ThemeService>().ApplyAccent(settings.AccentColor);

            MainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "KI-Nexus konnte nicht starten.");
            MessageBox.Show($"KI-Nexus konnte nicht starten.\n\n{ex.Message}\n\nDetails stehen im Log unter data\\logs.",
                "KI-Nexus", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("KI-Nexus wird beendet (Code {ExitCode}).", e.ApplicationExitCode);
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>Fängt Fehler ab, die sonst die App ohne Meldung abstürzen lassen.</summary>
    private void RegisterGlobalErrorHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unbehandelter Fehler. Die App stürzt ab.");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unbeobachteter Fehler in einer Hintergrund-Aufgabe.");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unbehandelter Fehler in der Oberfläche.");
        MessageBox.Show($"Es ist ein Fehler aufgetreten:\n\n{e.Exception.Message}\n\nDetails stehen im Log unter data\\logs.",
            "KI-Nexus", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
