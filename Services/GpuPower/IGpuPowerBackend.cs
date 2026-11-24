using System;
using CryoTray.Models;

namespace CryoTray.Services.GpuPower
{
    internal interface IGpuPowerBackend : IDisposable
    {
        string Vendor { get; }
        GpuPowerBounds? Bounds { get; }
        bool TryReadPercent(out double percent, out string error);
        bool TryApplyPercent(double percent, out double appliedPercent, out string error);
        bool TryRestoreDefault(out string error);
    }

    internal static class GpuPowerMath
    {
        /// <summary>
        /// CryoTray is a limiter, never an overclocker. The usable ceiling is the
        /// native VBIOS default capped at 100%, and the floor is the VBIOS minimum.
        /// </summary>
        public static GpuPowerBounds NormalizeBounds(
            double minPercent,
            double defaultPercent,
            double maxPercent)
        {
            double min = Math.Clamp(minPercent, 0, 100);
            double max = Math.Clamp(maxPercent, min, 100);
            double def = Math.Clamp(defaultPercent, min, max);
            return new GpuPowerBounds(
                Math.Round(min, 2),
                Math.Round(def, 2),
                Math.Round(max, 2));
        }

        public static double Clamp(GpuPowerBounds bounds, double percent)
        {
            double ceiling = Math.Min(bounds.DefaultPercent, bounds.MaxPercent);
            return Math.Round(Math.Clamp(percent, bounds.MinPercent, ceiling), 2);
        }

        public static bool IsUsable(GpuPowerBounds bounds)
        {
            return double.IsFinite(bounds.MinPercent) &&
                double.IsFinite(bounds.DefaultPercent) &&
                double.IsFinite(bounds.MaxPercent) &&
                bounds.MinPercent >= 0 &&
                bounds.MinPercent < bounds.DefaultPercent &&
                bounds.DefaultPercent <= 100.5;
        }
    }
}
