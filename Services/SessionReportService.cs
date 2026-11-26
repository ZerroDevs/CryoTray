using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CryoTray.Models;

namespace CryoTray.Services
{
    public sealed class SessionTimelineEvent
    {
        public DateTime TimestampLocal { get; init; }
        public string Kind { get; init; } = "";
        public string Text { get; init; } = "";
        public ActivitySeverity Severity { get; init; } = ActivitySeverity.Info;
    }

    public sealed class SessionChartEvent
    {
        public DateTime TimestampLocal { get; init; }
        public string Kind { get; init; } = "";
        public string Text { get; init; } = "";
        public ActivitySeverity Severity { get; init; } = ActivitySeverity.Info;
    }

    public readonly struct BelowNominalSummary
    {
        public TimeSpan Total { get; init; }
        public TimeSpan Longest { get; init; }
        public int Periods { get; init; }
        public double PeakTemp { get; init; }
    }

    public sealed class SessionReport
    {
        public DateTime StartLocal { get; init; }
        public DateTime EndLocal { get; init; }
        public TimeSpan Duration { get; init; }
        public int SampleCount { get; init; }
        public bool IsFahrenheit { get; init; }
        public string CpuName { get; init; } = "";
        public string GpuName { get; init; } = "";
        public double CpuPeak { get; init; }
        public double CpuAverage { get; init; }
        public double GpuPeak { get; init; }
        public double GpuAverage { get; init; }
        public double? HotspotPeak { get; init; }
        public double? HotspotEdgeAverage { get; init; }
        public TimeSpan CpuAboveThreshold { get; init; }
        public TimeSpan GpuAboveThreshold { get; init; }
        public TimeSpan HotspotDeltaAboveThreshold { get; init; }
        public int CpuThreshold { get; init; }
        public int GpuThreshold { get; init; }
        public int HotspotDeltaThreshold { get; init; }
        public int CoolingChanges { get; init; }
        public int ProfileChanges { get; init; }
        public int AlertCount { get; init; }
        public int FanEventCount { get; init; }
        public bool LimitWatchEnabled { get; init; }
        public TimeSpan BelowNominalTime { get; init; }
        public int BelowNominalPeriods { get; init; }
        public TimeSpan BelowNominalLongest { get; init; }
        public double BelowNominalPeakTemp { get; init; }
        public IReadOnlyList<SessionTimelineEvent> Timeline { get; init; } =
            Array.Empty<SessionTimelineEvent>();
        public IReadOnlyList<SessionTimelineEvent> Updates { get; init; } =
            Array.Empty<SessionTimelineEvent>();
    }

    public static class SessionReportService
    {
        private const int MaxChartEvents = 14;
        private const int MaxTimelineEvents = 30;

