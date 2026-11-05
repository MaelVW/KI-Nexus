using System.IO;
using System.Reflection;
using System.Windows;
using KiNexus.App.ViewModels;
using KiNexus.Core.Paths;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KiNexus.App.Modules;

/// <summary>
/// Ein Zusatzmodul, das nicht im öffentlichen Repository liegt (z.B. der private Fun-Tab).
/// Liegt seine DLL neben der App, lädt die App es beim Start. Fehlt sie, gibt es den Tab einfach nicht.
/// </summary>
public interface IAppModule
{
    /// <summary>Eigene Dienste anmelden (vor dem Bau des Hauptfensters).</summary>
    void ConfigureServices(IServiceCollection services, AppPaths paths);

    /// <summary>Vorlagen (DataTemplates) und Stile, die der Tab braucht. Werden in die App-Ressourcen übernommen.</summary>
    ResourceDictionary? CreateResources();

    /// <summary>Die Tabs des Moduls, ganz rechts in der Tab-Leiste.</summary>
    IEnumerable<TabViewModel> CreateTabs(IServiceProvider services, IChatShell shell);

    /// <summary>Knopf "Entladen": Speicher freigeben (z.B. Sprache).</summary>
    Task UnloadAsync();

    /// <summary>Beim Schliessen der App.</summary>
    Task ShutdownAsync();
}

/// <summary>Findet und lädt Module (nur die bekannten Dateinamen unten, nichts anderes).</summary>
public static class AppModules
{
    /// <summary>Bekannte Modul-DLLs neben der App.</summary>
    private static readonly string[] KnownFiles = ["KiNexus.Fun.dll"];

    public static IReadOnlyList<IAppModule> Loaded { get; private set; } = [];

    /// <summary>Gibt es den Gesprächsmodus? Nur mit dem privaten Fun-Modul (davon hängen z.B. die Sprach-Einstellungen ab).</summary>
    public static bool HasConversation => Loaded.Count > 0;

    public static void Load(string directory)
    {
        var modules = new List<IAppModule>();
        foreach (var file in KnownFiles.Select(name => Path.Combine(directory, name)).Where(File.Exists))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAppModule).IsAssignableFrom(t)))
                {
                    modules.Add((IAppModule)Activator.CreateInstance(type)!);
                    Log.Information("Modul geladen: {Type} aus {File}", type.FullName, Path.GetFileName(file));
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or MissingMethodException or TargetInvocationException)
            {
                // Ein kaputtes Modul darf die App nie am Starten hindern.
                Log.Error(ex, "Modul {File} konnte nicht geladen werden.", file);
            }
        }

        Loaded = modules;
    }
}
