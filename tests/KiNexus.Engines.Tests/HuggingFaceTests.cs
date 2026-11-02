using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KiNexus.Core.Models;
using KiNexus.Core.Tests;
using KiNexus.Engines.HuggingFace;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

public class HuggingFaceProtocolTests
{
    [Fact]
    public void SearchPath_FiltersGguf_AndEscapesQuery()
    {
        var path = HuggingFaceProtocol.SearchPath("qwen3 0.6b", 20);

        Assert.StartsWith("api/models?filter=gguf&sort=downloads&direction=-1&limit=20", path, StringComparison.Ordinal);
        Assert.Contains("&search=qwen3%200.6b", path, StringComparison.Ordinal);
        Assert.Contains("&expand[]=cardData", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePath_EscapesEachPart()
    {
        Assert.Equal("Qwen/Qwen3-0.6B-GGUF/resolve/main/Q4%20K/modell.gguf",
            HuggingFaceProtocol.ResolvePath("Qwen/Qwen3-0.6B-GGUF", "Q4 K/modell.gguf"));
    }

    [Fact]
    public void ParseSearch_ReadsCountsDateGatedAndLicense()
    {
        using var json = JsonDocument.Parse("""
            [
              { "_id": "1", "id": "Qwen/Qwen3-0.6B-GGUF", "downloads": 12345, "likes": 67, "lastModified": "2025-05-01T10:00:00.000Z",
                "gated": false, "cardData": { "license": "apache-2.0" }, "pipeline_tag": "text-generation" },
              { "_id": "2", "id": "google/gemma-3-4b-it-qat-q4_0-gguf", "downloads": 5, "likes": 1, "gated": "manual",
                "cardData": { "license": ["gemma", "other"] } },
              { "_id": "3" }
            ]
            """);

        var models = HuggingFaceProtocol.ParseSearch(json.RootElement);

        Assert.Equal(2, models.Count);
        Assert.Equal("Qwen", models[0].Author);
        Assert.Equal("Qwen3-0.6B-GGUF", models[0].Name);
        Assert.Equal(12345, models[0].Downloads);
        Assert.Equal(2025, models[0].LastModified!.Value.Year);
        Assert.False(models[0].Gated);
        Assert.Equal("apache-2.0", models[0].License);
        Assert.True(models[1].Gated);
        Assert.Equal("gemma, other", models[1].License);
    }

    [Fact]
    public void ParseSearch_ReadsTagsAndGgufArchitecture()
    {
        using var json = JsonDocument.Parse("""
            [
              { "id": "test/video-gguf", "tags": ["gguf", "ltx-video"], "gguf": { "total": 21005004544, "architecture": "ltxv" } },
              { "id": "test/chat-gguf", "pipeline_tag": "text-generation", "gguf": { "architecture": "qwen3", "context_length": 40960 } }
            ]
            """);

        var models = HuggingFaceProtocol.ParseSearch(json.RootElement);

        Assert.Equal(["gguf", "ltx-video"], models[0].Tags);
        Assert.Equal("ltxv", models[0].GgufArchitecture);
        Assert.Null(models[1].Tags);
        Assert.Equal("qwen3", models[1].GgufArchitecture);
    }

    [Fact]
    public void SearchPath_AsksForTagsAndGgufHeader()
    {
        var path = HuggingFaceProtocol.SearchPath("qwen", 20);

        Assert.Contains("expand[]=tags", path, StringComparison.Ordinal);
        Assert.Contains("expand[]=gguf", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseTree_ReadsSizeAndSha256_SkipsFolders()
    {
        using var json = JsonDocument.Parse("""
            [
              { "type": "directory", "path": "Q8_0", "size": 0 },
              { "type": "file", "path": "Qwen3-0.6B-Q8_0.gguf", "size": 639446688,
                "lfs": { "oid": "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031", "size": 639446688 } },
              { "type": "file", "path": "README.md", "size": 1200 }
            ]
            """);

        var files = HuggingFaceProtocol.ParseTree(json.RootElement);

        Assert.Equal(2, files.Count);
        Assert.Equal("9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031", files[0].Sha256);
        Assert.Null(files[1].Sha256);
    }

    [Theory]
    [InlineData("Qwen3-0.6B-Q8_0.gguf", "Q8_0")]
    [InlineData("gemma-3-4b-it-q4_k_m.gguf", "Q4_K_M")]
    [InlineData("Qwen3-30B-A3B-UD-Q4_K_XL.gguf", "UD-Q4_K_XL")]
    [InlineData("Model-IQ4_XS-00001-of-00002.gguf", "IQ4_XS")]
    [InlineData("gpt-oss-20b-MXFP4.gguf", "MXFP4")]
    [InlineData("Llama-3.2-1B-Instruct-f16.gguf", "F16")]
    [InlineData("Q5_K_M/modell-00001-of-00003.gguf", "Q5_K_M")]
    [InlineData("BF16/modell.gguf", "BF16")]
    public void QuantLabel_FindsQuantization(string path, string expected)
    {
        Assert.Equal(expected, HuggingFaceProtocol.QuantLabel(path));
    }

    [Fact]
    public void Group_JoinsSplitParts_SeparatesProjectors_AndRecommendsQ4KM()
    {
        HfFile[] files =
        [
            new("Model-Q8_0.gguf", 800, "a"),
            new("Model-Q4_K_M.gguf", 400, "b"),
            new("Model-Q2_K.gguf", 250, "c"),
            new("BF16/Model-BF16-00001-of-00002.gguf", 1000, "d"),
            new("BF16/Model-BF16-00002-of-00002.gguf", 600, "e"),
            new("mmproj-Model-Q8_0.gguf", 50, "f"),
            new("mmproj-Model-F16.gguf", 90, "g"),
            new("mmproj-Model-BF16.gguf", 90, "h"),
            new("README.md", 10, null),
        ];

        var grouped = HuggingFaceProtocol.Group("x/Model-GGUF", files);

        Assert.Equal(["Q2_K", "Q4_K_M", "Q8_0", "BF16"], grouped.Options.Select(o => o.Label));
        var recommended = Assert.Single(grouped.Options, o => o.IsRecommended);
        Assert.Equal("Q4_K_M", recommended.Label);
        var split = grouped.Options[^1];
        Assert.True(split.IsSplit);
        Assert.Equal(1600, split.TotalBytes);
        Assert.Equal("mmproj-Model-F16.gguf", grouped.Projectors[0].FileName);
        Assert.Equal(3, grouped.Projectors.Count);
    }

    [Fact]
    public void Group_WithoutQ4KM_RecommendsNextBest()
    {
        var grouped = HuggingFaceProtocol.Group("x/y", [new("Qwen3-0.6B-Q8_0.gguf", 600, "a"), new("Qwen3-0.6B-F16.gguf", 1200, "b")]);

        Assert.Equal("Q8_0", Assert.Single(grouped.Options, o => o.IsRecommended).Label);
    }

    [Fact]
    public void Group_SameQuantSeveralTimes_AddsFolderOrFileName()
    {
        // Wie bei unsloth/LTX-2.3-GGUF: dieselbe Stufe als dev (oben) und als distilled (Unterordner).
        var grouped = HuggingFaceProtocol.Group("unsloth/LTX-2.3-GGUF",
        [
            new("ltx-2.3-22b-dev-Q2_K.gguf", 771, "a"),
            new("distilled/ltx-2.3-22b-distilled-Q2_K.gguf", 772, "b"),
            new("distilled-1.1/ltx-2.3-22b-distilled-1.1-Q2_K.gguf", 739, "c"),
            new("ltx-2.3-22b-dev-Q4_K_M.gguf", 1334, "d"),
            new("distilled-1.1/ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf", 1322, "e"),
        ]);

        Assert.Equal(["Q2_K (distilled-1.1)", "Q2_K (ltx-2.3-22b-dev)", "Q2_K (distilled)", "Q4_K_M (distilled-1.1)", "Q4_K_M (ltx-2.3-22b-dev)"],
            grouped.Options.Select(o => o.Label));
        Assert.Equal("Q4_K_M (distilled-1.1)", Assert.Single(grouped.Options, o => o.IsRecommended).Label);
    }
}

public class HuggingFaceClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_IsSentAsBearer_OnlyWhenStored()
    {
        var handler = new FakeHttp((request, _) => FakeHttp.Json("[]"));
        string? token = null;
        using var client = new HuggingFaceClient(() => token, handler);

        await client.SearchAsync("qwen", 5, Ct);
        token = "hf_test";
        await client.SearchAsync("qwen", 5, Ct);

        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Equal("Bearer hf_test", handler.Requests[1].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Forbidden_GivesGatedHint()
    {
        using var client = new HuggingFaceClient(() => null, new FakeHttp((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var error = await Assert.ThrowsAsync<HuggingFaceException>(() => client.GetFilesAsync("meta/gesperrt", Ct));
        Assert.Contains("Token", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WebPage_PointsToRepo()
    {
        using var client = new HuggingFaceClient(() => null);
        Assert.Equal("https://huggingface.co/Qwen/Qwen3-0.6B-GGUF", client.WebPage("Qwen/Qwen3-0.6B-GGUF").ToString());
    }
}

public class ModelDownloaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Download_VerifiesChecksum_MovesToModels_AndWritesManifest()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content);
        var (downloader, _) = Create(models, server);

        var job = downloader.Start(Plan(content));
        await job.Worker;

        Assert.Equal(DownloadState.Completed, job.State);
        Assert.Equal(content.Length, job.BytesDone);
        var model = job.Result!;
        Assert.Equal("test-q4_k_m", model.FolderName);
        Assert.Equal(content, File.ReadAllBytes(model.ModelPath));
        Assert.Equal("huggingface:x/Test-GGUF/Test-Q4_K_M.gguf", model.Manifest.Source);
        Assert.Equal("mit", model.Manifest.License);
        Assert.False(Directory.Exists(Path.Combine(models.Path, ModelLibrary.DownloadsFolderName, "test-q4_k_m")));
    }

    [Fact]
    public async Task BrokenConnection_ThenResume_ContinuesWithRange()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { BreakAfterBytes = 100 };
        var (downloader, _) = Create(models, server);

        var job = downloader.Start(Plan(content));
        await job.Worker;
        Assert.Equal(DownloadState.Failed, job.State);
        Assert.Equal(100, job.BytesDone);

        server.BreakAfterBytes = null;
        downloader.Resume(job);
        await job.Worker;

        Assert.Equal(DownloadState.Completed, job.State);
        Assert.Equal(100, server.RangeStarts[^1]);
        Assert.Equal(content, File.ReadAllBytes(job.Result!.ModelPath));
    }

    [Fact]
    public async Task Pause_KeepsPartFile_ResumeFinishes()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { HangAfterBytes = 64 };
        var (downloader, _) = Create(models, server);

        var job = downloader.Start(Plan(content));
        await server.Hanging.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        downloader.Pause(job);
        await job.Worker;

        Assert.Equal(DownloadState.Paused, job.State);
        Assert.Equal(64, job.BytesDone);

        server.HangAfterBytes = null;
        downloader.Resume(job);
        await job.Worker;
        Assert.Equal(DownloadState.Completed, job.State);
        Assert.Equal(64, server.RangeStarts[^1]);
    }

    [Fact]
    public async Task StalledConnection_IsRetried_FromSameByte()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { HangAfterBytes = 50 };
        var (downloader, _) = Create(models, server, stallTimeout: TimeSpan.FromMilliseconds(300));

        var job = downloader.Start(Plan(content));
        await server.Hanging.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        server.HangAfterBytes = null;
        await job.Worker.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(DownloadState.Completed, job.State);
        Assert.Equal([0L, 50L], server.RangeStarts);
        Assert.Equal(content, File.ReadAllBytes(job.Result!.ModelPath));
    }

    [Fact]
    public async Task BrokenConnection_IsRetried_ThenGivesUp()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { BreakAfterBytes = 0 };
        var (downloader, _) = Create(models, server, retries: true);

        var job = downloader.Start(Plan(content));
        await job.Worker.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(DownloadState.Failed, job.State);
        Assert.Equal(5, server.RangeStarts.Count);
    }

    [Fact]
    public async Task WrongChecksum_FailsAndDeletesPart()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var (downloader, _) = Create(models, new FakeFileServer(content));
        var plan = Plan(content) with { ModelFiles = [new HfFile("Test-Q4_K_M.gguf", content.Length, new string('0', 64))] };

        var job = downloader.Start(plan);
        await job.Worker;

        Assert.Equal(DownloadState.Failed, job.State);
        Assert.Contains("Prüfsumme", job.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(job.StagingDirectory, "*.part"));
        Assert.Empty(new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance).Scan());
    }

    [Fact]
    public async Task Cancel_DeletesStaging()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { HangAfterBytes = 10 };
        var (downloader, _) = Create(models, server);

        var job = downloader.Start(Plan(content));
        await server.Hanging.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await downloader.CancelAsync(job);

        Assert.Equal(DownloadState.Cancelled, job.State);
        Assert.False(Directory.Exists(job.StagingDirectory));
        Assert.Empty(downloader.Jobs);
    }

