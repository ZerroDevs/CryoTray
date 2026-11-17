using System;
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
        public bool HideOnFocusLoss { get; set; } = true;
        public bool UseHotkey { get; set; } = false;
        public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string HotkeyKey { get; set; } = "C";

        // View
        public bool ShowGpuBadge { get; set; } = true;
        public bool ShowCpuBadge { get; set; } = true;
        public bool AnchorToTray { get; set; } = true;
        public bool ShowOverlayRow { get; set; } = false;
        public bool ShowAutopilotRow { get; set; } = false;
        public bool MergeTrayIcons { get; set; } = false;

        // Dashboard readouts
        public bool ShowCpuPower { get; set; } = true;
        public bool ShowGpuPower { get; set; } = true;
        public bool ShowGpuTemp { get; set; } = true;
        public bool ShowGpuName { get; set; } = false;
        public bool ShowGpuHotspot { get; set; } = true;
        public bool ShowGpuVram { get; set; } = true;
        public bool ShowGpuFan { get; set; } = true;

        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        // CPU & Power Plan
        public string CoolingPowerPlanGuid { get; set; } = "";
        public string NormalPowerPlanGuid { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e"; // Balanced
        public int MaxCpuThrottlePercent { get; set; } = 99; // Default 99%
        public int PerfBoostMode { get; set; } = 0; // 0 = Disabled (Default), 1 = Enabled

        // Autopilot
        public bool AutopilotEnabled { get; set; } = false;
        public List<string> AutopilotWhitelist { get; set; } = new();

        // Overlay
        public bool OverlayEnabled { get; set; } = false;
        public bool OverlayClickThrough { get; set; } = false;
        public bool OverlayShowCpuTemp { get; set; } = true;
        public bool OverlayShowCpuLoad { get; set; } = true;
        public bool OverlayShowCpuPower { get; set; } = true;
        public bool OverlayShowCpuClock { get; set; } = true;
        public bool OverlayShowGpuTemp { get; set; } = true;
        public bool OverlayShowGpuLoad { get; set; } = true;
        public bool OverlayShowGpuPower { get; set; } = true;
        public int OverlayOpacityPercent { get; set; } = 90;
        public double? OverlayLeft { get; set; }
        public double? OverlayTop { get; set; }

        // Thresholds and alerts
        public int HighTempThresholdCpu { get; set; } = 85;
        public int HighTempThresholdGpu { get; set; } = 80;
        public int HighTempThresholdDisk { get; set; } = 60;
        public bool MuteTemperatureAlerts { get; set; } = false;

        // Updates
        public bool AutomaticUpdateChecks { get; set; } = true;
        public DateTime? LastUpdateCheckUtc { get; set; }
        public string LastNotifiedUpdateVersion { get; set; } = "";

        public List<string> ExcludedDrives { get; set; } = new();
    }
}
