using KiNexus.Core.Settings;

namespace KiNexus.Core.Tests;

public class WindowSettingsTests
{
    // Ein Bildschirm mit 1920 × 1080 Pixel.
    private const double ScreenLeft = 0, ScreenTop = 0, ScreenWidth = 1920, ScreenHeight = 1080;

    [Fact]
    public void IsOnScreen_ReturnsFalse_WithoutSavedPosition()
    {
        var window = new WindowSettings();

        Assert.False(window.IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [Fact]
    public void IsOnScreen_ReturnsTrue_ForWindowInsideScreen()
    {
        var window = new WindowSettings { Left = 100, Top = 100 };

        Assert.True(window.IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [Fact]
    public void IsOnScreen_ReturnsFalse_ForWindowOnRemovedSecondMonitor()
    {
        var window = new WindowSettings { Left = 2500, Top = 100 };

        Assert.False(window.IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [Fact]
    public void IsOnScreen_ReturnsFalse_WhenTitleBarIsAboveScreen()
    {
        var window = new WindowSettings { Left = 100, Top = -50 };

        Assert.False(window.IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }
}
