using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Kis;

/// <summary>
/// Eigene Vorlagen für den Systemprompt: z.B. ein Prompt, den man öfters braucht, der aber zu keiner der eingebauten passt.
/// Liegen in data/prompt-vorlagen.json (bleibt bei Updates erhalten).
/// </summary>
public sealed class PromptTemplateStore(string file, ILogger<PromptTemplateStore> logger)
{
    public const int MaxNameLength = 40;

    /// <summary>Alle eigenen Vorlagen, nach Name. Fehlt die Datei oder ist sie kaputt: keine.</summary>
    public IReadOnlyList<PromptTemplate> Load()
    {
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(file), JsonDefaults.Options) ?? [];
            return [.. items.Where(i => i.Name.Trim().Length > 0)
                .Select(i => new PromptTemplate(i.Name.Trim(), i.Text, IsCustom: true))
                .DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Eigene Vorlagen nicht lesbar: {File}", file);
            return [];
        }
    }

    /// <summary>Speichert eine Vorlage. Gibt es den Namen schon (ohne Gross/klein), wird sie ersetzt.</summary>
    public IReadOnlyList<PromptTemplate> Save(string name, string text)
    {
        var clean = name.Trim();
        if (clean.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"Der Name muss 1 bis {MaxNameLength} Zeichen haben.", nameof(name));
        }

        var all = Load().Where(t => !string.Equals(t.Name, clean, StringComparison.OrdinalIgnoreCase)).ToList();
        all.Add(new PromptTemplate(clean, text.TrimEnd(), IsCustom: true));
        Write(all);
        logger.LogInformation("Eigene Vorlage {Name} gespeichert.", clean);
        return Load();
    }

    public IReadOnlyList<PromptTemplate> Delete(string name)
    {
        Write([.. Load().Where(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))]);
        logger.LogInformation("Eigene Vorlage {Name} gelöscht.", name);
        return Load();
    }

    public bool Exists(string name) => Load().Any(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private void Write(IReadOnlyList<PromptTemplate> templates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var items = templates.Select(t => new Item { Name = t.Name, Text = t.Text }).ToList();
        File.WriteAllText(file, JsonSerializer.Serialize(items, JsonDefaults.Options));
    }

    private sealed class Item
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
