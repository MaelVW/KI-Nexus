using System.Text.Json;
using KiNexus.Core.Inference;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Kis;

public interface IKiRegistry
{
    IReadOnlyList<KiProfile> All { get; }

    /// <summary>Wird ausgelöst, wenn eine KI dazukommt, sich ändert oder gelöscht wird.</summary>
    event EventHandler? Changed;

    Task<IReadOnlyList<KiProfile>> LoadAsync(CancellationToken ct);

    KiProfile? Find(string? id);

    /// <summary>Speichert die KI als kis/&lt;id&gt;.json (neu oder überschreiben).</summary>
    Task SaveAsync(KiProfile ki, CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}

/// <summary>
/// Liest und schreibt die KIs im Ordner kis/ (eine JSON-Datei pro KI).
/// Kaputte Dateien werden übersprungen und ins Log geschrieben.
/// </summary>
public sealed class KiRegistry(string directory, ILogger<KiRegistry> logger) : IKiRegistry
{
    public IReadOnlyList<KiProfile> All { get; private set; } = [];

    public event EventHandler? Changed;

    public async Task SaveAsync(KiProfile ki, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ki.Id);
        if (ki.Id != KiIds.Slug(ki.Id))
        {
            throw new ArgumentException($"Die Id \"{ki.Id}\" ist kein gültiger Dateiname.", nameof(ki));
        }

        var now = DateTimeOffset.Now;
        if (ki.CreatedAt == default)
        {
            ki.CreatedAt = now;
        }

        ki.UpdatedAt = now;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ki.Id + ".json");
        await using (var stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, ki, JsonDefaults.Options, ct).ConfigureAwait(false);
        }

        All = [.. All.Where(k => !string.Equals(k.Id, ki.Id, StringComparison.OrdinalIgnoreCase)), ki];
        logger.LogInformation("KI {Id} gespeichert.", ki.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(directory, KiIds.Slug(id) + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        All = [.. All.Where(k => !string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase))];
        logger.LogInformation("KI {Id} gelöscht.", id);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

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
