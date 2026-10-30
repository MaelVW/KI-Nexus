namespace KiNexus.Core.Paths;

/// <summary>
/// Kennt alle festen Ordner und Dateien von KI-Nexus.
/// Alles hängt am Projektordner (Root), z.B. C:\Users\mael\ki\KI-Nexus.
/// </summary>
public sealed class AppPaths
{
    /// <summary>An dieser Datei erkennt die App den Projektordner.</summary>
    public const string RootMarkerFile = "KI-Nexus.sln";

    /// <summary>Mit dieser Umgebungsvariable kann man den Projektordner fest vorgeben.</summary>
    public const string RootEnvironmentVariable = "KINEXUS_ROOT";

    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        // "C:\Projekt\" und "C:\Projekt" sollen derselbe Ordner sein.
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root { get; }

    public string DataDirectory => Path.Combine(Root, "data");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string CacheDirectory => Path.Combine(DataDirectory, "cache");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string ChatDatabaseFile => Path.Combine(DataDirectory, "chats.db");
    public string KisDirectory => Path.Combine(Root, "kis");

    /// <summary>
    /// Sucht den Projektordner.
    /// Reihenfolge: Umgebungsvariable, dann nach oben suchen bis zur Marker-Datei,
    /// sonst der Startordner selbst.
    /// </summary>
    public static AppPaths Detect(string startDirectory, string? environmentRoot)
    {
        if (!string.IsNullOrWhiteSpace(environmentRoot) && Directory.Exists(environmentRoot))
        {
            return new AppPaths(environmentRoot);
        }

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, RootMarkerFile)))
            {
                return new AppPaths(current.FullName);
            }

            current = current.Parent;
        }

        return new AppPaths(startDirectory);
    }

    /// <summary>
    /// Macht aus einem Pfad aus den Einstellungen einen vollen Pfad.
    /// Umgebungsvariablen wie %USERPROFILE% werden ersetzt.
    /// Relative Pfade gelten ab dem Projektordner.
    /// </summary>
    public string Resolve(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(Root, expanded));
    }

    /// <summary>Legt die Daten-Ordner an, falls sie fehlen.</summary>
    public void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
    }
}
