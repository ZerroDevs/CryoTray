using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;

namespace CryoTray.Services
{
    internal interface IDownloadTimerSampler : IDisposable
    {
        double SampleDownloadBytesPerSecond();
        bool TrySampleDiskWriteBytesPerSecond(out double bytesPerSecond);
    }

    internal interface IPowerActionExecutor
    {
        void Execute(DownloadPowerAction action);
    }

    /// <summary>
    /// Arms only when the opt-in setting is true, auto-disarms after firing or a
    /// manual cancel, and blocks the action while the disk guard cannot be sampled.
    /// </summary>
    public sealed class DownloadTimerService : IDisposable
    {
        public static readonly TimeSpan WarningDuration = TimeSpan.FromSeconds(60);

        private static readonly Lazy<DownloadTimerService> LazyInstance = new(() => new DownloadTimerService());
        public static DownloadTimerService Instance => LazyInstance.Value;

        private readonly object _sync = new();
        private readonly DownloadTimerEngine _engine = new();
        private readonly IDownloadTimerSampler _sampler;
        private readonly IPowerActionExecutor _executor;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private DownloadTimerStatus _status;

        public event Action<DownloadTimerStatus>? StatusChanged;
        public event Action? WarningStarted;
        public event Action? WarningEnded;

        public bool IsArmed
        {
            get
            {
                lock (_sync)
                {
                    return _engine.Phase != DownloadTimerPhase.Disarmed;
                }
            }
        }

        public DownloadTimerStatus CurrentStatus
        {
            get
            {
                lock (_sync)
                {
                    return _status;
                }
            }
        }

        public DownloadTimerService()
            : this(new SystemDownloadTimerSampler(), new SystemPowerActionExecutor())
        {
        }

        internal DownloadTimerService(
            IDownloadTimerSampler sampler,
            IPowerActionExecutor executor)
        {
            _sampler = sampler;
            _executor = executor;
            _status = CreateStatus(DownloadTimerPhase.Disarmed, 0, 0, false, TimeSpan.Zero, CurrentOptions(), 0);
        }

