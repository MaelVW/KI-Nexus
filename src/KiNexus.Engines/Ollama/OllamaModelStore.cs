using System.Text.Json;
using System.Text.Json.Serialization;

namespace KiNexus.Engines.Ollama;

/// <summary>Ein Teil eines Ollama-Modells (Modell, Projektor, Vorlage …) als Datei in blobs\.</summary>
public sealed record OllamaLayer(string MediaType, string Digest, long Size);

/// <summary>Ein Modell, wie Ollama es auf der Festplatte speichert (Manifest + Blobs).</summary>
public sealed record OllamaInstalledModel(string Name, IReadOnlyList<OllamaLayer> Layers)
{
    public const string ModelType = "application/vnd.ollama.image.model";
    public const string ProjectorType = "application/vnd.ollama.image.projector";
    public const string DraftType = "application/vnd.ollama.image.draft";
    public const string ParamsType = "application/vnd.ollama.image.params";

    public OllamaLayer? Model => Find(ModelType);
    public OllamaLayer? Projector => Find(ProjectorType);
    public OllamaLayer? Draft => Find(DraftType);

    /// <summary>Die Parameter aus dem Modelfile (JSON, z.B. temperature, top_k).</summary>
    public OllamaLayer? Params => Find(ParamsType);

    private OllamaLayer? Find(string mediaType) => Layers.FirstOrDefault(l => l.MediaType == mediaType);
}

/// <summary>
/// Liest Ollamas Modell-Ordner (Manifeste und Blobs). Nur lesen! Die App ändert dort nie etwas.
/// Aufbau: models\manifests\registry.ollama.ai\library\&lt;name&gt;\&lt;tag&gt; und models\blobs\sha256-…
/// </summary>
public sealed class OllamaModelStore(string modelsDirectory)
{
    private const string DefaultRegistryPrefix = "registry.ollama.ai/library/";

    public string ModelsDirectory => modelsDirectory;

    /// <summary>Ollama nutzt %OLLAMA_MODELS% oder sonst %USERPROFILE%\.ollama\models.</summary>
    public static string DefaultDirectory() =>
        Environment.GetEnvironmentVariable("OLLAMA_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");

    public IReadOnlyList<OllamaInstalledModel> List()
    {
        var manifestsRoot = Path.Combine(modelsDirectory, "manifests");
        if (!Directory.Exists(manifestsRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(manifestsRoot, "*", SearchOption.AllDirectories)
            .Select(file => TryRead(manifestsRoot, file))
            .OfType<OllamaInstalledModel>()
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public OllamaInstalledModel? Find(string name) =>
        List().FirstOrDefault(m => string.Equals(m.Name, NormalizeName(name), StringComparison.OrdinalIgnoreCase));

    public string BlobPath(OllamaLayer layer) =>
        Path.Combine(modelsDirectory, "blobs", layer.Digest.Replace(':', '-'));

    /// <summary>"gemma4" wird zu "gemma4:latest", wie bei Ollama.</summary>
    public static string NormalizeName(string name) => name.Contains(':', StringComparison.Ordinal) ? name : name + ":latest";

    /// <summary>
    /// Aus dem Pfad des Manifests wird der Name, z.B. registry.ollama.ai\library\gemma4\26b → gemma4:26b.
    /// </summary>
    internal static string NameFromRelativePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/');
        var lastSlash = parts.LastIndexOf('/');
        var withTag = parts[..lastSlash] + ":" + parts[(lastSlash + 1)..];
        return withTag.StartsWith(DefaultRegistryPrefix, StringComparison.OrdinalIgnoreCase)
            ? withTag[DefaultRegistryPrefix.Length..]
            : withTag;
    }

    private static OllamaInstalledModel? TryRead(string manifestsRoot, string file)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ManifestJson>(File.ReadAllText(file));
            if (manifest?.Layers is null)
            {
                return null;
            }

            var layers = manifest.Layers.Select(l => new OllamaLayer(l.MediaType ?? "", l.Digest ?? "", l.Size)).ToList();
            return new OllamaInstalledModel(NameFromRelativePath(Path.GetRelativePath(manifestsRoot, file)), layers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ManifestJson
    {
        [JsonPropertyName("layers")]
        public List<LayerJson>? Layers { get; set; }
    }

    private sealed class LayerJson
    {
        [JsonPropertyName("mediaType")]
        public string? MediaType { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
