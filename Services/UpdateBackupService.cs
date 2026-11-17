using System;
using System.IO;
using System.Text.Json;

namespace CryoTray.Services
{
    public sealed class UpdateBackupInfo
    {
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }

    public static class UpdateBackupService
    {
        private static readonly string RollbackRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CryoTray",
            "Rollback");

        private static readonly string ManifestPath = Path.Combine(RollbackRoot, "backup.json");

        public static void CreateBackup(string version)
        {
            try
            {
                if (Directory.Exists(RollbackRoot))
                {
                    Directory.Delete(RollbackRoot, true);
                }

                string safeVersion = MakeSafeDirectoryName(version);
                string backupPath = Path.Combine(RollbackRoot, safeVersion);
                Directory.CreateDirectory(backupPath);
                CopyDirectory(AppContext.BaseDirectory, backupPath);

                var info = new UpdateBackupInfo
                {
                    Version = version,
                    Path = backupPath,
                    CreatedUtc = DateTime.UtcNow
                };

                Directory.CreateDirectory(RollbackRoot);
                File.WriteAllText(ManifestPath, JsonSerializer.Serialize(info));
            }
            catch
            {
                TryDeleteDirectory(RollbackRoot);
                throw;
            }
        }

        public static UpdateBackupInfo? GetCurrent()
        {
            try
            {
                if (!File.Exists(ManifestPath))
                {
                    return null;
                }

                var info = JsonSerializer.Deserialize<UpdateBackupInfo>(File.ReadAllText(ManifestPath));
                if (info == null ||
                    string.IsNullOrWhiteSpace(info.Version) ||
                    string.IsNullOrWhiteSpace(info.Path) ||
                    !Directory.Exists(info.Path) ||
                    !IsInsideRollbackRoot(info.Path))
                {
                    return null;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsInsideRollbackRoot(string path)
        {
            string root = Path.GetFullPath(RollbackRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeSafeDirectoryName(string version)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            foreach (char invalidCharacter in invalidCharacters)
            {
                version = version.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(version) ? "previous" : version;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destination, true);
            }

            foreach (string directory in Directory.GetDirectories(sourceDirectory))
            {
                string destination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, destination);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
