using KiNexus.Core.Models;
using KiNexus.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

/// <summary>Bildmodelle erkennen: eingebaut, von Hugging Face, FLUX.2 klein 4B/9B, Z-Image, FLUX.1.</summary>
public class ImageCatalogTests
{
    private static readonly Dictionary<string, ulong[]> Klein4B = new()
    {
        ["double_stream_modulation_img.lin.weight"] = [3072, 18432],
        ["txt_in.weight"] = [7680, 3072],
    };

    private static readonly Dictionary<string, ulong[]> Klein9B = new()
    {
        ["double_stream_modulation_img.lin.weight"] = [4096, 24576],
        ["txt_in.weight"] = [12288, 4096],
    };

    private static readonly Dictionary<string, ulong[]> Flux1 = new() { ["txt_in.weight"] = [4096, 3072] };

    private static readonly Dictionary<string, ulong[]> ZImage = new() { ["cap_embedder.1.weight"] = [2560, 3840], ["x_embedder.weight"] = [64, 3840] };

    [Fact]
    public void Reader_FindsTensorShapes()
    {
        using var root = new TempDirectory();
        var file = root.Combine("klein.gguf");
        GgufTestFile.Write(file, "flux", fileType: 7, expertCount: null, tokenizer: false, tensors: Klein4B);

        var shapes = GgufReader.ReadTensorShapes(file, ["txt_in.weight", "gibt-es-nicht"]);

        Assert.Equal([7680UL, 3072UL], shapes["txt_in.weight"]);
        Assert.False(shapes.ContainsKey("gibt-es-nicht"));
    }

