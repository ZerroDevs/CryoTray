using System;
using System.Diagnostics;
using System.Threading;

namespace CryoTray.Services
{
    /// <summary>
    /// Coalesces working-set trims into a single off-UI compacting GC + EmptyWorkingSet pass
    /// once burst UI/background work settles.
    /// </summary>
    public sealed class MemoryTrimService
    {
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1.5);
        public static readonly TimeSpan LoadedDelay = TimeSpan.FromSeconds(3.5);
        public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2.5);
        public static readonly TimeSpan InteractionCooldown = TimeSpan.FromMilliseconds(1200);
        public static readonly TimeSpan MinDeactivatedInterval = TimeSpan.FromSeconds(5);

        private static MemoryTrimService? _instance;
        public static MemoryTrimService Instance => _instance ??= new MemoryTrimService();

        private readonly object _lock = new();
        private Timer? _debounceTimer;
        private int _trimInFlight;
        private long _lastTrimCompletedUtcTicks;
        private long _suppressUntilUtcTicks;
        private bool _shuttingDown;
        private int _pendingGeneration;

        /// <summary>
        /// Temporarily defers working-set trims during brief UI bursts (such as tab switches)
        /// without blocking trims for the full lifetime of open windows.
        /// </summary>
        public void PauseForInteraction(TimeSpan? duration = null)
        {
            TimeSpan effective = duration ?? InteractionCooldown;
            if (effective <= TimeSpan.Zero)
            {
                return;
            }

            long untilTicks = DateTime.UtcNow.Add(effective).Ticks;
            while (true)
            {
                long current = Interlocked.Read(ref _suppressUntilUtcTicks);
                if (untilTicks <= current)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _suppressUntilUtcTicks, untilTicks, current) == current)
                {
                    break;
                }
            }

            lock (_lock)
            {
                if (_shuttingDown || _debounceTimer == null)
                {
                    return;
                }

                long remainingTicks = Interlocked.Read(ref _suppressUntilUtcTicks) - DateTime.UtcNow.Ticks;
                if (remainingTicks <= 0)
                {
                    return;
                }

                TimeSpan pushedDelay = TimeSpan.FromTicks(remainingTicks) + TimeSpan.FromMilliseconds(100);
                int generation = ++_pendingGeneration;
                _debounceTimer.Dispose();
                _debounceTimer = new Timer(OnTimerElapsed, generation, pushedDelay, Timeout.InfiniteTimeSpan);
            }
        }

        internal Action? TrimExecutorForTest { get; set; }
        internal int ScheduledCountForTest { get; private set; }
        internal int ExecutedCountForTest { get; private set; }

        private MemoryTrimService()
        {
        }

        public bool HasPendingTrim
        {
            get
            {
                lock (_lock)
                {
                    return _debounceTimer != null;
                }
            }
        }

        public void ScheduleTrim(TimeSpan? delay = null, bool skipIfRecentlyTrimmed = false)
        {
            if (skipIfRecentlyTrimmed)
            {
                long lastTicks = Interlocked.Read(ref _lastTrimCompletedUtcTicks);
                if (lastTicks > 0 && DateTime.UtcNow.Ticks - lastTicks < MinDeactivatedInterval.Ticks)
                {
                    return;
                }
            }

            TimeSpan effectiveDelay = delay ?? DefaultDelay;
            if (effectiveDelay < TimeSpan.FromMilliseconds(50))
            {
                effectiveDelay = TimeSpan.FromMilliseconds(50);
            }

            lock (_lock)
            {
                if (_shuttingDown)
                {
                    return;
                }

                ScheduledCountForTest++;
                int generation = ++_pendingGeneration;
                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(OnTimerElapsed, generation, effectiveDelay, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _debounceTimer.Dispose();
                    _debounceTimer = new Timer(OnTimerElapsed, generation, effectiveDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }

        public void CancelPending(bool shuttingDown = false)
        {
            Timer? timer;
            lock (_lock)
            {
                if (shuttingDown)
                {
                    _shuttingDown = true;
                }

                _pendingGeneration++;
                timer = _debounceTimer;
                _debounceTimer = null;
            }

            timer?.Dispose();
        }

        internal void ResetForTest()
        {
            CancelPending(shuttingDown: false);
            Interlocked.Exchange(ref _suppressUntilUtcTicks, 0);
            Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, 0);
            lock (_lock)
            {
                _shuttingDown = false;
                ScheduledCountForTest = 0;
                ExecutedCountForTest = 0;
            }
            TrimExecutorForTest = null;
        }

        private void OnTimerElapsed(object? state)
        {
            int firedGeneration = state is int g ? g : -1;
            Timer? timerToDispose = null;

            lock (_lock)
            {
                if (_shuttingDown || firedGeneration != _pendingGeneration)
                {
                    return;
                }

                timerToDispose = _debounceTimer;
                _debounceTimer = null;
            }

            timerToDispose?.Dispose();
            ExecuteTrimCore();
        }

        private void ExecuteTrimCore()
        {
            if (Volatile.Read(ref _trimInFlight) != 0 || IsShuttingDown())
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _trimInFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (IsShuttingDown())
                {
                    return;
                }

                long remainingTicks = Interlocked.Read(ref _suppressUntilUtcTicks) - DateTime.UtcNow.Ticks;
                if (remainingTicks > 0)
                {
                    ScheduleTrim(TimeSpan.FromTicks(remainingTicks) + TimeSpan.FromMilliseconds(100));
                    return;
                }

                ExecutedCountForTest++;
                Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, DateTime.UtcNow.Ticks);
                Action? custom = TrimExecutorForTest;
                if (custom != null)
                {
                    custom();
                    return;
                }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                if (IsShuttingDown())
                {
                    return;
                }

                using var process = Process.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(process.Handle);
                Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch
            {
            }
            finally
            {
                Volatile.Write(ref _trimInFlight, 0);
            }
        }

        private bool IsShuttingDown()
        {
            lock (_lock)
            {
                return _shuttingDown;
            }
        }
    }
}
