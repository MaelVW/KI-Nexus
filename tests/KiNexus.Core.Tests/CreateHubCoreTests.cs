using KiNexus.Core.Inference;
using KiNexus.Core.IO;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KiNexus.Core.Tests;

public class FitEstimatorTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const ulong Vram8Gb = 8UL * 1024 * 1024 * 1024;
    private const ulong Ram32Gb = 32UL * 1024 * 1024 * 1024;

    [Fact]
    public void SmallModel_FitsInVram_IsGreen()
    {
        // qwen3.5:9b: 5.24 GB Modell + 0.86 GB Projektor, gemessen in M1 ganz im VRAM.
        var estimate = FitEstimator.Estimate((long)(5.24 * Gb), (long)(0.86 * Gb), 8192, Vram8Gb, Ram32Gb);

        Assert.Equal(FitLevel.Green, estimate.Level);
        Assert.Contains("Schätzung", estimate.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeModel_NeedsRam_IsYellow()
    {
        // gemma4:26b, ca. 17 GB: läuft, aber teilweise im RAM.
        Assert.Equal(FitLevel.Yellow, FitEstimator.Estimate(17 * Gb, 1 * Gb, 8192, Vram8Gb, Ram32Gb).Level);
    }

    [Fact]
    public void HugeModel_IsRed()
    {
        Assert.Equal(FitLevel.Red, FitEstimator.Estimate(40 * Gb, 0, 8192, Vram8Gb, Ram32Gb).Level);
    }

    [Fact]
    public void WithoutNvidiaCard_NothingIsGreen()
    {
        Assert.Equal(FitLevel.Yellow, FitEstimator.Estimate(1 * Gb, 0, 4096, vramTotalBytes: null, Ram32Gb).Level);
    }

    [Fact]
    public void LongerContext_NeedsMoreBuffer()
    {
        Assert.True(FitEstimator.ContextBufferBytes(32768) > FitEstimator.ContextBufferBytes(8192));
    }
}

public class KiIdsTests
{
    [Theory]
    [InlineData("Mael-KI", "mael-ki")]
    [InlineData("Schul KI für Mathe", "schul-ki-fuer-mathe")]
    [InlineData("  Grösse & Übung!  ", "groesse-uebung")]
    [InlineData("Qwen 3.5 (schnell)", "qwen-3.5-schnell")]
    [InlineData("Straße", "strasse")]
    public void Slug_MakesSafeFileNames(string name, string expected)
    {
        Assert.Equal(expected, KiIds.Slug(name));
    }

    [Fact]
    public void FromName_AddsNumber_WhenIdExists()
    {
        Assert.Equal("mael-ki-2", KiIds.FromName("Mael-KI", ["mael-ki"]));
        Assert.Equal("mael-ki-3", KiIds.FromName("Mael KI", ["mael-ki", "MAEL-KI-2"]));
    }

    [Fact]
    public void FromName_FallsBackToKi_ForEmptyNames()
    {
        Assert.Equal("ki", KiIds.FromName("!!!", []));
    }
}

public class KiRegistrySaveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_WritesJson_LoadReadsItBack_AndRaisesChanged()
    {
        using var temp = new TempDirectory();
        var registry = new KiRegistry(temp.Path, NullLogger<KiRegistry>.Instance);
        var changed = 0;
        registry.Changed += (_, _) => changed++;

        var ki = new KiProfile { Id = "schul-ki", DisplayName = "Schul-KI", SystemPrompt = "Erkläre einfach." };
        ki.BaseModel.Path = "models/qwen3-0.6b-q8_0/Qwen3-0.6B-Q8_0.gguf";
        await registry.SaveAsync(ki, Ct);

        Assert.True(File.Exists(temp.Combine("schul-ki.json")));
        Assert.Equal(1, changed);
        Assert.NotEqual(default, ki.CreatedAt);

        var loaded = Assert.Single(await new KiRegistry(temp.Path, NullLogger<KiRegistry>.Instance).LoadAsync(Ct));
        Assert.Equal("Schul-KI", loaded.DisplayName);
        Assert.Equal("Erkläre einfach.", loaded.SystemPrompt);
        Assert.Equal(EngineKind.LlamaCpp, loaded.Engine);
    }

    [Fact]
    public async Task Delete_RemovesFile_AndEntry()
    {
        using var temp = new TempDirectory();
        var registry = new KiRegistry(temp.Path, NullLogger<KiRegistry>.Instance);
        await registry.SaveAsync(new KiProfile { Id = "weg", DisplayName = "Weg" }, Ct);

        await registry.DeleteAsync("weg", Ct);

        Assert.False(File.Exists(temp.Combine("weg.json")));
        Assert.Empty(registry.All);
    }

    [Fact]
    public async Task Save_RejectsIdsThatAreNoSafeFileName()
    {
        using var temp = new TempDirectory();
        var registry = new KiRegistry(temp.Path, NullLogger<KiRegistry>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveAsync(new KiProfile { Id = "..\\böse" }, Ct));
    }
}

