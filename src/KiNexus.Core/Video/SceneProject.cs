using System.Globalization;
using System.Text.Json;

namespace KiNexus.Core.Video;

public enum SceneStatus
{
    Open,
    Done,
    Failed,
}

/// <summary>Eine Szene: eigener Prompt und eigene Länge. Ab der zweiten startet sie mit dem letzten Bild der vorherigen.</summary>
public sealed class Scene
{
    public string Prompt { get; set; } = "";
    public double Seconds { get; set; } = SceneProject.DefaultSeconds;
    public long Seed { get; set; }
    public SceneStatus Status { get; set; }

    /// <summary>Dateinamen im Projektordner, z.B. "szene-01.mp4", "szene-01-ende.png", "szene-01.png".</summary>
    public string? Clip { get; set; }
    public string? LastFrame { get; set; }
    public string? Thumbnail { get; set; }
    public string? Error { get; set; }

    /// <summary>So lange hat die Szene gedauert (inkl. Start von ComfyUI), für die Gesamtzeit in der Galerie.</summary>
    public double GenerationSeconds { get; set; }
}

/// <summary>
/// Ein Szenen-Projekt (Bauplan Szenen, S1): mehrere Clips, die nahtlos aneinander anschliessen, und am Schluss ein Video.
/// Liegt als projekt.json in data/videos/projekte/&lt;name&gt;/, zusammen mit den Clips.
/// </summary>
public sealed class SceneProject
{
    public const double DefaultSeconds = 4;
    public const double MinSeconds = 2;
    public const double MaxSeconds = 8;

    public string Name { get; set; } = "";

    /// <summary>Gilt für alle Szenen (z.B. Licht, Kamera, Aussehen der Figur). Hält das Aussehen zusammen.</summary>
    public string Style { get; set; } = "";

    public int Width { get; set; } = 768;
    public int Height { get; set; } = 512;
    public bool WithAudio { get; set; } = true;
    public List<Scene> Scenes { get; set; } = [];

    /// <summary>Das zusammengesetzte Video im Projektordner ("video.mp4"), wenn schon zusammengesetzt.</summary>
    public string? FinalVideo { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public double TotalSeconds => Scenes.Sum(s => s.Seconds);

    public bool AllDone => Scenes.Count > 0 && Scenes.All(s => s.Status == SceneStatus.Done);

    /// <summary>Prompt für den Clip: die Szene, dann der Stil.</summary>
    public string PromptFor(int index) =>
        Style.Trim().Length == 0 ? Scenes[index].Prompt.Trim() : $"{Scenes[index].Prompt.Trim()} {Style.Trim()}";

    /// <summary>
    /// Übernimmt die Szenen aus dem Text (eine pro Zeile, wahlweise "Sekunden | Prompt"). Bleibt eine Szene gleich, bleibt sie fertig.
    /// Ändert sich eine, müssen sie und alle folgenden neu (jede startet mit dem letzten Bild der vorherigen).
    /// </summary>
    public void ApplyText(string text)
    {
        var parsed = ParseText(text);
        var firstChange = Enumerable.Range(0, Math.Max(parsed.Count, Scenes.Count))
            .FirstOrDefault(i => i >= parsed.Count || i >= Scenes.Count
                                 || parsed[i].Prompt != Scenes[i].Prompt || Math.Abs(parsed[i].Seconds - Scenes[i].Seconds) > 0.01, -1);
        if (firstChange < 0)
        {
            return;
        }

        var kept = Scenes.Take(Math.Min(firstChange, parsed.Count)).ToList();
        kept.AddRange(parsed.Skip(kept.Count).Select(p => new Scene { Prompt = p.Prompt, Seconds = p.Seconds }));
        Scenes = kept;
        FinalVideo = null;
    }

    /// <summary>"Ab hier neu": diese Szene und alle folgenden wieder offen.</summary>
    public void ResetFrom(int index)
    {
        foreach (var scene in Scenes.Skip(Math.Max(0, index)))
        {
            scene.Status = SceneStatus.Open;
            scene.Error = null;
        }

        FinalVideo = null;
    }

    public string ToText() => string.Join(Environment.NewLine, Scenes.Select(s =>
        Math.Abs(s.Seconds - DefaultSeconds) < 0.01 ? s.Prompt : string.Create(CultureInfo.InvariantCulture, $"{s.Seconds:0.#} | {s.Prompt}")));

    /// <summary>Eine Szene pro Zeile. "5 | Prompt" = 5 Sekunden, sonst 4. Leere Zeilen und Zeilen mit # fallen weg.</summary>
    public static IReadOnlyList<(double Seconds, string Prompt)> ParseText(string text)
    {
        var scenes = new List<(double, string)>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var bar = line.IndexOf('|', StringComparison.Ordinal);
            var seconds = DefaultSeconds;
            if (bar > 0 && double.TryParse(line[..bar].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var given))
            {
                seconds = Math.Clamp(given, MinSeconds, MaxSeconds);
                line = line[(bar + 1)..].Trim();
            }

            if (line.Length > 0)
            {
                scenes.Add((seconds, line));
            }
        }

        return scenes;
    }
}

/// <summary>Speichert Szenen-Projekte in data/videos/projekte/&lt;name&gt;/projekt.json.</summary>
public sealed class SceneProjectStore(string directory)
{
    public const string FileName = "projekt.json";

    public string Directory => directory;

    public string Folder(string name) => Path.Combine(directory, name);

    public IReadOnlyList<string> Names() => System.IO.Directory.Exists(directory)
        ? [.. System.IO.Directory.EnumerateDirectories(directory).Where(d => File.Exists(Path.Combine(d, FileName))).Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.CurrentCultureIgnoreCase)]
        : [];

    public SceneProject? Load(string name)
    {
        var file = Path.Combine(Folder(name), FileName);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SceneProject>(File.ReadAllText(file), JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(SceneProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        System.IO.Directory.CreateDirectory(Folder(project.Name));
        var file = Path.Combine(Folder(project.Name), FileName);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(project, JsonDefaults.Options));
        File.Move(file + ".tmp", file, overwrite: true); // nie eine halbe Datei, auch wenn der Laptop zugeklappt wird
    }

    /// <summary>Gültiger Ordnername aus dem eingegebenen Namen, z.B. "Hund am Teich" → "hund-am-teich".</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
