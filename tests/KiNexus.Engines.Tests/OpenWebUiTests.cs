using System.Net;
using System.Net.Sockets;
using KiNexus.Core.Processes;
using KiNexus.Core.Tests;
using KiNexus.Engines.OpenWebUi;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Engines.Tests;

public class OpenWebUiCommandTests
{
    private static OpenWebUiOptions Options(string? version = null) =>
        new(@"C:\uv\uvx.exe", @"C:\Users\mael\ki\open-webui", 8080, 11434, 8090, version, StopOnExit: true);

    [Fact]
    public void Arguments_LikeByHand_ButOnlyLocal()
    {
        Assert.Equal(["--python", "3.11", "open-webui@latest", "serve", "--host", "127.0.0.1", "--port", "8080"],
            OpenWebUiCommand.Arguments(Options()));
    }

    [Fact]
    public void PinnedVersion_IsUsed()
    {
        Assert.Equal("open-webui@0.12.5", OpenWebUiCommand.Arguments(Options("0.12.5"))[2]);
        Assert.Equal("open-webui@latest", OpenWebUiCommand.Package("  "));
    }

    [Theory]
    [InlineData("0.12.5", true)]
    [InlineData("1.0", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("latest", false)]
    [InlineData("0.12.5; rm -rf", false)]
    [InlineData("v0.12", false)]
    public void Versions_AreChecked(string? version, bool valid)
    {
        Assert.Equal(valid, OpenWebUiCommand.IsValidVersion(version));
    }

    [Fact]
    public void InvalidVersion_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => OpenWebUiCommand.Arguments(Options("0.12; echo")));
    }

    [Fact]
    public void Environment_DataDirTelemetryAndOllama()
    {
        var environment = OpenWebUiCommand.Environment(Options(), freshDataDirectory: false);

        Assert.Equal(@"C:\Users\mael\ki\open-webui", environment["DATA_DIR"]);
        Assert.Equal("http://127.0.0.1:11434", environment["OLLAMA_BASE_URL"]);
        Assert.Equal("true", environment["SCARF_NO_ANALYTICS"]);
        Assert.Equal("true", environment["DO_NOT_TRACK"]);
        Assert.Equal("false", environment["ANONYMIZED_TELEMETRY"]);
        Assert.False(environment.ContainsKey("OPENAI_API_BASE_URL"));
    }

    [Fact]
    public void FreshDataDirectory_ConnectsLlamaServer()
    {
        var environment = OpenWebUiCommand.Environment(Options(), freshDataDirectory: true);

        Assert.Equal("http://127.0.0.1:8090/v1", environment["OPENAI_API_BASE_URL"]);
        Assert.Equal("none", environment["OPENAI_API_KEY"]);
    }

    [Fact]
    public void WorkingDirectory_UsesExistingSecretKey_OlderWins()
    {
        using var temp = new TempDirectory();
        var data = Directory.CreateDirectory(temp.Combine("open-webui")).FullName;

        Assert.Equal(data, OpenWebUiCommand.WorkingDirectory(data));

        File.WriteAllText(temp.Combine(".webui_secret_key"), "alt");
        File.SetLastWriteTimeUtc(temp.Combine(".webui_secret_key"), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(temp.Path, OpenWebUiCommand.WorkingDirectory(data));

        File.WriteAllText(Path.Combine(data, ".webui_secret_key"), "neu");
        Assert.Equal(temp.Path, OpenWebUiCommand.WorkingDirectory(data));
    }

    [Fact]
    public void StripAnsi_RemovesColorCodes()
    {
        Assert.Equal("INFO:     Started server process [1234]", OpenWebUiCommand.StripAnsi("\u001b[32mINFO\u001b[0m:     Started server process [1234]"));
    }
}

