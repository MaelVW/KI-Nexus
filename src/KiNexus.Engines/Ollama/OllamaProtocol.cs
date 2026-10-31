using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;

namespace KiNexus.Engines.Ollama;

/// <summary>Eine Zeile aus dem Stream von /api/chat.</summary>
public sealed record OllamaStreamEvent(string Text, string Thinking, bool IsDone, ChatStats? Stats);

/// <summary>Übersetzt zwischen der App und der Ollama-API (/api/chat, /api/generate).</summary>
public static class OllamaProtocol
{
    public static JsonObject BuildChatRequest(ChatRequest request, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = RequireName(request.Ki),
            ["messages"] = new JsonArray(request.Messages.Select(ToJson).ToArray<JsonNode?>()),
            ["stream"] = stream,
            ["think"] = request.Think,
            ["keep_alive"] = KeepAlive(request.Ki),
            ["options"] = BuildOptions(request.Ki, request.EffectiveMaxTokens),
        };
        return body;
    }

    /// <summary>Lädt das Modell ohne Frage (leerer Prompt). Gleiche Optionen wie beim Chat, sonst lädt Ollama neu.</summary>
    public static JsonObject BuildLoadRequest(KiProfile ki) => new()
    {
        ["model"] = RequireName(ki),
        ["keep_alive"] = KeepAlive(ki),
        ["options"] = BuildOptions(ki),
    };

    /// <summary>keep_alive 0 = sofort entladen.</summary>
    public static JsonObject BuildUnloadRequest(string model) => new()
    {
        ["model"] = model,
        ["keep_alive"] = 0,
    };

    public static OllamaStreamEvent? ParseStreamLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new InferenceException($"Ollama meldet einen Fehler: {error.GetString()}");
        }

        var text = "";
        var thinking = "";
        if (root.TryGetProperty("message", out var message))
        {
            text = GetString(message, "content");
            thinking = GetString(message, "thinking");
        }

        var isDone = root.TryGetProperty("done", out var done) && done.GetBoolean();
        return new OllamaStreamEvent(text, thinking, isDone, isDone ? ParseStats(root) : null);
    }

    /// <summary>Anfrage an DELETE /api/delete.</summary>
    public static JsonObject BuildDeleteRequest(string model) => new() { ["model"] = model };

    /// <summary>Anfrage an /api/show: Darin steht unter "capabilities", ob das Modell Bilder versteht ("vision").</summary>
    public static JsonObject BuildShowRequest(string model) => new() { ["model"] = model };

    public static bool HasVision(JsonElement showResponse) =>
        showResponse.TryGetProperty("capabilities", out var capabilities)
        && capabilities.ValueKind == JsonValueKind.Array
        && capabilities.EnumerateArray().Any(c => c.GetString() == "vision");

    /// <summary>Ollama misst Zeiten in Nanosekunden.</summary>
    private static ChatStats ParseStats(JsonElement root)
    {
        var promptTokens = GetLong(root, "prompt_eval_count");
        var promptNs = GetLong(root, "prompt_eval_duration");
        var evalTokens = GetLong(root, "eval_count");
        var evalNs = GetLong(root, "eval_duration");
        var totalNs = GetLong(root, "total_duration");

        return new ChatStats(
            (int)promptTokens,
            (int)evalTokens,
            PerSecond(promptTokens, promptNs),
            PerSecond(evalTokens, evalNs),
            TimeSpan.FromTicks(totalNs / 100),
            EngineKind.Ollama,
            (int)(promptTokens + evalTokens));
    }

    private static JsonObject BuildOptions(KiProfile ki, int? maxTokens = null)
    {
        var p = ki.Parameters;
        var options = new JsonObject
        {
            ["num_ctx"] = p.ContextLength,
            ["num_predict"] = maxTokens ?? p.MaxTokens,
        };
        if (p.Temperature is double temperature) { options["temperature"] = temperature; }
        if (p.TopP is double topP) { options["top_p"] = topP; }
        if (p.TopK is int topK) { options["top_k"] = topK; }
        if (p.MinP is double minP) { options["min_p"] = minP; }
        if (p.RepeatPenalty is double repeat) { options["repeat_penalty"] = repeat; }
        if (p.PresencePenalty is double presence) { options["presence_penalty"] = presence; }
        return options;
    }

    /// <summary>"5m" = 5 Minuten. 0 Minuten in der KI heisst "geladen lassen" (-1 bei Ollama).</summary>
    private static JsonNode KeepAlive(KiProfile ki) =>
        ki.EngineOptions.KeepAliveMinutes <= 0
            ? JsonValue.Create(-1)
            : JsonValue.Create(ki.EngineOptions.KeepAliveMinutes.ToString(CultureInfo.InvariantCulture) + "m");

    private static string RequireName(KiProfile ki) =>
        string.IsNullOrWhiteSpace(ki.BaseModel.OllamaName)
            ? throw new InferenceException($"Bei der KI \"{ki.DisplayName}\" fehlt der Ollama-Name (baseModel.ollamaName).")
            : ki.BaseModel.OllamaName;

    /// <summary>Bilder hängt Ollama als Liste von Base64-Texten an die Nachricht ("images").</summary>
    private static JsonObject ToJson(ChatMessage message)
    {
        var json = new JsonObject
        {
            ["role"] = message.Role.ToString().ToLowerInvariant(),
            ["content"] = message.Content,
        };
        if (message.HasImages)
        {
            json["images"] = new JsonArray(message.ImagePaths!.Select(path => (JsonNode?)ImageEncoding.ToBase64(path)).ToArray());
        }

        return json;
    }

    private static double PerSecond(long tokens, long nanoseconds) =>
        nanoseconds > 0 ? tokens / (nanoseconds / 1_000_000_000d) : 0;

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
