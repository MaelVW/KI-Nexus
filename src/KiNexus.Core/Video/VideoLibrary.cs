using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Video;

/// <summary>
/// Ein fertiges Video oder Bild mit allem, was man zum Nachbauen braucht. Liegt als JSON neben der MP4 oder PNG.
/// Bilder (Bauplan Bild) nutzen denselben Eintrag, damit die Galerie beides gemeinsam zeigt: <see cref="Kind"/> = "bild".
/// </summary>
public sealed record VideoEntry
{
    public const string VideoKind = "video";
    public const string ImageKind = "bild";

    /// <summary>Dateiname ohne Endung, z.B. "2026-11-02_144301_seed11".</summary>
    public required string Id { get; init; }

    /// <summary>"video" oder "bild". Ältere Einträge haben nichts, das heisst Video.</summary>
    public string Kind { get; init; } = VideoKind;

    [JsonIgnore] public bool IsImage => Kind == ImageKind;

    /// <summary>Bild: das wievielte aus demselben Auftrag (ab 0) und wie viele es waren. Gleicher Seed braucht dieselbe Anzahl.</summary>
    public int BatchIndex { get; init; }

    public int BatchCount { get; init; } = 1;

    public required string Prompt { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Frames { get; init; }
    public double FrameRate { get; init; }
    public long Seed { get; init; }
    public bool WithAudio { get; init; }

    /// <summary>Bild zu Video: Dateiname der Kopie des Startbilds in data/videos, z.B. "…_start.png". Null = Text zu Video.</summary>
    public string? StartImage { get; init; }

    [JsonIgnore] public string? StartImagePath { get; init; }

    /// <summary>Name des Videomodells, z.B. "LTX-2.3 distilled (Q4_K_M)".</summary>
    public string Model { get; init; } = "";

    /// <summary>So lange hat das Erzeugen gedauert (Sekunden).</summary>
    public double GenerationSeconds { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Die MP4, bei einem Bild die PNG.</summary>
    [JsonIgnore] public string VideoPath { get; init; } = "";

    [JsonIgnore] public string? ThumbnailPath { get; init; }

    /// <summary>Länge des Videos in Sekunden.</summary>
    [JsonIgnore] public double Seconds => FrameRate > 0 ? Frames / FrameRate : 0;

    /// <summary>Derselbe Auftrag nochmals, z.B. für "Nochmal mit gleichem Seed".</summary>
    public VideoRequest ToRequest() => new(Prompt, Width, Height, Frames, FrameRate, Seed, WithAudio, StartImagePath: StartImagePath);
}

/// <summary>
/// Die fertigen Videos in data/videos: pro Video eine MP4, ein Vorschaubild (PNG) und eine JSON-Datei.
/// ComfyUI speichert zuerst in data/videos/roh. Die Bibliothek holt die Dateien von dort und gibt ihnen einen festen Namen.
/// </summary>
public sealed partial class VideoLibrary(string directory, ILogger<VideoLibrary> logger)
{
    public string Directory => directory;

    /// <summary>Nimmt ein fertiges Video auf. Die Dateien werden aus dem Rohordner verschoben.</summary>
    public async Task<VideoEntry> AddAsync(VideoDone done, VideoRequest request, VideoModelSet models, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(request);
        System.IO.Directory.CreateDirectory(directory);

        var id = UniqueId(CleanName(Path.GetFileNameWithoutExtension(done.VideoPath)));
        var videoPath = Path.Combine(directory, id + ".mp4");
        File.Move(done.VideoPath, videoPath);
        string? thumbnailPath = null;
        if (done.ThumbnailPath is { } thumbnail && File.Exists(thumbnail))
        {
            thumbnailPath = Path.Combine(directory, id + ".png");
            File.Move(thumbnail, thumbnailPath);
        }

        // Bild zu Video: eine Kopie des Startbilds, damit "Nochmal" auch geht, wenn das Original weg ist.
        string? startImage = null;
        if (request.StartImagePath is { } source && File.Exists(source))
        {
            startImage = id + "_start" + Path.GetExtension(source).ToLowerInvariant();
            File.Copy(source, Path.Combine(directory, startImage), overwrite: true);
        }

        var entry = new VideoEntry
        {
            Id = id,
            Prompt = request.Prompt.Trim(),
            Width = request.Width,
            Height = request.Height,
            Frames = request.Frames,
            FrameRate = request.FrameRate,
            Seed = request.Seed,
            WithAudio = request.WithAudio,
            StartImage = startImage,
            StartImagePath = startImage is null ? null : Path.Combine(directory, startImage),
            Model = models.Name,
            GenerationSeconds = done.Seconds,
            CreatedAt = DateTimeOffset.Now,
            VideoPath = videoPath,
            ThumbnailPath = thumbnailPath,
        };
        await using (var stream = File.Create(Path.Combine(directory, id + ".json")))
        {
            await JsonSerializer.SerializeAsync(stream, entry, JsonDefaults.Options, ct).ConfigureAwait(false);
        }

        logger.LogInformation("Video {Id} gespeichert ({Width}×{Height}, {Frames} Bilder).", id, entry.Width, entry.Height, entry.Frames);
        return entry;
    }

    /// <summary>Alle Videos, das neueste zuerst. Einträge ohne MP4 fallen weg.</summary>
    public async Task<IReadOnlyList<VideoEntry>> LoadAllAsync(CancellationToken ct)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<VideoEntry>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var entry = await JsonSerializer.DeserializeAsync<VideoEntry>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
                var id = Path.GetFileNameWithoutExtension(file);
                var video = Path.Combine(directory, id + ".mp4");
                if (entry is null || !File.Exists(video))
                {
                    continue;
                }

                var thumbnail = Path.Combine(directory, id + ".png");
                var start = entry.StartImage is { } name ? Path.Combine(directory, name) : null;
                entries.Add(entry with
                {
                    Id = id,
                    VideoPath = video,
                    ThumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                    StartImagePath = start is not null && File.Exists(start) ? start : null,
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Video-Eintrag {File} ist nicht lesbar.", file);
            }
        }

        return [.. entries.OrderByDescending(e => e.CreatedAt)];
    }

    /// <summary>Alle Dateien eines Videos (MP4, Vorschaubild, JSON), z.B. zum Löschen.</summary>
    public IReadOnlyList<string> FilesOf(VideoEntry entry) =>
    [
        .. new[] { ".mp4", ".png", ".json" }.Select(ext => Path.Combine(directory, entry.Id + ext)).Where(File.Exists),
        .. entry.StartImagePath is { } start && File.Exists(start) ? [start] : Array.Empty<string>(),
    ];

    /// <summary>ComfyUI hängt "_00001_" an. Das braucht der Name nicht.</summary>
    internal static string CleanName(string name)
    {
        var clean = CounterSuffix().Replace(name, "");
        return clean.Length > 0 ? clean : "video";
    }

    private string UniqueId(string baseName)
    {
        var id = baseName;
        for (var i = 2; File.Exists(Path.Combine(directory, id + ".mp4")) || File.Exists(Path.Combine(directory, id + ".json")); i++)
        {
            id = $"{baseName}-{i}";
        }

        return id;
    }

    [GeneratedRegex(@"_\d{5}_$")]
    private static partial Regex CounterSuffix();
}