public class ModelImporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void FolderName_DropsSplitSuffix()
    {
        Assert.Equal("qwen3-0.6b-q8_0", ModelImporter.FolderNameFor("Qwen3-0.6B-Q8_0.gguf"));
        Assert.Equal("big-q4_k_m", ModelImporter.FolderNameFor("Big-Q4_K_M-00001-of-00003.gguf"));
    }

    [Fact]
    public void PartsOf_ListsAllParts()
    {
        var parts = ModelImporter.PartsOf(@"C:\m\Big-Q4_K_M-00002-of-00003.gguf");

        Assert.Equal([@"C:\m\Big-Q4_K_M-00001-of-00003.gguf", @"C:\m\Big-Q4_K_M-00002-of-00003.gguf", @"C:\m\Big-Q4_K_M-00003-of-00003.gguf"], parts);
    }

    [Fact]
    public async Task Copy_KeepsOriginal_AndWritesManifest()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        var file = source.Combine("Test-Q4_K_M.gguf");
        GgufTestFile.Write(file);
        var projector = source.Combine("mmproj-F16.gguf");
        GgufTestFile.Write(projector, architecture: "clip", fileType: 1, expertCount: null, type: "mmproj");

        var model = await CreateImporter(models).ImportFileAsync(file, projector, move: false, "datei:" + file, null, null, Ct);

        Assert.True(File.Exists(file));
        Assert.Equal("test-q4_k_m", model.FolderName);
        Assert.True(File.Exists(model.ModelPath));
        Assert.Equal("mmproj-F16.gguf", model.Manifest.MmprojFile);
        Assert.Equal("Q4_K_M", model.Manifest.Quantization);
        Assert.Equal("apache-2.0", model.Manifest.License);
        Assert.Equal(1.0, model.Manifest.Temperature);
        Assert.Equal(64, model.Manifest.TopK);
        Assert.Single(new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance).Scan());
    }

    [Fact]
    public async Task Move_RemovesOriginal_AndReportsProgress()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        var file = source.Combine("Test.gguf");
        GgufTestFile.Write(file);
        var reports = new List<double>();

        var model = await CreateImporter(models).ImportFileAsync(file, null, move: true, "datei:" + file, null, new SyncProgress(reports.Add), Ct);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(model.ModelPath));
        Assert.Equal(1.0, reports[^1], 3);
    }

    [Fact]
    public async Task SameName_GetsSecondFolder()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        var file = source.Combine("Test.gguf");
        GgufTestFile.Write(file);
        var importer = CreateImporter(models);

        await importer.ImportFileAsync(file, null, move: false, "datei", null, null, Ct);
        var second = await importer.ImportFileAsync(file, null, move: false, "datei", null, null, Ct);

        Assert.Equal("test-2", second.FolderName);
    }

    [Fact]
    public async Task ProjectorAlone_IsRejected()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        var projector = source.Combine("mmproj.gguf");
        GgufTestFile.Write(projector, architecture: "clip", type: "mmproj");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateImporter(models).ImportFileAsync(projector, null, false, "datei", null, null, Ct));
        Assert.Empty(Directory.EnumerateDirectories(models.Path));
    }

    [Fact]
    public async Task ModelAsProjector_IsRejected()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        GgufTestFile.Write(source.Combine("a.gguf"));
        GgufTestFile.Write(source.Combine("b.gguf"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateImporter(models).ImportFileAsync(source.Combine("a.gguf"), source.Combine("b.gguf"), false, "datei", null, null, Ct));
    }

    [Fact]
    public async Task MissingSplitPart_IsReported()
    {
        using var source = new TempDirectory();
        using var models = new TempDirectory();
        GgufTestFile.Write(source.Combine("Big-00001-of-00002.gguf"));

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CreateImporter(models).ImportFileAsync(source.Combine("Big-00001-of-00002.gguf"), null, false, "datei", null, null, Ct));
        Assert.Contains("00002", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_FromReadOnlyFolder_IsRefused_AndOriginalStays()
    {
        using var ollama = new TempDirectory();
        using var models = new TempDirectory();
        var blob = ollama.Combine("blob.gguf");
        GgufTestFile.Write(blob);
        var importer = new ModelImporter(new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance), [ollama.Path], NullLogger<ModelImporter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportFileAsync(blob, null, move: true, "datei", null, null, Ct));
        Assert.True(File.Exists(blob));

        var copy = await importer.ImportFileAsync(blob, null, move: false, "datei", null, null, Ct);
        Assert.True(File.Exists(copy.ModelPath));
    }

    [Fact]
    public void Delete_OnlyInsideModelsFolder()
    {
        using var models = new TempDirectory();
        using var outside = new TempDirectory();
        var library = new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance);
        var folder = library.CreateFolder("weg");
        File.WriteAllText(Path.Combine(folder, "x.gguf"), "x");

        library.Delete(new LocalModel("weg", folder, new ModelManifest()));
        Assert.False(Directory.Exists(folder));

        Assert.Throws<InvalidOperationException>(() => library.Delete(new LocalModel("fremd", outside.Path, new ModelManifest())));
        Assert.True(Directory.Exists(outside.Path));
    }

    private static ModelImporter CreateImporter(TempDirectory models) =>
        new(new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance), [], NullLogger<ModelImporter>.Instance);

    /// <summary>Progress&lt;T&gt; meldet über den Thread-Pool, zu spät für Asserts. Diese Variante sofort.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public class SecretStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrip_WithoutPlainText()
    {
        using var temp = new TempDirectory();
        var store = new SecretStore(temp.Combine("hf-token.bin"));

        store.Save("hf_geheim123");

        Assert.True(store.HasSecret);
        Assert.Equal("hf_geheim123", store.Load());
        var raw = File.ReadAllBytes(temp.Combine("hf-token.bin"));
        Assert.DoesNotContain("hf_geheim123", System.Text.Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
        Assert.DoesNotContain("hf_geheim123", System.Text.Encoding.Unicode.GetString(raw), StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenFile_GivesNull_AndDeleteRemovesIt()
    {
        using var temp = new TempDirectory();
        var store = new SecretStore(temp.Combine("hf-token.bin"));
        File.WriteAllBytes(temp.Combine("hf-token.bin"), [1, 2, 3]);

        Assert.Null(store.Load());

        store.Delete();
        Assert.False(store.HasSecret);
        Assert.Null(store.Load());
    }
}

public class TestLoadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestLoad_LoadsAndUnloadsAgain()
    {
        var llama = new FakeBackend(EngineKind.LlamaCpp);
        var manager = new EngineManager([llama], NullLogger<EngineManager>.Instance, new FakeTimeProvider());

        await manager.TestLoadAsync(new KiProfile { Id = "ladetest", DisplayName = "Ladetest" }, null, Ct);

        Assert.Equal(1, llama.LoadCount);
        Assert.Equal(1, llama.UnloadCount);
        Assert.Null(manager.LoadedKi);
        Assert.Equal(EngineManagerState.Unloaded, manager.State);
    }

    [Fact]
    public async Task TestLoad_Failure_IsReported()
    {
        var llama = new FakeBackend(EngineKind.LlamaCpp) { FailLoad = true };
        var manager = new EngineManager([llama], NullLogger<EngineManager>.Instance, new FakeTimeProvider());

        await Assert.ThrowsAsync<InferenceException>(() => manager.TestLoadAsync(new KiProfile { Id = "ladetest" }, null, Ct));
        Assert.Null(manager.LoadedKi);
    }
}
