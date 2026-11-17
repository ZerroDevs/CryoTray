using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using CryoTray.Models;

namespace CryoTray.Services
{
    public class SystemInfoQueryService
    {
        private static SystemInfoQueryService? _instance;
        public static SystemInfoQueryService Instance => _instance ??= new SystemInfoQueryService();

        public SystemInfoData QuerySystemInfo()
        {
            var info = new SystemInfoData
            {
                PcName = Environment.MachineName
            };

            // 1. Processor
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    string? processorName = obj["Name"]?.ToString()?.Trim();
                    info.ProcessorName = string.IsNullOrWhiteSpace(processorName)
                        ? LocalizationService.Get("Common_NotReported")
                        : processorName;
                    if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores))
                    {
                        info.ProcessorCores = LocalizationService.Format("Main_CpuProcessor", cores);
                    }
                    break;
                }
            }
            catch
            {
                info.ProcessorName = LocalizationService.Get("Common_NotReported");
                info.ProcessorCores = string.Empty;
            }

            if (string.IsNullOrEmpty(info.ProcessorCores))
            {
                info.ProcessorCores = LocalizationService.Format("Main_CpuProcessor", Environment.ProcessorCount);
            }

            // 2. Memory
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                    {
                        double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                        info.Memory = $"{gb:0.00} GB";
                    }
                    break;
                }
            }
            catch
            {
                info.Memory = LocalizationService.Get("Common_NotReported");
            }

            // 3. Graphics card
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    string name = obj["Name"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        info.GraphicsCard = name;
                        break;
                    }
                }
            }
            catch
            {
                info.GraphicsCard = LocalizationService.Get("Common_NotReported");
            }

            // 4. Storage devices
            var telemetryDrives = HardwareMonitorService.Instance.CurrentSnapshot.StorageDrives;
            try
            {
                var drives = new List<StorageInfoItem>();
                var driveInfos = DriveInfo.GetDrives();
                int driveIndex = 0;
                using var driveSearcher = new ManagementObjectSearcher("SELECT Model, Size, Index, DeviceID FROM Win32_DiskDrive");
                using var driveResults = driveSearcher.Get();
                foreach (ManagementObject drive in driveResults)
                {
                    using var driveRow = drive;
                    string model = drive["Model"]?.ToString()?.Trim() ?? "SSD";
                    ulong sizeBytes = 0;
                    if (ulong.TryParse(drive["Size"]?.ToString(), out ulong sz)) sizeBytes = sz;
                    double sizeGb = Math.Round(sizeBytes / (1024.0 * 1024.0 * 1024.0), 2);

                    string type = model.ToUpperInvariant().Contains("NVME") || model.ToUpperInvariant().Contains("SSD") ? "SSD" : "HDD";
                    var driveTelemetry = telemetryDrives.Count > driveIndex ? telemetryDrives[driveIndex] : null;
                    int? health = driveTelemetry?.SmartHealthPercent;
                    string healthText = health.HasValue
                        ? LocalizationService.Format(
                            "System_HealthFormat",
                            health.Value,
                            LocalizationService.Health(driveTelemetry?.StatusText))
                        : LocalizationService.Get("Common_Unknown");
                    string letters = "";
                    var lettersList = new List<string>();

                    try
                    {
                        string devId = drive["DeviceID"]?.ToString()?.Replace("\\", "\\\\") ?? "";
                        string q = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{devId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                        using var partSearcher = new ManagementObjectSearcher(q);
                        using var partResults = partSearcher.Get();

                        foreach (ManagementObject part in partResults)
                        {
                            using var partRow = part;
                            string partDevId = part["DeviceID"]?.ToString()?.Replace("\\", "\\\\") ?? "";
                            string qLogical = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partDevId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                            using var logSearcher = new ManagementObjectSearcher(qLogical);
                            using var logResults = logSearcher.Get();
                            foreach (ManagementObject log in logResults)
                            {
                                using var logRow = log;
                                string letter = log["DeviceID"]?.ToString()?.TrimEnd(':') ?? "";
                                if (!string.IsNullOrEmpty(letter) && !lettersList.Contains(letter))
                                {
                                    lettersList.Add(letter);
                                }
                            }
                        }

                        if (lettersList.Count > 0)
                        {
                            letters = string.Join(", ", lettersList);
                        }
                    }
                    catch
                    {
                        // Fallback
                    }

                    if (string.IsNullOrEmpty(letters))
                    {
                        letters = drives.Count == 0 ? "C, Z" : "E";
                    }

                    if (lettersList.Count == 0)
                    {
                        lettersList.AddRange(letters.Split(',').Select(letter => letter.Trim())
                            .Where(letter => letter.Length > 0));
                    }

                    double freeGb = 0;
                    bool hasFreeSpace = false;
                    foreach (string letter in lettersList)
                    {
                        try
                        {
                            var logicalDrive = driveInfos.FirstOrDefault(info =>
                                info.Name.StartsWith(letter + ":", StringComparison.OrdinalIgnoreCase));
                            if (logicalDrive != null && logicalDrive.IsReady)
                            {
                                freeGb += logicalDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                                hasFreeSpace = true;
                            }
                        }
                        catch
                        {
                            // Ignore a drive that disappears during the query.
                        }
                    }

                    drives.Add(new StorageInfoItem
                    {
                        Header = LocalizationService.Get("System_StorageDevice"),
                        Title = LocalizationService.Format("System_StorageTitle", type, healthText),
                        Subtitle = LocalizationService.Format("System_StorageSubtitle", letters, $"{sizeGb:0.00}"),
                        FreeSpace = hasFreeSpace
                            ? LocalizationService.Format("System_FreeSpace", $"{freeGb:0.00}")
                            : ""
                    });
                    driveIndex++;
                }

                if (drives.Count > 0)
                {
                    info.StorageDevices = drives;
                }
            }
            catch
            {
                // Fallback
            }

            if (info.StorageDevices.Count == 0 && telemetryDrives.Count > 0)
            {
                info.StorageDevices = telemetryDrives.Select((drive, index) => new StorageInfoItem
                {
                    Header = LocalizationService.Get("System_StorageDevice"),
                    Title = drive.SmartHealthPercent.HasValue
                        ? LocalizationService.Format(
                            "System_StorageTitle",
                            "SSD",
                            LocalizationService.Format(
                                "System_HealthFormat",
                                drive.SmartHealthPercent.Value,
                                LocalizationService.Health(drive.StatusText)))
                        : LocalizationService.Format(
                            "System_StorageTitle",
                            "SSD",
                            LocalizationService.Get("Common_Unknown")),
                    Subtitle = LocalizationService.Format(
                        "System_StorageSubtitle",
                        LocalizationService.Format("System_DriveFallback", index + 1),
                        $"{drive.CapacityGb:0.00}")
                }).ToList();
            }

            // 5. Operating System
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture FROM Win32_OperatingSystem");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    string? caption = obj["Caption"]?.ToString()?.Replace("Microsoft", "")?.Trim();
                    string arch = obj["OSArchitecture"]?.ToString()?.ToLowerInvariant().Contains("64") == true ? "x64" : "x86";
                    info.OperatingSystem = string.IsNullOrWhiteSpace(caption)
                        ? LocalizationService.Get("Common_NotReported")
                        : $"{caption} {arch}".Trim();
                    break;
                }
            }
            catch
            {
                info.OperatingSystem = LocalizationService.Get("Common_NotReported");
            }

            // 6. Windows Uptime
            info.WindowsUptime = FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));

            // 7. Startup Speed
            info.StartupSpeed = QueryBootTime();

            // 8. Temp Stats
            var (min, avg, max) = HardwareMonitorService.Instance.RingBuffer.GetCpuStats();
            if (min > 0 && max > 0)
            {
                bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
                string formatKey = isFahrenheit ? "System_TempStatsFormatF" : "System_TempStatsFormat";
                info.TempStats = LocalizationService.Format(
                    formatKey,
                    Math.Round(isFahrenheit ? (min * 9 / 5) + 32 : min),
                    Math.Round(isFahrenheit ? (avg * 9 / 5) + 32 : avg),
                    Math.Round(isFahrenheit ? (max * 9 / 5) + 32 : max));
            }
            else
            {
                info.TempStats = LocalizationService.Get("Common_NotReported");
            }

            // 9. Monitors
            info.Monitors = QueryMonitorNames();

            // 10. Network
            info.Network = QueryActiveNetwork();

            // 11. System Errors (BSOD Count)
            info.SystemErrors = QueryBsodCount();

            return info;
        }

        public static string FormatUptime(TimeSpan uptime)
        {
            return LocalizationService.Format(
                "System_UptimeFormat",
                uptime.Days,
                uptime.Hours,
                uptime.Minutes,
                uptime.Seconds);
        }

        private string QueryBootTime()
        {
            try
            {
                string query = "*[System[(EventID=100)]]";
                var eventQuery = new EventLogQuery(
                    "Microsoft-Windows-Diagnostics-Performance/Operational",
                    PathType.LogName, query)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(eventQuery);
                EventRecord? record = reader.ReadEvent();
                if (record != null)
                {
                    string xml = record.ToXml();
                    int idx = xml.IndexOf("MainPathBootTime\">", StringComparison.Ordinal);
                    if (idx > 0)
                    {
                        int end = xml.IndexOf("</Data>", idx, StringComparison.Ordinal);
                        if (end > idx)
                        {
                            string msStr = xml.Substring(idx + 18, end - idx - 18);
                            if (double.TryParse(msStr, out double ms))
                            {
                                return LocalizationService.Format("System_SecondsFormat", $"{ms / 1000.0:0.0}");
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            return LocalizationService.Get("Common_NotReported");
        }

        private string QueryMonitorNames()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT UserFriendlyName FROM WmiMonitorID");
                using var results = searcher.Get();
                var names = new List<string>();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    if (obj["UserFriendlyName"] is ushort[] chars)
                    {
                        var sb = new StringBuilder();
                        foreach (ushort c in chars)
                        {
                            if (c == 0) break;
                            sb.Append((char)c);
                        }
                        string name = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                }

                if (names.Count > 0)
                {
                    return string.Join(", ", names);
                }
            }
            catch
            {
                // Ignore
            }

            return LocalizationService.Get("Common_NotReported");
        }

        private string QueryActiveNetwork()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT NetConnectionID, Name, NetConnectionStatus FROM Win32_NetworkAdapter WHERE NetConnectionStatus = 2");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using var row = obj;
                    string id = obj["NetConnectionID"]?.ToString()?.Trim() ?? "Ethernet";
                    string name = obj["Name"]?.ToString()?.Trim() ?? "";

                    // Ignore virtual/tunnel adapters if physical is available
                    if (name.Contains("WARP", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        return LocalizationService.Format("System_NetworkFormat", id, name);
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return LocalizationService.Get("Common_NotReported");
        }

        private string QueryBsodCount()
        {
            int count = 0;
            try
            {
                string query = "*[System[(EventID=1001) and (Provider[@Name='BugCheck' or @Name='Microsoft-Windows-WER-SystemErrorReporting'])]]";
                var eventQuery = new EventLogQuery("System", PathType.LogName, query);
                using var reader = new EventLogReader(eventQuery);
                while (reader.ReadEvent() != null)
                {
                    count++;
                }
            }
            catch
            {
                return LocalizationService.Get("Common_NotReported");
            }

            return LocalizationService.Format("System_BsodCount", count);
        }

        public string GenerateTextReport(SystemInfoData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=================================================");
            sb.AppendLine(LocalizationService.Get("System_ReportTitle"));
            sb.AppendLine(LocalizationService.Format(
                "System_ReportGenerated",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("=================================================");
            sb.AppendLine();
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_Pc"), data.PcName));
            sb.AppendLine(LocalizationService.Format(
                "System_ReportLine",
                LocalizationService.Get("System_Processor"),
                $"{data.ProcessorName} ({data.ProcessorCores})"));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_Memory"), data.Memory));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_GraphicsCard"), data.GraphicsCard));
            sb.AppendLine();
            sb.AppendLine(LocalizationService.Get("System_ReportStorage"));
            foreach (var d in data.StorageDevices)
            {
                string free = string.IsNullOrWhiteSpace(d.FreeSpace) ? "" : $" ({d.FreeSpace})";
                sb.AppendLine($"  - {LocalizationService.Format("System_ReportLine", d.Title, d.Subtitle)}{free}");
            }
            sb.AppendLine();
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_OperatingSystem"), data.OperatingSystem));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_Uptime"), data.WindowsUptime));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_StartupSpeed"), data.StartupSpeed));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_ReportTempStats"), data.TempStats));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_Monitors"), data.Monitors));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_Network"), data.Network));
            sb.AppendLine(LocalizationService.Format("System_ReportLine", LocalizationService.Get("System_ReportSystemErrors"), data.SystemErrors));
            sb.AppendLine();
            sb.AppendLine("=================================================");
            return sb.ToString();
        }
    }
}
