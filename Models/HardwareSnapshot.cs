using System;
using System.Collections.Generic;

namespace CryoTray.Models
{
    public class DriveTelemetry
    {
        public int Index { get; set; }
        public string Model { get; set; } = "";
        public double Temperature { get; set; }
        public int? SmartHealthPercent { get; set; }
        public string StatusText { get; set; } = "Unknown";
        public string DriveLetters { get; set; } = "";
        public double CapacityGb { get; set; }
    }

    public class CoreTelemetry
    {
        public int Index { get; set; }
        public string Name { get; set; } = "CPU00";
        public double Temperature { get; set; }
        public int DisplayTemp { get; set; }
        public bool IsHottest { get; set; }
    }

    public class HardwareSnapshot
    {
        public string CpuName { get; set; } = "";
        public int CpuCores { get; set; } = 8;
        public double CpuTemp { get; set; } = 37.0;
        public double PeakCpuTemp { get; set; } = 37.0;
        public double MaxCoreTemp { get; set; } = 37.0;
        public double CpuGhz { get; set; } = 3.66;
        public double CpuBaseGhz { get; set; } = 3.80;
        public double CpuMaxGhz { get; set; } = 3.80;
        public double CpuLoad { get; set; } = 12.0;
        public int? CpuPackageWatts { get; set; }
        public List<CoreTelemetry> CpuCoresTelemetry { get; set; } = new();

        public string GpuName { get; set; } = "";
        public string GpuVendor { get; set; } = "";
        public double GpuTemp { get; set; } = 47.0;
        public double GpuLoad { get; set; } = 15.0;
        public int? GpuPowerWatts { get; set; }
        public double? GpuHotspotTemp { get; set; }
        public double? GpuVramTemp { get; set; }
        public int? GpuFanRpm { get; set; }

        public List<DriveTelemetry> StorageDrives { get; set; } = new();
        public bool CoolingModeActive { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
