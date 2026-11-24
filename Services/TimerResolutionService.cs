using System;
using System.Runtime.InteropServices;
using CryoTray.Models;
using Microsoft.Win32;

namespace CryoTray.Services
{
    public enum TimerResolutionMode
    {
        Off,
        AutopilotOnly,
        AlwaysOn
    }

    public sealed record TimerResolutionStatus(
        TimerResolutionMode Mode,
        double MinimumMs,
        double MaximumMs,
        double CurrentMs,
        double? RequestedMs,
        bool IsHolding,
        bool IsPausedOnBattery,
        bool OnBattery,
        bool IsWindows11,
        bool GlobalRequestsEnabled);

    /// <summary>
    /// Opt-in process timer-resolution requests. Every request is paired with an explicit release
    /// on mode changes, game exit, battery power, and application shutdown.
    /// </summary>
    public sealed class TimerResolutionService : IDisposable
    {
        private const uint RequestedHalfMs = 5000;
        private const uint RequestedOneMs = 10000;
        private const string GlobalRequestsKeyPath =
            @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
        private const string GlobalRequestsValueName = "GlobalTimerResolutionRequests";

        private static TimerResolutionService? _instance;
        public static TimerResolutionService Instance => _instance ??= new TimerResolutionService();

        private readonly object _sync = new();
        private bool _started;
        private bool _disposed;
        private bool _isHoldingResolution;
        private bool _pausedOnBattery;
        private uint _lastRequested;

        public event Action? StatusChanged;

        private TimerResolutionService()
        {
        }

        public TimerResolutionStatus GetStatus()
        {
            bool onBattery = IsOnBattery();
            bool querySucceeded = TryQuery(out uint minimum, out uint maximum, out uint current);
            TimerResolutionMode mode = ParseMode(SettingsService.Instance.Current.TimerResolutionMode);

            lock (_sync)
            {
                return new TimerResolutionStatus(
                    mode,
                    querySucceeded ? ToMilliseconds(minimum) : double.NaN,
                    querySucceeded ? ToMilliseconds(maximum) : double.NaN,
                    querySucceeded ? ToMilliseconds(current) : double.NaN,
                    _isHoldingResolution ? ToMilliseconds(_lastRequested) : null,
                    _isHoldingResolution,
                    _pausedOnBattery,
                    onBattery,
                    IsWindows11,
                    IsGlobalRequestsEnabled());
            }
        }

        public void Start()
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            AutopilotService.Instance.StateChanged += OnAutopilotStateChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            ApplyCore();
        }

        public void Refresh()
        {
            if (!_started || _disposed)
            {
                return;
            }

            ApplyCore();
        }

        /// <summary>Releases the NT timer request synchronously; safe to call more than once.</summary>
        public void ReleaseForExit()
        {
            bool released;
            lock (_sync)
            {
                released = ReleaseLocked();
                _pausedOnBattery = false;
            }

            if (released)
            {
                RaiseStatusChanged();
            }
        }

        private void OnSettingsChanged()
        {
            ApplyCore();
        }

