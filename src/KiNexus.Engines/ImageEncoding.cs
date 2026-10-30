namespace KiNexus.Engines;

/// <summary>Bilder für die Engines vorbereiten: als Base64-Text (so gehen Bilder in JSON).</summary>
public static class ImageEncoding
{
    public static string ToBase64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

    /// <summary>"data:image/png;base64,…" – so erwartet die OpenAI-kompatible API von llama-server ein Bild.</summary>
    public static string ToDataUrl(string path) => $"data:{MimeType(path)};base64,{ToBase64(path)}";

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream",
    };
}
