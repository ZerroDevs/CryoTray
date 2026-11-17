using System;
using System.Collections.Generic;
using CryoTray.Models;

namespace CryoTray.Services
{
    public class AutopilotService
    {
        private static AutopilotService? _instance;
        public static AutopilotService Instance => _instance ??= new AutopilotService();

        private readonly Queue<double> _recentLoads = new();
        private const int MaxHistory = 30; // 30 seconds

        public bool IsEnabled
        {
            get => SettingsService.Instance.Current.AutopilotEnabled;
            set
            {
                SettingsService.Instance.Current.AutopilotEnabled = value;
                SettingsService.Instance.Save();
            }
        }

        public void ProcessTelemetry(HardwareSnapshot snapshot)
        {
            if (!IsEnabled)
            {
                _recentLoads.Clear();
                return;
            }

            _recentLoads.Enqueue(snapshot.CpuLoad);
            while (_recentLoads.Count > MaxHistory)
            {
                _recentLoads.Dequeue();
            }

            var settings = SettingsService.Instance.Current;

            // If we have enough history to evaluate idle state
            if (_recentLoads.Count >= Math.Min(settings.AutopilotIdleSeconds, MaxHistory))
            {
                double sum = 0;
                foreach (var load in _recentLoads) sum += load;
                double avgLoad = sum / _recentLoads.Count;

                // If idle for the window and cooling is OFF -> turn cooling ON
                if (avgLoad < settings.AutopilotIdleThresholdPercent && !snapshot.CoolingModeActive)
                {
                    PowerPlanService.Instance.SetCoolingMode(true);
                }
                // If high load detected (> busy threshold) and cooling is ON -> switch back to Balanced/performance
                else if (snapshot.CpuLoad > settings.AutopilotBusyThresholdPercent && snapshot.CoolingModeActive)
                {
                    PowerPlanService.Instance.SetCoolingMode(false);
                }
            }
        }
    }
}