        public void SyncWithSettings()
        {
            if (_disposed)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            if (settings.DownloadTimerEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (_disposed)
            {
                return;
            }

            lock (_sync)
            {
                if (_cts != null)
                {
                    return;
                }

                _engine.Arm(CurrentOptions());
                _cts = new CancellationTokenSource();
                _status = CreateStatus(_engine.Phase, 0, 0, false, TimeSpan.Zero, CurrentOptions(), 0);
                _ = Task.Run(() => RunAsync(_cts.Token));
            }

            PublishStatus();
        }

        public void Stop()
        {
            CancellationTokenSource? toCancel = null;
            bool endedWarning = false;
            lock (_sync)
            {
                if (_cts == null)
                {
                    return;
                }

                endedWarning = _engine.Phase == DownloadTimerPhase.Warning;
                toCancel = _cts;
                _cts = null;
                _engine.Disarm();
                _status = CreateStatus(DownloadTimerPhase.Disarmed, 0, 0, false, TimeSpan.Zero, CurrentOptions(), 0);
            }

            try
            {
                toCancel.Cancel();
                toCancel.Dispose();
            }
            catch
            {
            }

            PublishStatus();
            if (endedWarning)
            {
                Raise(WarningEnded);
            }
        }

        /// <summary>Manual Cancel / master OFF: disarm and persist false.</summary>
        public void CancelAndDisarm()
        {
            PersistDisarmed();
            Stop();
        }

        public bool CancelWarning()
        {
            bool cancelled;
            lock (_sync)
            {
                cancelled = _engine.CancelWarning();
            }

            if (!cancelled)
            {
                return false;
            }

            PersistDisarmed();
            Stop();
            Raise(WarningEnded);
            return true;
        }

        private async Task RunAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var options = CurrentOptions();
                    double download = _sampler.SampleDownloadBytesPerSecond();
                    bool diskAvailable = _sampler.TrySampleDiskWriteBytesPerSecond(out double disk);
                    DownloadTimerTick tick;
                    lock (_sync)
                    {
                        tick = _engine.Advance(options, download, disk, diskAvailable);
                        _status = CreateStatus(
                            tick.Phase,
                            tick.DownloadBytesPerSecond,
                            tick.DiskWriteBytesPerSecond,
                            tick.DiskActivityAvailable,
                            tick.QuietElapsed,
                            options,
                            _engine.WarningSecondsRemaining);
                    }

                    PublishStatus();

                    if (tick.WarningStarted)
                    {
                        Raise(WarningStarted);
                    }
                    if (tick.WarningEnded)
                    {
                        Raise(WarningEnded);
                    }
                    if (tick.ExecuteAction)
                    {
                        await ExecuteActionAsync(token).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch
            {
                // A sampling failure must never crash the tray app or power off the PC.
                PersistDisarmed();
                Stop();
            }
        }

        private Task ExecuteActionAsync(CancellationToken token)
        {
            DownloadPowerAction action = ReadAction();
            // Persist false before the action so waking up can never re-arm and sleep again.
            PersistDisarmed();
            // Release the run slot before the action so the user can arm the timer again
            // after waking up without restarting the app.
            Stop();
            try
            {
                WarningEnded?.Invoke();
            }
            catch
            {
            }

            try
            {
                _executor.Execute(action);
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        private void PersistDisarmed()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.DownloadTimerEnabled)
            {
                return;
            }

            settings.DownloadTimerEnabled = false;
            // This runs on the timer thread. A notification would run App's settings
            // handlers (tray/theme/hotkey) off the UI thread; the Disarmed status event
            // updates the settings card and tray instead.
            SettingsService.Instance.SaveWithoutNotification();
        }

        private DownloadTimerOptions CurrentOptions()
        {
            var s = SettingsService.Instance.Current;
            double threshold = Math.Clamp(s.DownloadTimerThresholdKbps, 64, 1024 * 32) * 1024.0;
            int quietMinutes = Math.Clamp(s.DownloadTimerQuietMinutes, 1, 60);
            return new DownloadTimerOptions(
                threshold,
                TimeSpan.FromMinutes(quietMinutes),
                s.DownloadTimerDiskGuard,
                s.DownloadTimerWaitForStart,
                WarningDuration);
        }

        private DownloadTimerStatus CreateStatus(
            DownloadTimerPhase phase,
            double download,
            double disk,
            bool diskAvailable,
            TimeSpan quietElapsed,
            DownloadTimerOptions options,
            int warningSeconds)
        {
            return new DownloadTimerStatus(
                phase,
                download,
                disk,
                diskAvailable,
                quietElapsed,
                options.QuietDuration,
                warningSeconds,
                ReadAction());
        }

        private static DownloadPowerAction ReadAction()
        {
            return SettingsService.Instance.Current.DownloadTimerAction switch
            {
                "Hibernate" => DownloadPowerAction.Hibernate,
                "ShutDown" => DownloadPowerAction.ShutDown,
                _ => DownloadPowerAction.Sleep
            };
        }

        private void PublishStatus()
        {
            DownloadTimerStatus status;
            lock (_sync)
            {
                status = _status;
            }

            try
            {
                StatusChanged?.Invoke(status);
            }
            catch
            {
            }
        }

        private static void Raise(Action? action)
        {
            try
            {
                action?.Invoke();
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
            Stop();
            _sampler.Dispose();
        }

        private sealed class SystemPowerActionExecutor : IPowerActionExecutor
        {
            public void Execute(DownloadPowerAction action)
            {
                if (action == DownloadPowerAction.ShutDown)
                {
                    string shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
                    var startInfo = new ProcessStartInfo(shutdownPath, "/s /t 0")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(startInfo);
                    return;
                }

                NativeMethods.SetSuspendState(
                    action == DownloadPowerAction.Hibernate,
                    false,
                    false);
            }
        }
    }

    internal sealed class SystemDownloadTimerSampler : IDownloadTimerSampler
    {
        private readonly Dictionary<string, long> _lastReceived = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private IntPtr _query;
        private IntPtr _diskCounter;
        private bool _hasDiskBaseline;
        private bool _disposed;

        public SystemDownloadTimerSampler()
        {
            TryInitializeDiskCounter();
        }

        public double SampleDownloadBytesPerSecond()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return 0;
                }

                double total = 0;
                try
                {
                    foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter.OperationalStatus != OperationalStatus.Up ||
                            adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                            adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        {
                            continue;
                        }

                        long received;
                        try
                        {
                            received = adapter.GetIPStatistics().BytesReceived;
                        }
                        catch
                        {
                            continue;
                        }

                        string key = adapter.Id;
                        if (_lastReceived.TryGetValue(key, out long previous))
                        {
                            long delta = received - previous;
                            if (delta > 0)
                            {
                                total += delta;
                            }
                        }
                        _lastReceived[key] = received;
                    }
                }
                catch
                {
                    return 0;
                }

