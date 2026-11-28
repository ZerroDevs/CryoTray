using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;

namespace CryoTray.Services
{
    /// <summary>
    /// Orchestrates delayed post-boot startup app launches cleanly once per Windows boot session,
    /// using lightweight CPU/disk idle gating, unelevated launching, and immediate timer teardown
    /// so steady-state idle CPU/timers remain at zero.
    /// </summary>
    public sealed class StartupDelayService : IDisposable
    {
        private static readonly Lazy<StartupDelayService> LazyInstance = new(() => new StartupDelayService());
        public static StartupDelayService Instance => LazyInstance.Value;

        public const long AutostartMaxBootWindowMs = 5 * 60 * 1000L;
        public const double IdleCpuCeilingPercent = 35.0;
        public const double IdleDiskWriteCeilingBytesPerSec = 5.0 * 1024.0 * 1024.0;
        public const int RequiredCalmSamples = 2;
        public const int LaunchStaggerMilliseconds = 1500;

        private static string StateFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CryoTray",
            "cryoboot-state.json");

        private sealed class BootSessionMarker
        {
            public DateTime CompletedBootUtc { get; set; }
        }

        private sealed class QueuedBootItem
        {
            public StartupDelaySetting Setting { get; }
            public int EffectiveDelaySeconds { get; set; }
            public DateTime QueuedUtc { get; set; }
            public int ConsecutiveCalmSamples { get; set; }
            public StartupDelayItemState State { get; set; } = StartupDelayItemState.Pending;
            public string Error { get; set; } = "";

            public QueuedBootItem(StartupDelaySetting setting, DateTime queuedUtc, int effectiveDelaySeconds)
            {
                Setting = setting;
                QueuedUtc = queuedUtc;
                EffectiveDelaySeconds = Math.Max(5, effectiveDelaySeconds);
            }
        }

        private readonly IStartupProcessLauncher _launcher;
        private readonly object _sync = new();
        private readonly List<QueuedBootItem> _queue = new();
        private readonly Dictionary<string, (StartupDelayItemState State, string Error)> _runtimeStates =
            new(StringComparer.OrdinalIgnoreCase);

        private Timer? _tickTimer;
        private IntPtr _pdhQuery = IntPtr.Zero;
        private IntPtr _pdhDiskCounter = IntPtr.Zero;
        private bool _hasDiskBaseline;
        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private bool _hasCpuBaseline;
        private DateTime _bootStartUtc;
        private DateTime _lastLaunchUtc = DateTime.MinValue;
        private bool _isRunning;
        private bool _isEvaluating;
        private bool _disposed;
        private CryoBootQueueStatus _currentStatus = new(false, 0, 0, 0, "", 0, false);

        public event Action<CryoBootQueueStatus>? StatusChanged;
        public event Action? QueueCompleted;

        public bool IsQueueActive
        {
            get
            {
                lock (_sync)
                {
                    return _isRunning;
                }
            }
        }

        public CryoBootQueueStatus CurrentStatus
        {
            get
            {
                lock (_sync)
                {
                    return _currentStatus;
                }
            }
        }

        public StartupDelayService()
            : this(UnelevatedProcessLauncher.Instance)
        {
        }

        internal StartupDelayService(IStartupProcessLauncher launcher)
        {
            _launcher = launcher;
        }

        public static DateTime ComputeCurrentBootUtc(DateTime utcNow, long tickCount64Ms)
        {
            return utcNow - TimeSpan.FromMilliseconds(Math.Max(0, tickCount64Ms));
        }

        public static bool ShouldRunBootQueue(
            bool hasDelayedEntries,
            bool isAutostart,
            long tickCount64Ms,
            DateTime currentBootUtc,
            DateTime? lastCompletedBootUtc)
        {
            if (!hasDelayedEntries)
            {
                return false;
            }

            if (lastCompletedBootUtc.HasValue &&
                Math.Abs((currentBootUtc - lastCompletedBootUtc.Value).TotalSeconds) <= 120.0)
            {
                return false;
            }

            return isAutostart || tickCount64Ms <= AutostartMaxBootWindowMs;
        }

