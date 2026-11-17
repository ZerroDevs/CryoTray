using System.Collections.Generic;

namespace CryoTray.Models
{
    public class AppSettings
    {
        public string Language { get; set; } = "English"; // "English" or "العربية"
        public string TemperatureUnit { get; set; } = "Celsius"; // "Celsius" or "Fahrenheit"
        public bool ShowComputerName { get; set; } = true;
        public bool LaunchOnStartup { get; set; } = false;
        public bool LaunchInBackground { get; set; } = false;
        public bool EnableCoolingOnLaunch { get; set; } = false;
        public bool PinToTaskbar { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
        public bool UseHotkey { get; set; } = false;
        public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string HotkeyKey { get; set; } = "C";

        // View
        public bool ShowGpuBadge { get; set; } = true;
        public bool ShowCpuBadge { get; set; } = true;
        public bool AnchorToTray { get; set; } = true;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        // CPU & Power Plan
        public string CoolingPowerPlanGuid { get; set; } = "";
        public string NormalPowerPlanGuid { get; set; } = "";
        public int MaxCpuThrottlePercent { get; set; } = 99; // Default 99%
        public int PerfBoostMode { get; set; } = 0; // 0 = Disabled (Default), 1 = Enabled

        // Autopilot
        public bool AutopilotEnabled { get; set; } = false;
        public int AutopilotIdleThresholdPercent { get; set; } = 25;
        public int AutopilotIdleSeconds { get; set; } = 30;
        public int AutopilotBusyThresholdPercent { get; set; } = 65;

        // Overlay
        public bool OverlayEnabled { get; set; } = false;
        public bool OverlayClickThrough { get; set; } = false;
        public double? OverlayLeft { get; set; }
        public double? OverlayTop { get; set; }

        // Thresholds
        public int HighTempThresholdCpu { get; set; } = 85;
        public int HighTempThresholdGpu { get; set; } = 80;
        public int HighTempThresholdDisk { get; set; } = 60;

        public List<string> ExcludedDrives { get; set; } = new();
    }
}
