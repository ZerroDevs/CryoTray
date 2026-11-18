using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;

namespace CryoTray.Services
{
    /// <summary>Reads physical, usable, and kernel page-list RAM telemetry via Win32 and ntdll APIs.</summary>
    public static class MemoryTelemetryService
    {
        private static readonly ulong PageSize = (ulong)Math.Max(4096, Environment.SystemPageSize);
        private static readonly Lazy<ulong> CachedInstalledBytes = new(ReadInstalledPhysicalBytes);
        private static int _pageListPrivilegeAttempted;

        private static ulong ReadInstalledPhysicalBytes()
        {
            try
            {
                if (NativeMethods.GetPhysicallyInstalledSystemMemory(out ulong kb) && kb > 0)
                {
                    return kb * 1024UL;
                }
            }
            catch
            {
            }

            return 0UL;
        }

        /// <summary>Reads physical installed, usable, and NT memory-list breakdown without WMI.</summary>
        public static bool TryReadBreakdown(out MemoryBreakdown breakdown)
        {
            breakdown = new MemoryBreakdown();

            var status = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
            };

            if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            {
                return false;
            }

            ulong totalUsable = status.ullTotalPhys;
            ulong available = Math.Min(status.ullAvailPhys, totalUsable);
            ulong inUse = totalUsable - available;
            ulong installed = Math.Max(CachedInstalledBytes.Value, totalUsable);
            ulong hwReserved = installed > totalUsable ? installed - totalUsable : 0UL;

            breakdown.InstalledBytes = installed;
            breakdown.TotalUsableBytes = totalUsable;
            breakdown.AvailableBytes = available;
            breakdown.InUseBytes = inUse;
            breakdown.HardwareReservedBytes = hwReserved;
            breakdown.MemoryLoadPercent = status.dwMemoryLoad;

            if (TryQuerySystemMemoryList(out var listInfo))
            {
                breakdown.FreeBytes = ((ulong)listInfo.ZeroPageCount + (ulong)listInfo.FreePageCount) * PageSize;
                breakdown.ModifiedBytes = ((ulong)listInfo.ModifiedPageCount + (ulong)listInfo.ModifiedNoWritePageCount) * PageSize;
                breakdown.StandbyBytes = SumStandbyPages(in listInfo) * PageSize;
                breakdown.HasPageListBreakdown = true;
            }

            return true;
        }

        private static bool TryQuerySystemMemoryList(out NativeMethods.SYSTEM_MEMORY_LIST_INFORMATION info)
        {
            int size = Marshal.SizeOf<NativeMethods.SYSTEM_MEMORY_LIST_INFORMATION>();
            int status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemMemoryListInformation,
                out info,
                size,
                out _);

            if ((status == NativeMethods.STATUS_PRIVILEGE_NOT_HELD || status == NativeMethods.STATUS_ACCESS_DENIED) &&
                Interlocked.Exchange(ref _pageListPrivilegeAttempted, 1) == 0)
            {
                _ = NativeMethods.RtlAdjustPrivilege(
                    NativeMethods.SeProfileSingleProcessPrivilege,
                    true,
                    false,
                    out _);

                status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    out info,
                    size,
                    out _);
            }

            return status == 0;
        }

        private static ulong SumStandbyPages(in NativeMethods.SYSTEM_MEMORY_LIST_INFORMATION info)
        {
            return (ulong)info.PageCountByPriority0
                 + (ulong)info.PageCountByPriority1
                 + (ulong)info.PageCountByPriority2
                 + (ulong)info.PageCountByPriority3
                 + (ulong)info.PageCountByPriority4
                 + (ulong)info.PageCountByPriority5
                 + (ulong)info.PageCountByPriority6
                 + (ulong)info.PageCountByPriority7;
        }

        /// <summary>Formats byte counts as GB (>= 1024 MB, 2 decimals) or MB (&lt; 1024 MB).</summary>
        public static string FormatBytes(ulong bytes)
        {
            const ulong OneGiB = 1024UL * 1024UL * 1024UL;
            const double OneMiB = 1024.0 * 1024.0;

            if (bytes >= OneGiB)
            {
                double gb = bytes / (double)OneGiB;
                return $"{gb.ToString("0.00", CultureInfo.InvariantCulture)} GB";
            }

            double mb = bytes / OneMiB;
            string format = mb > 0.0 && mb < 10.0 ? "0.00" : "0.0";
            return $"{mb.ToString(format, CultureInfo.InvariantCulture)} MB";
        }

        /// <summary>Computes overall CPU utilization % from two GetSystemTimes snapshots (kernel includes idle).</summary>
        internal static double ComputeCpuUsagePercent(
            ulong idle1, ulong kernel1, ulong user1,
            ulong idle2, ulong kernel2, ulong user2)
        {
            ulong idleDelta = idle2 >= idle1 ? idle2 - idle1 : 0UL;
            ulong kernelDelta = kernel2 >= kernel1 ? kernel2 - kernel1 : 0UL;
            ulong userDelta = user2 >= user1 ? user2 - user1 : 0UL;
            ulong totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0)
            {
                return 0.0;
            }

            ulong busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0UL;
            return Math.Clamp((busyDelta / (double)totalDelta) * 100.0, 0.0, 100.0);
        }

        /// <summary>Measures system-wide CPU utilization over the given window using GetSystemTimes.</summary>
        public static async Task<double?> MeasureCpuUsageAsync(TimeSpan sampleWindow, CancellationToken cancellationToken = default)
        {
            if (!NativeMethods.GetSystemTimes(out ulong idle1, out ulong kernel1, out ulong user1))
            {
                return null;
            }

            await Task.Delay(sampleWindow, cancellationToken).ConfigureAwait(false);

            if (!NativeMethods.GetSystemTimes(out ulong idle2, out ulong kernel2, out ulong user2))
            {
                return null;
            }

            return ComputeCpuUsagePercent(idle1, kernel1, user1, idle2, kernel2, user2);
        }
    }
}
