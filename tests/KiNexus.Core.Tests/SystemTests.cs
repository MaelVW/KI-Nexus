using KiNexus.Core.IO;
using KiNexus.Core.Processes;
using KiNexus.Core.Resources;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

/// <summary>Tests für die Windows-Teile: Hardlinks, Job Object, Speicher-Anzeige.</summary>
public class SystemTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void HardLink_SharesData_WithoutCopy()
    {
        using var temp = new TempDirectory();
        var original = temp.Combine("original.bin");
        var link = temp.Combine("link.bin");
        File.WriteAllText(original, "Daten");

        FileLinks.CreateHardLink(link, original);

        Assert.True(FileLinks.AreSameFile(original, link));
        Assert.Equal(2, FileLinks.GetLinkCount(original));
        Assert.Equal("Daten", File.ReadAllText(link));
    }

    [Fact]
    public void AreSameFile_ReturnsFalse_ForCopies()
    {
        using var temp = new TempDirectory();
        var original = temp.Combine("a.bin");
        var copy = temp.Combine("b.bin");
        File.WriteAllText(original, "Daten");
        File.Copy(original, copy);

        Assert.False(FileLinks.AreSameFile(original, copy));
    }

    [Fact]
    public async Task Supervisor_Dispose_KillsChildProcesses()
    {
        var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        using var process = supervisor.Start(ping, ["-n", "60", "127.0.0.1"]);
        Assert.False(process.HasExited);

        supervisor.Dispose();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Supervisor_Stop_EndsProcess()
    {
        using var supervisor = new ProcessSupervisor(NullLogger<ProcessSupervisor>.Instance);
        using var process = supervisor.Start(Path.Combine(Environment.SystemDirectory, "ping.exe"), ["-n", "60", "127.0.0.1"]);

        await supervisor.StopAsync(process, Ct);

        Assert.True(process.HasExited);
    }

    [Fact]
    public void ResourceSnapshot_FormatsGigabytes()
    {
        const ulong gb = 1024UL * 1024 * 1024;
        var snapshot = new ResourceSnapshot(VramUsed: 7 * gb, VramTotal: 8 * gb, RamUsed: 27 * gb, RamTotal: 32 * gb);

        Assert.Equal("7.0 / 8.0 GB", snapshot.VramText);
        Assert.Equal("27.0 / 32.0 GB", snapshot.RamText);
        Assert.False(snapshot.IsVramHigh);
        Assert.True(snapshot with { VramUsed = 15 * gb / 2 } is { IsVramHigh: true });
    }

    [Fact]
    public void ResourceSnapshot_WithoutNvidia_ShowsDash()
    {
        var snapshot = new ResourceSnapshot(null, null, 1, 2);

        Assert.Equal("–", snapshot.VramText);
    }

    [Fact]
    public void ResourceMonitor_ReadsRam()
    {
        using var monitor = new ResourceMonitor(NullLogger<ResourceMonitor>.Instance);

        var snapshot = monitor.Read();

        Assert.True(snapshot.RamTotal > 0);
        Assert.InRange(snapshot.RamUsed, 1UL, snapshot.RamTotal);
    }
}
