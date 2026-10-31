using System.Text;
using KiNexus.Core.IO;
using KiNexus.Core.Models;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.Ollama;

/// <summary>
/// Übernimmt ein Ollama-Modell nach models\ – per Hardlink, also ohne doppelten Speicherplatz.
/// Der Hardlink bleibt gültig, auch wenn Ollama später deinstalliert wird.
/// Ollama-Dateien werden dabei nie verändert.
/// </summary>
public sealed class OllamaImporter(OllamaModelStore store, ModelLibrary library, ILogger<OllamaImporter> logger)
{
    /// <summary>Ordnername für ein Ollama-Modell: "gemma4:26b" → "gemma4-26b", "mael-ki:latest" → "mael-ki".</summary>
    public static string FolderNameFor(string ollamaName)
    {
        var name = ollamaName.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) ? ollamaName[..^":latest".Length] : ollamaName;
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            builder.Append(c is ':' or '/' or '\\' || Path.GetInvalidFileNameChars().Contains(c) ? '-' : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Legt models\&lt;ordner&gt;\ mit Hardlinks und model.json an.
    /// Gibt es die Hardlinks schon (gleiche Datei), passiert nichts Neues.
    /// </summary>
    public LocalModel Import(OllamaInstalledModel model)
    {
        var modelLayer = model.Model ?? throw new InvalidOperationException($"{model.Name} enthält keine Modell-Datei.");
        var folderName = FolderNameFor(model.Name);
        var folder = Path.Combine(library.Directory, folderName);
        Directory.CreateDirectory(folder);

        var modelFile = $"{folderName}.gguf";
        LinkLayer(modelLayer, Path.Combine(folder, modelFile));
        var mmprojFile = LinkOptional(model.Projector, folder, "mmproj.gguf");
        var draftFile = LinkOptional(model.Draft, folder, "draft.gguf");

        var metadata = GgufReader.Read(Path.Combine(folder, modelFile));
        var manifest = new ModelManifest
        {
            Name = model.Name,
            ModelFile = modelFile,
            MmprojFile = mmprojFile,
            DraftFile = draftFile,
            Source = $"ollama:{model.Name}",
            Architecture = metadata.Architecture,
            Quantization = metadata.Quantization,
            License = metadata.License,
            IsMoe = metadata.IsMoe,
            SizeBytes = modelLayer.Size + (model.Projector?.Size ?? 0),
            Temperature = metadata.Temperature,
            TopP = metadata.TopP,
            TopK = metadata.TopK,
            ImportedAt = DateTimeOffset.Now,
        };
        ModelLibrary.WriteManifest(folder, manifest);

        logger.LogInformation("Ollama-Modell {Name} übernommen nach {Folder} ({Arch}, {Quant}).", model.Name, folder, metadata.Architecture, metadata.Quantization);
        return new LocalModel(folderName, folder, manifest);
    }

    private string? LinkOptional(OllamaLayer? layer, string folder, string fileName)
    {
        if (layer is null)
        {
            return null;
        }

        LinkLayer(layer, Path.Combine(folder, fileName));
        return fileName;
    }

    private void LinkLayer(OllamaLayer layer, string linkPath)
    {
        var blob = store.BlobPath(layer);
        if (!File.Exists(blob))
        {
            throw new FileNotFoundException($"Die Ollama-Datei fehlt: {blob}");
        }

        if (File.Exists(linkPath))
        {
            if (FileLinks.AreSameFile(linkPath, blob))
            {
                return;
            }

            throw new IOException($"{linkPath} gibt es schon, aber es ist eine andere Datei. Bitte zuerst löschen.");
        }

        FileLinks.CreateHardLink(linkPath, blob);
    }
}
