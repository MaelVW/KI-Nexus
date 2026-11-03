namespace KiNexus.Core.Video;

/// <summary>Welche Art Bildmodell. Jede Art hat eine eigene Workflow-Vorlage.</summary>
public enum ImageModelFamily
{
    /// <summary>FLUX.2 klein (Black Forest Labs): Flux2Scheduler, CFGGuider.</summary>
    Flux2Klein,

    /// <summary>Z-Image Turbo (Tongyi/Alibaba): KSampler mit res_multistep und AuraFlow-Verschiebung.</summary>
    ZImageTurbo,
}

/// <summary>
/// Alle Dateien, die ein Bildmodell braucht, dazu die Einstellungen fürs Entrauschen (Bauplan Bild, B1).
/// Die Dateinamen gelten in den Unterordnern von models/image/ (diffusion_models, text_encoders, vae).
/// Beide Modelle lesen den Prompt mit demselben Text-Encoder (Qwen3 4B).
/// </summary>
/// <param name="Steps">Schritte beim Entrauschen. Beide Modelle sind "destilliert" und brauchen nur wenige.</param>
/// <param name="Guidance">CFG. Destillierte Modelle rechnen mit 1, also ohne Negativ-Prompt.</param>
public sealed record ImageModelSet(
    string Name,
    ImageModelFamily Family,
    string Model,
    string TextEncoder,
    string Vae,
    int Steps,
    double Guidance = 1.0)
{
    /// <summary>FLUX.2 klein 4B (Apache 2.0), 4 Schritte. Gemessen in B0 (docs/11-Bild.md).</summary>
    public static ImageModelSet Flux2Klein4B { get; } = new(
        "FLUX.2 klein 4B (Q8_0)",
        ImageModelFamily.Flux2Klein,
        "flux-2-klein-4b-Q8_0.gguf",
        "qwen_3_4b.safetensors",
        "flux2-vae.safetensors",
        Steps: 4);

    /// <summary>Z-Image Turbo 6B (Apache 2.0), 8 Schritte. Gemessen in B0 (docs/11-Bild.md).</summary>
    public static ImageModelSet ZImageTurbo { get; } = new(
        "Z-Image Turbo (Q8_0)",
        ImageModelFamily.ZImageTurbo,
        "z-image-turbo-Q8_0.gguf",
        "qwen_3_4b.safetensors",
        "ae.safetensors",
        Steps: 8);

    /// <summary>
    /// FLUX.2 klein 9B (Lizenz: FLUX Non-Commercial), 4 Schritte. Braucht Qwen3 8B statt 4B als Text-Encoder.
    /// Nicht eingebaut und nicht gemessen: Das Studio bietet es an, wenn es in models/ liegt.
    /// </summary>
    public static ImageModelSet Flux2Klein9B { get; } = new(
        "FLUX.2 klein 9B",
        ImageModelFamily.Flux2Klein,
        "flux-2-klein-9b.gguf",
        "qwen_3_8b_fp8mixed.safetensors",
        "flux2-vae.safetensors",
        Steps: 4);

    /// <summary>Die eingebauten Modelle (setup-video.ps1 -Images lädt nur das erste).</summary>
    public static IReadOnlyList<ImageModelSet> Known { get; } = [Flux2Klein4B, ZImageTurbo];

    /// <summary>
    /// Ordner des Modells, wenn es nicht in models/image/diffusion_models liegt (z.B. models/&lt;ordner&gt;/ von Hugging Face).
    /// Text-Encoder und VAE kommen trotzdem aus models/image.
    /// </summary>
    public string? ModelFolder { get; init; }

    /// <summary>Kann sich das Modell an Referenzbildern orientieren? FLUX.2 klein ja (ReferenceLatent), Z-Image Turbo nicht.</summary>
    public bool SupportsReferences => Family == ImageModelFamily.Flux2Klein;

    /// <summary>Wo jede Datei liegen muss. Die erste ist das Modell selbst.</summary>
    public IReadOnlyList<string> Files(string imageModelsDirectory) =>
    [
        Path.Combine(ModelFolder ?? Path.Combine(imageModelsDirectory, "diffusion_models"), Model),
        Path.Combine(imageModelsDirectory, "text_encoders", TextEncoder),
        Path.Combine(imageModelsDirectory, "vae", Vae),
    ];

    public IReadOnlyList<string> MissingFiles(string imageModelsDirectory) =>
        [.. Files(imageModelsDirectory).Where(file => !File.Exists(file))];
}
