using System.Diagnostics;
using System.IO;

namespace KiNexus.App.Services;

/// <summary>Öffnet Dinge mit Windows, z.B. einen Ordner im Explorer.</summary>
public interface IShellService
{
    void OpenFolder(string path);
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
