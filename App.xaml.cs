using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;
using CryoTray.Views;

namespace CryoTray
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _singleInstanceMutex;
        private TrayIconManager? _trayManager;
        private MainWindow? _mainWindow;
        private OverlayWindow? _overlayWindow;
        private HwndSource? _hotkeySource;
        private DispatcherTimer? _startupTrimTimer;
        private DispatcherTimer? _powerPlanWatchTimer;
        private DispatcherTimer? _updateCheckTimer;
        private TemperatureAlertService? _temperatureAlertService;
        private bool _updateCheckInProgress;
        private bool _isExiting;

        protected override void OnStartup(StartupEventArgs e)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            if (e.Args.Length > 0 && e.Args[0].Equals("/test", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.VerificationTests.RunAll();
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("/dump", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.CpuSensorDump.Run();
                Environment.Exit(exitCode);
                return;
            }

            const string appGuid = "CryoTray-Hardware-Monitor-Mutex-99210";
            _singleInstanceMutex = new Mutex(true, appGuid, out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            ApplyWindowAccent(PowerPlanService.Instance.IsCoolingModeActive);
            PowerPlanService.Instance.CoolingModeChanged += ApplyWindowAccent;

            DispatcherUnhandledException += (s, args) =>
            {
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
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoTray");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now}] Domain Exception: {args.ExceptionObject}\n");
                }
                catch { }
            };

            var settings = SettingsService.Instance.Current;
            LocalizationService.Instance.Apply(settings.Language);

            _trayManager = new TrayIconManager();
            _trayManager.OnOpenDashboardRequested += ToggleDashboard;
            _trayManager.OnToggleCoolingRequested += () => PowerPlanService.Instance.ToggleCoolingMode();
            _trayManager.OnOpenStressTestRequested += OpenStressTestFromTray;
            _trayManager.OnOpenSettingsRequested += OpenSettingsFromTray;
            _trayManager.OnExitRequested += ExitApplication;

            SettingsService.Instance.SettingsChanged += RegisterHotkeyIfEnabled;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            _temperatureAlertService = new TemperatureAlertService();
            _temperatureAlertService.AlertRaised += OnTemperatureAlertRaised;

            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
            HardwareMonitorService.Instance.Start();
            AutopilotService.Instance.Start();

            if (settings.EnableCoolingOnLaunch)
            {
                PowerPlanService.Instance.SetCoolingMode(true);
            }

            RegisterHotkeyIfEnabled();

            _powerPlanWatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _powerPlanWatchTimer.Tick += (s, args) => PowerPlanService.Instance.SyncCoolingStateFromSystem();
            _powerPlanWatchTimer.Start();

            if (!settings.LaunchInBackground)
            {
                ShowDashboard();
            }

            _startupTrimTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _startupTrimTimer.Tick += (s, args) =>
            {
                _startupTrimTimer?.Stop();
                _startupTrimTimer = null;
                TrimWorkingSet();
            };
            _startupTrimTimer.Start();

            ScheduleAutomaticUpdateCheck();
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

            if (!settings.UseHotkey)
            {
                GlobalHotkeyService.Instance.Unregister();
                if (_hotkeySource != null)
                {
                    _hotkeySource.RemoveHook(HwndHook);
                    _hotkeySource.Dispose();
                    _hotkeySource = null;
                }
                return;
            }

            if (_hotkeySource == null)
            {
                var parameters = new HwndSourceParameters("CryoTrayHotkey")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0,
                    ExtendedWindowStyle = 0,
                    ParentWindow = new IntPtr(-3)
                };

                _hotkeySource = new HwndSource(parameters);
                _hotkeySource.AddHook(HwndHook);
            }

            GlobalHotkeyService.Instance.Register(_hotkeySource.Handle, settings.HotkeyModifiers, settings.HotkeyKey);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                PowerPlanService.Instance.ToggleCoolingMode();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            _trayManager?.UpdateBadges(snapshot.GpuTemp, snapshot.CpuTemp, snapshot.CoolingModeActive);
            _temperatureAlertService?.Observe(snapshot);
        }

        private void OnSettingsChanged()
        {
            LocalizationService.Instance.Apply(SettingsService.Instance.Current.Language);
            _trayManager?.ApplyLanguage();
            ScheduleAutomaticUpdateCheck();
        }

        private void OnLanguageChanged()
        {
            _trayManager?.ApplyLanguage();
        }

        private void OnTemperatureAlertRaised(TemperatureAlert alert)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string current = FormatTemperature(alert.CurrentCelsius);
                string threshold = FormatTemperature(alert.ThresholdCelsius);
                string message = alert.Component switch
                {
                    TemperatureAlertComponent.Cpu => LocalizationService.Format("Alert_Cpu", current, threshold),
                    TemperatureAlertComponent.Gpu => LocalizationService.Format("Alert_Gpu", current, threshold),
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
                    System.Windows.Forms.ToolTipIcon.Warning);
            }));
        }

        public void ShowTestNotification()
        {
            ShowTrayNotification(
                LocalizationService.Get("Alert_TestTitle"),
                LocalizationService.Get("Alert_TestBody"),
                null,
                System.Windows.Forms.ToolTipIcon.Info);
        }

        public void ShowTrayNotification(string title, string message, Action? onClick = null,
            System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
        {
            _trayManager?.ShowNotification(title, message, onClick, icon);
        }

        private static string FormatTemperature(double celsius)
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double value = isFahrenheit ? (celsius * 9 / 5) + 32 : celsius;
            return $"{Math.Round(value)}°{(isFahrenheit ? "F" : "C")}";
        }

        private void ScheduleAutomaticUpdateCheck()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutomaticUpdateChecks || _updateCheckInProgress)
            {
                if (!settings.AutomaticUpdateChecks)
                {
                    _updateCheckTimer?.Stop();
                    _updateCheckTimer = null;
                }

                return;
            }

            if (_updateCheckTimer != null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            DateTime? lastCheck = settings.LastUpdateCheckUtc?.ToUniversalTime();
            TimeSpan delay = lastCheck.HasValue
                ? lastCheck.Value.AddHours(24) - now
                : TimeSpan.Zero;

            if (delay < TimeSpan.FromSeconds(20))
            {
                delay = TimeSpan.FromSeconds(20);
            }

            _updateCheckTimer = new DispatcherTimer { Interval = delay };
            _updateCheckTimer.Tick += UpdateCheckTimer_Tick;
            _updateCheckTimer.Start();
        }

        private async void UpdateCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (_updateCheckTimer != null)
            {
                _updateCheckTimer.Stop();
                _updateCheckTimer.Tick -= UpdateCheckTimer_Tick;
                _updateCheckTimer = null;
            }

            await RunAutomaticUpdateCheckAsync();
            ScheduleAutomaticUpdateCheck();
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

                if (release != null &&
                    UpdateService.IsNewer(release.Version, UpdateService.CurrentVersion) &&
                    !string.Equals(settings.LastNotifiedUpdateVersion, release.Version, StringComparison.Ordinal))
                {
                    settings.LastNotifiedUpdateVersion = release.Version;
                    ShowTrayNotification(
                        LocalizationService.Get("Update_NotificationTitle"),
                        LocalizationService.Format("Update_NotificationBody", release.Version),
                        () => ShowDashboard()?.OpenSettingsDialog(true),
                        System.Windows.Forms.ToolTipIcon.Info);
                }

                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsService.Instance.Save();
            }
            catch
            {
                try
                {
                    SettingsService.Instance.Current.LastUpdateCheckUtc = DateTime.UtcNow;
                    SettingsService.Instance.Save();
                }
                catch
                {
                }
            }
            finally
            {
                _updateCheckInProgress = false;
            }
        }

        public void TrimWorkingSet()
        {
            if (_isExiting)
            {
                return;
            }

            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();

            using var process = Process.GetCurrentProcess();
            NativeMethods.EmptyWorkingSet(process.Handle);
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _startupTrimTimer?.Stop();
            _startupTrimTimer = null;
            _powerPlanWatchTimer?.Stop();
            _powerPlanWatchTimer = null;
            _updateCheckTimer?.Stop();
            _updateCheckTimer = null;

            GlobalHotkeyService.Instance.Unregister();
            SettingsService.Instance.SettingsChanged -= RegisterHotkeyIfEnabled;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            if (_temperatureAlertService != null)
            {
                _temperatureAlertService.AlertRaised -= OnTemperatureAlertRaised;
                _temperatureAlertService = null;
            }

            if (_hotkeySource != null)
            {
                _hotkeySource.RemoveHook(HwndHook);
                _hotkeySource.Dispose();
                _hotkeySource = null;
            }

            var overlay = _overlayWindow;
            _overlayWindow = null;
            overlay?.Close();

            var main = _mainWindow;
            _mainWindow = null;
            main?.Close();

            AutopilotService.Instance.Stop();
            HardwareMonitorService.Instance.Stop();
            _trayManager?.Dispose();
            _trayManager = null;
            _singleInstanceMutex?.ReleaseMutex();
            Shutdown();
        }

        private void ApplyWindowAccent(bool isCooling)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                isCooling ? "#2B88D8" : "#C26A47"));
            brush.Freeze();
            Resources["WindowBorderBrush"] = brush;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isExiting = true;
            _startupTrimTimer?.Stop();
            _powerPlanWatchTimer?.Stop();
            _updateCheckTimer?.Stop();
            _updateCheckTimer = null;

            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            PowerPlanService.Instance.CoolingModeChanged -= ApplyWindowAccent;

            if (_hotkeySource != null)
            {
                _hotkeySource.RemoveHook(HwndHook);
                _hotkeySource.Dispose();
                _hotkeySource = null;
            }

            _trayManager?.Dispose();
            AutopilotService.Instance.Dispose();
            HardwareMonitorService.Instance.Dispose();
            base.OnExit(e);
        }
    }
}
