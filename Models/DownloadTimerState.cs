using System;

namespace CryoTray.Models
{
    public enum DownloadPowerAction
    {
        Sleep,
        Hibernate,
        ShutDown
    }

    public enum DownloadTimerPhase
    {
        Disarmed,
        WaitingForDownload,
        Monitoring,
        WaitingForDisk,
        Warning
    }

    public sealed record DownloadTimerOptions(
        double DownloadThresholdBytesPerSecond,
        TimeSpan QuietDuration,
        bool DiskGuardEnabled,
        bool WaitForDownloadStart,
        TimeSpan WarningDuration);

    public sealed record DownloadTimerTick(
        DownloadTimerPhase Phase,
        double DownloadBytesPerSecond,
        double DiskWriteBytesPerSecond,
        bool DiskActivityAvailable,
        TimeSpan QuietElapsed,
        TimeSpan QuietDuration,
        int WarningSecondsRemaining,
        bool ExecuteAction,
        bool WarningStarted,
        bool WarningEnded);

    public sealed record DownloadTimerStatus(
        DownloadTimerPhase Phase,
        double DownloadBytesPerSecond,
        double DiskWriteBytesPerSecond,
        bool DiskActivityAvailable,
        TimeSpan QuietElapsed,
        TimeSpan QuietDuration,
        int WarningSecondsRemaining,
        DownloadPowerAction Action);

    /// <summary>
    /// Pure one-second state machine for the download-completion timer. Keeping the
    /// logic separate from sampling and power actions lets the safety rules be tested
    /// without network traffic or a real sleep/shutdown.
    /// </summary>
    public sealed class DownloadTimerEngine
    {
        public const double DiskGuardBytesPerSecond = 2 * 1024 * 1024;

        private DownloadTimerPhase _phase = DownloadTimerPhase.Disarmed;
        private int _quietSeconds;
        private int _warningSeconds;

        public DownloadTimerPhase Phase => _phase;
        public int WarningSecondsRemaining => _warningSeconds;

        public void Arm(DownloadTimerOptions options)
        {
            _phase = options.WaitForDownloadStart
                ? DownloadTimerPhase.WaitingForDownload
                : DownloadTimerPhase.Monitoring;
            _quietSeconds = 0;
            _warningSeconds = 0;
        }

        public void Disarm()
        {
            _phase = DownloadTimerPhase.Disarmed;
            _quietSeconds = 0;
            _warningSeconds = 0;
        }

        public bool CancelWarning()
        {
            if (_phase != DownloadTimerPhase.Warning)
            {
                return false;
            }

            Disarm();
            return true;
        }

        public DownloadTimerTick Advance(
            DownloadTimerOptions options,
            double downloadBytesPerSecond,
            double diskWriteBytesPerSecond,
            bool diskActivityAvailable)
        {
            bool warningStarted = false;
            bool warningEnded = false;
            bool executeAction = false;

            double download = Math.Max(0, downloadBytesPerSecond);
            double disk = Math.Max(0, diskWriteBytesPerSecond);
            int quietSeconds = Math.Max(1, (int)Math.Round(options.QuietDuration.TotalSeconds));
            int warningSeconds = Math.Max(1, (int)Math.Round(options.WarningDuration.TotalSeconds));
            double threshold = Math.Max(0, options.DownloadThresholdBytesPerSecond);

            switch (_phase)
            {
                case DownloadTimerPhase.WaitingForDownload:
                    if (download >= threshold)
                    {
                        _phase = DownloadTimerPhase.Monitoring;
                        _quietSeconds = 0;
                    }
                    break;

                case DownloadTimerPhase.Monitoring:
                    if (download >= threshold)
                    {
                        _quietSeconds = 0;
                    }
                    else
                    {
                        _quietSeconds++;
                        if (_quietSeconds >= quietSeconds)
                        {
                            if (options.DiskGuardEnabled &&
                                (!diskActivityAvailable || disk >= DiskGuardBytesPerSecond))
                            {
                                _phase = DownloadTimerPhase.WaitingForDisk;
                            }
                            else
                            {
                                StartWarning(warningSeconds);
                                warningStarted = true;
                            }
                        }
                    }
                    break;

                case DownloadTimerPhase.WaitingForDisk:
                    if (download >= threshold)
                    {
                        _quietSeconds = 0;
                        _phase = DownloadTimerPhase.Monitoring;
                    }
                    else if (diskActivityAvailable && disk < DiskGuardBytesPerSecond)
                    {
                        StartWarning(warningSeconds);
                        warningStarted = true;
                    }
                    break;

                case DownloadTimerPhase.Warning:
                    bool activityReturned = download >= threshold ||
                        (options.DiskGuardEnabled &&
                         diskActivityAvailable &&
                         disk >= DiskGuardBytesPerSecond);
                    if (activityReturned)
                    {
                        _phase = DownloadTimerPhase.Monitoring;
                        _quietSeconds = 0;
                        _warningSeconds = 0;
                        warningEnded = true;
                    }
                    else
                    {
                        _warningSeconds--;
                        if (_warningSeconds <= 0)
                        {
                            _warningSeconds = 0;
                            _phase = DownloadTimerPhase.Disarmed;
                            warningEnded = true;
                            executeAction = true;
                        }
                    }
                    break;
            }

            return new DownloadTimerTick(
                _phase,
                download,
                disk,
                diskActivityAvailable,
                TimeSpan.FromSeconds(Math.Min(_quietSeconds, quietSeconds)),
                options.QuietDuration,
                _warningSeconds,
                executeAction,
                warningStarted,
                warningEnded);
        }

        private void StartWarning(int warningSeconds)
        {
            _phase = DownloadTimerPhase.Warning;
            _warningSeconds = warningSeconds;
        }
    }
}
