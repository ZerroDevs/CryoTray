using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using CryoTray.Models;
using Microsoft.Win32;

namespace CryoTray.Services
{
    public sealed class GpuDiagnosticsInfo
    {
        public bool Available { get; init; }
        public string GpuName { get; init; } = "";
        public string DriverVersion { get; init; } = "";
        public string DriverDateText { get; init; } = "";
        public string VideoBios { get; init; } = "";
        public string PciIds { get; init; } = "";
        public string PnpDeviceId { get; init; } = "";
        public int? TdrCount { get; init; }
        public bool TdrUnavailable { get; init; }
        public double? HotspotEdgeCurrent { get; init; }
        public double? HotspotEdgeAverage { get; init; }
        public double? HotspotEdgeMax { get; init; }
        public double? HotspotEdgeTrend { get; init; }
        public string HotspotEdgeTrendKey { get; init; } = "";
        public double? EdgeTemperature { get; init; }
        public double? ThermalHeadroom { get; init; }
        public int ThermalLimit { get; init; }
    }

    public static class GpuDiagnosticsService
    {
        private const string DisplayClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        private static readonly object CacheLock = new();
        private static StaticGpuAdapterInfo? _cachedStatic;
        private static IReadOnlyList<string> _cachedControllerNames = Array.Empty<string>();
        private static DateTime _cachedStaticAtUtc;
        private static readonly TimeSpan StaticCacheLifetime = TimeSpan.FromMinutes(2);

        internal sealed record StaticGpuAdapterInfo(
            bool Available,
            string GpuName,
            string DriverVersion,
            string DriverDateText,
            string VideoBios,
            string PciIds,
            string PnpDeviceId,
            int? TdrCount);

