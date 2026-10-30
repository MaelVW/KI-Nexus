namespace KiNexus.Core.Chats;

/// <summary>
/// Bilder, die an Nachrichten hängen. Die App kopiert sie nach data/attachments/&lt;chat-id&gt;/.
/// So funktioniert der Chat auch noch, wenn das Original verschoben oder gelöscht wird.
/// </summary>
public sealed class AttachmentStore(string attachmentsDirectory)
{
    /// <summary>Formate, die llama.cpp (stb_image) und Ollama lesen können.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

    public const long MaxFileBytes = 20 * 1024 * 1024;

    public static bool IsSupportedImage(string path) =>
        AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Kopiert ein Bild in den Ordner des Chats. Liegt es schon dort, bleibt es, wie es ist.</summary>
    public string Store(long chatId, string sourcePath)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        var chatFolder = FolderFor(chatId);
        if (fullSource.StartsWith(chatFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fullSource;
        }

        if (!IsSupportedImage(fullSource))
        {
            throw new InvalidDataException($"{Path.GetFileName(fullSource)}: Dieses Format geht nicht. Erlaubt sind {string.Join(", ", AllowedExtensions)}.");
        }

        var info = new FileInfo(fullSource);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Das Bild gibt es nicht mehr: {fullSource}");
        }

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"{info.Name} ist zu gross ({info.Length / 1024 / 1024} MB). Erlaubt sind höchstens {MaxFileBytes / 1024 / 1024} MB.");
        }

        Directory.CreateDirectory(chatFolder);
        var target = Path.Combine(chatFolder, $"{Guid.NewGuid():N}{info.Extension.ToLowerInvariant()}");
        File.Copy(fullSource, target);
        return target;
    }

    /// <summary>Löscht alle Bilder eines Chats (beim Löschen des Chats).</summary>
    public void DeleteChat(long chatId)
    {
        var folder = FolderFor(chatId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string FolderFor(long chatId) =>
        Path.GetFullPath(Path.Combine(attachmentsDirectory, chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
