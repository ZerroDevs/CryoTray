using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using CryoTray.Services;

namespace CryoTray.Tests
{
    public static class UpdateVerificationTests
    {
        public static int RunAll()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("      CryoTray Update Verification Tests          ");
            Console.WriteLine("==================================================");

            bool allPassed = true;
            allPassed &= TestVersionComparison();
            allPassed &= TestStatusService();
            allPassed &= TestCompletionState();
            allPassed &= TestRollbackHashVerification();
            allPassed &= TestProtectedStorageAcl();
            allPassed &= TestUntrustedRollbackRootIsRejected();
            allPassed &= TestApplicationDirectoryProtection();
            allPassed &= TestInstallationClassification();

            Console.WriteLine("==================================================");
            Console.WriteLine(allPassed
                ? ">>> ALL UPDATE VERIFICATION TESTS PASSED! <<<"
                : ">>> SOME UPDATE VERIFICATION TESTS FAILED! <<<");
            return allPassed ? 0 : 1;
        }

        private static bool TestVersionComparison()
        {
            Console.Write("[TEST] Update version comparison... ");
            try
            {
                Assert(UpdateService.IsNewer("0.0.101", "0.0.100"));
                Assert(UpdateService.IsNewer("v0.0.101", "0.0.100"));
                Assert(!UpdateService.IsNewer("0.0.100", "0.0.100"));
                Assert(!UpdateService.IsNewer("0.0.99", "0.0.100"));
                Assert(!UpdateService.IsNewer("not-a-version", "0.0.100"));
                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestStatusService()
        {
            Console.Write("[TEST] Update check status... ");
            try
            {
                bool raised = false;
                Action handler = () => raised = true;
                UpdateStatusService.Instance.Changed += handler;
                try
                {
                    UpdateStatusService.Instance.Record(
                        UpdateCheckOutcome.Available, "9.9.9", "test detail");
                }
                finally
                {
                    UpdateStatusService.Instance.Changed -= handler;
                }

                var status = UpdateStatusService.Instance;
                Assert(raised);
                Assert(status.LastCheckedUtc.HasValue);
                Assert(Math.Abs((DateTime.UtcNow - status.LastCheckedUtc!.Value).TotalSeconds) < 5);
                Assert(status.Outcome == UpdateCheckOutcome.Available);
                Assert(status.Version == "9.9.9");
                Assert(status.Detail == "test detail");
                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestCompletionState()
        {
            Console.Write("[TEST] Update completion state... ");
            try
            {
                string current = UpdateService.CurrentVersion;
                string from = current == "0.0.0" ? "0.0.1" : "0.0.0";
                UpdateCompletionService.Clear();
                string? updateId = UpdateCompletionService.RecordPending(from, current);
                Assert(!string.IsNullOrWhiteSpace(updateId));

                // A marker is pending until the elevated updater confirms it applied.
                Assert(UpdateCompletionService.ReadPending(current) == null);
                Assert(UpdateCompletionService.MarkApplied(updateId!));

                var info = UpdateCompletionService.ReadPending(current);
                Assert(info != null);
                Assert(info!.FromVersion == from);
                Assert(info.ToVersion == current);

                UpdateCompletionService.Clear();
                Assert(UpdateCompletionService.ReadPending(current) == null);
                Console.WriteLine("PASSED");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                return false;
            }
        }

        private static bool TestRollbackHashVerification()
        {
            Console.Write("[TEST] Rollback hash manifest... ");
            string testDirectory = Path.Combine(
                UpdateStorage.RollbackRoot,
                "verify-" + Guid.NewGuid().ToString("N"));

            try
            {
                UpdateStorage.EnsureRollbackRoot();
                Directory.CreateDirectory(testDirectory);
                string file = Path.Combine(testDirectory, "CryoTray.exe");
                File.WriteAllText(file, "verified payload");

                string hash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
                var info = new UpdateBackupInfo
                {
                    Version = "test",
                    Path = testDirectory,
                    Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CryoTray.exe"] = hash
                    }
                };

                Assert(UpdateBackupService.VerifyBackup(info));

                File.AppendAllText(file, "tampered");
                Assert(!UpdateBackupService.VerifyBackup(info));
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
                    if (Directory.Exists(testDirectory))
                    {
                        Directory.Delete(testDirectory, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool TestProtectedStorageAcl()
        {
            Console.Write("[TEST] Protected ProgramData ACL... ");
            try
            {
                UpdateStorage.EnsureUpdateRoot();
                UpdateStorage.EnsureRollbackRoot();

                var administrators = new SecurityIdentifier(
                    WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(
                    WellKnownSidType.LocalSystemSid, null);
                var users = new SecurityIdentifier(
                    WellKnownSidType.BuiltinUsersSid, null);

                foreach (string path in new[]
                {
                    UpdateStorage.RootPath,
                    UpdateStorage.UpdateRoot,
                    UpdateStorage.DownloadsRoot,
                    UpdateStorage.RollbackRoot
                })
                {
                    var security = new DirectoryInfo(path).GetAccessControl();
                    Assert(security.AreAccessRulesProtected);
                    var owner = security.GetOwner(typeof(SecurityIdentifier));
                    Assert(owner != null &&
                           (owner.Equals(administrators) || owner.Equals(system)));

                    var rules = security.GetAccessRules(
                            includeExplicit: true,
                            includeInherited: true,
                            targetType: typeof(SecurityIdentifier))
                        .Cast<FileSystemAccessRule>()
                        .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                        .ToList();

                    Assert(rules.Any(rule =>
                        rule.IdentityReference.Equals(administrators) &&
                        rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)));
                    Assert(rules.Any(rule =>
                        rule.IdentityReference.Equals(system) &&
                        rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)));
                    Assert(!rules.Any(rule => rule.IdentityReference.Equals(users)));
                }

                string target = Path.Combine(
                    UpdateStorage.DownloadsRoot, "reparse-target-" + Guid.NewGuid().ToString("N"));
                string link = Path.Combine(
                    UpdateStorage.DownloadsRoot, "reparse-link-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(target);
                try
                {
                    using var mklink = Process.Start(new ProcessStartInfo(
                        "cmd.exe",
                        $"/c mklink /J \"{link}\" \"{target}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    Assert(mklink != null);
                    mklink!.WaitForExit();
                    Assert(mklink.ExitCode == 0);

                    bool rejected = false;
                    try
                    {
                        UpdateStorage.SecureDirectory(link);
                    }
                    catch (InvalidOperationException)
                    {
                        rejected = true;
                    }

                    Assert(rejected);
                }
                finally
                {
                    try { if (Directory.Exists(link)) { Directory.Delete(link, false); } } catch { }
                    try { if (Directory.Exists(target)) { Directory.Delete(target, true); } } catch { }
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

        private static bool TestUntrustedRollbackRootIsRejected()
        {
            Console.Write("[TEST] Untrusted rollback root... ");
            string? previousRoot = UpdateStorage.RootPathOverride;
            string testRoot = Path.Combine(
                Path.GetTempPath(), "cryotray-rollback-root-" + Guid.NewGuid().ToString("N"));
            string rollbackRoot = Path.Combine(testRoot, "Rollback");
            string fakeBackup = Path.Combine(rollbackRoot, "fake-backup");

            try
            {
                Directory.CreateDirectory(fakeBackup);
                string fakeExecutable = Path.Combine(fakeBackup, "CryoTray.exe");
                File.WriteAllText(fakeExecutable, "attacker-controlled payload");
                string hash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(fakeExecutable))).ToLowerInvariant();
                var maliciousManifest = new UpdateBackupInfo
                {
                    Version = "9.9.9",
                    Path = fakeBackup,
                    Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CryoTray.exe"] = hash
                    }
                };
                File.WriteAllText(
                    Path.Combine(rollbackRoot, "backup.json"),
                    JsonSerializer.Serialize(maliciousManifest));

                // The temporary root has inherited, unprotected ACLs, which is the
                // condition a standard user can create under ProgramData.
                UpdateStorage.RootPathOverride = testRoot;
                Assert(UpdateBackupService.GetCurrent() == null);
                Assert(!Directory.Exists(fakeBackup));
                Assert(!File.Exists(Path.Combine(rollbackRoot, "backup.json")));

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
                try
                {
                    if (Directory.Exists(testRoot))
                    {
                        Directory.Delete(testRoot, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool TestApplicationDirectoryProtection()
        {
            Console.Write("[TEST] Protected application folder... ");
            string testDirectory = Path.Combine(
                UpdateStorage.RootPath, "app-verify-" + Guid.NewGuid().ToString("N"));
            string unsafeDirectory = Path.Combine(
                Path.GetTempPath(), "cryotray-location-" + Guid.NewGuid().ToString("N"));

            try
            {
                UpdateStorage.EnsureUpdateRoot();
                Directory.CreateDirectory(testDirectory);
                File.WriteAllText(Path.Combine(testDirectory, "CryoTray.exe"), "test");

                UpdateStorage.EnsureApplicationDirectoryProtected(testDirectory);

                var administrators = new SecurityIdentifier(
                    WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(
                    WellKnownSidType.LocalSystemSid, null);
                var users = new SecurityIdentifier(
                    WellKnownSidType.BuiltinUsersSid, null);

                var security = new DirectoryInfo(testDirectory).GetAccessControl();
                Assert(security.AreAccessRulesProtected);
                var owner = security.GetOwner(typeof(SecurityIdentifier));
                Assert(owner != null &&
                       (owner.Equals(administrators) || owner.Equals(system)));

                var rules = security.GetAccessRules(
                        includeExplicit: true,
                        includeInherited: true,
                        targetType: typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                    .ToList();

                Assert(rules.Any(rule =>
                    rule.IdentityReference.Equals(users) &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute)));
                Assert(!rules.Any(rule =>
                    rule.IdentityReference.Equals(users) &&
                    (rule.FileSystemRights & (FileSystemRights.Write |
                        FileSystemRights.Delete |
                        FileSystemRights.ChangePermissions |
                        FileSystemRights.TakeOwnership)) != 0));
                Assert(UpdateStorage.IsSafeApplicationLocation(testDirectory));

                Directory.CreateDirectory(unsafeDirectory);
                Assert(!UpdateStorage.IsSafeApplicationLocation(unsafeDirectory));

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
                try { if (Directory.Exists(testDirectory)) { Directory.Delete(testDirectory, true); } } catch { }
                try { if (Directory.Exists(unsafeDirectory)) { Directory.Delete(unsafeDirectory, true); } } catch { }
            }
        }

        private static bool TestInstallationClassification()
        {
            Console.Write("[TEST] Installed and portable classification... ");
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            string testDirectory = Path.Combine(
                programFiles, "CryoTray-Install-Test-" + Guid.NewGuid().ToString("N"));
            string portableDirectory = Path.Combine(
                UpdateStorage.RootPath, "portable-test-" + Guid.NewGuid().ToString("N"));

            try
            {
                if (string.IsNullOrWhiteSpace(programFiles))
                {
                    throw new InvalidOperationException("Program Files path is unavailable.");
                }

                UpdateStorage.EnsureUpdateRoot();
                Directory.CreateDirectory(testDirectory);
                Directory.CreateDirectory(portableDirectory);

                // A portable copy under Program Files has no Inno uninstaller.
                Assert(!UpdateInstallationService.IsInstalledAt(testDirectory));

                File.WriteAllText(Path.Combine(testDirectory, "unins000.exe"), string.Empty);
                Assert(UpdateInstallationService.IsInstalledAt(testDirectory));

                // The same marker outside Program Files does not make a portable copy installed.
                File.WriteAllText(Path.Combine(portableDirectory, "unins000.exe"), string.Empty);
                Assert(!UpdateInstallationService.IsInstalledAt(portableDirectory));

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
                try { if (Directory.Exists(testDirectory)) { Directory.Delete(testDirectory, true); } } catch { }
                try { if (Directory.Exists(portableDirectory)) { Directory.Delete(portableDirectory, true); } } catch { }
            }
        }

        private static void Assert(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed.");
            }
        }
    }
}
