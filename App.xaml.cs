using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
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
        private HwndSource? _hwndSource;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 && e.Args[0].Equals("/test", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = Tests.VerificationTests.RunAll();
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

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoTray");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now}] Dispatcher Exception: {args.Exception}\n");
                }
                catch { }
                MessageBox.Show($"CryoTray encountered an issue:\n{args.Exception.Message}", "CryoTray Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Initialize Tray Icons
            _trayManager = new TrayIconManager();
            _trayManager.OnOpenDashboardRequested += ToggleDashboard;
            _trayManager.OnToggleCoolingRequested += () => PowerPlanService.Instance.ToggleCoolingMode();
            _trayManager.OnOpenStressTestRequested += () =>
            {
                if (_mainWindow != null)
                {
                    if (!_mainWindow.IsVisible)
                    {
                        _mainWindow.PositionAboveTray();
                        _mainWindow.Show();
                    }
                    var win = new StressTestWindow { Owner = _mainWindow };
                    _mainWindow.DockSideBySide(win);
                    win.ShowDialog();
                }
            };
            _trayManager.OnOpenSettingsRequested += () =>
            {
                if (_mainWindow != null)
                {
                    if (!_mainWindow.IsVisible)
                    {
                        _mainWindow.PositionAboveTray();
                        _mainWindow.Show();
                    }
                    var win = new SettingsWindow { Owner = _mainWindow };
                    _mainWindow.DockSideBySide(win);
                    win.ShowDialog();
                }
            };
            _trayManager.OnExitRequested += ExitApplication;

            // Initialize MainWindow
            _mainWindow = new MainWindow();

            // Hook HwndSource for global hotkeys
            _mainWindow.SourceInitialized += (s, args) =>
            {
                var handle = new WindowInteropHelper(_mainWindow).Handle;
                _hwndSource = HwndSource.FromHwnd(handle);
                _hwndSource?.AddHook(HwndHook);

                RegisterHotkeyIfEnabled();
            };

            SettingsService.Instance.SettingsChanged += RegisterHotkeyIfEnabled;

            // Start Hardware Monitor Service
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
            HardwareMonitorService.Instance.Start();

            // Check launch options
            if (settings.EnableCoolingOnLaunch)
            {
                PowerPlanService.Instance.SetCoolingMode(true);
            }

            if (!settings.LaunchInBackground)
            {
                _mainWindow.PositionAboveTray();
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        }

        private void RegisterHotkeyIfEnabled()
        {
            var s = SettingsService.Instance.Current;
            if (_mainWindow != null)
            {
                var handle = new WindowInteropHelper(_mainWindow).Handle;
                if (s.UseHotkey)
                {
                    GlobalHotkeyService.Instance.Register(handle, s.HotkeyModifiers, s.HotkeyKey);
                }
                else
                {
                    GlobalHotkeyService.Instance.Unregister();
                }
            }
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
        }

        private void ToggleDashboard()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.PositionAboveTray();
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }

        private void ExitApplication()
        {
            GlobalHotkeyService.Instance.Unregister();
            HardwareMonitorService.Instance.Stop();
            _trayManager?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            HardwareMonitorService.Instance.Dispose();
            base.OnExit(e);
        }
    }
}
