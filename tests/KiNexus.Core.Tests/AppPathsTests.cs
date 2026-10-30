using KiNexus.Core.Paths;

namespace KiNexus.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Detect_FindsRoot_BySearchingUpwardForMarkerFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine(AppPaths.RootMarkerFile), "");
        var deepFolder = Directory.CreateDirectory(temp.Combine("src", "KiNexus.App", "bin", "Release")).FullName;

        var paths = AppPaths.Detect(deepFolder, environmentRoot: null);

        Assert.Equal(temp.Path, paths.Root);
    }

    [Fact]
    public void Detect_PrefersEnvironmentVariable_WhenFolderExists()
    {
        using var temp = new TempDirectory();
        using var other = new TempDirectory();
        File.WriteAllText(temp.Combine(AppPaths.RootMarkerFile), "");

        var paths = AppPaths.Detect(temp.Path, environmentRoot: other.Path);

        Assert.Equal(other.Path, paths.Root);
    }

    [Fact]
    public void Detect_IgnoresEnvironmentVariable_WhenFolderMissing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine(AppPaths.RootMarkerFile), "");

        var paths = AppPaths.Detect(temp.Path, environmentRoot: temp.Combine("gibt-es-nicht"));

        Assert.Equal(temp.Path, paths.Root);
    }

    [Fact]
    public void Detect_FallsBackToStartDirectory_WhenNoMarkerFound()
    {
        using var temp = new TempDirectory();

        var paths = AppPaths.Detect(temp.Path, environmentRoot: null);

        Assert.Equal(temp.Path, paths.Root);
    }

    [Fact]
    public void Resolve_MakesRelativePathsAbsolute_FromRoot()
    {
        var paths = new AppPaths(@"C:\Projekt");

        Assert.Equal(@"C:\Projekt\models", paths.Resolve("models"));
    }

    [Fact]
    public void Resolve_KeepsAbsolutePaths()
    {
        var paths = new AppPaths(@"C:\Projekt");

        Assert.Equal(@"D:\Modelle", paths.Resolve(@"D:\Modelle"));
    }

    [Fact]
    public void Resolve_ExpandsEnvironmentVariables()
    {
        var paths = new AppPaths(@"C:\Projekt");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(userProfile, "ki", "open-webui"), paths.Resolve(@"%USERPROFILE%\ki\open-webui"));
    }

    [Fact]
    public void Root_IgnoresTrailingBackslash()
    {
        Assert.Equal(@"C:\Projekt", new AppPaths(@"C:\Projekt\").Root);
    }

    [Fact]
    public void FixedFiles_LiveInDataFolder()
    {
        var paths = new AppPaths(@"C:\Projekt");

        Assert.Equal(@"C:\Projekt\data\settings.json", paths.SettingsFile);
        Assert.Equal(@"C:\Projekt\data\logs", paths.LogsDirectory);
        Assert.Equal(@"C:\Projekt\kis", paths.KisDirectory);
    }

    [Fact]
    public void EnsureDataDirectories_CreatesAllFolders()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);

        paths.EnsureDataDirectories();

        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.CacheDirectory));
        Assert.True(Directory.Exists(paths.AttachmentsDirectory));
    }
}
