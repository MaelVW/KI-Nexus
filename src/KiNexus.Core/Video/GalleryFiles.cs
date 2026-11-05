namespace KiNexus.Core.Video;

/// <summary>Verschiebt die Dateien eines Galerie-Eintrags (z.B. in den Privatordner).</summary>
internal static class GalleryFiles
{
    /// <summary>
    /// Alle oder keine: Gibt es im Ziel schon eine Datei mit demselben Namen, wird nichts verschoben.
    /// Geht beim Verschieben etwas schief, kommen die schon verschobenen Dateien zurück.
    /// </summary>
    public static void Move(IReadOnlyList<string> files, string targetDirectory)
    {
        if (files.FirstOrDefault(f => File.Exists(Path.Combine(targetDirectory, Path.GetFileName(f)))) is { } clash)
        {
            throw new IOException($"Im Ziel gibt es schon eine Datei {Path.GetFileName(clash)}.");
        }

        Directory.CreateDirectory(targetDirectory);
        var moved = new List<(string From, string To)>();
        try
        {
            foreach (var file in files)
            {
                var to = Path.Combine(targetDirectory, Path.GetFileName(file));
                File.Move(file, to);
                moved.Add((file, to));
            }
        }
        catch
        {
            foreach (var (from, to) in moved)
            {
                File.Move(to, from);
            }

            throw;
        }
    }
}
