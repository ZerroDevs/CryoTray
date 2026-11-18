using System;
using System.Windows.Forms;

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
            var status = SystemInformation.PowerStatus;
            bool noBattery = (status.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            float life = status.BatteryLifePercent;
            bool hasBattery = !noBattery && life >= 0f && life <= 1f;
            int percent = hasBattery
                ? (int)Math.Round(Math.Clamp(life, 0f, 1f) * 100f)
                : 0;
            bool charging = hasBattery &&
                            (status.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
            bool onAc = status.PowerLineStatus == PowerLineStatus.Online;

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
