using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CryoTray.Models;
using WinForms = System.Windows.Forms;

namespace CryoTray.Services
{
    public sealed record AutopilotState(
        bool IsActive,
        string ProcessName,
        string ProfileKey,
        bool IsGameMode);

    internal sealed record AutomationTarget(
        string ProcessName,
        string ProfileKey,
        bool IsGameMode);

    public sealed class AutopilotService : IDisposable
    {
        private static AutopilotService? _instance;
        public static AutopilotService Instance => _instance ??= new AutopilotService();

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2.5);

        private static readonly HashSet<string> ShellProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer",
            "CryoTray",
            "SearchHost",
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "ApplicationFrameHost",
            "LockApp"
        };

        private readonly object _stateLock = new();
        private Timer? _timer;
        private int _polling;
        private bool _pausedByAutopilot;
        private bool _lastShouldPause;
        private bool _automationActive;
        private bool _previousWasCooling;
        private string? _previousProfileKey;
        private string? _appliedProfileKey;
        private AutomationTarget? _activeTarget;
        private AutopilotState _currentState = new(false, "", "", false);
        private bool _disposed;

        public bool ShouldHoldPause
        {
            get
            {
                lock (_stateLock)
                {
                    return _pausedByAutopilot;
                }
            }
        }

        public bool IsPauseConditionActive
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastShouldPause;
                }
            }
        }

        public AutopilotState CurrentState
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentState;
                }
            }
        }

        public event Action<AutopilotState>? StateChanged;

        internal Func<bool>? ExternalPauseHoldCheck { get; set; }

        internal void TransferTimedPauseToAutopilot()
        {
            lock (_stateLock)
            {
                if (_lastShouldPause)
                {
                    _pausedByAutopilot = true;
                }
            }
        }

        private AutopilotService()
        {
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_disposed || _timer != null)
                {
                    return;
                }

                _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
            }
        }

        public void Stop()
        {
            Timer? timer;
            lock (_stateLock)
            {
                timer = _timer;
                _timer = null;
            }

            timer?.Dispose();
        }

        private void Poll()
        {
            if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
            {
                return;
            }

            try
            {
                Evaluate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Autopilot poll error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private void Evaluate()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutopilotEnabled)
            {
                if (IsAutomationActive())
                {
                    RestorePreviousProfile(settings);
                }

                lock (_stateLock)
                {
                    _pausedByAutopilot = false;
                    _lastShouldPause = false;
                }

                return;
            }

            var whitelist = settings.AutopilotWhitelist ?? new List<string>();
            bool hasWhitelist = whitelist.Any(entry => !string.IsNullOrWhiteSpace(entry));
            bool needProcessScan = hasWhitelist || settings.AutopilotPauseOnFullscreen;

            Dictionary<uint, string>? runningByPid = needProcessScan
                ? EnumerateProcessesByPidToolhelp32()
                : null;

            AutomationTarget? target = ResolveActiveTarget(
                settings, whitelist, runningByPid);

            if (target == null)
            {
                if (IsAutomationActive())
                {
                    RestorePreviousProfile(settings);
                }
                else
                {
                    lock (_stateLock)
                    {
                        _lastShouldPause = false;
                    }
                }

                return;
            }

            bool needsSwitch;
            lock (_stateLock)
            {
                needsSwitch = !_automationActive ||
                    _activeTarget == null ||
                    !TargetEquals(_activeTarget, target) ||
                    !string.Equals(_appliedProfileKey, target.ProfileKey, StringComparison.OrdinalIgnoreCase);

                if (needsSwitch && !_automationActive)
                {
                    var activeProfile = PowerPlanService.Instance.ActiveProfile;
                    _previousProfileKey = activeProfile?.Key;
                    _previousWasCooling = PowerPlanService.Instance.IsCoolingModeActive;
                    _automationActive = true;
                    _lastShouldPause = true;
                    _pausedByAutopilot = _previousWasCooling;
                }
                else if (needsSwitch)
                {
                    _lastShouldPause = true;
                }
            }

            if (!needsSwitch)
            {
                return;
            }

            if (!SwitchProfile(target.ProfileKey, settings))
            {
                return;
            }

            bool stateChanged;
            lock (_stateLock)
            {
                _activeTarget = target;
                _appliedProfileKey = target.ProfileKey;
                stateChanged = !_currentState.Equals(
                    new AutopilotState(true, target.ProcessName, target.ProfileKey, target.IsGameMode));
                _currentState = new AutopilotState(
                    true, target.ProcessName, target.ProfileKey, target.IsGameMode);
            }

            if (stateChanged)
            {
                StateChanged?.Invoke(CurrentState);
            }
        }

        private bool IsAutomationActive()
        {
            lock (_stateLock)
            {
                return _automationActive;
            }
        }

        private void RestorePreviousProfile(AppSettings settings)
        {
            string? previousKey;
            bool previousCooling;
            lock (_stateLock)
            {
                previousKey = _previousProfileKey;
                previousCooling = _previousWasCooling;
                _automationActive = false;
                _activeTarget = null;
                _appliedProfileKey = null;
                _lastShouldPause = false;
                _pausedByAutopilot = false;
                _currentState = new AutopilotState(false, "", "", false);
            }

            bool externalHold = ExternalPauseHoldCheck?.Invoke() == true;
            if (previousCooling && externalHold)
            {
                SwitchProfile(settings.PreferredGamingProfile, settings);
            }
            else if (!string.IsNullOrWhiteSpace(previousKey))
            {
                SwitchProfile(previousKey, settings);
            }
            else if (previousCooling)
            {
                SetCoolingMode(true);
            }

            StateChanged?.Invoke(CurrentState);
        }

        internal static AutomationTarget? ResolveActiveTarget(
            AppSettings settings,
            IReadOnlyCollection<string> whitelist,
            IReadOnlyDictionary<uint, string>? processesByPid)
        {
            string? foreground = GetForegroundProcessName(processesByPid);
            IReadOnlyCollection<string> running =
                (IReadOnlyCollection<string>?)processesByPid?.Values.ToList()
                ?? Array.Empty<string>();
            AutomationTarget? target = ResolveWhitelistTarget(
                settings, whitelist, foreground, running);
            if (target != null)
            {
                return target;
            }

            if (settings.AutopilotPauseOnFullscreen &&
                IsFullscreen3DOrBorderlessActive(processesByPid))
            {
                return new AutomationTarget(
                    "", settings.PreferredGamingProfile, true);
            }

            return null;
        }

        internal static AutomationTarget? ResolveWhitelistTarget(
            AppSettings settings,
            IReadOnlyCollection<string> whitelist,
            string? foreground,
            IReadOnlyCollection<string> runningNames)
        {
            string? matchedProcess = null;
            foreach (string entry in whitelist)
            {
                string normalized = NormalizeProcessName(entry);
                if (normalized.Length == 0)
                {
                    continue;
                }

                bool matches = settings.AutopilotForegroundOnly
                    ? string.Equals(
                        NormalizeProcessName(foreground),
                        normalized,
                        StringComparison.OrdinalIgnoreCase)
                    : runningNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);

                if (matches)
                {
                    matchedProcess = normalized;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(matchedProcess))
            {
                return null;
            }

            string profileKey = ResolveProfileKey(
                matchedProcess,
                settings.AutopilotAppProfiles,
                settings.PreferredGamingProfile,
                settings);
            return new AutomationTarget(matchedProcess, profileKey, false);
        }

        internal static string ResolveProfileKey(
            string processName,
            IReadOnlyDictionary<string, string>? profiles,
            string fallbackKey,
            AppSettings? settings = null)
        {
            string fallback = string.IsNullOrWhiteSpace(fallbackKey)
                ? PowerProfiles.BalancedKey
                : fallbackKey;

            if (profiles != null && !string.IsNullOrWhiteSpace(processName))
            {
                foreach (var pair in profiles)
                {
                    if (!string.Equals(
                        NormalizeProcessName(pair.Key),
                        NormalizeProcessName(processName),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (PowerProfiles.Resolve(pair.Value, settings) != null)
                    {
                        return pair.Value;
                    }
                }
            }

            return fallback;
        }

        private static bool TargetEquals(AutomationTarget left, AutomationTarget right)
        {
            return string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.ProfileKey, right.ProfileKey, StringComparison.OrdinalIgnoreCase) &&
                   left.IsGameMode == right.IsGameMode;
        }

        private static string? GetForegroundProcessName(
            IReadOnlyDictionary<uint, string>? processesByPid)
        {
            IntPtr fgWindow = NativeMethods.GetForegroundWindow();
            if (fgWindow == IntPtr.Zero)
            {
                return null;
            }

            _ = NativeMethods.GetWindowThreadProcessId(fgWindow, out uint fgPid);
            if (fgPid == 0 || fgPid == (uint)Environment.ProcessId)
            {
                return null;
            }

            var processMap = processesByPid ?? EnumerateProcessesByPidToolhelp32();
            return processMap.TryGetValue(fgPid, out string? processName)
                ? processName
                : null;
        }

        private static bool SwitchProfile(string profileKey, AppSettings settings)
        {
            PowerProfileDefinition? profile = PowerProfiles.Resolve(profileKey, settings);
            if (profile == null)
            {
                profile = PowerProfiles.GetGaming(settings.PreferredGamingProfile, settings);
            }

            var application = System.Windows.Application.Current;
            if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(new Action(() =>
                {
                    PowerPlanService.Instance.SwitchToProfile(profile);
                }));
                return true;
            }

            return PowerPlanService.Instance.SwitchToProfile(profile);
        }

        private static void SetCoolingMode(bool enable)
        {
            var application = System.Windows.Application.Current;
            if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (PowerPlanService.Instance.IsCoolingModeActive != enable)
                    {
                        PowerPlanService.Instance.SetCoolingMode(enable);
                    }
                }));
                return;
            }

            if (PowerPlanService.Instance.IsCoolingModeActive != enable)
            {
                PowerPlanService.Instance.SetCoolingMode(enable);
            }
        }

        private static bool IsAnyWhitelistedProcessRunning(
            IReadOnlyCollection<string> whitelist,
            IEnumerable<string> runningNames)
        {
            var targets = whitelist
                .Select(NormalizeProcessName)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (targets.Count == 0)
            {
                return false;
            }

            foreach (string processName in runningNames)
            {
                if (targets.Contains(processName))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsWhitelistedProcessName(
            string? processName,
            IReadOnlyCollection<string> whitelist)
        {
            string normalized = NormalizeProcessName(processName);
            if (normalized.Length == 0)
            {
                return false;
            }

            foreach (string entry in whitelist)
            {
                if (string.Equals(
                    NormalizeProcessName(entry),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsWhitelistedForegroundProcess(
            IReadOnlyCollection<string> whitelist,
            IReadOnlyDictionary<uint, string> processesByPid)
        {
            string? foreground = GetForegroundProcessName(processesByPid);
            return IsWhitelistedProcessName(foreground, whitelist);
        }

        /// <summary>Enumerates running process names keyed by PID with a single Toolhelp32 kernel snapshot.</summary>
        internal static Dictionary<uint, string> EnumerateProcessesByPidToolhelp32()
        {
            var result = new Dictionary<uint, string>();
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == NativeMethods.INVALID_HANDLE_VALUE)
            {
                return result;
            }

            try
            {
                var entry = new NativeMethods.PROCESSENTRY32W
                {
                    dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>()
                };

                if (!NativeMethods.Process32FirstW(snapshot, ref entry))
                {
                    return result;
                }

                uint currentPid = (uint)Environment.ProcessId;
                do
                {
                    if (entry.th32ProcessID != 0 && entry.th32ProcessID != currentPid)
                    {
                        string normalized = NormalizeProcessName(entry.szExeFile);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            result[entry.th32ProcessID] = normalized;
                        }
                    }
                }
                while (NativeMethods.Process32NextW(snapshot, ref entry));
            }
            finally
            {
                NativeMethods.CloseHandle(snapshot);
            }

            return result;
        }

        /// <summary>Detects exclusive Direct3D fullscreen or borderless fullscreen non-shell windows.</summary>
        internal static bool IsFullscreen3DOrBorderlessActive(IReadOnlyDictionary<uint, string>? processesByPid = null)
        {
            int state = 0;
            bool hasNotificationState = NativeMethods.SHQueryUserNotificationState(out state) == 0;
            if (hasNotificationState && state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN)
            {
                return true;
            }

            IntPtr fgWindow = NativeMethods.GetForegroundWindow();
            if (fgWindow == IntPtr.Zero)
            {
                return false;
            }

            _ = NativeMethods.GetWindowThreadProcessId(fgWindow, out uint fgPid);
            if (fgPid == 0 || fgPid == (uint)Environment.ProcessId)
            {
                return false;
            }

            var processMap = processesByPid ?? EnumerateProcessesByPidToolhelp32();
            if (processMap.TryGetValue(fgPid, out string? procName) &&
                ShellProcessNames.Contains(procName))
            {
                return false;
            }

            if (hasNotificationState &&
                (state == NativeMethods.QUNS_BUSY || state == NativeMethods.QUNS_PRESENTATION_MODE))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(fgWindow, out var rect))
            {
                return false;
            }

            var bounds = WinForms.Screen.FromHandle(fgWindow).Bounds;
            return rect.Left <= bounds.Left + 2 &&
                   rect.Top <= bounds.Top + 2 &&
                   rect.Right >= bounds.Right - 2 &&
                   rect.Bottom >= bounds.Bottom - 2;
        }

        private static string NormalizeProcessName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            try
            {
                return Path.GetFileNameWithoutExtension(value.Trim());
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }
}
