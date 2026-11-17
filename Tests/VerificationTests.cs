using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using CryoTray.Models;
using CryoTray.Services;

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
            allPassed &= TestWindowsXamlInstantiation();
            allPassed &= TestPerCoreTemperaturePolling();

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

                // Poll hardware directly
                System.Threading.Thread.Sleep(300);
                var snapshot = hms.CurrentSnapshot;
                if (snapshot == null)
                {
                    // If polling loop hasn't completed first tick, invoke ReadHardware via reflection or wait
                    System.Threading.Thread.Sleep(1200);
                    snapshot = hms.CurrentSnapshot;
                }

                if (snapshot == null) throw new Exception("CurrentSnapshot is null");
                if (snapshot.CpuCoresTelemetry.Count < 2) throw new Exception($"Expected multiple CPU cores, got {snapshot.CpuCoresTelemetry.Count}");

                // Validate zero-padded naming (CPU00, CPU01, etc.)
                for (int i = 0; i < snapshot.CpuCoresTelemetry.Count; i++)
                {
                    string expectedName = $"CPU{i:D2}";
                    if (snapshot.CpuCoresTelemetry[i].Name != expectedName)
                    {
                        throw new Exception($"Core {i} name mismatch: expected {expectedName}, got {snapshot.CpuCoresTelemetry[i].Name}");
                    }
                }

                // Validate MaxCoreTemp is highest integer and matches CpuTemp
                int maxInt = (int)Math.Round(snapshot.CpuCoresTelemetry.Max(c => c.Temperature));
                if ((int)Math.Round(snapshot.MaxCoreTemp) != maxInt)
                {
                    throw new Exception($"MaxCoreTemp mismatch: expected {maxInt}, got {snapshot.MaxCoreTemp}");
                }
                if ((int)Math.Round(snapshot.CpuTemp) != maxInt)
                {
                    throw new Exception($"CpuTemp mismatch: expected {maxInt}, got {snapshot.CpuTemp}");
                }

                // Validate hottest flag
                int hottestCount = 0;
                foreach (var c in snapshot.CpuCoresTelemetry)
                {
                    if (c.IsHottest) hottestCount++;
                }
                if (hottestCount < 1) throw new Exception("No core marked as IsHottest");

                Console.WriteLine($"PASSED ({snapshot.CpuCoresTelemetry.Count} cores, Hottest: {maxInt}°C, {hottestCount} tied hottest)");
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