                return total;
            }
        }

        public bool TrySampleDiskWriteBytesPerSecond(out double bytesPerSecond)
        {
            bytesPerSecond = 0;
            lock (_sync)
            {
                if (_disposed || _query == IntPtr.Zero || _diskCounter == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    if (PdhCollectQueryData(_query) != 0)
                    {
                        return false;
                    }

                    if (!_hasDiskBaseline)
                    {
                        _hasDiskBaseline = true;
                        return false;
                    }

                    uint type;
                    if (PdhGetFormattedCounterValue(_diskCounter, PdhFormatDouble, out type, out PdhFmtValue value) != 0)
                    {
                        return false;
                    }

                    if (value.CStatus != 0 && value.CStatus != 1)
                    {
                        return false;
                    }

                    bytesPerSecond = Math.Max(0, value.Value.DoubleValue);
                    return double.IsFinite(bytesPerSecond);
                }
                catch
                {
                    return false;
                }
            }
        }

        private void TryInitializeDiskCounter()
        {
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0 || _query == IntPtr.Zero)
                {
                    _query = IntPtr.Zero;
                    return;
                }

                if (PdhAddEnglishCounterW(
                        _query,
                        @"\PhysicalDisk(_Total)\Disk Write Bytes/sec",
                        IntPtr.Zero,
                        out _diskCounter) != 0 ||
                    _diskCounter == IntPtr.Zero)
                {
                    PdhCloseQuery(_query);
                    _query = IntPtr.Zero;
                    _diskCounter = IntPtr.Zero;
                    return;
                }

                // Prime PDH; the first formatted value is only valid after a second sample.
                PdhCollectQueryData(_query);
            }
            catch
            {
                _query = IntPtr.Zero;
                _diskCounter = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_query != IntPtr.Zero)
                {
                    try
                    {
                        PdhCloseQuery(_query);
                    }
                    catch
                    {
                    }
                    _query = IntPtr.Zero;
                    _diskCounter = IntPtr.Zero;
                }
            }
        }

        private const uint PdhFormatDouble = 0x00000200;

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtValue
        {
            public uint CStatus;
            public PdhFmtValueUnion Value;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PdhFmtValueUnion
        {
            [FieldOffset(0)]
            public long LongValue;
            [FieldOffset(0)]
            public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint PdhOpenQueryW(
            string? dataSource,
            IntPtr userData,
            out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint PdhAddEnglishCounterW(
            IntPtr query,
            string fullCounterPath,
            IntPtr userData,
            out IntPtr counter);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint type,
            out PdhFmtValue value);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint PdhCloseQuery(IntPtr query);
    }
}
