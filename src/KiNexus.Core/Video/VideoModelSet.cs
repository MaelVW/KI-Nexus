namespace KiNexus.Core.Video;

/// <summary>
/// Alle Dateien, die ein LTX-Videomodell braucht, dazu die Einstellungen fürs Entrauschen.
/// Die Dateinamen gelten in den Unterordnern von models/video/ (gleich wie bei ComfyUI).
/// </summary>
/// <param name="Model">Der Kern (Diffusion) als GGUF, in diffusion_models/.</param>
/// <param name="TextEncoder">Gemma 3 12B als GGUF, in text_encoders/. Liest den Prompt.</param>
/// <param name="TextProjection">Übersetzt die Gemma-Ausgabe für den Kern, in text_encoders/.</param>
/// <param name="VideoVae">Macht aus dem Ergebnis echte Bilder, in vae/.</param>
/// <param name="AudioVae">Macht daraus Ton (mit Vocoder), in vae/.</param>
/// <param name="Sigmas">
/// Feste Rausch-Stufen fürs Entrauschen (distilled), eine pro Schritt plus 0 am Schluss.
/// Null = dev: Die Stufen rechnet ComfyUI selbst (LTXVScheduler) mit <see cref="SchedulerSteps"/> Schritten.
/// </param>
/// <param name="Guidance">CFG: wie streng das Modell dem Prompt folgt. distilled braucht 1, dev ca. 4 (rechnet dann jeden Schritt doppelt).</param>
public sealed record VideoModelSet(
    string Name,
    string Model,
    string TextEncoder,
    string TextProjection,
    string VideoVae,
    string AudioVae,
    string? Sigmas,
    double Guidance)
{
    /// <summary>Wie bei Lightricks und Unsloth für LTX-2.3 dev: 20 Schritte, CFG 4, mit Negativ-Prompt.</summary>
    public const int DevSteps = 20;
    public const double DevGuidance = 4;
    public const string DevNegativePrompt = "blurry, low quality, still frame, frames, watermark, overlay, titles, has blurbox, has subtitles";

    /// <summary>Schritte für den LTXVScheduler (nur dev, wenn <see cref="Sigmas"/> null ist).</summary>
    public int SchedulerSteps { get; init; } = DevSteps;

    /// <summary>Negativ-Prompt, wenn der Auftrag keinen hat. Wirkt nur mit CFG über 1 (dev).</summary>
    public string? DefaultNegativePrompt { get; init; }

    /// <summary>dev-Ablauf: Scheduler statt fester Stufen, CFG über 1.</summary>
    public bool IsDev => Sigmas is null;

    /// <summary>
    /// Dieselben Dateien mit dem dev-Ablauf. Text-Encoder, Projektion und VAEs teilen sich dev und distilled:
    /// Die Gewichte sind gleich (Stichproben-Vergleich in docs/10-Video.md), nur die Metadaten unterscheiden sich.
    /// </summary>
    public VideoModelSet AsDev() => this with
    {
        Sigmas = null,
        Guidance = DevGuidance,
        SchedulerSteps = DevSteps,
        DefaultNegativePrompt = DevNegativePrompt,
    };

    /// <summary>Offizielles LTX-2.3, distilled 1.1 (8 Schritte), getestet in V0 (docs/10-Video.md).</summary>
    public static VideoModelSet Ltx23Distilled { get; } = new(
        "LTX-2.3 distilled (Q4_K_M)",
        "ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf",
        "gemma-3-12b-it-qat-UD-Q4_K_XL.gguf",
        "ltx-2.3-22b-distilled_embeddings_connectors.safetensors",
        "ltx-2.3-22b-distilled_video_vae.safetensors",
        "ltx-2.3-22b-distilled_audio_vae.safetensors",
        "1.0, 0.99375, 0.9875, 0.98125, 0.975, 0.909375, 0.725, 0.421875, 0.0",
        1.0);

    /// <summary>
    /// Ordner des Modells, wenn es nicht in models/video/diffusion_models liegt (z.B. models/&lt;ordner&gt;/ von Hugging Face).
    /// ComfyUI bekommt den Ordner dann zusätzlich in kinexus-paths.yaml.
    /// </summary>
    public string? ModelFolder { get; init; }

    /// <summary>Anzahl Schritte beim Entrauschen (distilled: eine Stufe weniger als Sigmas).</summary>
    public int Steps => Sigmas is null ? SchedulerSteps : Sigmas.Split(',').Length - 1;

    /// <summary>Die Zusatzdateien (Text-Encoder, Projektion, VAEs). Fehlen sie, hilft setup-video.ps1 -Companions.</summary>
    public IReadOnlyList<string> CompanionFiles(string videoModelsDirectory) => [.. Files(videoModelsDirectory).Skip(1)];

    /// <summary>Wo jede Datei liegen muss. Die erste ist das Modell selbst.</summary>
    public IReadOnlyList<string> Files(string videoModelsDirectory) =>
    [
        Path.Combine(ModelFolder ?? Path.Combine(videoModelsDirectory, "diffusion_models"), Model),
        Path.Combine(videoModelsDirectory, "text_encoders", TextEncoder),
        Path.Combine(videoModelsDirectory, "text_encoders", TextProjection),
        Path.Combine(videoModelsDirectory, "vae", VideoVae),
        Path.Combine(videoModelsDirectory, "vae", AudioVae),
    ];

    public IReadOnlyList<string> MissingFiles(string videoModelsDirectory) =>
        [.. Files(videoModelsDirectory).Where(file => !File.Exists(file))];
}
