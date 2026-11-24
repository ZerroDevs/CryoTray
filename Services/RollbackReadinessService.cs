using System;
using System.IO;
using System.Linq;

namespace CryoTray.Services
{
    public sealed record RollbackReadinessResult(
        bool Ready,
        int FileCount,
        long SizeBytes,
        string? ErrorKey);

    /// <summary>
    /// Read-only verification of the saved rollback copy. It never writes to the app directory.
    /// </summary>
    public static class RollbackReadinessService
    {
        public static RollbackReadinessResult Verify()
        {
            return VerifyCore(verifyApplicationLocation: true);
        }

        internal static RollbackReadinessResult VerifyForTest()
        {
            return VerifyCore(verifyApplicationLocation: false);
        }

        private static RollbackReadinessResult VerifyCore(bool verifyApplicationLocation)
        {
            UpdateBackupInfo? backup = UpdateBackupService.GetCurrent();
            if (backup == null)
            {
                return Fail("Settings_ReadinessNoBackup");
            }

            if (!UpdateStorage.IsDirectoryProtected(backup.Path))
            {
                return Fail("Settings_ReadinessAcl");
            }

            if (!UpdateBackupService.VerifyBackup(backup))
            {
                return Fail("Settings_ReadinessCorrupt");
            }

            if (!backup.Files.ContainsKey("CryoTray.exe") ||
                backup.Files.ContainsKey("CryoTray.dll") == false)
            {
                return Fail("Settings_ReadinessIncomplete");
            }

            if (verifyApplicationLocation &&
                !UpdateStorage.IsSafeApplicationLocation(AppContext.BaseDirectory))
            {
                return Fail("Settings_ReadinessAppLocation");
            }

            long size = UpdateBackupService.GetSizeBytes(backup);
            if (size <= 0)
            {
                return Fail("Settings_ReadinessIncomplete");
            }

            return new RollbackReadinessResult(true, backup.Files.Count, size, null);
        }

        private static RollbackReadinessResult Fail(string key)
        {
            return new RollbackReadinessResult(false, 0, 0, key);
        }
    }
}
