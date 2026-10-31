using System.Text.RegularExpressions;
using KiNexus.Core.Kis;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Models;

/// <summary>
/// Nimmt GGUF-Dateien in den Ordner models/ auf: aus einer Datei (Drag &amp; Drop, Datei-Dialog)
/// oder nach einem Download. Schreibt dazu model.json mit den Infos aus dem GGUF-Kopf.
/// </summary>
/// <param name="readOnlyFolders">Ordner, aus denen nie verschoben wird (z.B. der Modell-Ordner von Ollama).</param>
public sealed partial class ModelImporter(ModelLibrary library, IReadOnlyList<string> readOnlyFolders, ILogger<ModelImporter> logger)
{
    private const int CopyBufferSize = 4 * 1024 * 1024;

    /// <summary>Ordnername aus dem Dateinamen: "Qwen3-0.6B-Q8_0.gguf" → "qwen3-0.6b-q8_0". Ohne "-00001-of-00003".</summary>
    public static string FolderNameFor(string modelFileName)
    {
        var name = Path.GetFileNameWithoutExtension(modelFileName);
        return KiIds.Slug(SplitSuffix().Replace(name, ""));
    }

    /// <summary>Bei geteilten Modellen ("…-00001-of-00003.gguf") gehören alle Teile dazu.</summary>
    public static IReadOnlyList<string> PartsOf(string modelFile)
    {
        var match = SplitSuffix().Match(Path.GetFileNameWithoutExtension(modelFile));
        if (!match.Success)
        {
            return [modelFile];
        }

        var prefix = Path.GetFileNameWithoutExtension(modelFile)[..match.Index];
        var count = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.GetDirectoryName(modelFile) ?? "";
        var digits = match.Groups[1].Value.Length;
        return Enumerable.Range(1, count)
            .Select(i => Path.Combine(folder, $"{prefix}-{i.ToString().PadLeft(digits, '0')}-of-{match.Groups[2].Value}.gguf"))
            .ToList();
    }

    /// <summary>
    /// Prüft die Dateien und kopiert oder verschiebt sie nach models/&lt;name&gt;/.
    /// </summary>
    /// <param name="move">True = verschieben (spart Platz), false = kopieren (Original bleibt).</param>
    public async Task<LocalModel> ImportFileAsync(string modelFile, string? mmprojFile, bool move, string source, string? license,
        IProgress<double>? progress, CancellationToken ct)
    {
        var parts = PartsOf(Path.GetFullPath(modelFile));
        Validate(parts, mmprojFile);
        if (move && parts.Append(mmprojFile).OfType<string>().Any(IsReadOnly))
        {
            throw new InvalidOperationException("Aus dem Ollama-Ordner wird nie verschoben, damit Ollama-Dateien unverändert bleiben. Wähle \"Kopieren\" oder übernimm das Modell unter \"Aus Ollama\".");
        }

        var folder = library.CreateFolder(FolderNameFor(modelFile));
        try
        {
            var files = mmprojFile is null ? parts : [.. parts, mmprojFile];
            var total = files.Sum(f => new FileInfo(f).Length);
            long done = 0;
            foreach (var file in files)
            {
                var target = Path.Combine(folder, Path.GetFileName(file));
                await TransferAsync(file, target, move, bytes => progress?.Report((done + bytes) / (double)Math.Max(1, total)), ct).ConfigureAwait(false);
                done += new FileInfo(target).Length;
            }

            var model = library.WriteManifestFor(folder, Path.GetFileName(parts[0]), mmprojFile is null ? null : Path.GetFileName(mmprojFile), source, license);
            logger.LogInformation("Modell {Folder} aufgenommen ({Mode}).", Path.GetFileName(folder), move ? "verschoben" : "kopiert");
            return model;
        }
        catch
        {
            // Halbe Kopien nicht liegen lassen. Beim Verschieben liegt das Original noch am alten Ort, bis es fertig ist.
            TryDelete(folder);
            throw;
        }
    }

    private bool IsReadOnly(string file)
    {
        var full = Path.GetFullPath(file);
        return readOnlyFolders.Any(folder =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static void Validate(IReadOnlyList<string> parts, string? mmprojFile)
    {
        foreach (var part in parts)
        {
            if (!File.Exists(part))
            {
                throw new FileNotFoundException($"Die Datei fehlt: {Path.GetFileName(part)}. Bei geteilten Modellen müssen alle Teile im selben Ordner liegen.");
            }
        }

        var metadata = GgufReader.Read(parts[0]);
        if (metadata.IsProjector)
        {
            throw new InvalidDataException("Das ist ein Bild-Projektor (mmproj), kein Modell. Wähle zuerst die Modell-Datei und den Projektor als Zusatz.");
        }

        if (mmprojFile is not null && !GgufReader.Read(mmprojFile).IsProjector)
        {
            throw new InvalidDataException($"{Path.GetFileName(mmprojFile)} ist kein Bild-Projektor (mmproj).");
        }
    }

    /// <summary>Verschieben auf demselben Laufwerk geht sofort. Sonst wird kopiert (mit Fortschritt).</summary>
    private static async Task TransferAsync(string source, string target, bool move, Action<long> progress, CancellationToken ct)
    {
        if (move && string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(source, target);
            progress(new FileInfo(target).Length);
            return;
        }

        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
        {
            var buffer = new byte[CopyBufferSize];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                copied += read;
                progress(copied);
            }
        }

        if (move)
        {
            File.Delete(source);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Nicht schlimm: Der Ordner hat kein model.json und wird darum nicht als Modell angezeigt.
        }
    }

    [GeneratedRegex(@"-(\d{5})-of-(\d{5})$")]
    private static partial Regex SplitSuffix();
}
