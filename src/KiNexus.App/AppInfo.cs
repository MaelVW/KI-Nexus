using System.Reflection;

namespace KiNexus.App;

/// <summary>Infos über die App selbst, z.B. die Version aus Directory.Build.props.</summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        // Das SDK hängt den Git-Commit an ("0.1.0+abc123"). Für die Anzeige reicht die Version.
        var plusIndex = informational.IndexOf('+');
        return plusIndex < 0 ? informational : informational[..plusIndex];
    }
}
