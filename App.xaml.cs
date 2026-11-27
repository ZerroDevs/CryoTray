using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;
using CryoTray.Views;
using Microsoft.Win32;

namespace CryoTray
{
    public partial class App : System.Windows.Application
    {
        public const string ActivateEventName = "CryoTray-Show-Dashboard-Event-99210";

        private static Mutex? _singleInstanceMutex;
        private EventWaitHandle? _activateWaitEvent;
        private RegisteredWaitHandle? _activateRegisteredWait;
        private TrayIconManager? _trayManager;
        private MainWindow? _mainWindow;
        private OverlayWindow? _overlayWindow;
        private DispatcherTimer? _powerPlanWatchTimer;
        private DispatcherTimer? _coolingPauseTimer;
        private DispatcherTimer? _updateCheckTimer;
        private TemperatureAlertService? _temperatureAlertService;
        private FanHealthService? _fanHealthService;
        private StorageHealthAlertService? _storageHealthAlertService;
        private readonly SensorDropoutWatch _sensorDropoutWatch = new();
        private DateTime _lastPowerPlanDriftNoticeUtc = DateTime.MinValue;
        private DownloadTimerWarningWindow? _downloadTimerWarningWindow;
        private bool _gpuPowerStartupRefreshStarted;
        private static readonly TimeSpan DashboardUpdateInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TrayUpdateInterval = TimeSpan.FromMinutes(30);
        private DateTime _lastAutomaticUpdateCheckUtc = DateTime.MinValue;
        private bool _dashboardVisible;
        private bool _updateCheckInProgress;
        private bool _coolingEnabledByBattery;
        private bool _isExiting;
        private bool _isPrimaryInstance;
        private bool _mutexReleased;
        private bool _startupHealthCheckPending;
        private bool _startupHealthCheckPostUpdate;
        private bool _startupHealthCheckFirstRun;

        public DateTime? CoolingPausedUntilUtc { get; private set; }
        public bool IsCoolingPausedByTimer =>
            CoolingPausedUntilUtc.HasValue && CoolingPausedUntilUtc.Value > DateTime.UtcNow;

        public ReleaseInfo? AvailableUpdate { get; private set; }
        public UpdateCompletionInfo? PendingUpdateSuccess { get; private set; }
        public event Action? UpdateAvailabilityChanged;
        public event Action? CoolingPauseChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            if (e.Args.Length > 0 && e.Args[0].Equals("/test", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.VerificationTests.RunAll();
                Console.Out.Flush();
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("/test-unit", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.VerificationTests.RunUnitSafe();
                Console.Out.Flush();
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Length > 0 &&
                e.Args[0].Equals("/test-updates", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.UpdateVerificationTests.RunAll();
                Console.Out.Flush();
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("/dump", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.CpuSensorDump.Run();
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("/uninstall-cleanup", StringComparison.OrdinalIgnoreCase))
            {
                PowerPlanService.Instance.CleanupForUninstall();
                Environment.Exit(0);
                return;
            }

            bool isAutostart = Array.Exists(
                e.Args,
                arg => arg.Equals(StartupTaskService.AutostartArg, StringComparison.OrdinalIgnoreCase));

            const string appGuid = "CryoTray-Hardware-Monitor-Mutex-99210";
            _singleInstanceMutex = new Mutex(true, appGuid, out bool createdNew);
            if (!createdNew)
            {
                if (!isAutostart)
                {
                    TrySignalExistingInstance(ActivateEventName);
                }
                Shutdown();
                return;
            }

            _isPrimaryInstance = true;
            RegisterActivationWaitHandle();

            base.OnStartup(e);

            TextScaleService.Initialize();
            ApplyWindowAccent();
            PowerPlanService.Instance.ProfileChanged += OnPowerPlanProfileChanged;
            PowerPlanService.Instance.CoolingModeChanged += OnPowerPlanCoolingChanged;
            PowerPlanService.Instance.ExternalPlanMissing += OnExternalPlanMissing;

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    TimerResolutionService.Instance.ReleaseForExit();
                    KeepAwakeService.Instance.ReleaseForExit();
                    GpuPowerControlService.Instance.RestoreForExit();
                    DownloadTimerService.Instance.Stop();
                }
                catch { }

                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoTray");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now}] Dispatcher Exception: {args.Exception}\n");
                }
                catch { }
                MessageBox.Show(
                    LocalizationService.Format("Error_Body", args.Exception.Message),
                    LocalizationService.Get("Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    TimerResolutionService.Instance.ReleaseForExit();
                    KeepAwakeService.Instance.ReleaseForExit();
                    GpuPowerControlService.Instance.RestoreForExit();
                    DownloadTimerService.Instance.Stop();
                }
                catch { }

                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoTray");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now}] Domain Exception: {args.ExceptionObject}\n");
                }
                catch { }
            };

