using System.Text;

namespace KiNexus.Core.Kis;

/// <summary>Macht aus einem Namen eine Id (= Dateiname in kis/): "Mein Coach Ü" → "mein-coach-ue".</summary>
public static class KiIds
{
    public static string FromName(string displayName, IEnumerable<string> existingIds)
    {
        var baseId = Slug(displayName);
        if (baseId.Length == 0)
        {
            baseId = "ki";
        }

        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{baseId}-{n}";
        }

        return id;
    }

    /// <summary>Nur Kleinbuchstaben, Ziffern, Punkt, Unterstrich und Bindestrich. Umlaute werden zu ae, oe, ue.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            var part = c switch
            {
                'ä' => "ae",
                'ö' => "oe",
                'ü' => "ue",
                'ß' => "ss",
                _ when char.IsAsciiLetterOrDigit(c) || c is '.' or '_' => c.ToString(),
                _ => "-",
            };
            builder.Append(part);
        }

        var slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-', '.');
    }
}
