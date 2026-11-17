using System;

namespace CryoTray.Models
{
    public class TelemetrySample
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
        public double CpuGhz { get; set; }
        public double CpuLoad { get; set; }
        public double GpuLoad { get; set; }
        public bool CoolingModeActive { get; set; }
    }
}
