using KiNexus.Core.Video;

namespace KiNexus.Core.Tests;

public class VideoTests
{
    [Fact]
    public void Request_DefaultIsValid()
    {
        var request = new VideoRequest("A dog runs across a meadow.");

        Assert.Null(request.Problem());
        Assert.Equal(3.88, request.Seconds, 2);
    }

    [Theory]
    [InlineData("", 768, 512, 97, "Prompt")]
    [InlineData("x", 770, 512, 97, "32")]
    [InlineData("x", 768, 128, 97, "zwischen 256")]
    [InlineData("x", 1536, 512, 97, "1280")]
    [InlineData("x", 768, 512, 96, "8·n + 1")]
    [InlineData("x", 768, 512, 1, "8·n + 1")]
    [InlineData("x", 768, 512, 257, "249")]
    public void Request_ExplainsWhatIsWrong(string prompt, int width, int height, int frames, string expected)
    {
        var problem = new VideoRequest(prompt, width, height, frames).Problem();

        Assert.NotNull(problem);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_RejectsOddFrameRate()
    {
        Assert.Contains("Bilder pro Sekunde", new VideoRequest("x", FrameRate: 0).Problem(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, 25, 97)]
    [InlineData(2, 25, 49)]
    [InlineData(5, 24, 121)]
    [InlineData(10, 25, 249)]
    [InlineData(0.1, 25, 9)]
    public void FramesFor_RoundsToEightTimesNPlusOne(double seconds, double frameRate, int expected)
    {
        var frames = VideoRequest.FramesFor(seconds, frameRate);

        Assert.Equal(expected, frames);
        Assert.Equal(1, (frames - 1) % 8 + 1);
    }

    [Fact]
    public void ModelSet_KnowsItsFilesAndSteps()
    {
        using var root = new TempDirectory();
        var models = VideoModelSet.Ltx23Distilled;
        Directory.CreateDirectory(root.Combine("vae"));
        File.WriteAllText(root.Combine("vae", models.VideoVae), "x");

        Assert.Equal(8, models.Steps);
        Assert.Equal(1.0, models.Guidance);
        Assert.Equal(5, models.Files(root.Path).Count);
        Assert.Contains(root.Combine("diffusion_models", models.Model), models.Files(root.Path));
        var missing = models.MissingFiles(root.Path);
        Assert.Equal(4, missing.Count);
        Assert.DoesNotContain(root.Combine("vae", models.VideoVae), missing);
    }
}
