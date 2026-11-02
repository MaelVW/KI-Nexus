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
    public static JsonObject Build(VideoRequest request, VideoModelSet models, string filenamePrefix)
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
        Inputs(workflow, "KN_Sigmas")["sigmas"] = models.Sigmas;
        Inputs(workflow, "KN_Guider")["cfg"] = models.Guidance;
        Inputs(workflow, "KN_CreateVideo")["fps"] = request.FrameRate;
        Inputs(workflow, "KN_Save")["filename_prefix"] = filenamePrefix;

        SetNegativePrompt(workflow, request.NegativePrompt, models.Guidance);
        if (!request.WithAudio)
        {
            RemoveAudio(workflow);
        }

        return workflow;
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
