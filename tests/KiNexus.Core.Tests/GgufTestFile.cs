using System.Text;

namespace KiNexus.Core.Tests;

/// <summary>Schreibt eine winzige GGUF-Datei mit nur Metadaten. Reicht für Tests, ein echtes Modell ist das nicht.</summary>
internal static class GgufTestFile
{
    public static void Write(string path, string architecture = "gemma4", int fileType = 15, int? expertCount = 128)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        var entries = new List<Action>
        {
            () => WriteString(writer, "general.architecture", architecture),
            () => WriteString(writer, "general.name", "Testmodell"),
            () => WriteString(writer, "general.license", "apache-2.0"),
            () => { WriteKey(writer, "general.sampling.temp"); writer.Write(6u); writer.Write(1.0f); },
            () => { WriteKey(writer, "general.sampling.top_k"); writer.Write(5u); writer.Write(64); },
            () => { WriteKey(writer, $"{architecture}.context_length"); writer.Write(4u); writer.Write(262144u); },
            () => WriteStringArray(writer, "tokenizer.ggml.tokens", ["<pad>", "<eos>", "Hallo", "Welt"]),
            () => { WriteKey(writer, "tokenizer.ggml.scores"); writer.Write(9u); writer.Write(6u); writer.Write(2ul); writer.Write(0.5f); writer.Write(0.25f); },
            () => WriteString(writer, "tokenizer.chat_template", "{{ messages }}"),
            () => { WriteKey(writer, "general.file_type"); writer.Write(4u); writer.Write((uint)fileType); },
        };
        if (expertCount is int experts)
        {
            entries.Add(() => { WriteKey(writer, $"{architecture}.expert_count"); writer.Write(4u); writer.Write((uint)experts); });
        }

        writer.Write(0x46554747u); // "GGUF"
        writer.Write(3u);          // Version
        writer.Write(0ul);         // Tensoren
        writer.Write((ulong)entries.Count);
        entries.ForEach(write => write());
    }

    private static void WriteKey(BinaryWriter writer, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteString(BinaryWriter writer, string key, string value)
    {
        WriteKey(writer, key);
        writer.Write(8u);
        WriteRaw(writer, value);
    }

    private static void WriteStringArray(BinaryWriter writer, string key, string[] values)
    {
        WriteKey(writer, key);
        writer.Write(9u);
        writer.Write(8u);
        writer.Write((ulong)values.Length);
        foreach (var value in values)
        {
            WriteRaw(writer, value);
        }
    }

    private static void WriteRaw(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
