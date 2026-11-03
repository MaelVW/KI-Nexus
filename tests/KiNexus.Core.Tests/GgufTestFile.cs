using System.Text;

namespace KiNexus.Core.Tests;

/// <summary>Schreibt eine winzige GGUF-Datei mit nur Metadaten. Reicht für Tests, ein echtes Modell ist das nicht.</summary>
internal static class GgufTestFile
{
    /// <param name="type">"mmproj" für einen Bild-Projektor.</param>
    /// <param name="tokenizer">false = wie ein Bild- oder Video-Modell für ComfyUI, ohne Tokenizer.</param>
    /// <param name="texts">Weitere Text-Schlüssel, z.B. "model_version" und "config" wie bei LTX-2.3.</param>
    /// <param name="tensors">Einträge in der Liste der Tensoren (Name und Grösse), ohne Daten. Z.B. "txt_in.weight" = [7680, 3072] wie bei FLUX.2 klein 4B.</param>
    public static void Write(string path, string architecture = "gemma4", int fileType = 15, int? expertCount = 128, string? type = null, bool tokenizer = true,
        IReadOnlyDictionary<string, string>? texts = null, IReadOnlyDictionary<string, ulong[]>? tensors = null)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        var entries = new List<Action>
        {
            () => WriteString(writer, "general.architecture", architecture),
            () => WriteString(writer, "general.name", "Testmodell"),
            () => WriteString(writer, "general.license", "apache-2.0"),
            () => { WriteKey(writer, "general.sampling.temp"); writer.Write(6u); writer.Write(1.0f); },
            () => { WriteKey(writer, "general.sampling.top_k"); writer.Write(5u); writer.Write(64); },
            () => { WriteKey(writer, "general.sampling.min_p"); writer.Write(6u); writer.Write(0.05f); },
            () => { WriteKey(writer, "general.sampling.penalty_repeat"); writer.Write(6u); writer.Write(1.1f); },
            () => { WriteKey(writer, $"{architecture}.block_count"); writer.Write(4u); writer.Write(30u); },
            () => { WriteKey(writer, $"{architecture}.context_length"); writer.Write(4u); writer.Write(262144u); },
            () => { WriteKey(writer, "general.file_type"); writer.Write(4u); writer.Write((uint)fileType); },
        };
        if (tokenizer)
        {
            entries.Add(() => WriteStringArray(writer, "tokenizer.ggml.tokens", ["<pad>", "<eos>", "Hallo", "Welt"]));
            entries.Add(() => { WriteKey(writer, "tokenizer.ggml.scores"); writer.Write(9u); writer.Write(6u); writer.Write(2ul); writer.Write(0.5f); writer.Write(0.25f); });
            entries.Add(() => WriteString(writer, "tokenizer.chat_template", "{{ messages }}"));
        }

        if (type is not null)
        {
            entries.Add(() => WriteString(writer, "general.type", type));
        }

        if (expertCount is int experts)
        {
            entries.Add(() => { WriteKey(writer, $"{architecture}.expert_count"); writer.Write(4u); writer.Write((uint)experts); });
        }

        foreach (var (key, value) in texts ?? new Dictionary<string, string>())
        {
            entries.Add(() => WriteString(writer, key, value));
        }

        writer.Write(0x46554747u); // "GGUF"
        writer.Write(3u);          // Version
        writer.Write((ulong)(tensors?.Count ?? 0)); // Tensoren
        writer.Write((ulong)entries.Count);
        entries.ForEach(write => write());
        foreach (var (name, shape) in tensors ?? new Dictionary<string, ulong[]>())
        {
            WriteRaw(writer, name);
            writer.Write((uint)shape.Length);
            foreach (var size in shape)
            {
                writer.Write(size);
            }

            writer.Write(8u);  // Typ Q8_0
            writer.Write(0ul); // Position
        }
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
