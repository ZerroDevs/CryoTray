using System;

namespace CryoTray.Services
{
    public readonly record struct BatteryTelemetry(
        bool HasBattery,
        int Percent,
        bool IsCharging,
        bool IsOnAc);

    public static class BatteryTelemetryService
    {
        public static BatteryTelemetry Read()
        {
            if (NativeMethods.GetSystemPowerStatus(out var rawStatus))
            {
                return ParsePowerStatus(
                    rawStatus.ACLineStatus,
                    rawStatus.BatteryFlag,
                    rawStatus.BatteryLifePercent);
            }

            return new BatteryTelemetry(false, 0, false, true);
        }

        internal static BatteryTelemetry ParsePowerStatus(
            byte acLineStatus,
            byte batteryFlag,
            byte batteryLifePercent)
        {
            bool unknownStatus = batteryFlag == 255 || batteryLifePercent > 100;
            bool noBattery = !unknownStatus && (batteryFlag & 128) != 0;
            bool hasBattery = !unknownStatus && !noBattery;
            int percent = hasBattery ? batteryLifePercent : 0;
            bool charging = hasBattery && (batteryFlag & 8) != 0;
            bool onAc = acLineStatus == 1;

            return new BatteryTelemetry(hasBattery, percent, charging, onAc);
        }

        public static string GetStateLabel(BatteryTelemetry battery)
        {
            if (!battery.HasBattery)
            {
                return "";
            }

            if (battery.IsCharging)
            {
                return LocalizationService.Get("Battery_Charging");
            }

            if (battery.IsOnAc && battery.Percent >= 99)
            {
                return LocalizationService.Get("Battery_Full");
            }

            return battery.IsOnAc
                ? LocalizationService.Get("Battery_PluggedIn")
                : LocalizationService.Get("Battery_OnBattery");
        }
    }
}
