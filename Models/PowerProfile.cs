using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CryoTray.Models
{
    public readonly record struct PowerValue(Guid SubGroup, Guid Setting, uint Ac, uint Dc);

    public sealed class ProfilePalette
    {
        private static readonly (byte R, byte G, byte B) DarkBase = (0x0B, 0x12, 0x18);
        private static readonly (byte R, byte G, byte B) WhiteBase = (0xFF, 0xFF, 0xFF);

        public string Accent { get; init; } = "#2B88D8";
        public string BannerTop { get; init; } = "#255D8E";
        public string BannerBottom { get; init; } = "#1E9FD0";
        public string FanAccent { get; init; } = "#35D7FF";
        public string FanRing { get; init; } = "#1A7A99";
        public string FanCircuit { get; init; } = "#1F9FC4";
        public string FanDots { get; init; } = "#8FF4FF";
        public string FanTileTop { get; init; } = "#071A2B";
        public string FanTileMiddle { get; init; } = "#0B2A40";
        public string FanTileBottom { get; init; } = "#071722";
        public string PetalTop { get; init; } = "#E9FDFF";
        public string PetalMiddle { get; init; } = "#9DE8FF";
        public string PetalBottom { get; init; } = "#3FAFD9";
        public string PetalOutline { get; init; } = "#C8F7FF";
        public string PetalVein { get; init; } = "#E8FFFF";
        public string ToggleTrack { get; init; } = "#2B88D8";
        public string ToggleText { get; init; } = "#FFFFFF";
        public string ToggleThumb { get; init; } = "#FFFFFF";
        public string ChartLine { get; init; } = "#3B9BCB";
        public string ChartArea { get; init; } = "#1A5A7E";

        public static string NormalizeHex(string? hex, string fallback = "#6E859E")
        {
            var (r, g, b) = TryParseHex(hex, out var parsed)
                ? parsed
                : TryParseHex(fallback, out var fb)
                    ? fb
                    : ((byte)0x6E, (byte)0x85, (byte)0x9E);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        /// <summary>Derives a full dashboard, fan, toggle, and chart palette from one accent color.</summary>
        public static ProfilePalette FromAccent(string? accentHex)
        {
            (byte r, byte g, byte b) baseRgb = TryParseHex(accentHex, out var parsed)
                ? parsed
                : ((byte)0x6E, (byte)0x85, (byte)0x9E);

            var trackRgb = Blend(baseRgb, DarkBase, 0.14);
            double lumDark = RelativeLuminance((0x0D, 0x0D, 0x0D));
            double lumWhite = 1.0;
            double lumTrack = RelativeLuminance(trackRgb);
            double darkContrast = (Math.Max(lumTrack, lumDark) + 0.05) / (Math.Min(lumTrack, lumDark) + 0.05);
            double whiteContrast = (Math.Max(lumTrack, lumWhite) + 0.05) / (Math.Min(lumTrack, lumWhite) + 0.05);

            bool useDarkText = darkContrast > whiteContrast;
            if (!useDarkText && whiteContrast < 4.5)
            {
                for (int step = 1; step <= 8 && whiteContrast < 4.5; step++)
                {
                    trackRgb = Blend(trackRgb, DarkBase, 0.12);
                    lumTrack = RelativeLuminance(trackRgb);
                    whiteContrast = (1.05) / (lumTrack + 0.05);
                }
            }
            else if (useDarkText && darkContrast < 4.5)
            {
                for (int step = 1; step <= 8 && darkContrast < 4.5; step++)
                {
                    trackRgb = Blend(trackRgb, WhiteBase, 0.12);
                    lumTrack = RelativeLuminance(trackRgb);
                    darkContrast = (lumTrack + 0.05) / (lumDark + 0.05);
                }
            }

            return new ProfilePalette
            {
                Accent = FormatRgb(baseRgb),
                BannerTop = FormatRgb(Blend(baseRgb, DarkBase, 0.50)),
                BannerBottom = FormatRgb(Blend(baseRgb, DarkBase, 0.24)),
                FanAccent = FormatRgb(Blend(baseRgb, WhiteBase, 0.22)),
                FanRing = FormatRgb(Blend(baseRgb, DarkBase, 0.36)),
                FanCircuit = FormatRgb(Blend(baseRgb, DarkBase, 0.15)),
                FanDots = FormatRgb(Blend(baseRgb, WhiteBase, 0.56)),
                FanTileTop = FormatRgb(Blend(baseRgb, DarkBase, 0.85)),
                FanTileMiddle = FormatRgb(Blend(baseRgb, DarkBase, 0.74)),
                FanTileBottom = FormatRgb(Blend(baseRgb, DarkBase, 0.88)),
                PetalTop = FormatRgb(Blend(baseRgb, WhiteBase, 0.86)),
                PetalMiddle = FormatRgb(Blend(baseRgb, WhiteBase, 0.48)),
                PetalBottom = FormatRgb(baseRgb),
                PetalOutline = FormatRgb(Blend(baseRgb, WhiteBase, 0.70)),
                PetalVein = FormatRgb(Blend(baseRgb, WhiteBase, 0.90)),
                ToggleTrack = FormatRgb(trackRgb),
                ToggleText = useDarkText ? "#0D0D0D" : "#FFFFFF",
                ToggleThumb = "#FFFFFF",
                ChartLine = FormatRgb(Blend(baseRgb, WhiteBase, 0.10)),
                ChartArea = FormatRgb(Blend(baseRgb, DarkBase, 0.55))
            };
        }

        private static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = default;
            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            string trimmed = hex.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.Length == 8)
            {
                trimmed = trimmed[2..];
            }

            if (trimmed.Length != 6 ||
                !byte.TryParse(trimmed[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(trimmed.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(trimmed.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return false;
            }

            rgb = (r, g, b);
            return true;
        }

        private static (byte R, byte G, byte B) Blend((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, double amount)
        {
            return (
                (byte)Math.Clamp((int)Math.Round(from.R + ((to.R - from.R) * amount)), 0, 255),
                (byte)Math.Clamp((int)Math.Round(from.G + ((to.G - from.G) * amount)), 0, 255),
                (byte)Math.Clamp((int)Math.Round(from.B + ((to.B - from.B) * amount)), 0, 255));
        }

        private static double RelativeLuminance((byte R, byte G, byte B) rgb)
        {
            static double Channel(byte value)
            {
                double c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(rgb.R)) + (0.7152 * Channel(rgb.G)) + (0.0722 * Channel(rgb.B));
        }

        private static string FormatRgb((byte R, byte G, byte B) rgb) =>
            $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    public sealed class PowerProfileDefinition
    {
        public string Key { get; init; } = "";
        public Guid SchemeGuid { get; init; }
        public string SchemeName { get; init; } = "";
        public bool IsCooling { get; init; }
        public bool IsWindowsDefault { get; init; }
        public bool IsExternal { get; init; }
        public string? LiteralName { get; init; }
        public string? LiteralBadge { get; init; }
        public string? LiteralDescription { get; init; }
        public ProfilePalette Palette { get; init; } = new();
        public IReadOnlyList<PowerValue> Values { get; init; } = Array.Empty<PowerValue>();

        public string NameKey => $"PowerProfile_{Key}_Name";
        public string BadgeKey => $"PowerProfile_{Key}_Badge";
        public string DescriptionKey => $"PowerProfile_{Key}_Description";
    }

    public static class PowerProfiles
    {
        public const string CoolingKey = "Cooling";
        public const string EcoKey = "Eco";
        public const string LegacyKey = "Legacy";
        public const string BalancedKey = "Balanced";
        public const string WorkKey = "Work";
        public const string GamingLowKey = "GamingLow";
        public const string GamingMediumKey = "GamingMedium";
        public const string GamingHighKey = "GamingHigh";
        public const string GamingUltimateKey = "GamingUltimate";
        public const string ExternalKeyPrefix = "ext:";

        private static readonly ProfilePalette CoolingPalette = new()
        {
            Accent = "#2B88D8",
            BannerTop = "#255D8E",
            BannerBottom = "#1E9FD0",
            FanAccent = "#35D7FF",
            FanRing = "#1A7A99",
            FanCircuit = "#1F9FC4",
            FanDots = "#8FF4FF",
            FanTileTop = "#071A2B",
            FanTileMiddle = "#0B2A40",
            FanTileBottom = "#071722",
            PetalTop = "#E9FDFF",
            PetalMiddle = "#9DE8FF",
            PetalBottom = "#3FAFD9",
            PetalOutline = "#C8F7FF",
                        PetalVein = "#E8FFFF",
            ToggleTrack = "#2B88D8",
            ToggleText = "#FFFFFF",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#3B9BCB",
            ChartArea = "#1A5A7E"
        };

        private static readonly ProfilePalette EcoPalette = new()
        {
            Accent = "#2699A8",
            BannerTop = "#174B57",
            BannerBottom = "#1D7F8E",
            FanAccent = "#4AD6E8",
            FanRing = "#176B7A",
            FanCircuit = "#1D899B",
            FanDots = "#A6F4FF",
            FanTileTop = "#061A20",
            FanTileMiddle = "#092831",
            FanTileBottom = "#06161B",
            PetalTop = "#E6FCFF",
            PetalMiddle = "#94E8F5",
            PetalBottom = "#31A4B5",
            PetalOutline = "#C7F6FC",
            PetalVein = "#E9FDFF",
            ToggleTrack = "#1C7986",
            ToggleText = "#FFFFFF",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#2DA7B8",
            ChartArea = "#144A54"
        };

        private static readonly ProfilePalette BalancedPalette = new()
        {
            Accent = "#C26A47",
            BannerTop = "#9E4531",
            BannerBottom = "#C26A45",
            FanAccent = "#F08A4B",
            FanRing = "#8A4728",
            FanCircuit = "#C56B3C",
            FanDots = "#FFD0A3",
            FanTileTop = "#1A0E0A",
            FanTileMiddle = "#2A160F",
            FanTileBottom = "#140B08",
            PetalTop = "#FFE3B5",
            PetalMiddle = "#FFAD5C",
            PetalBottom = "#C94A1E",
            PetalOutline = "#FFC07A",
                        PetalVein = "#FFE2B8",
            ToggleTrack = "#4A2A1E",
            ToggleText = "#D9835B",
            ToggleThumb = "#E68A57",
            ChartLine = "#C77950",
            ChartArea = "#6A3320"
        };

        private static readonly ProfilePalette WorkPalette = new()
        {
            Accent = "#6E859E",
            BannerTop = "#354354",
            BannerBottom = "#4F647D",
            FanAccent = "#8EB2D6",
            FanRing = "#445A73",
            FanCircuit = "#5E7B9C",
            FanDots = "#CFE3F7",
            FanTileTop = "#0D131A",
            FanTileMiddle = "#16202B",
            FanTileBottom = "#0B1016",
            PetalTop = "#EDF4FC",
            PetalMiddle = "#B8CFE6",
            PetalBottom = "#607D9C",
            PetalOutline = "#D6E6F5",
            PetalVein = "#F2F7FC",
            ToggleTrack = "#4A5E75",
            ToggleText = "#FFFFFF",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#738CA6",
            ChartArea = "#2D3D4F"
        };

        private static readonly ProfilePalette GamingLowPalette = new()
        {
            Accent = "#2F9E74",
            BannerTop = "#1F5C4A",
            BannerBottom = "#2E9E7E",
            FanAccent = "#5BE0B0",
            FanRing = "#1B6E5A",
            FanCircuit = "#2C9C7D",
            FanDots = "#A9FFE0",
            FanTileTop = "#06201A",
            FanTileMiddle = "#0A2C24",
            FanTileBottom = "#061A15",
            PetalTop = "#E6FFF6",
            PetalMiddle = "#9BF0D2",
            PetalBottom = "#3FB894",
            PetalOutline = "#C8FFE9",
                        PetalVein = "#EAFFF7",
            ToggleTrack = "#2F9E74",
            ToggleText = "#0D0D0D",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#2F9E74",
            ChartArea = "#1F5C4A"
        };

        private static readonly ProfilePalette GamingMediumPalette = new()
        {
            Accent = "#C98A2E",
            BannerTop = "#6E4C12",
            BannerBottom = "#B07A24",
            FanAccent = "#FFC65C",
            FanRing = "#8A6420",
            FanCircuit = "#C99532",
            FanDots = "#FFE6A8",
            FanTileTop = "#241A06",
            FanTileMiddle = "#33240A",
            FanTileBottom = "#1A1204",
            PetalTop = "#FFF4D6",
            PetalMiddle = "#FFD98A",
            PetalBottom = "#D99A32",
            PetalOutline = "#FFE3A0",
                        PetalVein = "#FFF6DE",
            ToggleTrack = "#C98A2E",
            ToggleText = "#0D0D0D",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#C98A2E",
            ChartArea = "#6E4C12"
        };

        private static readonly ProfilePalette GamingHighPalette = new()
        {
            Accent = "#C26A47",
            BannerTop = "#9E4531",
            BannerBottom = "#C26A45",
            FanAccent = "#F08A4B",
            FanRing = "#8A4728",
            FanCircuit = "#C56B3C",
            FanDots = "#FFD0A3",
            FanTileTop = "#1A0E0A",
            FanTileMiddle = "#2A160F",
            FanTileBottom = "#140B08",
            PetalTop = "#FFE3B5",
            PetalMiddle = "#FFAD5C",
            PetalBottom = "#C94A1E",
            PetalOutline = "#FFC07A",
                        PetalVein = "#FFE2B8",
            ToggleTrack = "#C26A47",
            ToggleText = "#0D0D0D",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#C77950",
            ChartArea = "#6A3320"
        };

        private static readonly ProfilePalette GamingUltimatePalette = new()
        {
            Accent = "#C43A2E",
            BannerTop = "#6E1B14",
            BannerBottom = "#B5342A",
            FanAccent = "#FF7A5C",
            FanRing = "#8A2C22",
            FanCircuit = "#C44432",
            FanDots = "#FFC2B0",
            FanTileTop = "#240B08",
            FanTileMiddle = "#32100C",
            FanTileBottom = "#190705",
            PetalTop = "#FFE4DC",
            PetalMiddle = "#FFA88F",
            PetalBottom = "#D64C34",
            PetalOutline = "#FFC9B8",
                        PetalVein = "#FFEDE6",
            ToggleTrack = "#B5342A",
            ToggleText = "#FFFFFF",
            ToggleThumb = "#FFFFFF",
            ChartLine = "#C43A2E",
            ChartArea = "#6E1B14"
        };

        public static readonly PowerProfileDefinition CoolingProfile = new()
        {
            Key = CoolingKey,
            SchemeGuid = new Guid("c7a00001-0000-4000-8000-000000000001"),
            SchemeName = "CryoTray — Cooling",
            IsCooling = true,
            Palette = CoolingPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 2, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 99, 99),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 5, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 70, 80),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreasePolicy, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreaseThreshold, 50, 80),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreaseThreshold, 20, 30),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 50, 10),
                Power(PowerGuids.SubProcessor, PowerGuids.SystemCoolingPolicy, 1, 0),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 1, 2),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 1, 1),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 200, 100)
            }
        };

        public static readonly PowerProfileDefinition EcoProfile = new()
        {
            Key = EcoKey,
            SchemeGuid = new Guid("c7a00007-0000-4000-8000-000000000007"),
            SchemeName = "CryoTray — Eco",
            IsCooling = true,
            Palette = EcoPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 70, 55),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 5, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 95, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreasePolicy, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreaseThreshold, 65, 85),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreaseThreshold, 25, 35),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 10, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.SystemCoolingPolicy, 0, 0),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 2, 2),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 1, 1),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 100, 50),
                Power(PowerGuids.SubWireless, PowerGuids.WirelessPowerSaving, 2, 3)
            }
        };

        public static readonly PowerProfileDefinition LegacyProfile = new()
        {
            Key = LegacyKey,
            SchemeGuid = new Guid("c7a00006-0000-4000-8000-000000000006"),
            SchemeName = "CryoTray Cooling - Legacy",
            IsCooling = true,
            Palette = CoolingPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 2, 2),
                Power(PowerGuids.SubNone, PowerGuids.DeviceIdlePolicy, 0, 1),
                Power(PowerGuids.SubNone, PowerGuids.ConnectivityInStandby, 1, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostPolicy, 60, 40),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 99, 99),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMaxClass1, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMaxClass2, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 5, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 0, 50),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEppClass1, 33, 50),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEppClass2, 33, 50),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 2, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicyClass1, 3, 3),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreasePolicy, 1, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreaseThreshold, 30, 90),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreaseThresholdClass1, 60, 90),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreaseThreshold, 10, 30),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfDecreaseThresholdClass1, 20, 30),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfTimeCheck, 15, 30),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100, 10),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingOverutilization, 85, 85),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingConcurrency, 97, 97),
                Power(PowerGuids.SubProcessor, PowerGuids.IdlePromote, 60, 40),
                Power(PowerGuids.SubProcessor, PowerGuids.IdleDemote, 40, 20),
                Power(PowerGuids.SubProcessor, PowerGuids.SystemCoolingPolicy, 1, 0),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 1, 2),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 1, 1),
                Power(PowerGuids.SubUsb, PowerGuids.UsbHubTimeout, 50, 50),
                Power(PowerGuids.SubUsb, PowerGuids.Usb3Lpm, 2, 2),
                Power(PowerGuids.SubDisk, PowerGuids.AhciHipm, 1, 1),
                Power(PowerGuids.SubDisk, PowerGuids.DiskIdle, 1200, 600),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 200, 100),
                Power(PowerGuids.SubDisk, PowerGuids.SecondaryNvmeIdle, 2000, 1000),
                Power(PowerGuids.SubDisk, PowerGuids.NvmeNoppme, 1, 0),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeLatency, 15, 50),
                Power(PowerGuids.SubDisk, PowerGuids.SecondaryNvmeLatency, 100, 100),
                Power(PowerGuids.SubWireless, PowerGuids.WirelessPowerSaving, 0, 2)
            }
        };

        public static readonly PowerProfileDefinition BalancedProfile = new()
        {
            Key = BalancedKey,
            SchemeGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
            SchemeName = "Balanced",
            IsCooling = false,
            IsWindowsDefault = true,
            Palette = BalancedPalette,
            Values = Array.Empty<PowerValue>()
        };

        public static readonly PowerProfileDefinition WorkProfile = new()
        {
            Key = WorkKey,
            SchemeGuid = new Guid("c7a00008-0000-4000-8000-000000000008"),
            SchemeName = "CryoTray — Work",
            IsCooling = false,
            Palette = WorkPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 2, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 4, 3),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 5, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 25, 50),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 3, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 50, 25),
                Power(PowerGuids.SubProcessor, PowerGuids.SystemCoolingPolicy, 1, 0),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 1, 2),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 1, 1),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 200, 100)
            }
        };

        public static readonly PowerProfileDefinition GamingLowProfile = new()
        {
            Key = GamingLowKey,
            SchemeGuid = new Guid("c7a00002-0000-4000-8000-000000000002"),
            SchemeName = "CryoTray — Gaming (Low)",
            Palette = GamingLowPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 2, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 3, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 100, 99),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 5, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 50, 65),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100, 50),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 0, 1),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 0, 1),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 0, 200)
            }
        };

        public static readonly PowerProfileDefinition GamingMediumProfile = new()
        {
            Key = GamingMediumKey,
            SchemeGuid = new Guid("c7a00003-0000-4000-8000-000000000003"),
            SchemeName = "CryoTray — Gaming (Medium)",
            Palette = GamingMediumPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 2, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 4, 3),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 10, 5),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 25, 45),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 3, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100, 50),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 0, 1),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 0, 0),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 0, 100)
            }
        };

        public static readonly PowerProfileDefinition GamingHighProfile = new()
        {
            Key = GamingHighKey,
            SchemeGuid = new Guid("c7a00004-0000-4000-8000-000000000004"),
            SchemeName = "CryoTray — Gaming (High)",
            Palette = GamingHighPalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 1, 1),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 1, 3),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 20, 10),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 0, 25),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 2, 3),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100, 100),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 0, 0),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 0, 0),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 0, 0)
            }
        };

        public static readonly PowerProfileDefinition GamingUltimateProfile = new()
        {
            Key = GamingUltimateKey,
            SchemeGuid = new Guid("c7a00005-0000-4000-8000-000000000005"),
            SchemeName = "CryoTray — Gaming (Ultimate)",
            Palette = GamingUltimatePalette,
            Values = new[]
            {
                Power(PowerGuids.SubNone, PowerGuids.Personality, 1, 1),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfBoostMode, 2, 1),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMax, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.ThrottleMin, 100, 50),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfEpp, 0, 0),
                Power(PowerGuids.SubProcessor, PowerGuids.PerfIncreasePolicy, 2, 2),
                Power(PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100, 100),
                Power(PowerGuids.SubProcessor, PowerGuids.IdleDisable, 0, 0),
                Power(PowerGuids.SubPcie, PowerGuids.PcieAspm, 0, 0),
                Power(PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 0, 0),
                Power(PowerGuids.SubDisk, PowerGuids.PrimaryNvmeIdle, 0, 0)
            }
        };

        public static readonly IReadOnlyList<PowerProfileDefinition> All = new[]
        {
            CoolingProfile,
            LegacyProfile,
            EcoProfile,
            WorkProfile,
            GamingLowProfile,
            GamingMediumProfile,
            GamingHighProfile,
            GamingUltimateProfile
        };

        public static readonly IReadOnlyList<PowerProfileDefinition> CoolingProfiles = new[]
        {
            CoolingProfile,
            LegacyProfile,
            EcoProfile
        };

        public static readonly IReadOnlyList<PowerProfileDefinition> GamingProfiles = new[]
        {
            BalancedProfile,
            WorkProfile,
            GamingLowProfile,
            GamingMediumProfile,
            GamingHighProfile,
            GamingUltimateProfile
        };

        public static readonly IReadOnlyList<PowerProfileDefinition> MenuProfiles = new[]
        {
            CoolingProfile,
            LegacyProfile,
            EcoProfile,
            BalancedProfile,
            WorkProfile,
            GamingLowProfile,
            GamingMediumProfile,
            GamingHighProfile,
            GamingUltimateProfile
        };

        private static readonly Dictionary<string, PowerProfileDefinition> ByKey =
            MenuProfiles.ToDictionary(profile => profile.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<Guid, PowerProfileDefinition> ByGuid =
            MenuProfiles.ToDictionary(profile => profile.SchemeGuid);

        public static readonly Guid[] SchemeGuids =
            All.Select(profile => profile.SchemeGuid).ToArray();

        public static string MakeExternalKey(Guid guid) => $"{ExternalKeyPrefix}{guid:N}";

        public static bool TryParseExternalGuid(string? key, out Guid guid)
        {
            guid = Guid.Empty;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(ExternalKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Guid.TryParse(key[ExternalKeyPrefix.Length..], out guid) && guid != Guid.Empty;
        }

        public static bool CanHide(PowerProfileDefinition? profile)
        {
            return profile != null &&
                !profile.IsExternal &&
                !string.Equals(profile.Key, CoolingKey, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile.Key, BalancedKey, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsHidden(string? key, AppSettings? settings)
        {
            if (string.IsNullOrWhiteSpace(key) || settings?.HiddenPowerProfiles == null)
            {
                return false;
            }

            var profile = Get(key);
            if (!CanHide(profile))
            {
                return false;
            }

            return settings.HiddenPowerProfiles.Any(
                hidden => string.Equals(hidden, profile!.Key, StringComparison.OrdinalIgnoreCase));
        }

        public static PowerProfileDefinition? BuildExternalProfile(
            ExternalPowerProfileSetting? entry,
            string? fallbackName = null)
        {
            if (entry == null ||
                !Guid.TryParse(entry.Guid, out Guid guid) ||
                guid == Guid.Empty ||
                guid == BalancedProfile.SchemeGuid ||
                IsCryoTrayScheme(guid))
            {
                return null;
            }

            string name = !string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Name.Trim()
                : !string.IsNullOrWhiteSpace(fallbackName)
                    ? fallbackName.Trim()
                    : guid.ToString("D");

            return new PowerProfileDefinition
            {
                Key = MakeExternalKey(guid),
                SchemeGuid = guid,
                SchemeName = name,
                IsCooling = entry.IsCooling,
                IsExternal = true,
                LiteralName = name,
                Palette = ProfilePalette.FromAccent(entry.AccentColor),
                Values = Array.Empty<PowerValue>()
            };
        }

        public static IReadOnlyList<PowerProfileDefinition> GetExternalProfiles(AppSettings? settings)
        {
            if (settings?.ExternalPowerProfiles == null || settings.ExternalPowerProfiles.Count == 0)
            {
                return Array.Empty<PowerProfileDefinition>();
            }

            var result = new List<PowerProfileDefinition>();
            var seen = new HashSet<Guid>();
            foreach (var entry in settings.ExternalPowerProfiles)
            {
                var built = BuildExternalProfile(entry);
                if (built != null && seen.Add(built.SchemeGuid))
                {
                    result.Add(built);
                }
            }

            return result;
        }

        public static PowerProfileDefinition? Get(string? key)
        {
            return !string.IsNullOrWhiteSpace(key) && ByKey.TryGetValue(key, out var profile)
                ? profile
                : null;
        }

        public static PowerProfileDefinition? Resolve(string? key, AppSettings? settings = null)
        {
            var builtIn = Get(key);
            if (builtIn != null)
            {
                return builtIn;
            }

            if (!TryParseExternalGuid(key, out Guid guid))
            {
                return null;
            }

            return GetExternalProfiles(settings)
                .FirstOrDefault(profile => profile.SchemeGuid == guid);
        }

        public static IReadOnlyList<PowerProfileDefinition> GetVisibleCoolingProfiles(AppSettings? settings)
        {
            var visible = CoolingProfiles
                .Where(profile => !IsHidden(profile.Key, settings))
                .ToList();
            visible.AddRange(GetExternalProfiles(settings).Where(profile => profile.IsCooling));
            return visible;
        }

        public static IReadOnlyList<PowerProfileDefinition> GetVisibleGamingProfiles(AppSettings? settings)
        {
            var visible = GamingProfiles
                .Where(profile => !IsHidden(profile.Key, settings))
                .ToList();
            visible.AddRange(GetExternalProfiles(settings).Where(profile => !profile.IsCooling));
            return visible;
        }

        public static IReadOnlyList<PowerProfileDefinition> GetVisibleMenuProfiles(AppSettings? settings)
        {
            var visible = new List<PowerProfileDefinition>();
            visible.AddRange(GetVisibleCoolingProfiles(settings));
            visible.AddRange(GetVisibleGamingProfiles(settings));
            return visible;
        }

        public static PowerProfileDefinition GetCooling(string? key, AppSettings? settings = null)
        {
            var profile = Resolve(key, settings);
            if (profile is { IsCooling: true } && !IsHidden(profile.Key, settings))
            {
                return profile;
            }

            return CoolingProfile;
        }

        public static PowerProfileDefinition GetGaming(string? key, AppSettings? settings = null)
        {
            var profile = Resolve(key, settings);
            if (profile is { IsCooling: false } && !IsHidden(profile.Key, settings))
            {
                return profile;
            }

            return BalancedProfile;
        }

        public static PowerProfileDefinition? FromSchemeGuid(Guid guid, AppSettings? settings = null)
        {
            if (ByGuid.TryGetValue(guid, out var profile))
            {
                return profile;
            }

            return GetExternalProfiles(settings)
                .FirstOrDefault(ext => ext.SchemeGuid == guid);
        }

        public static bool IsCryoTrayScheme(Guid guid) => SchemeGuids.Contains(guid);

        private static PowerValue Power(Guid subGroup, Guid setting, uint ac, uint dc) =>
            new(subGroup, setting, ac, dc);
    }

    internal static class PowerGuids
    {
        // Subgroups
        public static readonly Guid SubNone = new("fea3413e-7e05-4911-9a71-700331f1c294");
        public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid SubPcie = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        public static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
        public static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
        public static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        public static readonly Guid SubWireless = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");

        // Settings belonging to no subgroup
        public static readonly Guid Personality = new("245d8541-3943-4422-b025-13a784f679b7");
        public static readonly Guid DeviceIdlePolicy = new("4faab71a-92e5-4726-b531-224559672d19");
        public static readonly Guid ConnectivityInStandby = new("f15576e8-98b7-4186-b944-eafa664402d9");

        // Processor
        public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
        public static readonly Guid PerfBoostPolicy = new("45bcc044-d885-43e2-8605-ee0ec6e96b59");
        public static readonly Guid ThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid ThrottleMaxClass1 = new("bc5038f7-23e0-4960-96da-33abaf5935ed");
        public static readonly Guid ThrottleMaxClass2 = new("bc5038f7-23e0-4960-96da-33abaf5935ee");
        public static readonly Guid ThrottleMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid PerfEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        public static readonly Guid PerfEppClass1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
        public static readonly Guid PerfEppClass2 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6865");
        public static readonly Guid PerfIncreasePolicy = new("465e1f50-b610-473a-ab58-00d1077dc418");
        public static readonly Guid PerfIncreasePolicyClass1 = new("465e1f50-b610-473a-ab58-00d1077dc419");
        public static readonly Guid PerfDecreasePolicy = new("40fbefc7-2e9d-4d25-a185-0cfd8574bac6");
        public static readonly Guid PerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
        public static readonly Guid PerfIncreaseThresholdClass1 = new("06cadf0e-64ed-448a-8927-ce7bf90eb35e");
        public static readonly Guid PerfDecreaseThreshold = new("12a0ab44-fe28-4fa9-b3bd-4b64f44960a6");
        public static readonly Guid PerfDecreaseThresholdClass1 = new("12a0ab44-fe28-4fa9-b3bd-4b64f44960a7");
        public static readonly Guid PerfTimeCheck = new("4d2b0152-7d5c-498b-88e2-34345392a2c5");
        public static readonly Guid CoreParkingMinCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
        public static readonly Guid CoreParkingOverutilization = new("943c8cb6-6f93-4227-ad87-e9a3feec08d1");
        public static readonly Guid CoreParkingConcurrency = new("2430ab6f-a520-44a2-9601-f7f23b5134b1");
        public static readonly Guid IdlePromote = new("7b224883-b3cc-4d79-819f-8374152cbe7c");
        public static readonly Guid IdleDemote = new("4b92d758-5a24-4851-a470-815d78aee119");
        public static readonly Guid SystemCoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
        public static readonly Guid IdleDisable = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");

        // PCI Express, USB, storage, wireless
        public static readonly Guid PcieAspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
        public static readonly Guid UsbSelectiveSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
        public static readonly Guid UsbHubTimeout = new("0853a681-27c8-4100-a2fd-82013e970683");
        public static readonly Guid Usb3Lpm = new("d4e98f31-5ffe-4ce1-be31-1b38b384c009");
        public static readonly Guid AhciHipm = new("0b2d69d7-a2a1-449c-9680-f91c70521c60");
        public static readonly Guid DiskIdle = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");
        public static readonly Guid PrimaryNvmeIdle = new("d639518a-e56d-4345-8af2-b9f32fb26109");
        public static readonly Guid SecondaryNvmeIdle = new("d3d55efd-c1ff-424e-9dc3-441be7833010");
        public static readonly Guid NvmeNoppme = new("fc7372b6-ab2d-43ee-8797-15e9841f2cca");
        public static readonly Guid PrimaryNvmeLatency = new("fc95af4d-40e7-4b6d-835a-56d131dbc80e");
        public static readonly Guid SecondaryNvmeLatency = new("dbc9e238-6de9-49e3-92cd-8c2b4946b472");
        public static readonly Guid WirelessPowerSaving = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");
    }
}
