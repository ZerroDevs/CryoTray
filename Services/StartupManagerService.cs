using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CryoTray.Models;
using Microsoft.Win32;

namespace CryoTray.Services
{
    internal interface IStartupPlatformStore
    {
        IReadOnlyList<StartupItemRecord> DiscoverNativeEntries(CancellationToken cancellationToken);
        bool TrySetApprovedEnabled(StartupSourceKind sourceKind, string sourceName, string taskPath, bool enabled, out string errorMessage);
    }

    /// <summary>
    /// On-demand discovery and non-destructive state management for Windows startup items
    /// (Registry Run keys, Startup folders, non-Microsoft logon Scheduled Tasks, and CryoBoot delays).
    /// Performs zero background scanning unless opened or mutated.
    /// </summary>
    public sealed class StartupManagerService
    {
        private static readonly Lazy<StartupManagerService> LazyInstance = new(() => new StartupManagerService());
        public static StartupManagerService Instance => LazyInstance.Value;

        internal const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string Wow64RunSubKey = @"Software\Microsoft\WOW6432Node\Windows\CurrentVersion\Run";
        internal const string ApprovedRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        internal const string ApprovedRun32SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        internal const string ApprovedFolderSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        private readonly IStartupPlatformStore _store;
        private readonly IStartupProcessLauncher _launcher;
        private readonly object _sync = new();

        public event Action? EntriesChanged;

        public StartupManagerService()
            : this(new WindowsStartupPlatformStore(), UnelevatedProcessLauncher.Instance)
        {
        }

        internal StartupManagerService(
            IStartupPlatformStore store,
            IStartupProcessLauncher launcher)
        {
            _store = store;
            _launcher = launcher;
        }

        public static string BuildEntryId(StartupSourceKind sourceKind, string identifier)
        {
            string prefix = sourceKind switch
            {
                StartupSourceKind.RegistryCurrentUserRun => "Reg:HKCU:Run:",
                StartupSourceKind.RegistryLocalMachineRun => "Reg:HKLM:Run:",
                StartupSourceKind.RegistryLocalMachineRun32 => "Reg:HKLM:Run32:",
                StartupSourceKind.StartupFolderUser => "Folder:User:",
                StartupSourceKind.StartupFolderCommon => "Folder:Common:",
                StartupSourceKind.ScheduledTask => "Task:",
                _ => "Unknown:"
            };

            return prefix + (identifier ?? "").Trim();
        }

        public static bool DecodeApprovedEnabled(byte[]? rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
            {
                return true;
            }

            // Windows Task Manager / Shell convention: even byte (0x02 / 0x06) = Enabled; odd (0x01 / 0x03 / 0x07) = Disabled.
            return (rawBytes[0] & 1) == 0;
        }

        public static byte[] EncodeApprovedBytes(bool enabled, DateTime? timestampUtc = null)
        {
            byte[] data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;

            if (!enabled)
            {
                long fileTime = (timestampUtc ?? DateTime.UtcNow).ToFileTimeUtc();
                byte[] timeBytes = BitConverter.GetBytes(fileTime);
                Array.Copy(timeBytes, 0, data, 4, Math.Min(8, timeBytes.Length));
            }

            return data;
        }

        public static (string ExecutablePath, string Arguments) SplitCommandLine(
            string? commandLine,
            Func<string, bool>? fileExists = null)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return ("", "");
            }