        private static readonly Regex PciVendorRegex = new(
            "VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PciDeviceRegex = new(
            "DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PciSubsystemRegex = new(
            "SUBSYS_([0-9A-F]{8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PciRevisionRegex = new(
            "REV_([0-9A-F]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool HasFreshStaticCache()
        {
            lock (CacheLock)
            {
                return _cachedStatic != null &&
                    DateTime.UtcNow - _cachedStaticAtUtc < StaticCacheLifetime;
            }
        }

        internal static void InvalidateCacheForTest()
        {
            lock (CacheLock)
            {
                _cachedStatic = null;
                _cachedControllerNames = Array.Empty<string>();
                _cachedStaticAtUtc = DateTime.MinValue;
            }
        }

        public static GpuDiagnosticsInfo CreateImmediate(
            HardwareSnapshot snapshot,
            AppSettings settings,
            out bool hasStaticCache)
        {
            StaticGpuAdapterInfo? cached;
            lock (CacheLock)
            {
                if (_cachedStatic != null &&
                    DateTime.UtcNow - _cachedStaticAtUtc < StaticCacheLifetime)
                {
                    cached = _cachedStatic;
                    hasStaticCache = true;
                }
                else
                {
                    cached = null;
                    hasStaticCache = false;
                }
            }

            if (cached != null)
            {
                return ComposeDiagnostics(cached, snapshot, settings);
            }

            var placeholder = new StaticGpuAdapterInfo(
                Available: true,
                GpuName: snapshot.GpuName ?? "",
                DriverVersion: "",
                DriverDateText: "",
                VideoBios: "",
                PciIds: "",
                PnpDeviceId: "",
                TdrCount: null);
            return ComposeDiagnostics(placeholder, snapshot, settings);
        }

        public static GpuDiagnosticsInfo Query(bool force = false)
        {
            StaticGpuAdapterInfo? cachedStatic = null;
            if (!force)
            {
                lock (CacheLock)
                {
                    if (_cachedStatic != null &&
                        DateTime.UtcNow - _cachedStaticAtUtc < StaticCacheLifetime)
                    {
                        cachedStatic = _cachedStatic;
                    }
                }
            }

            StaticGpuAdapterInfo staticInfo = cachedStatic ??
                QueryStaticAdapterInfo(preReadTdrCount: null, useSharedTdr: false, out _);

            return ComposeDiagnostics(
                staticInfo,
                HardwareMonitorService.Instance.CurrentSnapshot,
                SettingsService.Instance.Current);
        }

        internal static StaticGpuAdapterInfo QueryStaticAdapterInfo(
            int? preReadTdrCount,
            bool useSharedTdr,
            out IReadOnlyList<string> controllerNames)
        {
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            string primaryName = snapshot.GpuName ?? "";
            string driverVersion = "";
            DateTime? driverDate = null;
            string videoBios = "";
            string pnpDeviceId = "";
            var allNames = new List<string>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DriverVersion, DriverDate, PNPDeviceID, VideoProcessor, " +
                    "AdapterCompatibility, Status FROM Win32_VideoController");
                using ManagementObjectCollection results = searcher.Get();
                var controllers = new List<ManagementObject>();
                foreach (ManagementObject obj in results)
                {
                    controllers.Add(obj);
                    string rawName = obj["Name"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(rawName))
                    {
                        allNames.Add(rawName);
                    }
                }

                using ManagementObject? controller = SelectController(controllers, primaryName);
                if (controller != null)
                {
                    driverVersion = controller["DriverVersion"]?.ToString()?.Trim() ?? "";
                    driverDate = ParseWmiDate(controller["DriverDate"]);
                    pnpDeviceId = controller["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                    videoBios = ReadWmiBios(controller);
                    string controllerName = controller["Name"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(primaryName))
                    {
                        primaryName = controllerName;
                    }
                }

                foreach (ManagementObject obj in controllers)
                {
                    obj.Dispose();
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(videoBios))
            {
                videoBios = ReadRegistryBios(primaryName);
            }

            int? tdrCount = useSharedTdr ? preReadTdrCount : ReadTdrCount();
            bool available = !string.IsNullOrWhiteSpace(primaryName) ||
                !string.IsNullOrWhiteSpace(driverVersion);

            var staticInfo = new StaticGpuAdapterInfo(
                available,
                primaryName,
                driverVersion,
                driverDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                videoBios,
                FormatPciIds(pnpDeviceId),
                pnpDeviceId,
                tdrCount);

            controllerNames = allNames;
            lock (CacheLock)
            {
                _cachedStatic = staticInfo;
                _cachedControllerNames = allNames;
                _cachedStaticAtUtc = DateTime.UtcNow;
            }

            return staticInfo;
        }

        internal static IReadOnlyList<string> GetCachedControllerNames()
        {
            lock (CacheLock)
            {
                return _cachedControllerNames;
            }
        }

        internal static GpuDiagnosticsInfo ComposeDiagnostics(
            StaticGpuAdapterInfo staticInfo,
            HardwareSnapshot snapshot,
            AppSettings settings)
        {
            List<TelemetrySample> samples = HardwareMonitorService.Instance.RingBuffer
                .GetRecentSamples(60);
            var deltas = samples
                .Where(sample => sample.GpuHotspotTemp.HasValue &&
                    IsFinitePositive(sample.GpuHotspotTemp.Value) &&
                    IsFinitePositive(sample.GpuTemp))
                .Select(sample => sample.GpuHotspotTemp!.Value - sample.GpuTemp)
                .Where(delta => delta > 0 && delta < 80)
                .ToList();

            double? current = null;
            if (snapshot.GpuHotspotTemp.HasValue &&
                IsFinitePositive(snapshot.GpuHotspotTemp.Value) &&
                IsFinitePositive(snapshot.GpuTemp))
            {
                current = snapshot.GpuHotspotTemp.Value - snapshot.GpuTemp;
            }
            else if (deltas.Count > 0)
            {
                current = deltas[^1];
            }

            double? average = deltas.Count > 0 ? deltas.Average() : null;
            double? maximum = deltas.Count > 0 ? deltas.Max() : null;
            double? trend = null;
            string trendKey = "GpuDiag_TrendStable";
            if (deltas.Count >= 2)
            {
                trend = deltas[^1] - deltas[0];
                if (trend > 0.5)
                {
                    trendKey = "GpuDiag_TrendRising";
                }
                else if (trend < -0.5)
                {
                    trendKey = "GpuDiag_TrendFalling";
                }
            }

            double? edgeTemperature = IsFinitePositive(snapshot.GpuTemp)
                ? snapshot.GpuTemp
                : null;
            double? headroom = edgeTemperature.HasValue
                ? settings.HighTempThresholdGpu - edgeTemperature.Value
                : null;

            return new GpuDiagnosticsInfo
            {
                Available = staticInfo.Available,
                GpuName = !string.IsNullOrWhiteSpace(snapshot.GpuName) ? snapshot.GpuName : staticInfo.GpuName,
                DriverVersion = staticInfo.DriverVersion,
                DriverDateText = staticInfo.DriverDateText,
                VideoBios = staticInfo.VideoBios,
                PciIds = staticInfo.PciIds,
                PnpDeviceId = staticInfo.PnpDeviceId,
                TdrCount = staticInfo.TdrCount,
                TdrUnavailable = staticInfo.TdrCount == null,
                HotspotEdgeCurrent = current,
                HotspotEdgeAverage = average,
                HotspotEdgeMax = maximum,
                HotspotEdgeTrend = trend,
                HotspotEdgeTrendKey = trendKey,
                EdgeTemperature = edgeTemperature,
                ThermalHeadroom = headroom,
                ThermalLimit = settings.HighTempThresholdGpu
            };
        }

        private static ManagementObject? SelectController(
            IReadOnlyList<ManagementObject> controllers,
            string primaryName)
        {
            if (controllers.Count == 0)
            {
                return null;
            }

            string normalizedPrimary = NormalizeGpuName(primaryName);
            if (!string.IsNullOrWhiteSpace(normalizedPrimary))
            {
                ManagementObject? match = controllers.FirstOrDefault(controller =>
                {
                    string name = NormalizeGpuName(controller["Name"]?.ToString() ?? "");
                    return name.Length > 0 &&
                        (name.Contains(normalizedPrimary, StringComparison.OrdinalIgnoreCase) ||
                         normalizedPrimary.Contains(name, StringComparison.OrdinalIgnoreCase));
                });
                if (match != null)
                {
                    return match;
                }
            }

            ManagementObject? hardware = controllers.FirstOrDefault(controller =>
            {
                string name = controller["Name"]?.ToString() ?? "";
                return !name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Remote", StringComparison.OrdinalIgnoreCase);
            });
            return hardware ?? controllers[0];
        }

        private static string NormalizeGpuName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return Regex.Replace(
                value.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase),
                @"\s+",
                " ").Trim();
        }

