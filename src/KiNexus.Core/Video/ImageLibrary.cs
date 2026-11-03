using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Video;

/// <summary>
/// Die fertigen Bilder in data/images (Bauplan Bild, B2): pro Bild eine PNG und eine JSON-Datei.
/// Die Einträge sind <see cref="VideoEntry"/> mit <see cref="VideoEntry.Kind"/> = "bild". So zeigt die Galerie Bilder und Videos gemeinsam.
/// </summary>
public sealed class ImageLibrary(string directory, ILogger<ImageLibrary> logger)
{
    public string Directory => directory;

    /// <summary>Nimmt die Bilder eines Auftrags auf, eines pro Datei. Die Dateien werden aus dem Rohordner verschoben.</summary>
    public async Task<IReadOnlyList<VideoEntry>> AddAsync(ImageDone done, ImageRequest request, ImageModelSet models, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(models);
        System.IO.Directory.CreateDirectory(directory);

        var entries = new List<VideoEntry>();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < done.ImagePaths.Count; i++)
        {
            var source = done.ImagePaths[i];
            var id = UniqueId(VideoLibrary.CleanName(Path.GetFileNameWithoutExtension(source)) + (done.ImagePaths.Count > 1 ? $"_{i + 1}" : ""));
            var path = Path.Combine(directory, id + ".png");
            File.Move(source, path);

            // Kopien der Referenzbilder, damit "Nochmal" auch geht, wenn die Originale weg sind.
            var references = new List<string>();
            foreach (var (reference, number) in (request.ReferenceImages ?? []).Where(File.Exists).Select((r, n) => (r, n + 1)))
            {
                var name = $"{id}_ref{number}{Path.GetExtension(reference).ToLowerInvariant()}";
                File.Copy(reference, Path.Combine(directory, name), overwrite: true);
                references.Add(name);
            }

            string? sourceCopy = null;
            if (request.SourceImage is { } sourceImage && File.Exists(sourceImage))
            {
                sourceCopy = $"{id}_vorlage{Path.GetExtension(sourceImage).ToLowerInvariant()}";
                File.Copy(sourceImage, Path.Combine(directory, sourceCopy), overwrite: true);
            }

            var entry = new VideoEntry
            {
                Id = id,
                Kind = VideoEntry.ImageKind,
                Prompt = request.Prompt.Trim(),
                Width = request.Width,
                Height = request.Height,
                Seed = request.Seed,
                BatchIndex = i,
                BatchCount = done.ImagePaths.Count,
                ReferenceImages = references,
                ReferenceImagePaths = [.. references.Select(name => Path.Combine(directory, name))],
                SourceImage = sourceCopy,
                SourceImagePath = sourceCopy is null ? null : Path.Combine(directory, sourceCopy),
                Strength = request.Strength,
                Model = models.Name,
                GenerationSeconds = done.Seconds,
                // Gleiche Zeit plus eine Millisekunde pro Bild: So bleibt die Reihenfolge in der Galerie fest.
                CreatedAt = now.AddMilliseconds(-i),
                VideoPath = path,
                ThumbnailPath = path,
            };
            await using (var stream = File.Create(Path.Combine(directory, id + ".json")))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonDefaults.Options, ct).ConfigureAwait(false);
            }

            entries.Add(entry);
        }

        logger.LogInformation("{Count} Bilder gespeichert ({Width}×{Height}, Seed {Seed}).", entries.Count, request.Width, request.Height, request.Seed);
        return entries;
    }

    /// <summary>Alle Bilder, das neueste zuerst. Einträge ohne PNG fallen weg.</summary>
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
                var image = Path.Combine(directory, id + ".png");
                if (entry is null || !File.Exists(image))
                {
                    continue;
                }

                entries.Add(entry with
                {
                    Id = id,
                    Kind = VideoEntry.ImageKind,
                    VideoPath = image,
                    ThumbnailPath = image,
                    ReferenceImagePaths = [.. entry.ReferenceImages.Select(name => Path.Combine(directory, name)).Where(File.Exists)],
                    SourceImagePath = entry.SourceImage is { } source && File.Exists(Path.Combine(directory, source)) ? Path.Combine(directory, source) : null,
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Bild-Eintrag {File} ist nicht lesbar.", file);
            }
        }

        return [.. entries.OrderByDescending(e => e.CreatedAt)];
    }

    /// <summary>PNG, JSON und Referenzbilder eines Bilds, z.B. zum Löschen.</summary>
    public IReadOnlyList<string> FilesOf(VideoEntry entry) =>
    [
        .. new[] { ".png", ".json" }.Select(ext => Path.Combine(directory, entry.Id + ext)).Where(File.Exists),
        .. entry.ReferenceImagePaths.Where(File.Exists),
        .. entry.SourceImagePath is { } source && File.Exists(source) ? [source] : Array.Empty<string>(),
    ];

    private string UniqueId(string baseName)
    {
        var id = baseName;
        for (var i = 2; File.Exists(Path.Combine(directory, id + ".png")) || File.Exists(Path.Combine(directory, id + ".json")); i++)
        {
            id = $"{baseName}-{i}";
        }

        return id;
    }
}
