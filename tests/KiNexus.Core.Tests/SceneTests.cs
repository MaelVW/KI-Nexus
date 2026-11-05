using KiNexus.Core.Video;

namespace KiNexus.Core.Tests;

/// <summary>Szenen-Projekte (Bauplan Szenen, S1).</summary>
public class SceneTests
{
    [Fact]
    public void ParseText_ReadsSecondsAndSkipsComments()
    {
        var scenes = SceneProject.ParseText("Der Hund rennt.\n\n# Notiz\n5 | Er springt ins Wasser.\n20 | zu lang\n1,5|zu kurz\r\n");

        Assert.Equal([(4.0, "Der Hund rennt."), (5.0, "Er springt ins Wasser."), (8.0, "zu lang"), (2.0, "zu kurz")], scenes);
    }

    [Fact]
    public void ApplyText_KeepsDoneScenesUntilTheFirstChange()
    {
        var project = new SceneProject { Name = "hund" };
        project.ApplyText("A\nB\nC");
        foreach (var scene in project.Scenes)
        {
            scene.Status = SceneStatus.Done;
        }

        project.FinalVideo = "video.mp4";
        project.ApplyText("A\nB\nC");
        Assert.True(project.AllDone);
        Assert.Equal("video.mp4", project.FinalVideo);

        project.ApplyText("A\nB neu\nC\nD");

        Assert.Equal([SceneStatus.Done, SceneStatus.Open, SceneStatus.Open, SceneStatus.Open], project.Scenes.Select(s => s.Status));
        Assert.Null(project.FinalVideo);
        Assert.Equal("A\nB neu\nC\nD", project.ToText().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ResetFrom_OpensTheRest_AndPromptAddsStyle()
    {
        var project = new SceneProject { Name = "x", Style = "warm light." };
        project.ApplyText("A\n6 | B\nC");
        project.Scenes.ForEach(s => s.Status = SceneStatus.Done);

        project.ResetFrom(1);

        Assert.Equal([SceneStatus.Done, SceneStatus.Open, SceneStatus.Open], project.Scenes.Select(s => s.Status));
        Assert.Equal("B warm light.", project.PromptFor(1));
        Assert.Equal(14, project.TotalSeconds);
        Assert.Equal("A\n6 | B\nC", project.ToText().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Store_SavesAndLists()
    {
        using var root = new TempDirectory();
        var store = new SceneProjectStore(root.Combine("projekte"));
        var project = new SceneProject { Name = SceneProjectStore.Slug("Hund am Teich!"), Style = "s" };
        project.ApplyText("A\nB");

        store.Save(project);

        Assert.Equal("hund-am-teich", project.Name);
        Assert.Equal(["hund-am-teich"], store.Names());
        var loaded = store.Load("hund-am-teich")!;
        Assert.Equal(2, loaded.Scenes.Count);
        Assert.Equal("s", loaded.Style);
        Assert.Null(store.Load("gibt-es-nicht"));
    }
}
