using KiNexus.Core.Models;

namespace KiNexus.Core.Tests;

public class GgufReaderTests
{
    [Fact]
    public void Read_ReturnsMetadata_AndSkipsLargeArrays()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("test.gguf");
        GgufTestFile.Write(path);

        var meta = GgufReader.Read(path);

        Assert.Equal("gemma4", meta.Architecture);
        Assert.Equal("Testmodell", meta.Name);
        Assert.Equal("Q4_K_M", meta.Quantization);
        Assert.Equal("apache-2.0", meta.License);
        Assert.Equal(262144, meta.ContextLength);
        Assert.Equal(128, meta.ExpertCount);
        Assert.True(meta.IsMoe);
        Assert.Equal(1.0, meta.Temperature);
        Assert.Equal(64, meta.TopK);
        Assert.Equal(0.05, meta.MinP);
        Assert.Equal(1.1, meta.RepeatPenalty);
        Assert.Equal(30, meta.BlockCount);
        Assert.True(meta.HasChatTemplate);
    }

    [Fact]
    public void Read_DenseModel_IsNotMoe()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("dense.gguf");
        GgufTestFile.Write(path, architecture: "qwen35", fileType: 7, expertCount: null);

        var meta = GgufReader.Read(path);

        Assert.False(meta.IsMoe);
        Assert.Equal("Q8_0", meta.Quantization);
    }

    [Fact]
    public void ReadKeyValues_ReportsArrayLength()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("test.gguf");
        GgufTestFile.Write(path);

        var values = GgufReader.ReadKeyValues(path);

        Assert.Equal(4ul, values["tokenizer.ggml.tokens"]);
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("kein.gguf");
        File.WriteAllText(path, "Das ist Text und kein Modell.");

        Assert.Throws<InvalidDataException>(() => GgufReader.Read(path));
    }

    [Theory]
    [InlineData(15, "Q4_K_M")]
    [InlineData(32, "BF16")]
    [InlineData(999, "Typ 999")]
    public void QuantizationName_MapsFileTypes(int fileType, string expected)
    {
        Assert.Equal(expected, GgufReader.QuantizationName(fileType));
    }
}
