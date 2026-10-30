using System.IO;
using KiNexus.App.ViewModels;
using KiNexus.App.Views;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using KiNexus.Core.Resources;
using KiNexus.Core.Settings;
using KiNexus.Engines.LlamaCpp;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KiNexus.App.Services;

/// <summary>
/// Meldet alle Dienste, ViewModels und Fenster an (Dependency Injection).
/// So muss keine Klasse ihre Abhängigkeiten selbst erstellen.
/// </summary>
/// <remarks>
/// Dienste, die Pfade oder Ports aus den Einstellungen brauchen, werden erst beim ersten Gebrauch erstellt.
/// Dann sind die Einstellungen schon geladen. Änderungen an Pfaden und Ports wirken darum erst nach einem Neustart.
/// </remarks>
public static class ServiceRegistration
{
    public static ServiceProvider Build(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ISettingsService>(provider =>
            new SettingsService(paths.SettingsFile, provider.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellService, ShellService>();

        AddEngines(services, paths);

        services.AddSingleton<EngineTestViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void AddEngines(ServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<IKiRegistry>(provider => new KiRegistry(paths.KisDirectory, provider.GetRequiredService<ILogger<KiRegistry>>()));
        services.AddSingleton(provider => new ModelLibrary(
            paths.Resolve(Settings(provider).Paths.Models), provider.GetRequiredService<ILogger<ModelLibrary>>()));
        services.AddSingleton<IResourceMonitor, ResourceMonitor>();
        services.AddSingleton<ProcessSupervisor>();

        services.AddSingleton(_ => new OllamaModelStore(OllamaModelStore.DefaultDirectory()));
        services.AddSingleton<OllamaImporter>();

        services.AddSingleton(provider =>
        {
            var settings = Settings(provider);
            var options = new LlamaCppOptions(
                ServerExe: Path.Combine(paths.Resolve(settings.Paths.Engines), "llama.cpp", "llama-server.exe"),
                Port: settings.Ports.LlamaServer,
                LogFile: Path.Combine(paths.LogsDirectory, "llama-server.log"));
            return new LlamaCppBackend(options, paths, provider.GetRequiredService<ProcessSupervisor>(),
                provider.GetRequiredService<ModelLibrary>(), provider.GetRequiredService<ILogger<LlamaCppBackend>>());
        });
        services.AddSingleton(provider => new OllamaBackend(Settings(provider).Ports.Ollama, provider.GetRequiredService<ILogger<OllamaBackend>>()));
        services.AddSingleton<IInferenceBackend>(provider => provider.GetRequiredService<LlamaCppBackend>());
        services.AddSingleton<IInferenceBackend>(provider => provider.GetRequiredService<OllamaBackend>());
        services.AddSingleton<EngineManager>();
    }

    private static AppSettings Settings(IServiceProvider provider) => provider.GetRequiredService<ISettingsService>().Current;
}
