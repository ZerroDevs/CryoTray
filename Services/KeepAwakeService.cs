using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum KeepAwakeMode
    {
        Off,
        Timed,
        Indefinite
    }

    public sealed record KeepAwakeStatus(
        KeepAwakeMode Mode,
        DateTime? EndsUtc,
        TimeSpan? Duration,
        TimeSpan? Remaining,
        bool AllowDisplayOff,
        bool IsActive);

    /// <summary>
    /// Runtime-only sleep blocker. SetThreadExecutionState is thread-affine, so every native call
    /// and the countdown timer are marshalled to the WPF dispatcher thread.
    /// </summary>
    public sealed class KeepAwakeService : IDisposable
    {
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;

        private static KeepAwakeService? _instance;
        public static KeepAwakeService Instance => _instance ??= new KeepAwakeService();

        private readonly object _sync = new();
        private DispatcherTimer? _timer;
        private KeepAwakeMode _mode = KeepAwakeMode.Off;
        private DateTime? _endsUtc;
        private TimeSpan? _duration;
        private bool _started;
        private bool _disposed;

        public event Action? StatusChanged;

        public bool IsActive
        {
            get
            {
                lock (_sync)
                {
                    return _mode != KeepAwakeMode.Off;
                }
            }
        }

        private KeepAwakeService()
        {
        }

        public void Start()
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        }

        public KeepAwakeStatus GetStatus()
        {
            lock (_sync)
            {
                TimeSpan? remaining = _mode == KeepAwakeMode.Timed && _endsUtc.HasValue
                    ? Max(TimeSpan.Zero, _endsUtc.Value - DateTime.UtcNow)
                    : null;

                return new KeepAwakeStatus(
                    _mode,
                    _endsUtc,
                    _duration,
                    remaining,
                    SettingsService.Instance.Current.KeepAwakeAllowDisplayOff,
                    _mode != KeepAwakeMode.Off);
            }
        }

        public bool StartTimed(TimeSpan duration)
        {
            if (_disposed || duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(72))
            {
                return false;
            }

            return InvokeOnUi(() => StartCore(duration));
        }

        public bool StartIndefinite()
        {
            if (_disposed)
            {
                return false;
            }

            return InvokeOnUi(() => StartCore(null));
        }

        public void Stop()
        {
            if (_disposed)
            {
                return;
            }

            InvokeOnUi(StopCore);
        }

        /// <summary>Clears the native hold and stops the timer; safe when already off.</summary>
        public void ReleaseForExit()
        {
            InvokeOnUi(StopCore);
        }

        private bool StartCore(TimeSpan? duration)
        {
            if (_disposed)
            {
                return false;
            }

            if (!TryApplyFlags())
            {
                StopCore();
                return false;
            }

            lock (_sync)
            {
                _mode = duration.HasValue ? KeepAwakeMode.Timed : KeepAwakeMode.Indefinite;
                _endsUtc = duration.HasValue ? DateTime.UtcNow + duration.Value : null;
                _duration = duration;
            }

            if (duration.HasValue)
            {
                _timer ??= CreateTimer();
                _timer.Start();
            }
            else
            {
                _timer?.Stop();
            }

            RaiseStatusChanged();
            return true;
        }

        private void StopCore()
        {
            bool wasActive;
            lock (_sync)
            {
                wasActive = _mode != KeepAwakeMode.Off;
                _mode = KeepAwakeMode.Off;
                _endsUtc = null;
                _duration = null;
            }

            _timer?.Stop();
            if (wasActive || _started)
            {
                TrySetExecutionState(EsContinuous);
            }

            RaiseStatusChanged();
        }

        private bool TryApplyFlags()
        {
            bool allowDisplayOff = SettingsService.Instance.Current.KeepAwakeAllowDisplayOff;
            uint flags = EsContinuous | EsSystemRequired;
            if (!allowDisplayOff)
            {
                flags |= EsDisplayRequired;
            }

            return TrySetExecutionState(flags) != 0;
        }

        private static uint TrySetExecutionState(uint flags)
        {
            try
            {
                return SetThreadExecutionState(flags);
            }
            catch
            {
                return 0;
            }
        }

        private DispatcherTimer CreateTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => OnTimerTick();
            return timer;
        }

        private void OnTimerTick()
        {
            lock (_sync)
            {
                if (_mode != KeepAwakeMode.Timed || !_endsUtc.HasValue)
                {
                    _timer?.Stop();
                    return;
                }

                if (_endsUtc.Value <= DateTime.UtcNow)
                {
                    _timer?.Stop();
                    StopCore();
                    return;
                }
            }

            RaiseStatusChanged();
        }

        private void OnSettingsChanged()
        {
            if (!_started || _disposed)
            {
                return;
            }

            bool isActive;
            lock (_sync)
            {
                isActive = _mode != KeepAwakeMode.Off;
            }

            if (!isActive)
            {
                return;
            }

            InvokeOnUi(() =>
            {
                if (GetStatus().IsActive)
                {
                    TryApplyFlags();
                    RaiseStatusChanged();
                }
            });
        }

        internal static bool TryParseCustomDuration(
            string? amountText,
            string? unit,
            out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (!int.TryParse(
                    amountText?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int amount) || amount <= 0)
            {
                return false;
            }

            bool hours = string.Equals(unit, "Hours", StringComparison.OrdinalIgnoreCase);
            bool minutes = string.Equals(unit, "Minutes", StringComparison.OrdinalIgnoreCase);
            if (!hours && !minutes)
            {
                return false;
            }

            if (hours && amount > 72)
            {
                return false;
            }

            if (minutes && amount > 72 * 60)
            {
                return false;
            }

            duration = hours ? TimeSpan.FromHours(amount) : TimeSpan.FromMinutes(amount);
            return duration >= TimeSpan.FromMinutes(1) && duration <= TimeSpan.FromHours(72);
        }

        internal static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            int totalHours = (int)remaining.TotalHours;
            return $"{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }

        private static TimeSpan Max(TimeSpan first, TimeSpan second)
        {
            return first >= second ? first : second;
        }

        private static T InvokeOnUi<T>(Func<T> action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return default!;
                }

                return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
            }
            catch
            {
                return default!;
            }
        }

        private static void InvokeOnUi(Action action)
        {
            InvokeOnUi(() =>
            {
                action();
                return true;
            });
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

            ReleaseForExit();
            _disposed = true;
            _started = false;
            try
            {
                SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            }
            catch
            {
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);
    }
}
