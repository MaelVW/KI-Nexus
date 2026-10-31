using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KiNexus.Core;
using KiNexus.Core.Models;
using Microsoft.Extensions.Logging;

namespace KiNexus.Engines.HuggingFace;

public enum DownloadState
{
    Running,
    Paused,
    Verifying,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Was zu einem Download gehört. Liegt als download.json im Download-Ordner, damit er nach einem Neustart weitergehen kann.</summary>
public sealed record DownloadPlan(string Repo, string Label, IReadOnlyList<HfFile> ModelFiles, HfFile? Projector, string? License)
{
    [JsonIgnore]
    public IEnumerable<HfFile> AllFiles => Projector is null ? ModelFiles : [.. ModelFiles, Projector];

    [JsonIgnore]
    public long TotalBytes => AllFiles.Sum(f => f.Size);
}

/// <summary>Ein Download. Die Werte ändern sich im Hintergrund, <see cref="Changed"/> meldet das (nicht im UI-Thread).</summary>
public sealed class DownloadJob
{
    internal DownloadJob(string id, string stagingDirectory, DownloadPlan plan)
    {
        Id = id;
        StagingDirectory = stagingDirectory;
        Plan = plan;
    }

    /// <summary>Gleichzeitig der Ordnername unter models/.</summary>
    public string Id { get; }

    public DownloadPlan Plan { get; }

    public string StagingDirectory { get; }

    public DownloadState State { get; internal set; } = DownloadState.Paused;

    public long BytesDone { get; internal set; }

    public long TotalBytes => Plan.TotalBytes;

    public double BytesPerSecond { get; internal set; }

    public string? Error { get; internal set; }

    /// <summary>Das fertige Modell in models/, sobald alles geprüft ist.</summary>
    public LocalModel? Result { get; internal set; }

    public event EventHandler? Changed;

    internal CancellationTokenSource? Cancellation { get; set; }

    internal Task Worker { get; set; } = Task.CompletedTask;

    internal bool CancelRequested { get; set; }

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Lädt GGUF-Dateien von Hugging Face nach models/.downloads/&lt;id&gt;/ (als .part-Dateien).
/// Pause und Fortsetzen gehen über HTTP-Range (ab dem Byte weiterladen, wo es aufgehört hat).
/// Danach prüft er die SHA-256-Prüfsumme und verschiebt alles nach models/&lt;id&gt;/.
/// </summary>
public sealed class ModelDownloader(HuggingFaceClient client, ModelLibrary library, ILogger<ModelDownloader> logger)
{
    private const string PlanFileName = "download.json";
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Mindestens so viel Platz soll nach dem Download frei bleiben.</summary>
    private const long FreeSpaceReserve = 2L * 1024 * 1024 * 1024;

    /// <summary>So oft versucht er es bei einer hängenden oder abgebrochenen Verbindung, bevor er aufgibt.</summary>
    internal int MaxAttempts { get; init; } = 5;