public class UvxLocatorTests
{
    [Fact]
    public void FindsUvxInPath_ThenInLocalBin()
    {
        using var temp = new TempDirectory();
        var inPath = Directory.CreateDirectory(temp.Combine("tools")).FullName;
        var profile = Directory.CreateDirectory(temp.Combine("profile")).FullName;
        Directory.CreateDirectory(Path.Combine(profile, ".local", "bin"));
        File.WriteAllText(Path.Combine(profile, ".local", "bin", "uvx.exe"), "");

        Assert.Equal(Path.Combine(profile, ".local", "bin", "uvx.exe"), UvxLocator.Find(inPath, profile));

        File.WriteAllText(Path.Combine(inPath, "uvx.exe"), "");
        Assert.Equal(Path.Combine(inPath, "uvx.exe"), UvxLocator.Find(inPath, profile));
    }

    [Fact]
    public void Missing_GivesNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(UvxLocator.Find(temp.Path, temp.Path));
    }
}

public class OpenWebUiLauncherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PortUsedByOpenWebUi_CountsAsRunningExternally()
    {
        using var temp = new TempDirectory();
        var port = FreePort();
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        _ = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                var context = await server.GetContextAsync();
                context.Response.StatusCode = context.Request.Url!.AbsolutePath == "/health" ? 200 : 404;
                context.Response.Close();
            }
        }, Ct);
        await using var launcher = CreateLauncher(temp);

        await launcher.StartAsync(Options(temp, port), Ct);

        Assert.Equal(OpenWebUiState.Running, launcher.State);
        Assert.True(launcher.IsExternal);
        server.Stop();
    }

    [Fact]
    public async Task PortUsedBySomethingElse_Fails_WithoutStarting()
    {
        using var temp = new TempDirectory();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var launcher = CreateLauncher(temp);

        await launcher.StartAsync(Options(temp, port), Ct);

        Assert.Equal(OpenWebUiState.Failed, launcher.State);
        Assert.Contains($"Port {port}", launcher.StatusText, StringComparison.Ordinal);
        listener.Stop();
    }

    [Fact]
    public async Task Detect_StaleProcessFile_IsRemoved()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("open-webui.processes"), "999999;12345\nkaputt");
        await using var launcher = CreateLauncher(temp);

        await launcher.DetectAsync(FreePort(), Ct);

        Assert.False(File.Exists(temp.Combine("open-webui.processes")));
        Assert.Equal(OpenWebUiState.Stopped, launcher.State);
    }

    [Fact]
    public async Task Dispose_Twice_IsHarmless()
    {
        using var temp = new TempDirectory();
        var launcher = CreateLauncher(temp);

        await launcher.DisposeAsync();
        await launcher.DisposeAsync();
    }

    [Fact]
    public async Task Detect_NothingRunning_StaysStopped()
    {
        using var temp = new TempDirectory();
        await using var launcher = CreateLauncher(temp);

        await launcher.DetectAsync(FreePort(), Ct);

        Assert.Equal(OpenWebUiState.Stopped, launcher.State);
    }

    private static OpenWebUiLauncher CreateLauncher(TempDirectory temp) =>
        new(new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance), NullLogger<OpenWebUiLauncher>.Instance,
            temp.Combine("logs", "open-webui.log"), temp.Combine("open-webui.processes"));

    private static OpenWebUiOptions Options(TempDirectory temp, int port) =>
        new(temp.Combine("uvx.exe"), temp.Combine("data"), port, 11434, 8090, null, StopOnExit: true);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public class JobObjectTests
{
    [Fact]
    public void Terminate_EndsChildrenAndGrandchildren()
    {
        using var job = new JobObject(killOnClose: true);
        // cmd startet ping als Kind. Beide müssen mit dem Job enden.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        job.Assign(process);
        Thread.Sleep(500);
        Assert.True(job.ActiveProcessCount >= 1);

        job.Terminate();

        Assert.True(process.WaitForExit(5000));
        for (var i = 0; i < 25 && job.ActiveProcessCount > 0; i++)
        {
            Thread.Sleep(100);
        }

        Assert.Equal(0, job.ActiveProcessCount);
    }

    [Fact]
    public void ProcessIds_ListsChildrenToo()
    {
        using var job = new JobObject(killOnClose: true);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        job.Assign(process);
        Thread.Sleep(700);

        var ids = job.ProcessIds();

        Assert.Contains(process.Id, ids);
        Assert.True(ids.Count >= 2, "cmd und ping sollten beide im Job sein.");
        job.Terminate();
    }
}
