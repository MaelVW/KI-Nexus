using System.Diagnostics;
using System.IO;
using System.Windows;

namespace KiNexus.App.Services;

/// <summary>Öffnet Dinge mit Windows (Ordner, Bilder) und nutzt die Zwischenablage.</summary>
public interface IShellService
{
    void OpenFolder(string path);

    /// <summary>Öffnet eine Datei mit dem Standardprogramm von Windows, z.B. ein Bild.</summary>
    void OpenFile(string path);

    void CopyText(string text);

    /// <summary>Öffnet eine Webseite im Standard-Browser. Nur https.</summary>
    void OpenUrl(Uri uri);
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public void CopyText(string text) => Clipboard.SetText(text);

    public void OpenUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Nur https-Adressen werden geöffnet.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
