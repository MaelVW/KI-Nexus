using KiNexus.Core.Paths;
using KiNexus.Core.Tests;
using KiNexus.Core.Training;
using KiNexus.Engines.Training;

namespace KiNexus.Engines.Tests;

public class TrainingProtocolTests
{
    [Fact]
    public void Parse_ReadsAllEvents()
    {
        Assert.Equal(new TrainingStage("train", "Training: 20 Beispiele", 15),
            TrainingProtocol.Parse("""{"type":"stage","stage":"train","text":"Training: 20 Beispiele","totalSteps":15}"""));
        Assert.Equal(new TrainingDownload(1024, 4096), TrainingProtocol.Parse("""{"type":"download","done":1024,"total":4096}"""));
        Assert.Equal(new TrainingProgress(3, 15, 1, 3, 1.25, 12.5, 50, 4.2),
            TrainingProtocol.Parse("""{"type":"progress","step":3,"totalSteps":15,"epoch":1,"epochs":3,"loss":1.25,"elapsedSeconds":12.5,"etaSeconds":50,"vramGb":4.2}"""));
        Assert.Equal(new TrainingDone(@"C:\a.gguf", @"C:\b.gguf", 0.42, 95),
            TrainingProtocol.Parse("""{"type":"done","adapterGguf":"C:\\a.gguf","baseGguf":"C:\\b.gguf","finalLoss":0.42,"seconds":95}"""));
        Assert.Equal(new TrainingFailed("Zu wenig VRAM"), TrainingProtocol.Parse("""{"type":"error","message":"Zu wenig VRAM"}"""));
        Assert.IsType<TrainingCancelled>(TrainingProtocol.Parse("""{"type":"cancelled"}"""));
        Assert.Equal(new TrainingCheck(true, null, "2.14.0+cu130", "NVIDIA GeForce RTX 5070 Laptop GPU", 8.0),
            TrainingProtocol.Parse("""{"type":"check","ok":true,"torch":"2.14.0+cu130","gpu":"NVIDIA GeForce RTX 5070 Laptop GPU","vramGb":8.0}"""));
        Assert.Equal(new TrainingUnknown("neu"), TrainingProtocol.Parse("""{"type":"neu"}"""));
        Assert.Null(TrainingProtocol.Parse(""));
    }

    [Fact]
    public void Paths_And_Config_UseModelFolderAndRunName()
    {
        var model = TrainingModels.Recommended[0];
        var files = TrainingPaths.For(new AppPaths(@"C:\Projekt"), @"C:\Projekt\models", model, "mein-stil");

        Assert.Equal(@"C:\Projekt\models\hf\qwen3-1.7b", files.BaseModelDir);
        Assert.Equal(@"C:\Projekt\models\qwen3-1.7b\qwen3-1.7b-q8_0.gguf", files.BaseGguf);
        Assert.Equal(@"C:\Projekt\models\qwen3-1.7b\lora\mein-stil.gguf", files.AdapterGguf);
        Assert.Equal(@"C:\Projekt\data\training\runs\mein-stil\run.json", files.ConfigFile);

        var config = TrainingProtocol.BuildConfig(model, new TrainingOptions(Epochs: 5), files, @"C:\conv", download: true);
        Assert.Equal("Qwen/Qwen3-1.7B", config["baseRepo"]!.GetValue<string>());
        Assert.Equal(5, config["epochs"]!.GetValue<int>());
        Assert.False(config["use4Bit"]!.GetValue<bool>());
        Assert.Equal(files.DatasetFile, config["datasetPath"]!.GetValue<string>());
        Assert.Null(TrainingProtocol.BuildConfig(model, new TrainingOptions(), files, @"C:\conv", download: false)["baseRepo"]);
        Assert.Null(config["resumeAdapterDir"]);
    }

    [Fact]
    public void Config_ForAVersionResumesFromTheOldAdapter()
    {
        var model = TrainingModels.Recommended[0];
        var files = TrainingPaths.ForVersion(@"C:\Projekt\models", model, "nexi", 2, @"C:\Projekt\data\training\runs\nexi\v2");

        Assert.Equal(@"C:\Projekt\models\qwen3-1.7b\lora\nexi-v2.gguf", files.AdapterGguf);
        Assert.Equal(@"C:\Projekt\data\training\runs\nexi\v2\run.json", files.ConfigFile);

        var config = TrainingProtocol.BuildConfig(model, new TrainingOptions(Epochs: 3, LearningRate: 1e-4), files, @"C:\conv", download: false,
            resumeAdapterDir: @"C:\Projekt\data\training\runs\nexi\adapter");
        Assert.Equal(@"C:\Projekt\data\training\runs\nexi\adapter", config["resumeAdapterDir"]!.GetValue<string>());
        Assert.Equal(0.0001, config["learningRate"]!.GetValue<double>());
    }

    [Fact]
    public void Installation_ExplainsTheSetupScript()
    {
        using var root = new TempDirectory();
        var installation = TrainingInstallation.For(new AppPaths(root.Path), root.Combine("engines"));

        Assert.Contains("setup-training.ps1", installation.Problem(), StringComparison.Ordinal);
    }
}