        private static DateTime? ParseWmiDate(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            string text = value.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return ManagementDateTimeConverter.ToDateTime(text).ToLocalTime();
            }
            catch
            {
                return DateTime.TryParse(
                    text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                    ? parsed
                    : null;
            }
        }

        private static string ReadWmiBios(ManagementObject controller)
        {
            foreach (string propertyName in new[]
            {
                "VideoBIOSVersion", "VideoBiosVersion", "BIOSVersion", "BiosVersion"
            })
            {
                try
                {
                    if (controller.Properties[propertyName] != null)
                    {
                        string text = controller[propertyName]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string ReadRegistryBios(string gpuName)
        {
            try
            {
                using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
                if (classKey == null)
                {
                    return "";
                }

                string normalized = NormalizeGpuName(gpuName);
                string? fallback = null;
                foreach (string subKeyName in classKey.GetSubKeyNames())
                {
                    using RegistryKey? subKey = classKey.OpenSubKey(subKeyName);
                    if (subKey == null)
                    {
                        continue;
                    }

                    string bios = ReadBiosValue(subKey);
                    if (string.IsNullOrWhiteSpace(bios))
                    {
                        continue;
                    }

                    string description = subKey.GetValue("DriverDesc")?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(normalized) &&
                        NormalizeGpuName(description).Contains(
                            normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return bios;
                    }

                    fallback ??= bios;
                }

                return fallback ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string ReadBiosValue(RegistryKey key)
        {
            foreach (string valueName in new[]
            {
                "VBIOS", "VideoBIOSVersion", "VideoBiosVersion", "BiosVersion", "BIOSVersion"
            })
            {
                object? value = key.GetValue(valueName);
                string text = ConvertBiosValue(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return "";
        }

        private static string ConvertBiosValue(object? value)
        {
            if (value == null)
            {
                return "";
            }

            if (value is string text)
            {
                return text.Trim();
            }

            if (value is string[] strings)
            {
                return string.Join(" ", strings).Trim();
            }

            if (value is byte[] bytes)
            {
                string ascii = new(bytes
                    .Where(b => b >= 0x20 && b <= 0x7E)
                    .Select(b => (char)b)
                    .ToArray());
                if (ascii.Length >= 4)
                {
                    return ascii.Trim();
                }

                return Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 16)));
            }

            return value.ToString()?.Trim() ?? "";
        }

        private static string FormatPciIds(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
            {
                return "";
            }

            Match vendor = PciVendorRegex.Match(pnpDeviceId);
            Match device = PciDeviceRegex.Match(pnpDeviceId);
            Match subsystem = PciSubsystemRegex.Match(pnpDeviceId);
            Match revision = PciRevisionRegex.Match(pnpDeviceId);
            var parts = new List<string>();
            if (vendor.Success)
            {
                parts.Add($"VEN_{vendor.Groups[1].Value.ToUpperInvariant()}");
            }
            if (device.Success)
            {
                parts.Add($"DEV_{device.Groups[1].Value.ToUpperInvariant()}");
            }
            if (subsystem.Success)
            {
                parts.Add($"SUBSYS_{subsystem.Groups[1].Value.ToUpperInvariant()}");
            }
            if (revision.Success)
            {
                parts.Add($"REV_{revision.Groups[1].Value.ToUpperInvariant()}");
            }

            return parts.Count == 0 ? pnpDeviceId : string.Join(" · ", parts);
        }

        private static int? ReadTdrCount()
        {
            try
            {
                var query = new EventLogQuery(
                    "System",
                    PathType.LogName,
                    "*[System[Provider[@Name='Display'] and (EventID=4101)]]")
                {
                    TolerateQueryErrors = true
                };
                using var reader = new EventLogReader(query);
                int count = 0;
                EventRecord? record;
                while ((record = reader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsFinitePositive(double value) =>
            double.IsFinite(value) && value > 0;
    }
}
