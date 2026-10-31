using System.Security.Cryptography;
using System.Text;

namespace KiNexus.Core.IO;

/// <summary>
/// Speichert ein Geheimnis (z.B. den Hugging-Face-Token) verschlüsselt mit Windows DPAPI.
/// Nur derselbe Windows-Benutzer auf diesem Laptop kann es wieder lesen. Nie im Klartext auf der Festplatte.
/// </summary>
public sealed class SecretStore(string filePath)
{
    /// <summary>Zusätzlicher Schlüssel-Teil, damit andere Programme den Blob nicht einfach mitbenutzen können.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KI-Nexus Secret v1");

    public bool HasSecret => File.Exists(filePath);

    public void Save(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(filePath, encrypted);
    }

    /// <summary>Null, wenn nichts gespeichert ist oder die Datei nicht (mehr) lesbar ist.</summary>
    public string? Load()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Delete()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
