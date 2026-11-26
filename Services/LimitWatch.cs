using CryoTray.Models;

namespace CryoTray.Services
{
    /// <summary>
    /// Opt-in rule for CPU time spent under the nominal (base) clock while busy.
    /// The PDH "% Processor Performance" counter follows the power-plan cap instead of the
    /// measured clock, so the trigger uses the measured clock from the hardware sensors.
    /// </summary>
    public static class LimitWatch
    {
        public const double LoadFloorPercent = 50.0;
        public const double NominalRatio = 0.95;

        public static bool IsBelowNominal(HardwareSnapshot snapshot)
        {
            return IsBelowNominal(snapshot.CpuMeasuredGhz, snapshot.CpuBaseGhz, snapshot.CpuLoad);
        }

        public static bool IsBelowNominal(double measuredGhz, double baseGhz, double loadPercent)
        {
            if (!double.IsFinite(measuredGhz) || !double.IsFinite(baseGhz) || baseGhz <= 0)
            {
                return false;
            }

            if (!double.IsFinite(loadPercent) || loadPercent < LoadFloorPercent)
            {
                return false;
            }

            return measuredGhz < baseGhz * NominalRatio;
        }
    }
}
