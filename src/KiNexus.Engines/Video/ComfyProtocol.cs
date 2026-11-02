using System.Text.Json;
using KiNexus.Core.Video;

namespace KiNexus.Engines.Video;

/// <summary>Eine Meldung von ComfyUI über den WebSocket.</summary>
public abstract record ComfyMessage(string? PromptId);

/// <summary>ComfyUI arbeitet jetzt an diesem Knoten.</summary>
public sealed record ComfyExecuting(string? PromptId, string Node) : ComfyMessage(PromptId);

/// <summary>Fortschritt innerhalb eines Knotens, z.B. Schritt 3 von 8 beim Entrauschen.</summary>
public sealed record ComfyProgress(string? PromptId, string? Node, int Value, int Max) : ComfyMessage(PromptId);

/// <summary>Ein Knoten ist fertig und hat Dateien geschrieben (z.B. SaveVideo).</summary>
public sealed record ComfyExecuted(string? PromptId, string Node, IReadOnlyList<ComfyFile> Files) : ComfyMessage(PromptId);

public sealed record ComfySuccess(string? PromptId) : ComfyMessage(PromptId);

public sealed record ComfyError(string? PromptId, string? NodeType, string Message) : ComfyMessage(PromptId);

/// <summary>Nach POST /interrupt: Der Auftrag wurde abgebrochen.</summary>
public sealed record ComfyInterrupted(string? PromptId) : ComfyMessage(PromptId);

/// <summary>Alles andere (status, progress_state, execution_start …). Braucht die App nicht.</summary>
public sealed record ComfyOther(string Type) : ComfyMessage((string?)null);

public sealed record ComfyFile(string Filename, string Subfolder, string Type);

/// <summary>
/// Übersetzt zwischen KI-Nexus und ComfyUI. Die Formate stammen aus echten Meldungen von ComfyUI 0.43 (docs/10-Video.md).
/// </summary>
public static class ComfyProtocol
{
    public static ComfyMessage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = Text(root, "type") ?? "";
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new ComfyOther(type);
        }

        var promptId = Text(data, "prompt_id");
        return type switch
        {
            // "executing" mit node = null heisst bei älteren Versionen "fertig". Fertig erkennen wir an execution_success.
            "executing" when Text(data, "node") is { } node => new ComfyExecuting(promptId, node),
            "progress" => new ComfyProgress(promptId, Text(data, "node"), Integer(data, "value"), Integer(data, "max")),
            "executed" when Text(data, "node") is { } node => new ComfyExecuted(promptId, node, Files(data)),
            "execution_success" => new ComfySuccess(promptId),
            "execution_error" => new ComfyError(promptId, Text(data, "node_type"),
                Text(data, "exception_message")?.Trim() is { Length: > 0 } message ? message : "Unbekannter Fehler"),
            "execution_interrupted" => new ComfyInterrupted(promptId),
            _ => new ComfyOther(type),
        };
    }

    /// <summary>
    /// Antwort auf POST /prompt. 200: die Auftragsnummer. 400: der Workflow ist ungültig (z.B. Datei oder Knoten fehlt).
    /// </summary>
    public static (string? PromptId, string? Error) ParseQueueResponse(int statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (statusCode is >= 200 and < 300 && Text(root, "prompt_id") is { } promptId)
            {
                return (promptId, null);
            }

            var message = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? Text(error, "message") ?? Text(error, "type")
                : null;
            var details = NodeErrors(root);
            return (null, $"ComfyUI hat den Auftrag abgelehnt: {message ?? "unbekannter Grund"}{(details is null ? "" : " " + details)}");
        }
        catch (JsonException)
        {
            return (null, $"ComfyUI hat den Auftrag abgelehnt (HTTP {statusCode}).");
        }
    }

    /// <summary>Welcher Abschnitt gehört zu diesem Knoten-Titel? Null: kein eigener Abschnitt (z.B. leere Latents).</summary>
    public static VideoPhase? PhaseFor(string title) => title switch
    {
        "KN_TextEncoder" => VideoPhase.LoadingTextEncoder,
        "KN_Prompt" or "KN_NegativePrompt" => VideoPhase.ReadingPrompt,
        "KN_Model" => VideoPhase.LoadingModel,
        "KN_Denoise" => VideoPhase.Denoising,
        "KN_DecodeAudio" => VideoPhase.DecodingAudio,
        // Nicht "KN_VideoVae": ComfyUI lädt die VAE oft schon ganz am Anfang. Sonst stünde dort kurz "Bilder erzeugen".
        "KN_DecodeVideo" => VideoPhase.DecodingVideo,
        "KN_CreateVideo" or "KN_Save" => VideoPhase.Saving,
        _ => null,
    };

    public static string Describe(VideoPhase phase) => phase switch
    {
        VideoPhase.Starting => "ComfyUI startet …",
        VideoPhase.LoadingTextEncoder => "Text-Encoder laden …",
        VideoPhase.ReadingPrompt => "Prompt lesen …",
        VideoPhase.LoadingModel => "Videomodell vorbereiten …",
        VideoPhase.Denoising => "Entrauschen (der erste Schritt lädt das Videomodell) …",
        VideoPhase.DecodingAudio => "Ton erzeugen …",
        VideoPhase.DecodingVideo => "Bilder erzeugen …",
        VideoPhase.Saving => "Video speichern …",
        _ => phase.ToString(),
    };

    /// <summary>Macht Fehler von ComfyUI verständlich. Am häufigsten: zu wenig Speicher.</summary>
    public static string Explain(ComfyError error)
    {
        var message = error.Message;
        var outOfMemory = message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OutOfMemory", StringComparison.Ordinal)
            || message.Contains("not enough memory", StringComparison.OrdinalIgnoreCase);
        if (outOfMemory)
        {
            return "Zu wenig Speicher für dieses Video. Versuch es kleiner (z.B. 512×320) oder kürzer, und schliess grosse Programme.";
        }

        return error.NodeType is null ? $"Fehler in ComfyUI: {message}" : $"Fehler in ComfyUI ({error.NodeType}): {message}";
    }

    public static string FullPath(ComfyFile file, string outputDirectory) =>
        Path.GetFullPath(Path.Combine(outputDirectory, file.Subfolder, file.Filename));

    /// <summary>SaveVideo meldet seine Datei unter "images" (mit animated = true). Wir nehmen jede Liste mit Dateinamen.</summary>
    private static List<ComfyFile> Files(JsonElement data)
    {
        var files = new List<ComfyFile>();
        if (!data.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object)
        {
            return files;
        }

        foreach (var list in output.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Array))
        {
            foreach (var item in list.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
            {
                if (Text(item, "filename") is { } name)
                {
                    files.Add(new ComfyFile(name, Text(item, "subfolder") ?? "", Text(item, "type") ?? "output"));
                }
            }
        }

        return files;
    }

    private static string? NodeErrors(JsonElement root)
    {
        if (!root.TryGetProperty("node_errors", out var nodes) || nodes.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var node in nodes.EnumerateObject())
        {
            if (node.Value.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                texts.AddRange(errors.EnumerateArray()
                    .Select(e => Text(e, "details") is { Length: > 0 } details ? $"{Text(e, "message")} ({details})" : Text(e, "message"))
                    .OfType<string>());
            }
        }

        return texts.Count == 0 ? null : string.Join("; ", texts.Distinct());
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (int)value.GetDouble() : 0;
}
