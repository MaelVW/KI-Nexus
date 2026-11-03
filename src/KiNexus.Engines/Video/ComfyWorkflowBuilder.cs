using System.Text.Json.Nodes;
using KiNexus.Core.Video;

namespace KiNexus.Engines.Video;

/// <summary>
/// Füllt die Workflow-Vorlage für ComfyUI aus (Prompt, Grösse, Seed, Modelldateien …).
/// Die Knoten findet er über ihren Titel (KN_…), nicht über die Nummer. So überlebt die Vorlage kleine Änderungen.
/// </summary>
public static class ComfyWorkflowBuilder
{
    public const string TextToVideoTemplate = "ltx23-text-zu-video.json";

    /// <param name="filenamePrefix">Name der Videodatei ohne Endung. ComfyUI hängt "_00001_.mp4" an.</param>
    /// <param name="startImageName">Bild zu Video: Dateiname des Startbilds im Eingabe-Ordner von ComfyUI.</param>
    public static JsonObject Build(VideoRequest request, VideoModelSet models, string filenamePrefix, string? startImageName = null)
    {
        if (request.Problem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        var workflow = LoadTemplate(TextToVideoTemplate);
        Inputs(workflow, "KN_Model")["unet_name"] = models.Model;
        var textEncoder = Inputs(workflow, "KN_TextEncoder");
        textEncoder["clip_name1"] = models.TextEncoder;
        textEncoder["clip_name2"] = models.TextProjection;
        Inputs(workflow, "KN_VideoVae")["vae_name"] = models.VideoVae;
        Inputs(workflow, "KN_AudioVae")["ckpt_name"] = models.AudioVae;
        Inputs(workflow, "KN_Prompt")["text"] = request.Prompt.Trim();
        Inputs(workflow, "KN_Conditioning")["frame_rate"] = request.FrameRate;
        var videoLatent = Inputs(workflow, "KN_VideoLatent");
        videoLatent["width"] = request.Width;
        videoLatent["height"] = request.Height;
        videoLatent["length"] = request.Frames;
        var audioLatent = Inputs(workflow, "KN_AudioLatent");
        audioLatent["frames_number"] = request.Frames;
        audioLatent["frame_rate"] = request.FrameRate;
        Inputs(workflow, "KN_Seed")["noise_seed"] = request.Seed;
        SetSchedule(workflow, models);
        Inputs(workflow, "KN_Guider")["cfg"] = models.Guidance;
        Inputs(workflow, "KN_CreateVideo")["fps"] = request.FrameRate;
        Inputs(workflow, "KN_Save")["filename_prefix"] = filenamePrefix;

        // Vorschaubild für die Galerie: ein Bild aus der Mitte des Videos.
        Inputs(workflow, "KN_Thumbnail")["batch_index"] = request.Frames / 2;
        Inputs(workflow, "KN_SaveThumbnail")["filename_prefix"] = filenamePrefix + "_vorschau";

        SetNegativePrompt(workflow, string.IsNullOrWhiteSpace(request.NegativePrompt) ? models.DefaultNegativePrompt : request.NegativePrompt, models.Guidance);
        if (!request.WithAudio)
        {
            RemoveAudio(workflow);
        }

        if (request.StartImagePath is not null)
        {
            AddStartImage(workflow, request, startImageName ?? throw new ArgumentException("Für Bild zu Video fehlt der Name des Startbilds.", nameof(startImageName)));
        }

        return workflow;
    }

    /// <summary>
    /// Bild-Auftrag (Bauplan Bild, B1): eine Vorlage pro Modell-Art. Alle Bilder entstehen in einem Durchgang (batch_size),
    /// so lädt das Modell nur einmal. Gleicher Seed, gleicher Prompt und gleiche Anzahl ergeben dieselben Bilder.
    /// </summary>
    /// <param name="filenamePrefix">Name der Bilddateien ohne Endung, z.B. "bilder/2026-11-03_101500_seed42".</param>
    public static JsonObject BuildImage(ImageRequest request, ImageModelSet models, string filenamePrefix)
    {
        if (request.Problem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        var workflow = LoadTemplate(TemplateFor(models.Family));
        Inputs(workflow, "KN_Model")["unet_name"] = models.Model;
        Inputs(workflow, "KN_TextEncoder")["clip_name"] = models.TextEncoder;
        Inputs(workflow, "KN_Vae")["vae_name"] = models.Vae;
        Inputs(workflow, "KN_Prompt")["text"] = request.Prompt.Trim();
        var latent = Inputs(workflow, "KN_Latent");
        latent["width"] = request.Width;
        latent["height"] = request.Height;
        latent["batch_size"] = request.Count;
        Inputs(workflow, "KN_Save")["filename_prefix"] = filenamePrefix;
        switch (models.Family)
        {
            case ImageModelFamily.Flux2Klein:
                var sigmas = Inputs(workflow, "KN_Sigmas");
                sigmas["steps"] = models.Steps;
                sigmas["width"] = request.Width;
                sigmas["height"] = request.Height;
                Inputs(workflow, "KN_Seed")["noise_seed"] = request.Seed;
                Inputs(workflow, "KN_Guider")["cfg"] = models.Guidance;
                break;
            case ImageModelFamily.ZImageTurbo:
                var denoise = Inputs(workflow, "KN_Denoise");
                denoise["seed"] = request.Seed;
                denoise["steps"] = models.Steps;
                denoise["cfg"] = models.Guidance;
                break;
        }

        return workflow;
    }

    public static string TemplateFor(ImageModelFamily family) => family switch
    {
        ImageModelFamily.Flux2Klein => "flux2-klein-bild.json",
        ImageModelFamily.ZImageTurbo => "z-image-bild.json",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unbekannte Art von Bildmodell."),
    };

    /// <summary>
    /// Bild zu Video: Startbild laden, auf die Grösse des Videos zuschneiden (Mitte), leicht komprimieren wie bei Lightricks
    /// (sonst bleibt das Bild oft stehen) und als erstes Bild ins Latent setzen. Das Modell rechnet von dort weiter.
    /// </summary>
    private static void AddStartImage(JsonObject workflow, VideoRequest request, string imageName)
    {
        var vae = NodeId(workflow, "KN_VideoVae");
        var videoLatent = NodeId(workflow, "KN_VideoLatent");
        AddNode(workflow, "30", "LoadImage", "KN_StartImage", new JsonObject { ["image"] = imageName });
        AddNode(workflow, "31", "ImageScale", "KN_StartScale", new JsonObject
        {
            ["image"] = new JsonArray("30", 0),
            ["upscale_method"] = "lanczos",
            ["width"] = request.Width,
            ["height"] = request.Height,
            ["crop"] = "center",
        });
        AddNode(workflow, "32", "LTXVPreprocess", "KN_StartPreprocess", new JsonObject { ["image"] = new JsonArray("31", 0), ["img_compression"] = 35 });
        AddNode(workflow, "33", "LTXVImgToVideoInplace", "KN_ImageToVideo", new JsonObject
        {
            ["vae"] = new JsonArray(vae, 0),
            ["image"] = new JsonArray("32", 0),
            ["latent"] = new JsonArray(videoLatent, 0),
            ["strength"] = 1.0,
            ["bypass"] = false,
        });
        Inputs(workflow, "KN_ConcatAV")["video_latent"] = new JsonArray("33", 0);
    }

    private static void AddNode(JsonObject workflow, string id, string classType, string title, JsonObject inputs)
    {
        if (workflow.ContainsKey(id))
        {
            throw new InvalidOperationException($"Die Vorlage hat schon einen Knoten {id}.");
        }

        workflow[id] = new JsonObject { ["class_type"] = classType, ["_meta"] = new JsonObject { ["title"] = title }, ["inputs"] = inputs };
    }

    /// <summary>Knoten-Nummer → Titel, z.B. "15" → "KN_Denoise". Damit übersetzt das Backend die Meldungen von ComfyUI.</summary>
    public static IReadOnlyDictionary<string, string> Titles(JsonObject workflow) =>
        workflow.Where(node => Title(node.Value) is not null).ToDictionary(node => node.Key, node => Title(node.Value)!);

    /// <summary>Die Nummer des Knotens mit diesem Titel.</summary>
    public static string NodeId(JsonObject workflow, string title) =>
        workflow.FirstOrDefault(node => Title(node.Value) == title).Key
        ?? throw new InvalidOperationException($"Die Vorlage hat keinen Knoten {title}.");

    internal static JsonObject LoadTemplate(string name)
    {
        using var stream = typeof(ComfyWorkflowBuilder).Assembly.GetManifestResourceStream("KiNexus.Video." + name)
            ?? throw new InvalidOperationException($"Workflow-Vorlage {name} fehlt in der DLL.");
        return JsonNode.Parse(stream)?.AsObject() ?? throw new InvalidOperationException($"Workflow-Vorlage {name} ist leer.");
    }

    /// <summary>
    /// distilled: feste Rausch-Stufen (ManualSigmas). dev: ComfyUI rechnet die Stufen selbst (LTXVScheduler,
    /// Werte wie im Workflow von Unsloth), passend zur Grösse des Latents.
    /// </summary>
    private static void SetSchedule(JsonObject workflow, VideoModelSet models)
    {
        if (models.Sigmas is { } sigmas)
        {
            Inputs(workflow, "KN_Sigmas")["sigmas"] = sigmas;
            return;
        }

        workflow[NodeId(workflow, "KN_Sigmas")] = new JsonObject
        {
            ["class_type"] = "LTXVScheduler",
            ["_meta"] = new JsonObject { ["title"] = "KN_Sigmas" },
            ["inputs"] = new JsonObject
            {
                ["steps"] = models.SchedulerSteps,
                ["max_shift"] = 2.05,
                ["base_shift"] = 0.95,
                ["stretch"] = true,
                ["terminal"] = 0.1,
                ["latent"] = new JsonArray(NodeId(workflow, "KN_ConcatAV"), 0),
            },
        };
    }

    /// <summary>
    /// Mit CFG 1 rechnet das Modell ohne Negativ-Prompt. Statt ihn mit Gemma zu lesen (ca. 4 s), gibt es dann
    /// "keine Bedingung" (ConditioningZeroOut). Nur mit CFG über 1 und Text wird er wirklich gelesen.
    /// </summary>
    private static void SetNegativePrompt(JsonObject workflow, string? negativePrompt, double guidance)
    {
        if (guidance > 1 && !string.IsNullOrWhiteSpace(negativePrompt))
        {
            Inputs(workflow, "KN_NegativePrompt")["text"] = negativePrompt.Trim();
            return;
        }

        var id = NodeId(workflow, "KN_NegativePrompt");
        workflow[id] = new JsonObject
        {
            ["class_type"] = "ConditioningZeroOut",
            ["_meta"] = new JsonObject { ["title"] = "KN_NegativePrompt" },
            ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray(NodeId(workflow, "KN_Prompt"), 0) },
        };
    }

    /// <summary>Ohne Ton: Die Tonspur wird nicht dekodiert und nicht ins Video gepackt (das Modell rechnet sie trotzdem mit).</summary>
    private static void RemoveAudio(JsonObject workflow)
    {
        workflow.Remove(NodeId(workflow, "KN_DecodeAudio"));
        Inputs(workflow, "KN_CreateVideo").Remove("audio");
    }

    private static JsonObject Inputs(JsonObject workflow, string title) =>
        workflow[NodeId(workflow, title)]!["inputs"]!.AsObject();

    private static string? Title(JsonNode? node) => node?["_meta"]?["title"]?.GetValue<string>();
}
