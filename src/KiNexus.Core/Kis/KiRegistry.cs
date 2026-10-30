using System.Text.Json;
using KiNexus.Core.Inference;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Kis;

public interface IKiRegistry
{
    IReadOnlyList<KiProfile> All { get; }

    Task<IReadOnlyList<KiProfile>> LoadAsync(CancellationToken ct);

    KiProfile? Find(string? id);
}

/// <summary>
/// Liest alle KIs aus dem Ordner kis/ (eine JSON-Datei pro KI).
/// Kaputte Dateien werden übersprungen und ins Log geschrieben.
/// </summary>
public sealed class KiRegistry(string directory, ILogger<KiRegistry> logger) : IKiRegistry
{
    public IReadOnlyList<KiProfile> All { get; private set; } = [];

    public async Task<IReadOnlyList<KiProfile>> LoadAsync(CancellationToken ct)
    {
        var kis = new List<KiProfile>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                var ki = await TryReadAsync(file, ct).ConfigureAwait(false);
                if (ki is not null)
                {
                    kis.Add(ki);
                }
            }
        }

        All = kis;
        logger.LogInformation("{Count} KIs geladen aus {Directory}", kis.Count, directory);
        return All;
    }

    public KiProfile? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(ki => string.Equals(ki.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sortiert für die Auswahl: zuerst die bevorzugte Engine (Einstellung "Engine-Reihenfolge"), dann nach Name.
    /// </summary>
    public static IReadOnlyList<KiProfile> SortForDisplay(IEnumerable<KiProfile> kis, EngineOrder order)
    {
        var preferred = order == EngineOrder.OllamaFirst ? EngineKind.Ollama : EngineKind.LlamaCpp;
        return kis
            .OrderBy(ki => ki.Engine == preferred ? 0 : 1)
            .ThenBy(ki => ki.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<KiProfile?> TryReadAsync(string file, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(file);
            var ki = await JsonSerializer.DeserializeAsync<KiProfile>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
            return ki is null ? null : Normalize(ki, file);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "KI-Datei ist beschädigt und wird übersprungen: {File}", file);
            return null;
        }
    }

    /// <summary>Der Dateiname ist die Id. Fehlende Werte bekommen einen Standard.</summary>
    private KiProfile Normalize(KiProfile ki, string file)
    {
        var idFromFile = Path.GetFileNameWithoutExtension(file);
        if (!string.Equals(ki.Id, idFromFile, StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(ki.Id))
            {
                logger.LogWarning("Id '{Id}' passt nicht zum Dateinamen {File}. Es gilt der Dateiname.", ki.Id, file);
            }

            ki.Id = idFromFile;
        }

        if (string.IsNullOrWhiteSpace(ki.DisplayName)) { ki.DisplayName = ki.Id; }
        ki.BaseModel ??= new();
        ki.Parameters ??= new();
        ki.EngineOptions ??= new();
        ki.Voice ??= new();
        ki.SystemPrompt ??= "";
        ki.Description ??= "";
        return ki;
    }
}
