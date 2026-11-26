using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Services.GpuPower;
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
            allPassed &= TestPowerProfileWhitelistEngine();
            allPassed &= TestSystemInfoQueries();
            allPassed &= TestGdiLeakPrevention();
            allPassed &= TestLocalization();
            allPassed &= TestWindowsXamlInstantiation();
            allPassed &= TestPerCoreTemperaturePolling();
            allPassed &= TestTemperatureAlerts();
            allPassed &= TestGpuDetailsAndDeltaAlerts();
            allPassed &= TestProcessUsageSampling();
            allPassed &= TestFanHealthWatch();
            allPassed &= TestStorageHealthWatch();
            allPassed &= TestNoSyntheticFallbacksAndNaNSafety();
            allPassed &= TestCoolingScopedCpuStats();
            allPassed &= TestDriveTelemetryMatching();
            allPassed &= TestMultiGpuSelection();
            allPassed &= TestPawnIoDetection();
            allPassed &= TestOverlayClickThroughFlag();
            allPassed &= TestDriveExclusionFiltering();
            allPassed &= TestCustomHotkeyRecorderAndRegister();
            allPassed &= TestAutopilotToolhelpAndFullscreenScan();
            allPassed &= TestSecondLaunchActivationSignal();
            allPassed &= TestRamTelemetryAndCsvExport();
            allPassed &= TestPowerPlanUninstallCleanup();
            allPassed &= TestStartupTaskXmlAndSync();
            allPassed &= TestSsdRowFormatting();
            allPassed &= TestTimedCoolingPause();
            allPassed &= TestStressCoolingDelta();
            allPassed &= TestLimitWatch();
            allPassed &= TestSensorAndPlanDriftWatches();
            allPassed &= TestMemoryBreakdownTelemetry();
            allPassed &= TestDynamicByteFormatting();
            allPassed &= TestCpuIdleGetSystemTimesMath();
            allPassed &= TestMemoryOptimizerEngineAndFallback();
            allPassed &= TestRamHotkeyAndSettingsPersistence();
            allPassed &= TestRamTooltipHoverLifecycle();
            allPassed &= TestTrayContextMenuDarkRendering();
            allPassed &= TestCustomFrameColorsAndSettingsPersistence();
            allPassed &= TestTrayBadgeBlackAndWhiteDigits();
            allPassed &= TestCpuClockBaseSmallGraphAndDriveSamples();
            allPassed &= TestZeroHardcodedHardwareInfoAndCloneIntegrity();
            allPassed &= TestWpfContextMenusDarkRendering();
            allPassed &= TestSystemMemoryListMultiVersionParsing();
            allPassed &= TestPowerCfgLocalizedParsing();
            allPassed &= TestBatteryUnknownStatusParsing();
            allPassed &= TestPerGpuTelemetryHistoryAndCsv();
            allPassed &= TestGpuHotspotAndVramAlertsAndThreadSafety();
            allPassed &= TestStorageHealthCumulativeDropTracking();
            allPassed &= TestGlobalHotkeyInvalidKeySafety();
            allPassed &= TestWindowPlacementMultiMonitorAndOffScreen();
            allPassed &= TestSettingsAtomicSaveAndCorruptImport();
            allPassed &= TestStorageCleanupSafety();
            allPassed &= TestStorageCleanupBroadRootGuard();
            allPassed &= TestStorageCleanupGuardConfirmation();
            allPassed &= TestStorageCleanupCancelKeepsWindowOpen();
            allPassed &= TestStorageCleanupClosePromptsWhileBusy();
            allPassed &= TestGpuPowerAndDownloadTimerSafety();
            allPassed &= TestSystemInfoAndHardwareSensorHelpers();
            allPassed &= FollowUpVerificationTests.RunNonElevatedChecks();

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

        public static int RunUnitSafe()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       CryoTray Non-Destructive Unit Suite        ");
            Console.WriteLine("==================================================");

            bool allPassed = true;
            allPassed &= TestRingBuffer();
            allPassed &= TestGdiLeakPrevention();
            allPassed &= TestLocalization();
            allPassed &= TestWindowsXamlInstantiation();
            allPassed &= TestHealthCheckAndSessionReport();
            allPassed &= TestTemperatureAlerts();
            allPassed &= TestFanHealthWatch();
            allPassed &= TestStorageHealthWatch();
            allPassed &= TestNoSyntheticFallbacksAndNaNSafety();
            allPassed &= TestCoolingScopedCpuStats();
            allPassed &= TestDriveTelemetryMatching();
            allPassed &= TestMultiGpuSelection();
            allPassed &= TestPawnIoDetection();
            allPassed &= TestOverlayClickThroughFlag();
            allPassed &= TestDriveExclusionFiltering();
            allPassed &= TestCustomHotkeyRecorderAndRegister();
            allPassed &= TestAutopilotToolhelpAndFullscreenScan();
            allPassed &= TestSecondLaunchActivationSignal();
            allPassed &= TestRamTelemetryAndCsvExport();
            allPassed &= TestStartupTaskXmlAndSync();
            allPassed &= TestSsdRowFormatting();
            allPassed &= TestStressCoolingDelta();
            allPassed &= TestLimitWatch();
            allPassed &= TestSensorAndPlanDriftWatches();
            allPassed &= TestMemoryBreakdownTelemetry();
            allPassed &= TestDynamicByteFormatting();
            allPassed &= TestCpuIdleGetSystemTimesMath();
            allPassed &= TestTrayContextMenuDarkRendering();
            allPassed &= TestTrayBadgeBlackAndWhiteDigits();
            allPassed &= TestCpuClockBaseSmallGraphAndDriveSamples();
            allPassed &= TestZeroHardcodedHardwareInfoAndCloneIntegrity();
            allPassed &= TestWpfContextMenusDarkRendering();
            allPassed &= TestSystemMemoryListMultiVersionParsing();
            allPassed &= TestPowerCfgLocalizedParsing();
            allPassed &= TestBatteryUnknownStatusParsing();
            allPassed &= TestPerGpuTelemetryHistoryAndCsv();
            allPassed &= TestGpuHotspotAndVramAlertsAndThreadSafety();
            allPassed &= TestStorageHealthCumulativeDropTracking();
            allPassed &= TestGlobalHotkeyInvalidKeySafety();
            allPassed &= TestWindowPlacementMultiMonitorAndOffScreen();
            allPassed &= TestSettingsAtomicSaveAndCorruptImport();
            allPassed &= TestStorageCleanupSafety();
            allPassed &= TestStorageCleanupBroadRootGuard();
            allPassed &= TestStorageCleanupGuardConfirmation();
            allPassed &= TestStorageCleanupCancelKeepsWindowOpen();
            allPassed &= TestStorageCleanupClosePromptsWhileBusy();
            allPassed &= TestGpuPowerAndDownloadTimerSafety();
            allPassed &= TestSystemInfoAndHardwareSensorHelpers();
            allPassed &= FollowUpVerificationTests.RunNonElevatedChecks();
            allPassed &= UpdateVerificationTests.RunNonElevatedChecks();

            Console.WriteLine("==================================================");
            Console.WriteLine(allPassed
                ? ">>> ALL NON-DESTRUCTIVE UNIT TESTS PASSED! <<<"
                : ">>> SOME UNIT TESTS FAILED! <<<");
            return allPassed ? 0 : 1;
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

                // Ensure the Legacy profile exists (created from Balanced when absent).
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

        private static bool TestPowerProfileWhitelistEngine()
        {
            Console.Write("[TEST] Six-Profile Whitelist Engine... ");
            var pps = PowerPlanService.Instance;
            Guid originalActive = pps.GetActiveSchemeGuid();
            try
            {
                var guids = PowerProfiles.All.Select(profile => profile.SchemeGuid).ToList();
                if (guids.Count != 6 || guids.Distinct().Count() != 6)
                {
                    throw new Exception("Expected six unique profile GUIDs.");
                }
                if (guids.Contains(PowerPlanService.BalancedGuid))
                {
                    throw new Exception("A profile reused the Balanced GUID.");
                }
                if (PowerProfiles.MenuProfiles.Count != 7 || PowerProfiles.SchemeGuids.Length != 6)
                {
                    throw new Exception("Expected six CryoTray schemes and seven menu profiles.");
                }
                if (PowerProfiles.GamingProfiles[0].Key != PowerProfiles.BalancedKey ||
                    PowerProfiles.GetGaming("").Key != PowerProfiles.BalancedKey)
                {
                    throw new Exception("Windows Balanced must be the default off profile.");
                }

                if (!pps.SwitchToProfile(PowerProfiles.GamingHighProfile))
                {
                    throw new Exception("Failed to activate Gaming (High).");
                }
                if (pps.GetActiveProfile()?.Key != PowerProfiles.GamingHighKey)
                {
                    throw new Exception("Gaming (High) was not detected as active.");
                }
                AssertSingleCryoTrayScheme(pps, PowerProfiles.GamingHighProfile, "Gaming (High)");

                if (!pps.SwitchToProfile(PowerProfiles.CoolingProfile))
                {
                    throw new Exception("Failed to activate the default Cooling profile.");
                }
                if (!pps.IsCoolingModeActive)
                {
                    throw new Exception("Cooling Mode stayed off after activating Cooling.");
                }
                AssertSingleCryoTrayScheme(pps, PowerProfiles.CoolingProfile, "Cooling");
                AssertAcValue(
                    PowerProfiles.CoolingProfile.SchemeGuid,
                    PowerGuids.SubProcessor,
                    PowerGuids.PerfBoostMode,
                    0,
                    "Cooling boost mode");
                AssertAcValue(
                    PowerProfiles.CoolingProfile.SchemeGuid,
                    PowerGuids.SubProcessor,
                    PowerGuids.ThrottleMax,
                    99,
                    "Cooling max processor state");
                AssertAcValue(
                    PowerProfiles.CoolingProfile.SchemeGuid,
                    PowerGuids.SubProcessor,
                    PowerGuids.PerfEpp,
                    70,
                    "Cooling EPP");
                AssertAcValue(
                    PowerProfiles.CoolingProfile.SchemeGuid,
                    PowerGuids.SubPcie,
                    PowerGuids.PcieAspm,
                    1,
                    "Cooling PCIe ASPM");

                if (!pps.SwitchToProfile(PowerProfiles.LegacyProfile))
                {
                    throw new Exception("Failed to activate the Legacy cooling profile.");
                }
                AssertSingleCryoTrayScheme(pps, PowerProfiles.LegacyProfile, "Legacy");

                if (!pps.SwitchToProfile(PowerProfiles.BalancedProfile))
                {
                    throw new Exception("Failed to activate Windows Balanced.");
                }
                if (pps.GetActiveProfile()?.Key != PowerProfiles.BalancedKey)
                {
                    throw new Exception("Windows Balanced was not detected as active.");
                }
                if (pps.GetInstalledSchemes().Any(scheme => PowerProfiles.IsCryoTrayScheme(scheme.Guid)))
                {
                    throw new Exception("CryoTray schemes survived a switch to Windows Balanced.");
                }

                if (pps.GetInstalledSchemes().All(scheme => scheme.Guid != PowerPlanService.BalancedGuid))
                {
                    throw new Exception("Balanced was removed while switching profiles.");
                }

                pps.CleanupForUninstall();
                if (pps.GetInstalledSchemes().Any(scheme => PowerProfiles.IsCryoTrayScheme(scheme.Guid)))
                {
                    throw new Exception("CryoTray schemes survived uninstall cleanup.");
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
                Guid restore = originalActive;
                if (pps.GetInstalledSchemes().All(scheme => scheme.Guid != restore))
                {
                    restore = PowerPlanService.BalancedGuid;
                }

                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref restore);
                pps.SyncCoolingStateFromSystem();
            }
        }

        private static void AssertAcValue(
            Guid scheme,
            Guid subGroup,
            Guid setting,
            uint expected,
            string label)
        {
            uint value;
            uint result = NativeMethods.PowerReadACValueIndex(
                IntPtr.Zero, ref scheme, ref subGroup, ref setting, out value);
            if (result != 0)
            {
                throw new Exception($"{label}: read failed with code {result}.");
            }
            if (value != expected)
            {
                throw new Exception($"{label}: expected {expected}, got {value}.");
            }
        }

        private static void AssertSingleCryoTrayScheme(
            PowerPlanService pps,
            PowerProfileDefinition expected,
            string label)
        {
            var installed = pps.GetInstalledSchemes()
                .Where(scheme => PowerProfiles.IsCryoTrayScheme(scheme.Guid))
                .ToList();
            if (installed.Count != 1 || installed[0].Guid != expected.SchemeGuid)
            {
                throw new Exception(
                    $"{label}: expected exactly one CryoTray plan, found {installed.Count}.");
            }
            if (!installed[0].Name.Equals(expected.SchemeName, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"{label}: scheme name was '{installed[0].Name}', expected '{expected.SchemeName}'.");
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

                var diagnosticsData = new SystemInfoData
                {
                    PcName = "PRIVATE-PC",
                    ProcessorName = "Test CPU",
                    GraphicsCard = "Test GPU",
                    OperatingSystem = "Test OS"
                };
                var diagnosticsSnapshot = new HardwareSnapshot
                {
                    CpuName = "Test CPU",
                    GpuName = "Test GPU",
                    CpuTemp = 50,
                    GpuTemp = 51,
                    CpuLoad = 20,
                    GpuLoad = 10,
                    GpuFanRpm = 900,
                    RamUsedGb = 4,
                    RamTotalGb = 8,
                    RamUsagePercent = 50,
                    StorageDrives = new List<DriveTelemetry>
                    {
                        new()
                        {
                            DriveLetters = "C:",
                            SmartHealthPercent = 98,
                            StatusText = "Good",
                            SerialNumber = "SERIAL-SECRET",
                            HardwareIdentifier = "PATH-SECRET"
                        }
                    }
                };
                var diagnosticsFans = new List<FanHealthStatus>
                {
                    new("fan:__diagnostics_test_fan__", "Diagnostics Test Fan", false, 1200,
                        FanHealthState.Armed, null, DateTime.UtcNow, null)
                };
                string diagnostics = SystemInfoQueryService.Instance.GenerateDiagnosticsSnapshot(
                    diagnosticsData, diagnosticsSnapshot, diagnosticsFans, false);
                if (!diagnostics.Contains("CryoTray") ||
                    !diagnostics.Contains("Test CPU") ||
                    !diagnostics.Contains("Diagnostics Test Fan: 1200 RPM") ||
                    !diagnostics.Contains("C: 98% (Good)") ||
                    diagnostics.Contains("PRIVATE-PC") ||
                    diagnostics.Contains("SERIAL-SECRET") ||
                    diagnostics.Contains("PATH-SECRET"))
                {
                    throw new Exception($"Diagnostics snapshot content or privacy mismatch.\n{diagnostics}");
                }

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
                    new Views.OverlayWindow(),
                    new Views.StorageCleanupWindow()
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

        private static bool TestGpuDetailsAndDeltaAlerts()
        {
            Console.Write("[TEST] GPU detail formatting and hotspot delta alert... ");
            var settings = SettingsService.Instance.Current;
            bool originalMute = settings.MuteTemperatureAlerts;
            bool originalDeltaEnabled = settings.GpuDeltaAlertsEnabled;
            int originalThreshold = settings.GpuHotspotDeltaThresholdCelsius;
            try
            {
                settings.MuteTemperatureAlerts = false;
                settings.GpuDeltaAlertsEnabled = true;
                settings.GpuHotspotDeltaThresholdCelsius = 25;

                var details = new HardwareSnapshot
                {
                    GpuCoreClockMhz = 1455,
                    GpuMemoryClockMhz = 7001,
                    GpuThrottleReason = "GPU Performance Limit - Power"
                };
                if (Views.MainWindow.FormatGpuClocks(details) != "1455/7001 MHz")
                {
                    throw new Exception($"Unexpected clocks: {Views.MainWindow.FormatGpuClocks(details)}");
                }

                if (Views.MainWindow.CompactThrottleReason(details.GpuThrottleReason) != "Power")
                {
                    throw new Exception("Throttle reason was not compacted.");
                }

                var service = new TemperatureAlertService();
                int deltaAlerts = 0;
                service.AlertRaised += alert =>
                {
                    if (alert.Component == TemperatureAlertComponent.GpuDelta)
                    {
                        deltaAlerts++;
                    }
                };

                var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                var hot = new HardwareSnapshot { GpuTemp = 50, GpuHotspotTemp = 80 };
                service.Observe(hot, start);
                service.Observe(hot, start.AddMinutes(4));
                if (deltaAlerts != 0)
                {
                    throw new Exception("Delta alert fired before the sustain window.");
                }

                service.Observe(hot, start.AddMinutes(5));
                if (deltaAlerts != 1)
                {
                    throw new Exception($"Expected one delta alert, got {deltaAlerts}.");
                }

                service.Observe(hot, start.AddMinutes(20));
                if (deltaAlerts != 1)
                {
                    throw new Exception("Delta alert ignored its cooldown.");
                }

                var cool = new HardwareSnapshot { GpuTemp = 50, GpuHotspotTemp = 70 };
                service.Observe(cool, start.AddMinutes(22));
                service.Observe(hot, start.AddMinutes(36));
                service.Observe(hot, start.AddMinutes(41));
                if (deltaAlerts != 2)
                {
                    throw new Exception($"Expected a second delta alert after reset, got {deltaAlerts}.");
                }

                var resetService = new TemperatureAlertService();
                int resetAlerts = 0;
                resetService.AlertRaised += alert =>
                {
                    if (alert.Component == TemperatureAlertComponent.GpuDelta)
                    {
                        resetAlerts++;
                    }
                };
                var justBelow = new HardwareSnapshot { GpuTemp = 50, GpuHotspotTemp = 74 };
                resetService.Observe(hot, start);
                resetService.Observe(justBelow, start.AddMinutes(4));
                resetService.Observe(hot, start.AddMinutes(6));
                resetService.Observe(hot, start.AddMinutes(10));
                if (resetAlerts != 0)
                {
                    throw new Exception("A gap dip below the threshold did not reset the sustain timer.");
                }

                resetService.Observe(hot, start.AddMinutes(11));
                if (resetAlerts != 1)
                {
                    throw new Exception("The reset delta watch did not alert after five continuous minutes.");
                }

                settings.GpuDeltaAlertsEnabled = false;
                var disabled = new TemperatureAlertService();
                int disabledAlerts = 0;
                disabled.AlertRaised += _ => disabledAlerts++;
                disabled.Observe(hot, start);
                disabled.Observe(hot, start.AddMinutes(6));
                if (disabledAlerts != 0)
                {
                    throw new Exception("Disabled GPU delta alerts were raised.");
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
                settings.GpuDeltaAlertsEnabled = originalDeltaEnabled;
                settings.GpuHotspotDeltaThresholdCelsius = originalThreshold;
            }
        }

        private static bool TestProcessUsageSampling()
        {
            Console.Write("[TEST] Process CPU/GPU usage sampling... ");
            try
            {
                var first = ProcessUsageService.CaptureAsync().GetAwaiter().GetResult();
                Thread.Sleep(350);
                var second = ProcessUsageService.CaptureAsync().GetAwaiter().GetResult();
                if (!second.CpuMeasured)
                {
                    throw new Exception("CPU percentages were not measured on the second sample.");
                }

                if (second.Entries.Count == 0)
                {
                    throw new Exception("No process entries were returned.");
                }

                if (!first.Entries.Any() && !second.Entries.Any())
                {
                    throw new Exception("Both samples were empty.");
                }

                if (second.Entries.Any(entry => entry.ProcessId <= 0 || string.IsNullOrWhiteSpace(entry.Name)))
                {
                    throw new Exception("A process entry had no id or name.");
                }

                Console.WriteLine(
                    $"PASSED ({second.Entries.Count} processes, GPU counters: {(second.GpuAvailable ? "available" : "unavailable")})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
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
                    var scw = new Views.StorageCleanupWindow();
                    var kac = new Views.KeepAwakeConfirmWindow(true);
                    var kad = new Views.KeepAwakeDurationWindow();
                    var hcw = new Views.HealthCheckWindow(false, false);
                    var srw = new Views.SessionReportWindow();
                    mw.Close();
                    sw.Close();
                    siw.Close();
                    stw.Close();
                    scw.Close();
                    kac.Close();
                    kad.Close();
                    hcw.Close();
                    srw.Close();
                    _ = ow;
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
                finally
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
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

            Console.WriteLine("PASSED (All 10 Windows instantiated cleanly)");
            return true;
        }

        private static bool TestHealthCheckAndSessionReport()
        {
            Console.Write("[TEST] Health check & session report services... ");
            try
            {
                HealthCheckReport health = HealthCheckService.Run(postUpdate: false);
                if (health.Items.Count != 7)
                {
                    throw new Exception($"Expected 7 health checks, got {health.Items.Count}");
                }

                if (health.Items.Select(item => item.Id).Distinct().Count() != 7)
                {
                    throw new Exception("Health check IDs are not unique.");
                }

                foreach (HealthCheckItem item in health.Items.Where(item =>
                    item.Level == HealthCheckLevel.Warning ||
                    item.Level == HealthCheckLevel.ActionRequired))
                {
                    if (item.Action == HealthCheckActionKind.None)
                    {
                        throw new Exception($"Issue {item.Id} has no fix action.");
                    }
                }

                DateTime now = DateTime.Now;
                var samples = new List<TelemetrySample>
                {
                    new()
                    {
                        Timestamp = now,
                        CoolingModeActive = true,
                        PowerProfileKey = PowerProfiles.CoolingKey,
                        CpuTemp = 40,
                        GpuTemp = 35
                    },
                    new()
                    {
                        Timestamp = now.AddSeconds(1),
                        CoolingModeActive = false,
                        PowerProfileKey = PowerProfiles.BalancedKey,
                        CpuTemp = 45,
                        GpuTemp = 40
                    },
                    new()
                    {
                        Timestamp = now.AddSeconds(2),
                        CoolingModeActive = false,
                        PowerProfileKey = PowerProfiles.GamingHighKey,
                        CpuTemp = 50,
                        GpuTemp = 45
                    }
                };
                IReadOnlyList<SessionChartEvent> markers =
                    SessionReportService.GetChartEvents(samples);
                if (!markers.Any(item => item.Kind == "cooling") ||
                    !markers.Any(item => item.Kind == "profile"))
                {
                    throw new Exception("Chart events did not include cooling and profile changes.");
                }

                string reportText = SessionReportService.BuildText(SessionReportService.Build());
                if (string.IsNullOrWhiteSpace(reportText) ||
                    !reportText.Contains(LocalizationService.Get("Session_Title"), StringComparison.Ordinal))
                {
                    throw new Exception("Session report text was empty or missing its title.");
                }

                Console.WriteLine("PASSED (7 checks, cooling/profile markers, report text)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
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

        private static bool TestOverlayClickThroughFlag()
        {
            Console.Write("[TEST] Overlay Click-Through WS_EX_TRANSPARENT... ");
            var settings = SettingsService.Instance.Current;
            bool originalClickThrough = settings.OverlayClickThrough;
            double? origLeft = settings.OverlayLeft;
            double? origTop = settings.OverlayTop;

            Views.OverlayWindow? overlay = null;
            try
            {
                settings.OverlayClickThrough = true;
                overlay = new Views.OverlayWindow
                {
                    Left = -3200,
                    Top = -3200,
                    ShowActivated = false
                };
                overlay.Show();
                IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    throw new Exception("Overlay HWND was not created.");
                }

                int exStyleOn = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                if ((exStyleOn & NativeMethods.WS_EX_TRANSPARENT) == 0)
                {
                    throw new Exception("WS_EX_TRANSPARENT was not set when OverlayClickThrough is enabled.");
                }

                overlay.ApplyClickThrough(false);
                int exStyleOff = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                if ((exStyleOff & NativeMethods.WS_EX_TRANSPARENT) != 0)
                {
                    throw new Exception("WS_EX_TRANSPARENT was not cleared when click-through disabled.");
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
                settings.OverlayClickThrough = originalClickThrough;
                try { overlay?.Close(); } catch { }
                settings.OverlayLeft = origLeft;
                settings.OverlayTop = origTop;
            }
        }

        private static bool TestDriveExclusionFiltering()
        {
            Console.Write("[TEST] Per-Drive Exclusion Key & Alert Filtering... ");
            var settings = SettingsService.Instance.Current;
            var origExcluded = new List<string>(settings.ExcludedDrives ?? new List<string>());
            bool origMute = settings.MuteTemperatureAlerts;
            int origDiskThreshold = settings.HighTempThresholdDisk;

            try
            {
                var hiddenDrive = new DriveTelemetry
                {
                    Index = 0,
                    Model = "External Backup HDD",
                    SerialNumber = "USB-998877",
                    Temperature = 88
                };
                var activeDrive = new DriveTelemetry
                {
                    Index = 1,
                    Model = "Primary NVMe",
                    SerialNumber = "NVME-112233",
                    Temperature = 88
                };

                string hiddenKey = HardwareMonitorService.GetDriveExclusionKey(hiddenDrive);
                string activeKey = HardwareMonitorService.GetDriveExclusionKey(activeDrive);
                if (hiddenKey != "serial:USB-998877" || activeKey != "serial:NVME-112233")
                {
                    throw new Exception($"Unexpected drive exclusion keys: {hiddenKey}, {activeKey}");
                }

                settings.ExcludedDrives = new List<string> { hiddenKey };
                if (!HardwareMonitorService.IsDriveExcluded(hiddenDrive, settings.ExcludedDrives) ||
                    HardwareMonitorService.IsDriveExcluded(activeDrive, settings.ExcludedDrives))
                {
                    throw new Exception("IsDriveExcluded filter mismatch.");
                }

                var alertService = new TemperatureAlertService();
                var alertedModels = new List<string>();
                alertService.AlertRaised += alert =>
                {
                    if (alert.Component == TemperatureAlertComponent.Drive && alert.DriveName != null)
                    {
                        alertedModels.Add(alert.DriveName);
                    }
                };

                settings.MuteTemperatureAlerts = false;
                settings.HighTempThresholdDisk = 60;
                alertService.Observe(new HardwareSnapshot
                {
                    StorageDrives = new List<DriveTelemetry> { hiddenDrive, activeDrive }
                });

                if (alertedModels.Count != 1 || alertedModels[0] != "Primary NVMe")
                {
                    throw new Exception($"Expected only Primary NVMe alert, got [{string.Join(", ", alertedModels)}]");
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
                settings.ExcludedDrives = origExcluded;
                settings.MuteTemperatureAlerts = origMute;
                settings.HighTempThresholdDisk = origDiskThreshold;
            }
        }

        private static bool TestCustomHotkeyRecorderAndRegister()
        {
            Console.Write("[TEST] Custom Hotkey Recorder & Registration... ");
            HwndSource? source = null;
            var hotkeyService = new GlobalHotkeyService();
            try
            {
                if (!GlobalHotkeyService.TryCaptureShortcut(Key.F9, Key.None, ModifierKeys.Control | ModifierKeys.Alt, out string mods1, out string key1) ||
                    mods1 != "Ctrl+Alt" || key1 != "F9")
                {
                    throw new Exception("Failed capturing Ctrl+Alt+F9.");
                }

                if (!GlobalHotkeyService.TryCaptureShortcut(Key.System, Key.D1, ModifierKeys.Alt | ModifierKeys.Shift, out string mods2, out string key2) ||
                    mods2 != "Alt+Shift" || key2 != "D1" ||
                    GlobalHotkeyService.FormatShortcutDisplay(mods2, key2) != "Alt + Shift + 1")
                {
                    throw new Exception("Failed capturing/formatting Alt+Shift+1.");
                }

                if (!GlobalHotkeyService.TryCaptureShortcut(Key.F10, Key.None, ModifierKeys.None, out string mods3, out string key3) ||
                    mods3 != "" || key3 != "F10")
                {
                    throw new Exception("Failed capturing standalone F10.");
                }

                if (GlobalHotkeyService.TryCaptureShortcut(Key.A, Key.None, ModifierKeys.None, out _, out _))
                {
                    throw new Exception("Bare letter A without modifiers must be rejected.");
                }

                var p = new HwndSourceParameters("CryoTrayHotkeyTest") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) };
                source = new HwndSource(p);
                bool registered = hotkeyService.Register(source.Handle, "Ctrl+Alt+Shift", "F11");
                if (!registered || !hotkeyService.IsRegistered)
                {
                    throw new Exception("Failed RegisterHotKey for Ctrl+Alt+Shift+F11.");
                }

                hotkeyService.Unregister();
                if (hotkeyService.IsRegistered)
                {
                    throw new Exception("IsRegistered stayed true after Unregister.");
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
                hotkeyService.Unregister();
                source?.Dispose();
            }
        }

        private static bool TestAutopilotToolhelpAndFullscreenScan()
        {
            Console.Write("[TEST] Autopilot Toolhelp32 & Fullscreen Detection... ");
            try
            {
                var processes = AutopilotService.EnumerateProcessesByPidToolhelp32();
                if (processes.Count == 0)
                {
                    throw new Exception("Toolhelp32 returned zero processes.");
                }

                if (processes.ContainsKey((uint)Environment.ProcessId))
                {
                    throw new Exception("Toolhelp32 should exclude the current CryoTray process ID.");
                }

                _ = AutopilotService.IsFullscreen3DOrBorderlessActive(processes);

                if (!AutopilotService.IsWhitelistedProcessName("Game.exe", new[] { "game" }) ||
                    !AutopilotService.IsWhitelistedProcessName("Game.exe", new[] { "C:\\Games\\Game.exe" }) ||
                    AutopilotService.IsWhitelistedProcessName("launcher.exe", new[] { "game" }) ||
                    AutopilotService.IsWhitelistedProcessName("", new[] { "game" }))
                {
                    throw new Exception("Whitelisted foreground process matching failed.");
                }

                Console.WriteLine($"PASSED ({processes.Count} processes scanned)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestSecondLaunchActivationSignal()
        {
            Console.Write("[TEST] Second-Launch Activation EventWaitHandle... ");
            string testEventName = $"CryoTray-Test-Activate-{Guid.NewGuid():N}";
            try
            {
                using var waitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, testEventName);
                Task.Run(() => App.TrySignalExistingInstance(testEventName)).Wait(2000);
                if (!waitEvent.WaitOne(1500))
                {
                    throw new Exception("Activation signal was not received.");
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

        private static bool TestRamTelemetryAndCsvExport()
        {
            Console.Write("[TEST] GlobalMemoryStatusEx RAM & TelemetryCsvService... ");
            string tempCsv = Path.Combine(Path.GetTempPath(), $"cryotray_test_{Guid.NewGuid():N}.csv");
            try
            {
                if (!HardwareMonitorService.TryReadMemoryMetrics(out double usedGb, out double totalGb, out double usagePct))
                {
                    throw new Exception("GlobalMemoryStatusEx failed.");
                }

                if (totalGb <= 0 || usedGb < 0 || usedGb > totalGb || usagePct < 0 || usagePct > 100)
                {
                    throw new Exception($"Invalid RAM values: used={usedGb}, total={totalGb}, pct={usagePct}");
                }

                var samples = new List<TelemetrySample>
                {
                    new()
                    {
                        Timestamp = new DateTime(2026, 11, 18, 12, 0, 0),
                        CoolingModeActive = true,
                        CpuTemp = double.NaN,
                        CpuGhz = 3.85,
                        CpuLoad = 18.2,
                        GpuTemp = 48.0,
                        GpuLoad = 12.0,
                        GpuPowerWatts = 31,
                        RamUsedGb = usedGb,
                        RamUsagePercent = usagePct
                    },
                    new()
                    {
                        Timestamp = new DateTime(2026, 11, 18, 12, 0, 1),
                        CoolingModeActive = false,
                        CpuTemp = 62.5,
                        CpuGhz = 4.55,
                        CpuLoad = 75.0,
                        CpuPowerWatts = 105,
                        GpuTemp = 65.0,
                        GpuLoad = 88.0,
                        GpuPowerWatts = 195,
                        RamUsedGb = usedGb,
                        RamUsagePercent = usagePct
                    }
                };

                TelemetryCsvService.ExportSamples(samples, tempCsv);
                string[] lines = File.ReadAllLines(tempCsv);
                if (lines.Length != 3 || !lines[0].StartsWith("Timestamp,CoolingMode,CpuTempC", StringComparison.Ordinal))
                {
                    throw new Exception($"Unexpected CSV structure (lines={lines.Length}).");
                }

                if (string.Join("\n", lines).Contains("NaN", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("CSV output must never contain literal NaN.");
                }

                Console.WriteLine($"PASSED ({usedGb:0.00}/{totalGb:0.00} GB, {usagePct:0.0}%)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(tempCsv)) File.Delete(tempCsv); } catch { }
            }
        }

        private static bool TestPowerPlanUninstallCleanup()
        {
            Console.Write("[TEST] PowerPlanService Uninstall Cleanup... ");
            var pps = PowerPlanService.Instance;
            Guid originalActive = pps.GetActiveSchemeGuid();
            try
            {
                pps.EnsureCryoTrayCoolingScheme();
                pps.CleanupForUninstall();

                var remaining = pps.GetInstalledSchemes()
                    .Where(s => PowerProfiles.All.Any(profile => profile.SchemeGuid == s.Guid) ||
                        s.Name.Equals(PowerPlanService.ObsoleteCryoTraySchemeName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (remaining.Count != 0)
                {
                    throw new Exception($"Expected 0 CryoTray schemes after cleanup, found {remaining.Count}.");
                }

                Guid recreated = pps.EnsureCryoTrayCoolingScheme();
                if (recreated == Guid.Empty)
                {
                    throw new Exception("Failed to recreate the Legacy profile after cleanup check.");
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
                Guid restore = originalActive;
                if (pps.GetInstalledSchemes().All(s => s.Guid != restore))
                {
                    restore = pps.GetNormalPowerPlanGuid();
                }

                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref restore);
                pps.SyncCoolingStateFromSystem();
            }
        }

        private static bool TestStartupTaskXmlAndSync()
        {
            Console.Write("[TEST] StartupTaskService XML, Battery Flags & Live Sync... ");
            string testTaskName = $"CryoTray-Test-{Guid.NewGuid():N}";
            try
            {
                string xml = StartupTaskService.BuildTaskXml(
                    @"C:\Program Files\CryoTray\CryoTray.exe",
                    @"C:\Program Files\CryoTray",
                    @"DESKTOP\User");

                string[] requiredNodes =
                {
                    "<LogonType>InteractiveToken</LogonType>",
                    "<RunLevel>HighestAvailable</RunLevel>",
                    "<Delay>PT5S</Delay>",
                    "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>",
                    "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>",
                    "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>",
                    "<Arguments>/autostart</Arguments>",
                    "<WorkingDirectory>C:\\Program Files\\CryoTray</WorkingDirectory>"
                };

                foreach (string node in requiredNodes)
                {
                    if (!xml.Contains(node, StringComparison.Ordinal))
                    {
                        throw new Exception($"Task XML missing required node: {node}");
                    }
                }

                if (StartupTaskService.IsCurrentProcessElevated())
                {
                    string expectedExe = StartupTaskService.ResolveExecutablePath();
                    if (!File.Exists(expectedExe))
                    {
                        throw new Exception($"Expected executable missing at {expectedExe}");
                    }

                    if (!StartupTaskService.TrySetStartupForTask(testTaskName, true, out string? createErr))
                    {
                        throw new Exception($"Elevated task registration failed: {createErr}");
                    }

                    var state = StartupTaskService.QueryStateForTask(testTaskName);
                    if (!state.Exists ||
                        !string.Equals(state.CommandPath, expectedExe, StringComparison.OrdinalIgnoreCase) ||
                        !(state.Arguments ?? "").Contains(StartupTaskService.AutostartArg, StringComparison.OrdinalIgnoreCase) ||
                        !state.AllowsBattery)
                    {
                        throw new Exception(
                            $"Unexpected queried task state: exists={state.Exists}, cmd={state.CommandPath}, args={state.Arguments}, battery={state.AllowsBattery}");
                    }

                    if (!StartupTaskService.TrySetStartupForTask(testTaskName, false, out string? deleteErr))
                    {
                        throw new Exception($"Elevated task cleanup failed: {deleteErr}");
                    }

                    if (StartupTaskService.QueryStateForTask(testTaskName).Exists)
                    {
                        throw new Exception("Test scheduled task still exists after deletion.");
                    }

                    Console.WriteLine("PASSED (Task 1.2 XML & live elevated schtasks round-trip verified)");
                }
                else
                {
                    Console.WriteLine("PASSED (Task 1.2 XML verified; live schtasks requires elevation)");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                StartupTaskService.TrySetStartupForTask(testTaskName, false, out _);
            }
        }

        private static bool TestSsdRowFormatting()
        {
            Console.Write("[TEST] Dashboard SSD Drive Letter & Fallback Formatting... ");
            try
            {
                var driveC = new DriveTelemetry { Index = 0, DriveLetters = "C" };
                var driveMulti = new DriveTelemetry { Index = 1, DriveLetters = "D:, E" };
                var driveUnmounted = new DriveTelemetry { Index = 2, DriveLetters = "" };

                string badgeC = Views.MainWindow.FormatSsdBadgeText(driveC);
                string labelC = Views.MainWindow.FormatSsdRowLabel(driveC);
                if (badgeC != "C" || labelC != "SSD (C:)")
                {
                    throw new Exception($"Single drive mismatch: badge='{badgeC}', label='{labelC}'");
                }

                string badgeMulti = Views.MainWindow.FormatSsdBadgeText(driveMulti);
                string labelMulti = Views.MainWindow.FormatSsdRowLabel(driveMulti);
                if (badgeMulti != "D" || labelMulti != "SSD (D:, E:)")
                {
                    throw new Exception($"Multi drive mismatch: badge='{badgeMulti}', label='{labelMulti}'");
                }

                string badgeNone = Views.MainWindow.FormatSsdBadgeText(driveUnmounted);
                string labelNone = Views.MainWindow.FormatSsdRowLabel(driveUnmounted);
                if (badgeNone != "3" || labelNone != "SSD 3")
                {
                    throw new Exception($"Unmounted drive mismatch: badge='{badgeNone}', label='{labelNone}'");
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

        private static bool TestTimedCoolingPause()
        {
            Console.Write("[TEST] Timed Cooling Pause & Resume... ");
            var pps = PowerPlanService.Instance;
            Guid originalActive = pps.GetActiveSchemeGuid();
            var app = System.Windows.Application.Current as App;
            if (app == null)
            {
                Console.WriteLine("FAILED: App.Current is null.");
                return false;
            }

            try
            {
                pps.EnsureCryoTrayCoolingScheme();
                pps.SetCoolingMode(true);
                if (!pps.IsCoolingModeActive)
                {
                    throw new Exception("Failed to activate Cooling Mode before pause.");
                }

                app.PauseCoolingFor(TimeSpan.FromMinutes(15));
                if (!app.IsCoolingPausedByTimer || !app.CoolingPausedUntilUtc.HasValue)
                {
                    throw new Exception("PauseCoolingFor did not arm timed pause.");
                }

                if (pps.IsCoolingModeActive)
                {
                    throw new Exception("PauseCoolingFor did not turn Cooling Mode off.");
                }

                app.CancelCoolingPause(resumeCooling: false);
                if (app.IsCoolingPausedByTimer || pps.IsCoolingModeActive)
                {
                    throw new Exception("CancelCoolingPause(resumeCooling: false) left timer active or changed scheme.");
                }

                app.PauseCoolingFor(TimeSpan.FromMinutes(30));
                app.CancelCoolingPause(resumeCooling: true, forceResume: true);
                if (app.IsCoolingPausedByTimer || !pps.IsCoolingModeActive)
                {
                    throw new Exception("CancelCoolingPause(resumeCooling: true) failed to resume Cooling Mode.");
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
                app.CancelCoolingPause(resumeCooling: false);
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref originalActive);
                pps.SyncCoolingStateFromSystem();
            }
        }

        private static bool TestFanHealthWatch()
        {
            Console.Write("[TEST] Fan Health Watch Arming, Idle Guard & Resume Grace... ");
            var settings = SettingsService.Instance.Current;
            bool originalSetting = settings.FanHealthAlertsEnabled;
            try
            {
                settings.FanHealthAlertsEnabled = true;
                var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                var service = new FanHealthService();
                var alerts = new List<FanStoppedEvent>();
                service.FanStopped += alerts.Add;

                HardwareSnapshot Snapshot(int rpm, double cpuLoad, int? gpuRpm = null,
                    double gpuLoad = 0, double gpuTemp = 40) => new()
                {
                    CpuLoad = cpuLoad,
                    GpuLoad = gpuLoad,
                    CpuTemp = cpuLoad >= 25 ? 70 : 50,
                    GpuTemp = gpuTemp,
                    GpuFanRpm = gpuRpm,
                    Fans = new List<FanTelemetry> { new() { Name = "Chassis Fan", Rpm = rpm } }
                };

                // A header that never spins must never arm.
                service.Observe(Snapshot(0, 80), start);
                service.Observe(Snapshot(0, 80), start.AddMinutes(2));
                if (alerts.Count != 0)
                {
                    throw new Exception("Unconnected fan header armed unexpectedly.");
                }

                var idleStatus = service.GetStatuses().First();
                if (idleStatus.State != FanHealthState.IdleAtZero || idleStatus.Rpm != 0)
                {
                    throw new Exception("Unconnected fan header did not report the idle state.");
                }

                // Spin for a full minute, then stop under load.
                for (int second = 0; second <= 60; second++)
                {
                    service.Observe(Snapshot(1000, 80), start.AddSeconds(second));
                }

                var armedStatus = service.GetStatuses().First();
                if (armedStatus.State != FanHealthState.Armed || armedStatus.Rpm != 1000)
                {
                    throw new Exception("Armed fan did not report the armed state.");
                }

                service.Observe(Snapshot(0, 80), start.AddSeconds(61));
                var stoppedStatus = service.GetStatuses().First();
                if (stoppedStatus.State != FanHealthState.Stopped || stoppedStatus.Rpm != 0)
                {
                    throw new Exception("Stopped fan did not report the stopped state.");
                }
                service.Observe(Snapshot(0, 80), start.AddSeconds(90));
                if (alerts.Count != 0)
                {
                    throw new Exception("Fan alert fired before 30 seconds.");
                }

                service.Observe(Snapshot(0, 80), start.AddSeconds(91));
                if (alerts.Count != 1 ||
                    alerts[0].FanName != "Chassis Fan" ||
                    alerts[0].LastRpm != 1000 ||
                    alerts[0].IsGpuFan)
                {
                    throw new Exception("Fan alert did not fire after the 30-second stop window.");
                }

                var alertedStatus = service.GetStatuses().First();
                if (!alertedStatus.LastAlertUtc.HasValue)
                {
                    throw new Exception("Fan alert did not record its last alert time.");
                }

                // GPU fans use the GPU load and heat guard, not CPU demand.
                var gpuService = new FanHealthService();
                var gpuAlerts = new List<FanStoppedEvent>();
                gpuService.FanStopped += gpuAlerts.Add;
                for (int second = 0; second <= 60; second++)
                {
                    gpuService.Observe(
                        Snapshot(0, 5, gpuRpm: 1200, gpuLoad: 80, gpuTemp: 70),
                        start.AddSeconds(second));
                }

                gpuService.Observe(
                    Snapshot(0, 5, gpuRpm: 0, gpuLoad: 80, gpuTemp: 70),
                    start.AddSeconds(61));
                gpuService.Observe(
                    Snapshot(0, 5, gpuRpm: 0, gpuLoad: 80, gpuTemp: 70),
                    start.AddSeconds(91));
                if (gpuAlerts.Count != 1 ||
                    !gpuAlerts[0].IsGpuFan ||
                    gpuAlerts[0].FanKey != "gpu" ||
                    gpuAlerts[0].LastRpm != 1200)
                {
                    throw new Exception("GPU fan health did not use GPU demand and the GPU fan sensor.");
                }

                var gpuStatus = gpuService.GetStatuses().First(status => status.IsGpuFan);
                if (gpuStatus.State != FanHealthState.Stopped || gpuStatus.Rpm != 0)
                {
                    throw new Exception("GPU fan did not report its stopped status.");
                }

                // Idle fan curves are ignored even after they stop.
                var idleService = new FanHealthService();
                var idleAlerts = new List<FanStoppedEvent>();
                idleService.FanStopped += idleAlerts.Add;
                for (int second = 0; second <= 60; second++)
                {
                    idleService.Observe(Snapshot(1000, 5), start.AddSeconds(second));
                }

                idleService.Observe(Snapshot(0, 5), start.AddSeconds(61));
                idleService.Observe(Snapshot(0, 5), start.AddSeconds(120));
                if (idleAlerts.Count != 0)
                {
                    throw new Exception("Idle fan stop raised an alert.");
                }

                // Resume grace clears armed state and suppresses checks.
                var resumeService = new FanHealthService();
                var resumeAlerts = new List<FanStoppedEvent>();
                resumeService.FanStopped += resumeAlerts.Add;
                for (int second = 0; second <= 60; second++)
                {
                    resumeService.Observe(Snapshot(1000, 80), start.AddSeconds(second));
                }

                resumeService.Resume(start.AddSeconds(60));
                resumeService.Observe(Snapshot(0, 80), start.AddSeconds(70));
                resumeService.Observe(Snapshot(0, 80), start.AddSeconds(130));
                if (resumeAlerts.Count != 0)
                {
                    throw new Exception("Resume grace failed to suppress fan checks.");
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
                settings.FanHealthAlertsEnabled = originalSetting;
            }
        }

        private static bool TestLimitWatch()
        {
            Console.Write("[TEST] Limit watch below-nominal detection... ");
            var failures = new System.Collections.Generic.List<string>();

            if (LimitWatch.IsBelowNominal(3.70, 3.80, 80))
            {
                failures.Add("a 99% cap at 3.70 GHz was flagged");
            }

            if (!LimitWatch.IsBelowNominal(3.40, 3.80, 80))
            {
                failures.Add("3.40 GHz under load was not flagged");
            }

            if (LimitWatch.IsBelowNominal(3.40, 3.80, 20))
            {
                failures.Add("an idle sample was flagged");
            }

            if (LimitWatch.IsBelowNominal(double.NaN, 3.80, 80) ||
                LimitWatch.IsBelowNominal(3.40, double.NaN, 80))
            {
                failures.Add("a missing clock or base was flagged");
            }

            if (new CryoTray.Models.AppSettings().LimitWatchEnabled)
            {
                failures.Add("the feature did not default to off");
            }

            var samples = new System.Collections.Generic.List<CryoTray.Models.TelemetrySample>
            {
                new() { Timestamp = new DateTime(2026, 1, 1, 10, 0, 0), CpuBelowNominal = true, CpuTemp = 88.5 },
                new() { Timestamp = new DateTime(2026, 1, 1, 10, 0, 1), CpuBelowNominal = true, CpuTemp = 90.0 },
                new() { Timestamp = new DateTime(2026, 1, 1, 10, 0, 2), CpuBelowNominal = false, CpuTemp = 80.0 },
                new() { Timestamp = new DateTime(2026, 1, 1, 10, 0, 3), CpuBelowNominal = true, CpuTemp = 87.0 },
                new() { Timestamp = new DateTime(2026, 1, 1, 10, 0, 4), CpuBelowNominal = false, CpuTemp = 80.0 }
            };
            var summary = SessionReportService.SummarizeBelowNominal(samples);
            if (summary.Periods != 2)
            {
                failures.Add($"expected 2 periods, got {summary.Periods}");
            }

            if (Math.Abs(summary.Total.TotalSeconds - 3) > 0.01)
            {
                failures.Add($"expected 3 s total, got {summary.Total.TotalSeconds:F2}");
            }

            if (Math.Abs(summary.Longest.TotalSeconds - 2) > 0.01)
            {
                failures.Add($"expected 2 s longest, got {summary.Longest.TotalSeconds:F2}");
            }

            if (Math.Abs(summary.PeakTemp - 90.0) > 0.01)
            {
                failures.Add($"expected 90 C peak, got {summary.PeakTemp:F1}");
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("PASSED (boundaries, off by default, 2 periods, 3 s total, 2 s longest)");
                return true;
            }

            Console.WriteLine("FAILED");
            foreach (string failure in failures)
            {
                Console.WriteLine($"  -> {failure}");
            }

            return false;
        }

        private static bool TestSensorAndPlanDriftWatches()
        {
            Console.Write("[TEST] Sensor dropout and power-plan drift watches... ");
            var failures = new List<string>();

            var watch = new SensorDropoutWatch();
            DateTime t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            if (watch.Observe(true, true, t0) != SensorDropoutKind.None)
            {
                failures.Add("a healthy sample was reported");
            }

            if (watch.Observe(false, true, t0.AddSeconds(10)) != SensorDropoutKind.None)
            {
                failures.Add("CPU loss fired on its first missing sample");
            }

            if (watch.Observe(false, true, t0.AddSeconds(39)) != SensorDropoutKind.None)
            {
                failures.Add("CPU loss fired before 30 s");
            }

            if (watch.Observe(false, true, t0.AddSeconds(41)) != SensorDropoutKind.Cpu)
            {
                failures.Add("CPU loss with a live GPU was not reported as CPU only");
            }

            if (watch.Observe(false, false, t0.AddMinutes(10)) != SensorDropoutKind.None)
            {
                failures.Add("a second entry fired inside the 30 min window");
            }

            if (watch.Observe(true, false, t0.AddMinutes(11)) != SensorDropoutKind.None)
            {
                failures.Add("a missing GPU alone was reported");
            }

            if (watch.Observe(false, false, t0.AddMinutes(41)) != SensorDropoutKind.None)
            {
                failures.Add("total loss fired before 30 s");
            }

            if (watch.Observe(false, false, t0.AddMinutes(42)) != SensorDropoutKind.All)
            {
                failures.Add("total loss was not reported after the window elapsed");
            }

            if (watch.Observe(false, false, t0.AddMinutes(43)) != SensorDropoutKind.None)
            {
                failures.Add("total loss repeated inside its own window");
            }

            if (!SensorDropoutWatch.HasCpuReadings(new HardwareSnapshot { CpuTemp = 64 }) ||
                !SensorDropoutWatch.HasCpuReadings(new HardwareSnapshot { CpuMeasuredGhz = 3.6 }))
            {
                failures.Add("a CPU temperature or measured clock was treated as missing");
            }

            if (SensorDropoutWatch.HasCpuReadings(new HardwareSnapshot { CpuGhz = 3.7 }))
            {
                failures.Add("the PDH clock fallback counted as a CPU reading");
            }

            if (!SensorDropoutWatch.HasGpuReadings(new HardwareSnapshot
                {
                    Gpus = new List<GpuTelemetry> { new() { Temperature = 55 } }
                }))
            {
                failures.Add("a GPU temperature was treated as missing");
            }

            if (SensorDropoutWatch.HasCpuReadings(new HardwareSnapshot()) ||
                SensorDropoutWatch.HasGpuReadings(new HardwareSnapshot()))
            {
                failures.Add("an empty snapshot counted as healthy");
            }

            var cooling = PowerProfiles.GetCooling(null);
            if (PowerPlanService.IsExternalDrift(cooling, cooling.SchemeGuid))
            {
                failures.Add("an unchanged plan was treated as drift");
            }

            if (PowerPlanService.IsExternalDrift(null, cooling.SchemeGuid))
            {
                failures.Add("a null previous profile was treated as drift");
            }

            if (PowerPlanService.IsExternalDrift(
                    PowerProfiles.BalancedProfile, PowerProfiles.GamingHighProfile.SchemeGuid))
            {
                failures.Add("a non-CryoTray previous plan was treated as drift");
            }

            if (!PowerPlanService.IsExternalDrift(cooling, PowerProfiles.BalancedProfile.SchemeGuid))
            {
                failures.Add("Cooling to Balanced was not treated as drift");
            }

            if (!PowerPlanService.IsExternalDrift(cooling, PowerProfiles.GamingHighProfile.SchemeGuid))
            {
                failures.Add("a CryoTray profile swap by another process was not treated as drift");
            }

            var defaults = new AppSettings();
            if (!defaults.SensorDropoutLogEnabled || !defaults.PowerPlanDriftNoticeEnabled)
            {
                failures.Add("one of the watches did not default to on");
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("PASSED (CPU and total loss, 30 s window, 30 min cap, drift rule)");
                return true;
            }

            Console.WriteLine("FAILED");
            foreach (string failure in failures)
            {
                Console.WriteLine($"  -> {failure}");
            }

            return false;
        }

        private static bool TestStressCoolingDelta()
        {
            Console.Write("[TEST] Stress Test Cooling Delta, Throughput & FPS Impact... ");
            try
            {
                var samples = new List<TelemetrySample>();
                for (int i = 0; i < 6; i++)
                {
                    samples.Add(new TelemetrySample
                    {
                        CoolingModeActive = false,
                        CpuTemp = 75 + i,
                        CpuPowerWatts = 107 + i,
                        StressIterationsPerSecond = 1000
                    });
                }

                // Trailing 5 OFF samples: temps 76..80 (avg 78), watts 108..112 (avg 110), 1000 iter/s
                // Add transient cooling-on drop + 5 settled ON samples: temps 46..50 (avg 48), watts 33..37 (avg 35), 900 iter/s
                samples.Add(new TelemetrySample
                {
                    CoolingModeActive = true,
                    CpuTemp = 72,
                    CpuPowerWatts = 60,
                    StressIterationsPerSecond = 500
                });
                for (int i = 0; i < 5; i++)
                {
                    samples.Add(new TelemetrySample
                    {
                        CoolingModeActive = true,
                        CpuTemp = 46 + i,
                        CpuPowerWatts = 33 + i,
                        StressIterationsPerSecond = 900
                    });
                }

                var cmpC = Views.StressTestWindow.ComputeCoolingComparison(samples, isFahrenheit: false);
                if (!cmpC.HasOffPhase || !cmpC.HasOnPhase ||
                    Math.Abs(cmpC.OffTemp - 78.0) > 0.01 ||
                    Math.Abs(cmpC.OnTemp - 48.0) > 0.01 ||
                    Math.Abs(cmpC.DeltaTemp - (-30.0)) > 0.01 ||
                    Math.Abs(cmpC.OffIterationsPerSecond - 1000.0) > 0.01 ||
                    Math.Abs(cmpC.OnIterationsPerSecond - 900.0) > 0.01 ||
                    Math.Abs(cmpC.PerformanceRetainedPercent - 90.0) > 0.01 ||
                    Math.Abs(cmpC.DeltaWatts - (-75.0)) > 0.01)
                {
                    throw new Exception(
                        $"Celsius delta mismatch: off={cmpC.OffTemp}C/{cmpC.OffWatts}W/{cmpC.OffIterationsPerSecond}i/s, on={cmpC.OnTemp}C/{cmpC.OnWatts}W/{cmpC.OnIterationsPerSecond}i/s, dT={cmpC.DeltaTemp}, dW={cmpC.DeltaWatts}, perf={cmpC.PerformanceRetainedPercent}%");
                }

                var cmpF = Views.StressTestWindow.ComputeCoolingComparison(samples, isFahrenheit: true);
                if (Math.Abs(cmpF.DeltaTemp - (-54.0)) > 0.01)
                {
                    throw new Exception($"Fahrenheit delta mismatch: expected -54 F, got {cmpF.DeltaTemp}");
                }

                var fps100 = Views.StressTestWindow.ComputeFpsImpact(84.0);
                if (fps100.ProjectedFps != 84 || fps100.LostFps != 16)
                {
                    throw new Exception(
                        $"FPS impact at 100 FPS mismatch: projected={fps100.ProjectedFps}, lost={fps100.LostFps}");
                }

                var fps144 = Views.StressTestWindow.ComputeFpsImpact(84.0, 144);
                if (fps144.ProjectedFps != 121 || fps144.LostFps != 23)
                {
                    throw new Exception(
                        $"FPS impact at 144 FPS mismatch: projected={fps144.ProjectedFps}, lost={fps144.LostFps}");
                }

                if (Views.StressTestWindow.FpsPresets.Length != 6 ||
                    Views.StressTestWindow.NormalizeFpsReference(75) != 100 ||
                    Views.StressTestWindow.NormalizeFpsReference(240) != 240)
                {
                    throw new Exception("FPS preset list or normalization mismatch.");
                }

                var fps240 = Views.StressTestWindow.ComputeFpsImpact(84.0, 240);
                if (fps240.ProjectedFps != 202 || fps240.LostFps != 38)
                {
                    throw new Exception(
                        $"FPS impact at 240 FPS mismatch: projected={fps240.ProjectedFps}, lost={fps240.LostFps}");
                }

                var presetSettings = new AppSettings { StressFpsReference = 165 };
                string presetJson = System.Text.Json.JsonSerializer.Serialize(presetSettings);
                var presetRoundTrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(presetJson);
                if (presetRoundTrip?.StressFpsReference != 165)
                {
                    throw new Exception("Stress FPS preset did not persist through AppSettings JSON.");
                }

                var defaultSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}");
                if (defaultSettings?.StressFpsReference != 100)
                {
                    throw new Exception("Stress FPS preset default is not 100.");
                }

                var fpsCapped = Views.StressTestWindow.ComputeFpsImpact(103.0);
                if (fpsCapped.ProjectedFps != 100 || fpsCapped.LostFps != 0)
                {
                    throw new Exception(
                        $"FPS impact cap mismatch: projected={fpsCapped.ProjectedFps}, lost={fpsCapped.LostFps}");
                }

                var fpsUnknown = Views.StressTestWindow.ComputeFpsImpact(double.NaN);
                if (fpsUnknown.ProjectedFps != 0 || fpsUnknown.LostFps != 0)
                {
                    throw new Exception(
                        $"FPS impact NaN mismatch: projected={fpsUnknown.ProjectedFps}, lost={fpsUnknown.LostFps}");
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

        private static bool TestMemoryBreakdownTelemetry()
        {
            Console.Write("[TEST] Kernel MemoryBreakdown Telemetry... ");
            try
            {
                if (!MemoryTelemetryService.TryReadBreakdown(out var breakdown))
                {
                    throw new Exception("TryReadBreakdown returned false.");
                }

                if (breakdown.TotalUsableBytes == 0 || breakdown.InstalledBytes < breakdown.TotalUsableBytes)
                {
                    throw new Exception(
                        $"Invalid capacity values: installed={breakdown.InstalledBytes}, usable={breakdown.TotalUsableBytes}");
                }

                if (breakdown.HardwareReservedBytes != breakdown.InstalledBytes - breakdown.TotalUsableBytes)
                {
                    throw new Exception("HardwareReservedBytes mismatch.");
                }

                if (breakdown.InUseBytes + breakdown.AvailableBytes != breakdown.TotalUsableBytes)
                {
                    throw new Exception("InUse + Available does not equal TotalUsableBytes.");
                }

                if (StartupTaskService.IsCurrentProcessElevated())
                {
                    if (!breakdown.HasPageListBreakdown || (breakdown.StandbyBytes + breakdown.FreeBytes) == 0)
                    {
                        throw new Exception("Expected live NT page list breakdown when elevated.");
                    }

                    if (breakdown.CachedBytes != breakdown.StandbyBytes + breakdown.ModifiedBytes)
                    {
                        throw new Exception("CachedBytes must equal StandbyBytes + ModifiedBytes.");
                    }
                }

                Console.WriteLine(
                    $"PASSED (Installed={MemoryTelemetryService.FormatBytes(breakdown.InstalledBytes)}, InUse={MemoryTelemetryService.FormatBytes(breakdown.InUseBytes)}, Standby={MemoryTelemetryService.FormatBytes(breakdown.StandbyBytes)}, Free={MemoryTelemetryService.FormatBytes(breakdown.FreeBytes)})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestDynamicByteFormatting()
        {
            Console.Write("[TEST] MemoryTelemetryService Dynamic GB/MB Formatting... ");
            try
            {
                string zero = MemoryTelemetryService.FormatBytes(0);
                string small = MemoryTelemetryService.FormatBytes((ulong)(5.25 * 1024 * 1024));
                string medium = MemoryTelemetryService.FormatBytes((ulong)(412.5 * 1024 * 1024));
                string oneGb = MemoryTelemetryService.FormatBytes(1024UL * 1024UL * 1024UL);
                string sixteenGb = MemoryTelemetryService.FormatBytes(16UL * 1024UL * 1024UL * 1024UL);

                if (zero != "0.0 MB" ||
                    small != "5.25 MB" ||
                    medium != "412.5 MB" ||
                    oneGb != "1.00 GB" ||
                    sixteenGb != "16.00 GB")
                {
                    throw new Exception(
                        $"Formatting mismatch: zero='{zero}', small='{small}', medium='{medium}', oneGb='{oneGb}', sixteenGb='{sixteenGb}'");
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

        private static bool TestCpuIdleGetSystemTimesMath()
        {
            Console.Write("[TEST] GetSystemTimes CPU Idle Guard... ");
            try
            {
                double full = MemoryTelemetryService.ComputeCpuUsagePercent(0, 100, 0, 0, 200, 100);
                double zero = MemoryTelemetryService.ComputeCpuUsagePercent(0, 100, 0, 100, 200, 0);
                double five = MemoryTelemetryService.ComputeCpuUsagePercent(0, 0, 0, 95, 95, 5);
                double same = MemoryTelemetryService.ComputeCpuUsagePercent(10, 20, 30, 10, 20, 30);

                if (Math.Abs(full - 100.0) > 0.01 ||
                    Math.Abs(zero - 0.0) > 0.01 ||
                    Math.Abs(five - 5.0) > 0.01 ||
                    Math.Abs(same - 0.0) > 0.01)
                {
                    throw new Exception($"Math mismatch: full={full}, zero={zero}, five={five}, same={same}");
                }

                double? live = MemoryTelemetryService.MeasureCpuUsageAsync(TimeSpan.FromMilliseconds(120)).GetAwaiter().GetResult();
                if (!live.HasValue || !double.IsFinite(live.Value) || live.Value < 0.0 || live.Value > 100.0)
                {
                    throw new Exception($"Invalid live GetSystemTimes reading: {live}");
                }

                Console.WriteLine($"PASSED (Live CPU={live.Value:0.0}%)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestMemoryOptimizerEngineAndFallback()
        {
            Console.Write("[TEST] MemoryOptimizerService Commands & Elevation Fallback... ");
            var settings = SettingsService.Instance.Current;
            bool origWorking = settings.RamFreeWorkingSets;
            bool origStandby = settings.RamClearStandby;
            bool origClip = settings.RamClearClipboard;
            bool origNotify = settings.RamShowNotification;

            try
            {
                var privFail = MemoryOptimizerService.ExecuteKernelCommands(
                    true,
                    true,
                    _ => (NativeMethods.STATUS_PRIVILEGE_NOT_HELD, -1));
                if (privFail.Succeeded || !privFail.RequiresElevation ||
                    privFail.LastNtStatus != NativeMethods.STATUS_PRIVILEGE_NOT_HELD)
                {
                    throw new Exception("Expected clean RequiresElevation fallback on STATUS_PRIVILEGE_NOT_HELD.");
                }

                var noScope = MemoryOptimizerService.ExecuteKernelCommands(
                    false,
                    false,
                    _ => throw new Exception("No kernel commands should run when scope is empty."));
                if (noScope.Succeeded || noScope.RequiresElevation)
                {
                    throw new Exception("Unexpected status on empty cleaning scope.");
                }

                var sentCmds = new List<int>();
                var mockAll = MemoryOptimizerService.ExecuteKernelCommands(
                    true,
                    true,
                    cmd =>
                    {
                        sentCmds.Add(cmd);
                        return (0, 0);
                    });
                if (!mockAll.Succeeded ||
                    !sentCmds.SequenceEqual(new[]
                    {
                        NativeMethods.MemoryEmptyWorkingSets,
                        NativeMethods.MemoryFlushModifiedList,
                        NativeMethods.MemoryPurgeStandbyList
                    }))
                {
                    throw new Exception("Command sequence mismatch for WorkingSets+Standby.");
                }

                if (StartupTaskService.IsCurrentProcessElevated())
                {
                    settings.RamFreeWorkingSets = true;
                    settings.RamClearStandby = true;
                    settings.RamClearClipboard = false;
                    settings.RamShowNotification = false;

                    var liveResult = MemoryOptimizerService.Instance
                        .OptimizeAsync(MemoryOptimizationTrigger.Manual)
                        .GetAwaiter()
                        .GetResult();
                    if (liveResult == null || !liveResult.Succeeded || liveResult.RequiresElevation)
                    {
                        throw new Exception($"Elevated memory optimization failed: status={liveResult?.LastNtStatus}");
                    }

                    Console.WriteLine(
                        $"PASSED (Fallback OK, Live Freed={MemoryTelemetryService.FormatBytes(liveResult.FreedBytes)})");
                }
                else
                {
                    Console.WriteLine("PASSED (Fallback & command sequence verified; live cleanup requires elevation)");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                settings.RamFreeWorkingSets = origWorking;
                settings.RamClearStandby = origStandby;
                settings.RamClearClipboard = origClip;
                settings.RamShowNotification = origNotify;
            }
        }

        private static bool TestStorageHealthWatch()
        {
            Console.Write("[TEST] Storage Health Watch Transitions, Drops & Cooldown... ");
            var settings = SettingsService.Instance.Current;
            bool originalSetting = settings.StorageHealthAlertsEnabled;
            try
            {
                settings.StorageHealthAlertsEnabled = true;
                var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

                HardwareSnapshot Snapshot(int? health) => new()
                {
                    StorageDrives = new List<DriveTelemetry>
                    {
                        new()
                        {
                            Index = 0,
                            Model = "Test SSD",
                            SmartHealthPercent = health,
                            StatusText = HealthStatus(health),
                            HardwareIdentifier = "storage-test"
                        }
                    }
                };

                var service = new StorageHealthAlertService();
                var alerts = new List<StorageHealthAlert>();
                service.AlertRaised += alerts.Add;

                service.Observe(Snapshot(100), start);
                if (alerts.Count != 0)
                {
                    throw new Exception("Healthy drive raised a storage health alert.");
                }

                service.Observe(Snapshot(65), start.AddMinutes(1));
                if (alerts.Count != 1 ||
                    alerts[0].HealthPercent != 65 ||
                    alerts[0].PreviousPercent != 100 ||
                    alerts[0].StatusText != "Worn")
                {
                    throw new Exception("Drive did not alert when entering the Worn state.");
                }

                service.Observe(Snapshot(64), start.AddMinutes(2));
                if (alerts.Count != 1)
                {
                    throw new Exception("An unchanged warning state raised a duplicate alert.");
                }

                service.Observe(Snapshot(45), start.AddMinutes(12));
                if (alerts.Count != 2 || alerts[1].StatusText != "Replace")
                {
                    throw new Exception("Drive did not alert when entering the Replace state.");
                }

                var dropService = new StorageHealthAlertService();
                var dropAlerts = new List<StorageHealthAlert>();
                dropService.AlertRaised += dropAlerts.Add;
                dropService.Observe(Snapshot(100), start);
                dropService.Observe(Snapshot(89), start.AddMinutes(1));
                if (dropAlerts.Count != 1 ||
                    dropAlerts[0].PreviousPercent != 100 ||
                    dropAlerts[0].HealthPercent != 89)
                {
                    throw new Exception("A meaningful SMART health drop did not alert.");
                }

                dropService.Observe(Snapshot(88), start.AddMinutes(2));
                dropService.Observe(Snapshot(78), start.AddMinutes(3));
                if (dropAlerts.Count != 1)
                {
                    throw new Exception("A drop inside the cooldown raised a duplicate alert.");
                }

                dropService.Observe(Snapshot(67), start.AddMinutes(13));
                if (dropAlerts.Count != 2)
                {
                    throw new Exception("A new meaningful drop after the cooldown did not alert.");
                }

                settings.StorageHealthAlertsEnabled = false;
                var disabledService = new StorageHealthAlertService();
                var disabledAlerts = new List<StorageHealthAlert>();
                disabledService.AlertRaised += disabledAlerts.Add;
                disabledService.Observe(Snapshot(100), start);
                disabledService.Observe(Snapshot(45), start.AddMinutes(1));
                if (disabledAlerts.Count != 0)
                {
                    throw new Exception("Storage health alerts were not disabled by the setting.");
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
                settings.StorageHealthAlertsEnabled = originalSetting;
            }
        }

        private static string HealthStatus(int? percent)
        {
            if (!percent.HasValue) return "Unknown";
            if (percent.Value >= 90) return "Good";
            if (percent.Value >= 70) return "Fair";
            if (percent.Value >= 50) return "Worn";
            return "Replace";
        }

        private static bool TestRamHotkeyAndSettingsPersistence()
        {
            Console.Write("[TEST] RAM Settings Persistence & Dual Hotkey IDs... ");
            try
            {
                var sample = new AppSettings
                {
                    Theme = "System",
                    RamFreeWorkingSets = false,
                    RamClearStandby = true,
                    RamClearClipboard = true,
                    RamShowNotification = false,
                    AutoRamOptimizeEnabled = true,
                    AutoRamThresholdPercent = 30,
                    AutoRamCpuIdleOnly = false,
                    AutoRamIntervalHours = 4,
                    UseRamHotkey = true,
                    RamHotkeyModifiers = "Ctrl+Alt",
                    RamHotkeyKey = "M",
                    ShowFanSpeeds = false,
                    FanHealthAlertsEnabled = false,
                    ShowBatteryStatus = false,
                    ShowRamBadge = true,
                    RamTrayWhiteDigits = true,
                    RamTrayBadgeColor = "#123456",
                    CleanTemp = false,
                    CleanBrowserCache = true,
                    CleanGpuShaderCache = true,
                    CleanPrefetch = true,
                    FanAliases = new Dictionary<string, string>
                    {
                        ["fan:Chassis Fan"] = "Top intake",
                        ["gpu"] = "Aux fan"
                    },
                    StorageHealthAlertsEnabled = false,
                    AutopilotForegroundOnly = true,
                    SaveRollbackOnUpdate = false,
                    CpuTrayBadgeMetric = "Load",
                    GpuTrayBadgeMetric = "Ram"
                };

                string json = System.Text.Json.JsonSerializer.Serialize(sample);
                var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)
                    ?? throw new Exception("Deserialized AppSettings is null.");

                if (roundTrip.RamFreeWorkingSets ||
                    !roundTrip.RamClearStandby ||
                    !roundTrip.RamClearClipboard ||
                    roundTrip.RamShowNotification ||
                    !roundTrip.AutoRamOptimizeEnabled ||
                    roundTrip.AutoRamThresholdPercent != 30 ||
                    roundTrip.AutoRamCpuIdleOnly ||
                    roundTrip.AutoRamIntervalHours != 4 ||
                    !roundTrip.UseRamHotkey ||
                    roundTrip.RamHotkeyModifiers != "Ctrl+Alt" ||
                    roundTrip.RamHotkeyKey != "M" ||
                    roundTrip.ShowFanSpeeds ||
                    roundTrip.FanHealthAlertsEnabled ||
                    roundTrip.ShowBatteryStatus ||
                    !roundTrip.ShowRamBadge ||
                    !roundTrip.RamTrayWhiteDigits ||
                    roundTrip.RamTrayBadgeColor != "#123456" ||
                    roundTrip.FanAliases["fan:Chassis Fan"] != "Top intake" ||
                    roundTrip.FanAliases["gpu"] != "Aux fan" ||
                    roundTrip.StorageHealthAlertsEnabled ||
                    !roundTrip.AutopilotForegroundOnly ||
                    roundTrip.SaveRollbackOnUpdate ||
                    roundTrip.CpuTrayBadgeMetric != "Load" ||
                    roundTrip.GpuTrayBadgeMetric != "Ram" ||
                    roundTrip.CleanTemp ||
                    !roundTrip.CleanBrowserCache ||
                    !roundTrip.CleanGpuShaderCache ||
                    !roundTrip.CleanPrefetch ||
                    roundTrip.Theme != "System")
                {
                    throw new Exception("AppSettings health, tray metric, or Autopilot JSON round-trip mismatch.");
                }

                var defaults = new AppSettings();
                if (defaults.CpuTrayBadgeMetric != "Temperature" ||
                    defaults.GpuTrayBadgeMetric != "Temperature" ||
                    defaults.ShowRamBadge ||
                    defaults.RamTrayWhiteDigits ||
                    defaults.RamTrayBadgeColor != AppSettings.DefaultRamTrayBadgeColor ||
                    !defaults.ShowBatteryStatus ||
                    defaults.FanAliases.Count != 0 ||
                    !defaults.StorageHealthAlertsEnabled ||
                    !defaults.SaveRollbackOnUpdate ||
                    defaults.Theme != "Dark" ||
                    !defaults.ShowCpuLoad ||
                    !defaults.ShowGpuLoad ||
                    !defaults.CleanTemp ||
                    defaults.CleanBrowserCache ||
                    defaults.CleanGpuShaderCache ||
                    defaults.CleanPrefetch ||
                    ThemeService.Normalize("Light") != "Light" ||
                    ThemeService.Normalize("system") != "System" ||
                    ThemeService.Normalize("unsupported") != "Dark")
                {
                    throw new Exception("Unexpected default health, tray metric, rollback, or theme settings.");
                }

                if (GlobalHotkeyService.CoolingHotkeyId == GlobalHotkeyService.RamOptimizeHotkeyId)
                {
                    throw new Exception("Cooling and RAM hotkey IDs must be distinct.");
                }

                string display = GlobalHotkeyService.FormatShortcutDisplay(roundTrip.RamHotkeyModifiers, roundTrip.RamHotkeyKey);
                if (display != "Ctrl + Alt + M")
                {
                    throw new Exception($"Unexpected shortcut format: '{display}'");
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

        private static bool TestRamTooltipHoverLifecycle()
        {
            Console.Write("[TEST] RAM Breakdown Tooltip Hover & Pin Lifecycle... ");
            Exception? caughtEx = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var mw = new Views.MainWindow();
                    mw.ExerciseRamTooltipLifecycleForTest();
                    mw.Close();
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    caughtEx = ex.InnerException ?? ex;
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
                finally
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (caughtEx != null)
            {
                Console.WriteLine($"FAILED: {caughtEx.Message}");
                return false;
            }

            Console.WriteLine("PASSED");
            return true;
        }

        private static bool TestTrayContextMenuDarkRendering()
        {
            Console.Write("[TEST] Tray ContextMenu Dark Owner-Drawn Rendering... ");
            Bitmap? mainBmp = null;
            Bitmap? subBmp = null;
            Bitmap? arMain = null;
            Bitmap? arSub = null;
            try
            {
                using var tray = new TrayIconManager();
                (mainBmp, subBmp) = tray.RenderContextMenusForTest(selectFirstItem: true, overlayChecked: true);

                AssertDarkGutterAndHover(mainBmp, rtl: false, "MainMenu-LTR");
                AssertDarkGutterAndHover(subBmp, rtl: false, "SubMenu-LTR");
                AssertSteelBlueCheckboxSide(mainBmp, rtl: false, "MainMenu-LTR");

                LocalizationService.Instance.Apply("العربية");
                if (!LocalizationService.Instance.IsArabic)
                {
                    throw new Exception("LocalizationService failed to enter Arabic mode.");
                }

                tray.ApplyLanguage();
                (arMain, arSub) = tray.RenderContextMenusForTest(selectFirstItem: true, overlayChecked: true);
                AssertDarkGutterAndHover(arMain, rtl: true, "MainMenu-RTL");
                AssertDarkGutterAndHover(arSub, rtl: true, "SubMenu-RTL");
                AssertSteelBlueCheckboxSide(arMain, rtl: true, "MainMenu-RTL");

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
                mainBmp?.Dispose();
                subBmp?.Dispose();
                arMain?.Dispose();
                arSub?.Dispose();
                try
                {
                    LocalizationService.Instance.Apply("English");
                }
                catch
                {
                }
            }
        }

        private static void AssertSteelBlueCheckboxSide(Bitmap bmp, bool rtl, string label)
        {
            int xStart = rtl ? 8 : Math.Max(1, bmp.Width - 32);
            int xEnd = rtl ? Math.Min(bmp.Width - 2, 32) : bmp.Width - 8;
            for (int x = xStart; x < xEnd; x++)
            {
                for (int y = 4; y < bmp.Height - 4; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.B > 200 && c.G > 140 && c.R < 80)
                    {
                        return;
                    }
                }
            }

            throw new Exception($"{label} checked Click-Through Overlay checkbox lacked Steel Blue (#29B6F6) border pixels on {(rtl ? "left" : "right")} trailing edge.");
        }

        private static void AssertDarkGutterAndHover(Bitmap bmp, bool rtl, string label)
        {
            int xStart = rtl ? Math.Max(1, bmp.Width - 11) : 2;
            int xEnd = rtl ? bmp.Width - 3 : Math.Min(bmp.Width - 2, 10);
            for (int x = xStart; x <= xEnd; x++)
            {
                for (int y = 2; y <= bmp.Height - 3; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 75 || c.G > 75 || c.B > 75)
                    {
                        throw new Exception($"{label} bright/white/blue pixel at ({x},{y}): {c}");
                    }
                }
            }

            int hoverProbeX = rtl ? bmp.Width - 9 : 8;
            Color hover = bmp.GetPixel(hoverProbeX, 10);
            if (hover.R != 45 || hover.G != 45 || hover.B != 45)
            {
                throw new Exception($"{label} hovered pill at ({hoverProbeX},10) was {hover}, expected #2D2D2D (45,45,45).");
            }
        }

        private static bool TestCustomFrameColorsAndSettingsPersistence()
        {
            Console.Write("[TEST] Custom Frame Colors & Wave 5 Settings Persistence... ");
            try
            {
                var customOff = App.ResolveFrameColor("#FF9061", App.DefaultCoolingOffFrameColor);
                if (customOff.R != 0xFF || customOff.G != 0x90 || customOff.B != 0x61)
                {
                    throw new Exception($"Unexpected parsed color: {customOff}");
                }

                var fallbackOn = App.ResolveFrameColor("not-a-color", App.DefaultCoolingOnFrameColor);
                if (App.NormalizeHexColor(fallbackOn) != App.DefaultCoolingOnFrameColor)
                {
                    throw new Exception($"Invalid hex did not fall back to default: {App.NormalizeHexColor(fallbackOn)}");
                }

                var sample = new AppSettings
                {
                    CoolingOnFrameColor = "#2D94EF",
                    CoolingOffFrameColor = "#FF9061",
                    CpuTrayWhiteDigits = true,
                    GpuTrayWhiteDigits = true,
                    ShowCpuClock = false,
                    ShowCpuBaseSpeed = false,
                    ShowCpuGraph = true,
                    CpuGraphSmall = true,
                    CpuGraphTimeframeMinutes = 10,
                    ShowDiskTemp = false,
                    DiskGraphDrives = new List<string> { "drive-0", "drive-1" },
                    DiskGraphTimeframes = new Dictionary<string, int>
                    {
                        ["drive-0"] = 5,
                        ["drive-1"] = 30
                    }
                };

                string json = System.Text.Json.JsonSerializer.Serialize(sample);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)
                    ?? throw new Exception("Deserialized AppSettings is null.");

                if (loaded.CoolingOnFrameColor != "#2D94EF" ||
                    loaded.CoolingOffFrameColor != "#FF9061" ||
                    !loaded.CpuTrayWhiteDigits ||
                    !loaded.GpuTrayWhiteDigits ||
                    loaded.ShowCpuClock ||
                    loaded.ShowCpuBaseSpeed ||
                    !loaded.ShowCpuGraph ||
                    !loaded.CpuGraphSmall ||
                    loaded.CpuGraphTimeframeMinutes != 10 ||
                    loaded.ShowDiskTemp ||
                    loaded.DiskGraphDrives.Count != 2 ||
                    !loaded.DiskGraphTimeframes.TryGetValue("drive-1", out int d1Tf) ||
                    d1Tf != 30)
                {
                    throw new Exception("Wave 5 settings JSON round-trip mismatch.");
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

        private static bool TestTrayBadgeBlackAndWhiteDigits()
        {
            Console.Write("[TEST] Tray Badges Black vs White Digits & Handle Stability... ");
            try
            {
                IntPtr hProcess = Process.GetCurrentProcess().Handle;
                using var tray = new TrayIconManager();

                using (var cpuBlack = tray.RenderSingleBadgeForTest(53, isCpu: true, coolingModeActive: false, whiteDigits: false))
                using (var cpuWhite = tray.RenderSingleBadgeForTest(53, isCpu: true, coolingModeActive: true, whiteDigits: true))
                using (var gpuWhite = tray.RenderSingleBadgeForTest(48, isCpu: false, coolingModeActive: false, whiteDigits: true))
                using (var ramBlack = tray.RenderRamBadgeForTest(58, whiteDigits: false, colorHex: AppSettings.DefaultRamTrayBadgeColor))
                using (var ramWhite = tray.RenderRamBadgeForTest(58, whiteDigits: true, colorHex: AppSettings.DefaultRamTrayBadgeColor))
                using (var mergedMixed = tray.RenderMergedBadgeForTest(53, 48, coolingModeActive: false, cpuWhiteDigits: true, gpuWhiteDigits: false))
                {
                    AssertHasDarkAndBg(cpuBlack, minBgR: 220, expectDarkInk: true, label: "CpuBlackOff");
                    AssertHasWhiteAndBg(cpuWhite, minBgB: 150, label: "CpuWhiteOn");
                    AssertHasWhiteAndBg(gpuWhite, minBgG: 110, label: "GpuWhite");
                    AssertHasDarkAndBg(ramBlack, minBgR: 70, expectDarkInk: true, label: "RamBlack");
                    AssertHasWhiteAndBg(ramWhite, minBgB: 70, label: "RamWhite");

                    Color topCorner = mergedMixed.GetPixel(16, 2);
                    Color botCorner = mergedMixed.GetPixel(16, 29);
                    if (topCorner.R < 170 || botCorner.G < 150)
                    {
                        throw new Exception($"Merged split background mismatch: top={topCorner}, bot={botCorner}");
                    }
                }

                if (TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Temperature, 50, 10, 20, false) != 50 ||
                    TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Temperature, 50, 10, 20, true) != 122 ||
                    TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Load, 50, 10, 20, false) != 10 ||
                    TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Ram, 50, 10, 20, false) != 20 ||
                    TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Load, double.NaN, 0, 20, false) != 0 ||
                    TrayIconManager.ResolveBadgeValue(
                        TrayIconManager.TrayBadgeMetric.Load, double.NaN, double.NaN, 20, false).HasValue ||
                    TrayIconManager.NormalizeBadgeMetric("ram") != TrayIconManager.TrayBadgeMetric.Ram ||
                    TrayIconManager.NormalizeBadgeMetric("bogus") != TrayIconManager.TrayBadgeMetric.Temperature)
                {
                    throw new Exception("Tray badge metric resolution mismatch.");
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                uint gdiBefore = NativeMethods.GetGuiResources(hProcess, 0);
                for (int i = 0; i < 200; i++)
                {
                    using var b = i % 3 == 0
                        ? tray.RenderRamBadgeForTest(40 + (i % 50), whiteDigits: i % 2 == 1,
                            colorHex: AppSettings.DefaultRamTrayBadgeColor)
                        : tray.RenderSingleBadgeForTest(50 + (i % 10), isCpu: (i % 2 == 0),
                            coolingModeActive: (i % 3 == 0), whiteDigits: (i % 2 == 1));
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                uint gdiAfter = NativeMethods.GetGuiResources(hProcess, 0);
                if ((int)(gdiAfter - gdiBefore) > 5)
                {
                    throw new Exception($"GDI leak during tray badge renders: before={gdiBefore}, after={gdiAfter}");
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

        private static void AssertHasDarkAndBg(Bitmap bmp, int minBgR, bool expectDarkInk, string label)
        {
            bool foundDark = false;
            bool foundWhite = false;
            Color bg = bmp.GetPixel(1, 1);
            if (bg.R < minBgR)
            {
                throw new Exception($"{label} background corner too dark: {bg}");
            }

            for (int x = 4; x < bmp.Width - 4; x++)
            {
                for (int y = 4; y < bmp.Height - 4; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R < 65 && c.G < 65 && c.B < 65) foundDark = true;
                    if (c.R > 235 && c.G > 235 && c.B > 235) foundWhite = true;
                }
            }

            if (expectDarkInk && !foundDark)
            {
                throw new Exception($"{label} expected dark digits but found none.");
            }
            if (!expectDarkInk && !foundWhite)
            {
                throw new Exception($"{label} expected white digits but found none.");
            }
        }

        private static void AssertHasWhiteAndBg(Bitmap bmp, int minBgB = 0, int minBgG = 0, string label = "")
        {
            Color bg = bmp.GetPixel(1, 1);
            if (bg.B < minBgB || bg.G < minBgG)
            {
                throw new Exception($"{label} background corner mismatch: {bg}");
            }

            for (int x = 4; x < bmp.Width - 4; x++)
            {
                for (int y = 4; y < bmp.Height - 4; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 235 && c.G > 235 && c.B > 235)
                    {
                        return;
                    }
                }
            }

            throw new Exception($"{label} expected white digits but found none.");
        }

        private static bool TestCpuClockBaseSmallGraphAndDriveSamples()
        {
            Console.Write("[TEST] CPU Base Speed, Small/Large Graph & Drive Sample History... ");
            var s = SettingsService.Instance.Current;
            bool origShowClock = s.ShowCpuClock;
            bool origShowBase = s.ShowCpuBaseSpeed;
            bool origShowGraph = s.ShowCpuGraph;
            bool origSmall = s.CpuGraphSmall;
            bool origShowDisk = s.ShowDiskTemp;

            try
            {
                var snap = new HardwareSnapshot
                {
                    CpuTemp = 54,
                    GpuTemp = 48,
                    CpuGhz = 4.45,
                    CpuBaseGhz = 3.80,
                    CpuMaxGhz = 4.85,
                    StorageDrives = new List<DriveTelemetry>
                    {
                        new() { Index = 0, Model = "NVMe One", HardwareIdentifier = "nvme-0", DriveLetters = "C", Temperature = 42 },
                        new() { Index = 1, Model = "NVMe Two", HardwareIdentifier = "nvme-1", DriveLetters = "E", Temperature = double.NaN }
                    }
                };
                var sample = TelemetrySample.FromSnapshot(snap);
                if (!sample.TryGetDriveTemp("ID:NVME-0", out double d0) || Math.Abs(d0 - 42) > 0.01)
                {
                    throw new Exception($"Expected TryGetDriveTemp('ID:NVME-0')=42, got {d0}");
                }
                if (sample.TryGetDriveTemp("id:nvme-1", out _))
                {
                    throw new Exception("TryGetDriveTemp should return false for NaN drive temp.");
                }

                string withBase = Views.MainWindow.FormatCpuClockReadout(4.45, 3.80, 4.85, showBaseSpeed: true);
                string noBase = Views.MainWindow.FormatCpuClockReadout(4.45, 3.80, 4.85, showBaseSpeed: false);
                if (withBase != "4.45 / 3.80" || noBase != "4.45")
                {
                    throw new Exception($"Unexpected CPU clock format: withBase='{withBase}', noBase='{noBase}'");
                }

                string amdLabel = Views.MainWindow.FormatCpuSmallRowLabel("AMD Ryzen 7 5800X");
                string intelLabel = Views.MainWindow.FormatCpuSmallRowLabel("Intel Core i7-13700K");
                if (amdLabel != "CPU (AMD)" || intelLabel != "CPU (Intel)")
                {
                    throw new Exception($"Unexpected CPU small row labels: '{amdLabel}', '{intelLabel}'");
                }

                Exception? staEx = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        var mw = new Views.MainWindow();

                        s.ShowCpuClock = true;
                        s.ShowCpuGraph = true;
                        s.CpuGraphSmall = false;
                        s.ShowDiskTemp = true;
                        mw.ApplySettings();
                        double largeHeight = mw.Height;

                        s.CpuGraphSmall = true;
                        mw.ApplySettings();
                        double smallHeight = mw.Height;
                        if (smallHeight >= largeHeight)
                        {
                            throw new Exception($"Expected small graph height < large ({smallHeight} >= {largeHeight}).");
                        }

                        s.ShowCpuGraph = false;
                        s.ShowCpuClock = false;
                        s.ShowDiskTemp = false;
                        mw.ApplySettings();
                        if (mw.Height >= smallHeight)
                        {
                            throw new Exception($"Expected collapsed rows height < small ({mw.Height} >= {smallHeight}).");
                        }

                        mw.Close();
                    }
                    catch (Exception ex)
                    {
                        staEx = ex;
                    }
                    finally
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (staEx != null)
                {
                    throw staEx;
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
                s.ShowCpuClock = origShowClock;
                s.ShowCpuBaseSpeed = origShowBase;
                s.ShowCpuGraph = origShowGraph;
                s.CpuGraphSmall = origSmall;
                s.ShowDiskTemp = origShowDisk;
            }
        }

        private static bool TestZeroHardcodedHardwareInfoAndCloneIntegrity()
        {
            Console.Write("[TEST] Zero Hardcoded Hardware & Snapshot Clone Integrity... ");
            try
            {
                string ryzen24 = HardwareMonitorService.CleanCpuName("AMD Ryzen 9 7950X 24-Core Processor");
                if (ryzen24 != "AMD Ryzen 9 7950X")
                {
                    throw new Exception($"CleanCpuName mismatch: '{ryzen24}'");
                }

                string tr96 = HardwareMonitorService.CleanCpuName("AMD Threadripper PRO 7995WX 96-Cores");
                if (!tr96.Contains("7995WX", StringComparison.Ordinal))
                {
                    throw new Exception($"CleanCpuName altered Threadripper model: '{tr96}'");
                }

                string intelName = HardwareMonitorService.CleanCpuName("Intel(R) Core(TM) i9-14900K");
                if (intelName != "Intel(R) Core(TM) i9-14900K")
                {
                    throw new Exception($"CleanCpuName altered Intel model: '{intelName}'");
                }

                if (HardwareMonitorService.ResolveCpuVendor("GenuineIntel", "0000 Engineering Sample") != "Intel" ||
                    HardwareMonitorService.ResolveCpuVendor("AuthenticAMD", "Unknown") != "AMD" ||
                    HardwareMonitorService.ResolveCpuVendor(null, "Snapdragon X Elite - X1E80100") != "Qualcomm" ||
                    HardwareMonitorService.ResolveCpuVendor("", "") != "")
                {
                    throw new Exception("ResolveCpuVendor returned unexpected vendor mapping.");
                }

                if (HardwareMonitorService.ResolveGpuVendor(HardwareType.GpuNvidia, "Device") != "Nvidia" ||
                    HardwareMonitorService.ResolveGpuVendor(HardwareType.GpuAmd, "Device") != "AMD" ||
                    HardwareMonitorService.ResolveGpuVendor(HardwareType.GpuIntel, "Device") != "Intel" ||
                    HardwareMonitorService.ResolveGpuVendor(HardwareType.Motherboard, "Unknown Display") != "")
                {
                    throw new Exception("ResolveGpuVendor returned unexpected vendor mapping.");
                }

                if (Views.MainWindow.FormatCpuSmallRowLabel("Snapdragon X Elite", "Qualcomm") != "CPU (Qualcomm)" ||
                    Views.MainWindow.FormatCpuSmallRowLabel("", "") != "CPU")
                {
                    throw new Exception("FormatCpuSmallRowLabel failed dynamic vendor formatting.");
                }

                if (Views.MainWindow.CompactGpuName("Intel(R) UHD Graphics 770") != "UHD Graphics 770" ||
                    Views.MainWindow.CompactGpuName("NVIDIA GeForce RTX 4080") != "GeForce RTX 4080")
                {
                    throw new Exception("CompactGpuName failed vendor prefix stripping.");
                }

                string gpuSummary = SystemInfoQueryService.SelectGraphicsCardSummary(
                    new[] { "Parsec Virtual Display Adapter", "Intel(R) UHD Graphics 770", "NVIDIA GeForce RTX 4070" },
                    "NVIDIA GeForce RTX 4070");
                if (gpuSummary != "NVIDIA GeForce RTX 4070, Intel(R) UHD Graphics 770")
                {
                    throw new Exception($"SelectGraphicsCardSummary unexpected result: '{gpuSummary}'");
                }

                var rich = new HardwareSnapshot
                {
                    CpuName = "Test CPU",
                    CpuVendor = "Qualcomm",
                    GpuName = "NVIDIA GeForce RTX 4070",
                    GpuVendor = "Nvidia",
                    GpuTemp = 47.0,
                    GpuLoad = 18.5,
                    GpuPowerWatts = 31,
                    GpuHotspotTemp = 59.0,
                    GpuVramTemp = 52.0,
                    GpuFanRpm = 1150,
                    Fans = new List<FanTelemetry>
                    {
                        new() { Name = "CPU Fan", Rpm = 1200 },
                        new() { Name = "Chassis Fan #1", Rpm = 850 },
                        new() { Name = "System Fan #1", Rpm = 0 }
                    }
                };
                var cloned = HardwareMonitorService.CloneSnapshot(rich);
                if (cloned.CpuVendor != rich.CpuVendor ||
                    cloned.GpuName != rich.GpuName ||
                    cloned.GpuVendor != rich.GpuVendor ||
                    Math.Abs(cloned.GpuTemp - rich.GpuTemp) > 0.01 ||
                    Math.Abs(cloned.GpuLoad - rich.GpuLoad) > 0.01 ||
                    cloned.GpuPowerWatts != rich.GpuPowerWatts ||
                    cloned.GpuHotspotTemp != rich.GpuHotspotTemp ||
                    cloned.GpuVramTemp != rich.GpuVramTemp ||
                    cloned.GpuFanRpm != rich.GpuFanRpm ||
                    cloned.Fans.Count != 3 ||
                    cloned.Fans[0].Name != "CPU Fan" ||
                    cloned.Fans[0].Rpm != 1200 ||
                    cloned.Fans[1].Name != "Chassis Fan #1" ||
                    cloned.Fans[1].Rpm != 850 ||
                    cloned.Fans[2].Rpm != 0)
                {
                    throw new Exception("CloneSnapshot dropped CpuVendor, GPU telemetry, or fan telemetry fields.");
                }

                string fanText = SystemInfoQueryService.FormatFanSpeeds(rich.Fans);
                if (!fanText.Contains("CPU Fan: 1200 RPM", StringComparison.Ordinal) ||
                    !fanText.Contains("Chassis Fan #1: 850 RPM", StringComparison.Ordinal))
                {
                    throw new Exception($"FormatFanSpeeds unexpected output: '{fanText}'");
                }

                if (fanText.Contains("System Fan #1", StringComparison.Ordinal))
                {
                    throw new Exception("FormatFanSpeeds listed a stopped fan header.");
                }

                if (SystemInfoQueryService.FormatFanSpeeds(Array.Empty<FanTelemetry>()) != "--" ||
                    SystemInfoQueryService.FormatFanSpeeds(new List<FanTelemetry>
                    {
                        new() { Name = "Idle Fan", Rpm = 0 }
                    }) != "--")
                {
                    throw new Exception("FormatFanSpeeds should return -- when no fan is moving.");
                }

                var before = HardwareMonitorService.Instance.CurrentSnapshot;
                string beforeGpuName = before.GpuName;
                string beforeGpuVendor = before.GpuVendor;
                double beforeGpuTemp = before.GpuTemp;
                string beforeCpuVendor = before.CpuVendor;
                HardwareMonitorService.Instance.RefreshMemorySnapshotNow();
                var after = HardwareMonitorService.Instance.CurrentSnapshot;
                if (after.CpuVendor != beforeCpuVendor ||
                    after.GpuName != beforeGpuName ||
                    after.GpuVendor != beforeGpuVendor ||
                    (!double.IsNaN(beforeGpuTemp) && Math.Abs(after.GpuTemp - beforeGpuTemp) > 0.01))
                {
                    throw new Exception("RefreshMemorySnapshotNow dropped CPU vendor or GPU telemetry.");
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

        private static bool TestWpfContextMenusDarkRendering()
        {
            Console.Write("[TEST] WPF Tools & RAM ContextMenu Dark Rendering (No White Gutter)... ");
            string origLang = SettingsService.Instance.Current.Language;
            Exception? staEx = null;

            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        foreach (string lang in new[] { "English", "العربية" })
                        {
                            LocalizationService.Instance.Apply(lang);
                            var mw = new Views.MainWindow();
                            try
                            {
                                var toolsMenu = (System.Windows.Controls.ContextMenu)mw.FindName("ToolsContextMenu")
                                    ?? throw new Exception("ToolsContextMenu not found.");
                                var ramMenu = (System.Windows.Controls.ContextMenu)mw.FindName("RamScopeMenu")
                                    ?? throw new Exception("RamScopeMenu not found.");

                                var sepStyle = (Style)mw.FindResource("DarkMenuSeparatorStyle");
                                int itemCount = 0;
                                int sepCount = 0;
                                foreach (object entry in toolsMenu.Items)
                                {
                                    if (entry is System.Windows.Controls.MenuItem mi)
                                    {
                                        if (mi.IsCheckable)
                                        {
                                            throw new Exception("ToolsContextMenu item unexpectedly checkable.");
                                        }
                                        itemCount++;
                                    }
                                    else if (entry is System.Windows.Controls.Separator sep)
                                    {
                                        if (!ReferenceEquals(sep.Style, sepStyle))
                                        {
                                            throw new Exception("ToolsContextMenu separator missing DarkMenuSeparatorStyle.");
                                        }
                                        sepCount++;
                                    }
                                }

                                if (itemCount != 6 || sepCount != 1 || ramMenu.Items.Count != 5)
                                {
                                    throw new Exception($"Unexpected menu structure: toolsItems={itemCount}, toolsSep={sepCount}, ramItems={ramMenu.Items.Count}");
                                }

                                var ramRowStyle = (Style)mw.FindResource("RamScopeMenuItemStyle");
                                var ramHeaderStyle = (Style)mw.FindResource("RamScopeGroupHeaderStyle");
                                if (ramMenu.Items[0] is not System.Windows.Controls.MenuItem scopeHeader ||
                                    scopeHeader.IsEnabled ||
                                    !ReferenceEquals(scopeHeader.Style, ramHeaderStyle) ||
                                    scopeHeader.Header is null ||
                                    string.IsNullOrWhiteSpace(scopeHeader.Header.ToString()) ||
                                    ramMenu.Items[1] is not System.Windows.Controls.MenuItem workingSetsItem ||
                                    !workingSetsItem.IsCheckable ||
                                    !ReferenceEquals(workingSetsItem.Style, ramRowStyle) ||
                                    ramMenu.Items[2] is not System.Windows.Controls.MenuItem standbyItem ||
                                    !standbyItem.IsCheckable ||
                                    !ReferenceEquals(standbyItem.Style, ramRowStyle) ||
                                    ramMenu.Items[3] is not System.Windows.Controls.Separator ramSeparator ||
                                    !ReferenceEquals(ramSeparator.Style, sepStyle) ||
                                    ramMenu.Items[4] is not System.Windows.Controls.MenuItem cleanupItem ||
                                    cleanupItem.IsCheckable ||
                                    !ReferenceEquals(cleanupItem.Style, ramRowStyle) ||
                                    cleanupItem.Header is not System.Windows.Controls.Grid)
                                {
                                    throw new Exception("RAM scope menu rows, group header, or cleanup entry are missing or malformed.");
                                }

                                var (toolsBmp, ramBmp) = mw.RenderWpfContextMenusForTest();
                                using (toolsBmp)
                                using (ramBmp)
                                {
                                    AssertDarkWpfContextMenuBitmap(toolsBmp, $"{lang}-ToolsContextMenu");
                                    AssertDarkWpfContextMenuBitmap(ramBmp, $"{lang}-RamScopeMenu");
                                }
                            }
                            finally
                            {
                                mw.Close();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        staEx = ex;
                    }
                    finally
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (staEx != null)
                {
                    throw staEx;
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
                LocalizationService.Instance.Apply(origLang);
            }
        }

        private static void AssertDarkWpfContextMenuBitmap(Bitmap bmp, string label)
        {
            if (bmp.Width < 120 || bmp.Height < 60)
            {
                throw new Exception($"{label} unexpected dimensions: {bmp.Width}x{bmp.Height}");
            }

            Color interior = bmp.GetPixel(6, 6);
            if (interior.R < 20 || interior.R > 42 || interior.G < 20 || interior.G > 42 || interior.B < 20 || interior.B > 42)
            {
                throw new Exception($"{label} expected dark #1E1E1E surface near (6,6), got {interior}");
            }

            int leftMaxX = Math.Min(10, bmp.Width - 1);
            int rightMinX = Math.Max(0, bmp.Width - 11);
            for (int y = 3; y < bmp.Height - 3; y++)
            {
                for (int x = 3; x <= leftMaxX; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 180 && c.G > 180 && c.B > 180)
                    {
                        throw new Exception($"{label} detected light/white pixel in left edge at ({x},{y}): {c}");
                    }
                }

                for (int x = rightMinX; x <= bmp.Width - 4; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 180 && c.G > 180 && c.B > 180)
                    {
                        throw new Exception($"{label} detected light/white pixel in right edge at ({x},{y}): {c}");
                    }
                }
            }
        }

        private static bool TestSystemMemoryListMultiVersionParsing()
        {
            Console.Write("[TEST] NT MemoryList Multi-Version Buffer Parsing (Win10/Win11)... ");
            try
            {
                foreach (int structBytes in new[] { 168, 176, 208 })
                {
                    byte[] raw = new byte[structBytes];
                    BitConverter.TryWriteBytes(raw.AsSpan(0 * 8, 8), 100UL);  // ZeroPageCount
                    BitConverter.TryWriteBytes(raw.AsSpan(1 * 8, 8), 250UL);  // FreePageCount
                    BitConverter.TryWriteBytes(raw.AsSpan(2 * 8, 8), 40UL);   // ModifiedPageCount
                    BitConverter.TryWriteBytes(raw.AsSpan(3 * 8, 8), 10UL);   // ModifiedNoWritePageCount
                    for (int p = 0; p < 8; p++)
                    {
                        BitConverter.TryWriteBytes(raw.AsSpan((5 + p) * 8, 8), (ulong)(p + 1) * 10UL);
                    }

                    if (!MemoryTelemetryService.ParseSystemMemoryListBuffer(raw, 8, out ulong free, out ulong mod, out ulong standby))
                    {
                        throw new Exception($"Failed to parse {structBytes}-byte buffer.");
                    }

                    if (free != 350UL || mod != 50UL || standby != 360UL)
                    {
                        throw new Exception($"Unexpected parsed counts for {structBytes}B: free={free}, mod={mod}, standby={standby}");
                    }
                }

                if (MemoryTelemetryService.ParseSystemMemoryListBuffer(new byte[64], 8, out _, out _, out _))
                {
                    throw new Exception("Truncated buffer should have been rejected.");
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

        private static bool TestPowerCfgLocalizedParsing()
        {
            Console.Write("[TEST] Locale-Independent powercfg /list Parsing... ");
            try
            {
                const string multiLocaleOutput = """
                Bestehende Energieschemas (* Aktiv)
                -----------------------------------
                Energieschema-GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert) *
                GUID du mode de gestion de l'alimentation : 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Performances élevées)
                Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)
                """;

                var parsed = PowerPlanService.ParsePowerCfgOutput(multiLocaleOutput);
                if (parsed.Count != 3 ||
                    parsed[0].Guid != NativeMethods.GUID_POWERSCHEME_BALANCED ||
                    !parsed[0].IsActive ||
                    parsed[0].Name != "Ausbalanciert" ||
                    parsed[1].Guid != NativeMethods.GUID_POWERSCHEME_HIGH_PERF ||
                    parsed[1].IsActive ||
                    parsed[2].Guid != NativeMethods.GUID_POWERSCHEME_POWER_SAVER)
                {
                    throw new Exception($"Unexpected parsed schemes count={parsed.Count}");
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

        private static bool TestBatteryUnknownStatusParsing()
        {
            Console.Write("[TEST] Battery Telemetry Unknown/Desktop/Charging Parsing... ");
            try
            {
                var unknown = BatteryTelemetryService.ParsePowerStatus(acLineStatus: 1, batteryFlag: 255, batteryLifePercent: 255);
                if (unknown.HasBattery || unknown.Percent != 0)
                {
                    throw new Exception("Unknown battery status (255) must not report HasBattery=true or 100%.");
                }

                var unknownPct = BatteryTelemetryService.ParsePowerStatus(acLineStatus: 1, batteryFlag: 1, batteryLifePercent: 255);
                if (unknownPct.HasBattery)
                {
                    throw new Exception("Unknown battery percent (255) must not report HasBattery=true.");
                }

                var desktop = BatteryTelemetryService.ParsePowerStatus(acLineStatus: 1, batteryFlag: 128, batteryLifePercent: 255);
                if (desktop.HasBattery || !desktop.IsOnAc)
                {
                    throw new Exception("Desktop NoSystemBattery parsed incorrectly.");
                }

                var charging = BatteryTelemetryService.ParsePowerStatus(acLineStatus: 1, batteryFlag: 9, batteryLifePercent: 74);
                if (!charging.HasBattery || charging.Percent != 74 || !charging.IsCharging || !charging.IsOnAc)
                {
                    throw new Exception("Charging laptop status parsed incorrectly.");
                }

                var battery = BatteryTelemetryService.ParsePowerStatus(acLineStatus: 0, batteryFlag: 2, batteryLifePercent: 42);
                if (!battery.HasBattery || battery.Percent != 42 || battery.IsCharging || battery.IsOnAc)
                {
                    throw new Exception("On-battery status parsed incorrectly.");
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

        private static bool TestPerGpuTelemetryHistoryAndCsv()
        {
            Console.Write("[TEST] Per-GPU Telemetry History & Expanded CSV Export... ");
            string tempCsv = Path.Combine(Path.GetTempPath(), $"cryotray_pergpu_{Guid.NewGuid():N}.csv");
            try
            {
                var rb = new TelemetryRingBuffer(16);
                var snap1 = new HardwareSnapshot
                {
                    GpuIdentifier = "/gpu-nvidia/0",
                    GpuTemp = 62.0,
                    GpuHotspotTemp = 76.5,
                    GpuVramTemp = 70.0,
                    GpuFanRpm = 1450,
                    GpuLoad = 55.0,
                    GpuPowerWatts = 140,
                    CoolingModeActive = true,
                    Gpus = new List<GpuTelemetry>
                    {
                        new() { Identifier = "/gpu-nvidia/0", Name = "RTX 4070", Temperature = 62.0, Load = 55.0, PowerWatts = 140 },
                        new() { Identifier = "/gpu-amd/0", Name = "Radeon Graphics", Temperature = 38.0, Load = 5.0, PowerWatts = 12 }
                    }
                };
                var snap2 = new HardwareSnapshot
                {
                    GpuIdentifier = "/gpu-amd/0",
                    GpuTemp = 40.0,
                    GpuHotspotTemp = null,
                    GpuVramTemp = null,
                    GpuFanRpm = null,
                    GpuLoad = 12.0,
                    GpuPowerWatts = 15,
                    CoolingModeActive = true,
                    Gpus = new List<GpuTelemetry>
                    {
                        new() { Identifier = "/gpu-nvidia/0", Name = "RTX 4070", Temperature = 66.0, Load = 70.0, PowerWatts = 160 },
                        new() { Identifier = "/gpu-amd/0", Name = "Radeon Graphics", Temperature = 40.0, Load = 12.0, PowerWatts = 15 }
                    }
                };

                var s1 = TelemetrySample.FromSnapshot(snap1);
                var s2 = TelemetrySample.FromSnapshot(snap2);
                rb.AddSample(s1);
                rb.AddSample(s2);

                var nvStats = rb.GetGpuStats(coolingOnly: true, gpuIdentifier: "/gpu-nvidia/0");
                var igpuStats = rb.GetGpuStats(coolingOnly: true, gpuIdentifier: "/gpu-amd/0");
                if (nvStats.MinGpu != 62.0 || nvStats.MaxGpu != 66.0 ||
                    igpuStats.MinGpu != 38.0 || igpuStats.MaxGpu != 40.0)
                {
                    throw new Exception($"Per-GPU stats mixed GPUs: nv=({nvStats.MinGpu},{nvStats.MaxGpu}), igpu=({igpuStats.MinGpu},{igpuStats.MaxGpu})");
                }

                if (!s2.TryGetGpuLoad("/gpu-nvidia/0", out double nvLoad) || nvLoad != 70.0 ||
                    !s2.TryGetGpuPower("/gpu-nvidia/0", out double nvPower) || nvPower != 160.0 ||
                    !s2.TryGetGpuLoad("/gpu-amd/0", out double amdLoad) || amdLoad != 12.0 ||
                    !s2.TryGetGpuPower("/gpu-amd/0", out double amdPower) || amdPower != 15.0)
                {
                    throw new Exception("Per-GPU load/power history mixed GPUs or lost values.");
                }

                TelemetryCsvService.ExportSamples(new[] { s1, s2 }, tempCsv);
                string[] lines = File.ReadAllLines(tempCsv);
                if (lines.Length != 3 ||
                    !lines[0].Contains("GpuHotspotC,GpuVramTempC,GpuFanRpm", StringComparison.Ordinal) ||
                    !lines[1].Contains(",76.5,70.0,1450,", StringComparison.Ordinal) ||
                    string.Join("\n", lines).Contains("NaN", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Expanded CSV export format mismatch.");
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
                try { if (File.Exists(tempCsv)) File.Delete(tempCsv); } catch { }
            }
        }

        private static bool TestGpuHotspotAndVramAlertsAndThreadSafety()
        {
            Console.Write("[TEST] GPU Hotspot/VRAM Alerts & Concurrent Thread Safety... ");
            var settings = SettingsService.Instance.Current;
            bool originalMute = settings.MuteTemperatureAlerts;
            int originalGpu = settings.HighTempThresholdGpu;
            try
            {
                settings.MuteTemperatureAlerts = false;
                settings.HighTempThresholdGpu = 80;

                var service = new TemperatureAlertService();
                var raised = new List<TemperatureAlert>();
                object listLock = new();
                service.AlertRaised += alert => { lock (listLock) raised.Add(alert); };

                // 80 + 15 = 95 C threshold for hotspot & VRAM
                service.Observe(new HardwareSnapshot { GpuTemp = 72, GpuHotspotTemp = 92, GpuVramTemp = 90 });
                if (raised.Count != 0)
                {
                    throw new Exception("Should not alert below hotspot/VRAM threshold.");
                }

                service.Observe(new HardwareSnapshot { GpuTemp = 78, GpuHotspotTemp = 99, GpuVramTemp = 97 });
                if (!raised.Any(a => a.Component == TemperatureAlertComponent.GpuHotspot && a.ThresholdCelsius == 95) ||
                    !raised.Any(a => a.Component == TemperatureAlertComponent.GpuVram && a.ThresholdCelsius == 95))
                {
                    throw new Exception($"Expected GpuHotspot and GpuVram alerts, got {raised.Count}.");
                }

                // Stress concurrent Observe calls from multiple threads.
                var concurrentService = new TemperatureAlertService();
                Parallel.For(0, 64, i =>
                {
                    concurrentService.Observe(new HardwareSnapshot
                    {
                        MaxCoreTemp = 70 + (i % 30),
                        PeakCpuTemp = 70 + (i % 30),
                        GpuTemp = 65 + (i % 25),
                        GpuHotspotTemp = 80 + (i % 25),
                        GpuVramTemp = 80 + (i % 25)
                    });
                });

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
                settings.HighTempThresholdGpu = originalGpu;
            }
        }

        private static bool TestStorageHealthCumulativeDropTracking()
        {
            Console.Write("[TEST] Storage Health Watch Cumulative Wear Drop... ");
            var settings = SettingsService.Instance.Current;
            bool originalEnabled = settings.StorageHealthAlertsEnabled;
            try
            {
                settings.StorageHealthAlertsEnabled = true;
                var start = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
                var service = new StorageHealthAlertService();
                var alerts = new List<StorageHealthAlert>();
                service.AlertRaised += alerts.Add;

                HardwareSnapshot Snap(int health) => new()
                {
                    StorageDrives = new List<DriveTelemetry>
                    {
                        new()
                        {
                            Index = 0,
                            Model = "Gradual Wear NVMe",
                            SmartHealthPercent = health,
                            StatusText = health >= 90 ? "Good" : "Fair",
                            HardwareIdentifier = "storage-gradual"
                        }
                    }
                };

                service.Observe(Snap(100), start);
                service.Observe(Snap(97), start.AddMinutes(1));
                service.Observe(Snap(94), start.AddMinutes(2));
                service.Observe(Snap(91), start.AddMinutes(3));
                if (alerts.Count != 0)
                {
                    throw new Exception("Should not alert before cumulative 10-point drop.");
                }

                service.Observe(Snap(88), start.AddMinutes(4));
                if (alerts.Count != 1 || alerts[0].PreviousPercent != 100 || alerts[0].HealthPercent != 88)
                {
                    throw new Exception("Cumulative 12-point drop did not trigger storage health alert.");
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
                settings.StorageHealthAlertsEnabled = originalEnabled;
            }
        }

        private static bool TestGlobalHotkeyInvalidKeySafety()
        {
            Console.Write("[TEST] GlobalHotkeyService Invalid Key Safety (No Ctrl+C Hijack)... ");
            HwndSource? source = null;
            var hotkeyService = new GlobalHotkeyService();
            try
            {
                var p = new HwndSourceParameters("CryoTrayInvalidHotkeyTest") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) };
                source = new HwndSource(p);

                if (hotkeyService.Register(source.Handle, "Ctrl", ""))
                {
                    throw new Exception("Empty key must not register a hotkey.");
                }

                if (hotkeyService.Register(source.Handle, "Ctrl", "NotARealKeyName"))
                {
                    throw new Exception("Unknown key name must not fall back to Ctrl+C.");
                }

                if (GlobalHotkeyService.FormatShortcutDisplay("Ctrl", "") != "Ctrl")
                {
                    throw new Exception("Empty key must not format as 'C'.");
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
                hotkeyService.Unregister();
                source?.Dispose();
            }
        }

        private static bool TestWindowPlacementMultiMonitorAndOffScreen()
        {
            Console.Write("[TEST] Multi-Monitor & Off-Screen WindowPlacementHelper... ");
            try
            {
                var leftMonitor = new Rect(-1920, 0, 1920, 1040);
                var primaryMonitor = new Rect(0, 0, 1920, 1040);
                var screens = new[] { leftMonitor, primaryMonitor };

                // Window saved on left-of-primary negative-coordinate monitor stays there.
                var target = WindowPlacementHelper.FindTargetWorkArea(
                    screens, new Rect(-500, 200, 444, 673), primaryMonitor);
                if (target != leftMonitor)
                {
                    throw new Exception($"Expected left monitor, got {target}");
                }

                // Side-by-side docking on left monitor must dock left when room exists, never jump to right because Left < 0.
                var (dockLeft, dockTop) = WindowPlacementHelper.ComputeSideBySidePosition(
                    leftMonitor, new Rect(-500, 300, 444, 673), 460, 520, gap: 6);
                if (dockLeft != -966 || dockTop < leftMonitor.Top + 6 || dockTop + 520 > leftMonitor.Bottom - 6)
                {
                    throw new Exception($"Unexpected left-monitor dock: ({dockLeft}, {dockTop})");
                }

                // Right-edge clamping when anchor is near right edge and child is wider than remaining space.
                var (clampedLeft, clampedTop) = WindowPlacementHelper.ComputeSideBySidePosition(
                    primaryMonitor, new Rect(10, 10, 444, 673), 500, 900, gap: 6);
                if (clampedLeft < primaryMonitor.Left + 6 || clampedLeft + 500 > primaryMonitor.Right - 6 ||
                    clampedTop < primaryMonitor.Top + 6 || clampedTop + 900 > primaryMonitor.Bottom - 6)
                {
                    throw new Exception($"Side-by-side dock escaped work area: ({clampedLeft}, {clampedTop})");
                }

                // Disconnected monitor recovery rescues off-screen window onto primary monitor.
                var rescuedArea = WindowPlacementHelper.FindTargetWorkArea(
                    screens, new Rect(3000, 500, 444, 673), primaryMonitor);
                if (rescuedArea != primaryMonitor)
                {
                    throw new Exception("Off-screen saved position did not fall back to primary monitor.");
                }

                var (resLeft, resTop) = WindowPlacementHelper.ClampToWorkArea(rescuedArea, 3000, 500, 444, 673, 8);
                if (resLeft < primaryMonitor.Left + 8 || resLeft + 444 > primaryMonitor.Right - 8)
                {
                    throw new Exception($"Rescued coordinates out of bounds: ({resLeft}, {resTop})");
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

        private static bool TestSettingsAtomicSaveAndCorruptImport()
        {
            Console.Write("[TEST] SettingsService Atomic Save & Corrupt Import Handling... ");
            string tempDir = Path.Combine(Path.GetTempPath(), "cryotray-settings-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string target = Path.Combine(tempDir, "settings.json");
                var sample = new AppSettings
                {
                    Language = "العربية",
                    HighTempThresholdCpu = 91,
                    SaveRollbackOnUpdate = false
                };

                SettingsService.WriteAtomicJson(target, sample);
                if (!File.Exists(target) || Directory.GetFiles(tempDir, "*.tmp").Length != 0)
                {
                    throw new Exception("Atomic save left temporary files or failed to write target.");
                }

                var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(target));
                if (roundTrip == null || roundTrip.Language != "العربية" || roundTrip.HighTempThresholdCpu != 91 || roundTrip.SaveRollbackOnUpdate)
                {
                    throw new Exception("Atomic save round-trip mismatch.");
                }

                string corruptPath = Path.Combine(tempDir, "corrupt.json");
                File.WriteAllText(corruptPath, "null");
                bool rejectedNull = false;
                try
                {
                    SettingsService.Instance.Import(corruptPath);
                }
                catch (InvalidDataException)
                {
                    rejectedNull = true;
                }

                if (!rejectedNull)
                {
                    throw new Exception("Import did not reject JSON literal 'null'.");
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
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static bool TestStorageCleanupBroadRootGuard()
        {
            Console.Write("[TEST] Storage cleanup broad-root guard & redirected TEMP... ");
            string safeRoot = Path.Combine(
                Path.GetTempPath(),
                "cryotray-cleanup-root-guard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(safeRoot);

                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string? usersRoot = string.IsNullOrWhiteSpace(profile)
                    ? null
                    : Path.GetDirectoryName(profile);
                var rejected = new List<string>();
                void AddRejected(string? path)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        rejected.Add(path);
                    }
                }

                AddRejected(Path.GetPathRoot(Environment.SystemDirectory));
                AddRejected(windows);
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.System));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
                AddRejected(profile);
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
                AddRejected(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
                AddRejected(usersRoot);
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    AddRejected(Path.Combine(profile, "Downloads"));
                }
                if (!string.IsNullOrWhiteSpace(usersRoot))
                {
                    AddRejected(Path.Combine(usersRoot, "AnotherProfile"));
                }

                foreach (string path in rejected)
                {
                    if (StorageCleanupService.IsSafeCleanupRoot(path))
                    {
                        throw new Exception($"Broad cleanup root was accepted: {path}");
                    }
                }

                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!StorageCleanupService.IsSafeCleanupRoot(Path.GetTempPath()) ||
                    !StorageCleanupService.IsSafeCleanupRoot(safeRoot) ||
                    !StorageCleanupService.IsSafeCleanupRoot(Path.Combine(windows, "Temp")) ||
                    !StorageCleanupService.IsSafeCleanupRoot(Path.Combine(windows, "Prefetch")) ||
                    (!string.IsNullOrWhiteSpace(local) &&
                     !StorageCleanupService.IsSafeCleanupRoot(
                         Path.Combine(local, "D3DSCache"))) ||
                    (!string.IsNullOrWhiteSpace(local) &&
                     !StorageCleanupService.IsSafeCleanupRoot(
                         Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data"))) ||
                    (!string.IsNullOrWhiteSpace(programData) &&
                     !StorageCleanupService.IsSafeCleanupRoot(
                         Path.Combine(programData, "NVIDIA Corporation", "NV_Cache"))))
                {
                    throw new Exception("A normal cleanup target or sandbox root was rejected.");
                }

                string? oldTmp = Environment.GetEnvironmentVariable("TMP");
                string? oldTemp = Environment.GetEnvironmentVariable("TEMP");
                try
                {
                    Environment.SetEnvironmentVariable("TMP", windows);
                    Environment.SetEnvironmentVariable("TEMP", windows);
                    if (!string.Equals(
                            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                            Path.TrimEndingDirectorySeparator(Path.GetFullPath(windows)),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("The redirected TEMP path was not applied by the test environment.");
                    }

                    IReadOnlyList<string> redirectedRoots = StorageCleanupService.GetTempRootsForTest();
                    foreach (string root in redirectedRoots)
                    {
                        if (string.Equals(
                                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                                Path.TrimEndingDirectorySeparator(Path.GetFullPath(windows)),
                                StringComparison.OrdinalIgnoreCase) ||
                            !StorageCleanupService.IsSafeCleanupRoot(root))
                        {
                            throw new Exception($"Redirected TEMP produced an unsafe target root: {root}");
                        }
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("TMP", oldTmp);
                    Environment.SetEnvironmentVariable("TEMP", oldTemp);
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
                try
                {
                    if (Directory.Exists(safeRoot))
                    {
                        Directory.Delete(safeRoot, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool TestStorageCleanupGuardConfirmation()
        {
            Console.Write("[TEST] Storage cleanup confirmation guard... ");
            var window = new Views.StorageCleanupWindow();
            try
            {
                var optIn = new[]
                {
                    StorageCleanupCategory.BrowserCache,
                    StorageCleanupCategory.GpuShaderCache,
                    StorageCleanupCategory.Prefetch
                };

                foreach (StorageCleanupCategory category in optIn)
                {
                    if (!window.ExerciseCleanGuardForTest(new[] { category }) ||
                        !window.IsConfirmationVisibleForTest)
                    {
                        throw new Exception($"Confirmation did not open for {category}.");
                    }

                    window.HideConfirmationForTest();
                }

                if (window.ExerciseCleanGuardForTest(
                        new[] { StorageCleanupCategory.TempFiles }) ||
                    window.IsConfirmationVisibleForTest)
                {
                    throw new Exception("Temp-only cleanup opened a confirmation.");
                }

                if (!window.ExerciseCleanGuardForTest(
                        new[]
                        {
                            StorageCleanupCategory.TempFiles,
                            StorageCleanupCategory.BrowserCache
                        }))
                {
                    throw new Exception(
                        "Mixed temp and browser cleanup did not require confirmation.");
                }

                window.HideConfirmationForTest();
                if (!window.ExerciseCleanGuardForTest(optIn))
                {
                    throw new Exception(
                        "All opt-in categories did not require confirmation.");
                }

                Console.WriteLine("PASSED (3 opt-in categories confirmed; temp-only direct)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                window.Close();
            }
        }

        private static bool TestStorageCleanupCancelKeepsWindowOpen()
        {
            Console.Write("[TEST] Storage cleanup cancel keeps window open... ");
            var window = new Views.StorageCleanupWindow();
            try
            {
                foreach (bool cleaning in new[] { false, true })
                {
                    using var cts = new CancellationTokenSource();
                    int promptCalls = 0;
                    window.ClosePromptForTest = _ =>
                    {
                        promptCalls++;
                        return false;
                    };

                    bool closed = window.ExerciseCancelWhileBusyForTest(cleaning, cts);
                    window.ClosePromptForTest = null;
                    if (closed || window.IsClosedForTest)
                    {
                        throw new Exception("Cancel closed the window.");
                    }

                    if (!cts.IsCancellationRequested)
                    {
                        throw new Exception("Cancel did not cancel active work.");
                    }

                    if (promptCalls != 0)
                    {
                        throw new Exception("Cancel showed a close prompt.");
                    }
                }

                Console.WriteLine("PASSED (scan and cleanup cancellation kept window open)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                window.ClosePromptForTest = null;
                window.Close();
            }
        }

        private static bool TestStorageCleanupClosePromptsWhileBusy()
        {
            Console.Write("[TEST] Storage cleanup close prompt while busy... ");
            var window = new Views.StorageCleanupWindow();
            try
            {
                foreach (bool cleaning in new[] { false, true })
                {
                    using var cts = new CancellationTokenSource();
                    int promptCalls = 0;
                    string? promptMessage = null;
                    bool cancel = window.ExerciseCloseWhileBusyForTest(
                        cleaning,
                        cts,
                        message =>
                        {
                            promptCalls++;
                            promptMessage = message;
                            return false;
                        });
                    string expected = LocalizationService.Get(
                        cleaning
                            ? "StorageCleanup_CloseCleanBody"
                            : "StorageCleanup_CloseScanBody");

                    if (!cancel)
                    {
                        throw new Exception("Close was not canceled while work was active.");
                    }

                    if (promptCalls != 1)
                    {
                        throw new Exception($"Expected one close prompt, got {promptCalls}.");
                    }

                    if (!string.Equals(promptMessage, expected, StringComparison.Ordinal))
                    {
                        throw new Exception("Close prompt used the wrong message.");
                    }

                    if (cts.IsCancellationRequested || window.IsClosedForTest)
                    {
                        throw new Exception("Work stopped before the user confirmed close.");
                    }
                }

                Console.WriteLine("PASSED (scan and cleanup prompted before close)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
            finally
            {
                window.ClosePromptForTest = null;
                window.Close();
            }
        }

        private static bool TestStorageCleanupSafety()
        {
            Console.Write("[TEST] Storage cleanup containment, reparse & accounting... ");
            string root = Path.Combine(
                Path.GetTempPath(),
                "cryotray-cleanup-test-" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(
                Path.GetTempPath(),
                "cryotray-cleanup-outside-" + Guid.NewGuid().ToString("N"));
            string link = Path.Combine(root, "junction");
            bool linkCreated = false;
            try
            {
                Directory.CreateDirectory(root);
                string nested = Path.Combine(root, "nested");
                Directory.CreateDirectory(nested);
                string first = Path.Combine(root, "first.tmp");
                string second = Path.Combine(nested, "second.tmp");
                string locked = Path.Combine(root, "locked.tmp");
                File.WriteAllBytes(first, new byte[111]);
                File.WriteAllBytes(second, new byte[222]);
                File.WriteAllBytes(locked, new byte[10]);

                if (!StorageCleanupService.IsPathWithinRoot(first, root) ||
                    StorageCleanupService.IsPathWithinRoot(
                        Path.Combine(root, "..", "escape.tmp"),
                        root) ||
                    !StorageCleanupService.ContainsPrivateBrowserSegment(
                        Path.Combine(root, "Profile", "Local Storage", "cache")) ||
                    StorageCleanupService.ContainsPrivateBrowserSegment(
                        Path.Combine(root, "Profile", "Cache", "Cache_Data")))
                {
                    throw new Exception("Path containment or private browser-segment guard failed.");
                }

                if (!StorageCleanupService.IsPrefetchFile(Path.Combine(root, "boot.pf")) ||
                    !StorageCleanupService.IsPrefetchFile(Path.Combine(root, "boot.db")) ||
                    StorageCleanupService.IsPrefetchFile(Path.Combine(root, "boot.txt")))
                {
                    throw new Exception("Prefetch extension filter failed.");
                }

                try
                {
                    Directory.CreateDirectory(outside);
                    File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
                    Directory.CreateSymbolicLink(link, outside);
                    linkCreated = true;
                }
                catch (Exception ex) when (
                    ex is IOException ||
                    ex is UnauthorizedAccessException ||
                    ex is PlatformNotSupportedException)
                {
                }

                StorageCleanupTestResult result;
                using (var lockStream = new FileStream(
                    locked,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    result = StorageCleanupService.CleanRootsForTest(
                        new[] { root },
                        recursive: true,
                        prefetchOnly: false);
                }

                if (result.BytesFreed != 333 ||
                    result.FilesDeleted != 2 ||
                    result.SkippedLockedFiles != 1)
                {
                    throw new Exception(
                        $"Unexpected cleanup accounting: freed={result.BytesFreed}, deleted={result.FilesDeleted}, skipped={result.SkippedLockedFiles}.");
                }

                if (File.Exists(first) || File.Exists(second) || !File.Exists(locked))
                {
                    throw new Exception("Unlocked files were not deleted or the locked file was removed.");
                }

                if (linkCreated && !File.Exists(Path.Combine(outside, "keep.txt")))
                {
                    throw new Exception("Cleanup followed a reparse point outside the target root.");
                }

                string prefetch = Path.Combine(root, "prefetch");
                Directory.CreateDirectory(prefetch);
                File.WriteAllBytes(Path.Combine(prefetch, "app.pf"), new byte[40]);
                File.WriteAllBytes(Path.Combine(prefetch, "app.db"), new byte[20]);
                File.WriteAllBytes(Path.Combine(prefetch, "keep.txt"), new byte[10]);
                StorageCleanupTestResult prefetchResult = StorageCleanupService.CleanRootsForTest(
                    new[] { prefetch },
                    recursive: false,
                    prefetchOnly: true);
                if (prefetchResult.FilesDeleted != 2 ||
                    prefetchResult.BytesFreed != 60 ||
                    !File.Exists(Path.Combine(prefetch, "keep.txt")) ||
                    File.Exists(Path.Combine(prefetch, "app.pf")) ||
                    File.Exists(Path.Combine(prefetch, "app.db")))
                {
                    throw new Exception("Prefetch-only cleanup removed the wrong files.");
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
                try
                {
                    if (linkCreated && Directory.Exists(link))
                    {
                        Directory.Delete(link, recursive: false);
                    }
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(outside))
                    {
                        Directory.Delete(outside, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool TestGpuPowerAndDownloadTimerSafety()
        {
            Console.Write("[TEST] GPU Power Clamping & Download Timer Safety... ");
            try
            {
                var vbios = GpuPowerMath.NormalizeBounds(73, 100, 120);
                if (vbios.MinPercent != 73 || vbios.DefaultPercent != 100 || vbios.MaxPercent != 100)
                {
                    throw new Exception($"Unexpected normalized VBIOS bounds: {vbios}");
                }

                if (GpuPowerMath.Clamp(vbios, 70) != 73 ||
                    GpuPowerMath.Clamp(vbios, 85) != 85 ||
                    GpuPowerMath.Clamp(vbios, 100) != 100 ||
                    GpuPowerMath.Clamp(vbios, 150) != 100)
                {
                    throw new Exception("GPU presets were not clamped to the VBIOS range and 100% ceiling.");
                }

                if (GpuPowerMath.IsUsable(GpuPowerMath.NormalizeBounds(100, 100, 100)) ||
                    GpuPowerMath.IsUsable(GpuPowerMath.NormalizeBounds(73, 73, 100)))
                {
                    throw new Exception("A locked VBIOS power range was treated as controllable.");
                }

                var defaults = new AppSettings();
                if (defaults.GpuPowerControlEnabled ||
                    defaults.GpuReapplyPowerLimitOnStartup ||
                    defaults.DownloadTimerEnabled ||
                    defaults.DownloadTimerAction != "Sleep" ||
                    defaults.DownloadTimerThresholdKbps != 200 ||
                    defaults.DownloadTimerQuietMinutes != 5 ||
                    !defaults.DownloadTimerDiskGuard ||
                    !defaults.DownloadTimerWaitForStart)
                {
                    throw new Exception("New GPU/timer features are not safely disabled by default.");
                }

                if (DownloadTimerService.WarningDuration.TotalSeconds != 60)
                {
                    throw new Exception("The pre-action warning is not 60 seconds.");
                }

                var english = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Resources/Strings.en.xaml", UriKind.Absolute)
                };
                if ((string)english["Settings_GpuPowerTitle"] != "GPU Power Limit Control (Opt-In)" ||
                    (string)english["Settings_GpuPowerUnsupported"] != "Power limit control not supported by this GPU's VBIOS" ||
                    (string)english["Settings_DownloadTimerTitle"] != "Smart Download-Completion Timer (Opt-In)" ||
                    (string)english["Settings_DownloadTimerWaitingStart"] != "Waiting for active download..." ||
                    (string)english["DownloadTimer_WarningBody"] != "Download finished. Entering {0} in {1}s...")
                {
                    throw new Exception("Required GPU/timer English copy is missing or changed.");
                }

                var options = new DownloadTimerOptions(
                    200 * 1024,
                    TimeSpan.FromSeconds(5),
                    DiskGuardEnabled: true,
                    WaitForDownloadStart: true,
                    WarningDuration: TimeSpan.FromSeconds(3));
                var engine = new DownloadTimerEngine();
                engine.Arm(options);
                DownloadTimerTick tick = engine.Advance(options, 0, 0, true);
                if (tick.Phase != DownloadTimerPhase.WaitingForDownload)
                {
                    throw new Exception("Wait-for-start did not hold the timer before the first download.");
                }

                tick = engine.Advance(options, 300 * 1024, 0, true);
                if (tick.Phase != DownloadTimerPhase.Monitoring)
                {
                    throw new Exception("The timer did not begin monitoring after an active download.");
                }

                for (int i = 0; i < 4; i++)
                {
                    tick = engine.Advance(options, 0, 0, true);
                    if (tick.Phase != DownloadTimerPhase.Monitoring)
                    {
                        throw new Exception($"The quiet window completed early at second {i + 1}.");
                    }
                }

                tick = engine.Advance(options, 0, 0, true);
                if (tick.Phase != DownloadTimerPhase.Warning ||
                    !tick.WarningStarted ||
                    tick.WarningSecondsRemaining != 3)
                {
                    throw new Exception("The timer did not enter the 60-second warning pattern after the quiet window.");
                }

                tick = engine.Advance(options, 0, 0, true);
                tick = engine.Advance(options, 0, 0, true);
                tick = engine.Advance(options, 0, 0, true);
                if (!tick.ExecuteAction || tick.Phase != DownloadTimerPhase.Disarmed)
                {
                    throw new Exception("The timer did not execute only after the warning grace period.");
                }

                var cancelEngine = new DownloadTimerEngine();
                var cancelOptions = options with { QuietDuration = TimeSpan.FromSeconds(1), WaitForDownloadStart = false };
                cancelEngine.Arm(cancelOptions);
                cancelEngine.Advance(cancelOptions, 0, 0, true);
                if (!cancelEngine.CancelWarning() || cancelEngine.Phase != DownloadTimerPhase.Disarmed)
                {
                    throw new Exception("Manual cancel did not disarm the pending power action.");
                }

                var diskEngine = new DownloadTimerEngine();
                var diskOptions = options with
                {
                    QuietDuration = TimeSpan.FromSeconds(1),
                    WaitForDownloadStart = false
                };
                diskEngine.Arm(diskOptions);
                tick = diskEngine.Advance(diskOptions, 0, 5 * 1024 * 1024, true);
                if (tick.Phase != DownloadTimerPhase.WaitingForDisk)
                {
                    throw new Exception("Disk unpacking did not hold the timer after downloads went quiet.");
                }

                tick = diskEngine.Advance(diskOptions, 0, 0, false);
                if (tick.Phase != DownloadTimerPhase.WaitingForDisk)
                {
                    throw new Exception("An unavailable disk counter did not fail closed.");
                }

                tick = diskEngine.Advance(diskOptions, 0, 1024 * 1024, true);
                if (tick.Phase != DownloadTimerPhase.Warning)
                {
                    throw new Exception("The timer did not resume after disk writes settled below 2 MB/s.");
                }

                tick = diskEngine.Advance(diskOptions, 500 * 1024, 0, true);
                if (tick.Phase != DownloadTimerPhase.Monitoring || tick.ExecuteAction || !tick.WarningEnded)
                {
                    throw new Exception("Returning download activity did not cancel the pending warning.");
                }

                using (var sampler = new SystemDownloadTimerSampler())
                {
                    sampler.TrySampleDiskWriteBytesPerSecond(out _);
                    Thread.Sleep(1100);
                    if (!sampler.TrySampleDiskWriteBytesPerSecond(out double diskBytes) ||
                        !double.IsFinite(diskBytes) ||
                        diskBytes < 0)
                    {
                        throw new Exception("The live PDH physical-disk write counter could not be sampled.");
                    }
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

        private static bool TestSystemInfoAndHardwareSensorHelpers()
        {
            Console.Write("[TEST] SystemInfo, Hardware Sensor Validation & Localization Parity... ");
            try
            {
                if (SystemInfoQueryService.FormatOsArchitecture("ARM 64-bit Processor") != "ARM64" ||
                    SystemInfoQueryService.FormatOsArchitecture("64-bit") != "x64" ||
                    SystemInfoQueryService.FormatOsArchitecture("32-bit") != "x86")
                {
                    throw new Exception("FormatOsArchitecture returned unexpected values.");
                }

                if (SystemInfoQueryService.EscapeWqlValue(@"\\.\PHYSICALDRIVE0'test") != @"\\\\.\\PHYSICALDRIVE0\'test")
                {
                    throw new Exception("EscapeWqlValue did not escape backslashes and single quotes.");
                }

                if (!HardwareMonitorService.IsGpuEngineLoadSensor("D3D 3D") ||
                    !HardwareMonitorService.IsGpuEngineLoadSensor("D3D Video Decode") ||
                    HardwareMonitorService.IsGpuEngineLoadSensor("GPU Core") ||
                    HardwareMonitorService.IsGpuEngineLoadSensor("GPU Memory") ||
                    HardwareMonitorService.ScoreGpuLoadSensor("GPU Core") <= HardwareMonitorService.ScoreGpuLoadSensor("GPU Memory"))
                {
                    throw new Exception("GPU load sensor selection is incorrect.");
                }

                if (HardwareMonitorService.ResolveGpuLoad(17, 8) != 8 ||
                    HardwareMonitorService.ResolveGpuLoad(17, null) != 17 ||
                    HardwareMonitorService.ResolveGpuLoad(null, 0) != 0)
                {
                    throw new Exception("GPU load did not prefer the busiest engine.");
                }

                if (HardwareMonitorService.IsValidDriveTemperature(0) ||
                    HardwareMonitorService.IsValidDriveTemperature(-40) ||
                    HardwareMonitorService.IsValidDriveTemperature(255) ||
                    HardwareMonitorService.IsValidDriveTemperature(double.NaN) ||
                    !HardwareMonitorService.IsValidDriveTemperature(41.5))
                {
                    throw new Exception("Drive temperature range validation failed.");
                }

                var diag = SystemInfoQueryService.Instance.GenerateDiagnosticsSnapshot(
                    new SystemInfoData { OperatingSystem = "Windows 11 Pro x64", ProcessorName = "Test CPU", GraphicsCard = "Test GPU" },
                    new HardwareSnapshot
                    {
                        CpuTemp = 48,
                        CpuLoad = 20,
                        GpuTemp = 54,
                        GpuLoad = 35,
                        GpuPowerWatts = 95,
                        GpuHotspotTemp = 68,
                        GpuVramTemp = 64,
                        GpuCoreClockMhz = 2415,
                        GpuVramUsedMb = 4096,
                        GpuVramTotalMb = 12288,
                        GpuFanRpm = 1200
                    },
                    null,
                    isFahrenheit: false);

                if (!diag.Contains("Hotspot: 68°C", StringComparison.Ordinal) ||
                    !diag.Contains("VRAM Temp: 64°C", StringComparison.Ordinal) ||
                    !diag.Contains("Clock: 2415 MHz", StringComparison.Ordinal) ||
                    !diag.Contains("VRAM: 4096 / 12288 MB", StringComparison.Ordinal))
                {
                    throw new Exception($"Diagnostics snapshot missing extended GPU fields:\n{diag}");
                }

                // Verify English and Arabic localization dictionaries stay in strict key parity.
                var enDict = new ResourceDictionary { Source = new Uri("pack://application:,,,/Resources/Strings.en.xaml", UriKind.Absolute) };
                var arDict = new ResourceDictionary { Source = new Uri("pack://application:,,,/Resources/Strings.ar.xaml", UriKind.Absolute) };
                if (enDict.Count != arDict.Count)
                {
                    throw new Exception($"Localization key count mismatch: en={enDict.Count}, ar={arDict.Count}");
                }

                foreach (object key in enDict.Keys)
                {
                    if (!arDict.Contains(key))
                    {
                        throw new Exception($"Arabic dictionary missing key: {key}");
                    }
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
    }
}
