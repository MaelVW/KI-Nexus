namespace KiNexus.Core.Video;

public enum VideoBackendState
{
    Stopped,
    Starting,
    Ready,
    Busy,
    Failed,
}

/// <summary>Ein Schritt auf dem Weg zum Video. Die Reihenfolge ist immer dieselbe.</summary>
public enum VideoPhase
{
    Starting,
    LoadingTextEncoder,
    ReadingPrompt,
    LoadingModel,
    Denoising,
    DecodingAudio,
    DecodingVideo,
    Saving,
}

/// <summary>Eine Meldung während ein Video entsteht.</summary>
public abstract record VideoProgress;

/// <summary>Ein neuer Abschnitt, z.B. "Entrauschen".</summary>
public sealed record VideoStage(VideoPhase Phase, string Text) : VideoProgress;

/// <summary>Ein Schritt beim Entrauschen ist fertig.</summary>
public sealed record VideoStep(int Step, int TotalSteps, double ElapsedSeconds) : VideoProgress;

/// <param name="VideoPath">Die fertige MP4-Datei.</param>
/// <param name="Seconds">So lange hat der Auftrag gedauert (ohne Start des Dienstes).</param>
/// <param name="ThumbnailPath">Ein Bild aus der Mitte des Videos (PNG) für die Galerie, falls vorhanden.</param>
public sealed record VideoDone(string VideoPath, double Seconds, string? ThumbnailPath = null) : VideoProgress;

/// <summary>Ein Bild-Auftrag ist fertig.</summary>
/// <param name="ImagePaths">Die fertigen PNG-Dateien, eine pro Bild.</param>
public sealed record ImageDone(IReadOnlyList<string> ImagePaths, double Seconds) : VideoProgress;

public sealed record VideoFailed(string Message) : VideoProgress;

public sealed record VideoCancelled : VideoProgress;

/// <summary>
/// Eine Engine für Videomodelle (Bauplan Video, Kapitel 5). Die Oberfläche kennt nur diese Schnittstelle, nicht ComfyUI.
/// Es läuft immer nur ein Video gleichzeitig.
/// </summary>
public interface IVideoBackend
{
    string Name { get; }

    VideoBackendState State { get; }

    /// <summary>Kurzer Text für die Anzeige, z.B. "Bereit" oder ein Fehler.</summary>
    string StatusText { get; }

    /// <summary>Zustand oder Text hat sich geändert. Kann aus einem Hintergrund-Thread kommen.</summary>
    event EventHandler? Changed;

    /// <summary>Was fehlt noch, damit dieses Modell laufen kann (Einrichtung, Modelldateien)? Null, wenn alles da ist.</summary>
    string? SetupProblem(VideoModelSet models);

    /// <summary>Startet den Dienst und wartet, bis er antwortet. Wirft <see cref="InvalidOperationException"/>, wenn es nicht geht.</summary>
    Task StartAsync(CancellationToken ct);

    Task StopAsync();

    /// <summary>
    /// Erzeugt ein Video. Startet den Dienst bei Bedarf. Endet immer mit <see cref="VideoDone"/>, <see cref="VideoFailed"/>
    /// oder <see cref="VideoCancelled"/>. Abbrechen: den CancellationToken auslösen.
    /// </summary>
    IAsyncEnumerable<VideoProgress> GenerateAsync(VideoRequest request, VideoModelSet models, CancellationToken ct);

    /// <summary>
    /// Erzeugt Bilder (Bauplan Bild, B1). Gleicher Dienst und gleicher Ablauf wie bei Videos, endet mit <see cref="ImageDone"/>,
    /// <see cref="VideoFailed"/> oder <see cref="VideoCancelled"/>. Es läuft immer nur ein Auftrag gleichzeitig (Bild oder Video).
    /// </summary>
    IAsyncEnumerable<VideoProgress> GenerateImageAsync(ImageRequest request, ImageModelSet models, CancellationToken ct);

    /// <summary>Was fehlt noch, damit dieses Bildmodell laufen kann? Null, wenn alles da ist.</summary>
    string? SetupProblem(ImageModelSet models);

    /// <summary>Wirft die Modelle aus VRAM und RAM, damit der Chat wieder Platz hat. Der Dienst läuft weiter.</summary>
    Task FreeMemoryAsync(CancellationToken ct);
}
