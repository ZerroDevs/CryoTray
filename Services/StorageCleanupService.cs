using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;

namespace CryoTray.Services
{
    internal sealed record StorageCleanupTestResult(
        long BytesFreed,
        int FilesDeleted,
        int SkippedLockedFiles);

    /// <summary>
    /// Scans and removes only the cache and temporary locations exposed by
    /// <see cref="StorageCleanupCatalog"/>. Every path is reparse-checked,
    /// containment-checked, and counted only after a successful delete.
    /// </summary>
    public static class StorageCleanupService
    {
        private static readonly EnumerationOptions EnumerateAll = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        private static readonly string[] BrowserProcessNames =
        {
            "chrome",
            "msedge",
            "brave",
            "opera",
            "firefox"
        };

        private static readonly HashSet<string> BrowserPrivateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Cookies",
            "Login Data",
            "Web Data",
            "History",
            "Bookmarks",
            "Sessions",
            "Local Storage",
            "IndexedDB"
        };

        private sealed record CleanupTarget(
            string RootPath,
            bool Recursive,
            bool PrefetchOnly,
            bool BrowserPrivacy);

        private sealed class CleanupCounters
        {
            public long Bytes { get; set; }
            public int Files { get; set; }
            public int Skipped { get; set; }
        }

        public static bool HasRunningBrowser()
        {
            foreach (string processName in BrowserProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (processes.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        public static Task<StorageCleanupScan> ScanAsync(
            CancellationToken cancellationToken = default,
            IProgress<StorageCleanupProgress>? progress = null)
        {
            return Task.Run(() => ScanCore(cancellationToken, progress), cancellationToken);
        }

        public static Task<StorageCleanupRunResult> CleanAsync(
            IEnumerable<StorageCleanupCategory> categories,
            CancellationToken cancellationToken = default,
            IProgress<StorageCleanupProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(categories);
            return Task.Run(() => CleanCore(categories, cancellationToken, progress), cancellationToken);
        }

        public static string FormatBytes(long bytes)
        {
            return bytes <= 0
                ? "0 MB"
                : MemoryTelemetryService.FormatBytes((ulong)bytes);
        }

        internal static bool IsPathWithinRoot(string candidatePath, string rootPath)
        {
            try
            {
                string root = NormalizeRoot(rootPath);
                string candidate = Path.GetFullPath(candidatePath);
                if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool ContainsPrivateBrowserSegment(string path)
        {
            try
            {
                string[] segments = path.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                return segments.Any(segment => BrowserPrivateNames.Contains(segment));
            }
            catch
            {
                return true;
            }
        }

        internal static bool IsPrefetchFile(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".pf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".db", StringComparison.OrdinalIgnoreCase);
        }

        // A redirected TEMP or cache path must never turn cleanup into a system-wide delete.
        internal static bool IsSafeCleanupRoot(string rootPath)
        {
            try
            {
                string root = NormalizeRoot(rootPath);
                string? pathRoot = Path.GetPathRoot(root);
                if (string.IsNullOrWhiteSpace(pathRoot) ||
                    string.Equals(
                        root,
                        Path.TrimEndingDirectorySeparator(pathRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (string protectedPath in GetProtectedCleanupRoots(userProfile))
                {
                    if (string.IsNullOrWhiteSpace(protectedPath))
                    {
                        continue;
                    }

                    string protectedRoot;
                    try
                    {
                        protectedRoot = NormalizeRoot(protectedPath);
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsPathWithinRoot(protectedRoot, root))
                    {
                        return false;
                    }
                }

                string? profileRoot = null;
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    try
                    {
                        profileRoot = NormalizeRoot(userProfile);
                    }
                    catch
                    {
                    }
                }

                string? parent = Path.GetDirectoryName(root);
                if (profileRoot != null &&
                    string.Equals(parent, profileRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string? usersRoot = GetUsersRoot(userProfile);
                if (usersRoot != null &&
                    string.Equals(parent, usersRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static IReadOnlyList<string> GetTempRootsForTest()
        {
            return BuildTempTargets().Select(target => target.RootPath).ToList();
        }

        internal static StorageCleanupTestResult CleanRootsForTest(
            IEnumerable<string> roots,
            bool recursive,
            bool prefetchOnly,
            CancellationToken cancellationToken = default)
        {
            var counters = new CleanupCounters();
            foreach (string root in roots)
            {
                ProcessRoot(
                    new CleanupTarget(root, recursive, prefetchOnly, BrowserPrivacy: false),
                    delete: true,
                    cancellationToken,
                    counters);
            }

            return new StorageCleanupTestResult(
                counters.Bytes,
                counters.Files,
                counters.Skipped);
        }

        private static StorageCleanupScan ScanCore(
            CancellationToken cancellationToken,
            IProgress<StorageCleanupProgress>? progress)
        {
            var categories = new Dictionary<StorageCleanupCategory, StorageCleanupCategoryScan>();
            long totalBytes = 0;
            int totalFiles = 0;
            int completed = 0;
            foreach (var definition in StorageCleanupCatalog.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StorageCleanupProgress(
                    StorageCleanupPhase.Scanning,
                    definition.Category,
                    completed,
                    StorageCleanupCatalog.All.Count,
                    totalBytes,
                    totalFiles));
                CleanupCounters counters = ProcessCategory(
                    definition.Category,
                    delete: false,
                    cancellationToken);
                categories[definition.Category] = new StorageCleanupCategoryScan(
                    definition.Category,
                    counters.Bytes,
                    counters.Files);
                totalBytes += counters.Bytes;
                totalFiles += counters.Files;
                completed++;
            }

            return new StorageCleanupScan(categories);
        }

        private static StorageCleanupRunResult CleanCore(
            IEnumerable<StorageCleanupCategory> categories,
            CancellationToken cancellationToken,
            IProgress<StorageCleanupProgress>? progress)
        {
            var selected = categories
                .Distinct()
                .Where(category => StorageCleanupCatalog.All.Any(item => item.Category == category))
                .ToList();

            var freedByCategory = new Dictionary<StorageCleanupCategory, long>();
            long totalBytes = 0;
            int totalFiles = 0;
            int totalSkipped = 0;
            int completed = 0;

            foreach (StorageCleanupCategory category in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StorageCleanupProgress(
                    StorageCleanupPhase.Cleaning,
                    category,
                    completed,
                    selected.Count,
                    totalBytes,
                    totalFiles));
                CleanupCounters counters = ProcessCategory(category, delete: true, cancellationToken);
                freedByCategory[category] = counters.Bytes;
                totalBytes += counters.Bytes;
                totalFiles += counters.Files;
                totalSkipped += counters.Skipped;
                completed++;
            }

            return new StorageCleanupRunResult(
                totalBytes,
                totalFiles,
                totalSkipped,
                freedByCategory);
        }

        private static CleanupCounters ProcessCategory(
            StorageCleanupCategory category,
            bool delete,
            CancellationToken cancellationToken)
        {
            var counters = new CleanupCounters();
            foreach (CleanupTarget target in BuildTargets(category))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessRoot(target, delete, cancellationToken, counters);
            }

            return counters;
        }

        private static IReadOnlyList<CleanupTarget> BuildTargets(StorageCleanupCategory category)
        {
            return category switch
            {
                StorageCleanupCategory.TempFiles => BuildTempTargets(),
                StorageCleanupCategory.BrowserCache => BuildBrowserTargets(),
                StorageCleanupCategory.GpuShaderCache => BuildGpuTargets(),
                StorageCleanupCategory.Prefetch => BuildPrefetchTargets(),
                _ => Array.Empty<CleanupTarget>()
            };
        }

        private static IReadOnlyList<CleanupTarget> BuildTempTargets()
        {
            var targets = new List<CleanupTarget>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            AddDirectoryTarget(targets, Path.GetTempPath(), recursive: true);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                AddDirectoryTarget(targets, Path.Combine(windows, "Temp"), recursive: true);
            }
            return DistinctTargets(targets);
        }

        private static IReadOnlyList<CleanupTarget> BuildBrowserTargets()
        {
            var targets = new List<CleanupTarget>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
            {
                return targets;
            }

            AddProfileTargets(
                targets,
                Path.Combine(local, "Google", "Chrome", "User Data"),
                Path.Combine("Cache", "Cache_Data"),
                "Code Cache",
                "GPUCache",
                "DawnCache");
            AddProfileTargets(
                targets,
                Path.Combine(local, "Microsoft", "Edge", "User Data"),
                Path.Combine("Cache", "Cache_Data"),
                "Code Cache",
                "GPUCache",
                "DawnCache");
            AddProfileTargets(
                targets,
                Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
                Path.Combine("Cache", "Cache_Data"),
                "Code Cache",
                "GPUCache");
            AddProfileTargets(
                targets,
                Path.Combine(local, "Opera Software"),
                Path.Combine("Cache", "Cache_Data"),
                "Code Cache",
                "GPUCache");
            AddProfileTargets(
                targets,
                Path.Combine(local, "Mozilla", "Firefox", "Profiles"),
                "cache2");

            return DistinctTargets(targets);
        }

        private static IReadOnlyList<CleanupTarget> BuildGpuTargets()
        {
            var targets = new List<CleanupTarget>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            AddDirectoryTarget(targets, Path.Combine(local, "D3DSCache"), recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "NVIDIA", "DXCache"), recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "NVIDIA", "GLCache"), recursive: true);
            AddDirectoryTarget(
                targets,
                Path.Combine(programData, "NVIDIA Corporation", "NV_Cache"),
                recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "AMD", "DxCache"), recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "AMD", "GLCache"), recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "AMD", "OglCache"), recursive: true);
            AddDirectoryTarget(targets, Path.Combine(local, "Intel", "ShaderCache"), recursive: true);

            return DistinctTargets(targets);
        }

        private static IReadOnlyList<CleanupTarget> BuildPrefetchTargets()
        {
            var targets = new List<CleanupTarget>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                AddDirectoryTarget(
                    targets,
                    Path.Combine(windows, "Prefetch"),
                    recursive: false,
                    prefetchOnly: true);
            }
            return DistinctTargets(targets);
        }

        private static void AddProfileTargets(
            List<CleanupTarget> targets,
            string profilesRoot,
            params string[] relativePaths)
        {
            if (string.IsNullOrWhiteSpace(profilesRoot))
            {
                return;
            }

            string root;
            try
            {
                root = Path.GetFullPath(profilesRoot);
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(root))
            {
                return;
            }

            List<string> profiles;
            try
            {
                profiles = Directory
                    .EnumerateDirectories(root, "*", EnumerateAll)
                    .ToList();
            }
            catch
            {
                return;
            }

            foreach (string profile in profiles)
            {
                if (IsReparsePoint(profile))
                {
                    continue;
                }

                foreach (string relativePath in relativePaths)
                {
                    string target = Path.Combine(profile, relativePath);
                    if (Directory.Exists(target))
                    {
                        AddDirectoryTarget(
                            targets,
                            target,
                            recursive: true,
                            browserPrivacy: true);
                    }
                }
            }
        }

        private static void AddDirectoryTarget(
            List<CleanupTarget> targets,
            string? path,
            bool recursive,
            bool prefetchOnly = false,
            bool browserPrivacy = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string root = NormalizeRoot(path);
                if (string.IsNullOrWhiteSpace(root) || !IsSafeCleanupRoot(root))
                {
                    return;
                }

                targets.Add(new CleanupTarget(root, recursive, prefetchOnly, browserPrivacy));
            }
            catch
            {
            }
        }

        private static IReadOnlyList<CleanupTarget> DistinctTargets(
            IEnumerable<CleanupTarget> targets)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var distinct = new List<CleanupTarget>();
            foreach (CleanupTarget target in targets)
            {
                if (seen.Add(target.RootPath))
                {
                    distinct.Add(target);
                }
            }

            return distinct;
        }

        private static void ProcessRoot(
            CleanupTarget target,
            bool delete,
            CancellationToken cancellationToken,
            CleanupCounters counters)
        {
            string root;
            try
            {
                root = NormalizeRoot(target.RootPath);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(root) ||
                !IsSafeCleanupRoot(root) ||
                !Directory.Exists(root) ||
                IsReparsePoint(root))
            {
                return;
            }

            if (target.PrefetchOnly)
            {
                ProcessPrefetchRoot(root, delete, cancellationToken, counters);
                return;
            }

            ProcessDirectory(
                root,
                root,
                target.Recursive,
                target.BrowserPrivacy,
                delete,
                cancellationToken,
                counters);
        }

        private static void ProcessPrefetchRoot(
            string root,
            bool delete,
            CancellationToken cancellationToken,
            CleanupCounters counters)
        {
            List<string> files;
            try
            {
                files = Directory
                    .EnumerateFiles(root, "*", EnumerateAll)
                    .ToList();
            }
            catch
            {
                return;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsPrefetchFile(file))
                {
                    ProcessFile(file, root, browserPrivacy: false, delete, cancellationToken, counters);
                }
            }
        }

