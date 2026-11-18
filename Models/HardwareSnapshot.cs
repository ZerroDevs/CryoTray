using System;
using System.Collections.Generic;

namespace CryoTray.Models
{
    public class DriveTelemetry
    {
        public int Index { get; set; }
        public string Model { get; set; } = "";
        public double Temperature { get; set; } = double.NaN;
        public int? SmartHealthPercent { get; set; }
        public string StatusText { get; set; } = "Unknown";
        public string DriveLetters { get; set; } = "";
        public double CapacityGb { get; set; }
        public int? PhysicalDriveNumber { get; set; }
        public string SerialNumber { get; set; } = "";
        public string HardwareIdentifier { get; set; } = "";
        public bool? IsSolidState { get; set; }
    }

    public class GpuTelemetry
    {
        public string Identifier { get; set; } = "";
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public bool IsDiscrete { get; set; }
        public double Temperature { get; set; } = double.NaN;
        public double Load { get; set; } = double.NaN;
        public int? PowerWatts { get; set; }
        public double? HotspotTemp { get; set; }
        public double? VramTemp { get; set; }
        public int? FanRpm { get; set; }
    }

    public class GpuDeviceOption
    {
        public string Identifier { get; set; } = "";
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public bool IsDiscrete { get; set; }
        public string DisplayName { get; set; } = "";
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
        public int CpuCores { get; set; }
        public double CpuTemp { get; set; } = double.NaN;
        public double PeakCpuTemp { get; set; } = double.NaN;
        public double MaxCoreTemp { get; set; } = double.NaN;
        public double CpuGhz { get; set; } = double.NaN;
        public double CpuBaseGhz { get; set; } = double.NaN;
        public double CpuMaxGhz { get; set; } = double.NaN;
        public double CpuLoad { get; set; } = double.NaN;
        public int? CpuPackageWatts { get; set; }
        public List<CoreTelemetry> CpuCoresTelemetry { get; set; } = new();

        public string GpuName { get; set; } = "";
        public string GpuVendor { get; set; } = "";
        public double GpuTemp { get; set; } = double.NaN;
        public double GpuLoad { get; set; } = double.NaN;
        public int? GpuPowerWatts { get; set; }
        public double? GpuHotspotTemp { get; set; }
        public double? GpuVramTemp { get; set; }
        public int? GpuFanRpm { get; set; }
        public List<GpuTelemetry> Gpus { get; set; } = new();

        public List<DriveTelemetry> StorageDrives { get; set; } = new();
        public bool PawnIoInstalled { get; set; } = true;
        public bool CoolingModeActive { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