        private void OnAutopilotStateChanged(AutopilotState _)
        {
            ApplyCore();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange || e.Mode == PowerModes.Resume)
            {
                ApplyCore();
            }
        }

        private void ApplyCore()
        {
            if (!_started || _disposed)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            TimerResolutionMode mode = ParseMode(settings.TimerResolutionMode);
            bool aimingForResolution = mode == TimerResolutionMode.AlwaysOn ||
                (mode == TimerResolutionMode.AutopilotOnly &&
                 AutopilotService.Instance.CurrentState.IsActive);
            bool onBattery = IsOnBattery();
            bool pauseForBattery = settings.TimerResolutionPauseOnBattery && onBattery;
            bool shouldHold = aimingForResolution && !pauseForBattery;

            bool changed;
            lock (_sync)
            {
                bool previousHolding = _isHoldingResolution;
                double? previousRequested = _isHoldingResolution ? ToMilliseconds(_lastRequested) : null;
                bool previousPaused = _pausedOnBattery;

                if (shouldHold && TryQuery(out uint minimum, out uint maximum, out _))
                {
                    uint desired = NormalizeTarget(settings.TimerResolutionHundredNs);
                    desired = ClampToHardware(desired, minimum, maximum);

                    if (!_isHoldingResolution || _lastRequested != desired)
                    {
                        ReleaseLocked();
                        if (TrySetResolution(desired, true))
                        {
                            _lastRequested = desired;
                            _isHoldingResolution = true;
                        }
                    }
                }
                else
                {
                    ReleaseLocked();
                }

                _pausedOnBattery = pauseForBattery && aimingForResolution;

                changed = previousHolding != _isHoldingResolution ||
                    previousRequested != (_isHoldingResolution ? ToMilliseconds(_lastRequested) : null) ||
                    previousPaused != _pausedOnBattery;
            }

            if (changed)
            {
                RaiseStatusChanged();
            }
        }

        private bool ReleaseLocked()
        {
            if (!_isHoldingResolution)
            {
                return false;
            }

            // A failed release is harmless at process exit, where Windows drops the process request.
            // Mark it released so a guarded Off state cannot retry on every UI refresh.
            TrySetResolution(_lastRequested, false);
            _lastRequested = 0;
            _isHoldingResolution = false;
            return true;
        }

        private static bool TrySetResolution(uint desired, bool enabled)
        {
            try
            {
                int status = NtSetTimerResolution(desired, enabled, out _);
                return status == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryQuery(out uint minimum, out uint maximum, out uint current)
        {
            try
            {
                return NtQueryTimerResolution(out minimum, out maximum, out current) == 0 &&
                    minimum > 0 && maximum > 0;
            }
            catch
            {
                minimum = 0;
                maximum = 0;
                current = 0;
                return false;
            }
        }

        internal static uint NormalizeTarget(int hundredNanoseconds)
        {
            return hundredNanoseconds == RequestedHalfMs ? RequestedHalfMs : RequestedOneMs;
        }

        internal static uint ClampToHardware(uint desired, uint minimum, uint maximum)
        {
            uint low = Math.Min(minimum, maximum);
            uint high = Math.Max(minimum, maximum);
            return Math.Clamp(desired, low, high);
        }

        internal static double ToMilliseconds(uint hundredNanoseconds)
        {
            return hundredNanoseconds / 10000.0;
        }

        internal static TimerResolutionMode ParseMode(string? value)
        {
            return value switch
            {
                "AutopilotOnly" => TimerResolutionMode.AutopilotOnly,
                "AlwaysOn" => TimerResolutionMode.AlwaysOn,
                _ => TimerResolutionMode.Off
            };
        }

        internal static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        public static bool IsGlobalRequestsEnabled()
        {
            if (!IsWindows11)
            {
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(GlobalRequestsKeyPath, writable: false);
                object? value = key?.GetValue(GlobalRequestsValueName);
                return value is int number && number != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Explicit user action only. Toggling this registry value changes Windows' global timer
        /// request policy and requires a reboot; it is never called automatically.
        /// </summary>
        public static bool TrySetGlobalRequestsEnabled(bool enabled, out string error)
        {
            error = string.Empty;
            if (!IsWindows11)
            {
                error = "Windows 11 is required.";
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(GlobalRequestsKeyPath, writable: true);
                if (key == null)
                {
                    error = "The Windows kernel settings key is unavailable.";
                    return false;
                }

                if (enabled)
                {
                    key.SetValue(GlobalRequestsValueName, 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue(GlobalRequestsValueName, throwOnMissingValue: false);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool IsOnBattery()
        {
            try
            {
                return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                    System.Windows.Forms.PowerLineStatus.Offline;
            }
            catch
            {
                return false;
            }
        }

        private void RaiseStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            try
            {
                SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
                AutopilotService.Instance.StateChanged -= OnAutopilotStateChanged;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch
            {
            }

            ReleaseForExit();
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(
            out uint minimumResolution,
            out uint maximumResolution,
            out uint currentResolution);

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(
            uint desiredResolution,
            [MarshalAs(UnmanagedType.U1)] bool setResolution,
            out uint currentResolution);
    }
}