    /// <summary>Kommen so lange keine Daten, gilt die Verbindung als hängend und wird neu aufgebaut (ab demselben Byte).</summary>
    internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Wartezeit vor dem ersten neuen Versuch. Danach jedes Mal doppelt so lang.</summary>
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    private readonly List<DownloadJob> _jobs = [];

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_jobs)
            {
                return [.. _jobs];
            }
        }
    }

    /// <summary>Ein Download kam dazu oder wurde entfernt.</summary>
    public event EventHandler? JobsChanged;

    /// <summary>Angefangene Downloads vom letzten Mal wieder in die Liste holen (pausiert).</summary>
    public void RestorePending()
    {
        if (!Directory.Exists(library.DownloadsDirectory))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(library.DownloadsDirectory))
        {
            var planFile = Path.Combine(folder, PlanFileName);
            var id = Path.GetFileName(folder);
            if (!File.Exists(planFile) || Find(id) is not null)
            {
                continue;
            }

            try
            {
                var plan = JsonSerializer.Deserialize<DownloadPlan>(File.ReadAllText(planFile), JsonDefaults.Options);
                if (plan is not null)
                {
                    var job = new DownloadJob(id, folder, plan) { BytesDone = BytesOnDisk(folder, plan) };
                    Add(job);
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Angefangener Download {Folder} ist nicht lesbar.", folder);
            }
        }
    }

    /// <summary>Startet einen neuen Download. Gibt es denselben schon (angefangen), geht er dort weiter.</summary>
    public DownloadJob Start(DownloadPlan plan)
    {
        var id = ModelImporter.FolderNameFor(plan.ModelFiles[0].FileName);
        var existing = Find(id);
        if (existing is not null && existing.Plan.Repo == plan.Repo)
        {
            Resume(existing);
            return existing;
        }

        if (existing is not null || Directory.Exists(Path.Combine(library.Directory, id)))
        {
            id = UniqueId(id);
        }

        var staging = Path.Combine(library.DownloadsDirectory, id);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, PlanFileName), JsonSerializer.Serialize(plan, JsonDefaults.Options));

        var job = new DownloadJob(id, staging, plan) { BytesDone = BytesOnDisk(staging, plan) };
        Add(job);
        Resume(job);
        return job;
    }

    public void Pause(DownloadJob job)
    {
        if (job.State == DownloadState.Running)
        {
            job.Cancellation?.Cancel();
        }
    }

    public void Resume(DownloadJob job)
    {
        if (job.State is DownloadState.Running or DownloadState.Verifying or DownloadState.Completed or DownloadState.Cancelled)
        {
            return;
        }

        job.Error = null;
        job.CancelRequested = false;
        job.State = DownloadState.Running;
        job.Cancellation = new CancellationTokenSource();
        job.RaiseChanged();
        var token = job.Cancellation.Token;
        job.Worker = Task.Run(() => RunAsync(job, token));
    }

    /// <summary>Bricht ab und löscht die angefangenen Dateien.</summary>
    public async Task CancelAsync(DownloadJob job)
    {
        job.CancelRequested = true;
        job.Cancellation?.Cancel();
        await job.Worker.ConfigureAwait(false);

        if (job.State != DownloadState.Completed)
        {
            TryDeleteDirectory(job.StagingDirectory);
            job.State = DownloadState.Cancelled;
            job.RaiseChanged();
        }

        Remove(job);
    }

    /// <summary>Fertige, abgebrochene oder fehlgeschlagene Downloads aus der Liste nehmen.</summary>
    public void Dismiss(DownloadJob job)
    {
        if (job.State is DownloadState.Completed or DownloadState.Cancelled)
        {
            Remove(job);
        }
    }

    /// <summary>Für den Fortschritt: Wie viel liegt schon auf der Festplatte?</summary>
    internal static long BytesOnDisk(string staging, DownloadPlan plan) => plan.AllFiles.Sum(file =>
    {
        var done = Path.Combine(staging, file.FileName);
        var part = done + ".part";
        return File.Exists(done) ? file.Size : File.Exists(part) ? Math.Min(file.Size, new FileInfo(part).Length) : 0;
    });

    private async Task RunAsync(DownloadJob job, CancellationToken ct)
    {
        try
        {
            if (FreeSpaceProblem(job) is { } problem)
            {
                job.Error = problem;
                job.State = DownloadState.Failed;
                return;
            }

            var finishedBytes = 0L;
            foreach (var file in job.Plan.AllFiles)
            {
                await DownloadFileAsync(job, file, finishedBytes, ct).ConfigureAwait(false);
                finishedBytes += file.Size;
            }

            job.Result = Finish(job);
            job.State = DownloadState.Completed;
            job.BytesPerSecond = 0;
            logger.LogInformation("Download {Id} von {Repo} fertig.", job.Id, job.Plan.Repo);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.State = job.CancelRequested ? DownloadState.Cancelled : DownloadState.Paused;
            job.BytesPerSecond = 0;
            job.BytesDone = BytesOnDisk(job.StagingDirectory, job.Plan);
        }
        catch (Exception ex) when (ex is HuggingFaceException or IOException or InvalidDataException or UnauthorizedAccessException
                                       or HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Download {Id} fehlgeschlagen.", job.Id);
            job.Error = ex is HttpRequestException or TimeoutException or IOException and not FileNotFoundException
                ? $"Die Verbindung ist abgebrochen oder die Festplatte meldet einen Fehler. \"Fortsetzen\" macht dort weiter. ({ex.Message})"
                : ex.Message;
            job.State = DownloadState.Failed;
            job.BytesPerSecond = 0;
            job.BytesDone = BytesOnDisk(job.StagingDirectory, job.Plan);
        }
        finally
        {
            job.RaiseChanged();
        }
    }

    private async Task DownloadFileAsync(DownloadJob job, HfFile file, long finishedBytes, CancellationToken ct)
    {
        var done = Path.Combine(job.StagingDirectory, file.FileName);
        if (File.Exists(done))
        {
            return;
        }

        var part = done + ".part";
        var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset > file.Size)
        {
            File.Delete(part);
            offset = 0;
        }

        for (var attempt = 1; offset < file.Size; attempt++)
        {
            try
            {
                offset = await ReceiveAsync(job, file, part, offset, finishedBytes, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && IsTransient(ex))
            {
                // Hängt die Verbindung oder bricht sie ab: kurz warten und ab dem letzten Byte neu verbinden.
                offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                logger.LogInformation("Download {Id}: {Reason} Versuch {Attempt} von {Max}, weiter ab Byte {Offset}.",
                    job.Id, ex.Message, attempt + 1, MaxAttempts, offset);
                job.BytesPerSecond = 0;
                job.BytesDone = finishedBytes + offset;
                job.RaiseChanged();
                await Task.Delay(RetryDelay * Math.Pow(2, attempt - 1), ct).ConfigureAwait(false);
                continue;
            }

            if (offset < file.Size)
            {
                break;
            }
        }

        if (offset != file.Size)
        {
            throw new InvalidDataException($"{file.FileName} ist unvollständig ({offset} von {file.Size} Bytes). \"Fortsetzen\" lädt den Rest.");
        }

        if (file.Sha256 is { Length: > 0 } expected)
        {
            job.State = DownloadState.Verifying;
            job.BytesPerSecond = 0;
            job.RaiseChanged();
            var actual = await HashAsync(part, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException($"Die Prüfsumme von {file.FileName} stimmt nicht. Die Datei ist beschädigt und wurde gelöscht. Bitte nochmals herunterladen.");
            }

            job.State = DownloadState.Running;
        }

        File.Move(part, done);
    }

    /// <summary>Lädt ab <paramref name="offset"/> und hängt an die .part-Datei an. Gibt die neue Länge zurück.</summary>
    private async Task<long> ReceiveAsync(DownloadJob job, HfFile file, string part, long offset, long finishedBytes, CancellationToken ct)
    {
        using var response = await client.OpenFileAsync(job.Plan.Repo, file.Path, offset, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Die Datei ist schon ganz da (oder der Server kennt die Länge anders): von vorne.
            File.Delete(part);
            return await ReceiveAsync(job, file, part, 0, finishedBytes, ct).ConfigureAwait(false);
        }

        if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            // Der Server kann nicht fortsetzen und schickt die ganze Datei.
            logger.LogInformation("Server ignoriert Range für {File}. Download beginnt von vorne.", file.FileName);
            offset = 0;
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        var position = offset;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastBytes = position;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            int read;
            stall.CancelAfter(StallTimeout);
            try
            {
                read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Seit {StallTimeout.TotalSeconds:F0} s kommen keine Daten mehr.");
            }

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            position += read;

            var now = clock.Elapsed;
            if (now - lastReport >= ReportInterval)
            {
                var speed = (position - lastBytes) / (now - lastReport).TotalSeconds;
                job.BytesPerSecond = job.BytesPerSecond <= 0 ? speed : (job.BytesPerSecond * 0.7) + (speed * 0.3);
                job.BytesDone = finishedBytes + position;
                job.RaiseChanged();
                lastReport = now;
                lastBytes = position;
            }
        }

        job.BytesDone = finishedBytes + position;
        return position;
    }

    /// <summary>Lohnt sich ein neuer Versuch? Ja bei Netzwerk-Problemen, nein bei "nicht gefunden" oder "kein Zugriff".</summary>
    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException or HttpRequestException
        || (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException)
        || (ex is HuggingFaceException { InnerException: HttpRequestException });

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Alle Dateien sind geprüft: nach models/&lt;id&gt;/ verschieben und model.json schreiben.</summary>
    private LocalModel Finish(DownloadJob job)
    {
        var folder = library.CreateFolder(job.Id);
        foreach (var file in job.Plan.AllFiles)
        {
            File.Move(Path.Combine(job.StagingDirectory, file.FileName), Path.Combine(folder, file.FileName));
        }

        var first = job.Plan.ModelFiles[0];
        var model = library.WriteManifestFor(folder, first.FileName, job.Plan.Projector?.FileName,
            $"huggingface:{job.Plan.Repo}/{first.Path}", job.Plan.License);
        TryDeleteDirectory(job.StagingDirectory);
        if (Directory.Exists(library.DownloadsDirectory) && !Directory.EnumerateFileSystemEntries(library.DownloadsDirectory).Any())
        {
            TryDeleteDirectory(library.DownloadsDirectory);
        }

        return model;
    }

    /// <summary>Null, wenn genug Platz frei ist. Sonst die Meldung für die Oberfläche.</summary>
    private string? FreeSpaceProblem(DownloadJob job)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(library.Directory));
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var missing = job.TotalBytes - BytesOnDisk(job.StagingDirectory, job.Plan);
        var free = new DriveInfo(root).AvailableFreeSpace;
        return free - missing >= FreeSpaceReserve
            ? null
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Zu wenig Speicherplatz auf {root}: Der Download braucht noch {missing / 1e9:F1} GB, frei sind {free / 1e9:F1} GB (2 GB sollen frei bleiben).");
    }

    private string UniqueId(string id)
    {
        for (var n = 2; ; n++)
        {
            var candidate = $"{id}-{n}";
            if (Find(candidate) is null && !Directory.Exists(Path.Combine(library.Directory, candidate)) && !Directory.Exists(Path.Combine(library.DownloadsDirectory, candidate)))
            {
                return candidate;
            }
        }
    }

    private DownloadJob? Find(string id)
    {
        lock (_jobs)
        {
            return _jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void Add(DownloadJob job)
    {
        lock (_jobs)
        {
            _jobs.Add(job);
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(DownloadJob job)
    {
        lock (_jobs)
        {
            _jobs.Remove(job);
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Download-Ordner {Folder} konnte nicht gelöscht werden.", folder);
        }
    }
}