            var settings = SettingsService.Instance.Current;
            LocalizationService.Instance.Apply(settings.Language);
            try
            {
#if !DEBUG
                // Debug builds and an administrator-created dev-mode marker may run from
                // user-writable output; release and published copies stay protected.
                if (!UpdateStorage.IsDeveloperModeEnabled)
                {
                    string applicationDirectory = AppContext.BaseDirectory.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!UpdateStorage.IsSafeApplicationLocation(applicationDirectory))
                    {
                        MessageBox.Show(
                            LocalizationService.Get("Update_UnsafeLocation"),
                            LocalizationService.Get("Error_Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Shutdown(1);
                        return;
                    }

                    UpdateStorage.EnsureApplicationDirectoryProtected(applicationDirectory);
                }
#endif
                UpdateStorage.EnsureUpdateRoot();
                UpdateStorage.EnsureRollbackRoot();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    LocalizationService.Get("Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            ThemeService.Initialize(settings.Theme);
            UpdateStatusService.Instance.Restore(settings);
            PendingUpdateSuccess = UpdateCompletionService.ReadPending(UpdateService.CurrentVersion);
            _startupHealthCheckPostUpdate = PendingUpdateSuccess != null;
            _startupHealthCheckFirstRun =
                !settings.HasCompletedFirstRunHealthCheck && PendingUpdateSuccess == null;
            _startupHealthCheckPending =
                _startupHealthCheckPostUpdate || _startupHealthCheckFirstRun;
            ActivityLogService.Instance.Record(
                ActivitySeverity.Info, "app", "Activity_AppStarted", UpdateService.CurrentVersion);
            if (PendingUpdateSuccess != null)
            {
                ActivityLogService.Instance.Record(
                    ActivitySeverity.Info, "update", "Activity_UpdateSucceeded",
                    PendingUpdateSuccess.ToVersion);
            }
            ThemeService.ThemeChanged += OnThemeChanged;
            StartupTaskService.EnsureSynchronized(settings.LaunchOnStartup);

            _trayManager = new TrayIconManager();
            _trayManager.ApplyTheme();
            _trayManager.OnOpenDashboardRequested += ToggleDashboard;
            _trayManager.OnToggleCoolingRequested += () => PowerPlanService.Instance.ToggleCoolingMode();
            _trayManager.OnPowerProfileRequested += key =>
            {
                var profile = PowerProfiles.Resolve(key, SettingsService.Instance.Current);
                if (profile != null)
                {
                    PowerPlanService.Instance.SwitchToProfile(profile);
                }
            };
            _trayManager.OnPauseCoolingRequested += PauseCoolingFor;
            _trayManager.OnCancelCoolingPauseRequested += () => CancelCoolingPause(resumeCooling: true, forceResume: true);
            _trayManager.OnToggleDownloadTimerRequested += ToggleDownloadTimerFromTray;
            _trayManager.OnKeepAwakeTimedRequested += StartKeepAwakeTimed;
            _trayManager.OnKeepAwakeIndefiniteRequested += StartKeepAwakeIndefiniteFromTray;
            _trayManager.OnKeepAwakeCustomRequested += StartKeepAwakeCustomFromTray;
            _trayManager.OnKeepAwakeStopRequested += () => KeepAwakeService.Instance.Stop();
            _trayManager.OnKeepAwakeDisplayOffChanged += allowDisplayOff =>
            {
                SettingsService.Instance.Current.KeepAwakeAllowDisplayOff = allowDisplayOff;
                SettingsService.Instance.Save();
            };
            _trayManager.OnOpenStressTestRequested += OpenStressTestFromTray;
            _trayManager.OnToggleOverlayClickThroughRequested += ToggleOverlayClickThrough;
            _trayManager.OnOpenSettingsRequested += OpenSettingsFromTray;
            _trayManager.OnExitRequested += ExitApplication;

            SettingsService.Instance.SettingsChanged += RegisterHotkeyIfEnabled;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            _temperatureAlertService = new TemperatureAlertService();
            _temperatureAlertService.AlertRaised += OnTemperatureAlertRaised;

            _fanHealthService = new FanHealthService();
            _fanHealthService.FanStopped += OnFanStopped;

            _storageHealthAlertService = new StorageHealthAlertService();
            _storageHealthAlertService.AlertRaised += OnStorageHealthAlertRaised;
            PowerPlanService.Instance.PlanDrifted += OnPowerPlanDrifted;

            RefreshTelemetryDemandBaseline();
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
            HardwareMonitorService.Instance.Start();
            DownloadTimerService.Instance.WarningStarted += OnDownloadTimerWarningStarted;
            DownloadTimerService.Instance.StatusChanged += OnDownloadTimerStatusChanged;
            DownloadTimerService.Instance.WarningEnded += OnDownloadTimerWarningEnded;
            DownloadTimerService.Instance.SyncWithSettings();
            AutopilotService.Instance.ExternalPauseHoldCheck = () => IsCoolingPausedByTimer;
            AutopilotService.Instance.StateChanged += OnAutopilotStateChanged;
            AutopilotService.Instance.Start();
            MemoryOptimizerService.Instance.OptimizationCompleted += OnMemoryOptimizationCompleted;
            MemoryOptimizerService.Instance.Start();
            TimerResolutionService.Instance.Start();
            StandbyPurgeService.Instance.Start();
            KeepAwakeService.Instance.Start();

            if (settings.EnableCoolingOnLaunch)
            {
                PowerPlanService.Instance.SetCoolingMode(true);
            }
            EvaluateBatteryCoolingRule();

            RegisterHotkeyIfEnabled();

            _powerPlanWatchTimer = new DispatcherTimer
            {
                Interval = settings.LaunchInBackground
                    ? TimeSpan.FromSeconds(4)
                    : TimeSpan.FromSeconds(2)
            };
            _powerPlanWatchTimer.Tick += (s, args) => PowerPlanService.Instance.SyncCoolingStateFromSystem();
            _powerPlanWatchTimer.Start();

            if (!settings.LaunchInBackground)
            {
                ShowDashboard();
            }

            MemoryTrimService.Instance.ScheduleTrim(MemoryTrimService.StartupDelay);

            ScheduleAutomaticUpdateCheck(checkNow: true);
        }

        public bool TryConsumeStartupHealthCheck(out bool postUpdate, out bool firstRun)
        {
            postUpdate = _startupHealthCheckPostUpdate;
            firstRun = _startupHealthCheckFirstRun;
            if (!_startupHealthCheckPending)
            {
                return false;
            }

            _startupHealthCheckPending = false;
            var settings = SettingsService.Instance.Current;
            if (!settings.HasCompletedFirstRunHealthCheck)
            {
                settings.HasCompletedFirstRunHealthCheck = true;
                SettingsService.Instance.Save();
            }

            return true;
        }

        internal static bool TrySignalExistingInstance(string eventName)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(eventName, out var existing))
                {
                    using (existing)
                    {
                        return existing.Set();
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void RegisterActivationWaitHandle()
        {
            try
            {
                _activateWaitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
                _activateRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
                    _activateWaitEvent,
                    (_, timedOut) =>
                    {
                        if (!timedOut)
                        {
                            ActivateDashboardFromSignal();
                        }
                    },
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch
            {
            }
        }

        private void UnregisterActivationWaitHandle()
        {
            try
            {
                _activateRegisteredWait?.Unregister(null);
            }
            catch
            {
            }
            _activateRegisteredWait = null;

            try
            {
                _activateWaitEvent?.Dispose();
            }
            catch
            {
            }
            _activateWaitEvent = null;
        }

        private void ActivateDashboardFromSignal()
        {
            if (_isExiting)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isExiting)
                {
                    return;
                }

                var window = ShowDashboard();
                if (window != null)
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(hwnd);
                    }
                }
            }));
        }

        private void ToggleOverlayClickThrough()
        {
            var settings = SettingsService.Instance.Current;
            settings.OverlayClickThrough = !settings.OverlayClickThrough;
            SettingsService.Instance.Save();
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            try
            {
                TimerResolutionService.Instance.ReleaseForExit();
                KeepAwakeService.Instance.ReleaseForExit();
                DownloadTimerService.Instance.CancelAndDisarm();
                GpuPowerControlService.Instance.RestoreForExit();
            }
            catch
            {
            }
        }

        public bool ConfirmKeepAwakeIndefinite()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(ConfirmKeepAwakeIndefinite);
            }

            var dialog = new KeepAwakeConfirmWindow(
                SettingsService.Instance.Current.KeepAwakeAllowDisplayOff);
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                dialog.Owner = _mainWindow;
            }

            return dialog.ShowDialog() == true;
        }

        public TimeSpan? PromptKeepAwakeDuration()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(PromptKeepAwakeDuration);
            }

            var dialog = new KeepAwakeDurationWindow();
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                dialog.Owner = _mainWindow;
            }

            return dialog.ShowDialog() == true ? dialog.Duration : null;
        }

        private void StartKeepAwakeTimed(TimeSpan duration)
        {
            KeepAwakeService.Instance.StartTimed(duration);
        }

        private void StartKeepAwakeIndefiniteFromTray()
        {
            if (ConfirmKeepAwakeIndefinite())
            {
                KeepAwakeService.Instance.StartIndefinite();
            }
        }

        private void StartKeepAwakeCustomFromTray()
        {
            TimeSpan? duration = PromptKeepAwakeDuration();
            if (duration.HasValue)
            {
                KeepAwakeService.Instance.StartTimed(duration.Value);
            }
        }

        private void OnSystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _fanHealthService?.Suspend();
                return;
            }

            if (e.Mode == PowerModes.Resume)
            {
                _fanHealthService?.Resume();
                if (!_isExiting)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        TryRunAutomaticUpdateCheck(TimeSpan.FromSeconds(30))));
                }
                return;
            }

            if (e.Mode != PowerModes.StatusChange || _isExiting)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(EvaluateBatteryCoolingRule));
        }

        private void EvaluateBatteryCoolingRule()
        {
            if (_isExiting || IsCoolingPausedByTimer)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            if (!settings.EnableCoolingOnBattery)
            {
                _coolingEnabledByBattery = false;
                return;
            }

            if (!NativeMethods.GetSystemPowerStatus(out var powerStatus))
            {
                return;
            }

            if (powerStatus.ACLineStatus == 0)
            {
                if (!PowerPlanService.Instance.IsCoolingModeActive)
                {
                    _coolingEnabledByBattery = true;
                    PowerPlanService.Instance.SetCoolingMode(true);
                }
            }
            else if (powerStatus.ACLineStatus == 1 && _coolingEnabledByBattery)
            {
                _coolingEnabledByBattery = false;
                if (PowerPlanService.Instance.IsCoolingModeActive)
                {
                    PowerPlanService.Instance.SetCoolingMode(false);
                }
            }
        }

        private void OnPowerPlanCoolingChanged(bool isCooling)
        {
            ActivityLogService.Instance.Record(
                ActivitySeverity.Info,
                "profile",
                isCooling ? "Activity_CoolingOn" : "Activity_CoolingOff");

            if (!isCooling || !CoolingPausedUntilUtc.HasValue)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => CancelCoolingPause(resumeCooling: false)));
        }

        public void PauseCoolingFor(TimeSpan duration)
        {
            if (_isExiting || duration <= TimeSpan.Zero)
            {
                return;
            }

            CoolingPausedUntilUtc = DateTime.UtcNow.Add(duration);
            _coolingEnabledByBattery = false;
            _trayManager?.SetCoolingPauseUntil(CoolingPausedUntilUtc);
            CoolingPauseChanged?.Invoke();

            if (PowerPlanService.Instance.IsCoolingModeActive)
            {
                PowerPlanService.Instance.SetCoolingMode(false);
            }

            if (_coolingPauseTimer == null)
            {
                _coolingPauseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _coolingPauseTimer.Tick += CoolingPauseTimer_Tick;
            }

            _coolingPauseTimer.Start();
        }

        public void CancelCoolingPause(bool resumeCooling = true, bool forceResume = false)
        {
            bool wasPaused = CoolingPausedUntilUtc.HasValue;
            _coolingPauseTimer?.Stop();
            CoolingPausedUntilUtc = null;
            _trayManager?.SetCoolingPauseUntil(null);
            if (wasPaused)
            {
                CoolingPauseChanged?.Invoke();
            }

            if (!resumeCooling || _isExiting)
            {
                return;
            }

            if (!forceResume &&
                (AutopilotService.Instance.ShouldHoldPause || AutopilotService.Instance.IsPauseConditionActive))
            {
                AutopilotService.Instance.TransferTimedPauseToAutopilot();
                return;
            }

            if (!PowerPlanService.Instance.IsCoolingModeActive)
            {
                PowerPlanService.Instance.SetCoolingMode(true);
            }
        }

        private void CoolingPauseTimer_Tick(object? sender, EventArgs e)
        {
            if (_isExiting)
            {
                _coolingPauseTimer?.Stop();
                return;
            }

            if (!CoolingPausedUntilUtc.HasValue || DateTime.UtcNow >= CoolingPausedUntilUtc.Value)
            {
                CancelCoolingPause(resumeCooling: true, forceResume: false);
                return;
            }

            _trayManager?.SetCoolingPauseUntil(CoolingPausedUntilUtc);
        }

        private MainWindow? EnsureMainWindow()
        {
            if (_mainWindow != null)
            {
                return _mainWindow;
            }

            var window = new MainWindow();
            window.Closed += MainWindow_Closed;
            _mainWindow = window;
            return window;
        }

        private MainWindow? ShowDashboard()
        {
            var window = EnsureMainWindow();
            if (window == null)
            {
                return null;
            }

            if (!window.IsVisible)
            {
                window.PositionAboveTray();
            }

            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
            return window;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _mainWindow))
            {
                _mainWindow = null;
            }

            if (!_isExiting)
            {
                TrimWorkingSet();
            }
        }

        private void ToggleDashboard()
        {
            if (_mainWindow != null && _mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Close();
            }
            else
            {
                ShowDashboard();
            }
        }

        private void OpenSettingsFromTray()
        {
            ShowDashboard()?.OpenSettingsDialog();
        }

        private void OpenStressTestFromTray()
        {
            ShowDashboard()?.OpenStressTestDialog();
        }

        private void ToggleDownloadTimerFromTray()
        {
            var settings = SettingsService.Instance.Current;
            settings.DownloadTimerEnabled = !settings.DownloadTimerEnabled;
            SettingsService.Instance.Save();
            DownloadTimerService.Instance.SyncWithSettings();
        }

        private void OnDownloadTimerWarningStarted()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isExiting)
                {
                    return;
                }

                CloseDownloadTimerWarning();
                var window = new DownloadTimerWarningWindow();
                _downloadTimerWarningWindow = window;
                window.Cancelled += () => DownloadTimerService.Instance.CancelWarning();
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_downloadTimerWarningWindow, window))
                    {
                        _downloadTimerWarningWindow = null;
                    }
                };
                window.UpdateStatus(DownloadTimerService.Instance.CurrentStatus);
                window.Show();
            }));
        }

        private void OnDownloadTimerStatusChanged(DownloadTimerStatus status)
        {
            if (status.Phase == DownloadTimerPhase.Warning)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _downloadTimerWarningWindow?.UpdateStatus(status);
                }));
            }
            else if (status.Phase == DownloadTimerPhase.Disarmed)
            {
                Dispatcher.BeginInvoke(new Action(() => _trayManager?.RefreshDownloadTimerCheck()));
            }
        }

        private void OnDownloadTimerWarningEnded()
        {
            Dispatcher.BeginInvoke(new Action(CloseDownloadTimerWarning));
        }

        private void CloseDownloadTimerWarning()
        {
            var window = _downloadTimerWarningWindow;
            _downloadTimerWarningWindow = null;
            window?.CloseWarning();
        }

        public void ToggleOverlay()
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                DisableOverlay();
            }
            else
            {
                EnableOverlay();
            }
        }

        private void EnableOverlay()
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow();
                _overlayWindow.Closed += OverlayWindow_Closed;
            }

            _overlayWindow.Show();
            _overlayWindow.Activate();

            var settings = SettingsService.Instance.Current;
            if (!settings.OverlayEnabled)
            {
                settings.OverlayEnabled = true;
                SettingsService.Instance.Save();
            }
        }

        private void DisableOverlay()
        {
            var overlay = _overlayWindow;
            _overlayWindow = null;

            if (overlay != null)
            {
                overlay.Closed -= OverlayWindow_Closed;
                overlay.Close();
            }

            var settings = SettingsService.Instance.Current;
            if (settings.OverlayEnabled)
            {
                settings.OverlayEnabled = false;
                SettingsService.Instance.Save();
            }

            TrimWorkingSet();
        }

        private void OverlayWindow_Closed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _overlayWindow))
            {
                _overlayWindow = null;
            }

            if (_isExiting)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            if (settings.OverlayEnabled)
            {
                settings.OverlayEnabled = false;
                SettingsService.Instance.Save();
            }

            TrimWorkingSet();
        }

        private void RegisterHotkeyIfEnabled()
        {
            var settings = SettingsService.Instance.Current;

            if (!settings.UseHotkey && !settings.UseRamHotkey)
            {
                GlobalHotkeyService.Instance.DisposeMessageWindow();
                return;
            }

            IntPtr sinkHwnd = GlobalHotkeyService.Instance.EnsureMessageWindow(OnGlobalHotkey);

            if (settings.UseHotkey)
            {
                GlobalHotkeyService.Instance.Register(sinkHwnd, settings.HotkeyModifiers, settings.HotkeyKey);
            }
            else
            {
                GlobalHotkeyService.Instance.Unregister();
            }

            if (settings.UseRamHotkey)
            {
                GlobalHotkeyService.Instance.RegisterRamHotkey(sinkHwnd, settings.RamHotkeyModifiers, settings.RamHotkeyKey);
            }
            else
            {
                GlobalHotkeyService.Instance.UnregisterRamHotkey();
            }
        }

        private void OnGlobalHotkey(int hotkeyId)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isExiting)
                {
                    return;
                }

                if (hotkeyId == GlobalHotkeyService.CoolingHotkeyId)
                {
                    PowerPlanService.Instance.ToggleCoolingMode();
                }
                else if (hotkeyId == GlobalHotkeyService.RamOptimizeHotkeyId)
                {
                    _ = MemoryOptimizerService.Instance.OptimizeAsync(MemoryOptimizationTrigger.Hotkey);
                }
            }));
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            if (!_gpuPowerStartupRefreshStarted && snapshot.Gpus.Count > 0)
            {
                _gpuPowerStartupRefreshStarted = true;
                _ = GpuPowerControlService.Instance.RefreshAsync();
            }

            _trayManager?.UpdateBadges(
                snapshot.GpuTemp,
                snapshot.CpuTemp,
                snapshot.CoolingModeActive,
                gpuLoad: snapshot.GpuLoad,
                cpuLoad: snapshot.CpuLoad,
                ramPercent: snapshot.RamUsagePercent);
            _temperatureAlertService?.Observe(snapshot);
            _fanHealthService?.Observe(snapshot);
            _storageHealthAlertService?.Observe(snapshot);

            if (SettingsService.Instance.Current.SensorDropoutLogEnabled)
            {
                SensorDropoutKind dropout = _sensorDropoutWatch.Observe(
                    SensorDropoutWatch.HasCpuReadings(snapshot),
                    SensorDropoutWatch.HasGpuReadings(snapshot),
                    DateTime.UtcNow);

                if (dropout != SensorDropoutKind.None)
                {
                    ActivityLogService.Instance.Record(
                        ActivitySeverity.Warning,
                        "sensors",
                        dropout == SensorDropoutKind.All
                            ? "Activity_SensorDropout"
                            : "Activity_CpuSensorsLost");
                }
            }
            else
            {
                // Do not let a gap that started while the feature was off fire on re-enable.
                _sensorDropoutWatch.Reset();
            }
        }

        private void OnSettingsChanged()
        {
            var settings = SettingsService.Instance.Current;
            LocalizationService.Instance.Apply(settings.Language);
            ThemeService.Apply(settings.Theme);
            ApplyWindowAccent();
            _trayManager?.ApplyLanguage();
            DownloadTimerService.Instance.SyncWithSettings();
            RefreshTelemetryDemandBaseline();
            EvaluateBatteryCoolingRule();
            ScheduleAutomaticUpdateCheck();
        }

        private static void RefreshTelemetryDemandBaseline()
        {
            var settings = SettingsService.Instance.Current;
            // The tray tooltip and menu consume live values even when badges are hidden.
            TelemetryDemand demand = TelemetryDemand.Tray;

            if (!settings.DisableAllNotifications &&
                (!settings.MuteTemperatureAlerts ||
                 settings.FanHealthAlertsEnabled ||
                 settings.StorageHealthAlertsEnabled ||
                 settings.GpuDeltaAlertsEnabled))
            {
                demand |= TelemetryDemand.Alerts;
            }

            if (settings.AutoRamOptimizeEnabled)
            {
                demand |= TelemetryDemand.Memory;
            }

            TelemetryDemandTracker.Instance.SetBaseline(demand);
        }

        private void OnLanguageChanged()
        {
            _trayManager?.ApplyLanguage();
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            ApplyWindowAccent();
            _trayManager?.ApplyTheme();
        }

        private void OnFanStopped(FanStoppedEvent alert)
        {
            ActivityLogService.Instance.Record(
                ActivitySeverity.Warning,
                "alert",
                "Activity_FanStopped",
                FanHealthService.GetDisplayName(alert.FanKey, alert.FanName, alert.IsGpuFan));
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string fanName = FanHealthService.GetDisplayName(
                    alert.FanKey, alert.FanName, alert.IsGpuFan);
                ShowTrayNotification(
                    LocalizationService.Get("Alert_FanTitle"),
                    LocalizationService.Format("Alert_FanStopped", fanName, alert.LastRpm),
                    () => ShowDashboard()?.OpenSystemInfoDialog(alert.FanKey),
                    TrayNotificationIcon.Warning);
            }));
        }

        private void OnStorageHealthAlertRaised(StorageHealthAlert alert)
        {
            ActivityLogService.Instance.Record(
                ActivitySeverity.Warning,
                "alert",
                "Activity_DriveHealth",
                alert.DriveName,
                alert.HealthPercent);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowTrayNotification(
                    LocalizationService.Get("Alert_DriveHealthTitle"),
                    LocalizationService.Format(
                        "Alert_DriveHealthBody",
                        alert.DriveName,
                        alert.HealthPercent,
                        LocalizationService.Health(alert.StatusText)),
                    () => ShowDashboard(),
                    TrayNotificationIcon.Warning);
            }));
        }

        private void OnPowerPlanDrifted(PowerPlanService.PowerPlanDrift drift)
        {
            if (!SettingsService.Instance.Current.PowerPlanDriftNoticeEnabled)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (_lastPowerPlanDriftNoticeUtc != DateTime.MinValue &&
                nowUtc - _lastPowerPlanDriftNoticeUtc < TimeSpan.FromMinutes(10))
            {
                return;
            }

            _lastPowerPlanDriftNoticeUtc = nowUtc;

            ActivityLogService.Instance.Record(
                ActivitySeverity.Warning,
                "power",
                "Activity_PowerPlanDrift",
                drift.PreviousName,
                drift.NewName);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowTrayNotification(
                    LocalizationService.Get("Alert_PowerPlanDriftTitle"),
                    LocalizationService.Format(
                        "Alert_PowerPlanDriftBody",
                        drift.PreviousName,
                        drift.NewName),
                    () => ShowDashboard(),
                    TrayNotificationIcon.Info);
            }));
        }

        private void OnExternalPlanMissing(string planName)
        {
            ActivityLogService.Instance.Record(
                ActivitySeverity.Warning,
                "power",
                "Activity_ExternalPlanRemoved",
                planName);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowTrayNotification(
                    LocalizationService.Get("Alert_ExternalPlanMissingTitle"),
                    LocalizationService.Format("Alert_ExternalPlanMissingBody", planName),
                    () => ShowDashboard(),
                    TrayNotificationIcon.Warning);
            }));
        }

        private void OnTemperatureAlertRaised(TemperatureAlert alert)
        {
            ActivityLogService.Instance.RecordDetail(
                ActivitySeverity.Warning,
                "alert",
                "Activity_TemperatureAlert",
                alert.Component.ToString());
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string current = FormatTemperature(alert.CurrentCelsius);
                string threshold = FormatTemperature(alert.ThresholdCelsius);
                string message = alert.Component switch
                {
                    TemperatureAlertComponent.Cpu => LocalizationService.Format("Alert_Cpu", current, threshold),
                    TemperatureAlertComponent.Gpu => LocalizationService.Format("Alert_Gpu", current, threshold),
                    TemperatureAlertComponent.GpuDelta => LocalizationService.Format(
                        "Alert_GpuDelta",
                        FormatTemperatureDelta(alert.CurrentCelsius),
                        FormatTemperatureDelta(alert.ThresholdCelsius)),
                    TemperatureAlertComponent.GpuHotspot => LocalizationService.Format("Alert_GpuHotspot", current, threshold),
                    TemperatureAlertComponent.GpuVram => LocalizationService.Format("Alert_GpuVram", current, threshold),
                    _ => LocalizationService.Format(
                        "Alert_Drive",
                        alert.DriveName ?? LocalizationService.Format("Alert_DriveName", 1),
                        current,
                        threshold)
                };

                ShowTrayNotification(
                    LocalizationService.Get("Alert_Title"),
                    message,
                    () => ShowDashboard(),
                    TrayNotificationIcon.Warning);
            }));
        }

        public void ShowTestNotification()
        {
            ShowTrayNotification(
                LocalizationService.Get("Alert_TestTitle"),
                LocalizationService.Get("Alert_TestBody"),
                null,
                TrayNotificationIcon.Info);
        }

        public void ShowTrayNotification(string title, string message, Action? onClick = null,
            TrayNotificationIcon icon = TrayNotificationIcon.Info)
        {
            if (SettingsService.Instance.Current.DisableAllNotifications)
            {
                return;
            }

            _trayManager?.ShowNotification(title, message, onClick, icon);
        }

        public FanHealthService? FanHealth => _fanHealthService;

        private static string FormatTemperature(double celsius)
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double value = isFahrenheit ? (celsius * 9 / 5) + 32 : celsius;
            return $"{Math.Round(value)}°{(isFahrenheit ? "F" : "C")}";
        }

        private static string FormatTemperatureDelta(double celsius)
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double value = isFahrenheit ? celsius * 9 / 5 : celsius;
            return $"{Math.Round(value)}°{(isFahrenheit ? "F" : "C")}";
        }

        private void ScheduleAutomaticUpdateCheck(bool checkNow = false)
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutomaticUpdateChecks)
            {
                if (_updateCheckTimer != null)
                {
                    _updateCheckTimer.Stop();
                    _updateCheckTimer.Tick -= UpdateCheckTimer_Tick;
                    _updateCheckTimer = null;
                }
                return;
            }

            TimeSpan interval = _dashboardVisible ? DashboardUpdateInterval : TrayUpdateInterval;
            bool hadTimer = _updateCheckTimer != null;
            if (hadTimer && _updateCheckTimer!.Interval == interval)
            {
                if (checkNow)
                {
                    TryRunAutomaticUpdateCheck(TimeSpan.Zero);
                }
                return;
            }

            if (_updateCheckTimer != null)
            {
                _updateCheckTimer.Stop();
                _updateCheckTimer.Tick -= UpdateCheckTimer_Tick;
            }

            _updateCheckTimer = new DispatcherTimer { Interval = interval };
            _updateCheckTimer.Tick += UpdateCheckTimer_Tick;
            _updateCheckTimer.Start();

            if (checkNow || !hadTimer)
            {
                TryRunAutomaticUpdateCheck(TimeSpan.Zero);
            }
        }

        private void UpdateCheckTimer_Tick(object? sender, EventArgs e)
        {
            TryRunAutomaticUpdateCheck(TimeSpan.Zero);
        }

        private void TryRunAutomaticUpdateCheck(TimeSpan minimumGap)
        {
            if (_isExiting || _updateCheckInProgress)
            {
                return;
            }

            if (!SettingsService.Instance.Current.AutomaticUpdateChecks)
            {
                return;
            }

            if (minimumGap > TimeSpan.Zero &&
                DateTime.UtcNow - _lastAutomaticUpdateCheckUtc < minimumGap)
            {
                return;
            }

            _lastAutomaticUpdateCheckUtc = DateTime.UtcNow;
            _ = RunAutomaticUpdateCheckAsync();
        }

        public void OnDashboardVisibilityChanged(bool visible)
        {
            if (_isExiting || _dashboardVisible == visible)
            {
                return;
            }

            _dashboardVisible = visible;
            if (_powerPlanWatchTimer != null)
            {
                _powerPlanWatchTimer.Interval = visible
                    ? TimeSpan.FromSeconds(2)
                    : TimeSpan.FromSeconds(4);
            }
            ScheduleAutomaticUpdateCheck();
            if (visible)
            {
                TryRunAutomaticUpdateCheck(DashboardUpdateInterval);
            }
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable || _isExiting)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
                TryRunAutomaticUpdateCheck(TimeSpan.FromSeconds(30))));
        }

        public void SetAvailableUpdate(ReleaseInfo? release)
        {
            if (string.Equals(AvailableUpdate?.Version, release?.Version, StringComparison.Ordinal))
            {
                return;
            }

            AvailableUpdate = release;
            if (release != null)
            {
                ActivityLogService.Instance.Record(
                    ActivitySeverity.Info, "update", "Activity_UpdateAvailable", release.Version);
            }
            UpdateAvailabilityChanged?.Invoke();
        }

        public void ConsumeUpdateSuccess()
        {
            PendingUpdateSuccess = null;
            UpdateCompletionService.Clear();
        }

        private async System.Threading.Tasks.Task RunAutomaticUpdateCheckAsync()
        {
            if (_updateCheckInProgress)
            {
                return;
            }

            _updateCheckInProgress = true;
            try
            {
                var release = await UpdateService.Instance.GetLatestReleaseAsync(
                    System.Threading.CancellationToken.None);
                var settings = SettingsService.Instance.Current;

                ReleaseInfo? available = release != null &&
                    UpdateService.IsNewer(release.Version, UpdateService.CurrentVersion)
                    ? release
                    : null;

                SetAvailableUpdate(available);

                if (release == null)
                {
                    UpdateStatusService.Instance.Record(UpdateCheckOutcome.NoReleases, "");
                }
                else if (available == null)
                {
                    UpdateStatusService.Instance.Record(
                        UpdateCheckOutcome.UpToDate, UpdateService.CurrentVersion);
                }
                else
                {
                    UpdateStatusService.Instance.Record(
                        UpdateCheckOutcome.Available, available.Version);
                }

                bool notified = false;
                if (available != null &&
                    !string.Equals(settings.LastNotifiedUpdateVersion, available.Version, StringComparison.Ordinal) &&
                    !settings.DisableAllNotifications)
                {
                    settings.LastNotifiedUpdateVersion = available.Version;
                    notified = true;
                    ShowTrayNotification(
                        LocalizationService.Get("Update_NotificationTitle"),
                        LocalizationService.Format("Update_NotificationBody", available.Version),
                        () => ShowDashboard()?.OpenSettingsDialog(true, available, false),
                        TrayNotificationIcon.Info);
                }

                if (notified)
                {
                    SettingsService.Instance.Save();
                }
            }
            catch (Exception ex)
            {
                UpdateStatusService.Instance.Record(
                    UpdateCheckOutcome.Failed, string.Empty, ex.Message);
                ActivityLogService.Instance.RecordDetail(
                    ActivitySeverity.Warning, "update", "Activity_UpdateFailed", ex.Message);
            }
            finally
            {
                _updateCheckInProgress = false;
                if (!_isExiting)
                {
                    MemoryTrimService.Instance.ScheduleTrim(TimeSpan.FromSeconds(2));
                }
            }
        }

        public void TrimWorkingSet()
        {
            if (_isExiting)
            {
                return;
            }

            MemoryTrimService.Instance.ScheduleTrim();
        }

        private void ExitApplication()
        {
            if (!ConfirmExitWithCoolingMode())
            {
                return;
            }

            _isExiting = true;
            MemoryTrimService.Instance.CancelPending(shuttingDown: true);
            _powerPlanWatchTimer?.Stop();
            _powerPlanWatchTimer = null;
            _coolingPauseTimer?.Stop();
            _coolingPauseTimer = null;
            CoolingPausedUntilUtc = null;
            _updateCheckTimer?.Stop();
            _updateCheckTimer = null;

            GlobalHotkeyService.Instance.Unregister();
            GlobalHotkeyService.Instance.UnregisterRamHotkey();
            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            PowerPlanService.Instance.CoolingModeChanged -= OnPowerPlanCoolingChanged;
            UnregisterActivationWaitHandle();
            AutopilotService.Instance.ExternalPauseHoldCheck = null;
            AutopilotService.Instance.StateChanged -= OnAutopilotStateChanged;
            MemoryOptimizerService.Instance.OptimizationCompleted -= OnMemoryOptimizationCompleted;
            SettingsService.Instance.SettingsChanged -= RegisterHotkeyIfEnabled;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            DownloadTimerService.Instance.WarningStarted -= OnDownloadTimerWarningStarted;
            DownloadTimerService.Instance.StatusChanged -= OnDownloadTimerStatusChanged;
            DownloadTimerService.Instance.WarningEnded -= OnDownloadTimerWarningEnded;
            DownloadTimerService.Instance.Dispose();
            GpuPowerControlService.Instance.Dispose();
            CloseDownloadTimerWarning();
            if (_temperatureAlertService != null)
            {
                _temperatureAlertService.AlertRaised -= OnTemperatureAlertRaised;
                _temperatureAlertService = null;
            }

            if (_fanHealthService != null)
            {
                _fanHealthService.FanStopped -= OnFanStopped;
                _fanHealthService = null;
            }

            if (_storageHealthAlertService != null)
            {
                _storageHealthAlertService.AlertRaised -= OnStorageHealthAlertRaised;
                _storageHealthAlertService = null;
            }

            GlobalHotkeyService.Instance.DisposeMessageWindow();

            var overlay = _overlayWindow;
            _overlayWindow = null;
            overlay?.Close();

            var main = _mainWindow;
            _mainWindow = null;
            main?.Close();

            MemoryOptimizerService.Instance.Stop();
            AutopilotService.Instance.Stop();
            HardwareMonitorService.Instance.Stop();
            TelemetryCsvService.Instance.Dispose();
            _trayManager?.Dispose();
            _trayManager = null;
            ReleaseSingleInstanceMutex();
            Shutdown();
        }

        private void ReleaseSingleInstanceMutex()
        {
            if (_mutexReleased)
            {
                return;
            }

            _mutexReleased = true;
            var mutex = _singleInstanceMutex;
            _singleInstanceMutex = null;
            if (mutex == null)
            {
                return;
            }

            try
            {
                if (_isPrimaryInstance)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch
            {
            }

            mutex.Dispose();
        }

        private bool ConfirmExitWithCoolingMode()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.DisableCoolingModeOnExit ||
                !PowerPlanService.Instance.IsCoolingModeActive)
            {
                return true;
            }

            var owner = _mainWindow != null &&
                        _mainWindow.IsVisible &&
                        _mainWindow.WindowState != WindowState.Minimized
                ? _mainWindow
                : null;
            owner?.SetExitConfirmationOpen(true);

            try
            {
                var dialog = new ConfirmExitWindow();
                if (owner != null)
                {
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }

                if (dialog.ShowDialog() != true)
                {
                    return false;
                }
            }
            finally
            {
                owner?.SetExitConfirmationOpen(false);
            }

            DisableCoolingForExit();
            return true;
        }

        private void DisableCoolingForExit()
        {
            if (!_isPrimaryInstance)
            {
                return;
            }

            try
            {
                var activeProfile = PowerPlanService.Instance.ActiveProfile;
                if (SettingsService.Instance.Current.DisableCoolingModeOnExit &&
                    activeProfile != null &&
                    !activeProfile.IsWindowsDefault)
                {
                    PowerPlanService.Instance.RestoreOriginalPowerPlan();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to disable Cooling Mode on exit: {ex.Message}");
            }
        }

        public const string DefaultCoolingOnFrameColor = "#2B88D8";
        public const string DefaultCoolingOffFrameColor = "#C26A47";

        internal static Color ResolveFrameColor(string? configuredHex, string fallbackHex)
        {
            if (!string.IsNullOrWhiteSpace(configuredHex))
            {
                try
                {
                    if (ColorConverter.ConvertFromString(configuredHex.Trim()) is Color parsed)
                    {
                        return Color.FromRgb(parsed.R, parsed.G, parsed.B);
                    }
                }
                catch
                {
                }
            }

            var fallback = (Color)ColorConverter.ConvertFromString(fallbackHex);
            return Color.FromRgb(fallback.R, fallback.G, fallback.B);
        }

        internal static string NormalizeHexColor(Color color) =>
            $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private void OnPowerPlanProfileChanged()
        {
            var profile = PowerPlanService.Instance.ActiveProfile;
            ActivityLogService.Instance.Record(
                ActivitySeverity.Info,
                "profile",
                "Activity_ProfileChanged",
                profile == null ? LocalizationService.Get("Common_Unknown")
                    : LocalizationService.GetProfileName(profile));

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ApplyWindowAccent));
                return;
            }

            ApplyWindowAccent();
        }

        private void OnAutopilotStateChanged(AutopilotState state)
        {
            if (!state.IsActive)
            {
                ActivityLogService.Instance.Record(
                    ActivitySeverity.Info, "automation", "Activity_GameModeOff");
                return;
            }

            var resolvedProfile = PowerProfiles.Resolve(state.ProfileKey, SettingsService.Instance.Current);
            string profileName = resolvedProfile != null
                ? LocalizationService.GetProfileName(resolvedProfile)
                : LocalizationService.Get("Common_Unknown");
            string targetName = string.IsNullOrWhiteSpace(state.ProcessName)
                ? LocalizationService.Get("Activity_FullscreenGame")
                : state.ProcessName;
            ActivityLogService.Instance.Record(
                ActivitySeverity.Info,
                "automation",
                "Activity_GameModeOn",
                profileName,
                targetName);
        }

        private void OnMemoryOptimizationCompleted(MemoryOptimizationResult result)
        {
            if (!result.Succeeded)
            {
                return;
            }

            ActivityLogService.Instance.Record(
                ActivitySeverity.Info,
                "memory",
                "Activity_RamOptimized",
                MemoryTelemetryService.FormatBytes(result.FreedBytes));
        }

        internal static Color ResolveWindowAccentColor(PowerProfileDefinition? profile, AppSettings settings)
        {
            if (profile?.Key == PowerProfiles.CoolingKey || profile?.Key == PowerProfiles.LegacyKey)
            {
                return ResolveFrameColor(settings.CoolingOnFrameColor, DefaultCoolingOnFrameColor);
            }

            if (profile == null || profile.IsWindowsDefault || profile.Key == PowerProfiles.GamingHighKey)
            {
                return ResolveFrameColor(settings.CoolingOffFrameColor, DefaultCoolingOffFrameColor);
            }

            return (Color)ColorConverter.ConvertFromString(profile.Palette.Accent);
        }

        private void ApplyWindowAccent()
        {
            if (ThemeService.IsHighContrast)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            var profile = PowerPlanService.Instance.ActiveProfile;
            Color color = ResolveWindowAccentColor(profile, settings);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Resources["WindowBorderBrush"] = brush;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ActivityLogService.Instance.Record(
                ActivitySeverity.Info, "app", "Activity_AppExited");
            DisableCoolingForExit();
            _isExiting = true;
            MemoryTrimService.Instance.CancelPending(shuttingDown: true);
            _powerPlanWatchTimer?.Stop();
            _coolingPauseTimer?.Stop();
            _coolingPauseTimer = null;
            CoolingPausedUntilUtc = null;
            _updateCheckTimer?.Stop();
            _updateCheckTimer = null;

            DownloadTimerService.Instance.WarningStarted -= OnDownloadTimerWarningStarted;
            DownloadTimerService.Instance.StatusChanged -= OnDownloadTimerStatusChanged;
            DownloadTimerService.Instance.WarningEnded -= OnDownloadTimerWarningEnded;
            TimerResolutionService.Instance.Dispose();
            KeepAwakeService.Instance.Dispose();
            StandbyPurgeService.Instance.Dispose();
            DownloadTimerService.Instance.Dispose();
            GpuPowerControlService.Instance.Dispose();
            CloseDownloadTimerWarning();
            SystemEvents.SessionEnding -= OnSessionEnding;

            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            PowerPlanService.Instance.ProfileChanged -= OnPowerPlanProfileChanged;
            PowerPlanService.Instance.CoolingModeChanged -= OnPowerPlanCoolingChanged;
            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            SettingsService.Instance.SettingsChanged -= RegisterHotkeyIfEnabled;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            ThemeService.Shutdown();
            UnregisterActivationWaitHandle();
            AutopilotService.Instance.ExternalPauseHoldCheck = null;
            AutopilotService.Instance.StateChanged -= OnAutopilotStateChanged;
            MemoryOptimizerService.Instance.OptimizationCompleted -= OnMemoryOptimizationCompleted;

            GlobalHotkeyService.Instance.DisposeMessageWindow();
            _trayManager?.Dispose();
            MemoryOptimizerService.Instance.Dispose();
            TelemetryCsvService.Instance.Dispose();
            AutopilotService.Instance.Dispose();
            HardwareMonitorService.Instance.Dispose();
            ReleaseSingleInstanceMutex();
            base.OnExit(e);
        }
    }
}
