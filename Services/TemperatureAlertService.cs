using System;
using System.Collections.Generic;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum TemperatureAlertComponent
    {
        Cpu,
        Gpu,
        GpuDelta,
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
        private static readonly TimeSpan DeltaSustainDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DeltaCooldown = TimeSpan.FromMinutes(30);
        private const double ClearHysteresisCelsius = 3;

        private readonly Dictionary<string, SensorState> _states = new(StringComparer.Ordinal);
        private DateTime? _gpuDeltaAboveSince;
        private DateTime? _gpuDeltaLastAlertUtc;

        public event Action<TemperatureAlert>? AlertRaised;

        public void Observe(HardwareSnapshot snapshot)
        {
            Observe(snapshot, DateTime.UtcNow);
        }

        internal void Observe(HardwareSnapshot snapshot, DateTime nowUtc)
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
                nowUtc,
                () => new TemperatureAlert(
                    TemperatureAlertComponent.Cpu,
                    cpuTemperature,
                    settings.HighTempThresholdCpu,
                    null));

            Evaluate(
                "gpu",
                snapshot.GpuTemp,
                settings.HighTempThresholdGpu,
                nowUtc,
                () => new TemperatureAlert(
                    TemperatureAlertComponent.Gpu,
                    snapshot.GpuTemp,
                    settings.HighTempThresholdGpu,
                    null));

            ObserveGpuDelta(snapshot, settings, nowUtc);

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
                    nowUtc,
                    () => new TemperatureAlert(
                        TemperatureAlertComponent.Drive,
                        drive.Temperature,
                        settings.HighTempThresholdDisk,
                        driveName));
            }
        }

        private void ObserveGpuDelta(
            HardwareSnapshot snapshot, AppSettings settings, DateTime nowUtc)
        {
            if (!settings.GpuDeltaAlertsEnabled ||
                !snapshot.GpuHotspotTemp.HasValue ||
                !double.IsFinite(snapshot.GpuHotspotTemp.Value) ||
                snapshot.GpuHotspotTemp.Value <= 0 ||
                !double.IsFinite(snapshot.GpuTemp) ||
                snapshot.GpuTemp <= 0)
            {
                _gpuDeltaAboveSince = null;
                return;
            }

            double delta = snapshot.GpuHotspotTemp.Value - snapshot.GpuTemp;
            int threshold = settings.GpuHotspotDeltaThresholdCelsius;
            if (delta < threshold)
            {
                _gpuDeltaAboveSince = null;
                return;
            }

            _gpuDeltaAboveSince ??= nowUtc;
            if (nowUtc - _gpuDeltaAboveSince.Value < DeltaSustainDuration)
            {
                return;
            }

            if (_gpuDeltaLastAlertUtc.HasValue &&
                nowUtc - _gpuDeltaLastAlertUtc.Value < DeltaCooldown)
            {
                return;
            }

            _gpuDeltaLastAlertUtc = nowUtc;
            AlertRaised?.Invoke(new TemperatureAlert(
                TemperatureAlertComponent.GpuDelta,
                Math.Round(delta, 1),
                threshold,
                null));
        }

        public void Reset()
        {
            _states.Clear();
            _gpuDeltaAboveSince = null;
            _gpuDeltaLastAlertUtc = null;
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

        private void Evaluate(
            string key,
            double temperature,
            int threshold,
            DateTime nowUtc,
            Func<TemperatureAlert> alertFactory)
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

            if (state.LastAlertUtc.HasValue &&
                nowUtc - state.LastAlertUtc.Value < Cooldown)
            {
                state.AboveThreshold = true;
                return;
            }

            state.AboveThreshold = true;
            state.LastAlertUtc = nowUtc;
            AlertRaised?.Invoke(alertFactory());
        }

        private sealed class SensorState
        {
            public bool AboveThreshold { get; set; }
            public DateTime? LastAlertUtc { get; set; }
        }
    }
}