        public static SessionReport Build()
        {
            var settings = SettingsService.Instance.Current;
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            List<TelemetrySample> samples = HardwareMonitorService.Instance.RingBuffer
                .GetRecentSamples(HardwareMonitorService.Instance.RingBuffer.Capacity);
            List<ActivityEvent> activity = ActivityLogService.Instance.Snapshot().ToList();

            if (samples.Count == 0)
            {
                return new SessionReport
                {
                    StartLocal = DateTime.Now,
                    EndLocal = DateTime.Now,
                    CpuName = snapshot.CpuName,
                    GpuName = snapshot.GpuName,
                    IsFahrenheit = string.Equals(
                        settings.TemperatureUnit,
                        "Fahrenheit",
                        StringComparison.OrdinalIgnoreCase),
                    CpuThreshold = settings.HighTempThresholdCpu,
                    GpuThreshold = settings.HighTempThresholdGpu,
                    HotspotDeltaThreshold = settings.GpuHotspotDeltaThresholdCelsius,
                    LimitWatchEnabled = settings.LimitWatchEnabled,
                    Updates = BuildUpdateHistory(activity)
                };
            }

            DateTime start = samples[0].Timestamp;
            DateTime end = samples[^1].Timestamp;
            var validCpu = samples.Where(sample => IsFinitePositive(sample.CpuTemp)).ToList();
            var validGpu = samples.Where(sample => IsFinitePositive(sample.GpuTemp)).ToList();
            var hotspotDeltas = samples
                .Select(SampleHotspotDelta)
                .Where(delta => delta.HasValue && delta.Value > 0)
                .Select(delta => delta!.Value)
                .ToList();
            double cpuPeak = validCpu.Count > 0 ? validCpu.Max(sample => sample.CpuTemp) : 0;
            double gpuPeak = validGpu.Count > 0 ? validGpu.Max(sample => sample.GpuTemp) : 0;
            double? hotspotPeak = samples
                .Where(sample => sample.GpuHotspotTemp.HasValue && IsFinitePositive(sample.GpuHotspotTemp.Value))
                .Select(sample => (double?)sample.GpuHotspotTemp!.Value)
                .OrderByDescending(value => value)
                .FirstOrDefault();

            BelowNominalSummary belowNominal = SummarizeBelowNominal(samples);

            var timeline = new List<SessionTimelineEvent>();
            int coolingChanges = 0;
            int profileChanges = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                TelemetrySample previous = samples[i - 1];
                TelemetrySample current = samples[i];
                if (previous.CoolingModeActive != current.CoolingModeActive)
                {
                    coolingChanges++;
                    timeline.Add(new SessionTimelineEvent
                    {
                        TimestampLocal = current.Timestamp,
                        Kind = "cooling",
                        Text = LocalizationService.Get(
                            current.CoolingModeActive
                                ? "Activity_CoolingOn"
                                : "Activity_CoolingOff")
                    });
                    continue;
                }

                string? previousProfile = NormalizeProfileKey(previous);
                string? currentProfile = NormalizeProfileKey(current);
                if (previousProfile != null && currentProfile != null &&
                    !string.Equals(previousProfile, currentProfile, StringComparison.Ordinal))
                {
                    profileChanges++;
                    timeline.Add(new SessionTimelineEvent
                    {
                        TimestampLocal = current.Timestamp,
                        Kind = "profile",
                        Text = LocalizationService.Format(
                            "Activity_ProfileChanged", ProfileName(currentProfile))
                    });
                }
            }

            foreach (ActivityEvent item in activity)
            {
                DateTime localTime = item.TimestampUtc.ToLocalTime();
                if (localTime < start || localTime > end)
                {
                    continue;
                }

                if (string.Equals(item.Kind, "alert", StringComparison.OrdinalIgnoreCase))
                {
                    timeline.Add(new SessionTimelineEvent
                    {
                        TimestampLocal = localTime,
                        Kind = IsFanActivity(item) ? "fan" : "alert",
                        Text = ActivityLogService.Instance.Format(item),
                        Severity = item.Severity
                    });
                }
            }

            int alertCount = timeline.Count(item => item.Kind == "alert");
            int fanEventCount = timeline.Count(item => item.Kind == "fan");
            var orderedTimeline = timeline
                .OrderBy(item => item.TimestampLocal)
                .TakeLast(MaxTimelineEvents)
                .ToList();

            return new SessionReport
            {
                StartLocal = start,
                EndLocal = end,
                Duration = end > start ? end - start : TimeSpan.Zero,
                SampleCount = samples.Count,
                IsFahrenheit = string.Equals(
                    settings.TemperatureUnit,
                    "Fahrenheit",
                    StringComparison.OrdinalIgnoreCase),
                CpuName = snapshot.CpuName,
                GpuName = snapshot.GpuName,
                CpuPeak = cpuPeak,
                CpuAverage = validCpu.Count > 0 ? validCpu.Average(sample => sample.CpuTemp) : 0,
                GpuPeak = gpuPeak,
                GpuAverage = validGpu.Count > 0 ? validGpu.Average(sample => sample.GpuTemp) : 0,
                HotspotPeak = hotspotPeak,
                HotspotEdgeAverage = hotspotDeltas.Count > 0 ? hotspotDeltas.Average() : null,
                CpuAboveThreshold = DurationAbove(samples, sample => sample.CpuTemp, settings.HighTempThresholdCpu),
                GpuAboveThreshold = DurationAbove(samples, sample => sample.GpuTemp, settings.HighTempThresholdGpu),
                HotspotDeltaAboveThreshold = DurationAbove(
                    samples,
                    sample => SampleHotspotDelta(sample) ?? double.NaN,
                    settings.GpuHotspotDeltaThresholdCelsius),
                CpuThreshold = settings.HighTempThresholdCpu,
                GpuThreshold = settings.HighTempThresholdGpu,
                HotspotDeltaThreshold = settings.GpuHotspotDeltaThresholdCelsius,
                LimitWatchEnabled = settings.LimitWatchEnabled,
                CoolingChanges = coolingChanges,
                ProfileChanges = profileChanges,
                AlertCount = alertCount,
                FanEventCount = fanEventCount,
                BelowNominalTime = belowNominal.Total,
                BelowNominalPeriods = belowNominal.Periods,
                BelowNominalLongest = belowNominal.Longest,
                BelowNominalPeakTemp = belowNominal.PeakTemp,
                Timeline = orderedTimeline,
                Updates = BuildUpdateHistory(activity)
            };
        }

