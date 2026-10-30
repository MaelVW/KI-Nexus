using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KiNexus.Core.IO;

/// <summary>
/// Hardlinks: ein zweiter Dateiname für dieselben Daten auf der Festplatte.
/// Braucht keinen zusätzlichen Platz. Die Daten bleiben, bis der letzte Name gelöscht ist.
/// Geht nur auf demselben Laufwerk (NTFS).
/// </summary>
public static class FileLinks
{
    /// <summary>Legt <paramref name="linkPath"/> als zweiten Namen für <paramref name="existingFile"/> an.</summary>
    public static void CreateHardLink(string linkPath, string existingFile)
    {
        if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(linkPath)), Path.GetPathRoot(Path.GetFullPath(existingFile)), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Ein Hardlink geht nur auf demselben Laufwerk. Der Modell-Ordner muss auf demselben Laufwerk wie Ollama liegen.");
        }

        if (!CreateHardLinkW(linkPath, existingFile, IntPtr.Zero))
        {
            throw new IOException($"Hardlink konnte nicht erstellt werden: {linkPath}", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    /// <summary>True, wenn beide Pfade auf dieselben Daten zeigen (gleiches Laufwerk, gleiche Datei-Nummer).</summary>
    public static bool AreSameFile(string pathA, string pathB)
    {
        if (!File.Exists(pathA) || !File.Exists(pathB))
        {
            return false;
        }

        var a = GetFileId(pathA);
        var b = GetFileId(pathB);
        return a == b;
    }

    /// <summary>Wie viele Namen (Hardlinks) diese Datei hat.</summary>
    public static int GetLinkCount(string path) => (int)GetInfo(path).NumberOfLinks;

    private static (uint Volume, ulong Index) GetFileId(string path)
    {
        var info = GetInfo(path);
        return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    private static ByHandleFileInformation GetInfo(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw new IOException($"Datei-Infos nicht lesbar: {path}", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return info;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
