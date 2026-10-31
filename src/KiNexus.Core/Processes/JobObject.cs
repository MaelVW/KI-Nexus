using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KiNexus.Core.Processes;

/// <summary>
/// Ein Windows "Job Object": eine Gruppe von Prozessen. Was ein Prozess darin startet, gehört automatisch auch dazu.
/// <list type="bullet">
/// <item>Mit "beim Schliessen beenden" (Standard) enden alle Prozesse, sobald das Job Object geschlossen wird.
/// Das passiert auch, wenn KI-Nexus abstürzt: Windows schliesst dann alle Handles der App.
/// So bleibt nie ein llama-server im Hintergrund hängen und belegt die Grafikkarte.</item>
/// <item><see cref="Terminate"/> beendet alle Prozesse auf einmal, auch Enkel, deren Eltern schon weg sind
/// (z.B. uvx → uv → open-webui → python).</item>
/// <item><see cref="ProcessIds"/> listet alle Prozesse darin, z.B. um sie nach einem Neustart von KI-Nexus wiederzufinden.
/// (Einen Namen bräuchte es dafür nicht: Windows vergisst den Namen, sobald kein Handle mehr offen ist.)</item>
/// </list>
/// </summary>
public sealed class JobObject : IDisposable
{
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectBasicProcessIdList = 3;
    private const int MaxListedProcesses = 256;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly IntPtr _handle;
    private bool _disposed;

    /// <param name="killOnClose">True: Schliessen beendet alle Prozesse darin.</param>
    public JobObject(bool killOnClose = true)
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object konnte nicht erstellt werden.");
        }

        var info = new JobObjectExtendedLimitInformationData
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = killOnClose ? JobObjectLimitKillOnJobClose : 0 },
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformationData>();
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            CloseHandle(_handle);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object konnte nicht eingerichtet werden.");
        }
    }

    /// <summary>Wie viele Prozesse gerade darin laufen.</summary>
    public int ActiveProcessCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return QueryInformationJobObject(_handle, JobObjectBasicAccountingInformation, out var info,
                (uint)Marshal.SizeOf<JobObjectBasicAccountingInformationData>(), IntPtr.Zero)
                ? (int)info.ActiveProcesses
                : 0;
        }
    }

    /// <summary>Die Prozess-Nummern (PIDs) aller Prozesse darin.</summary>
    public IReadOnlyList<int> ProcessIds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // JOBOBJECT_BASIC_PROCESS_ID_LIST: zwei Zahlen (4 Bytes), dann die Liste (je 8 Bytes, ab Byte 8).
        var size = 8 + (IntPtr.Size * MaxListedProcesses);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(_handle, JobObjectBasicProcessIdList, buffer, (uint)size, IntPtr.Zero))
            {
                return [];
            }

            var count = Marshal.ReadInt32(buffer, 4);
            var ids = new int[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = (int)Marshal.ReadIntPtr(buffer, 8 + (i * IntPtr.Size));
            }

            return ids;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Prozess konnte nicht ans Job Object gehängt werden.");
        }
    }

    /// <summary>Beendet alle Prozesse im Job Object.</summary>
    public void Terminate(uint exitCode = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TerminateJobObject(_handle, exitCode))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Prozesse im Job Object liessen sich nicht beenden.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseHandle(_handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformationData info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out JobObjectBasicAccountingInformationData info, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformationData
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