        public static IReadOnlyList<SessionChartEvent> GetChartEvents(
            IReadOnlyList<TelemetrySample> samples)
        {
            if (samples.Count < 2)
            {
                return Array.Empty<SessionChartEvent>();
            }

            DateTime start = samples[0].Timestamp;
            DateTime end = samples[^1].Timestamp;
            var events = new List<SessionChartEvent>();
            for (int i = 1; i < samples.Count; i++)
            {
                TelemetrySample previous = samples[i - 1];
                TelemetrySample current = samples[i];
                if (previous.CoolingModeActive != current.CoolingModeActive)
                {
                    events.Add(new SessionChartEvent
                    {
                        TimestampLocal = current.Timestamp,
                        Kind = "cooling",
                        Text = LocalizationService.Get(
                            current.CoolingModeActive
                                ? "Activity_CoolingOn"
                                : "Activity_CoolingOff")
                    });
                    continue;
                }

                string? previousProfile = NormalizeProfileKey(previous);
                string? currentProfile = NormalizeProfileKey(current);
                if (previousProfile != null && currentProfile != null &&
                    !string.Equals(previousProfile, currentProfile, StringComparison.Ordinal))
                {
                    events.Add(new SessionChartEvent
                    {
                        TimestampLocal = current.Timestamp,
                        Kind = "profile",
                        Text = LocalizationService.Format(
                            "Activity_ProfileChanged", ProfileName(currentProfile))
                    });
                }

                if (current.CpuBelowNominal && !previous.CpuBelowNominal)
                {
                    events.Add(new SessionChartEvent
                    {
                        TimestampLocal = current.Timestamp,
                        Kind = "heldback",
                        Text = LocalizationService.Get("Session_BelowNominalEvent")
                    });
                }
            }

            foreach (ActivityEvent item in ActivityLogService.Instance.Snapshot())
            {
                DateTime localTime = item.TimestampUtc.ToLocalTime();
                if (localTime < start || localTime > end)
                {
                    continue;
                }

                if (string.Equals(item.Kind, "alert", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new SessionChartEvent
                    {
                        TimestampLocal = localTime,
                        Kind = IsFanActivity(item) ? "fan" : "alert",
                        Text = ActivityLogService.Instance.Format(item),
                        Severity = item.Severity
                    });
                }
                else if (string.Equals(item.Kind, "update", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new SessionChartEvent
                    {
                        TimestampLocal = localTime,
                        Kind = "update",
                        Text = ActivityLogService.Instance.Format(item),
                        Severity = item.Severity
                    });
                }
            }

            return events
                .OrderBy(item => item.TimestampLocal)
                .TakeLast(MaxChartEvents)
                .ToList();
        }