    [Fact]
    public async Task RestorePending_FindsUnfinishedDownloads()
    {
        using var models = new TempDirectory();
        var content = GgufBytes(models);
        var server = new FakeFileServer(content) { BreakAfterBytes = 30 };
        var (first, library) = Create(models, server);
        await first.Start(Plan(content)).Worker;

        var (second, _) = Create(models, server);
        second.RestorePending();

        var job = Assert.Single(second.Jobs);
        Assert.Equal(DownloadState.Paused, job.State);
        Assert.Equal(30, job.BytesDone);
        Assert.Equal("x/Test-GGUF", job.Plan.Repo);
        Assert.Empty(library.Scan());
    }

    private static DownloadPlan Plan(byte[] content) =>
        new("x/Test-GGUF", "Q4_K_M", [new HfFile("Test-Q4_K_M.gguf", content.Length, Convert.ToHexStringLower(SHA256.HashData(content)))], null, "mit");

    /// <param name="retries">False = beim ersten Fehler aufgeben (wie früher), damit Tests den Fehler-Zustand sehen.</param>
    private static (ModelDownloader Downloader, ModelLibrary Library) Create(TempDirectory models, FakeFileServer server,
        TimeSpan? stallTimeout = null, bool retries = false)
    {
        var library = new ModelLibrary(models.Path, NullLogger<ModelLibrary>.Instance);
        var client = new HuggingFaceClient(() => null, new FakeHttp(server.Handle));
        var downloader = new ModelDownloader(client, library, NullLogger<ModelDownloader>.Instance)
        {
            StallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30),
            RetryDelay = TimeSpan.FromMilliseconds(10),
            MaxAttempts = retries || stallTimeout is not null ? 5 : 1,
        };
        return (downloader, library);
    }

    /// <summary>Eine kleine, echte GGUF-Datei (nur Kopf), damit model.json geschrieben werden kann.</summary>
    private static byte[] GgufBytes(TempDirectory temp)
    {
        var path = temp.Combine("vorlage.bin");
        GgufTestFile.Write(path);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }
}

