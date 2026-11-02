using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace KiNexus.App.Services;

/// <summary>Öffnet Dinge mit Windows (Ordner, Bilder) und nutzt die Zwischenablage.</summary>
public interface IShellService
{
    void OpenFolder(string path);

    /// <summary>Öffnet eine Datei mit dem Standardprogramm von Windows, z.B. ein Bild.</summary>
    void OpenFile(string path);

    void CopyText(string text);

    /// <summary>Öffnet eine Webseite im Standard-Browser. Nur https, oder http auf diesem Laptop (z.B. Open WebUI).</summary>
    void OpenUrl(Uri uri);

    /// <summary>Verschiebt Dateien in den Papierkorb (nicht endgültig gelöscht). False, wenn es nicht geklappt hat.</summary>
    bool MoveToRecycleBin(IReadOnlyCollection<string> files);

    /// <summary>Startet ein PowerShell-Skript in einem eigenen, sichtbaren Fenster (z.B. setup-video.ps1). Das Fenster bleibt danach offen.</summary>
    void RunScriptInWindow(string scriptPath, string arguments);
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
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new ArgumentException("Nur https-Adressen (oder http auf diesem Laptop) werden geöffnet.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void RunScriptInWindow(string scriptPath, string arguments)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Skript nicht gefunden.", scriptPath);
        }

        // Windows PowerShell 5.1 wie in der Anleitung. -NoExit: Das Fenster bleibt offen, damit man das Ergebnis lesen kann.
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{scriptPath}\" {arguments}")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)) ?? "",
        });
    }

    public bool MoveToRecycleBin(IReadOnlyCollection<string> files)
    {
        if (files.Count == 0)
        {
            return true;
        }

        // Windows will die Liste mit \0 getrennt und mit \0\0 am Ende. FOF_ALLOWUNDO = Papierkorb statt endgültig löschen.
        var operation = new ShFileOperation
        {
            Function = FileOperationDelete,
            From = string.Join('\0', files.Select(Path.GetFullPath)) + "\0\0",
            Flags = FlagAllowUndo | FlagNoConfirmation | FlagSilent | FlagNoErrorUi,
        };
        return SHFileOperation(ref operation) == 0 && !operation.AnyOperationsAborted;
    }

    private const uint FileOperationDelete = 3;
    private const ushort FlagSilent = 0x0004;
    private const ushort FlagNoConfirmation = 0x0010;
    private const ushort FlagAllowUndo = 0x0040;
    private const ushort FlagNoErrorUi = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOperation operation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
}
