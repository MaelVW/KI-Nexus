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

    /// <summary>Unterordner für private Bilder (data/images/privat).</summary>
    public string PrivateDirectory => Path.Combine(directory, VideoLibrary.PrivateFolderName);

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

    /// <summary>Alle Bilder (auch die privaten), das neueste zuerst. Einträge ohne PNG fallen weg.</summary>
    public async Task<IReadOnlyList<VideoEntry>> LoadAllAsync(CancellationToken ct)
    {
        var entries = new List<VideoEntry>();
        foreach (var (folder, isPrivate) in new[] { (directory, false), (PrivateDirectory, true) })
        {
            if (!System.IO.Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.json"))
            {
                if (await LoadOneAsync(folder, Path.GetFileNameWithoutExtension(file), isPrivate, ct).ConfigureAwait(false) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        return [.. entries.OrderByDescending(e => e.CreatedAt)];
    }

    /// <summary>In den Privatordner oder zurück. PNG, JSON, Referenzen und Vorlage werden verschoben.</summary>
    public async Task<VideoEntry> SetPrivateAsync(VideoEntry entry, bool isPrivate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsPrivate == isPrivate)
        {
            return entry;
        }

        var target = isPrivate ? PrivateDirectory : directory;
        GalleryFiles.Move(FilesOf(entry), target);
        logger.LogInformation("Bild {Id} {Where}.", entry.Id, isPrivate ? "in den Privatordner verschoben" : "aus dem Privatordner geholt");
        return await LoadOneAsync(target, entry.Id, isPrivate, ct).ConfigureAwait(false)
            ?? throw new IOException($"Bild {entry.Id} ist nach dem Verschieben nicht lesbar.");
    }

    private async Task<VideoEntry?> LoadOneAsync(string folder, string id, bool isPrivate, CancellationToken ct)
    {
        var file = Path.Combine(folder, id + ".json");
        try
        {
            await using var stream = File.OpenRead(file);
            var entry = await JsonSerializer.DeserializeAsync<VideoEntry>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
            var image = Path.Combine(folder, id + ".png");
            if (entry is null || !File.Exists(image))
            {
                return null;
            }

            return entry with
            {
                Id = id,
                Kind = VideoEntry.ImageKind,
                IsPrivate = isPrivate,
                VideoPath = image,
                ThumbnailPath = image,
                ReferenceImagePaths = [.. entry.ReferenceImages.Select(name => Path.Combine(folder, name)).Where(File.Exists)],
                SourceImagePath = entry.SourceImage is { } source && File.Exists(Path.Combine(folder, source)) ? Path.Combine(folder, source) : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Bild-Eintrag {File} ist nicht lesbar.", file);
            return null;
        }
    }

    /// <summary>PNG, JSON, Referenzbilder und Vorlage eines Bilds, z.B. zum Löschen oder Verschieben.</summary>
    public IReadOnlyList<string> FilesOf(VideoEntry entry)
    {
        var folder = entry.IsPrivate ? PrivateDirectory : directory;
        return
        [
            .. new[] { ".png", ".json" }.Select(ext => Path.Combine(folder, entry.Id + ext)).Where(File.Exists),
            .. entry.ReferenceImagePaths.Where(File.Exists),
            .. entry.SourceImagePath is { } source && File.Exists(source) ? [source] : Array.Empty<string>(),
        ];
    }

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