/// <summary>Ersetzt das Netzwerk: Jede Anfrage geht an eine Funktion.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request, cancellationToken));
    }
}

/// <summary>Spielt einen Download-Server mit Range-Unterstützung. Kann die Verbindung abbrechen oder hängen lassen.</summary>
internal sealed class FakeFileServer(byte[] content)
{
    public int? BreakAfterBytes { get; set; }

    public int? HangAfterBytes { get; set; }

    public List<long> RangeStarts { get; } = [];

    public TaskCompletionSource Hanging { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HttpResponseMessage Handle(HttpRequestMessage request, CancellationToken ct)
    {
        var start = request.Headers.Range?.Ranges.First().From ?? 0;
        RangeStarts.Add(start);
        var body = new FlakyStream(content.AsMemory((int)start), BreakAfterBytes, HangAfterBytes, Hanging);
        return new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new StreamContent(body) };
    }

    private sealed class FlakyStream(ReadOnlyMemory<byte> data, int? breakAfter, int? hangAfter, TaskCompletionSource hanging) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var limit = breakAfter ?? hangAfter ?? data.Length;
            if (_position >= limit && _position < data.Length)
            {
                if (breakAfter is not null)
                {
                    throw new IOException("Verbindung unterbrochen (Test).");
                }

                hanging.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var count = Math.Min(buffer.Length, Math.Min(limit, data.Length) - _position);
            data.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
