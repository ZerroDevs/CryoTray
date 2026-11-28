using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CryoTray.Models;

namespace CryoTray.Services
{
    /// <summary>
    /// Builds a local diagnostic ZIP. Settings are reduced to an allowlist and activity details
    /// have user paths replaced before they leave the machine.
    /// </summary>
    public static class SupportBundleService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static void Create(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A support bundle path is required.", nameof(filePath));
            }

            string? folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var settings = SettingsService.Instance.Current;
            SystemInfoData systemInfo = SystemInfoQueryService.Instance.QuerySystemInfo();
            HardwareSnapshot snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            IReadOnlyList<FanHealthStatus>? fanStatuses =
                (System.Windows.Application.Current as App)?.FanHealth?.GetStatuses();
            bool isFahrenheit = string.Equals(
                settings.TemperatureUnit, "Fahrenheit", StringComparison.OrdinalIgnoreCase);
            string version = UpdateService.CurrentVersion;

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddText(archive, "summary.txt", BuildSummary(version, settings, snapshot));
            AddText(
                archive,
                "diagnostics.txt",
                SystemInfoQueryService.Instance.GenerateDiagnosticsSnapshot(
                    systemInfo, snapshot, fanStatuses, isFahrenheit));
            AddText(
                archive,
                "system-info.txt",
                SystemInfoQueryService.Instance.GenerateTextReport(systemInfo));
            AddText(archive, "activity.jsonl", ActivityLogService.Instance.SerializeForBundle());
            AddText(archive, "activity.txt", ActivityLogService.Instance.FormatForBundle());
            AddText(archive, "settings.json", BuildRedactedSettings(settings));
            AddUpdateLog(archive);
        }

        private static string BuildSummary(
            string version,
            AppSettings settings,
            HardwareSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"CryoTray {version}");
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Update channel: {settings.UpdateChannel}");
            builder.AppendLine($"Update status: {UpdateStatusService.Instance.Outcome} {UpdateStatusService.Instance.Version}".Trim());
            builder.AppendLine($"Power profile: {PowerPlanService.Instance.ActiveProfile?.Key ?? "Unknown"}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($"Machine: {Environment.MachineName}");
            builder.AppendLine($"CPU: {snapshot.CpuName}");
            builder.AppendLine($"GPU: {snapshot.GpuName}");
            builder.AppendLine($"Activity events: {ActivityLogService.Instance.Snapshot().Count}");
            var backup = UpdateBackupService.GetCurrent();
            builder.AppendLine($"Rollback backup: {(backup == null ? "none" : backup.Version)}");
            return builder.ToString();
        }

        private static string BuildRedactedSettings(AppSettings settings)
        {
            var safe = new
            {
                settings.Language,
                settings.Theme,
                settings.TemperatureUnit,
                settings.AutomaticUpdateChecks,
                settings.SaveRollbackOnUpdate,
                settings.UpdateChannel,
                settings.PreferredCoolingProfile,
                settings.PreferredGamingProfile,
                settings.CoolingDisplayOffAcMinutes,
                settings.CoolingDisplayOffDcMinutes,
                settings.CoolingSleepAfterAcMinutes,
                settings.CoolingSleepAfterDcMinutes,
                settings.OffDisplayOffAcMinutes,
                settings.OffDisplayOffDcMinutes,
                settings.OffSleepAfterAcMinutes,
                settings.OffSleepAfterDcMinutes,
                settings.AutopilotEnabled,
                settings.AutopilotPauseOnFullscreen,
                settings.AutopilotForegroundOnly,
                AutopilotAppCount = settings.AutopilotWhitelist?.Count ?? 0,
                AutopilotProfileCount = settings.AutopilotAppProfiles?.Count ?? 0,
                settings.ShowCpuBadge,
                settings.SyncCpuTrayBadgeColor,
                settings.ShowGpuBadge,
                settings.ShowRamBadge,
                settings.ShowRamRow,
                settings.ShowOverlayRow,
                settings.ShowAutopilotRow,
                settings.ShowCpuGraph,
                settings.CpuGraphTimeframeMinutes,
                settings.GpuGraphMetric,
                settings.HighTempThresholdCpu,
                settings.HighTempThresholdGpu,
                settings.HighTempThresholdDisk,
                settings.FanHealthAlertsEnabled,
                settings.StorageHealthAlertsEnabled,
                settings.GpuDeltaAlertsEnabled,
                settings.DisableAllNotifications,
                settings.DownloadTimerEnabled,
                settings.AutoRamOptimizeEnabled,
                settings.CryoBootDefaultDelaySeconds,
                settings.CryoBootWaitForSystemIdle,
                settings.CryoBootMaxIdleWaitSeconds,
                CryoBootEntryCount = settings.CryoBootEntries?.Count ?? 0
            };

            return JsonSerializer.Serialize(safe, JsonOptions);
        }

        private static void AddUpdateLog(ZipArchive archive)
        {
            try
            {
                string path = UpdateStorage.LogPath;
                if (!File.Exists(path))
                {
                    return;
                }

                string text = ReadTail(path, 256 * 1024);
                AddText(archive, "update.log", ActivityLogService.Sanitize(text));
            }
            catch
            {
            }
        }

        private static string ReadTail(string path, int maxBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = stream.Length;
            if (length > maxBytes)
            {
                stream.Seek(-maxBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static void AddText(ZipArchive archive, string name, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(text ?? "");
        }
    }
}
