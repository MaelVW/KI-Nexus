using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KiNexus.Engines.HuggingFace;

/// <summary>
/// Liest die Antworten der Hugging-Face-API und ordnet die GGUF-Dateien.
/// API: /api/models (Suche) und /api/models/&lt;repo&gt;/tree/main (Dateien).
/// </summary>
public static partial class HuggingFaceProtocol
{
    /// <summary>Ein guter Standard: klein genug und kaum schlechter als das Original.</summary>
    public const string RecommendedQuant = "Q4_K_M";

    /// <summary>Gibt es Q4_K_M nicht, ist die erste vorhandene Stufe aus dieser Liste die Empfehlung.</summary>
    private static readonly string[] Fallbacks = ["Q4_K_M", "UD-Q4_K_XL", "Q4_K_S", "IQ4_XS", "Q4_0", "Q5_K_M", "Q6_K", "Q8_0"];

    /// <summary>Pfad für die Suche. Nur GGUF-Modelle, die beliebtesten zuerst.</summary>
    public static string SearchPath(string query, int limit)
    {
        var builder = new StringBuilder("api/models?filter=gguf&sort=downloads&direction=-1");
        builder.Append(CultureInfo.InvariantCulture, $"&limit={limit}");
        if (!string.IsNullOrWhiteSpace(query))
        {
            builder.Append("&search=").Append(Uri.EscapeDataString(query.Trim()));
        }

        foreach (var field in new[] { "downloads", "likes", "lastModified", "gated", "cardData", "pipeline_tag", "tags", "gguf" })
        {
            builder.Append("&expand[]=").Append(field);
        }

        return builder.ToString();
    }

    public static string TreePath(string repo) => $"api/models/{EscapeRepo(repo)}/tree/main?recursive=true";

    /// <summary>Download-Pfad. Jeder Teil des Pfads wird einzeln maskiert, die Schrägstriche bleiben.</summary>
    public static string ResolvePath(string repo, string filePath) =>
        $"{EscapeRepo(repo)}/resolve/main/{string.Join('/', filePath.Split('/').Select(Uri.EscapeDataString))}";

    public static string EscapeRepo(string repo) => string.Join('/', repo.Split('/').Select(Uri.EscapeDataString));

