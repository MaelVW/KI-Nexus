using System.Text.Json.Nodes;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using KiNexus.Core.Tests;
using KiNexus.Core.Video;
using KiNexus.Engines.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

public class VideoTests
{
    private static readonly VideoModelSet Models = VideoModelSet.Ltx23Distilled;

    // ----------------------------------------------------------------- Workflow

    [Fact]
    public void Builder_FillsTemplateByTitle()
    {
        var request = new VideoRequest("  A fox jumps.  ", Width: 512, Height: 320, Frames: 49, FrameRate: 24, Seed: 7);
        var workflow = ComfyWorkflowBuilder.Build(request, Models, "2026-11-02_120000_seed7");

        Assert.Equal("A fox jumps.", Inputs(workflow, "KN_Prompt")["text"]!.GetValue<string>());
        Assert.Equal(512, Inputs(workflow, "KN_VideoLatent")["width"]!.GetValue<int>());
        Assert.Equal(320, Inputs(workflow, "KN_VideoLatent")["height"]!.GetValue<int>());
        Assert.Equal(49, Inputs(workflow, "KN_VideoLatent")["length"]!.GetValue<int>());
        Assert.Equal(49, Inputs(workflow, "KN_AudioLatent")["frames_number"]!.GetValue<int>());
        Assert.Equal(24.0, Inputs(workflow, "KN_Conditioning")["frame_rate"]!.GetValue<double>());
        Assert.Equal(24.0, Inputs(workflow, "KN_CreateVideo")["fps"]!.GetValue<double>());
        Assert.Equal(7, Inputs(workflow, "KN_Seed")["noise_seed"]!.GetValue<long>());
        Assert.Equal(Models.Model, Inputs(workflow, "KN_Model")["unet_name"]!.GetValue<string>());
        Assert.Equal(Models.TextEncoder, Inputs(workflow, "KN_TextEncoder")["clip_name1"]!.GetValue<string>());
        Assert.Equal(Models.TextProjection, Inputs(workflow, "KN_TextEncoder")["clip_name2"]!.GetValue<string>());
        Assert.Equal(Models.Sigmas, Inputs(workflow, "KN_Sigmas")["sigmas"]!.GetValue<string>());
        Assert.Equal("2026-11-02_120000_seed7", Inputs(workflow, "KN_Save")["filename_prefix"]!.GetValue<string>());
        Assert.Equal("h264", Inputs(workflow, "KN_Save")["codec"]!.GetValue<string>());
        Assert.Equal(24, Inputs(workflow, "KN_Thumbnail")["batch_index"]!.GetValue<int>());
        Assert.Equal("2026-11-02_120000_seed7_vorschau", Inputs(workflow, "KN_SaveThumbnail")["filename_prefix"]!.GetValue<string>());
        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Builder_SkipsNegativePromptWithCfgOne()
    {
        var workflow = ComfyWorkflowBuilder.Build(new VideoRequest("x", NegativePrompt: "blurry"), Models, "p");
        var negative = workflow[ComfyWorkflowBuilder.NodeId(workflow, "KN_NegativePrompt")]!;

        Assert.Equal("ConditioningZeroOut", negative["class_type"]!.GetValue<string>());
        Assert.Equal(ComfyWorkflowBuilder.NodeId(workflow, "KN_Prompt"), negative["inputs"]!["conditioning"]![0]!.GetValue<string>());
        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Builder_ReadsNegativePromptWithHigherCfg()
    {
        var workflow = ComfyWorkflowBuilder.Build(new VideoRequest("x", NegativePrompt: " blurry "), Models with { Guidance = 4 }, "p");

        Assert.Equal("CLIPTextEncode", workflow[ComfyWorkflowBuilder.NodeId(workflow, "KN_NegativePrompt")]!["class_type"]!.GetValue<string>());
        Assert.Equal("blurry", Inputs(workflow, "KN_NegativePrompt")["text"]!.GetValue<string>());
        Assert.Equal(4.0, Inputs(workflow, "KN_Guider")["cfg"]!.GetValue<double>());
    }

    [Fact]
    public void Builder_WithoutAudio_DropsAudioDecoding()
    {
        var workflow = ComfyWorkflowBuilder.Build(new VideoRequest("x", WithAudio: false), Models, "p");

        Assert.DoesNotContain("KN_DecodeAudio", ComfyWorkflowBuilder.Titles(workflow).Values);
        Assert.False(Inputs(workflow, "KN_CreateVideo").ContainsKey("audio"));
        AssertAllLinksExist(workflow);
    }

    [Fact]
    public void Builder_RejectsInvalidRequest()
    {
        Assert.Throws<ArgumentException>(() => ComfyWorkflowBuilder.Build(new VideoRequest("x", Width: 500), Models, "p"));
    }

    // ----------------------------------------------------------------- Protokoll (echte Meldungen von ComfyUI 0.43)

    [Fact]
    public void Parse_ReadsRealMessages()
    {
        Assert.Equal(new ComfyExecuting("p1", "15"),
            ComfyProtocol.Parse("""{"type": "executing", "data": {"node": "15", "display_node": "15", "prompt_id": "p1"}}"""));
        Assert.Equal(new ComfyProgress("p1", "15", 3, 8),
            ComfyProtocol.Parse("""{"type": "progress", "data": {"value": 3, "max": 8, "prompt_id": "p1", "node": "15"}}"""));
        Assert.Equal(new ComfySuccess("p1"), ComfyProtocol.Parse("""{"type": "execution_success", "data": {"prompt_id": "p1", "timestamp": 1}}"""));
        Assert.Equal(new ComfyInterrupted("p1"), ComfyProtocol.Parse(
            """{"type": "execution_interrupted", "data": {"prompt_id": "p1", "node_id": "15", "node_type": "SamplerCustomAdvanced", "executed": ["10"]}}"""));
        Assert.Equal(new ComfyError("p1", "VAEDecodeTiled", "CUDA out of memory."), ComfyProtocol.Parse(
            """{"type": "execution_error", "data": {"prompt_id": "p1", "node_id": "17", "node_type": "VAEDecodeTiled", "exception_message": "CUDA out of memory.\n"}}"""));
        Assert.Equal(new ComfyOther("status"), ComfyProtocol.Parse("""{"type": "status", "data": {"status": {"exec_info": {"queue_remaining": 0}}}}"""));
        Assert.Equal(new ComfyOther("progress_state"), ComfyProtocol.Parse("""{"type": "progress_state", "data": {"prompt_id": "p1", "nodes": {}}}"""));
        Assert.Null(ComfyProtocol.Parse(" "));
    }

    [Fact]
    public void Parse_FindsTheSavedVideo()
    {
        var message = ComfyProtocol.Parse("""
            {"type": "executed", "data": {"node": "20", "display_node": "20", "output": {"images": [{"filename": "cap-ok_00001_.mp4",
             "subfolder": "v0", "type": "output", "metadata": {"kind": "video", "width": 256, "height": 256}}], "animated": [true]}, "prompt_id": "p1"}}
            """);

        var executed = Assert.IsType<ComfyExecuted>(message);
        Assert.Equal("20", executed.Node);
        var file = Assert.Single(executed.Files);
        Assert.Equal(new ComfyFile("cap-ok_00001_.mp4", "v0", "output"), file);
        Assert.Equal(@"C:\data\videos\roh\v0\cap-ok_00001_.mp4", ComfyProtocol.FullPath(file, @"C:\data\videos\roh"));
    }

    [Fact]
    public void QueueResponse_ExplainsRejection()
    {
        Assert.Equal(("abc", null), ComfyProtocol.ParseQueueResponse(200, """{"prompt_id": "abc", "number": 0, "node_errors": {}}"""));

        var (id, error) = ComfyProtocol.ParseQueueResponse(400,
            """{"error": {"type": "missing_node_type", "message": "Node 'Nope' not found."}, "node_errors": {}}""");
        Assert.Null(id);
        Assert.Contains("Node 'Nope' not found.", error, StringComparison.Ordinal);

        var (_, fileError) = ComfyProtocol.ParseQueueResponse(400, """
            {"error": {"type": "prompt_outputs_failed_validation", "message": "Prompt outputs failed validation"},
             "node_errors": {"1": {"errors": [{"type": "value_not_in_list", "message": "Value not in list", "details": "unet_name: 'x.gguf' not in []"}]}}}
            """);
        Assert.Contains("unet_name: 'x.gguf' not in []", fileError, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", ComfyProtocol.ParseQueueResponse(500, "kein json").Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_MakesMemoryErrorsReadable()
    {
        Assert.Contains("Zu wenig Speicher", ComfyProtocol.Explain(new ComfyError("p", "KSampler", "CUDA out of memory. Tried to allocate")), StringComparison.Ordinal);
        Assert.Equal("Fehler in ComfyUI (VAELoader): kaputt", ComfyProtocol.Explain(new ComfyError("p", "VAELoader", "kaputt")));
    }

    [Fact]
    public void Phases_FollowTheTemplateTitles()
    {
        var titles = ComfyWorkflowBuilder.Titles(ComfyWorkflowBuilder.Build(new VideoRequest("x"), Models, "p")).Values;
        var phases = titles.Select(ComfyProtocol.PhaseFor).OfType<VideoPhase>().Distinct().ToList();

        // Jeder Abschnitt ausser "Starting" kommt in der Vorlage vor, und jeder hat einen Text.
        Assert.Equal(Enum.GetValues<VideoPhase>().Length - 1, phases.Count);
        Assert.All(Enum.GetValues<VideoPhase>(), phase => Assert.EndsWith("…", ComfyProtocol.Describe(phase), StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------- Startbefehl und Einrichtung

    [Fact]
    public void Command_ListensOnlyLocallyAndOffline()
    {
        var installation = ComfyInstallation.For(@"C:\Projekt\engines");
        var options = new ComfyOptions(@"C:\Projekt\models\video", @"C:\Projekt\data\videos\roh", @"C:\Projekt\data\comfyui", @"C:\log.txt", Port: 8199);
        var arguments = ComfyCommand.Arguments(installation, options);

        Assert.Equal(@"C:\Projekt\engines\comfyui\.venv\Scripts\python.exe", installation.PythonExe);
        Assert.Equal(["-s", @"C:\Projekt\engines\comfyui\main.py"], arguments.Take(2));
        Assert.Equal("127.0.0.1", arguments[arguments.ToList().IndexOf("--listen") + 1]);
        Assert.Equal("8199", arguments[arguments.ToList().IndexOf("--port") + 1]);
        Assert.Contains("--offline", arguments);
        Assert.Contains("--cache-none", arguments);
        Assert.Contains(@"C:\Projekt\data\comfyui\input", arguments);
        Assert.DoesNotContain("--cache-none", ComfyCommand.Arguments(installation, options with { CacheNone = false }));
    }

    [Fact]
    public void PathsYaml_PointsToModelsFolder()
    {
        var yaml = ComfyCommand.PathsYaml(@"C:\Projekt\models\video");

        Assert.Contains("""base_path: "C:/Projekt/models/video" """.TrimEnd(), yaml, StringComparison.Ordinal);
        Assert.Contains("checkpoints: vae", yaml, StringComparison.Ordinal);
        Assert.Contains("diffusion_models: diffusion_models", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Installation_ExplainsTheSetupScript()
    {
        using var root = new TempDirectory();

        Assert.Contains("setup-video.ps1", ComfyInstallation.For(root.Path).Problem(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backend_ReportsMissingModelsWithoutStarting()
    {
        using var root = new TempDirectory();
        using var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        await using var backend = new ComfyVideoBackend(supervisor, NullLogger<ComfyVideoBackend>.Instance, ComfyInstallation.For(root.Path),
            new ComfyOptions(root.Combine("models"), root.Combine("out"), root.Combine("work"), root.Combine("video.log")));

        var events = await Collect(backend.GenerateAsync(new VideoRequest("x"), Models, TestContext.Current.CancellationToken));

        var failed = Assert.IsType<VideoFailed>(Assert.Single(events));
        Assert.Contains("setup-video.ps1 -Models", failed.Message, StringComparison.Ordinal);
        Assert.Equal(VideoBackendState.Stopped, backend.State);
        Assert.Contains("setup-video.ps1", backend.SetupProblem(Models), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- Echter Testlauf (nur auf Wunsch, ca. 2–3 Minuten)

    /// <summary>
    /// Erzeugt ein echtes Video über die Schnittstelle, mit ComfyUI und LTX-2.3. Läuft nur mit KINEXUS_VIDEO_TEST=1,
    /// weil es Minuten dauert, die Grafikkarte braucht und die Modelle (24 GB) voraussetzt.
    /// </summary>
    [Fact]
    public async Task RealRun_CreatesVideoThroughTheInterface()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("KINEXUS_VIDEO_TEST") == "1", "Nur mit KINEXUS_VIDEO_TEST=1 (dauert Minuten).");
        var paths = AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(AppPaths.RootEnvironmentVariable));
        using var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        await using var backend = new ComfyVideoBackend(supervisor, NullLogger<ComfyVideoBackend>.Instance,
            ComfyInstallation.For(Path.Combine(paths.Root, "engines")),
            new ComfyOptions(Path.Combine(paths.Root, "models", "video"), Path.Combine(paths.DataDirectory, "videos", "roh"),
                Path.Combine(paths.DataDirectory, "comfyui"), Path.Combine(paths.LogsDirectory, "video.log")));
        var request = new VideoRequest("A small red ball rolls across a wooden table, soft clicking sound.", Width: 512, Height: 320, Frames: 49, Seed: 11);

        var events = await Collect(backend.GenerateAsync(request, Models, TestContext.Current.CancellationToken));
        foreach (var e in events)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(e.ToString());
        }

        var done = Assert.IsType<VideoDone>(events[^1]);
        Assert.True(new FileInfo(done.VideoPath).Length > 50_000);
        Assert.True(File.Exists(done.ThumbnailPath), "Vorschaubild fehlt.");
        Assert.Equal(Enumerable.Range(1, 8), events.OfType<VideoStep>().Select(s => s.Step));
        Assert.Contains(events, e => e is VideoStage { Phase: VideoPhase.Denoising });

        // Zweiter Auftrag: nach dem ersten Schritt abbrechen.
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var second = new List<VideoProgress>();
        await foreach (var progress in backend.GenerateAsync(request with { Seed = 12 }, Models, cancel.Token))
        {
            second.Add(progress);
            if (progress is VideoStep)
            {
                await cancel.CancelAsync();
            }
        }

        Assert.IsType<VideoCancelled>(second[^1]);
        Assert.Equal(VideoBackendState.Ready, backend.State);
        await backend.FreeMemoryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<VideoProgress>> Collect(IAsyncEnumerable<VideoProgress> source)
    {
        var list = new List<VideoProgress>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static JsonObject Inputs(JsonObject workflow, string title) =>
        workflow[ComfyWorkflowBuilder.NodeId(workflow, title)]!["inputs"]!.AsObject();

    /// <summary>Jede Verbindung ["nummer", ausgang] muss auf einen Knoten zeigen, den es gibt.</summary>
    private static void AssertAllLinksExist(JsonObject workflow)
    {
        foreach (var (_, node) in workflow)
        {
            foreach (var (_, value) in node!["inputs"]!.AsObject())
            {
                if (value is JsonArray { Count: 2 } link && link[0] is JsonValue id && id.TryGetValue<string>(out var target))
                {
                    Assert.True(workflow.ContainsKey(target), $"Verbindung zu Knoten {target} fehlt.");
                }
            }
        }
    }
}