        public static string BuildText(SessionReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine(LocalizationService.Get("Session_Title"));
            builder.AppendLine(LocalizationService.Format(
                "Session_Range",
                report.StartLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                report.EndLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture)));
            builder.AppendLine(LocalizationService.Format(
                "Session_SampleSummary",
                report.SampleCount,
                FormatDuration(report.Duration)));
            if (!string.IsNullOrWhiteSpace(report.CpuName))
            {
                builder.AppendLine(LocalizationService.Format("Session_CpuLine", report.CpuName));
            }
            if (!string.IsNullOrWhiteSpace(report.GpuName))
            {
                builder.AppendLine(LocalizationService.Format("Session_GpuLine", report.GpuName));
            }
            builder.AppendLine();
            builder.AppendLine(LocalizationService.Get("Session_ThermalSection"));
            builder.AppendLine(LocalizationService.Format(
                "Session_CpuThermalLine",
                report.CpuPeak > 0
                    ? FormatTemp(report.CpuAverage, report.IsFahrenheit)
                    : "—",
                report.CpuPeak > 0
                    ? FormatTemp(report.CpuPeak, report.IsFahrenheit)
                    : "—",
                FormatDuration(report.CpuAboveThreshold),
                FormatThreshold(report.CpuThreshold, report.IsFahrenheit)));
            builder.AppendLine(LocalizationService.Format(
                "Session_GpuThermalLine",
                report.GpuPeak > 0
                    ? FormatTemp(report.GpuAverage, report.IsFahrenheit)
                    : "—",
                report.GpuPeak > 0
                    ? FormatTemp(report.GpuPeak, report.IsFahrenheit)
                    : "—",
                FormatDuration(report.GpuAboveThreshold),
                FormatThreshold(report.GpuThreshold, report.IsFahrenheit)));
            builder.AppendLine(LocalizationService.Format(
                "Session_HotspotThermalLine",
                report.HotspotPeak.HasValue
                    ? FormatTemp(report.HotspotPeak.Value, report.IsFahrenheit)
                    : "—",
                report.HotspotEdgeAverage.HasValue
                    ? FormatDelta(report.HotspotEdgeAverage.Value, report.IsFahrenheit)
                    : "—",
                FormatDuration(report.HotspotDeltaAboveThreshold),
                FormatDeltaThreshold(report.HotspotDeltaThreshold, report.IsFahrenheit)));
            builder.AppendLine(LocalizationService.Get("Session_BelowNominal") + ": " +
                FormatBelowNominal(report));
            builder.AppendLine(LocalizationService.Get("Session_Changes"));
            builder.AppendLine(LocalizationService.Format(
                "Session_ChangesValue",
                report.CoolingChanges,
                report.ProfileChanges,
                report.AlertCount,
                report.FanEventCount));
            builder.AppendLine();
            builder.AppendLine(LocalizationService.Get("Session_TimelineSection"));
            if (report.Timeline.Count == 0)
            {
                builder.AppendLine(LocalizationService.Get("Session_TimelineEmpty"));
            }
            else
            {
                foreach (SessionTimelineEvent item in report.Timeline)
                {
                    builder.AppendLine(LocalizationService.Format(
                        "Session_TimelineLine",
                        item.TimestampLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                        item.Text));
                }
            }
            builder.AppendLine();
            builder.AppendLine(LocalizationService.Get("Session_UpdatesSection"));
            if (report.Updates.Count == 0)
            {
                builder.AppendLine(LocalizationService.Get("Session_UpdatesEmpty"));
            }
            else
            {
                foreach (SessionTimelineEvent item in report.Updates)
                {
                    builder.AppendLine(LocalizationService.Format(
                        "Session_TimelineLine",
                        item.TimestampLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                        item.Text));
                }
            }
            return builder.ToString().TrimEnd();
        }

        private static IReadOnlyList<SessionTimelineEvent> BuildUpdateHistory(
            IEnumerable<ActivityEvent> activity)
        {
            return activity
                .Where(item => string.Equals(item.Kind, "update", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.TimestampUtc)
                .Take(6)
                .Select(item => new SessionTimelineEvent
                {
                    TimestampLocal = item.TimestampUtc.ToLocalTime(),
                    Kind = "update",
                    Text = ActivityLogService.Instance.Format(item),
                    Severity = item.Severity
                })
                .ToList();
        }

        public static string FormatBelowNominal(SessionReport report)
        {
            if (!report.LimitWatchEnabled)
            {
                return LocalizationService.Get("Session_BelowNominalOff");
            }

            if (report.BelowNominalPeriods == 0)
            {
                return LocalizationService.Get("Session_None");
            }

            return LocalizationService.Format(
                "Session_BelowNominalValue",
                FormatDuration(report.BelowNominalTime),
                report.BelowNominalPeriods,
                FormatDuration(report.BelowNominalLongest),
                FormatTemp(report.BelowNominalPeakTemp, report.IsFahrenheit));
        }

        public static BelowNominalSummary SummarizeBelowNominal(
            IReadOnlyList<TelemetrySample> samples)
        {
            var total = TimeSpan.Zero;
            var longest = TimeSpan.Zero;
            var run = TimeSpan.Zero;
            int periods = 0;
            double peak = 0;
            bool wasBelow = false;

            for (int i = 0; i < samples.Count; i++)
            {
                TelemetrySample sample = samples[i];
                if (!sample.CpuBelowNominal)
                {
                    wasBelow = false;
                    continue;
                }

                if (!wasBelow)
                {
                    periods++;
                    run = TimeSpan.Zero;
                }

                wasBelow = true;
                if (i + 1 < samples.Count)
                {
                    double deltaSeconds =
                        (samples[i + 1].Timestamp - sample.Timestamp).TotalSeconds;
                    if (deltaSeconds > 0.25 && deltaSeconds < 60)
                    {
                        total += TimeSpan.FromSeconds(deltaSeconds);
                        run += TimeSpan.FromSeconds(deltaSeconds);
                    }
                }

                if (double.IsFinite(sample.CpuTemp) && sample.CpuTemp > peak)
                {
                    peak = sample.CpuTemp;
                }

                if (run > longest)
                {
                    longest = run;
                }
            }

            return new BelowNominalSummary
            {
                Total = total,
                Longest = longest,
                Periods = periods,
                PeakTemp = peak
            };
        }

        private static TimeSpan DurationAbove(
            IReadOnlyList<TelemetrySample> samples,
            Func<TelemetrySample, double> valueSelector,
            double threshold)
        {
            double totalSeconds = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                double value = valueSelector(samples[i]);
                if (!IsFinitePositive(value) || value <= threshold)
                {
                    continue;
                }

                double seconds = 1;
                if (i + 1 < samples.Count)
                {
                    double delta = (samples[i + 1].Timestamp - samples[i].Timestamp).TotalSeconds;
                    if (delta > 0.25 && delta < 10)
                    {
                        seconds = delta;
                    }
                }
                totalSeconds += seconds;
            }

            return TimeSpan.FromSeconds(totalSeconds);
        }

        private static double? SampleHotspotDelta(TelemetrySample sample)
        {
            if (!sample.GpuHotspotTemp.HasValue ||
                !IsFinitePositive(sample.GpuHotspotTemp.Value) ||
                !IsFinitePositive(sample.GpuTemp))
            {
                return null;
            }

            return sample.GpuHotspotTemp.Value - sample.GpuTemp;
        }

        private static string? NormalizeProfileKey(TelemetrySample sample)
        {
            if (!string.IsNullOrWhiteSpace(sample.PowerProfileKey))
            {
                return sample.PowerProfileKey;
            }

            return sample.CoolingModeActive ? PowerProfiles.CoolingKey : PowerProfiles.BalancedKey;
        }

        private static string ProfileName(string key)
        {
            PowerProfileDefinition? profile = PowerProfiles.Resolve(key, SettingsService.Instance.Current);
            return profile == null
                ? key
                : LocalizationService.GetProfileName(profile);
        }

        private static bool IsFanActivity(ActivityEvent item) =>
            item.MessageKey.Contains("Fan", StringComparison.OrdinalIgnoreCase);

        private static bool IsFinitePositive(double value) =>
            double.IsFinite(value) && value > 0;

        private static string FormatTemp(double value, bool isFahrenheit) =>
            isFahrenheit
                ? $"{Math.Round((value * 9 / 5) + 32)} °F"
                : $"{Math.Round(value)} °C";

        private static string FormatDelta(double value, bool isFahrenheit) =>
            isFahrenheit
                ? $"{Math.Round(value * 9 / 5)} °F"
                : $"{Math.Round(value)} °C";

        private static string FormatThreshold(int celsius, bool isFahrenheit) =>
            FormatTemp(celsius, isFahrenheit);

        private static string FormatDeltaThreshold(int celsius, bool isFahrenheit) =>
            FormatDelta(celsius, isFahrenheit);

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalMinutes >= 1)
            {
                return $"{(int)value.TotalMinutes}m {value.Seconds}s";
            }

            return $"{Math.Max(0, (int)Math.Round(value.TotalSeconds))}s";
        }
    }
}
