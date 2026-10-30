using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KiNexus.Core;

/// <summary>
/// Gemeinsame JSON-Optionen für alle Dateien von KI-Nexus (settings.json, KI-Profile, model.json):
/// Schlüssel in camelCase, Enums als Text, schön eingerückt.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Umlaute bleiben lesbar ("ü" statt "ü").
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Tiefe Kopie über JSON. So ändert z.B. ein Dialog nie direkt die aktiven Daten.</summary>
    public static T Clone<T>(T value) where T : new()
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
    }
}
