namespace KiNexus.Core.Tests;

/// <summary>Ein leerer Ordner nur für einen Test. Wird danach gelöscht.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KiNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist nicht wichtig genug, um einen Test scheitern zu lassen.
        }
    }
}
