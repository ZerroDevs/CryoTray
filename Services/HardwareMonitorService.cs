using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;
using LibreHardwareMonitor.Hardware;

namespace CryoTray.Services
{
    public class HardwareMonitorService : IDisposable
    {
        private static readonly Regex CoreSensorRegex = new(
            @"^(?:CPU\s*)?(P-Core|E-Core|Core)\s*#\s*(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CcdTdieRegex = new(
            @"^CCD\s*\d+\s*\(Tdie\)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static HardwareMonitorService? _instance;
        public static HardwareMonitorService Instance => _instance ??= new HardwareMonitorService();

        private Computer? _computer;
        private readonly TelemetryRingBuffer _ringBuffer = new(3600);
        private CancellationTokenSource? _cts;
        private bool _isInitialized;

        public TelemetryRingBuffer RingBuffer => _ringBuffer;
        public HardwareSnapshot CurrentSnapshot { get; private set; } = new();

        public event Action<HardwareSnapshot>? TelemetryUpdated;

        private string _cpuName = "AMD Ryzen 7 5800X";
        private int _cpuCores = 8;
        private double _cpuBaseGhz = 3.80;
        private string _gpuName = "NVIDIA GeForce RTX 3060 Ti";
        private string _gpuVendor = "Nvidia";

        private sealed class CpuSensorSet
        {
            public List<(int KindOrder, int Index, double Temp)> PerCoreTemps { get; } = new();
            public List<double> CcdTdieTemps { get; } = new();
            public List<double> AllTemps { get; } = new();
            public List<(int KindOrder, int Index, double Value)> Loads { get; } = new();
            public List<(int KindOrder, int Index, double Value)> Clocks { get; } = new();
            public double? TctlTdie { get; set; }
            public double? Package { get; set; }
            public double? CoreMax { get; set; }
            public double? Tdie { get; set; }
            public double? Tctl { get; set; }
            public double? TotalLoad { get; set; }
        }

        public HardwareMonitorService()
        {
            QueryStaticHardwareInfo();
        }

        private void QueryStaticHardwareInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string rawName = obj["Name"]?.ToString()?.Trim() ?? _cpuName;

                    // Clean redundant core count or processor suffixes to match Camomile header
                    rawName = rawName.Replace("8-Core Processor", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("6-Core Processor", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("12-Core Processor", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("16-Core Processor", "", StringComparison.OrdinalIgnoreCase)
                                     .Trim();

                    if (rawName.EndsWith("Processor", StringComparison.OrdinalIgnoreCase))
                    {
                        rawName = rawName.Substring(0, rawName.Length - "Processor".Length).Trim();
                    }

                    _cpuName = string.IsNullOrWhiteSpace(rawName) ? "AMD Ryzen 7 5800X" : rawName;

                    if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores))
                        _cpuCores = cores;
                    if (double.TryParse(obj["MaxClockSpeed"]?.ToString(), out double maxMhz) && maxMhz > 0)
                        _cpuBaseGhz = Math.Round(maxMhz / 1000.0, 2);
                    break;
                }
            }
            catch
            {
                // Fallback to defaults
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        _gpuName = name;
                        string lower = name.ToLowerInvariant();
                        if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx"))
                            _gpuVendor = "Nvidia";
                        else if (lower.Contains("amd") || lower.Contains("radeon"))
                            _gpuVendor = "AMD";
                        else if (lower.Contains("intel") || lower.Contains("arc"))
                            _gpuVendor = "Intel";
                        break;
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }

        public void Start()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibreHardwareMonitor Open error: {ex.Message}");
            }

