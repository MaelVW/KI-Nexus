using System.Text.Json;
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

    /// <summary>z.B. "ollama:gemma4:26b" oder später "huggingface:…".</summary>
    public string Source { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string? Quantization { get; set; }
    public string? License { get; set; }
    public bool IsMoe { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>Ein Grundmodell im Ordner models/.</summary>
public sealed record LocalModel(string FolderName, string FolderPath, ModelManifest Manifest)
{
    public string ModelPath => Path.Combine(FolderPath, Manifest.ModelFile);

    public string? MmprojPath => Manifest.MmprojFile is null ? null : Path.Combine(FolderPath, Manifest.MmprojFile);
}

/// <summary>Kennt die Grundmodelle im Ordner models/. Jeder Unterordner mit model.json ist ein Modell.</summary>
public sealed class ModelLibrary(string modelsDirectory, ILogger<ModelLibrary> logger)
{
    public const string ManifestFileName = "model.json";

    public string Directory => modelsDirectory;

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

        return models;
    }

    public static void WriteManifest(string folderPath, ModelManifest manifest) =>
        File.WriteAllText(Path.Combine(folderPath, ManifestFileName), JsonSerializer.Serialize(manifest, JsonDefaults.Options));
}
