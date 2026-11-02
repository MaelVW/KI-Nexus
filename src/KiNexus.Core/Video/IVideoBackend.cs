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
public sealed record VideoDone(string VideoPath, double Seconds) : VideoProgress;

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

    /// <summary>Startet den Dienst und wartet, bis er antwortet. Wirft <see cref="InvalidOperationException"/>, wenn es nicht geht.</summary>
    Task StartAsync(CancellationToken ct);

    Task StopAsync();

    /// <summary>
    /// Erzeugt ein Video. Startet den Dienst bei Bedarf. Endet immer mit <see cref="VideoDone"/>, <see cref="VideoFailed"/>
    /// oder <see cref="VideoCancelled"/>. Abbrechen: den CancellationToken auslösen.
    /// </summary>
    IAsyncEnumerable<VideoProgress> GenerateAsync(VideoRequest request, VideoModelSet models, CancellationToken ct);

    /// <summary>Wirft die Modelle aus VRAM und RAM, damit der Chat wieder Platz hat. Der Dienst läuft weiter.</summary>
    Task FreeMemoryAsync(CancellationToken ct);
}
