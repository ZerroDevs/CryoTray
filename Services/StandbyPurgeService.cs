using System;
using System.Diagnostics;
using System.Threading;
using CryoTray.Models;

namespace CryoTray.Services
{
    public sealed record StandbyPurgeStatus(
        bool Enabled,
        DateTime? LastAttemptUtc,
        ulong LastFreedBytes,
        bool LastSucceeded,
        int LastNtStatus);

    /// <summary>
    /// Opt-in standby-list-only cleanup. Active working sets are never trimmed here, and every
    /// attempt is separated by a user-configurable cooldown to prevent repeated purges.
    /// </summary>
    public sealed class StandbyPurgeService : IDisposable
    {
        private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(5);
        private static StandbyPurgeService? _instance;
        public static StandbyPurgeService Instance => _instance ??= new StandbyPurgeService();

        private readonly object _statusSync = new();
        private Timer? _timer;
        private int _evaluating;
        private bool _started;
        private bool _disposed;
        private DateTime? _lastAttemptUtc;
        private ulong _lastFreedBytes;
        private bool _lastSucceeded;
        private int _lastNtStatus;

        public event Action? StatusChanged;

        private StandbyPurgeService()
        {
        }

        public StandbyPurgeStatus GetStatus()
        {
            lock (_statusSync)
            {
                return new StandbyPurgeStatus(
                    SettingsService.Instance.Current.AutoStandbyPurgeEnabled,
                    _lastAttemptUtc,
                    _lastFreedBytes,
                    _lastSucceeded,
                    _lastNtStatus);
            }
        }

        public void Start()
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
            _timer = new Timer(_ => Evaluate(), null, EvaluationInterval, EvaluationInterval);
        }

        public static bool ShouldPurge(
            bool enabled,
            bool autopilotActive,
            bool allowDuringGames,
            double freeRamMb,
            double standbyMb,
            double freeRamThresholdMb,
            double standbyThresholdMb,
            DateTime nowUtc,
            DateTime? lastAttemptUtc,
            int cooldownMinutes)
        {
            if (!enabled || (autopilotActive && !allowDuringGames))
            {
                return false;
            }

            if (freeRamMb >= freeRamThresholdMb || standbyMb <= standbyThresholdMb)
            {
                return false;
            }

            int cooldown = Math.Clamp(cooldownMinutes, 1, 60);
            return !lastAttemptUtc.HasValue ||
                nowUtc - lastAttemptUtc.Value >= TimeSpan.FromMinutes(cooldown);
        }

        private void Evaluate()
        {
            if (_disposed || !_started || Interlocked.Exchange(ref _evaluating, 1) != 0)
            {
                return;
            }

            try
            {
                var settings = SettingsService.Instance.Current;
                if (!settings.AutoStandbyPurgeEnabled)
                {
                    return;
                }

                bool autopilotActive = AutopilotService.Instance.CurrentState.IsActive;
                if (autopilotActive && !settings.AllowAutoPurgeDuringGames)
                {
                    return;
                }

                if (MemoryOptimizerService.Instance.IsOptimizing)
                {
                    return;
                }

                if (!MemoryTelemetryService.TryReadBreakdown(out var before) ||
                    !before.HasPageListBreakdown)
                {
                    return;
                }

                double freeMb = before.FreeBytes / (1024.0 * 1024.0);
                double standbyMb = before.StandbyBytes / (1024.0 * 1024.0);
                DateTime now = DateTime.UtcNow;
                int cooldown = Math.Clamp(settings.AutoPurgeCooldownMinutes, 1, 60);
                DateTime? lastAttempt;
                lock (_statusSync)
                {
                    lastAttempt = _lastAttemptUtc;
                }

                if (!ShouldPurge(
                        enabled: true,
                        autopilotActive,
                        settings.AllowAutoPurgeDuringGames,
                        freeMb,
                        standbyMb,
                        Math.Clamp(settings.AutoPurgeFreeRamThresholdMb, 512, 8192),
                        Math.Clamp(settings.AutoPurgeStandbyThresholdMb, 1024, 32768),
                        now,
                        lastAttempt,
                        cooldown))
                {
                    return;
                }

                // Reserve the cooldown window before running so a slow kernel operation cannot
                // overlap with the next five-second evaluation.
                lock (_statusSync)
                {
                    _lastAttemptUtc = now;
                    _lastFreedBytes = 0;
                    _lastSucceeded = false;
                    _lastNtStatus = 0;
                }

                var result = MemoryOptimizerService.ExecuteKernelCommands(
                    freeWorkingSets: false,
                    clearStandby: true);

                ulong freed = 0;
                if (result.Succeeded)
                {
                    Thread.Sleep(50);
                    if (MemoryTelemetryService.TryReadBreakdown(out var after) &&
                        after.AvailableBytes > before.AvailableBytes)
                    {
                        freed = after.AvailableBytes - before.AvailableBytes;
                    }
                }

                lock (_statusSync)
                {
                    _lastFreedBytes = freed;
                    _lastSucceeded = result.Succeeded;
                    _lastNtStatus = result.LastNtStatus;
                }

                if (result.Succeeded)
                {
                    ActivityLogService.Instance.Record(
                        ActivitySeverity.Info,
                        "memory",
                        "Activity_AutoPurgeCompleted",
                        MemoryTelemetryService.FormatBytes(freed));
                }
                else
                {
                    Debug.WriteLine(
                        $"Automatic standby purge failed (NT status {result.LastNtStatus}, elevation: {result.RequiresElevation}).");
                }

                RaiseStatusChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Automatic standby purge error: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _evaluating, 0);
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
            _timer?.Dispose();
            _timer = null;
        }
    }
}
