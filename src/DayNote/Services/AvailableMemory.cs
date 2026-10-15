using System.Runtime.InteropServices;

namespace DayNote.Services;

/// <summary>
/// The memory the system can give an operation now, for comparing an operation's estimated cost with the
/// machine before it starts (volume-handling-conventions, Memory estimates). On macOS the free figure
/// leaves out memory the system reclaims on demand, so inactive, purgeable and speculative pages count
/// too; Windows reports available physical memory, which already includes its standby list, and Linux
/// its own MemAvailable estimate. Null when the platform will not say, and then no estimate is made.
/// </summary>
internal static class AvailableMemory
{
    internal static long? Bytes()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return MacBytes();
            if (OperatingSystem.IsWindows())
                return WindowsBytes();
            if (OperatingSystem.IsLinux())
                return LinuxBytes();
        }
        catch (Exception)
        {
            // A platform that will not answer means no estimate, not a failure of the operation.
        }

        return null;
    }

    /// <summary>
    /// Whether an operation costing <paramref name="cost"/> bytes may start: it takes at most half of what
    /// the system can give now, so the rest of the system keeps room and nothing is pushed into swap.
    /// With no figure from the platform, the operation starts and its own failure handling contains it.
    /// </summary>
    internal static bool Fits(long cost, long? available) => available is not { } bytes || cost <= bytes / 2;

    private static long? MacBytes()
    {
        var count = (uint)(Marshal.SizeOf<VmStatistics64>() / sizeof(int));
        if (host_statistics64(mach_host_self(), HostVmInfo64, out var stats, ref count) != 0)
            return null;
        long pages = (long)stats.FreeCount + stats.InactiveCount + stats.PurgeableCount + stats.SpeculativeCount;
        return pages * Environment.SystemPageSize;
    }

    private static long? WindowsBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : null;
    }

    private static long? LinuxBytes()
    {
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                continue;
            var kilobytes = line["MemAvailable:".Length..].Trim().Split(' ')[0];
            return long.TryParse(kilobytes, out var value) ? value * 1024 : null;
        }

        return null;
    }

    private const int HostVmInfo64 = 4;

    // vm_statistics64 from <mach/vm_statistics.h>, field for field.
    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics64
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public ulong ZeroFillCount;
        public ulong Reactivations;
        public ulong Pageins;
        public ulong Pageouts;
        public ulong Faults;
        public ulong CowFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong Swapins;
        public ulong Swapouts;
        public uint CompressorPageCount;
        public uint ThrottledCount;
        public uint ExternalPageCount;
        public uint InternalPageCount;
        public ulong TotalUncompressedPagesInCompressor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics64(uint host, int flavor, out VmStatistics64 info, ref uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
