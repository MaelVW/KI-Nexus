using System.Text.Json;
using System.Text.Json.Nodes;
using KiNexus.Core.Inference;

namespace KiNexus.Engines.LlamaCpp;

/// <summary>
/// Zeitmessung, die llama-server am Ende einer Antwort mitschickt.
/// CachedTokens = Token, die aus dem Cache kamen und nicht neu gelesen werden mussten.
/// </summary>
public sealed record LlamaTimings(int PromptTokens, double PromptMs, double PromptPerSecond, int PredictedTokens, double PredictedMs, double PredictedPerSecond, int CachedTokens = 0)
{
    /// <summary>Alles, was nach der Antwort im Kontext steht.</summary>
    public int ContextTokens => CachedTokens + PromptTokens + PredictedTokens;
}

/// <summary>Ein Ereignis aus dem Stream: neuer Text, neue Gedanken und/oder die Zeitmessung.</summary>
public sealed record LlamaStreamEvent(string Text, string Thinking, LlamaTimings? Timings);

/// <summary>
/// Übersetzt zwischen der App und der OpenAI-kompatiblen API von llama-server (/v1/chat/completions).
/// </summary>
public static class LlamaCppProtocol
{
    public const string DoneMarker = "[DONE]";

    public static JsonObject BuildChatRequest(ChatRequest request, bool stream)
    {
        var p = request.Ki.Parameters;
        var body = new JsonObject
        {
            ["messages"] = new JsonArray(request.Messages.Select(ToJson).ToArray<JsonNode?>()),
            ["stream"] = stream,
            ["max_tokens"] = request.EffectiveMaxTokens,
            ["cache_prompt"] = true,
            // Beide Vorlagen (Gemma 4, Qwen 3.5) kennen "enable_thinking". Wir setzen es immer ausdrücklich.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = request.Think },
        };

        if (!request.Think)
        {
            body["reasoning_effort"] = "none";
        }

        // Feste Form der Antwort: llama.cpp baut daraus eine Grammatik, das Modell kann nichts anderes schreiben.
        if (request.ResponseSchema is { } schema)
        {
            body["json_schema"] = schema.DeepClone();
        }

        if (stream)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        AddIfSet(body, "temperature", p.Temperature);
        AddIfSet(body, "top_p", p.TopP);
        AddIfSet(body, "top_k", p.TopK);
        AddIfSet(body, "min_p", p.MinP);
        AddIfSet(body, "repeat_penalty", p.RepeatPenalty);
        AddIfSet(body, "presence_penalty", p.PresencePenalty);
        return body;
    }

    /// <summary>
    /// Liest eine Zeile aus dem Stream ("data: {...}").
    /// Gibt null zurück für leere Zeilen, Kommentare und das Ende-Zeichen [DONE].
    /// </summary>
    public static LlamaStreamEvent? ParseStreamLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = line["data:".Length..].Trim();
        if (payload == DoneMarker)
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new InferenceException($"llama-server meldet einen Fehler: {ReadErrorMessage(error)}");
        }

        var text = "";
        var thinking = "";
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("delta", out var delta))
        {
            text = GetString(delta, "content");
            thinking = GetString(delta, "reasoning_content");
        }

        var timings = root.TryGetProperty("timings", out var t) ? ParseTimings(t) : null;
        return new LlamaStreamEvent(text, thinking, timings);
    }

    public static ChatStats ToStats(LlamaTimings? timings, TimeSpan wallTime) =>
        timings is null
            ? new ChatStats(0, 0, 0, 0, wallTime, EngineKind.LlamaCpp)
            : new ChatStats(timings.PromptTokens, timings.PredictedTokens, timings.PromptPerSecond,
                timings.PredictedPerSecond, wallTime, EngineKind.LlamaCpp, timings.ContextTokens);

    private static LlamaTimings ParseTimings(JsonElement t) => new(
        GetInt(t, "prompt_n"), GetDouble(t, "prompt_ms"), GetDouble(t, "prompt_per_second"),
        GetInt(t, "predicted_n"), GetDouble(t, "predicted_ms"), GetDouble(t, "predicted_per_second"),
        GetInt(t, "cache_n"));

    /// <summary>
    /// Ohne Bilder ist "content" einfach Text. Mit Bildern ist es eine Liste aus Text und Bildern (OpenAI-Format).
    /// </summary>
    private static JsonObject ToJson(ChatMessage message)
    {
        var json = new JsonObject { ["role"] = message.Role.ToString().ToLowerInvariant() };
        if (!message.HasImages)
        {
            json["content"] = message.Content;
            return json;
        }

        var parts = new JsonArray();
        foreach (var image in message.ImagePaths!)
        {
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = ImageEncoding.ToDataUrl(image) },
            });
        }

        parts.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
        json["content"] = parts;
        return json;
    }

    private static void AddIfSet<T>(JsonObject body, string key, T? value) where T : struct
    {
        if (value is T v)
        {
            body[key] = JsonValue.Create(v);
        }
    }

    private static string ReadErrorMessage(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : error.ToString();

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
