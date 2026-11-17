using System;
using System.Collections.Generic;

namespace CryoTray.Models
{
    public class DriveTelemetry
    {
        public int Index { get; set; }
        public string Model { get; set; } = "";
        public double Temperature { get; set; }
        public int SmartHealthPercent { get; set; } = 100;
        public string StatusText { get; set; } = "Good";
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
        public string CpuName { get; set; } = "AMD Ryzen 7 5800X";
        public int CpuCores { get; set; } = 8;
        public double CpuTemp { get; set; } = 37.0;
        public double PeakCpuTemp { get; set; } = 37.0;
        public double MaxCoreTemp { get; set; } = 37.0;
        public double CpuGhz { get; set; } = 3.66;
        public double CpuBaseGhz { get; set; } = 3.80;
        public double CpuLoad { get; set; } = 12.0;
        public List<CoreTelemetry> CpuCoresTelemetry { get; set; } = new();

        public string GpuName { get; set; } = "NVIDIA GeForce RTX 3060 Ti";
        public string GpuVendor { get; set; } = "Nvidia";
        public double GpuTemp { get; set; } = 47.0;
        public double GpuLoad { get; set; } = 15.0;

        public List<DriveTelemetry> StorageDrives { get; set; } = new();
        public bool CoolingModeActive { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
