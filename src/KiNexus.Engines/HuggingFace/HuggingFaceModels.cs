using System.Text.Json.Serialization;

namespace KiNexus.Engines.HuggingFace;

/// <summary>Ein Treffer der Suche: ein Repository (Modell-Sammlung) auf Hugging Face.</summary>
/// <param name="Gated">Gesperrt: Man muss auf der Webseite zuerst die Bedingungen akzeptieren und braucht einen Token.</param>
public sealed record HfModel(string Id, long Downloads, long Likes, DateTimeOffset? LastModified, bool Gated, string? License, string? PipelineTag)
{
    public string Author => Id.Contains('/') ? Id[..Id.IndexOf('/')] : "";

    public string Name => Id.Contains('/') ? Id[(Id.IndexOf('/') + 1)..] : Id;
}

/// <summary>Eine Datei im Repository.</summary>
/// <param name="Path">Pfad im Repository, z.B. "Q4_K_M/modell-00001-of-00002.gguf".</param>
/// <param name="Sha256">Prüfsumme (nur bei grossen Dateien, die Hugging Face als LFS speichert).</param>
public sealed record HfFile(string Path, long Size, string? Sha256)
{
    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Eine Quantisierung (Stufe der Verkleinerung) zum Herunterladen. Grosse Modelle bestehen aus mehreren Teilen.</summary>
public sealed record HfQuantOption(string Label, IReadOnlyList<HfFile> Files, bool IsRecommended)
{
    public long TotalBytes => Files.Sum(f => f.Size);

    public bool IsSplit => Files.Count > 1;
}

/// <summary>Alle GGUF-Dateien eines Repositorys, schon sortiert und gruppiert.</summary>
/// <param name="Projectors">Bild-Projektoren (mmproj). Die beste Wahl steht zuerst.</param>
public sealed record HfRepoFiles(string Repo, IReadOnlyList<HfQuantOption> Options, IReadOnlyList<HfFile> Projectors);

/// <summary>Fehler beim Gespräch mit Hugging Face, mit einer Meldung für die Oberfläche.</summary>
public sealed class HuggingFaceException(string message, Exception? inner = null) : Exception(message, inner);