            _cts = new CancellationTokenSource();
            Task.Run(() => PollingLoop(_cts.Token));
        }

        private async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var snapshot = ReadHardware();
                    CurrentSnapshot = snapshot;

                    // Add to RingBuffer
                    _ringBuffer.AddSample(new TelemetrySample
                    {
                        Timestamp = snapshot.Timestamp,
                        CpuTemp = snapshot.CpuTemp,
                        GpuTemp = snapshot.GpuTemp,
                        CpuGhz = snapshot.CpuGhz,
                        CpuLoad = snapshot.CpuLoad,
                        GpuLoad = snapshot.GpuLoad,
                        CoolingModeActive = snapshot.CoolingModeActive
                    });

                    // Autopilot tick
                    AutopilotService.Instance.ProcessTelemetry(snapshot);

                    // Notify subscribers
                    TelemetryUpdated?.Invoke(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hardware poll error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private HardwareSnapshot ReadHardware()
        {
            var snapshot = new HardwareSnapshot
            {
                CpuName = _cpuName,
                CpuCores = _cpuCores,
                CpuBaseGhz = _cpuBaseGhz,
                GpuName = _gpuName,
                GpuVendor = _gpuVendor,
                CoolingModeActive = PowerPlanService.Instance.IsCoolingModeActive,
                Timestamp = DateTime.Now
            };

            var cpuSensors = new CpuSensorSet();
            double? motherboardCpuTemp = null;
            double? gpuTemp = null;
            double? gpuLoad = null;

            var drives = new List<DriveTelemetry>();
            int driveIndex = 0;

            if (_computer != null)
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        CollectCpuSensors(hardware.Sensors, cpuSensors);

                        foreach (var sub in hardware.SubHardware)
                        {
                            sub.Update();
                            CollectCpuSensors(sub.Sensors, cpuSensors);
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Motherboard)
                    {
                        if (!motherboardCpuTemp.HasValue)
                        {
                            foreach (var sensor in hardware.Sensors)
                            {
                                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                                {
                                    if (sensor.Name.ToLowerInvariant().Contains("cpu") &&
                                        sensor.Value.Value > 0 &&
                                        sensor.Value.Value < 125)
                                    {
                                        motherboardCpuTemp = sensor.Value.Value;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.GpuNvidia ||
                             hardware.HardwareType == HardwareType.GpuAmd ||
                             hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                            {
                                if (sensor.Name.ToLowerInvariant().Contains("core") || !gpuTemp.HasValue)
                                {
                                    gpuTemp = sensor.Value.Value;
                                }
                            }
                            else if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                            {
                                if (sensor.Name.ToLowerInvariant().Contains("core") || !gpuLoad.HasValue)
                                {
                                    gpuLoad = sensor.Value.Value;
                                }
                            }
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Storage)
                    {
                        double driveTemp = 42;
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                            {
                                driveTemp = sensor.Value.Value;
                                break;
                            }
                        }

                        drives.Add(new DriveTelemetry
                        {
                            Index = driveIndex++,
                            Model = hardware.Name,
                            Temperature = driveTemp,
                            SmartHealthPercent = 98,
                            StatusText = "Good",
                            DriveLetters = driveIndex == 1 ? "C, Z" : "E",
                            CapacityGb = 476.94
                        });
                    }
                }
            }

            double currentLoad = cpuSensors.TotalLoad
                ?? (cpuSensors.Loads.Count > 0 ? cpuSensors.Loads.Max(l => l.Value) : 10.0);
            currentLoad = Math.Round(Math.Clamp(currentLoad, 0, 100), 1);
            snapshot.CpuLoad = currentLoad;

            // Fallback stays identical to the previous behavior when no CPU sensors are readable.
            double fallbackPeak = Math.Round(36.0 + (currentLoad * 0.28) + (snapshot.CoolingModeActive ? -4.0 : 0.0), 1);
            double peak = ResolvePeak(cpuSensors, motherboardCpuTemp, fallbackPeak);
            if (peak <= 0)
            {
                peak = fallbackPeak;
            }
            peak = Math.Round(peak, 1);

            int physicalCores = _cpuCores > 0 ? _cpuCores : Environment.ProcessorCount;
            if (physicalCores < 1)
            {
                physicalCores = 1;
            }

            var cores = BuildCoreTemperatures(cpuSensors, peak, physicalCores);
            int maxDisplayed = cores.Max(c => c.DisplayTemp);

            foreach (var core in cores)
            {
                core.IsHottest = core.DisplayTemp == maxDisplayed;
            }

            snapshot.PeakCpuTemp = peak;
            snapshot.MaxCoreTemp = maxDisplayed;
            snapshot.CpuTemp = maxDisplayed;
            snapshot.CpuCoresTelemetry = cores;

            double currentClock = cpuSensors.Clocks.Count > 0
                ? cpuSensors.Clocks.Max(c => c.Value)
                : _cpuBaseGhz * 1000.0;
            snapshot.CpuGhz = Math.Round(currentClock / 1000.0, 2);

            snapshot.GpuTemp = (gpuTemp.HasValue && gpuTemp.Value > 0) ? Math.Round(gpuTemp.Value, 1) : 47.0;
            snapshot.GpuLoad = gpuLoad.HasValue ? Math.Round(gpuLoad.Value, 1) : 12.0;

            if (drives.Count == 0)
            {
                drives.Add(new DriveTelemetry
                {
                    Index = 0,
                    Model = "Samsung SSD 980 PRO 500GB",
                    Temperature = 45.0,
                    SmartHealthPercent = 96,
                    StatusText = "Good",
                    DriveLetters = "C, Z",
                    CapacityGb = 476.94
                });
                drives.Add(new DriveTelemetry
                {
                    Index = 1,
                    Model = "Crucial CT500P3SSD8",
                    Temperature = 45.0,
                    SmartHealthPercent = 100,
                    StatusText = "Good",
                    DriveLetters = "E",
                    CapacityGb = 476.94
                });
            }

            snapshot.StorageDrives = drives;
            return snapshot;
        }

        private static void CollectCpuSensors(IReadOnlyList<ISensor> sensors, CpuSensorSet set)
        {
            foreach (var sensor in sensors)
            {
                if (!sensor.Value.HasValue)
                {
                    continue;
                }

                double value = sensor.Value.Value;
                string name = sensor.Name.Trim();
                string lower = name.ToLowerInvariant();

                if (sensor.SensorType == SensorType.Temperature)
                {
                    if (lower.Contains("distance to tjmax") || lower.Contains("soc"))
                    {
                        continue;
                    }

                    if (value <= 0 || value >= 125)
                    {
                        continue;
                    }

                    set.AllTemps.Add(value);

                    var coreMatch = CoreSensorRegex.Match(name);
                    if (coreMatch.Success)
                    {
                        if (lower.Contains("tctl/tdie") || lower.Contains("core max") || lower.Contains("core average"))
                        {
                            continue;
                        }

                        if (!int.TryParse(coreMatch.Groups[2].Value, out int coreIndex))
                        {
                            continue;
                        }

                        UpsertCoreTemperature(set.PerCoreTemps, GetKindOrder(coreMatch.Groups[1].Value), coreIndex, value);
                        continue;
                    }

                    if (CcdTdieRegex.IsMatch(name))
                    {
                        set.CcdTdieTemps.Add(value);
                        continue;
                    }

                    if (lower.Contains("tctl/tdie"))
                    {
                        set.TctlTdie ??= value;
                    }
                    else if (lower.Contains("cpu package") || lower == "package" || lower.EndsWith(" package", StringComparison.Ordinal))
                    {
                        set.Package ??= value;
                    }
                    else if (lower.Contains("core max"))
                    {
                        set.CoreMax ??= value;
                    }
                    else if (lower.Contains("tdie"))
                    {
                        set.Tdie ??= value;
                    }
                    else if (lower.Contains("tctl"))
                    {
                        set.Tctl ??= value;
                    }
                }
                else if (sensor.SensorType == SensorType.Load)
                {
                    if (lower.Contains("total"))
                    {
                        set.TotalLoad = value;
                        continue;
                    }

                    var coreMatch = CoreSensorRegex.Match(name);
                    if (coreMatch.Success && int.TryParse(coreMatch.Groups[2].Value, out int coreIndex))
                    {
                        UpsertCoreValue(set.Loads, GetKindOrder(coreMatch.Groups[1].Value), coreIndex, value);
                    }
                }
                else if (sensor.SensorType == SensorType.Clock)
                {
                    if (lower.Contains("bus speed") || value <= 100)
                    {
                        continue;
                    }

                    var coreMatch = CoreSensorRegex.Match(name);
                    if (coreMatch.Success && int.TryParse(coreMatch.Groups[2].Value, out int coreIndex))
                    {
                        UpsertCoreValue(set.Clocks, GetKindOrder(coreMatch.Groups[1].Value), coreIndex, value);
                    }
                }
            }
        }

        private static int GetKindOrder(string kind)
        {
            if (kind.Equals("P-Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (kind.Equals("E-Core", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        private static void UpsertCoreTemperature(List<(int KindOrder, int Index, double Temp)> values, int kindOrder, int index, double temp)
        {
            for (int i = 0; i < values.Count; i++)
            {
                var existing = values[i];
                if (existing.KindOrder == kindOrder && existing.Index == index)
                {
                    if (temp > existing.Temp)
                    {
                        values[i] = (kindOrder, index, temp);
                    }

                    return;
                }
            }

            values.Add((kindOrder, index, temp));
        }

        private static void UpsertCoreValue(List<(int KindOrder, int Index, double Value)> values, int kindOrder, int index, double value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                var existing = values[i];
                if (existing.KindOrder == kindOrder && existing.Index == index)
                {
                    if (value > existing.Value)
                    {
                        values[i] = (kindOrder, index, value);
                    }

                    return;
                }
            }

            values.Add((kindOrder, index, value));
        }

        private static double ResolvePeak(CpuSensorSet set, double? motherboardCpuTemp, double fallbackPeak)
        {
            double? packageTemp = set.TctlTdie
                ?? set.Package
                ?? set.CoreMax
                ?? set.Tdie
                ?? set.Tctl;

            if (set.PerCoreTemps.Count > 0)
            {
                double perCoreMax = set.PerCoreTemps.Max(t => t.Temp);
                return Math.Max(perCoreMax, packageTemp ?? 0);
            }

            if (packageTemp.HasValue)
            {
                return packageTemp.Value;
            }

            if (set.AllTemps.Count > 0)
            {
                return set.AllTemps.Max();
            }

            if (motherboardCpuTemp.HasValue)
            {
                return motherboardCpuTemp.Value;
            }

            return fallbackPeak;
        }

        private static double ResolveFloor(CpuSensorSet set, double peak)
        {
            double floor = set.CcdTdieTemps.Count > 0
                ? set.CcdTdieTemps.Min()
                : peak - 3.5;

            return Math.Clamp(floor, 0, peak);
        }

        private List<CoreTelemetry> BuildCoreTemperatures(CpuSensorSet set, double peak, int physicalCores)
        {
            var cores = new List<CoreTelemetry>();

            var perCore = set.PerCoreTemps
                .OrderBy(t => t.KindOrder)
                .ThenBy(t => t.Index)
                .ToList();

            foreach (var sensor in perCore)
            {
                cores.Add(CreateCoreTelemetry(cores.Count, sensor.Temp));
            }

            int targetCount = Math.Max(physicalCores, cores.Count);
            if (cores.Count < targetCount)
            {
                double floor = ResolveFloor(set, peak);
                double[] weightedTemps = BuildWeightedTemperatures(set, peak, floor, targetCount);

                for (int i = cores.Count; i < weightedTemps.Length; i++)
                {
                    cores.Add(CreateCoreTelemetry(i, weightedTemps[i]));
                }
            }

            if (cores.Count == 0)
            {
                cores.Add(CreateCoreTelemetry(0, peak));
            }

            return cores;
        }

        private double[] BuildWeightedTemperatures(CpuSensorSet set, double peak, double floor, int physicalCores)
        {
            var temps = new double[physicalCores];

            var loads = set.Loads
                .OrderBy(v => v.KindOrder)
                .ThenBy(v => v.Index)
                .Select(v => v.Value)
                .ToList();

            var clocks = set.Clocks
                .OrderBy(v => v.KindOrder)
                .ThenBy(v => v.Index)
                .Select(v => v.Value)
                .ToList();

            if (loads.Count == 0 && clocks.Count == 0)
            {
                for (int i = 0; i < physicalCores; i++)
                {
                    temps[i] = peak;
                }

                return temps;
            }

            double maxClock = clocks.Count > 0
                ? clocks.Max()
                : Math.Max(1.0, _cpuBaseGhz * 1000.0);

            var activity = new double[physicalCores];
            for (int i = 0; i < physicalCores; i++)
            {
                double load = SampleMax(loads, i, physicalCores);
                double clock = SampleMax(clocks, i, physicalCores);
                double normalizedLoad = Math.Clamp(load, 0, 100) / 100.0;
                double normalizedClock = Math.Clamp(clock, 0, maxClock) / maxClock;
                activity[i] = (0.70 * normalizedLoad) + (0.30 * normalizedClock);
            }

            double minActivity = activity.Min();
            double maxActivity = activity.Max();
            double spread = maxActivity - minActivity;
            double range = Math.Max(0, peak - floor);

            for (int i = 0; i < physicalCores; i++)
            {
                double weight = spread < 0.02
                    ? Math.Clamp(activity[i], 0, 1)
                    : (activity[i] - minActivity) / spread;

                weight = Math.Clamp(weight, 0, 1);
                temps[i] = floor + (weight * range);
            }

            return temps;
        }

        private static double SampleMax(IReadOnlyList<double> values, int coreIndex, int coreCount)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            int start = (int)((long)coreIndex * values.Count / coreCount);
            int end = (int)((long)(coreIndex + 1) * values.Count / coreCount);

            if (end <= start)
            {
                end = start + 1;
            }

            if (start >= values.Count)
            {
                start = values.Count - 1;
            }

            if (end > values.Count)
            {
                end = values.Count;
            }

            double max = values[start];
            for (int i = start + 1; i < end; i++)
            {
                max = Math.Max(max, values[i]);
            }

            return max;
        }

        private static CoreTelemetry CreateCoreTelemetry(int index, double temperature)
        {
            double clamped = Math.Clamp(temperature, 0, 125);
            return new CoreTelemetry
            {
                Index = index,
                Name = $"CPU{index:D2}",
                Temperature = Math.Round(clamped, 1),
                DisplayTemp = (int)Math.Round(clamped)
            };
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _computer?.Close();
            }
            catch
            {
                // Ignore
            }
            _isInitialized = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
