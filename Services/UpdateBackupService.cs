using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CryoTray.Services
{
    public sealed class UpdateBackupInfo
    {
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class UpdateBackupService
    {
        private static string RollbackRoot => UpdateStorage.RollbackRoot;
        private static string ManifestPath => Path.Combine(RollbackRoot, "backup.json");

        public static void CreateBackup(string version)
        {
            UpdateStorage.EnsureRollbackRoot();

            string safeVersion = MakeSafeDirectoryName(version);
            string backupPath = Path.Combine(
                RollbackRoot,
                $"{safeVersion}-{Guid.NewGuid():N}");

            try
            {
                UpdateStorage.SecureDirectory(backupPath);
                CopyDirectory(AppContext.BaseDirectory, backupPath);

                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.EnumerateFiles(
                    backupPath, "*", SearchOption.AllDirectories))
                {
                    string relative = GetRelativePath(backupPath, file);
                    files[relative] = ComputeHash(file);
                }

                var info = new UpdateBackupInfo
                {
                    Version = version,
                    Path = backupPath,
                    CreatedUtc = DateTime.UtcNow,
                    Files = files
                };

                string tempManifest = ManifestPath + ".tmp";
                File.WriteAllText(tempManifest, JsonSerializer.Serialize(info));
                File.Move(tempManifest, ManifestPath, true);

                foreach (string directory in Directory.GetDirectories(RollbackRoot))
                {
                    if (!string.Equals(
                        Path.GetFullPath(directory),
                        Path.GetFullPath(backupPath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteDirectory(directory);
                    }
                }
            }
            catch
            {
                TryDeleteDirectory(backupPath);
                throw;
            }
        }

        public static UpdateBackupInfo? GetCurrent()
        {
            try
            {
                UpdateStorage.EnsureRollbackRoot();
                if (!File.Exists(ManifestPath))
                {
                    return null;
                }

                var info = JsonSerializer.Deserialize<UpdateBackupInfo>(
                    File.ReadAllText(ManifestPath));
                if (info == null ||
                    string.IsNullOrWhiteSpace(info.Version) ||
                    string.IsNullOrWhiteSpace(info.Path) ||
                    !Directory.Exists(info.Path) ||
                    !IsInsideRollbackRoot(info.Path) ||
                    info.Files == null ||
                    info.Files.Keys.Any(key => !IsSafeRelativePath(key)))
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

        public static bool VerifyBackup(UpdateBackupInfo info)
        {
            if (info == null ||
                !Directory.Exists(info.Path) ||
                !IsInsideRollbackRoot(info.Path) ||
                info.Files == null)
            {
                return false;
            }

            try
            {
                UpdateStorage.EnsureNoReparsePoints(info.Path);
            }
            catch
            {
                return false;
            }

            if (ContainsReparsePoint(info.Path))
            {
                return false;
            }

            var actualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(
                info.Path, "*", SearchOption.AllDirectories))
            {
                string relative = GetRelativePath(info.Path, file);
                actualFiles.Add(relative);

                if (!info.Files.TryGetValue(relative, out string? expectedHash) ||
                    !IsSha256(expectedHash) ||
                    !string.Equals(ComputeHash(file), expectedHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return info.Files.Count == actualFiles.Count &&
                   info.Files.Keys.All(actualFiles.Contains);
        }

        private static bool ContainsReparsePoint(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if ((attributes & FileAttributes.Directory) != 0 &&
                    ContainsReparsePoint(entry))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool VerifyCurrent()
        {
            var info = GetCurrent();
            return info != null && VerifyBackup(info);
        }

        private static bool IsInsideRollbackRoot(string path)
        {
            string root = Path.GetFullPath(RollbackRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return candidate.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return false;
            }

            string[] parts = path.Replace('\\', '/').Split('/');
            return parts.All(part => part.Length > 0 && part != "." && part != "..");
        }

        private static bool IsSha256(string? hash)
        {
            return hash != null &&
                   hash.Length == 64 &&
                   hash.All(Uri.IsHexDigit);
        }

        private static string GetRelativePath(string root, string file)
        {
            return Path.GetRelativePath(root, file).Replace('\\', '/');
        }

        private static string ComputeHash(string file)
        {
            using var stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
            if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    LocalizationService.Get("Update_SecureStorageFailed"));
            }

            Directory.CreateDirectory(destinationDirectory);

            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Update_SecureStorageFailed"));
                }

                string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destination, true);
            }

            foreach (string directory in Directory.GetDirectories(sourceDirectory))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Update_SecureStorageFailed"));
                }

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