        internal static bool EvaluateLaunchReadiness(
            double elapsedSeconds,
            int delaySeconds,
            bool waitForIdle,
            int maxIdleWaitSeconds,
            double? cpuLoadPercent,
            double? diskWriteBytesPerSec,
            ref int consecutiveCalmSamples,
            out StartupDelayItemState nextState)
        {
            if (elapsedSeconds < delaySeconds)
            {
                consecutiveCalmSamples = 0;
                nextState = StartupDelayItemState.Pending;
                return false;
            }

            if (!waitForIdle)
            {
                nextState = StartupDelayItemState.Launching;
                return true;
            }

            double maxDeadline = delaySeconds + Math.Max(0, maxIdleWaitSeconds);
            if (elapsedSeconds >= maxDeadline)
            {
                nextState = StartupDelayItemState.Launching;
                return true;
            }

            bool cpuCalm = !cpuLoadPercent.HasValue || !double.IsFinite(cpuLoadPercent.Value) || cpuLoadPercent.Value < IdleCpuCeilingPercent;
            bool diskCalm = !diskWriteBytesPerSec.HasValue || !double.IsFinite(diskWriteBytesPerSec.Value) || diskWriteBytesPerSec.Value < IdleDiskWriteCeilingBytesPerSec;

            if (cpuCalm && diskCalm)
            {
                consecutiveCalmSamples++;
                if (consecutiveCalmSamples >= RequiredCalmSamples)
                {
                    nextState = StartupDelayItemState.Launching;
                    return true;
                }
            }
            else
            {
                consecutiveCalmSamples = 0;
            }

            nextState = StartupDelayItemState.WaitingForIdle;
            return false;
        }

        public static bool IsExecutableAlreadyRunning(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
            string targetName = Path.GetFileNameWithoutExtension(expanded);
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return false;
            }

