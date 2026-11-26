using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Tests
{
    public static class FollowUpVerificationTests
    {
        internal static bool RunNonElevatedChecks()
        {
            bool allPassed = true;
            allPassed &= TestTelemetryDemandAndPolicy();
            allPassed &= TestChartReducer();
            allPassed &= TestActivityLog();
            allPassed &= TestSupportBundle();
            allPassed &= TestSettingsSearch();
            allPassed &= TestAutopilotTargetResolution();
            allPassed &= TestPowerUtilitySafety();
            return allPassed;
        }

        internal static bool RunUpdateFollowUpChecks()
        {
            bool allPassed = true;
            allPassed &= TestUpdateChannelSelection();
            allPassed &= TestRollbackReadiness();
            return allPassed;
        }

        private static bool TestTelemetryDemandAndPolicy()
        {
            Console.Write("[TEST] Adaptive telemetry demand & policy... ");
            try
            {
                var tracker = new TelemetryDemandTracker();
                Assert(tracker.Current == TelemetryDemand.None, "initial demand");
                Assert(TelemetryPollPolicy.GetInterval(tracker.Current) == TimeSpan.FromSeconds(3), "idle interval");

                using (tracker.Acquire("dashboard", TelemetryDemand.Dashboard))
                {
                    Assert(tracker.Current == TelemetryDemand.Dashboard, "dashboard demand");
                    Assert(TelemetryPollPolicy.GetInterval(tracker.Current) == TimeSpan.FromSeconds(1), "dashboard interval");
                }

                Assert(tracker.Current == TelemetryDemand.None, "dashboard release");
                using (tracker.Acquire("overlay", TelemetryDemand.Overlay))
                using (tracker.Acquire("tray", TelemetryDemand.Tray))
                {
                    Assert(TelemetryPollPolicy.GetInterval(tracker.Current) == TimeSpan.FromSeconds(1), "overlay interval");
                }

                using (tracker.Acquire("tray", TelemetryDemand.Tray))
                {
                    Assert(TelemetryPollPolicy.GetInterval(tracker.Current) == TimeSpan.FromSeconds(2), "tray interval");
                }

                Assert(TelemetryPollPolicy.GetInterval(TelemetryDemand.None) == TimeSpan.FromSeconds(3), "none interval");
                Assert(TelemetryPollPolicy.ShouldReadStorage(TelemetryDemand.Dashboard), "dashboard storage");
                Assert(!TelemetryPollPolicy.ShouldReadStorage(TelemetryDemand.Tray), "tray storage");
                Assert(TelemetryPollPolicy.ShouldReadFans(TelemetryDemand.Alerts), "alert fans");
                Assert(!TelemetryPollPolicy.ShouldReadFans(TelemetryDemand.Tray), "tray fans");

                DateTime now = DateTime.UtcNow;
                Assert(TelemetryPollPolicy.IsStorageRefreshDue(
                    DateTime.MinValue, now, TelemetryDemand.Dashboard), "first storage");
                Assert(!TelemetryPollPolicy.IsStorageRefreshDue(
                    now.AddSeconds(-5), now, TelemetryDemand.Dashboard), "storage 5s");
                Assert(TelemetryPollPolicy.IsStorageRefreshDue(
                    now.AddSeconds(-11), now, TelemetryDemand.Dashboard), "storage 11s");
                Assert(!TelemetryPollPolicy.IsStorageRefreshDue(
                    now.AddSeconds(-11), now, TelemetryDemand.Alerts), "alert storage 11s");
                Assert(TelemetryPollPolicy.IsStorageRefreshDue(
                    now.AddSeconds(-31), now, TelemetryDemand.Alerts), "alert storage 31s");
                Assert(!TelemetryPollPolicy.IsStorageRefreshDue(
                    now.AddSeconds(-31), now, TelemetryDemand.Tray), "tray storage 31s");

                tracker.SetBaseline(TelemetryDemand.Tray);
                var first = tracker.Acquire("window", TelemetryDemand.Settings);
                var second = tracker.Acquire("window", TelemetryDemand.Settings);
                first.Dispose();
                first.Dispose();
                Assert(tracker.Current == (TelemetryDemand.Tray | TelemetryDemand.Settings), "first lease release");
                second.Dispose();
                Assert(tracker.Current == TelemetryDemand.Tray, "second lease release");

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestChartReducer()
        {
            Console.Write("[TEST] Chart decimation... ");
            try
            {
                var samples = Enumerable.Range(0, 3600)
                    .Select(i => new TelemetrySample
                    {
                        CpuTemp = 50 + Math.Sin(i / 20.0) * 10
                    })
                    .ToList();
                samples[1800].CpuTemp = 95;

                List<int> reduced = ChartSampleReducer.ReduceRange(
                    samples, 0, samples.Count - 1, 200, sample => sample.CpuTemp);
                Assert(reduced.Count <= 200);
                Assert(reduced.Contains(0));
                Assert(reduced.Contains(samples.Count - 1));
                Assert(reduced.Contains(1800));
                Assert(reduced.SequenceEqual(reduced.OrderBy(index => index)));
                Assert(reduced.Distinct().Count() == reduced.Count);

                List<int> firstSegment = ChartSampleReducer.ReduceRange(
                    samples, 0, 1799, 100, sample => sample.CpuTemp);
                List<int> secondSegment = ChartSampleReducer.ReduceRange(
                    samples, 1800, 3599, 100, sample => sample.CpuTemp);
                Assert(firstSegment.Contains(0));
                Assert(firstSegment.Contains(1799));
                Assert(secondSegment.Contains(1800));
                Assert(secondSegment.Contains(3599));
                Assert(firstSegment.Count + secondSegment.Count <= 200);

                var shortSeries = samples.Take(40).ToList();
                List<int> unchanged = ChartSampleReducer.ReduceRange(
                    shortSeries, 0, shortSeries.Count - 1, 200, sample => sample.CpuTemp);
                Assert(unchanged.Count == shortSeries.Count);
                Assert(unchanged[0] == 0 && unchanged[^1] == shortSeries.Count - 1);

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestActivityLog()
        {
            Console.Write("[TEST] Activity log cap & sanitization... ");
            string folder = Path.Combine(
                Path.GetTempPath(), "cryotray-activity-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var log = new ActivityLogService(folder);
                string userProfile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                for (int i = 0; i < 305; i++)
                {
                    log.RecordDetail(
                        ActivitySeverity.Info,
                        "test",
                        "Activity_AppStarted",
                        $"{userProfile}\\test-{i}.txt",
                        i);
                }

                Assert(log.Snapshot().Count == 50);
                Assert(log.Snapshot()[0].Detail.Contains("test-255.txt"));
                Assert(log.Snapshot()[^1].Detail.Contains("%USERPROFILE%"));
                Assert(!log.Snapshot()[^1].Detail.Contains(userProfile));
                Assert(log.SerializeForBundle().Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries).Length == 50);

                string logPath = Path.Combine(folder, "activity.jsonl");
                Assert(File.ReadAllLines(logPath).Length == 50);

                string lastDetail = log.Snapshot()[^1].Detail;
                log.RecordDetail(
                    ActivitySeverity.Info,
                    "test",
                    "Activity_AppStarted",
                    lastDetail,
                    304);
                Assert(log.Snapshot().Count == 50);
                Assert(File.ReadAllLines(logPath).Length == 50);

                var reloaded = new ActivityLogService(folder);
                Assert(reloaded.Snapshot().Count == 50);
                reloaded.Clear();
                Assert(reloaded.Snapshot().Count == 0);

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
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            }
        }

        private static bool TestSupportBundle()
        {
            Console.Write("[TEST] Support bundle entries & redaction... ");
            string path = Path.Combine(
                Path.GetTempPath(), "cryotray-bundle-test-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                SupportBundleService.Create(path);
                Assert(File.Exists(path));
                using (ZipArchive archive = ZipFile.OpenRead(path))
                {
                    var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(
                        StringComparer.OrdinalIgnoreCase);
                    Assert(names.Contains("summary.txt"));
                    Assert(names.Contains("diagnostics.txt"));
                    Assert(names.Contains("system-info.txt"));
                    Assert(names.Contains("activity.jsonl"));
                    Assert(names.Contains("activity.txt"));
                    Assert(names.Contains("settings.json"));

                    var settingsEntry = archive.GetEntry("settings.json");
                    Assert(settingsEntry != null);
                    using var reader = new StreamReader(settingsEntry!.Open());
                    string settings = reader.ReadToEnd();
                    Assert(settings.Contains("\"UpdateChannel\""));
                    Assert(settings.Contains("\"PreferredCoolingProfile\""));
                    Assert(settings.Contains("\"SyncCpuTrayBadgeColor\""));
                    Assert(settings.Contains("\"CoolingDisplayOffAcMinutes\""));
                    Assert(!settings.Contains("AutopilotWhitelist"));
                    Assert(!settings.Contains("AutopilotAppProfiles"));
                    Assert(!settings.Contains("HotkeyKey"));
                    Assert(!settings.Contains("FanAliases"));
                    Assert(!settings.Contains("ExternalPowerProfiles"));
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
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static bool TestSettingsSearch()
        {
            Console.Write("[TEST] Settings search ranking & localization... ");
            try
            {
                var items = new List<SettingsSearchItem>
                {
                    new() { Title = "Release channel", Keywords = "update stable preview", TabKey = "Settings_TabAbout" },
                    new() { Title = "Verify rollback readiness", Keywords = "restore backup", TabKey = "Settings_TabAbout" },
                    new() { Title = "Game Mode", Keywords = "autopilot profile", TabKey = "Settings_TabAutopilot" },
                    new() { Title = "CPU clock speed", Keywords = "cpu frequency", TabKey = "Settings_TabCpu" },
                    new() { Title = "CPU temperature", Keywords = "cpu temp thermal", TabKey = "Settings_TabOverlay" },
                    new() { Title = "Show CPU clock speed", Keywords = "clock cpu readout", TabKey = "Settings_TabCpu" },
                    new() { Title = "قناة الإصدار", Keywords = "تحديث معاينة", TabKey = "Settings_TabAbout" }
                };

                var channel = SettingsSearchMatcher.Rank(items, "channel");
                Assert(channel.Count == 1 && channel[0].Title == "Release channel");

                var arabic = SettingsSearchMatcher.Rank(items, "الإصدار");
                Assert(arabic.Count == 1 && arabic[0].Title == "قناة الإصدار");

                var rollback = SettingsSearchMatcher.Rank(items, "ROLLBACK");
                Assert(rollback.Count == 1 && rollback[0].Title == "Verify rollback readiness");

                var none = SettingsSearchMatcher.Rank(items, "printer");
                Assert(none.Count == 0);

                var gameMode = SettingsSearchMatcher.Rank(items, "game");
                Assert(gameMode.Count == 1 && gameMode[0].Title == "Game Mode");

                var cpu = SettingsSearchMatcher.Rank(items, "  cpu   ");
                Assert(cpu.Count >= 2 && cpu[0].Title == "CPU clock speed");

                var reversedTokens = SettingsSearchMatcher.Rank(items, "temp cpu");
                Assert(reversedTokens.Count == 1 &&
                       reversedTokens[0].Title == "CPU temperature");

                var extraToken = SettingsSearchMatcher.Rank(items, "cpu printer");
                Assert(extraToken.Count == 0);

                var clock = SettingsSearchMatcher.Rank(items, "clock cpu");
                Assert(clock.Any(item => item.Title == "Show CPU clock speed"));

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestAutopilotTargetResolution()
        {
            Console.Write("[TEST] Game Mode target resolution & profile fallback... ");
            try
            {
                var settings = new AppSettings
                {
                    AutopilotForegroundOnly = true,
                    PreferredGamingProfile = PowerProfiles.GamingLowKey,
                    AutopilotAppProfiles = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["game.exe"] = PowerProfiles.GamingHighKey,
                        ["code.exe"] = PowerProfiles.WorkKey
                    }
                };

                var foregroundTarget = AutopilotService.ResolveWhitelistTarget(
                    settings,
                    new[] { "game.exe" },
                    "game",
                    Array.Empty<string>());
                Assert(foregroundTarget != null);
                Assert(foregroundTarget!.ProcessName == "game");
                Assert(foregroundTarget.ProfileKey == PowerProfiles.GamingHighKey);
                Assert(!foregroundTarget.IsGameMode);

                Assert(AutopilotService.ResolveWhitelistTarget(
                    settings,
                    new[] { "game.exe" },
                    "other",
                    new[] { "game" }) == null);

                settings.AutopilotForegroundOnly = false;
                var runningTarget = AutopilotService.ResolveWhitelistTarget(
                    settings,
                    new[] { "game.exe" },
                    null,
                    new[] { "game" });
                Assert(runningTarget != null && runningTarget.ProcessName == "game");

                Assert(AutopilotService.ResolveWhitelistTarget(
                    settings,
                    new[] { "game.exe" },
                    "other",
                    new[] { "notgame" }) == null);
                Assert(AutopilotService.ResolveProfileKey(
                    "unknown.exe",
                    settings.AutopilotAppProfiles,
                    settings.PreferredGamingProfile) == PowerProfiles.GamingLowKey);
                Assert(AutopilotService.ResolveProfileKey(
                    "game.exe",
                    settings.AutopilotAppProfiles,
                    settings.PreferredGamingProfile) == PowerProfiles.GamingHighKey);
                Guid extGuid = Guid.NewGuid();
                string extKey = PowerProfiles.MakeExternalKey(extGuid);
                settings.ExternalPowerProfiles.Add(new ExternalPowerProfileSetting
                {
                    Guid = extGuid.ToString("D"),
                    Name = "OEM Performance",
                    AccentColor = "#3399CC"
                });
                settings.AutopilotAppProfiles["render.exe"] = extKey;

                Assert(AutopilotService.ResolveProfileKey(
                    "code.exe",
                    settings.AutopilotAppProfiles,
                    settings.PreferredGamingProfile) == PowerProfiles.WorkKey);
                Assert(AutopilotService.ResolveProfileKey(
                    "render.exe",
                    settings.AutopilotAppProfiles,
                    settings.PreferredGamingProfile,
                    settings) == extKey);

                settings.AutopilotAppProfiles["quiet.exe"] = PowerProfiles.EcoKey;
                Assert(AutopilotService.ResolveProfileKey(
                    "quiet.exe",
                    settings.AutopilotAppProfiles,
                    settings.PreferredGamingProfile,
                    settings) == PowerProfiles.EcoKey);
                var appProfileMenuKeys = PowerProfiles.GetVisibleMenuProfiles(settings)
                    .Select(p => p.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert(appProfileMenuKeys.Contains(PowerProfiles.EcoKey));
                Assert(appProfileMenuKeys.Contains(PowerProfiles.BalancedKey));
                Assert(appProfileMenuKeys.Contains(PowerProfiles.GamingHighKey));

                var moved = new AppSettings
                {
                    AutopilotForegroundOnly = false,
                    PreferredGamingProfile = PowerProfiles.GamingLowKey,
                    AutopilotAppProfiles = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["game.exe"] = PowerProfiles.GamingHighKey,
                        ["code.exe"] = PowerProfiles.WorkKey
                    }
                };
                string json = JsonSerializer.Serialize(moved);
                AppSettings? roundTripped = JsonSerializer.Deserialize<AppSettings>(json);
                Assert(roundTripped != null);
                Assert(roundTripped!.AutopilotAppProfiles["game.exe"] == PowerProfiles.GamingHighKey);

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        internal static bool TestUpdateChannelSelection()
        {
            Console.Write("[TEST] Update release channel selection... ");
            try
            {
                const string json = """
                [
                  {
                    "tag_name": "v0.0.104-preview.2",
                    "prerelease": true,
                    "draft": false,
                    "assets": [
                      { "name": "CryoTray-Setup-0.0.104.exe", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.104-preview.2/CryoTray-Setup-0.0.104.exe" },
                      { "name": "CryoTray-Update-0.0.104-win-x64.zip", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.104-preview.2/CryoTray-Update-0.0.104-win-x64.zip" }
                    ]
                  },
                  {
                    "tag_name": "v0.0.104",
                    "prerelease": false,
                    "draft": false,
                    "assets": [
                      { "name": "CryoTray-Setup-0.0.104.exe", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.104/CryoTray-Setup-0.0.104.exe" },
                      { "name": "CryoTray-Update-0.0.104-win-x64.zip", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.104/CryoTray-Update-0.0.104-win-x64.zip" }
                    ]
                  },
                  {
                    "tag_name": "v0.0.105-preview.1",
                    "prerelease": true,
                    "draft": false,
                    "assets": [
                      { "name": "CryoTray-Setup-0.0.105.exe", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.105-preview.1/CryoTray-Setup-0.0.105.exe" },
                      { "name": "CryoTray-Update-0.0.105-win-x64.zip", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.105-preview.1/CryoTray-Update-0.0.105-win-x64.zip" }
                    ]
                  },
                  {
                    "tag_name": "v0.0.106",
                    "prerelease": false,
                    "draft": true,
                    "assets": [
                      { "name": "CryoTray-Setup-0.0.106.exe", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/v0.0.106/CryoTray-Setup-0.0.106.exe" }
                    ]
                  },
                  {
                    "tag_name": "not-a-version",
                    "prerelease": false,
                    "draft": false,
                    "assets": [
                      { "name": "CryoTray-Setup.exe", "browser_download_url": "https://github.com/ZerroDevs/CryoTray/releases/download/bad/CryoTray-Setup.exe" }
                    ]
                  }
                ]
                """;
                using var document = JsonDocument.Parse(json);
                ReleaseInfo? stable = UpdateService.SelectReleaseForChannel(
                    document.RootElement, "Stable", installed: true);
                Assert(stable != null);
                Assert(stable!.Version == "0.0.104");
                Assert(!stable.IsPrerelease);

                ReleaseInfo? preview = UpdateService.SelectReleaseForChannel(
                    document.RootElement, "Preview", installed: false);
                Assert(preview != null);
                Assert(preview!.Version == "0.0.105");
                Assert(preview.IsPrerelease);
                Assert(preview.AssetKind == UpdateAssetKind.Archive);
                Assert(UpdateService.IsNewer("0.0.105-preview.1", "0.0.104"));
                Assert(UpdateService.NormalizeChannel("preview") == "Preview");
                Assert(UpdateService.NormalizeChannel("") == "Stable");

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        internal static bool TestRollbackReadiness()
        {
            Console.Write("[TEST] Rollback readiness verification... ");
            string? previousRoot = UpdateStorage.RootPathOverride;
            string testRoot = Path.Combine(
                Path.GetTempPath(), "cryotray-readiness-test-" + Guid.NewGuid().ToString("N"));
            string backupDirectory = Path.Combine(testRoot, "Rollback", "previous");
            try
            {
                UpdateStorage.RootPathOverride = testRoot;
                UpdateStorage.EnsureRollbackRoot();
                RollbackReadinessResult missing = RollbackReadinessService.VerifyForTest();
                Assert(!missing.Ready);
                Assert(missing.ErrorKey == "Settings_ReadinessNoBackup");

                Directory.CreateDirectory(backupDirectory);
                UpdateStorage.SecureDirectory(backupDirectory);
                string exe = Path.Combine(backupDirectory, "CryoTray.exe");
                string dll = Path.Combine(backupDirectory, "CryoTray.dll");
                File.WriteAllText(exe, "exe-payload");
                File.WriteAllText(dll, "dll-payload");

                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CryoTray.exe"] = Hash(exe),
                    ["CryoTray.dll"] = Hash(dll)
                };
                var info = new UpdateBackupInfo
                {
                    Version = "0.0.104",
                    Path = backupDirectory,
                    CreatedUtc = DateTime.UtcNow,
                    Files = files
                };
                string manifest = Path.Combine(UpdateStorage.RollbackRoot, "backup.json");
                File.WriteAllText(manifest, JsonSerializer.Serialize(info));

                RollbackReadinessResult ready = RollbackReadinessService.VerifyForTest();
                Assert(ready.Ready);
                Assert(ready.FileCount == 2);
                Assert(ready.SizeBytes > 0);

                File.AppendAllText(exe, "tampered");
                RollbackReadinessResult corrupt = RollbackReadinessService.VerifyForTest();
                Assert(!corrupt.Ready);
                Assert(corrupt.ErrorKey == "Settings_ReadinessCorrupt");

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
                UpdateStorage.RootPathOverride = previousRoot;
                try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
            }
        }

        private static bool TestPowerUtilitySafety()
        {
            Console.Write("[TEST] Opt-in power utility defaults & guardrails... ");
            try
            {
                var defaults = new AppSettings();
                Assert(defaults.TimerResolutionMode == "Off", "timer default");
                Assert(defaults.TimerResolutionHundredNs == 10000, "timer target default");
                Assert(defaults.TimerResolutionPauseOnBattery, "battery guard default");
                Assert(!defaults.AutoStandbyPurgeEnabled, "auto-purge default");
                Assert(!defaults.AllowAutoPurgeDuringGames, "game purge default");
                Assert(defaults.AutoPurgeFreeRamThresholdMb == 1024, "free threshold default");
                Assert(defaults.AutoPurgeStandbyThresholdMb == 4096, "standby threshold default");
                Assert(defaults.AutoPurgeCooldownMinutes == 5, "cooldown default");
                Assert(defaults.KeepAwakeAllowDisplayOff, "display-off default");

                string json = JsonSerializer.Serialize(defaults);
                Assert(!json.Contains("KeepAwakeMode", StringComparison.Ordinal), "runtime wake state persisted");
                Assert(!json.Contains("KeepAwakeEnds", StringComparison.Ordinal), "wake deadline persisted");
                Assert(json.Contains("KeepAwakeAllowDisplayOff", StringComparison.Ordinal), "display preference missing");
                AppSettings? roundTrip = JsonSerializer.Deserialize<AppSettings>(json);
                Assert(roundTrip != null && roundTrip.TimerResolutionMode == "Off");

                Assert(TimerResolutionService.NormalizeTarget(5000) == 5000);
                Assert(TimerResolutionService.NormalizeTarget(10000) == 10000);
                Assert(TimerResolutionService.NormalizeTarget(4242) == 10000);
                Assert(TimerResolutionService.ClampToHardware(4000, 5000, 156250) == 5000);
                Assert(TimerResolutionService.ClampToHardware(200000, 5000, 156250) == 156250);
                Assert(TimerResolutionService.ClampToHardware(10000, 156250, 5000) == 10000);
                Assert(Math.Abs(TimerResolutionService.ToMilliseconds(5000) - 0.5) < 0.0001);
                Assert(TimerResolutionService.ParseMode(null) == TimerResolutionMode.Off);
                Assert(TimerResolutionService.ParseMode("AutopilotOnly") == TimerResolutionMode.AutopilotOnly);

                DateTime now = DateTime.UtcNow;
                Assert(StandbyPurgeService.ShouldPurge(
                    true, false, false, 900, 5000, 1024, 4096, now, null, 5));
                Assert(!StandbyPurgeService.ShouldPurge(
                    false, false, false, 900, 5000, 1024, 4096, now, null, 5));
                Assert(!StandbyPurgeService.ShouldPurge(
                    true, true, false, 900, 5000, 1024, 4096, now, null, 5));
                Assert(StandbyPurgeService.ShouldPurge(
                    true, true, true, 900, 5000, 1024, 4096, now, null, 5));
                Assert(!StandbyPurgeService.ShouldPurge(
                    true, false, false, 1024, 5000, 1024, 4096, now, null, 5));
                Assert(!StandbyPurgeService.ShouldPurge(
                    true, false, false, 900, 4096, 1024, 4096, now, null, 5));
                Assert(!StandbyPurgeService.ShouldPurge(
                    true, false, false, 900, 5000, 1024, 4096, now, now.AddMinutes(-4), 5));
                Assert(StandbyPurgeService.ShouldPurge(
                    true, false, false, 900, 5000, 1024, 4096, now, now.AddMinutes(-5), 5));

                Assert(KeepAwakeService.TryParseCustomDuration("45", "Minutes", out TimeSpan fortyFive) &&
                    fortyFive == TimeSpan.FromMinutes(45));
                Assert(KeepAwakeService.TryParseCustomDuration("3", "Hours", out TimeSpan threeHours) &&
                    threeHours == TimeSpan.FromHours(3));
                Assert(KeepAwakeService.TryParseCustomDuration("4320", "Minutes", out _));
                Assert(!KeepAwakeService.TryParseCustomDuration("4321", "Minutes", out _));
                Assert(!KeepAwakeService.TryParseCustomDuration("73", "Hours", out _));
                Assert(!KeepAwakeService.TryParseCustomDuration("0", "Minutes", out _));
                Assert(!KeepAwakeService.TryParseCustomDuration("abc", "Minutes", out _));
                Assert(KeepAwakeService.FormatRemaining(TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(18)) == "00:42:18");
                Assert(KeepAwakeService.FormatRemaining(TimeSpan.FromHours(72)) == "72:00:00");

                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static string Hash(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant();
        }

        private static void Assert(bool condition, string? detail = null)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(detail) ? "Assertion failed." : $"Assertion failed: {detail}");
            }
        }
    }
}
