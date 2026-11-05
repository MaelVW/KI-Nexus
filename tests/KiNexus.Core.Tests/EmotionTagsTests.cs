using KiNexus.Core.Chats;

namespace KiNexus.Core.Tests;

public class EmotionTagsTests
{
    [Theory]
    [InlineData("Hallo! [[gefuehl:freude]] Schön.", "Hallo!  Schön.")]
    [InlineData("[[Gefühl: Traurig]]Oh nein.", "Oh nein.")]
    [InlineData("Ohne Markierung.", "Ohne Markierung.")]
    [InlineData("Gleich kommt [[gefu", "Gleich kommt ")]
    [InlineData("Gleich kommt [", "Gleich kommt ")]
    [InlineData("Siehe [die Doku](https://x.ch)", "Siehe [die Doku](https://x.ch)")]
    [InlineData("Liste [1", "Liste [1")]
    [InlineData("Licht aus. [[geraet:light.office:turn_off]]", "Licht aus. ")]
    [InlineData("Mache ich [[geraet:light.office:set_bright", "Mache ich ")]
    public void Strip_RemovesTagsAndUnfinishedStart(string text, string expected) => Assert.Equal(expected, EmotionTags.Strip(text));

    [Fact]
    public void Strip_GrowsOnlyAtTheEnd_WhileStreaming()
    {
        const string full = "Ja [[gefuehl:ueberrascht]] wirklich! [[geraet:light.office:set_brightness:30]] Fertig.";
        var previous = "";
        for (var i = 1; i <= full.Length; i++)
        {
            var clean = EmotionTags.Strip(full[..i]);
            Assert.StartsWith(previous, clean, StringComparison.Ordinal);
            previous = clean;
        }

        Assert.Equal("Ja  wirklich!  Fertig.", previous);
    }

    [Fact]
    public void Find_ReturnsKnownNamesInOrder()
    {
        Assert.Equal(["freude", "ueberrascht"], EmotionTags.Find("[[gefuehl:Freude]] a [[gefuehl:unbekannt]] b [[gefühl:überrascht]]"));
        Assert.Empty(EmotionTags.Find("nichts"));
    }
}
