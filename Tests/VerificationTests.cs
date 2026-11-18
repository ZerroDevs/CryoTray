using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;
using LibreHardwareMonitor.Hardware;

namespace CryoTray.Tests
{
    public static class VerificationTests
    {
        public static int RunAll()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("    CryoTray Comprehensive Runtime Verification   ");
            Console.WriteLine("==================================================");

            bool allPassed = true;

            allPassed &= TestRingBuffer();
            allPassed &= TestCryoTrayPowerSchemeIntegration();
            allPassed &= TestSystemInfoQueries();
            allPassed &= TestGdiLeakPrevention();
            allPassed &= TestLocalization();
            allPassed &= TestWindowsXamlInstantiation();
            allPassed &= TestPerCoreTemperaturePolling();
            allPassed &= TestTemperatureAlerts();
            allPassed &= TestNoSyntheticFallbacksAndNaNSafety();
            allPassed &= TestCoolingScopedCpuStats();
            allPassed &= TestDriveTelemetryMatching();
            allPassed &= TestMultiGpuSelection();
            allPassed &= TestPawnIoDetection();

            Console.WriteLine("==================================================");
            if (allPassed)
            {
                Console.WriteLine(">>> ALL VERIFICATION TESTS PASSED SUCCESSFULLY! <<<");
                return 0;
            }
            else
            {
                Console.WriteLine(">>> SOME VERIFICATION TESTS FAILED! <<<");
                return 1;
            }
        }