        private static void ProcessDirectory(
            string directory,
            string root,
            bool recursive,
            bool browserPrivacy,
            bool delete,
            CancellationToken cancellationToken,
            CleanupCounters counters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPathWithinRoot(directory, root))
            {
                return;
            }

            if (browserPrivacy && ContainsPrivateBrowserSegment(directory))
            {
                return;
            }

            List<string> entries;
            try
            {
                entries = Directory
                    .EnumerateFileSystemEntries(directory, "*", EnumerateAll)
                    .ToList();
            }
            catch
            {
                return;
            }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsPathWithinRoot(entry, root))
                {
                    continue;
                }

                if (browserPrivacy && ContainsPrivateBrowserSegment(entry))
                {
                    continue;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recursive)
                    {
                        ProcessDirectory(
                            entry,
                            root,
                            recursive,
                            browserPrivacy,
                            delete,
                            cancellationToken,
                            counters);
                    }
                    continue;
                }

                ProcessFile(entry, root, browserPrivacy, delete, cancellationToken, counters);
            }

            if (delete && !IsSamePath(directory, root))
            {
                TryDeleteEmptyDirectory(directory);
            }
        }

        private static void ProcessFile(
            string path,
            string root,
            bool browserPrivacy,
            bool delete,
            CancellationToken cancellationToken,
            CleanupCounters counters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPathWithinRoot(path, root) || IsReparsePoint(path))
            {
                return;
            }

            if (browserPrivacy && ContainsPrivateBrowserSegment(path))
            {
                return;
            }

            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is SecurityException)
            {
                if (delete)
                {
                    counters.Skipped++;
                }
                return;
            }

            if (!delete)
            {
                counters.Bytes += length;
                counters.Files++;
                return;
            }

            try
            {
                File.Delete(path);
                counters.Bytes += length;
                counters.Files++;
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
                counters.Skipped++;
            }
            catch (UnauthorizedAccessException)
            {
                counters.Skipped++;
            }
            catch (SecurityException)
            {
                counters.Skipped++;
            }
        }

        private static void TryDeleteEmptyDirectory(string directory)
        {
            if (IsReparsePoint(directory))
            {
                return;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    return;
                }

                Directory.Delete(directory, recursive: false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SecurityException)
            {
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSamePath(string first, string second)
        {
            try
            {
                return string.Equals(
                    NormalizeRoot(first),
                    NormalizeRoot(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetProtectedCleanupRoots(string userProfile)
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                yield return Path.Combine(userProfile, "AppData", "LocalLow");
                string? usersRoot = GetUsersRoot(userProfile);
                if (usersRoot != null)
                {
                    yield return usersRoot;
                }
            }
        }

        private static string? GetUsersRoot(string userProfile)
        {
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return null;
            }

            try
            {
                return Path.GetDirectoryName(NormalizeRoot(userProfile));
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeRoot(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
    }
}
