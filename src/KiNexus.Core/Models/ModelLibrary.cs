using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Models;

/// <summary>
/// Inhalt von models/&lt;ordner&gt;/model.json: woher das Modell kommt und was drin ist.
/// Dateinamen sind relativ zum Modell-Ordner.
/// </summary>
public sealed class ModelManifest
{
    public string Name { get; set; } = "";
    public string ModelFile { get; set; } = "";
    public string? MmprojFile { get; set; }
    public string? DraftFile { get; set; }

    /// <summary>z.B. "ollama:gemma4:26b", "huggingface:Qwen/Qwen3-0.6B-GGUF" oder "datei:C:\…".</summary>
    public string Source { get; set; } = "";
    public string Architecture { get; set; } = "";

    /// <summary>Version aus dem GGUF-Kopf (Schlüssel "model_version"), z.B. "2.3.0" bei LTX-2.3. Nur bei Videomodellen.</summary>
    public string? ModelVersion { get; set; }

    public string? Quantization { get; set; }
    public string? License { get; set; }
    public bool IsMoe { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Empfohlene Werte des Herstellers aus dem GGUF-Kopf (für "Standard" im Assistenten).</summary>
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>Ein Grundmodell im Ordner models/.</summary>
public sealed record LocalModel(string FolderName, string FolderPath, ModelManifest Manifest)
{
    public string ModelPath => Path.Combine(FolderPath, Manifest.ModelFile);

    public string? MmprojPath => Manifest.MmprojFile is null ? null : Path.Combine(FolderPath, Manifest.MmprojFile);

    [JsonIgnore]
    public bool SupportsImages => Manifest.MmprojFile is not null;

    /// <summary>Kann man damit chatten? Bild- und Video-Modelle (z.B. Architektur "ltxv") nicht.</summary>
    [JsonIgnore]
    public bool IsLanguageModel => ModelKinds.IsTextArchitecture(Manifest.Architecture);

    /// <summary>Pfad relativ zum Projektordner, so wie er in kis/*.json steht: "models/&lt;ordner&gt;/&lt;datei&gt;".</summary>
    public string RelativeModelPath => $"models/{FolderName}/{Manifest.ModelFile}";

    public string? RelativeMmprojPath => Manifest.MmprojFile is null ? null : $"models/{FolderName}/{Manifest.MmprojFile}";
}

/// <summary>Kennt die Grundmodelle im Ordner models/. Jeder Unterordner mit model.json ist ein Modell.</summary>
public sealed class ModelLibrary(string modelsDirectory, ILogger<ModelLibrary> logger)
{
    public const string ManifestFileName = "model.json";

    /// <summary>Hier liegen angefangene Downloads. Der Punkt am Anfang: Das ist kein Modell.</summary>
    public const string DownloadsFolderName = ".downloads";

    public string Directory => modelsDirectory;

    public string DownloadsDirectory => Path.Combine(modelsDirectory, DownloadsFolderName);

    public IReadOnlyList<LocalModel> Scan()
    {
        if (!System.IO.Directory.Exists(modelsDirectory))
        {
            return [];
        }

        var models = new List<LocalModel>();
        foreach (var folder in System.IO.Directory.EnumerateDirectories(modelsDirectory))
        {
            var manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(manifestPath), JsonDefaults.Options);
                if (manifest is not null)
                {
                    models.Add(new LocalModel(Path.GetFileName(folder), folder, manifest));
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "model.json ist beschädigt: {Path}", manifestPath);
            }
        }

        return models.OrderBy(m => m.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Legt models/&lt;name&gt;/ an. Gibt es den Namen schon, kommt "-2", "-3" usw. dazu.</summary>
    public string CreateFolder(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "modell" : baseName;
        var folder = Path.Combine(modelsDirectory, name);
        for (var n = 2; System.IO.Directory.Exists(folder); n++)
        {
            folder = Path.Combine(modelsDirectory, $"{name}-{n}");
        }

        System.IO.Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Liest den GGUF-Kopf und schreibt model.json. Gibt das fertige Modell zurück.</summary>
    public LocalModel WriteManifestFor(string folder, string modelFile, string? mmprojFile, string source, string? license)
    {
        var modelPath = Path.Combine(folder, modelFile);
        var metadata = GgufReader.Read(modelPath);
        var size = System.IO.Directory.EnumerateFiles(folder, "*.gguf").Sum(f => new FileInfo(f).Length);
        var version = metadata.IsLanguageModel ? null : GgufReader.ReadKeyValues(modelPath).GetValueOrDefault("model_version") as string;
        var manifest = new ModelManifest
        {
            Name = metadata.Name is { Length: > 0 } name && name != "Source" ? name : Path.GetFileNameWithoutExtension(modelFile),
            ModelFile = modelFile,
            MmprojFile = mmprojFile,
            Source = source,
            Architecture = metadata.Architecture,
            ModelVersion = version,
            Quantization = metadata.Quantization,
            License = license ?? metadata.License,
            IsMoe = metadata.IsMoe,
            SizeBytes = size,
            Temperature = metadata.Temperature,
            TopP = metadata.TopP,
            TopK = metadata.TopK,
            ImportedAt = DateTimeOffset.Now,
        };
        WriteManifest(folder, manifest);
        return new LocalModel(Path.GetFileName(folder), folder, manifest);
    }

    /// <summary>
    /// Löscht den Modell-Ordner. Bei Hardlinks auf Ollama verschwindet nur der zweite Name,
    /// die Datei von Ollama bleibt.
    /// </summary>
    public void Delete(LocalModel model)
    {
        var full = Path.GetFullPath(model.FolderPath);
        if (!full.StartsWith(Path.GetFullPath(modelsDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Es dürfen nur Ordner innerhalb von models/ gelöscht werden.");
        }

        System.IO.Directory.Delete(full, recursive: true);
        logger.LogInformation("Grundmodell {Folder} gelöscht.", model.FolderName);
    }

    public static void WriteManifest(string folderPath, ModelManifest manifest) =>
        File.WriteAllText(Path.Combine(folderPath, ManifestFileName), JsonSerializer.Serialize(manifest, JsonDefaults.Options));
}