            string trimmed = commandLine.Trim();
            fileExists ??= File.Exists;

            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = trimmed.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    string exe = Environment.ExpandEnvironmentVariables(trimmed[1..closingQuote].Trim());
                    string args = closingQuote + 1 < trimmed.Length
                        ? trimmed[(closingQuote + 1)..].Trim()
                        : "";
                    return (exe, args);
                }
            }

            string expanded = Environment.ExpandEnvironmentVariables(trimmed);

            // Check progressive whitespace boundaries when unquoted path contains spaces.
            int searchFrom = 0;
            while (searchFrom < expanded.Length)
            {
                int nextSpace = expanded.IndexOf(' ', searchFrom);
                if (nextSpace < 0)
                {
                    if (fileExists(expanded))
                    {
                        return (expanded, "");
                    }
                    break;
                }

                string candidate = expanded[..nextSpace].Trim();
                if (candidate.Length > 0 && fileExists(candidate))
                {
                    string args = expanded[(nextSpace + 1)..].Trim();
                    return (candidate, args);
                }

                searchFrom = nextSpace + 1;
            }

            // Fallback when file doesn't exist on disk (missing file or test mock).
            int exeExtIdx = expanded.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            if (exeExtIdx > 0)
            {
                string exe = expanded[..(exeExtIdx + 4)].Trim();
                string args = expanded[(exeExtIdx + 5)..].Trim();
                return (exe, args);
            }

            int firstSpace = expanded.IndexOf(' ');
            if (firstSpace > 0)
            {
                return (expanded[..firstSpace].Trim(), expanded[(firstSpace + 1)..].Trim());
            }

            return (expanded, "");
        }

        internal static bool TryParseLogonTaskXml(
            string xmlText,
            out bool hasLogonTrigger,
            out bool enabled,
            out string command,
            out string arguments,
            out string workingDirectory,
            out bool requiresHighestPrivilege)
        {
            hasLogonTrigger = false;
            enabled = true;
            command = "";
            arguments = "";
            workingDirectory = "";
            requiresHighestPrivilege = false;

            if (string.IsNullOrWhiteSpace(xmlText))
            {
                return false;
            }

            try
            {
                XDocument doc = XDocument.Parse(xmlText);
                XElement? root = doc.Root;
                if (root == null)
                {
                    return false;
                }

                hasLogonTrigger = root.Descendants().Any(e =>
                    e.Name.LocalName.Equals("LogonTrigger", StringComparison.Ordinal));
                if (!hasLogonTrigger)
                {
                    return false;
                }

                XElement? enabledEl = root
                    .Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("Settings", StringComparison.Ordinal))?
                    .Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("Enabled", StringComparison.Ordinal));
                if (enabledEl != null && bool.TryParse(enabledEl.Value.Trim(), out bool parsedEnabled))
                {
                    enabled = parsedEnabled;
                }

                XElement? runLevelEl = root.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("RunLevel", StringComparison.Ordinal));
                if (runLevelEl != null &&
                    runLevelEl.Value.Trim().Equals("HighestAvailable", StringComparison.OrdinalIgnoreCase))
                {
                    requiresHighestPrivilege = true;
                }

                XElement? execEl = root.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("Exec", StringComparison.Ordinal));
                if (execEl == null)
                {
                    return false;
                }

                command = execEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("Command", StringComparison.Ordinal))?
                    .Value.Trim() ?? "";
                arguments = execEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("Arguments", StringComparison.Ordinal))?
                    .Value.Trim() ?? "";
                workingDirectory = execEl.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("WorkingDirectory", StringComparison.Ordinal))?
                    .Value.Trim() ?? "";

                return !string.IsNullOrWhiteSpace(command);
            }
            catch
            {
                return false;
            }
        }

        public Task<IReadOnlyList<StartupItemRecord>> DiscoverAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => DiscoverAllCore(cancellationToken), cancellationToken);
        }

        internal IReadOnlyList<StartupItemRecord> DiscoverAllCore(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<StartupItemRecord> nativeItems = _store.DiscoverNativeEntries(cancellationToken);
            AppSettings settings = SettingsService.Instance.Current;
            var delayedById = (settings.CryoBootEntries ?? new List<StartupDelaySetting>())
                .Where(d => !string.IsNullOrWhiteSpace(d.EntryId))
                .GroupBy(d => d.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var runtimeStates = StartupDelayService.Instance.GetRuntimeStatesSnapshot();
            var publisherCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<StartupItemRecord>(nativeItems.Count + delayedById.Count);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (StartupItemRecord item in nativeItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seenIds.Add(item.EntryId);

                if (string.IsNullOrWhiteSpace(item.Publisher) && !string.IsNullOrWhiteSpace(item.ExecutablePath))
                {
                    item.Publisher = ReadPublisherCached(item.ExecutablePath, publisherCache);
                }

                if (!item.IsReadOnly && delayedById.TryGetValue(item.EntryId, out StartupDelaySetting? delayed))
                {
                    item.Mode = StartupEntryMode.Delayed;
                    item.DelaySeconds = ClampDelaySeconds(delayed.DelaySeconds);
                    item.PriorityOrder = Math.Max(1, delayed.PriorityOrder);
                    item.WaitForIdle = delayed.WaitForIdle;
                }
                else
                {
                    item.DelaySeconds = ClampDelaySeconds(settings.CryoBootDefaultDelaySeconds);
                    item.PriorityOrder = Math.Max(1, delayedById.Count + 1);
                    item.WaitForIdle = settings.CryoBootWaitForSystemIdle;
                }

                if (runtimeStates.TryGetValue(item.EntryId, out var runtime))
                {
                    item.RuntimeState = runtime.State;
                    item.LastError = runtime.Error;
                }

                merged.Add(item);
            }

            // Include orphaned CryoBoot entries if their native key moved/disappeared so user can still clean them up.
            foreach (StartupDelaySetting delayed in delayedById.Values.OrderBy(d => d.PriorityOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seenIds.Contains(delayed.EntryId))
                {
                    continue;
                }

                runtimeStates.TryGetValue(delayed.EntryId, out var runtime);
                merged.Add(new StartupItemRecord
                {
                    EntryId = delayed.EntryId,
                    Name = string.IsNullOrWhiteSpace(delayed.DisplayName) ? delayed.EntryId : delayed.DisplayName,
                    Publisher = ReadPublisherCached(delayed.ExecutablePath, publisherCache),
                    CommandLine = delayed.CommandLine,
                    ExecutablePath = delayed.ExecutablePath,
                    Arguments = delayed.Arguments,
                    WorkingDirectory = delayed.WorkingDirectory,
                    SourceKind = delayed.SourceKind,
                    SourceName = delayed.SourceName,
                    TaskPath = delayed.TaskPath,
                    RequiresElevation = delayed.RequiresElevation,
                    Mode = StartupEntryMode.Delayed,
                    NativeWindowsMode = StartupEntryMode.Disabled,
                    DelaySeconds = ClampDelaySeconds(delayed.DelaySeconds),
                    PriorityOrder = Math.Max(1, delayed.PriorityOrder),
                    WaitForIdle = delayed.WaitForIdle,
                    RuntimeState = runtime.State,
                    LastError = runtime.Error
                });
            }

            return merged
                .OrderBy(r => r.Mode == StartupEntryMode.Delayed ? 0 : r.Mode == StartupEntryMode.Enabled ? 1 : 2)
                .ThenBy(r => r.PriorityOrder)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public bool TrySetEntryMode(
            StartupItemRecord item,
            StartupEntryMode targetMode,
            out string errorMessage,
            int? delaySeconds = null,
            bool? waitForIdle = null)
        {
            errorMessage = "";
            if (item == null || string.IsNullOrWhiteSpace(item.EntryId))
            {
                errorMessage = "Invalid startup item.";
                return false;
            }

            if (item.IsReadOnly)
            {
                errorMessage = LocalizationService.Get("StartupManager_ReadOnlySelf");
                return false;
            }

            bool windowsShouldRunNatively = targetMode == StartupEntryMode.Enabled;

            lock (_sync)
            {
                if (!_store.TrySetApprovedEnabled(
                        item.SourceKind,
                        item.SourceName,
                        item.TaskPath,
                        windowsShouldRunNatively,
                        out errorMessage))
                {
                    return false;
                }

                AppSettings settings = SettingsService.Instance.Current;
                settings.CryoBootEntries ??= new List<StartupDelaySetting>();

                if (targetMode == StartupEntryMode.Delayed)
                {
                    StartupDelaySetting? existing = settings.CryoBootEntries.FirstOrDefault(e =>
                        string.Equals(e.EntryId, item.EntryId, StringComparison.OrdinalIgnoreCase));

                    int resolvedDelay = ClampDelaySeconds(delaySeconds ?? item.DelaySeconds);
                    bool resolvedWaitForIdle = waitForIdle ?? item.WaitForIdle;

                    if (existing == null)
                    {
                        int nextPriority = settings.CryoBootEntries.Count == 0
                            ? 1
                            : settings.CryoBootEntries.Max(e => e.PriorityOrder) + 1;
                        existing = new StartupDelaySetting
                        {
                            EntryId = item.EntryId,
                            PriorityOrder = nextPriority
                        };
                        settings.CryoBootEntries.Add(existing);
                    }

                    existing.DisplayName = item.Name;
                    existing.CommandLine = item.CommandLine;
                    existing.ExecutablePath = item.ExecutablePath;
                    existing.Arguments = item.Arguments;
                    existing.WorkingDirectory = item.WorkingDirectory;
                    existing.SourceKind = item.SourceKind;
                    existing.SourceName = item.SourceName;
                    existing.TaskPath = item.TaskPath;
                    existing.RequiresElevation = item.RequiresElevation;
                    existing.DelaySeconds = resolvedDelay;
                    existing.WaitForIdle = resolvedWaitForIdle;
                    existing.AutoRestoreOnUninstall = true;

                    item.DelaySeconds = resolvedDelay;
                    item.PriorityOrder = existing.PriorityOrder;
                    item.WaitForIdle = resolvedWaitForIdle;
                    item.NativeWindowsMode = StartupEntryMode.Disabled;
                }
                else
                {
                    settings.CryoBootEntries.RemoveAll(e =>
                        string.Equals(e.EntryId, item.EntryId, StringComparison.OrdinalIgnoreCase));
                    NormalizePriorities(settings.CryoBootEntries);
                    item.NativeWindowsMode = targetMode;
                }

                item.Mode = targetMode;
                SettingsService.Instance.Save();
                ActivityLogService.Instance.RecordDetail(
                    ActivitySeverity.Info,
                    "startup",
                    "Activity_CryoBootModeChanged",
                    $"{item.Name} -> {targetMode}");
            }

            EntriesChanged?.Invoke();
            return true;
        }

        public bool UpdateDelayedOptions(
            string entryId,
            int delaySeconds,
            bool waitForIdle)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            lock (_sync)
            {
                AppSettings settings = SettingsService.Instance.Current;
                StartupDelaySetting? entry = settings.CryoBootEntries?.FirstOrDefault(e =>
                    string.Equals(e.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return false;
                }

                entry.DelaySeconds = ClampDelaySeconds(delaySeconds);
                entry.WaitForIdle = waitForIdle;
                SettingsService.Instance.Save();
            }

            EntriesChanged?.Invoke();
            return true;
        }

        public bool MoveDelayedPriority(string entryId, int delta)
        {
            if (string.IsNullOrWhiteSpace(entryId) || delta == 0)
            {
                return false;
            }

            lock (_sync)
            {
                var list = (SettingsService.Instance.Current.CryoBootEntries ?? new List<StartupDelaySetting>())
                    .OrderBy(e => e.PriorityOrder)
                    .ToList();
                NormalizePriorities(list);

                int index = list.FindIndex(e => string.Equals(e.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                int targetIndex = index + Math.Sign(delta);
                if (index < 0 || targetIndex < 0 || targetIndex >= list.Count)
                {
                    return false;
                }

                (list[index], list[targetIndex]) = (list[targetIndex], list[index]);
                NormalizePriorities(list);
                SettingsService.Instance.Current.CryoBootEntries = list;
                SettingsService.Instance.Save();
            }

            EntriesChanged?.Invoke();
            return true;
        }

        public void RestoreAllDelayedEntriesForUninstall()
        {
            lock (_sync)
            {
                AppSettings settings = SettingsService.Instance.Current;
                var delayed = settings.CryoBootEntries;
                if (delayed == null || delayed.Count == 0)
                {
                    StartupDelayService.DeleteBootSessionMarker();
                    return;
                }

                foreach (StartupDelaySetting entry in delayed.ToList())
                {
                    if (!entry.AutoRestoreOnUninstall)
                    {
                        continue;
                    }

                    _store.TrySetApprovedEnabled(
                        entry.SourceKind,
                        entry.SourceName,
                        entry.TaskPath,
                        enabled: true,
                        out _);
                }

                settings.CryoBootEntries.Clear();
                SettingsService.Instance.Save();
                StartupDelayService.DeleteBootSessionMarker();
            }
        }

        public bool LaunchItemNow(StartupItemRecord item, out string errorMessage)
        {
            if (item == null)
            {
                errorMessage = "Invalid startup item.";
                return false;
            }

            bool ok = _launcher.TryLaunch(
                item.ExecutablePath,
                item.Arguments,
                item.WorkingDirectory,
                item.RequiresElevation,
                out errorMessage);

            if (ok)
            {
                ActivityLogService.Instance.RecordDetail(
                    ActivitySeverity.Info,
                    "startup",
                    "Activity_CryoBootLaunchedNow",
                    item.Name);
            }

            return ok;
        }

        public static int ClampDelaySeconds(int seconds) => Math.Clamp(seconds, 5, 300);

        private static void NormalizePriorities(List<StartupDelaySetting> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].PriorityOrder = i + 1;
            }
        }

        private static string ReadPublisherCached(string? executablePath, Dictionary<string, string> cache)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return "";
            }

            string expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
            if (cache.TryGetValue(expanded, out string? cached))
            {
                return cached;
            }

            string publisher = "";
            try
            {
                if (File.Exists(expanded))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(expanded);
                    publisher = (info.CompanyName ?? "").Trim();
                }
            }
            catch
            {
                publisher = "";
            }

            cache[expanded] = publisher;
            return publisher;
        }

        private sealed class WindowsStartupPlatformStore : IStartupPlatformStore
        {
            public IReadOnlyList<StartupItemRecord> DiscoverNativeEntries(CancellationToken cancellationToken)
            {
                var results = new List<StartupItemRecord>();

                ReadRegistryRunKey(
                    RegistryHive.CurrentUser,
                    RunSubKey,
                    ApprovedRunSubKey,
                    StartupSourceKind.RegistryCurrentUserRun,
                    results,
                    cancellationToken);

                ReadRegistryRunKey(
                    RegistryHive.LocalMachine,
                    RunSubKey,
                    ApprovedRunSubKey,
                    StartupSourceKind.RegistryLocalMachineRun,
                    results,
                    cancellationToken);

                ReadRegistryRunKey(
                    RegistryHive.LocalMachine,
                    Wow64RunSubKey,
                    ApprovedRun32SubKey,
                    StartupSourceKind.RegistryLocalMachineRun32,
                    results,
                    cancellationToken);

                ReadStartupFolder(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    RegistryHive.CurrentUser,
                    StartupSourceKind.StartupFolderUser,
                    results,
                    cancellationToken);

                ReadStartupFolder(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                    RegistryHive.LocalMachine,
                    StartupSourceKind.StartupFolderCommon,
                    results,
                    cancellationToken);

                ReadScheduledLogonTasks(results, cancellationToken);
                return results;
            }

            public bool TrySetApprovedEnabled(
                StartupSourceKind sourceKind,
                string sourceName,
                string taskPath,
                bool enabled,
                out string errorMessage)
            {
                errorMessage = "";
                try
                {
                    if (sourceKind == StartupSourceKind.ScheduledTask)
                    {
                        return TrySetScheduledTaskEnabled(taskPath, enabled, out errorMessage);
                    }

                    (RegistryHive hive, string approvedSubKey) = sourceKind switch
                    {
                        StartupSourceKind.RegistryCurrentUserRun => (RegistryHive.CurrentUser, ApprovedRunSubKey),
                        StartupSourceKind.RegistryLocalMachineRun => (RegistryHive.LocalMachine, ApprovedRunSubKey),
                        StartupSourceKind.RegistryLocalMachineRun32 => (RegistryHive.LocalMachine, ApprovedRun32SubKey),
                        StartupSourceKind.StartupFolderUser => (RegistryHive.CurrentUser, ApprovedFolderSubKey),
                        StartupSourceKind.StartupFolderCommon => (RegistryHive.LocalMachine, ApprovedFolderSubKey),
                        _ => (RegistryHive.CurrentUser, ApprovedRunSubKey)
                    };

                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var approvedKey = baseKey.CreateSubKey(approvedSubKey, writable: true);
                    if (approvedKey == null)
                    {
                        errorMessage = LocalizationService.Get("StartupManager_AccessDenied");
                        return false;
                    }

                    approvedKey.SetValue(sourceName, EncodeApprovedBytes(enabled), RegistryValueKind.Binary);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    errorMessage = LocalizationService.Get("StartupManager_AccessDenied");
                    return false;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    return false;
                }
            }

            private static void ReadRegistryRunKey(
                RegistryHive hive,
                string runSubKey,
                string approvedSubKey,
                StartupSourceKind kind,
                List<StartupItemRecord> results,
                CancellationToken cancellationToken)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var runKey = baseKey.OpenSubKey(runSubKey, writable: false);
                    if (runKey == null)
                    {
                        return;
                    }

                    using var approvedKey = baseKey.OpenSubKey(approvedSubKey, writable: false);
                    foreach (string valueName in runKey.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(valueName))
                        {
                            continue;
                        }

                        string? commandLine = runKey.GetValue(valueName) as string;
                        if (string.IsNullOrWhiteSpace(commandLine))
                        {
                            continue;
                        }

                        byte[]? approvedRaw = approvedKey?.GetValue(valueName) as byte[];
                        bool enabled = DecodeApprovedEnabled(approvedRaw);
                        var (exePath, args) = SplitCommandLine(commandLine);

                        results.Add(new StartupItemRecord
                        {
                            EntryId = BuildEntryId(kind, valueName),
                            Name = CleanDisplayName(valueName, exePath),
                            CommandLine = commandLine,
                            ExecutablePath = exePath,
                            Arguments = args,
                            WorkingDirectory = SafeParentDir(exePath),
                            SourceKind = kind,
                            SourceName = valueName,
                            Mode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled,
                            NativeWindowsMode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled
                        });
                    }
                }
                catch
                {
                }
            }

            private static void ReadStartupFolder(
                string folderPath,
                RegistryHive hive,
                StartupSourceKind kind,
                List<StartupItemRecord> results,
                CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return;
                }

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var approvedKey = baseKey.OpenSubKey(ApprovedFolderSubKey, writable: false);

                    foreach (string file in Directory.EnumerateFiles(folderPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fileName = Path.GetFileName(file);
                        if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        FileAttributes attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.System) != 0)
                        {
                            continue;
                        }

                        string targetExe = file;
                        string args = "";
                        string workDir = folderPath;

                        if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            ResolveShortcut(file, out string lnkTarget, out string lnkArgs, out string lnkDir);
                            if (!string.IsNullOrWhiteSpace(lnkTarget))
                            {
                                targetExe = lnkTarget;
                                args = lnkArgs;
                                workDir = !string.IsNullOrWhiteSpace(lnkDir) ? lnkDir : SafeParentDir(lnkTarget);
                            }
                        }

                        byte[]? approvedRaw = approvedKey?.GetValue(fileName) as byte[];
                        bool enabled = DecodeApprovedEnabled(approvedRaw);

                        results.Add(new StartupItemRecord
                        {
                            EntryId = BuildEntryId(kind, fileName),
                            Name = Path.GetFileNameWithoutExtension(fileName),
                            CommandLine = string.IsNullOrEmpty(args) ? targetExe : $"\"{targetExe}\" {args}",
                            ExecutablePath = targetExe,
                            Arguments = args,
                            WorkingDirectory = workDir,
                            SourceKind = kind,
                            SourceName = fileName,
                            Mode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled,
                            NativeWindowsMode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled
                        });
                    }
                }
                catch
                {
                }
            }

            private static void ReadScheduledLogonTasks(
                List<StartupItemRecord> results,
                CancellationToken cancellationToken)
            {
                try
                {
                    Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                    if (serviceType == null)
                    {
                        return;
                    }

                    dynamic? service = Activator.CreateInstance(serviceType);
                    if (service == null)
                    {
                        return;
                    }

                    try
                    {
                        service.Connect();
                        dynamic rootFolder = service.GetFolder(@"\");
                        EnumerateTaskFolder(rootFolder, true, results, cancellationToken);
                    }
                    finally
                    {
                        if (Marshal.IsComObject(service))
                        {
                            Marshal.ReleaseComObject(service);
                        }
                    }
                }
                catch
                {
                }
            }

            private static void EnumerateTaskFolder(
                dynamic folder,
                bool isRoot,
                List<StartupItemRecord> results,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic? tasks = null;
                dynamic? subFolders = null;

                try
                {
                    tasks = folder.GetTasks(0);
                    int count = tasks.Count;
                    for (int i = 1; i <= count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dynamic? task = null;
                        try
                        {
                            task = tasks[i];
                            string path = task.Path;
                            if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string xml = task.Xml;
                            if (!TryParseLogonTaskXml(
                                    xml,
                                    out _,
                                    out _,
                                    out string command,
                                    out string arguments,
                                    out string workDir,
                                    out bool requiresHighest))
                            {
                                continue;
                            }

                            string name = task.Name;
                            bool isCryoTrayTask = name.Equals(StartupTaskService.TaskName, StringComparison.OrdinalIgnoreCase);
                            bool enabled = task.Enabled;
                            string expandedExe = Environment.ExpandEnvironmentVariables(command);

                            results.Add(new StartupItemRecord
                            {
                                EntryId = BuildEntryId(StartupSourceKind.ScheduledTask, path),
                                Name = name,
                                CommandLine = string.IsNullOrEmpty(arguments) ? expandedExe : $"\"{expandedExe}\" {arguments}",
                                ExecutablePath = expandedExe,
                                Arguments = arguments,
                                WorkingDirectory = !string.IsNullOrWhiteSpace(workDir) ? Environment.ExpandEnvironmentVariables(workDir) : SafeParentDir(expandedExe),
                                SourceKind = StartupSourceKind.ScheduledTask,
                                SourceName = name,
                                TaskPath = path,
                                RequiresElevation = requiresHighest,
                                IsReadOnly = isCryoTrayTask,
                                ReadOnlyReasonKey = isCryoTrayTask ? "StartupManager_ManagedInSettings" : "",
                                Mode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled,
                                NativeWindowsMode = enabled ? StartupEntryMode.Enabled : StartupEntryMode.Disabled
                            });
                        }
                        finally
                        {
                            if (task != null && Marshal.IsComObject(task))
                            {
                                Marshal.ReleaseComObject(task);
                            }
                        }
                    }

                    subFolders = folder.GetFolders(0);
                    int subCount = subFolders.Count;
                    for (int i = 1; i <= subCount; i++)
                    {
                        dynamic? sub = null;
                        try
                        {
                            sub = subFolders[i];
                            string subPath = sub.Path;
                            if (isRoot && subPath.Equals(@"\Microsoft", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            EnumerateTaskFolder(sub, false, results, cancellationToken);
                        }
                        finally
                        {
                            if (sub != null && Marshal.IsComObject(sub))
                            {
                                Marshal.ReleaseComObject(sub);
                            }
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    if (subFolders != null && Marshal.IsComObject(subFolders))
                    {
                        Marshal.ReleaseComObject(subFolders);
                    }
                    if (tasks != null && Marshal.IsComObject(tasks))
                    {
                        Marshal.ReleaseComObject(tasks);
                    }
                }
            }

            private static bool TrySetScheduledTaskEnabled(string taskPath, bool enabled, out string errorMessage)
            {
                errorMessage = "";
                if (string.IsNullOrWhiteSpace(taskPath))
                {
                    errorMessage = "Missing scheduled task path.";
                    return false;
                }

                try
                {
                    Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                    if (serviceType == null)
                    {
                        errorMessage = "Task Scheduler service unavailable.";
                        return false;
                    }

                    dynamic? service = Activator.CreateInstance(serviceType);
                    if (service == null)
                    {
                        errorMessage = "Could not create Schedule.Service instance.";
                        return false;
                    }

                    dynamic? rootFolder = null;
                    dynamic? task = null;
                    try
                    {
                        service.Connect();
                        rootFolder = service.GetFolder(@"\");
                        task = rootFolder.GetTask(taskPath.TrimStart('\\'));
                        task.Enabled = enabled;
                        return true;
                    }
                    finally
                    {
                        if (task != null && Marshal.IsComObject(task)) Marshal.ReleaseComObject(task);
                        if (rootFolder != null && Marshal.IsComObject(rootFolder)) Marshal.ReleaseComObject(rootFolder);
                        if (Marshal.IsComObject(service)) Marshal.ReleaseComObject(service);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    errorMessage = LocalizationService.Get("StartupManager_AccessDenied");
                    return false;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    return false;
                }
            }

            private static void ResolveShortcut(
                string shortcutPath,
                out string targetPath,
                out string arguments,
                out string workingDirectory)
            {
                targetPath = "";
                arguments = "";
                workingDirectory = "";

                object? shellLinkObj = null;
                try
                {
                    shellLinkObj = new NativeMethods.ShellLink();
                    var link = (NativeMethods.IShellLinkW)shellLinkObj;
                    var file = (NativeMethods.IPersistFile)shellLinkObj;
                    file.Load(shortcutPath, 0);

                    var pathSb = new StringBuilder(520);
                    var argsSb = new StringBuilder(1024);
                    var dirSb = new StringBuilder(520);

                    link.GetPath(pathSb, pathSb.Capacity, IntPtr.Zero, 0);
                    link.GetArguments(argsSb, argsSb.Capacity);
                    link.GetWorkingDirectory(dirSb, dirSb.Capacity);

                    targetPath = Environment.ExpandEnvironmentVariables(pathSb.ToString().Trim());
                    arguments = argsSb.ToString().Trim();
                    workingDirectory = Environment.ExpandEnvironmentVariables(dirSb.ToString().Trim());
                }
                catch
                {
                }
                finally
                {
                    if (shellLinkObj != null && Marshal.IsComObject(shellLinkObj))
                    {
                        Marshal.ReleaseComObject(shellLinkObj);
                    }
                }
            }

            private static string CleanDisplayName(string valueName, string executablePath)
            {
                string trimmed = (valueName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }

                return string.IsNullOrWhiteSpace(executablePath)
                    ? "Startup Item"
                    : Path.GetFileNameWithoutExtension(executablePath);
            }

            private static string SafeParentDir(string? executablePath)
            {
                try
                {
                    return string.IsNullOrWhiteSpace(executablePath)
                        ? ""
                        : Path.GetDirectoryName(executablePath) ?? "";
                }
                catch
                {
                    return "";
                }
            }
        }
    }
}
