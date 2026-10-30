using KiNexus.App.ViewModels;
using KiNexus.App.Views;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KiNexus.App.Services;

/// <summary>
/// Meldet alle Dienste, ViewModels und Fenster an (Dependency Injection).
/// So muss keine Klasse ihre Abhängigkeiten selbst erstellen.
/// </summary>
public static class ServiceRegistration
{
    public static ServiceProvider Build(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false));
        services.AddSingleton(paths);

        services.AddSingleton<ISettingsService>(provider =>
            new SettingsService(paths.SettingsFile, provider.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellService, ShellService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