    public static IReadOnlyList<HfModel> ParseSearch(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return root.EnumerateArray().Select(item => new HfModel(
            Id: GetString(item, "id") ?? GetString(item, "modelId") ?? "",
            Downloads: GetLong(item, "downloads"),
            Likes: GetLong(item, "likes"),
            LastModified: item.TryGetProperty("lastModified", out var date) && date.ValueKind == JsonValueKind.String && date.TryGetDateTimeOffset(out var parsed) ? parsed : null,
            Gated: item.TryGetProperty("gated", out var gated) && gated.ValueKind is JsonValueKind.String or JsonValueKind.True,
            License: ReadLicense(item),
            PipelineTag: GetString(item, "pipeline_tag"),
            Tags: item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? [.. tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!)]
                : null,
            GgufArchitecture: item.TryGetProperty("gguf", out var gguf) && gguf.ValueKind == JsonValueKind.Object ? GetString(gguf, "architecture") : null))
            .Where(m => m.Id.Length > 0)
            .ToList();
    }

    public static IReadOnlyList<HfFile> ParseTree(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return root.EnumerateArray()
            .Where(item => GetString(item, "type") == "file")
            .Select(item => new HfFile(
                GetString(item, "path") ?? "",
                GetLong(item, "size"),
                item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object ? GetString(lfs, "oid") : null))
            .Where(f => f.Path.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Ordnet die Dateien: Teile eines geteilten Modells kommen zusammen, Projektoren (mmproj) separat.
    /// Sortiert nach Grösse, die Empfehlung ist markiert.
    /// </summary>
    public static HfRepoFiles Group(string repo, IEnumerable<HfFile> files)
    {
        var ggufs = files.Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
        var projectors = ggufs.Where(IsProjector).OrderBy(ProjectorRank).ThenBy(f => f.Size).ToList();

        var groups = ggufs.Where(f => !IsProjector(f))
            .GroupBy(f => SplitPart().Replace(f.Path, ""), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Label: QuantLabel(g.First().Path), Files: (IReadOnlyList<HfFile>)g.OrderBy(f => f.Path, StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Files.Sum(f => f.Size))
            .ToList();

        // Gleiche Stufe mehrmals (z.B. "Q2_K" als dev, distilled und distilled-1.1): Ordner oder Dateiname dazuschreiben.
        var duplicates = groups.GroupBy(g => g.Label, StringComparer.OrdinalIgnoreCase).Where(d => d.Count() > 1).Select(d => d.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        groups = [.. groups.Select(g => duplicates.Contains(g.Label) ? (Label: $"{g.Label} ({Variant(g.Files[0].Path, g.Label)})", g.Files) : g)];

        var recommended = Fallbacks
            .Select(label => groups.FindIndex(g => string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase)
                                                   || g.Label.StartsWith(label + " (", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(index => index >= 0, -1);

        var options = groups.Select((g, index) => new HfQuantOption(g.Label, g.Files, index == recommended)).ToList();
        return new HfRepoFiles(repo, options, projectors);
    }

    /// <summary>"Qwen3-0.6B-Q8_0.gguf" → "Q8_0". Findet nichts, gilt der Dateiname (oder der Ordner).</summary>
    public static string QuantLabel(string path)
    {
        var stem = SplitPart().Replace(Path.GetFileName(path), "");
        stem = stem.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? stem[..^5] : stem;
        var matches = QuantPattern().Matches(stem);
        if (matches.Count > 0)
        {
            return matches[^1].Value.ToUpperInvariant();
        }

        var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
        return string.IsNullOrEmpty(folder) ? stem : folder.Split('/')[^1];
    }

    public static bool IsProjector(HfFile file) => file.FileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>Was zwei Dateien mit derselben Stufe unterscheidet: der Ordner, sonst der Dateiname ohne die Stufe.</summary>
    private static string Variant(string path, string label)
    {
        var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder))
        {
            return folder.Split('/')[^1];
        }

        var stem = SplitPart().Replace(Path.GetFileNameWithoutExtension(path), "");
        var trimmed = stem.Replace(label, "", StringComparison.OrdinalIgnoreCase).Trim('-', '_', '.', ' ');
        return trimmed.Length > 0 ? trimmed : stem;
    }

    /// <summary>F16 ist die übliche, verlustfreie Wahl für Projektoren. Danach BF16, F32, dann die verkleinerten.</summary>
    private static int ProjectorRank(HfFile file)
    {
        var name = file.FileName.ToUpperInvariant();
        return name.Contains("BF16", StringComparison.Ordinal) ? 1
            : name.Contains("F16", StringComparison.Ordinal) ? 0
            : name.Contains("F32", StringComparison.Ordinal) ? 2
            : 3;
    }

    /// <summary>Die Lizenz steht in der Modellkarte (cardData), manchmal als Liste.</summary>
    private static string? ReadLicense(JsonElement item)
    {
        if (!item.TryGetProperty("cardData", out var card) || card.ValueKind != JsonValueKind.Object || !card.TryGetProperty("license", out var license))
        {
            return null;
        }

        return license.ValueKind switch
        {
            JsonValueKind.String => license.GetString(),
            JsonValueKind.Array => string.Join(", ", license.EnumerateArray().Select(l => l.GetString()).OfType<string>()),
            _ => null,
        };
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long GetLong(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    [GeneratedRegex(@"-\d{5}-of-\d{5}(?=\.gguf$)", RegexOptions.IgnoreCase)]
    private static partial Regex SplitPart();

    /// <summary>Typische Namen: Q4_K_M, IQ4_XS, Q8_0, UD-Q4_K_XL, BF16, F16, MXFP4_MOE.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:UD-)?(?:I?Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32|MXFP4(?:_MOE)?)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex QuantPattern();
}
