using System.Globalization;
using System.Text.Json;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines;

/// <summary>Empfohlene Werte des Modell-Herstellers für "Auf Standard zurücksetzen". Null = keine Empfehlung.</summary>
public sealed record SamplingDefaults(
    double? Temperature, double? TopP, int? TopK, double? MinP, double? RepeatPenalty, double? PresencePenalty, string Source)
{
    public static SamplingDefaults None { get; } = new(null, null, null, null, null, null,
        "Keine Empfehlung gefunden. Leere Felder nehmen den Standard der Engine.");

    /// <summary>Werte aus <paramref name="other"/> füllen, wo hier nichts steht.</summary>
    public SamplingDefaults Merge(SamplingDefaults other, string source) => new(
        Temperature ?? other.Temperature, TopP ?? other.TopP, TopK ?? other.TopK,
        MinP ?? other.MinP, RepeatPenalty ?? other.RepeatPenalty, PresencePenalty ?? other.PresencePenalty, source);

    public bool IsEmpty => Temperature is null && TopP is null && TopK is null && MinP is null && RepeatPenalty is null && PresencePenalty is null;
}

/// <summary>Was die Feineinstellungen über das Grundmodell einer KI wissen müssen.</summary>
/// <param name="ModelBytes">Grösse des Sprachmodells.</param>
/// <param name="ExtraBytes">Weitere Dateien, die immer mitgeladen werden (Projektor für Bilder).</param>
/// <param name="DraftBytes">Grösse des Draft-Modells. 0 = es gibt keins.</param>
/// <param name="DraftPath">Draft-Modell relativ zum Projektordner, wie in kis/*.json. Nur llama.cpp.</param>
/// <param name="MaxContext">Wie viel Kontext das Modell im Training gesehen hat (aus der GGUF-Datei).</param>
/// <param name="BlockCount">Anzahl Schichten (Layer). Obergrenze für "GPU-Layer".</param>
public sealed record ModelFacts(
    long ModelBytes, long ExtraBytes, long DraftBytes, string? DraftPath, int? MaxContext, int? BlockCount, bool IsMoe, SamplingDefaults Defaults)
{
    public static ModelFacts Unknown { get; } = new(0, 0, 0, null, null, null, false, SamplingDefaults.None);

    public bool HasDraft => DraftPath is not null && DraftBytes > 0;
}

/// <summary>
/// Liest die Fakten zum Grundmodell einer KI (Bauplan 8.5). Die empfohlenen Werte kommen zuerst aus dem Kopf
/// der GGUF-Datei (general.sampling.*), dann aus den Parametern von Ollama, falls das Modell von dort kommt.
/// Dateien von Ollama werden dabei nur gelesen.
/// </summary>
public sealed class ModelFactsProvider(AppPaths paths, ModelLibrary library, OllamaModelStore ollama, ILogger<ModelFactsProvider> logger)
{
    public ModelFacts Describe(KiProfile ki)
    {
        try
        {
            return ki.Engine == EngineKind.Ollama ? DescribeOllama(ki) : DescribeLlamaCpp(ki);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or EndOfStreamException)
        {
            logger.LogWarning(ex, "Angaben zum Modell von {Id} nicht lesbar.", ki.Id);
            return ModelFacts.Unknown;
        }
    }