            try
            {
                int currentSession = Process.GetCurrentProcess().SessionId;
                foreach (Process proc in Process.GetProcessesByName(targetName))
                {
                    using (proc)
                    {
                        if (proc.SessionId != currentSession)
                        {
                            continue;
                        }

                        try
                        {
                            string? runningPath = proc.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(runningPath))
                            {
                                if (string.Equals(
                                        Path.GetFullPath(runningPath),
                                        Path.GetFullPath(expanded),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                                continue;
                            }
                        }
                        catch
                        {
                        }

                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public IReadOnlyDictionary<string, (StartupDelayItemState State, string Error)> GetRuntimeStatesSnapshot()
        {
            lock (_sync)
            {
                return new Dictionary<string, (StartupDelayItemState, string)>(_runtimeStates, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void StartIfDue(bool isAutostart)
        {
            if (_disposed)
            {
                return;
            }

            AppSettings settings = SettingsService.Instance.Current;
            List<StartupDelaySetting> entries = (settings.CryoBootEntries ?? new List<StartupDelaySetting>())
                .Where(e => !string.IsNullOrWhiteSpace(e.EntryId))
                .OrderBy(e => e.PriorityOrder)
                .ThenBy(e => e.DelaySeconds)
                .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            DateTime bootUtc = ComputeCurrentBootUtc(DateTime.UtcNow, Environment.TickCount64);
            DateTime? completedBootUtc = ReadBootSessionMarker();

            if (!ShouldRunBootQueue(entries.Count > 0, isAutostart, Environment.TickCount64, bootUtc, completedBootUtc))
            {
                return;
            }

            StartQueueInternal(entries, bootUtc);
        }

        internal void StartQueueForTestOrManual(IReadOnlyList<StartupDelaySetting> entries)
        {
            if (_disposed || entries.Count == 0)
            {
                return;
            }

            DateTime bootUtc = ComputeCurrentBootUtc(DateTime.UtcNow, Environment.TickCount64);
            StartQueueInternal(entries.ToList(), bootUtc);
        }

        private void StartQueueInternal(List<StartupDelaySetting> entries, DateTime bootUtc)
        {
            lock (_sync)
            {
                if (_isRunning || _disposed || entries.Count == 0)
                {
                    return;
                }

                _queue.Clear();
                _runtimeStates.Clear();
                DateTime nowUtc = DateTime.UtcNow;

                foreach (StartupDelaySetting entry in entries)
                {
                    int delay = StartupManagerService.ClampDelaySeconds(entry.DelaySeconds);
                    _queue.Add(new QueuedBootItem(entry, nowUtc, delay));
                    _runtimeStates[entry.EntryId] = (StartupDelayItemState.Pending, "");
                }

                _bootStartUtc = bootUtc;
                _lastLaunchUtc = DateTime.MinValue;
                _hasCpuBaseline = false;
                _isRunning = true;
                InitializeTransientDiskCounter();
                PrimeCpuBaseline();
                UpdateStatusLocked(nowUtc);
                _tickTimer = new Timer(_ => EvaluateQueueTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            StatusChanged?.Invoke(CurrentStatus);
        }

        public bool LaunchQueuedItemNow(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            StartupDelaySetting? targetSetting = null;
            lock (_sync)
            {
                QueuedBootItem? item = _queue.FirstOrDefault(q =>
                    string.Equals(q.Setting.EntryId, entryId, StringComparison.OrdinalIgnoreCase) &&
                    q.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle);
                if (item != null)
                {
                    item.State = StartupDelayItemState.Launching;
                    targetSetting = item.Setting;
                }
            }

            if (targetSetting == null)
            {
                return false;
            }

            ExecuteQueuedLaunch(targetSetting);
            AdvanceCompletionState();
            return true;
        }

        public void RemovePendingEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                return;
            }

            bool foundPending = false;
            lock (_sync)
            {
                if (!_isRunning)
                {
                    return;
                }

                QueuedBootItem? item = _queue.FirstOrDefault(q =>
                    string.Equals(q.Setting.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                if (item != null &&
                    item.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle)
                {
                    item.State = StartupDelayItemState.Cancelled;
                    _runtimeStates[entryId] = (StartupDelayItemState.Cancelled, "");
                    foundPending = true;
                }
            }

            if (foundPending)
            {
                AdvanceCompletionState();
            }
        }

        public void LaunchAllNow()
        {
            List<StartupDelaySetting> pending;
            lock (_sync)
            {
                if (!_isRunning)
                {
                    return;
                }

                pending = _queue
                    .Where(q => q.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle)
                    .Select(q =>
                    {
                        q.State = StartupDelayItemState.Launching;
                        return q.Setting;
                    })
                    .ToList();
            }

            _ = Task.Run(() =>
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (_disposed)
                    {
                        break;
                    }

                    ExecuteQueuedLaunch(pending[i]);
                    if (i + 1 < pending.Count)
                    {
                        Thread.Sleep(500);
                    }
                }

                AdvanceCompletionState();
            });
        }

        public void CancelPendingQueue()
        {
            bool changed = false;
            lock (_sync)
            {
                if (!_isRunning)
                {
                    return;
                }

                foreach (QueuedBootItem item in _queue)
                {
                    if (item.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle)
                    {
                        item.State = StartupDelayItemState.Cancelled;
                        _runtimeStates[item.Setting.EntryId] = (StartupDelayItemState.Cancelled, "");
                        changed = true;
                    }
                }

                CompleteQueueLocked(recordMarker: true);
            }

            if (changed)
            {
                ActivityLogService.Instance.Record(ActivitySeverity.Info, "startup", "Activity_CryoBootQueueCancelled");
                StatusChanged?.Invoke(CurrentStatus);
                QueueCompleted?.Invoke();
            }
        }

        private void EvaluateQueueTick()
        {
            lock (_sync)
            {
                if (!_isRunning || _isEvaluating || _disposed)
                {
                    return;
                }

                _isEvaluating = true;
            }

            try
            {
                StartupDelaySetting? launchTarget = null;
                DateTime nowUtc = DateTime.UtcNow;
                double? cpuLoad = SampleCpuLoadPercent();
                double? diskWriteBps = SampleDiskWriteBytesPerSec();
                int maxIdleWait = Math.Clamp(SettingsService.Instance.Current.CryoBootMaxIdleWaitSeconds, 0, 300);

                lock (_sync)
                {
                    if (!_isRunning || _disposed)
                    {
                        return;
                    }

                    bool staggerSatisfied = _lastLaunchUtc == DateTime.MinValue ||
                        (nowUtc - _lastLaunchUtc).TotalMilliseconds >= LaunchStaggerMilliseconds;

                    foreach (QueuedBootItem item in _queue)
                    {
                        if (item.State is not (StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle))
                        {
                            continue;
                        }

                        double elapsed = (nowUtc - item.QueuedUtc).TotalSeconds;
                        int calm = item.ConsecutiveCalmSamples;
                        bool ready = EvaluateLaunchReadiness(
                            elapsed,
                            item.EffectiveDelaySeconds,
                            item.Setting.WaitForIdle,
                            maxIdleWait,
                            cpuLoad,
                            diskWriteBps,
                            ref calm,
                            out StartupDelayItemState nextState);

                        item.ConsecutiveCalmSamples = calm;
                        if (ready && staggerSatisfied)
                        {
                            item.State = StartupDelayItemState.Launching;
                            _lastLaunchUtc = nowUtc;
                            launchTarget = item.Setting;
                            break;
                        }

                        item.State = ready ? StartupDelayItemState.WaitingForIdle : nextState;
                        _runtimeStates[item.Setting.EntryId] = (item.State, "");
                    }
                }

                if (launchTarget != null)
                {
                    ExecuteQueuedLaunch(launchTarget);
                }

                AdvanceCompletionState();
            }
            finally
            {
                lock (_sync)
                {
                    _isEvaluating = false;
                }
            }
        }

        private void ExecuteQueuedLaunch(StartupDelaySetting setting)
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(setting.ExecutablePath ?? "");
            if (string.IsNullOrWhiteSpace(expandedPath) ||
                (!File.Exists(expandedPath) && !Directory.Exists(expandedPath)))
            {
                SetItemFinalState(setting.EntryId, StartupDelayItemState.SkippedMissing, "Executable not found.");
                return;
            }

            if (IsExecutableAlreadyRunning(expandedPath))
            {
                SetItemFinalState(setting.EntryId, StartupDelayItemState.AlreadyRunning, "");
                return;
            }

            bool ok = _launcher.TryLaunch(
                expandedPath,
                setting.Arguments,
                setting.WorkingDirectory,
                setting.RequiresElevation,
                out string error);

            if (ok)
            {
                SetItemFinalState(setting.EntryId, StartupDelayItemState.Launched, "");
                ActivityLogService.Instance.RecordDetail(
                    ActivitySeverity.Info,
                    "startup",
                    "Activity_CryoBootDelayedLaunch",
                    setting.DisplayName);
            }
            else
            {
                SetItemFinalState(setting.EntryId, StartupDelayItemState.Failed, error);
                ActivityLogService.Instance.RecordDetail(
                    ActivitySeverity.Warning,
                    "startup",
                    "Activity_CryoBootLaunchFailed",
                    $"{setting.DisplayName}: {error}");
            }
        }

        private void SetItemFinalState(string entryId, StartupDelayItemState state, string error)
        {
            lock (_sync)
            {
                QueuedBootItem? item = _queue.FirstOrDefault(q =>
                    string.Equals(q.Setting.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.State = state;
                    item.Error = error;
                }

                _runtimeStates[entryId] = (state, error);
            }
        }

        private void AdvanceCompletionState()
        {
            bool completedNow = false;
            CryoBootQueueStatus snapshot;

            lock (_sync)
            {
                if (!_isRunning)
                {
                    return;
                }

                bool anyPending = _queue.Any(q =>
                    q.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle or StartupDelayItemState.Launching);

                if (!anyPending)
                {
                    CompleteQueueLocked(recordMarker: true);
                    completedNow = true;
                }
                else
                {
                    UpdateStatusLocked(DateTime.UtcNow);
                }

                snapshot = _currentStatus;
            }

            StatusChanged?.Invoke(snapshot);
            if (completedNow)
            {
                QueueCompleted?.Invoke();
            }
        }

        private void UpdateStatusLocked(DateTime nowUtc)
        {
            int total = _queue.Count;
            int remaining = _queue.Count(q =>
                q.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle or StartupDelayItemState.Launching);
            int completed = Math.Max(0, total - remaining);

            QueuedBootItem? next = _queue
                .Where(q => q.State is StartupDelayItemState.Pending or StartupDelayItemState.WaitingForIdle)
                .OrderBy(q => Math.Max(0, q.EffectiveDelaySeconds - (nowUtc - q.QueuedUtc).TotalSeconds))
                .ThenBy(q => q.Setting.PriorityOrder)
                .FirstOrDefault();

            int secsLeft = next == null
                ? 0
                : Math.Max(0, (int)Math.Ceiling(next.EffectiveDelaySeconds - (nowUtc - next.QueuedUtc).TotalSeconds));
            bool waitingIdle = next?.State == StartupDelayItemState.WaitingForIdle;

            _currentStatus = new CryoBootQueueStatus(
                IsActive: remaining > 0,
                TotalCount: total,
                CompletedCount: completed,
                RemainingCount: remaining,
                NextEntryName: next?.Setting.DisplayName ?? "",
                SecondsUntilNext: secsLeft,
                WaitingForIdle: waitingIdle);
        }

        private void CompleteQueueLocked(bool recordMarker)
        {
            _isRunning = false;
            _tickTimer?.Dispose();
            _tickTimer = null;
            DisposeTransientDiskCounter();
            _currentStatus = new CryoBootQueueStatus(false, _queue.Count, _queue.Count, 0, "", 0, false);

            if (recordMarker && _bootStartUtc != DateTime.MinValue)
            {
                WriteBootSessionMarker(_bootStartUtc);
            }
        }

        private void PrimeCpuBaseline()
        {
            if (NativeMethods.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _hasCpuBaseline = true;
            }
        }

        private double? SampleCpuLoadPercent()
        {
            double snapshotLoad = HardwareMonitorService.Instance.CurrentSnapshot.CpuLoad;
            if (double.IsFinite(snapshotLoad) && snapshotLoad >= 0 && snapshotLoad <= 100)
            {
                return snapshotLoad;
            }

            if (!NativeMethods.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                return null;
            }

            if (!_hasCpuBaseline)
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _hasCpuBaseline = true;
                return null;
            }

            ulong idleDelta = idle - _prevIdleTime;
            ulong kernelDelta = kernel - _prevKernelTime;
            ulong userDelta = user - _prevUserTime;
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;

            ulong total = kernelDelta + userDelta;
            if (total == 0)
            {
                return null;
            }

            return Math.Clamp((1.0 - ((double)idleDelta / total)) * 100.0, 0.0, 100.0);
        }

        private void InitializeTransientDiskCounter()
        {
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) != 0 || _pdhQuery == IntPtr.Zero)
                {
                    _pdhQuery = IntPtr.Zero;
                    return;
                }

                if (PdhAddEnglishCounterW(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _pdhDiskCounter) != 0 ||
                    _pdhDiskCounter == IntPtr.Zero)
                {
                    PdhCloseQuery(_pdhQuery);
                    _pdhQuery = IntPtr.Zero;
                    _pdhDiskCounter = IntPtr.Zero;
                    return;
                }

                PdhCollectQueryData(_pdhQuery);
                _hasDiskBaseline = false;
            }
            catch
            {
                _pdhQuery = IntPtr.Zero;
                _pdhDiskCounter = IntPtr.Zero;
            }
        }

        private double? SampleDiskWriteBytesPerSec()
        {
            if (_pdhQuery == IntPtr.Zero || _pdhDiskCounter == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                if (PdhCollectQueryData(_pdhQuery) != 0)
                {
                    return null;
                }

                if (!_hasDiskBaseline)
                {
                    _hasDiskBaseline = true;
                    return null;
                }

                if (PdhGetFormattedCounterValue(_pdhDiskCounter, 0x00000200, out _, out PdhFmtValue value) != 0)
                {
                    return null;
                }

                if (value.CStatus != 0 && value.CStatus != 1)
                {
                    return null;
                }

                return double.IsFinite(value.DoubleValue) ? Math.Max(0, value.DoubleValue) : null;
            }
            catch
            {
                return null;
            }
        }

        private void DisposeTransientDiskCounter()
        {
            if (_pdhQuery != IntPtr.Zero)
            {
                try
                {
                    PdhCloseQuery(_pdhQuery);
                }
                catch
                {
                }
                _pdhQuery = IntPtr.Zero;
                _pdhDiskCounter = IntPtr.Zero;
                _hasDiskBaseline = false;
            }
        }

        private static DateTime? ReadBootSessionMarker()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return null;
                }

                string json = File.ReadAllText(StateFilePath);
                BootSessionMarker? marker = JsonSerializer.Deserialize<BootSessionMarker>(json);
                return marker?.CompletedBootUtc;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteBootSessionMarker(DateTime completedBootUtc)
        {
            try
            {
                string? dir = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(new BootSessionMarker { CompletedBootUtc = completedBootUtc });
                File.WriteAllText(StateFilePath, json);
            }
            catch
            {
            }
        }

        public static void DeleteBootSessionMarker()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    File.Delete(StateFilePath);
                }
            }
            catch
            {
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
                CompleteQueueLocked(recordMarker: false);
            }
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PdhFmtValue pValue);

        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PdhFmtValue
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }
    }
}
