using System.Text;

namespace KiNexus.Core.Models;

/// <summary>Die wichtigsten Infos aus dem Kopf einer GGUF-Datei.</summary>
public sealed record GgufMetadata(
    string Architecture,
    string? Name,
    string? Type,
    string? Quantization,
    string? License,
    int? ContextLength,
    int? ExpertCount,
    double? Temperature,
    double? TopP,
    int? TopK,
    bool HasChatTemplate,
    double? MinP = null,
    double? RepeatPenalty = null,
    int? BlockCount = null)
{
    /// <summary>Mixture of Experts: Das Modell hat viele "Experten", pro Wort rechnen nur wenige.</summary>
    public bool IsMoe => ExpertCount is > 1;

    /// <summary>Ein Projektor (mmproj) für Bilder statt eines Sprachmodells.</summary>
    public bool IsProjector => Type == "mmproj" || Architecture == "clip";
}

/// <summary>
/// Liest den Kopf (Metadaten) einer GGUF-Datei, ohne das ganze Modell zu laden.
/// Format: https://github.com/ggml-org/ggml/blob/master/docs/gguf.md
/// </summary>
public static class GgufReader
{
    private const uint Magic = 0x46554747; // "GGUF" als Zahl (Little Endian)

    private enum ValueType : uint
    {
        UInt8 = 0, Int8 = 1, UInt16 = 2, Int16 = 3, UInt32 = 4, Int32 = 5, Float32 = 6, Bool = 7,
        String = 8, Array = 9, UInt64 = 10, Int64 = 11, Float64 = 12,
    }

    public static GgufMetadata Read(string path)
    {
        var values = ReadKeyValues(path);
        var architecture = values.GetValueOrDefault("general.architecture") as string ?? "unbekannt";

        return new GgufMetadata(
            Architecture: architecture,
            Name: values.GetValueOrDefault("general.name") as string,
            Type: values.GetValueOrDefault("general.type") as string,
            Quantization: values.GetValueOrDefault("general.file_type") is { } fileType ? QuantizationName(Convert.ToInt32(fileType)) : null,
            License: values.GetValueOrDefault("general.license") as string,
            ContextLength: AsInt(values.GetValueOrDefault($"{architecture}.context_length")),
            ExpertCount: AsInt(values.GetValueOrDefault($"{architecture}.expert_count")),
            Temperature: AsDouble(values.GetValueOrDefault("general.sampling.temp")),
            TopP: AsDouble(values.GetValueOrDefault("general.sampling.top_p")),
            TopK: AsInt(values.GetValueOrDefault("general.sampling.top_k")),
            HasChatTemplate: values.ContainsKey("tokenizer.chat_template"),
            MinP: AsDouble(values.GetValueOrDefault("general.sampling.min_p")),
            RepeatPenalty: AsDouble(values.GetValueOrDefault("general.sampling.penalty_repeat")),
            BlockCount: AsInt(values.GetValueOrDefault($"{architecture}.block_count")));
    }

    /// <summary>
    /// Liest alle Schlüssel und Werte. Grosse Listen (z.B. die 260 000 Wörter des Tokenizers) werden übersprungen.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ReadKeyValues(string path)
    {
        using var stream = new BufferedStream(File.OpenRead(path), 1 << 20);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} ist keine GGUF-Datei.");
        }

        var version = reader.ReadUInt32();
        if (version < 2)
        {
            throw new InvalidDataException($"GGUF-Version {version} wird nicht unterstützt.");
        }

        _ = reader.ReadUInt64(); // Anzahl Tensoren, brauchen wir nicht
        var count = reader.ReadUInt64();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (ulong i = 0; i < count; i++)
        {
            var key = ReadString(reader);
            var type = (ValueType)reader.ReadUInt32();
            values[key] = type == ValueType.Array ? SkipArray(reader) : ReadValue(reader, type);
        }

        return values;
    }

    /// <summary>Name der Quantisierung (wie stark das Modell verkleinert wurde), z.B. 15 = Q4_K_M.</summary>
    public static string QuantizationName(int fileType) => fileType switch
    {
        0 => "F32", 1 => "F16", 2 => "Q4_0", 3 => "Q4_1", 7 => "Q8_0", 8 => "Q5_0", 9 => "Q5_1",
        10 => "Q2_K", 11 => "Q3_K_S", 12 => "Q3_K_M", 13 => "Q3_K_L", 14 => "Q4_K_S", 15 => "Q4_K_M",
        16 => "Q5_K_S", 17 => "Q5_K_M", 18 => "Q6_K", 19 => "IQ2_XXS", 20 => "IQ2_XS", 21 => "Q2_K_S",
        22 => "IQ3_XS", 23 => "IQ3_XXS", 24 => "IQ1_S", 25 => "IQ4_NL", 26 => "IQ3_S", 27 => "IQ3_M",
        28 => "IQ2_S", 29 => "IQ2_M", 30 => "IQ4_XS", 31 => "IQ1_M", 32 => "BF16", 36 => "TQ1_0",
        37 => "TQ2_0", 38 => "MXFP4_MOE",
        _ => $"Typ {fileType}",
    };

    private static object? ReadValue(BinaryReader reader, ValueType type) => type switch
    {
        ValueType.UInt8 => reader.ReadByte(),
        ValueType.Int8 => reader.ReadSByte(),
        ValueType.UInt16 => reader.ReadUInt16(),
        ValueType.Int16 => reader.ReadInt16(),
        ValueType.UInt32 => reader.ReadUInt32(),
        ValueType.Int32 => reader.ReadInt32(),
        ValueType.Float32 => reader.ReadSingle(),
        ValueType.Bool => reader.ReadByte() != 0,
        ValueType.String => ReadString(reader),
        ValueType.UInt64 => reader.ReadUInt64(),
        ValueType.Int64 => reader.ReadInt64(),
        ValueType.Float64 => reader.ReadDouble(),
        _ => throw new InvalidDataException($"Unbekannter GGUF-Werttyp {(uint)type}."),
    };

    /// <summary>Überspringt eine Liste und gibt nur ihre Länge zurück.</summary>
    private static ulong SkipArray(BinaryReader reader)
    {
        var itemType = (ValueType)reader.ReadUInt32();
        var length = reader.ReadUInt64();
        for (ulong i = 0; i < length; i++)
        {
            if (itemType == ValueType.String)
            {
                var size = reader.ReadUInt64();
                reader.BaseStream.Seek((long)size, SeekOrigin.Current);
            }
            else if (itemType == ValueType.Array)
            {
                SkipArray(reader);
            }
            else
            {
                reader.BaseStream.Seek(SizeOf(itemType), SeekOrigin.Current);
            }
        }

        return length;
    }

    private static int SizeOf(ValueType type) => type switch
    {
        ValueType.UInt8 or ValueType.Int8 or ValueType.Bool => 1,
        ValueType.UInt16 or ValueType.Int16 => 2,
        ValueType.UInt32 or ValueType.Int32 or ValueType.Float32 => 4,
        ValueType.UInt64 or ValueType.Int64 or ValueType.Float64 => 8,
        _ => throw new InvalidDataException($"Typ {(uint)type} hat keine feste Grösse."),
    };

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)length)));
    }

    private static int? AsInt(object? value) => value is null or string ? null : Convert.ToInt32(value);

    private static double? AsDouble(object? value) => value is null or string ? null : Math.Round(Convert.ToDouble(value), 4);
}
