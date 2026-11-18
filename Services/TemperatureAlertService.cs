using System;
using System.Collections.Generic;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum TemperatureAlertComponent
    {
        Cpu,
        Gpu,
        Drive
    }

    public sealed class TemperatureAlert
    {
        public TemperatureAlert(TemperatureAlertComponent component, double currentCelsius,
            int thresholdCelsius, string? driveName)
        {
            Component = component;
            CurrentCelsius = currentCelsius;
            ThresholdCelsius = thresholdCelsius;
            DriveName = driveName;
        }

        public TemperatureAlertComponent Component { get; }
        public double CurrentCelsius { get; }
        public int ThresholdCelsius { get; }
        public string? DriveName { get; }
    }

    public sealed class TemperatureAlertService
    {
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
        private const double ClearHysteresisCelsius = 3;

        private readonly Dictionary<string, SensorState> _states = new(StringComparer.Ordinal);

        public event Action<TemperatureAlert>? AlertRaised;

        public void Observe(HardwareSnapshot snapshot)
        {
            var settings = SettingsService.Instance.Current;
            if (settings.MuteTemperatureAlerts)
            {
                return;
            }

            double cpuTemperature = SelectMaxFinitePositive(snapshot.MaxCoreTemp, snapshot.PeakCpuTemp);
            Evaluate(
                "cpu",
                cpuTemperature,
                settings.HighTempThresholdCpu,
                () => new TemperatureAlert(
                    TemperatureAlertComponent.Cpu,
                    cpuTemperature,
                    settings.HighTempThresholdCpu,
                    null));

            Evaluate(
                "gpu",
                snapshot.GpuTemp,
                settings.HighTempThresholdGpu,
                () => new TemperatureAlert(
                    TemperatureAlertComponent.Gpu,
                    snapshot.GpuTemp,
                    settings.HighTempThresholdGpu,
                    null));

            foreach (var drive in snapshot.StorageDrives)
            {
                if (HardwareMonitorService.IsDriveExcluded(drive, settings.ExcludedDrives) ||
                    !double.IsFinite(drive.Temperature) ||
                    drive.Temperature <= 0)
                {
                    continue;
                }

                string driveName = string.IsNullOrWhiteSpace(drive.Model)
                    ? LocalizationService.Format("Alert_DriveName", drive.Index + 1)
                    : drive.Model.Trim();

                Evaluate(
                    $"drive:{HardwareMonitorService.GetDriveExclusionKey(drive)}",
                    drive.Temperature,
                    settings.HighTempThresholdDisk,
                    () => new TemperatureAlert(
                        TemperatureAlertComponent.Drive,
                        drive.Temperature,
                        settings.HighTempThresholdDisk,
                        driveName));
            }
        }

        public void Reset()
        {
            _states.Clear();
        }

        private static double SelectMaxFinitePositive(double first, double second)
        {
            bool firstValid = double.IsFinite(first) && first > 0;
            bool secondValid = double.IsFinite(second) && second > 0;
            if (firstValid && secondValid)
            {
                return Math.Max(first, second);
            }

            if (firstValid)
            {
                return first;
            }

            return secondValid ? second : double.NaN;
        }

        private void Evaluate(string key, double temperature, int threshold, Func<TemperatureAlert> alertFactory)
        {
            if (!double.IsFinite(temperature) || temperature <= 0)
            {
                return;
            }

            if (!_states.TryGetValue(key, out var state))
            {
                state = new SensorState();
                _states[key] = state;
            }

            if (temperature <= threshold - ClearHysteresisCelsius)
            {
                state.AboveThreshold = false;
                return;
            }

            if (temperature <= threshold || state.AboveThreshold)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (state.LastAlertUtc.HasValue &&
                now - state.LastAlertUtc.Value < Cooldown)
            {
                state.AboveThreshold = true;
                return;
            }

            state.AboveThreshold = true;
            state.LastAlertUtc = now;
            AlertRaised?.Invoke(alertFactory());
        }

        private sealed class SensorState
        {
            public bool AboveThreshold { get; set; }
            public DateTime? LastAlertUtc { get; set; }
        }
    }
}
