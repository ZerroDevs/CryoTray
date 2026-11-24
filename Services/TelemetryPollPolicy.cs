using System;

namespace CryoTray.Services
{
    public static class TelemetryPollPolicy
    {
        private const TelemetryDemand InteractiveDemand =
            TelemetryDemand.Dashboard |
            TelemetryDemand.Overlay |
            TelemetryDemand.Settings |
            TelemetryDemand.SystemInfo |
            TelemetryDemand.StressTest |
            TelemetryDemand.Csv;

        private const TelemetryDemand BackgroundDemand =
            TelemetryDemand.Tray |
            TelemetryDemand.Alerts |
            TelemetryDemand.Memory;

        private const TelemetryDemand StorageDemand =
            TelemetryDemand.Dashboard |
            TelemetryDemand.Settings |
            TelemetryDemand.SystemInfo |
            TelemetryDemand.StressTest |
            TelemetryDemand.Csv |
            TelemetryDemand.Alerts;

        private const TelemetryDemand FanDemand =
            TelemetryDemand.Dashboard |
            TelemetryDemand.Settings |
            TelemetryDemand.SystemInfo |
            TelemetryDemand.StressTest |
            TelemetryDemand.Alerts;

        public static bool IsInteractive(TelemetryDemand demand) =>
            (demand & InteractiveDemand) != 0;

        public static TimeSpan GetInterval(TelemetryDemand demand)
        {
            if ((demand & InteractiveDemand) != 0)
            {
                return TimeSpan.FromSeconds(1);
            }

            return (demand & BackgroundDemand) != 0
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromSeconds(3);
        }

        public static bool ShouldReadStorage(TelemetryDemand demand) =>
            (demand & StorageDemand) != 0;

        public static bool ShouldReadFans(TelemetryDemand demand) =>
            (demand & FanDemand) != 0;

        public static TimeSpan GetStorageRefreshInterval(TelemetryDemand demand) =>
            IsInteractive(demand) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30);

        public static bool IsStorageRefreshDue(
            DateTime lastRefreshUtc,
            DateTime nowUtc,
            TelemetryDemand demand)
        {
            if (!ShouldReadStorage(demand))
            {
                return false;
            }

            if (lastRefreshUtc == DateTime.MinValue)
            {
                return true;
            }

            return nowUtc - lastRefreshUtc >= GetStorageRefreshInterval(demand);
        }
    }
}