    /// <summary>Werte aus dem JSON der Ollama-Parameter (Blob "params"), z.B. {"temperature":1,"top_k":20}.</summary>
    public static SamplingDefaults? ParseOllamaParams(string json, string source)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var defaults = new SamplingDefaults(
            Number(root, "temperature"), Number(root, "top_p"), (int?)Number(root, "top_k"),
            Number(root, "min_p"), Number(root, "repeat_penalty"), Number(root, "presence_penalty"), source);
        return defaults.IsEmpty ? null : defaults;
    }

    /// <summary>Werte aus der GGUF-Datei, ergänzt mit denen von Ollama (falls vorhanden).</summary>
    public static SamplingDefaults Combine(GgufMetadata gguf, SamplingDefaults? fromOllama)
    {
        var fromFile = new SamplingDefaults(gguf.Temperature, gguf.TopP, gguf.TopK, gguf.MinP, gguf.RepeatPenalty, null,
            "Empfehlung des Herstellers aus der GGUF-Datei");
        return (fromFile.IsEmpty, fromOllama) switch
        {
            (false, null) => fromFile,
            (false, { } other) => fromFile.Merge(other, "Empfehlung des Herstellers aus der GGUF-Datei, ergänzt mit den Ollama-Parametern"),
            (true, { } other) => other,
            _ => SamplingDefaults.None,
        };
    }

    private ModelFacts DescribeLlamaCpp(KiProfile ki)
    {
        if (ki.BaseModel.Path is not { Length: > 0 } relative || paths.Resolve(relative) is not { } file || !File.Exists(file))
        {
            return ModelFacts.Unknown;
        }

        var gguf = GgufReader.Read(file);
        var folder = Path.GetDirectoryName(file);
        var manifest = library.Scan().FirstOrDefault(m => string.Equals(m.FolderPath, folder, StringComparison.OrdinalIgnoreCase))?.Manifest;

        // Aus Ollama übernommen (Hardlink)? Dann stehen weitere Empfehlungen in Ollamas Parametern.
        var fromOllama = manifest?.Source is { } source && source.StartsWith("ollama:", StringComparison.Ordinal)
            ? FromOllama(source["ollama:".Length..])
            : null;

        string? draftPath = null;
        long draftBytes = 0;
        if (manifest?.DraftFile is { Length: > 0 } draftFile && folder is not null && File.Exists(Path.Combine(folder, draftFile)))
        {
            draftBytes = new FileInfo(Path.Combine(folder, draftFile)).Length;
            draftPath = SiblingPath(relative, draftFile);
        }

        return new ModelFacts(
            new FileInfo(file).Length, Length(ki.BaseModel.MmprojPath), draftBytes, draftPath,
            gguf.ContextLength, gguf.BlockCount, gguf.IsMoe || manifest?.IsMoe == true, Combine(gguf, fromOllama));
    }

    private ModelFacts DescribeOllama(KiProfile ki)
    {
        if (string.IsNullOrWhiteSpace(ki.BaseModel.OllamaName) || ollama.Find(ki.BaseModel.OllamaName) is not { Model: { } layer } model)
        {
            return ModelFacts.Unknown;
        }

        var blob = ollama.BlobPath(layer);
        var gguf = File.Exists(blob) ? GgufReader.Read(blob) : null;
        var defaults = FromOllama(ki.BaseModel.OllamaName) ?? (gguf is null ? SamplingDefaults.None : Combine(gguf, null));
        return new ModelFacts(layer.Size, model.Projector?.Size ?? 0, 0, null, gguf?.ContextLength, gguf?.BlockCount, gguf?.IsMoe == true, defaults);
    }

    private SamplingDefaults? FromOllama(string name)
    {
        if (ollama.Find(name) is not { Params: { } layer })
        {
            return null;
        }

        var blob = ollama.BlobPath(layer);
        return File.Exists(blob) ? ParseOllamaParams(File.ReadAllText(blob), $"Empfehlung aus den Ollama-Parametern von {name}") : null;
    }

    private long Length(string? relative) =>
        relative is { Length: > 0 } && paths.Resolve(relative) is { } full && File.Exists(full) ? new FileInfo(full).Length : 0;

    /// <summary>"models/gemma4-26b/gemma4-26b.gguf" + "draft.gguf" → "models/gemma4-26b/draft.gguf".</summary>
    public static string SiblingPath(string relativeModelPath, string fileName)
    {
        var slash = relativeModelPath.Replace('\\', '/').LastIndexOf('/');
        return slash < 0 ? fileName : relativeModelPath.Replace('\\', '/')[..(slash + 1)] + fileName;
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? Math.Round(value.GetDouble(), 4)
            : null;
}

/// <summary>Zahlen für Eingabefelder: "0.95" (immer mit Punkt), leer = null.</summary>
public static class ParameterText
{
    public static string Format(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

    public static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>Leer = null (Standard). Komma geht auch ("0,7"). Ungültig = false.</summary>
    public static bool TryParse(string? text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            value = number;
            return true;
        }

        return false;
    }

    public static bool TryParse(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
            return true;
        }

        return false;
    }
}
