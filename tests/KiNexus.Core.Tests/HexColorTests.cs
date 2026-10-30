namespace KiNexus.Core.Tests;

public class HexColorTests
{
    [Theory]
    [InlineData("#00E5FF")]
    [InlineData("00e5ff")]
    [InlineData("  #00E5FF  ")]
    public void TryParse_AcceptsValidHex(string text)
    {
        Assert.True(HexColor.TryParse(text, out var color));
        Assert.Equal(HexColor.DefaultAccent, color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#00E5F")]
    [InlineData("#00E5FFAA")]
    [InlineData("#GGGGGG")]
    [InlineData("cyan")]
    public void TryParse_RejectsInvalidText(string? text)
    {
        Assert.False(HexColor.TryParse(text, out _));
    }

    [Fact]
    public void ToString_UsesUppercaseHashFormat()
    {
        Assert.Equal("#0A0E13", new HexColor(0x0A, 0x0E, 0x13).ToString());
    }

    [Fact]
    public void Dimmed_ForDefaultCyan_UsesDesignValue()
    {
        Assert.Equal("#0FA3B8", HexColor.DefaultAccent.Dimmed().ToString());
    }

    [Fact]
    public void Dimmed_ForOtherColors_IsDarker()
    {
        var violet = new HexColor(200, 100, 255);

        var dimmed = violet.Dimmed();

        Assert.Equal(new HexColor(144, 72, 184), dimmed);
    }
}
