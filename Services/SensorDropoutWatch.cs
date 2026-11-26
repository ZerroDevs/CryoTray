using System;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum SensorDropoutKind
    {
        None,
        Cpu,
        All
    }

    /// <summary>
    /// Detects a sustained loss of CPU sensor readings, or of every sensor reading. Reads
    /// nothing and writes nothing; the caller decides how to record the result.
    /// </summary>
    public sealed class SensorDropoutWatch
    {
        public const int MissingSeconds = 30;
        public static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(30);

        private DateTime? _gapSinceUtc;
        private DateTime _lastRaisedUtc = DateTime.MinValue;

        /// <summary>
        /// True while any hardware sensor produced a value this poll. The PDH fallbacks
        /// (CpuGhz and CpuLoad) are excluded so a dead sensor stack cannot look healthy.
        /// </summary>
        public static bool HasCpuReadings(HardwareSnapshot? snapshot)
        {
            return snapshot != null &&
                (double.IsFinite(snapshot.CpuTemp) || double.IsFinite(snapshot.CpuMeasuredGhz));
        }

        public static bool HasGpuReadings(HardwareSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (double.IsFinite(snapshot.GpuTemp) || double.IsFinite(snapshot.GpuLoad))
            {
                return true;
            }

            foreach (var gpu in snapshot.Gpus)
            {
                if (double.IsFinite(gpu.Temperature) || double.IsFinite(gpu.Load))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the kind to report once per outage. A CPU gap that later loses the GPU
        /// escalates to All after the window, and the shared gate prevents a second entry.
        /// </summary>
        public SensorDropoutKind Observe(bool cpuReadings, bool gpuReadings, DateTime nowUtc)
        {
            if (cpuReadings)
            {
                _gapSinceUtc = null;
                return SensorDropoutKind.None;
            }

            _gapSinceUtc ??= nowUtc;

            if (nowUtc - _gapSinceUtc.Value < TimeSpan.FromSeconds(MissingSeconds))
            {
                return SensorDropoutKind.None;
            }

            if (_lastRaisedUtc != DateTime.MinValue &&
                nowUtc - _lastRaisedUtc < RepeatWindow)
            {
                return SensorDropoutKind.None;
            }

            _lastRaisedUtc = nowUtc;
            return gpuReadings ? SensorDropoutKind.Cpu : SensorDropoutKind.All;
        }

        public void Reset()
        {
            _gapSinceUtc = null;
        }
    }
}
