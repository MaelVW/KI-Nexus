using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KiNexus.Core.Training;

/// <summary>Ein Beispiel im Datensatz: Frage und gewünschte Antwort, optional mit eigenem Systemprompt.</summary>
public sealed record TrainingExample(string Question, string Answer, string? System = null)
{
    /// <summary>Für die Liste und für Screenreader.</summary>
    public override string ToString() => Question;
}

/// <summary>Ergebnis eines Imports: die gelesenen Beispiele und wie viele Zeilen nicht passten.</summary>
public sealed record DatasetImport(IReadOnlyList<TrainingExample> Examples, int Skipped);

/// <summary>
/// Der Datensatz fürs Training (Bauplan 8.6) als JSONL: eine Zeile pro Beispiel, im Chat-Format
/// {"messages":[{"role":"user",…},{"role":"assistant",…}]}. Beim Import gehen auch andere übliche Formate.
/// </summary>
public static class TrainingDataset
{
    /// <summary>Eine Zeile pro Beispiel, Umlaute lesbar (nicht als ü).</summary>
    private static readonly JsonSerializerOptions LineOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] QuestionKeys = ["question", "frage", "prompt", "instruction", "input", "user"];
    private static readonly string[] AnswerKeys = ["answer", "antwort", "response", "completion", "output", "assistant"];

    public static DatasetImport Parse(IEnumerable<string> lines)
    {
        var examples = new List<TrainingExample>();
        var skipped = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ParseLine(line) is { } example)
            {
                examples.Add(example);
            }
            else
            {
                skipped++;
            }
        }

        return new DatasetImport(examples, skipped);
    }

    /// <summary>Eine Zeile lesen. Null, wenn sie kein Beispiel mit Frage und Antwort ist.</summary>
    public static TrainingExample? ParseLine(string line)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject root)
        {
            return null;
        }

        if (root["messages"] is JsonArray messages)
        {
            return FromMessages(messages);
        }

        // Alpaca-Format: instruction + input (Zusatz) → output.
        var instruction = Text(root, "instruction");
        var input = Text(root, "input");
        var question = instruction is not null && input is not null ? instruction + "\n\n" + input : FirstText(root, QuestionKeys);
        var answer = FirstText(root, AnswerKeys);
        return Valid(question, answer) ? new TrainingExample(question!.Trim(), answer!.Trim(), Text(root, "system")) : null;
    }

    public static string ToJsonLine(TrainingExample example)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(example.System))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = example.System });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = example.Question });
        messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = example.Answer });
        return new JsonObject { ["messages"] = messages }.ToJsonString(LineOptions);
    }

    public static async Task<IReadOnlyList<TrainingExample>> LoadAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? Parse(await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false)).Examples : [];

    /// <summary>Schreibt erst in eine Hilfsdatei und ersetzt dann. So geht bei einem Absturz nichts verloren.</summary>
    public static async Task SaveAsync(string path, IReadOnlyList<TrainingExample> examples, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllLinesAsync(temp, examples.Select(ToJsonLine), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Aus mehrteiligen Gesprächen: die letzte Antwort und die Frage direkt davor.</summary>
    private static TrainingExample? FromMessages(JsonArray messages)
    {
        var items = messages.OfType<JsonObject>().Select(m => (Role: Text(m, "role") ?? "", Content: Text(m, "content"))).ToList();
        var answerIndex = items.FindLastIndex(m => m.Role == "assistant");
        if (answerIndex <= 0)
        {
            return null;
        }

        var questionIndex = items.FindLastIndex(answerIndex - 1, m => m.Role == "user");
        var system = items.FirstOrDefault(m => m.Role == "system").Content;
        return questionIndex >= 0 && Valid(items[questionIndex].Content, items[answerIndex].Content)
            ? new TrainingExample(items[questionIndex].Content!.Trim(), items[answerIndex].Content!.Trim(), system)
            : null;
    }

    private static bool Valid(string? question, string? answer) => !string.IsNullOrWhiteSpace(question) && !string.IsNullOrWhiteSpace(answer);

    private static string? FirstText(JsonObject root, string[] keys) => keys.Select(key => Text(root, key)).FirstOrDefault(v => v is not null);

    private static string? Text(JsonObject root, string key) =>
        root[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
