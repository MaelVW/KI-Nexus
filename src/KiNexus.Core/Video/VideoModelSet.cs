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
/// <param name="Sigmas">Rausch-Stufen fürs Entrauschen, eine pro Schritt plus 0 am Schluss.</param>
/// <param name="Guidance">CFG: wie streng das Modell dem Prompt folgt. Das distilled-Modell braucht 1.</param>
public sealed record VideoModelSet(
    string Name,
    string Model,
    string TextEncoder,
    string TextProjection,
    string VideoVae,
    string AudioVae,
    string Sigmas,
    double Guidance)
{
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

    /// <summary>Anzahl Schritte beim Entrauschen (eine Stufe weniger als Sigmas).</summary>
    public int Steps => Sigmas.Split(',').Length - 1;

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
