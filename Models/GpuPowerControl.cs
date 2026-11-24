namespace CryoTray.Models
{
    public sealed record GpuPowerBounds(
        double MinPercent,
        double DefaultPercent,
        double MaxPercent);

    public sealed record GpuPowerControlState(
        bool IsSupported,
        bool IsBusy,
        bool IsEnabled,
        double CurrentPercent,
        double MinPercent,
        double DefaultPercent,
        double MaxPercent,
        string GpuName,
        string Detail)
    {
        public static GpuPowerControlState Initial { get; } = new(
            false,
            false,
            false,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            "",
            "");
    }
}
