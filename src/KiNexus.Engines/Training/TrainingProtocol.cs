using System.Text.Json;
using System.Text.Json.Nodes;
using KiNexus.Core.Paths;
using KiNexus.Core.Training;

namespace KiNexus.Engines.Training;

/// <summary>Ein Ereignis vom Trainings-Worker (eine Zeile auf seinem stdout).</summary>
public abstract record TrainingEvent;

/// <summary>Ein neuer Abschnitt: download, load, train, save, convert_base, convert_adapter.</summary>
public sealed record TrainingStage(string Stage, string Text, int? TotalSteps) : TrainingEvent;

public sealed record TrainingDownload(long Done, long Total) : TrainingEvent;

/// <summary>Ein Lernschritt ist fertig. Loss = wie falsch das Modell noch liegt (kleiner ist besser).</summary>
public sealed record TrainingProgress(int Step, int TotalSteps, int Epoch, int Epochs, double Loss, double ElapsedSeconds, double EtaSeconds, double VramGb)
    : TrainingEvent;

public sealed record TrainingDone(string AdapterGguf, string BaseGguf, double? FinalLoss, double Seconds) : TrainingEvent;

public sealed record TrainingFailed(string Message) : TrainingEvent;

public sealed record TrainingCancelled : TrainingEvent;

/// <summary>Ergebnis von "train.py --check": Findet PyTorch die Grafikkarte?</summary>
public sealed record TrainingCheck(bool Ok, string? Message, string? Torch, string? Gpu, double? VramGb) : TrainingEvent;

/// <summary>Der Prozess ist beendet.</summary>
public sealed record TrainingExited(int ExitCode) : TrainingEvent;

public sealed record TrainingUnknown(string Type) : TrainingEvent;

/// <summary>Wo die Dateien eines Trainings liegen.</summary>
/// <param name="BaseModelDir">Originalmodell (safetensors), z.B. models/hf/qwen3-1.7b.</param>
/// <param name="BaseGguf">Grundmodell als GGUF für llama.cpp, z.B. models/qwen3-1.7b/qwen3-1.7b-q8_0.gguf.</param>
/// <param name="AdapterGguf">Der trainierte Adapter, z.B. models/qwen3-1.7b/lora/mein-stil.gguf.</param>
/// <param name="RunDir">Arbeitsordner: Einstellungen, Kopie des Datensatzes, Adapter als safetensors.</param>
public sealed record TrainingPaths(string BaseModelDir, string BaseGguf, string AdapterGguf, string RunDir)
{
    public string ConfigFile => Path.Combine(RunDir, "run.json");

    public string DatasetFile => Path.Combine(RunDir, "dataset.jsonl");

    /// <summary>Legt die App diese Datei an, hört der Worker nach dem laufenden Schritt auf.</summary>
    public string StopFile => Path.Combine(RunDir, "stop");

    public static TrainingPaths For(AppPaths paths, string modelsDirectory, TrainingBaseModel model, string runName) => new(
        Path.Combine(modelsDirectory, "hf", model.Slug),
        Path.Combine(modelsDirectory, model.Slug, $"{model.Slug}-q8_0.gguf"),
        Path.Combine(modelsDirectory, model.Slug, "lora", runName + ".gguf"),
        Path.Combine(paths.DataDirectory, "training", "runs", runName));

    /// <summary>
    /// Eine Version (T1): Arbeitsordner runs/&lt;name&gt;/v&lt;n&gt; (bleibt erhalten), Adapter models/&lt;modell&gt;/lora/&lt;name&gt;-v&lt;n&gt;.gguf.
    /// </summary>
    public static TrainingPaths ForVersion(string modelsDirectory, TrainingBaseModel model, string runName, int version, string runDir) => new(
        Path.Combine(modelsDirectory, "hf", model.Slug),
        Path.Combine(modelsDirectory, model.Slug, $"{model.Slug}-q8_0.gguf"),
        Path.Combine(modelsDirectory, model.Slug, "lora", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{runName}-v{version}.gguf")),
        runDir);
}

/// <summary>
/// Übersetzt zwischen der App und dem Trainings-Worker (python/training/train.py). Das Protokoll steht in docs/06-Training.md.
/// </summary>
public static class TrainingProtocol
{
    /// <param name="resumeAdapterDir">Weitertrainieren: Ordner des bisherigen Adapters (safetensors). Null = neu beginnen.</param>
    public static JsonObject BuildConfig(TrainingBaseModel model, TrainingOptions options, TrainingPaths files, string convertScriptsDir, bool download,
        string? resumeAdapterDir = null) => new()
    {
        ["resumeAdapterDir"] = resumeAdapterDir,
        ["baseRepo"] = download ? model.Repo : null,
        ["baseModelDir"] = files.BaseModelDir,
        ["datasetPath"] = files.DatasetFile,
        ["outputDir"] = files.RunDir,
        ["epochs"] = options.Epochs,
        ["learningRate"] = options.LearningRate,
        ["loraRank"] = options.LoraRank,
        ["maxLength"] = options.MaxLength,
        ["gradientAccumulation"] = 4,
        ["use4Bit"] = model.Use4Bit,
        ["convertScriptsDir"] = convertScriptsDir,
        ["baseGgufPath"] = files.BaseGguf,
        ["adapterGgufPath"] = files.AdapterGguf,
        ["stopFile"] = files.StopFile,
    };

    public static TrainingEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = Text(root, "type") ?? "";
        return type switch
        {
            "stage" => new TrainingStage(Text(root, "stage") ?? "", Text(root, "text") ?? "", Number(root, "totalSteps") is { } total ? (int)total : null),
            "download" => new TrainingDownload((long)(Number(root, "done") ?? 0), (long)(Number(root, "total") ?? 0)),
            "progress" => new TrainingProgress((int)(Number(root, "step") ?? 0), (int)(Number(root, "totalSteps") ?? 0), (int)(Number(root, "epoch") ?? 0),
                (int)(Number(root, "epochs") ?? 0), Number(root, "loss") ?? 0, Number(root, "elapsedSeconds") ?? 0, Number(root, "etaSeconds") ?? 0,
                Number(root, "vramGb") ?? 0),
            "done" => new TrainingDone(Text(root, "adapterGguf") ?? "", Text(root, "baseGguf") ?? "", Number(root, "finalLoss"), Number(root, "seconds") ?? 0),
            "error" => new TrainingFailed(Text(root, "message") ?? "Unbekannter Fehler"),
            "cancelled" => new TrainingCancelled(),
            "check" => new TrainingCheck(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True, Text(root, "message"),
                Text(root, "torch"), Text(root, "gpu"), Number(root, "vramGb")),
            _ => new TrainingUnknown(type),
        };
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