        private static bool TestRingBuffer()
        {
            Console.Write("[TEST] TelemetryRingBuffer... ");
            try
            {
                var rb = new TelemetryRingBuffer(3600);
                for (int i = 0; i < 100; i++)
                {
                    rb.AddSample(new TelemetrySample
                    {
                        CpuTemp = 30 + (i % 20),
                        GpuTemp = 40 + (i % 15),
                        CpuGhz = 3.6,
                        CoolingModeActive = (i % 2 == 0)
                    });
                }

                var slice = rb.GetRecentSamples(60);
                if (slice.Count != 60) throw new Exception($"Expected 60 samples, got {slice.Count}");

                var (min, avg, max) = rb.GetCpuStats();
                if (min != 30 || max != 49) throw new Exception($"Min/Max incorrect: min={min}, max={max}");

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestCryoTrayPowerSchemeIntegration()
        {
            Console.Write("[TEST] CryoTray Power Scheme & Live Controls... ");
            try
            {
                var pps = PowerPlanService.Instance;

                // Ensure scheme exists (imports CryoTray.pow if not present)
                Guid coolingGuid = pps.EnsureCryoTrayCoolingScheme();
                if (coolingGuid == Guid.Empty) throw new Exception("Failed to ensure CryoTray Cooling scheme");

                var schemes = pps.GetInstalledSchemes();
                var found = schemes.FirstOrDefault(s => s.Name.Equals(PowerPlanService.CryoTrayCoolingSchemeName, StringComparison.OrdinalIgnoreCase));
                if (found == null) throw new Exception($"Scheme '{PowerPlanService.CryoTrayCoolingSchemeName}' not found in installed schemes");

                // Test live write of throttle (99%) and boost mode (0 = disabled)
                pps.ApplyCpuCoolingParameters(99, 0);

                // Test reset behavior
                pps.ResetCryoTraySchemeToDefault();

                Console.WriteLine($"PASSED (GUID: {coolingGuid}, Name: {found.Name})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestSystemInfoQueries()
        {
            Console.Write("[TEST] SystemInfoQueryService (WMI/EDID/EventLog)... ");
            try
            {
                var info = SystemInfoQueryService.Instance.QuerySystemInfo();
                if (string.IsNullOrEmpty(info.PcName)) throw new Exception("PcName empty");
                if (string.IsNullOrEmpty(info.ProcessorName)) throw new Exception("ProcessorName empty");
                if (string.IsNullOrEmpty(info.GraphicsCard)) throw new Exception("GraphicsCard empty");
                if (string.IsNullOrEmpty(info.OperatingSystem)) throw new Exception("OS empty");

                Console.WriteLine($"PASSED (PC: {info.PcName}, CPU: {info.ProcessorName}, RAM: {info.Memory}, GPU: {info.GraphicsCard})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestGdiLeakPrevention()
        {
            Console.Write("[TEST] GDI Leak Prevention (DestroyIcon loop 1,000x)... ");
            try
            {
                IntPtr hProcess = Process.GetCurrentProcess().Handle;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                uint gdiBefore = NativeMethods.GetGuiResources(hProcess, 0);

                for (int i = 0; i < 1000; i++)
                {
                    using (var bmp = new Bitmap(32, 32))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(124, 179, 66));
                        }
                        IntPtr hIcon = bmp.GetHicon();
                        try
                        {
                            using var icon = Icon.FromHandle(hIcon);
                            using var copy = (Icon)icon.Clone();
                        }
                        finally
                        {
                            NativeMethods.DestroyIcon(hIcon);
                        }
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                uint gdiAfter = NativeMethods.GetGuiResources(hProcess, 0);
                int delta = (int)(gdiAfter - gdiBefore);

                if (delta > 5)
                {
                    throw new Exception($"GDI leak detected! Delta={delta}");
                }

                Console.WriteLine($"PASSED (Before: {gdiBefore}, After: {gdiAfter}, Delta: {delta})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestLocalization()
        {
            Console.Write("[TEST] English and Arabic localization... ");
            try
            {
                LocalizationService.Instance.Apply("العربية");
                if (LocalizationService.Get("Settings_Title") != "الإعدادات")
                {
                    throw new Exception("Arabic dictionary did not load.");
                }

                // RunAll is already on the WPF UI thread, so windows created here
                // receive the LanguageChanged notification on their owning thread.
                var windows = new System.Windows.Window[]
                {
                    new Views.MainWindow(),
                    new Views.SettingsWindow(),
                    new Views.SystemInfoWindow(),
                    new Views.StressTestWindow(),
                    new Views.OverlayWindow()
                };

                foreach (var window in windows)
                {
                    if (window.FlowDirection != System.Windows.FlowDirection.RightToLeft)
                    {
                        throw new Exception($"{window.GetType().Name} did not switch to RTL.");
                    }

                    // OverlayWindow saves its position on close; leave it untouched in this test.
                    if (window is not Views.OverlayWindow)
                    {
                        window.Close();
                    }
                }

                Console.WriteLine("PASSED (Arabic dictionary and RTL windows loaded)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                LocalizationService.Instance.Apply("English");
            }
        }

        private static bool TestTemperatureAlerts()
        {
            Console.Write("[TEST] Temperature alert threshold, cooldown, and mute... ");
            var settings = SettingsService.Instance.Current;
            bool originalMute = settings.MuteTemperatureAlerts;
            int originalCpuThreshold = settings.HighTempThresholdCpu;
            try
            {
                var service = new TemperatureAlertService();
                int alerts = 0;
                service.AlertRaised += alert =>
                {
                    if (alert.Component == TemperatureAlertComponent.Cpu)
                    {
                        alerts++;
                    }
                };

                settings.MuteTemperatureAlerts = false;
                settings.HighTempThresholdCpu = 85;
                var hot = new HardwareSnapshot
                {
                    MaxCoreTemp = 100,
                    PeakCpuTemp = 100
                };

                service.Observe(hot);
                service.Observe(hot);
                if (alerts != 1)
                {
                    throw new Exception($"Expected one alert while above threshold, got {alerts}.");
                }

                settings.MuteTemperatureAlerts = true;
                var mutedService = new TemperatureAlertService();
                int mutedAlerts = 0;
                mutedService.AlertRaised += _ => mutedAlerts++;
                mutedService.Observe(hot);
                if (mutedAlerts != 0)
                {
                    throw new Exception("Muted alerts were raised.");
                }

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                settings.MuteTemperatureAlerts = originalMute;
                settings.HighTempThresholdCpu = originalCpuThreshold;
            }
        }

        private static bool TestWindowsXamlInstantiation()
        {
            Console.Write("[TEST] WPF Windows & XAML Instantiation... ");
            Exception? caughtEx = null;

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var mw = new Views.MainWindow();
                    var sw = new Views.SettingsWindow();
                    var siw = new Views.SystemInfoWindow();
                    var stw = new Views.StressTestWindow();
                    var ow = new Views.OverlayWindow();
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (caughtEx != null)
            {
                Console.WriteLine($"FAILED: {caughtEx.Message}");
                return false;
            }

            Console.WriteLine("PASSED (All 5 Windows instantiated cleanly)");
            return true;
        }

        private static bool TestPerCoreTemperaturePolling()
        {
            Console.Write("[TEST] Per-Core CPU Temperature & Hottest Underline... ");
            try
            {
                var hms = HardwareMonitorService.Instance;
                hms.Start();

                System.Threading.Thread.Sleep(400);
                var snapshot = hms.CurrentSnapshot;
                if (snapshot == null || snapshot.Timestamp == default)
                {
                    System.Threading.Thread.Sleep(1200);
                    snapshot = hms.CurrentSnapshot;
                }

                if (snapshot == null) throw new Exception("CurrentSnapshot is null");

                if (double.IsFinite(snapshot.PeakCpuTemp) && snapshot.PeakCpuTemp > 0)
                {
                    if (snapshot.CpuCoresTelemetry.Count < 2)
                    {
                        throw new Exception($"Expected multiple CPU cores, got {snapshot.CpuCoresTelemetry.Count}");
                    }

                    for (int i = 0; i < snapshot.CpuCoresTelemetry.Count; i++)
                    {
                        string expectedName = $"CPU{i:D2}";
                        if (snapshot.CpuCoresTelemetry[i].Name != expectedName)
                        {
                            throw new Exception($"Core {i} name mismatch: expected {expectedName}, got {snapshot.CpuCoresTelemetry[i].Name}");
                        }
                    }

                    int maxInt = (int)Math.Round(snapshot.CpuCoresTelemetry.Max(c => c.Temperature));
                    if ((int)Math.Round(snapshot.MaxCoreTemp) != maxInt)
                    {
                        throw new Exception($"MaxCoreTemp mismatch: expected {maxInt}, got {snapshot.MaxCoreTemp}");
                    }
                    if ((int)Math.Round(snapshot.CpuTemp) != maxInt)
                    {
                        throw new Exception($"CpuTemp mismatch: expected {maxInt}, got {snapshot.CpuTemp}");
                    }

                    int hottestCount = snapshot.CpuCoresTelemetry.Count(c => c.IsHottest);
                    if (hottestCount < 1) throw new Exception("No core marked as IsHottest");

                    Console.WriteLine($"PASSED ({snapshot.CpuCoresTelemetry.Count} cores, Hottest: {maxInt}°C, {hottestCount} tied hottest)");
                }
                else
                {
                    if (!double.IsNaN(snapshot.CpuTemp) || snapshot.CpuCoresTelemetry.Count != 0)
                    {
                        throw new Exception("Unavailable CPU sensor produced non-NaN temperature or synthetic cores.");
                    }

                    Console.WriteLine("PASSED (CPU thermal sensors unavailable unelevated; returned NaN without synthetic fallback)");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestNoSyntheticFallbacksAndNaNSafety()
        {
            Console.Write("[TEST] No Synthetic Fallbacks & NaN Safety... ");
            try
            {
                var snapshot = new HardwareSnapshot();
                if (!double.IsNaN(snapshot.CpuTemp) ||
                    !double.IsNaN(snapshot.PeakCpuTemp) ||
                    !double.IsNaN(snapshot.MaxCoreTemp) ||
                    !double.IsNaN(snapshot.CpuGhz) ||
                    !double.IsNaN(snapshot.CpuLoad) ||
                    !double.IsNaN(snapshot.GpuTemp) ||
                    !double.IsNaN(snapshot.GpuLoad))
                {
                    throw new Exception("Default HardwareSnapshot must use double.NaN for unavailable telemetry.");
                }

                if (!string.IsNullOrEmpty(snapshot.CpuName) || !string.IsNullOrEmpty(snapshot.GpuName))
                {
                    throw new Exception("Default HardwareSnapshot must not contain hardcoded hardware names.");
                }

                using (var tray = new TrayIconManager())
                {
                    tray.UpdateBadges(double.NaN, double.NaN, false, true);
                    tray.UpdateBadges(double.NaN, 42.0, true, true);
                }

                var alertService = new TemperatureAlertService();
                int raised = 0;
                alertService.AlertRaised += _ => raised++;
                alertService.Observe(snapshot);
                if (raised != 0)
                {
                    throw new Exception("Alerts must not fire for NaN telemetry.");
                }

                var partialCpu = new HardwareSnapshot
                {
                    MaxCoreTemp = double.NaN,
                    PeakCpuTemp = 92
                };
                var settings = SettingsService.Instance.Current;
                bool originalMute = settings.MuteTemperatureAlerts;
                int originalCpu = settings.HighTempThresholdCpu;
                try
                {
                    settings.MuteTemperatureAlerts = false;
                    settings.HighTempThresholdCpu = 85;
                    alertService.Observe(partialCpu);
                }
                finally
                {
                    settings.MuteTemperatureAlerts = originalMute;
                    settings.HighTempThresholdCpu = originalCpu;
                }

                if (raised != 1)
                {
                    throw new Exception($"Expected alert when PeakCpuTemp is finite and MaxCoreTemp is NaN, got {raised}.");
                }

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestCoolingScopedCpuStats()
        {
            Console.Write("[TEST] Cooling-Scoped CPU Temp Stats & NaN Filtering... ");
            try
            {
                var rb = new TelemetryRingBuffer(16);
                rb.AddSample(new TelemetrySample { CpuTemp = 70, CoolingModeActive = false });
                rb.AddSample(new TelemetrySample { CpuTemp = double.NaN, CoolingModeActive = true });
                rb.AddSample(new TelemetrySample { CpuTemp = 0, CoolingModeActive = true });
                rb.AddSample(new TelemetrySample { CpuTemp = 50, CoolingModeActive = true });
                rb.AddSample(new TelemetrySample { CpuTemp = 60, CoolingModeActive = true });

                var allStats = rb.GetCpuStats(coolingOnly: false);
                if (allStats.MinCpu != 50 || allStats.MaxCpu != 70 || Math.Abs(allStats.AvgCpu - 60) > 0.01)
                {
                    throw new Exception($"Unexpected all-sample stats: min={allStats.MinCpu}, avg={allStats.AvgCpu}, max={allStats.MaxCpu}");
                }

                var coolingStats = rb.GetCpuStats(coolingOnly: true);
                if (coolingStats.MinCpu != 50 || coolingStats.MaxCpu != 60 || Math.Abs(coolingStats.AvgCpu - 55) > 0.01)
                {
                    throw new Exception($"Unexpected cooling-only stats: min={coolingStats.MinCpu}, avg={coolingStats.AvgCpu}, max={coolingStats.MaxCpu}");
                }

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestDriveTelemetryMatching()
        {
            Console.Write("[TEST] Storage Drive Matching (Serial / Physical Index / Model)... ");
            try
            {
                var driveA = new DriveTelemetry
                {
                    Index = 0,
                    Model = "Argo N512SSD",
                    SerialNumber = "ARGO2212310000000093",
                    PhysicalDriveNumber = 1,
                    SmartHealthPercent = 94,
                    CapacityGb = 476.9
                };
                var driveB = new DriveTelemetry
                {
                    Index = 1,
                    Model = "Argo N512SSD",
                    SerialNumber = "ARGO2212310000000094",
                    PhysicalDriveNumber = 0,
                    SmartHealthPercent = 99,
                    CapacityGb = 476.9
                };
                var indexOnlyDrive = new DriveTelemetry
                {
                    Index = 2,
                    Model = "Samsung SSD 980",
                    SerialNumber = "",
                    PhysicalDriveNumber = 3,
                    SmartHealthPercent = 88,
                    CapacityGb = 931.5
                };
                var modelOnlyDrive = new DriveTelemetry
                {
                    Index = 3,
                    Model = "WDC WD10EZEX",
                    SerialNumber = "",
                    PhysicalDriveNumber = null,
                    SmartHealthPercent = 100,
                    CapacityGb = 931.5
                };

                var candidates = new[] { driveA, driveB, indexOnlyDrive, modelOnlyDrive };
                var consumed = new HashSet<DriveTelemetry>();

                var wmiDisk0 = SystemInfoQueryService.MatchDriveTelemetry(
                    "Argo N512SSD", "ARGO2212310000000094_00000001.", 0, 476.9, candidates, consumed);
                var wmiDisk1 = SystemInfoQueryService.MatchDriveTelemetry(
                    "Argo N512SSD", "ARGO2212310000000093_00000001.", 1, 476.9, candidates, consumed);
                var wmiDisk3 = SystemInfoQueryService.MatchDriveTelemetry(
                    "Different Label", "", 3, 931.5, candidates, consumed);
                var wmiModel = SystemInfoQueryService.MatchDriveTelemetry(
                    "WDC WD10EZEX", "", 4, 931.5, candidates, consumed);
                var wmiUnknown = SystemInfoQueryService.MatchDriveTelemetry(
                    "Crucial P5 Plus", "OTHER9999", 5, 1000.0, candidates, consumed);

                if (!ReferenceEquals(wmiDisk0, driveB) || !ReferenceEquals(wmiDisk1, driveA))
                {
                    throw new Exception("Identical-model drives did not match by serial number.");
                }
                if (!ReferenceEquals(wmiDisk3, indexOnlyDrive))
                {
                    throw new Exception("Drive did not match by physical drive number.");
                }
                if (!ReferenceEquals(wmiModel, modelOnlyDrive))
                {
                    throw new Exception("Drive did not match by model name.");
                }
                if (wmiUnknown != null)
                {
                    throw new Exception("Unmatched drive should return null rather than pairing by position.");
                }

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestMultiGpuSelection()
        {
            Console.Write("[TEST] Multi-GPU Discrete Priority & Preference Selection... ");
            try
            {
                var igpu = new GpuTelemetry
                {
                    Identifier = "/gpu-amd/0",
                    Name = "AMD Radeon(TM) Graphics",
                    Vendor = "AMD",
                    IsDiscrete = HardwareMonitorService.IsDiscreteGpu(HardwareType.GpuAmd, "AMD Radeon(TM) Graphics"),
                    Temperature = 39
                };
                var dgpu = new GpuTelemetry
                {
                    Identifier = "/gpu-nvidia/0",
                    Name = "NVIDIA GeForce RTX 3060 Ti",
                    Vendor = "Nvidia",
                    IsDiscrete = HardwareMonitorService.IsDiscreteGpu(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 3060 Ti"),
                    Temperature = 45
                };

                if (igpu.IsDiscrete || !dgpu.IsDiscrete)
                {
                    throw new Exception("Discrete vs integrated GPU classification failed.");
                }

                var autoSelected = HardwareMonitorService.SelectPrimaryGpu(new[] { igpu, dgpu }, "");
                if (!ReferenceEquals(autoSelected, dgpu))
                {
                    throw new Exception("Auto mode must prefer discrete GPU over integrated graphics.");
                }

                var explicitSelected = HardwareMonitorService.SelectPrimaryGpu(new[] { igpu, dgpu }, "/gpu-amd/0");
                if (!ReferenceEquals(explicitSelected, igpu))
                {
                    throw new Exception("Explicit PreferredGpuIdentifier must select the chosen GPU.");
                }

                if (HardwareMonitorService.SelectPrimaryGpu(Array.Empty<GpuTelemetry>(), "") != null)
                {
                    throw new Exception("Empty GPU list must return null.");
                }

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestPawnIoDetection()
        {
            Console.Write("[TEST] PawnIO Driver Registry Detection... ");
            try
            {
                bool installed = HardwareMonitorService.IsPawnIoInstalled();
                Console.WriteLine($"PASSED (PawnIO installed: {installed})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }
    }
}
