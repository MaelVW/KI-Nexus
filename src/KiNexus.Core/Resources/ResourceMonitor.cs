using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Resources;

/// <summary>Eine Messung von Grafikspeicher (VRAM) und Arbeitsspeicher (RAM), in Bytes.</summary>
public sealed record ResourceSnapshot(ulong? VramUsed, ulong? VramTotal, ulong RamUsed, ulong RamTotal)
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    /// <summary>z.B. "6.5 / 8.0 GB". Ohne NVIDIA-Karte: "–".</summary>
    public string VramText => VramUsed is ulong used && VramTotal is ulong total ? Format(used, total) : "–";

    public string RamText => Format(RamUsed, RamTotal);

    /// <summary>Ab 90 % Belegung warnt die Anzeige (gelb).</summary>
    public bool IsVramHigh => VramUsed is ulong used && VramTotal is ulong total && total > 0 && used >= total * 0.9;

    public bool IsRamHigh => RamTotal > 0 && RamUsed >= RamTotal * 0.9;

    internal static string Format(ulong used, ulong total) =>
        string.Create(CultureInfo.InvariantCulture, $"{used / BytesPerGb:F1} / {total / BytesPerGb:F1} GB");
}

public interface IResourceMonitor
{
    ResourceSnapshot Read();
}

/// <summary>
/// Misst VRAM über NVML (kommt mit dem NVIDIA-Treiber) und RAM über Windows.
/// Lektion 3 im Bauplan: Wir messen selbst und verlassen uns nicht auf Schätzungen der Engines.
/// </summary>
public sealed class ResourceMonitor : IResourceMonitor, IDisposable
{
    private readonly ILogger<ResourceMonitor> _logger;
    private readonly Lock _lock = new();
    private IntPtr _device;
    private bool _nvmlReady;
    private bool _nvmlFailed;

    public ResourceMonitor(ILogger<ResourceMonitor> logger)
    {
        _logger = logger;
    }

    public ResourceSnapshot Read()
    {
        var (ramUsed, ramTotal) = ReadRam();
        var (vramUsed, vramTotal) = ReadVram();
        return new ResourceSnapshot(vramUsed, vramTotal, ramUsed, ramTotal);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_nvmlReady)
            {
                _ = Nvml.Shutdown();
                _nvmlReady = false;
            }
        }
    }

    private static (ulong Used, ulong Total) ReadRam()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? (status.TotalPhys - status.AvailPhys, status.TotalPhys)
            : (0, 0);
    }

    private (ulong? Used, ulong? Total) ReadVram()
    {
        lock (_lock)
        {
            if (!EnsureNvml())
            {
                return (null, null);
            }

            var memory = new Nvml.Memory();
            return Nvml.DeviceGetMemoryInfo(_device, ref memory) == 0 ? (memory.Used, memory.Total) : (null, null);
        }
    }

    private bool EnsureNvml()
    {
        if (_nvmlReady || _nvmlFailed)
        {
            return _nvmlReady;
        }

        try
        {
            _nvmlReady = Nvml.Init() == 0 && Nvml.DeviceGetHandleByIndex(0, out _device) == 0;
        }
        catch (DllNotFoundException)
        {
            _nvmlReady = false;
        }

        _nvmlFailed = !_nvmlReady;
        if (_nvmlFailed)
        {
            _logger.LogWarning("NVML nicht verfügbar. Die VRAM-Anzeige bleibt leer (keine NVIDIA-Karte oder kein Treiber?).");
        }

        return _nvmlReady;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    /// <summary>Die drei Funktionen aus nvml.dll, die wir brauchen.</summary>
    private static class Nvml
    {
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        public static extern int Init();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        public static extern int Shutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int DeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int DeviceGetMemoryInfo(IntPtr device, ref Memory memory);

        [StructLayout(LayoutKind.Sequential)]
        public struct Memory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }
    }
}
