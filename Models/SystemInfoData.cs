using System.Collections.Generic;

namespace CryoTray.Models
{
    public class StorageInfoItem
    {
        public string Header { get; set; } = "Storage device";
        public string Title { get; set; } = "SSD - 100% (Good)";
        public string Subtitle { get; set; } = "C - 500 GB";
        public string FreeSpace { get; set; } = "";
    }

    public class SystemInfoData
    {
        public string PcName { get; set; } = "";
        public string ProcessorName { get; set; } = "";
        public string ProcessorCores { get; set; } = "";
        public string Memory { get; set; } = "";
        public string GraphicsCard { get; set; } = "";
        public List<StorageInfoItem> StorageDevices { get; set; } = new();
        public string OperatingSystem { get; set; } = "";
        public string WindowsUptime { get; set; } = "";
        public string StartupSpeed { get; set; } = "";
        public string TempStats { get; set; } = "";
        public string Monitors { get; set; } = "";
        public string Network { get; set; } = "";
        public string SystemErrors { get; set; } = "";
    }
}