    [Theory]
    [InlineData("flux", 7680UL, true, null, "x.gguf", null, ImageModelFamily.Flux2Klein, "FLUX.2 klein 4B")]
    [InlineData("flux", 12288UL, true, null, "x.gguf", "qwen_3_8b_fp8mixed.safetensors", ImageModelFamily.Flux2Klein, "FLUX.2 klein 9B")]
    [InlineData("lumina2", null, false, 2560UL, "z-image-turbo-Q4_K_M.gguf", null, ImageModelFamily.ZImageTurbo, "Z-Image Turbo")]
    [InlineData("lumina2", null, false, 2560UL, "z-image-Q8_0.gguf", null, ImageModelFamily.ZImageTurbo, "Gerechnet wie Turbo")]
    public void Classify_KnowsUsableModels(string architecture, ulong? textIn, bool isFlux2, ulong? caption, string fileName, string? qwen8b,
        ImageModelFamily family, string note)
    {
        var (template, text) = ImageModelCatalog.Classify(architecture, textIn, isFlux2, caption, fileName, qwen8b);

        Assert.Equal(family, Assert.IsType<ImageModelSet>(template).Family);
        Assert.Contains(note, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("flux", 4096UL, false, null, "T5 und CLIP")]
    [InlineData("flux", 12288UL, true, null, "Qwen3 8B")]
    [InlineData("flux", 15360UL, true, null, "viel zu gross")]
    [InlineData("lumina2", null, false, 2304UL, "nur Z-Image")]
    [InlineData("sdxl", null, false, null, "Architektur sdxl")]
    public void Classify_ExplainsUnusableModels(string architecture, ulong? textIn, bool isFlux2, ulong? caption, string note)
    {
        var (template, text) = ImageModelCatalog.Classify(architecture, textIn, isFlux2, caption, "x.gguf", null);

        Assert.Null(template);
        Assert.Contains(note, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_UsesFoundQwen8BFile()
    {
        var (template, _) = ImageModelCatalog.Classify("flux", 12288, true, null, "x.gguf", "qwen_3_8b.safetensors");

        Assert.Equal("qwen_3_8b.safetensors", template!.TextEncoder);
    }

    [Fact]
    public void Find_ListsBuiltInAndDownloadedModels()
    {
        using var root = new TempDirectory();
        var image = root.Combine("models", "image");
        Directory.CreateDirectory(Path.Combine(image, "diffusion_models"));
        GgufTestFile.Write(Path.Combine(image, "diffusion_models", "flux-2-klein-4b-Q8_0.gguf"), "flux", 7, null, tokenizer: false, tensors: Klein4B);
        GgufTestFile.Write(Path.Combine(image, "diffusion_models", "kaputt.gguf"), "flux", 7, null, tokenizer: false, tensors: Flux1);
        var library = new ModelLibrary(root.Combine("models"), NullLogger<ModelLibrary>.Instance);
        AddLibraryModel(library, root, "z-image-turbo-q4", "z-image-turbo-Q4_K_M.gguf", "lumina2", ZImage);
        AddLibraryModel(library, root, "klein-9b", "flux-2-klein-9b-Q4_K_M.gguf", "flux", Klein9B);

        var choices = ImageModelCatalog.Find(image, library.Scan());

        Assert.Equal(["FLUX.2 klein 4B (Q8_0)", "z-image-turbo-q4", "kaputt", "klein-9b"], choices.Select(c => c.Name));
        Assert.Null(choices[0].Set!.ModelFolder);
        var zImage = choices[1].Set!;
        Assert.Equal(ImageModelFamily.ZImageTurbo, zImage.Family);
        Assert.Equal(root.Combine("models", "z-image-turbo-q4"), zImage.ModelFolder);
        Assert.Equal(root.Combine("models", "z-image-turbo-q4", "z-image-turbo-Q4_K_M.gguf"), zImage.Files(image)[0]);
        Assert.Equal(Path.Combine(image, "vae", "ae.safetensors"), zImage.Files(image)[2]);
        Assert.False(choices[3].IsUsable);
        Assert.Contains("Qwen3 8B", choices[3].Note, StringComparison.Ordinal);

        // Mit dem Text-Encoder Qwen3 8B ist klein 9B nutzbar.
        Directory.CreateDirectory(Path.Combine(image, "text_encoders"));
        File.WriteAllText(Path.Combine(image, "text_encoders", "qwen_3_8b_fp8mixed.safetensors"), "x");
        var nine = ImageModelCatalog.Find(image, library.Scan()).Single(c => c.Name == "klein-9b");
        Assert.True(nine.IsUsable);
        Assert.Equal("qwen_3_8b_fp8mixed.safetensors", nine.Set!.TextEncoder);
        Assert.True(nine.Set.SupportsReferences);
    }

    /// <summary>Die echten Dateien aus B0 (nur wenn sie da sind): Liest nur Kopf und Tensor-Liste.</summary>
    [Fact]
    public void RealFiles_AreRecognised()
    {
        var paths = KiNexus.Core.Paths.AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(KiNexus.Core.Paths.AppPaths.RootEnvironmentVariable));
        var image = Path.Combine(paths.Root, "models", "image");
        Assert.SkipUnless(File.Exists(Path.Combine(image, "diffusion_models", ImageModelSet.Flux2Klein4B.Model)), "Bildmodelle nicht eingerichtet.");

        var choices = ImageModelCatalog.Find(image, []);

        var klein = choices.Single(c => c.Name == ImageModelSet.Flux2Klein4B.Name);
        Assert.Equal(ImageModelFamily.Flux2Klein, klein.Set!.Family);
        Assert.Equal(ImageModelSet.Flux2Klein4B.TextEncoder, klein.Set.TextEncoder);
        if (choices.SingleOrDefault(c => c.Name == ImageModelSet.ZImageTurbo.Name) is { } zImage)
        {
            Assert.Equal(ImageModelFamily.ZImageTurbo, zImage.Set!.Family);
            Assert.Contains("Z-Image Turbo", zImage.Note, StringComparison.Ordinal);
        }
    }

    private static void AddLibraryModel(ModelLibrary library, TempDirectory root, string folder, string file, string architecture, Dictionary<string, ulong[]> tensors)
    {
        var path = root.Combine("models", folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        GgufTestFile.Write(path, architecture, 15, null, tokenizer: false, tensors: tensors);
        library.WriteManifestFor(Path.GetDirectoryName(path)!, file, null, "huggingface:test/test", null);
    }
}
