using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KiNexus.Core.Settings;

/// <summary>
/// Gemeinsame JSON-Optionen: Schlüssel in camelCase, Enums als Text, schön eingerückt.
/// </summary>
public static class SettingsJson
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

    /// <summary>Tiefe Kopie über JSON. So ändert ein Dialog nie direkt die aktiven Einstellungen.</summary>
    public static AppSettings Clone(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }
}
