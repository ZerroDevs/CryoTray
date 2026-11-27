using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Tray
{
    public enum TrayNotificationIcon
    {
        None = 0,
        Info = 1,
        Warning = 2
    }

    public class TrayIconManager : IDisposable
    {
        private const int IconSize = 32;
        private const int IconHalfHeight = IconSize / 2;
        private const double MainMenuMinWidth = 212;
        private const double SubMenuMinWidth = 196;
        private const uint WmTrayIcon = NativeMethods.WM_USER + 0x201;
        private const uint SlotGpu = 1;
        private const uint SlotCpu = 2;
        private const uint SlotRam = 3;

        private sealed class TraySlot
        {
            public required uint Id { get; init; }
            public bool Added { get; set; }
            public bool Visible { get; set; }
            public string Tooltip { get; set; } = "CryoTray";
            public IntPtr HIcon { get; set; }
        }

        private readonly object _renderLock = new();
        private readonly TraySlot _gpuSlot = new() { Id = SlotGpu };
        private readonly TraySlot _cpuSlot = new() { Id = SlotCpu };
        private readonly TraySlot _ramSlot = new() { Id = SlotRam };

        private IntPtr _hwnd;
        private IntPtr _hInstance;
        private readonly string _windowClassName;
        private NativeMethods.WndProcDelegate? _wndProcDelegate;
        private uint _wmTaskbarCreated;

        private Window? _menuHostWindow;
        private ContextMenu? _contextMenu;
        private MenuItem? _itemOpen;
        private MenuItem? _itemCooling;
        private MenuItem? _itemPowerProfiles;
        private MenuItem? _itemPauseCooling;
        private MenuItem? _itemPause15m;
        private MenuItem? _itemPause30m;
        private MenuItem? _itemPause60m;
        private MenuItem? _itemResumeNow;
        private MenuItem? _itemKeepAwake;
        private MenuItem? _itemKeepAwakeOff;
        private MenuItem? _itemKeepAwake15;
        private MenuItem? _itemKeepAwake30;
        private MenuItem? _itemKeepAwake60;
        private MenuItem? _itemKeepAwake120;
        private MenuItem? _itemKeepAwakeCustom;
        private MenuItem? _itemKeepAwakeIndefinite;
        private MenuItem? _itemKeepAwakeStop;
        private MenuItem? _itemKeepAwakeDisplayOff;
        private MenuItem? _itemDownloadTimer;
        private MenuItem? _itemStress;
        private MenuItem? _itemOverlayClickThrough;
        private MenuItem? _itemSettings;
        private MenuItem? _itemExit;
        private readonly Dictionary<string, MenuItem> _powerProfileItems = new(StringComparer.OrdinalIgnoreCase);

        private TraySlot? _notificationSlot;
        private Action? _notificationAction;
        private DateTime _notificationHoldUntilUtc;
        private DateTime? _coolingPausedUntilUtc;
        private string _lastPowerProfileMenuSignature = "";
        private double _lastGpuTemp = double.NaN;
        private double _lastCpuTemp = double.NaN;
        private double _lastGpuLoad = double.NaN;
        private double _lastCpuLoad = double.NaN;
        private double _lastRamPercent = double.NaN;
        private bool _lastCoolingModeActive;
        private string _lastBadgeSignature = "";
        private BadgeSurface? _badgeSurface;
        private bool _menuPaletteDirty = true;
        private bool _disposed;

        // Palette state for WPF ContextMenu (dark by default, light-synced on ThemeService.IsLight)
        private Color _surfaceBg = Color.FromRgb(30, 30, 30);
        private Color _surfaceBorder = Color.FromRgb(58, 58, 58);
        private Color _hoverFill = Color.FromRgb(45, 45, 45);
        private Color _hoverBorder = Color.FromRgb(66, 66, 66);
        private Color _separatorLine = Color.FromRgb(51, 51, 51);
        private Color _textNormal = Color.FromRgb(234, 234, 234);
        private Color _textDisabled = Color.FromRgb(120, 120, 120);
        private Color _chevronColor = Color.FromRgb(190, 190, 190);
        private Color _checkBoxFill = Color.FromRgb(20, 20, 20);
        private Color _checkBoxUncheckedBorder = Color.FromRgb(120, 120, 120);
        private Color _checkBoxCheckedBorder = Color.FromRgb(41, 182, 246);
        private Color _checkMarkColor = Colors.White;

        public event Action? OnOpenDashboardRequested;
        public event Action? OnToggleCoolingRequested;
        public event Action<string>? OnPowerProfileRequested;
        public event Action<TimeSpan>? OnPauseCoolingRequested;
        public event Action? OnCancelCoolingPauseRequested;
        public event Action? OnToggleDownloadTimerRequested;
        public event Action<TimeSpan>? OnKeepAwakeTimedRequested;
        public event Action? OnKeepAwakeIndefiniteRequested;
        public event Action? OnKeepAwakeCustomRequested;
        public event Action? OnKeepAwakeStopRequested;
        public event Action<bool>? OnKeepAwakeDisplayOffChanged;
        public event Action? OnOpenStressTestRequested;
        public event Action? OnToggleOverlayClickThroughRequested;
        public event Action? OnOpenSettingsRequested;
        public event Action? OnExitRequested;

        // Camomile tray badge palette (pastel for black digits, deeper contrast-safe tones for white digits).
        private static readonly Color GpuGreen = Color.FromRgb(0xB3, 0xD8, 0x72);
        private static readonly Color CpuAmber = Color.FromRgb(0xFF, 0xBD, 0xAF);
        private static readonly Color CpuCyan = Color.FromRgb(0x8A, 0xD2, 0xF1);
        private static readonly Color MergedCpuOff = Color.FromRgb(0xE5, 0x98, 0x7E);
        private static readonly Color MergedCpuOn = Color.FromRgb(0x4F, 0xC3, 0xF7);
        private static readonly Color MergedGpuGreen = Color.FromRgb(0x7C, 0xB3, 0x42);
        private static readonly Color GpuGreenWhite = Color.FromRgb(0x4C, 0x80, 0x28);
        private static readonly Color CpuAmberWhite = Color.FromRgb(0xC4, 0x48, 0x30);
        private static readonly Color CpuCyanWhite = Color.FromRgb(0x1B, 0x6C, 0xB3);
        private static readonly Color RamBadgeBlue = ParseHexColor(AppSettings.DefaultRamTrayBadgeColor, Color.FromRgb(0x3B, 0x82, 0xF6));
        private static readonly Color BadgeInk = Color.FromRgb(20, 20, 20);
        private static readonly Color DividerColor = Color.FromRgb(20, 20, 20);

        private static readonly FontFamily MenuFontFamily = new("Segoe UI Variable Text, Segoe UI, Tahoma, Arial");

        public TrayIconManager()
        {
            _windowClassName = $"CryoTraySink_{Guid.NewGuid():N}";
            _hInstance = NativeMethods.GetModuleHandleW(null);
            _wmTaskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
            _wndProcDelegate = WndProc;

            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = _hInstance,
                lpszClassName = _windowClassName
            };
            NativeMethods.RegisterClassExW(ref wc);

            _hwnd = NativeMethods.CreateWindowExW(
                0,
                _windowClassName,
                "CryoTraySink",
                0,
                0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE,
                IntPtr.Zero,
                _hInstance,
                IntPtr.Zero);

            // Preserve taskbar left/right ordering: GPU first, then CPU, then RAM.
            AddSlot(_gpuSlot, visible: true);
            AddSlot(_cpuSlot, visible: true);
            AddSlot(_ramSlot, visible: SettingsService.Instance.Current.ShowRamBadge);

            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            KeepAwakeService.Instance.StatusChanged += OnKeepAwakeStatusChanged;
            PowerPlanService.Instance.ProfileChanged += OnPowerPlanProfileChanged;

            ApplyTheme();
            UpdateBadges(double.NaN, double.NaN, PowerPlanService.Instance.IsCoolingModeActive, true);
        }

        private void AddSlot(TraySlot slot, bool visible)
        {
            slot.Visible = visible;
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = slot.Id,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP | NativeMethods.NIF_STATE |
                         (slot.HIcon != IntPtr.Zero ? NativeMethods.NIF_ICON : 0u),
                uCallbackMessage = WmTrayIcon,
                hIcon = slot.HIcon,
                szTip = slot.Tooltip,
                dwState = visible ? 0u : NativeMethods.NIS_HIDDEN,
                dwStateMask = NativeMethods.NIS_HIDDEN,
                szInfo = "",
                szInfoTitle = ""
            };

            slot.Added = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref nid);
            if (slot.Added)
            {
                var ver = new NativeMethods.NOTIFYICONDATAW
                {
                    cbSize = nid.cbSize,
                    hWnd = _hwnd,
                    uID = slot.Id,
                    uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4,
                    szTip = "",
                    szInfo = "",
                    szInfoTitle = ""
                };
                NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref ver);
            }
        }

        private void UpdateSlotNative(TraySlot slot)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!slot.Added)
            {
                AddSlot(slot, slot.Visible);
                return;
            }

            var nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = slot.Id,
                uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_STATE |
                         (slot.HIcon != IntPtr.Zero ? NativeMethods.NIF_ICON : 0u),
                hIcon = slot.HIcon,
                szTip = slot.Tooltip,
                dwState = slot.Visible ? 0u : NativeMethods.NIS_HIDDEN,
                dwStateMask = NativeMethods.NIS_HIDDEN,
                szInfo = "",
                szInfoTitle = ""
            };

            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref nid);
        }

        private void RemoveSlot(TraySlot slot)
        {
            if (slot.Added && _hwnd != IntPtr.Zero)
            {
                var nid = new NativeMethods.NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                    hWnd = _hwnd,
                    uID = slot.Id,
                    szTip = "",
                    szInfo = "",
                    szInfoTitle = ""
                };
                NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref nid);
                slot.Added = false;
            }

            if (slot.HIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(slot.HIcon);
                slot.HIcon = IntPtr.Zero;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmTrayIcon && !_disposed)
            {
                // NOTIFYICON_VERSION_4 packs mouse message into low-word of lParam
                uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                switch (mouseMsg)
                {
                    case NativeMethods.WM_LBUTTONUP:
                        Dispatch(() => OnOpenDashboardRequested?.Invoke());
                        break;
                    case NativeMethods.WM_RBUTTONUP:
                    case NativeMethods.WM_CONTEXTMENU:
                        Dispatch(ShowContextMenuAtCursor);
                        break;
                    case NativeMethods.NIN_BALLOONUSERCLICK:
                        Dispatch(() =>
                        {
                            Action? action = _notificationAction;
                            _notificationAction = null;
                            action?.Invoke();
                        });
                        break;
                }
                return IntPtr.Zero;
            }

            if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated && !_disposed)
            {
                _gpuSlot.Added = false;
                _cpuSlot.Added = false;
                _ramSlot.Added = false;
                AddSlot(_gpuSlot, _gpuSlot.Visible);
                AddSlot(_cpuSlot, _cpuSlot.Visible);
                AddSlot(_ramSlot, _ramSlot.Visible);
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }

        private void EnsureMenuBuilt()
        {
            if (_contextMenu != null)
            {
                return;
            }

            var menu = new ContextMenu
            {
                HasDropShadow = true,
                StaysOpen = false,
                Padding = new Thickness(4)
            };

            _itemOpen = CreateMenuItem("Open Dashboard", () => OnOpenDashboardRequested?.Invoke(), isPrimary: true);
            _itemCooling = CreateMenuItem("Toggle Cooling Mode", () => OnToggleCoolingRequested?.Invoke());

            _itemPowerProfiles = CreateMenuItem("Power Profile", null);
            var settings = SettingsService.Instance.Current;
            PopulatePowerProfileSubmenu(
                _itemPowerProfiles,
                PowerProfiles.GetVisibleCoolingProfiles(settings),
                PowerProfiles.GetVisibleGamingProfiles(settings));

            _itemPauseCooling = CreateMenuItem("Pause Cooling Mode", null);
            _itemPause15m = CreateMenuItem("15 minutes", () => OnPauseCoolingRequested?.Invoke(TimeSpan.FromMinutes(15)));
            _itemPause30m = CreateMenuItem("30 minutes", () => OnPauseCoolingRequested?.Invoke(TimeSpan.FromMinutes(30)));
            _itemPause60m = CreateMenuItem("1 hour", () => OnPauseCoolingRequested?.Invoke(TimeSpan.FromHours(1)));
            _itemResumeNow = CreateMenuItem("Resume Cooling Now", () => OnCancelCoolingPauseRequested?.Invoke());
            _itemResumeNow.IsEnabled = false;
            _itemPauseCooling.Items.Add(_itemPause15m);
            _itemPauseCooling.Items.Add(_itemPause30m);
            _itemPauseCooling.Items.Add(_itemPause60m);
            _itemPauseCooling.Items.Add(CreateSeparator());
            _itemPauseCooling.Items.Add(_itemResumeNow);

            _itemKeepAwake = CreateMenuItem("Keep Awake", null);
            _itemKeepAwakeOff = CreateMenuItem("Off", () => OnKeepAwakeStopRequested?.Invoke(), isCheckable: true);
            _itemKeepAwake15 = CreateMenuItem("15 minutes", () => OnKeepAwakeTimedRequested?.Invoke(TimeSpan.FromMinutes(15)), isCheckable: true);
            _itemKeepAwake30 = CreateMenuItem("30 minutes", () => OnKeepAwakeTimedRequested?.Invoke(TimeSpan.FromMinutes(30)), isCheckable: true);
            _itemKeepAwake60 = CreateMenuItem("1 hour", () => OnKeepAwakeTimedRequested?.Invoke(TimeSpan.FromHours(1)), isCheckable: true);
            _itemKeepAwake120 = CreateMenuItem("2 hours", () => OnKeepAwakeTimedRequested?.Invoke(TimeSpan.FromHours(2)), isCheckable: true);
            _itemKeepAwakeCustom = CreateMenuItem("Custom...", () => OnKeepAwakeCustomRequested?.Invoke());
            _itemKeepAwakeIndefinite = CreateMenuItem("Until I change it", () => OnKeepAwakeIndefiniteRequested?.Invoke(), isCheckable: true);
            _itemKeepAwakeStop = CreateMenuItem("Stop Keep Awake", () => OnKeepAwakeStopRequested?.Invoke());
            _itemKeepAwakeStop.IsEnabled = false;
            _itemKeepAwakeDisplayOff = CreateMenuItem(
                "Keep PC awake, but allow the display to turn off",
                () => OnKeepAwakeDisplayOffChanged?.Invoke(!(_itemKeepAwakeDisplayOff?.IsChecked ?? false)),
                isCheckable: true);

            _itemKeepAwake.Items.Add(_itemKeepAwakeOff);
            _itemKeepAwake.Items.Add(_itemKeepAwake15);
            _itemKeepAwake.Items.Add(_itemKeepAwake30);
            _itemKeepAwake.Items.Add(_itemKeepAwake60);
            _itemKeepAwake.Items.Add(_itemKeepAwake120);
            _itemKeepAwake.Items.Add(_itemKeepAwakeCustom);
            _itemKeepAwake.Items.Add(_itemKeepAwakeIndefinite);
            _itemKeepAwake.Items.Add(CreateSeparator());
            _itemKeepAwake.Items.Add(_itemKeepAwakeStop);
            _itemKeepAwake.Items.Add(_itemKeepAwakeDisplayOff);

            _itemDownloadTimer = CreateMenuItem("Smart Download Timer", () => OnToggleDownloadTimerRequested?.Invoke(), isCheckable: true);
            _itemStress = CreateMenuItem("CPU Temperature Test", () => OnOpenStressTestRequested?.Invoke());
            _itemOverlayClickThrough = CreateMenuItem("Click-Through Overlay", () => OnToggleOverlayClickThroughRequested?.Invoke(), isCheckable: true);
            _itemSettings = CreateMenuItem("Settings", () => OnOpenSettingsRequested?.Invoke());
            _itemExit = CreateMenuItem("Exit", () => OnExitRequested?.Invoke());

            menu.Items.Add(_itemOpen);
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(_itemCooling);
            menu.Items.Add(_itemPowerProfiles);
            menu.Items.Add(_itemPauseCooling);
            menu.Items.Add(_itemKeepAwake);
            menu.Items.Add(_itemDownloadTimer);
            menu.Items.Add(_itemStress);
            menu.Items.Add(_itemOverlayClickThrough);
            menu.Items.Add(_itemSettings);
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(_itemExit);

            menu.Closed += (_, _) =>
            {
                if (_menuHostWindow != null && _menuHostWindow.IsVisible)
                {
                    _menuHostWindow.Hide();
                }

                MemoryTrimService.Instance.ScheduleTrim(TimeSpan.FromSeconds(1));
            };

            _contextMenu = menu;
            ApplyMenuPaletteToVisuals();
            ApplyLanguage();
        }

        private void ShowContextMenuAtCursor()
        {
            if (_disposed)
            {
                return;
            }

            MemoryTrimService.Instance.PauseForInteraction(TimeSpan.FromSeconds(2));
            EnsureMenuBuilt();
            SyncMenuDynamicStates();
            if (_menuPaletteDirty)
            {
                ApplyMenuPaletteToVisuals();
            }

            if (_menuHostWindow == null)
            {
                _menuHostWindow = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = -10000,
                    Top = -10000,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = true,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true
                };
            }

            NativeMethods.GetCursorPos(out var pt);
            double dpiX = 96.0;
            double dpiY = 96.0;
            IntPtr mon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero && NativeMethods.GetDpiForMonitor(mon, 0, out uint mx, out uint my) == 0 && mx > 0 && my > 0)
            {
                dpiX = mx;
                dpiY = my;
            }

            double dipX = pt.X * (96.0 / dpiX);
            double dipY = pt.Y * (96.0 / dpiY);

            _menuHostWindow.Left = dipX;
            _menuHostWindow.Top = dipY;
            if (!_menuHostWindow.IsVisible)
            {
                _menuHostWindow.Show();
            }

            IntPtr hostHwnd = new WindowInteropHelper(_menuHostWindow).Handle;
            if (hostHwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hostHwnd);
            }

            _contextMenu!.PlacementTarget = _menuHostWindow;
            _contextMenu.Placement = PlacementMode.MousePoint;
            _contextMenu.IsOpen = true;
        }

        private void SyncMenuDynamicStates()
        {
            if (_contextMenu == null)
            {
                return;
            }

            var s = SettingsService.Instance.Current;
            _itemOverlayClickThrough!.IsChecked = s.OverlayClickThrough;
            _itemDownloadTimer!.IsChecked = s.DownloadTimerEnabled;
            RefreshPowerProfileChecks();
            RefreshPauseMenuText();
            RefreshKeepAwakeMenuText();
        }

        private void OnSettingsChanged()
        {
            if (_disposed)
            {
                return;
            }

            UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, true,
                _lastGpuLoad, _lastCpuLoad, _lastRamPercent);
            RefreshPowerProfileMenu(force: false);
            RefreshKeepAwakeMenuText();
        }

        private void OnKeepAwakeStatusChanged()
        {
            RefreshKeepAwakeMenuText();
        }

        private void OnPowerPlanProfileChanged()
        {
            if (_disposed)
            {
                return;
            }

            UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, false,
                _lastGpuLoad, _lastCpuLoad, _lastRamPercent);
        }

        public void RefreshDownloadTimerCheck()
        {
            Dispatch(() =>
            {
                if (_itemDownloadTimer != null)
                {
                    _itemDownloadTimer.IsChecked = SettingsService.Instance.Current.DownloadTimerEnabled;
                }
            });
        }

        public void ApplyLanguage()
        {
            if (_disposed)
            {
                return;
            }

            Dispatch(() =>
            {
                if (_contextMenu == null)
                {
                    UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, true,
                        _lastGpuLoad, _lastCpuLoad, _lastRamPercent);
                    return;
                }

                FlowDirection dir = LocalizationService.Instance.IsArabic
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
                _contextMenu.FlowDirection = dir;

                _itemOpen!.Header = LocalizationService.Get("Tray_OpenDashboard");
                _itemCooling!.Header = LocalizationService.Get("Tray_ToggleCooling");
                _itemPowerProfiles!.Header = LocalizationService.Get("Tray_PowerProfile");
                RefreshPowerProfileMenu(force: true);
                _itemPause15m!.Header = LocalizationService.Get("Tray_Pause15m");
                _itemPause30m!.Header = LocalizationService.Get("Tray_Pause30m");
                _itemPause60m!.Header = LocalizationService.Get("Tray_Pause60m");
                _itemResumeNow!.Header = LocalizationService.Get("Tray_ResumeCoolingNow");
                RefreshPauseMenuText();
                _itemDownloadTimer!.Header = LocalizationService.Get("Tray_DownloadTimer");
                _itemDownloadTimer.IsChecked = SettingsService.Instance.Current.DownloadTimerEnabled;
                _itemStress!.Header = LocalizationService.Get("Tray_CpuTest");
                _itemOverlayClickThrough!.Header = LocalizationService.Get("Tray_OverlayClickThrough");
                _itemOverlayClickThrough.IsChecked = SettingsService.Instance.Current.OverlayClickThrough;
                _itemKeepAwakeOff!.Header = LocalizationService.Get("Tray_KeepAwakeOff");
                _itemKeepAwake15!.Header = LocalizationService.Get("Tray_KeepAwake15");
                _itemKeepAwake30!.Header = LocalizationService.Get("Tray_KeepAwake30");
                _itemKeepAwake60!.Header = LocalizationService.Get("Tray_KeepAwake1h");
                _itemKeepAwake120!.Header = LocalizationService.Get("Tray_KeepAwake2h");
                _itemKeepAwakeCustom!.Header = LocalizationService.Get("Tray_KeepAwakeCustom");
                _itemKeepAwakeIndefinite!.Header = LocalizationService.Get("Tray_KeepAwakeIndefinite");
                _itemKeepAwakeStop!.Header = LocalizationService.Get("Tray_KeepAwakeStop");
                RefreshKeepAwakeMenuText();
                _itemSettings!.Header = LocalizationService.Get("Tray_Settings");
                _itemExit!.Header = LocalizationService.Get("Tray_Exit");

                UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, true,
                    _lastGpuLoad, _lastCpuLoad, _lastRamPercent);
            });
        }

        public void ApplyTheme()
        {
            if (ThemeService.IsLight)
            {
                _surfaceBg = Color.FromRgb(255, 255, 255);
                _surfaceBorder = Color.FromRgb(207, 199, 186);
                _hoverFill = Color.FromRgb(240, 237, 231);
                _hoverBorder = Color.FromRgb(185, 177, 165);
                _separatorLine = Color.FromRgb(221, 215, 205);
                _textNormal = Color.FromRgb(28, 26, 23);
                _textDisabled = Color.FromRgb(163, 155, 144);
                _chevronColor = Color.FromRgb(92, 86, 77);
                _checkBoxFill = Color.FromRgb(255, 255, 255);
                _checkBoxUncheckedBorder = Color.FromRgb(169, 161, 150);
                _checkBoxCheckedBorder = Color.FromRgb(27, 108, 179);
                _checkMarkColor = Color.FromRgb(27, 108, 179);
            }
            else
            {
                _surfaceBg = Color.FromRgb(30, 30, 30);
                _surfaceBorder = Color.FromRgb(58, 58, 58);
                _hoverFill = Color.FromRgb(45, 45, 45);
                _hoverBorder = Color.FromRgb(66, 66, 66);
                _separatorLine = Color.FromRgb(51, 51, 51);
                _textNormal = Color.FromRgb(234, 234, 234);
                _textDisabled = Color.FromRgb(120, 120, 120);
                _chevronColor = Color.FromRgb(190, 190, 190);
                _checkBoxFill = Color.FromRgb(20, 20, 20);
                _checkBoxUncheckedBorder = Color.FromRgb(120, 120, 120);
                _checkBoxCheckedBorder = Color.FromRgb(41, 182, 246);
                _checkMarkColor = Colors.White;
            }

            if (_contextMenu != null)
            {
                _menuPaletteDirty = true;
                Dispatch(ApplyMenuPaletteToVisuals);
            }
            else
            {
                _menuPaletteDirty = true;
            }
        }

        private void ApplyMenuPaletteToVisuals()
        {
            if (_contextMenu == null)
            {
                return;
            }

            _menuPaletteDirty = false;

            var res = _contextMenu.Resources;
            res["TraySurfaceBg"] = FreezeBrush(_surfaceBg);
            res["TraySurfaceBorder"] = FreezeBrush(_surfaceBorder);
            res["TrayHoverFill"] = FreezeBrush(_hoverFill);
            res["TrayHoverBorder"] = FreezeBrush(_hoverBorder);
            res["TraySeparatorLine"] = FreezeBrush(_separatorLine);
            res["TrayTextNormal"] = FreezeBrush(_textNormal);
            res["TrayTextDisabled"] = FreezeBrush(_textDisabled);
            res["TrayChevronColor"] = FreezeBrush(_chevronColor);
            res["TrayCheckBoxFill"] = FreezeBrush(_checkBoxFill);
            res["TrayCheckBoxUncheckedBorder"] = FreezeBrush(_checkBoxUncheckedBorder);
            res["TrayCheckBoxCheckedBorder"] = FreezeBrush(_checkBoxCheckedBorder);
            res["TrayCheckMarkColor"] = FreezeBrush(_checkMarkColor);

            _contextMenu.Style = CreateContextMenuStyle(res, MainMenuMinWidth);
        }

        private static SolidColorBrush FreezeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private Style CreateContextMenuStyle(ResourceDictionary res, double minWidth)
        {
            var itemStyle = CreateMenuItemStyle(res);
            var sepStyle = CreateSeparatorStyle(res);

            var menuStyle = new Style(typeof(ContextMenu));
            menuStyle.Resources[typeof(MenuItem)] = itemStyle;
            menuStyle.Resources[MenuItem.SeparatorStyleKey] = sepStyle;
            menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, res["TraySurfaceBg"]));
            menuStyle.Setters.Add(new Setter(Control.BorderBrushProperty, res["TraySurfaceBorder"]));
            menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 4, 2, 4)));
            menuStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, minWidth));
            menuStyle.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
            menuStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildMenuContainerTemplate()));
            return menuStyle;
        }

        private static ControlTemplate BuildMenuContainerTemplate()
        {
            var template = new ControlTemplate(typeof(ContextMenu));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(Panel.IsItemsHostProperty, true);
            stack.SetValue(System.Windows.Input.KeyboardNavigation.DirectionalNavigationProperty,
                System.Windows.Input.KeyboardNavigationMode.Cycle);
            border.AppendChild(stack);

            template.VisualTree = border;
            return template;
        }

        private static Style CreateSeparatorStyle(ResourceDictionary res)
        {
            var style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
            style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 4, 8, 4)));

            var template = new ControlTemplate(typeof(Separator));
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(Border.BackgroundProperty, res["TraySeparatorLine"]);
            line.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            template.VisualTree = line;

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateMenuItemStyle(ResourceDictionary res)
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, res["TrayTextNormal"]));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, MenuFontFamily));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));

            var template = new ControlTemplate(typeof(MenuItem));

            var rootGrid = new FrameworkElementFactory(typeof(Grid));

            var pill = new FrameworkElementFactory(typeof(Border), "ItemPill");
            pill.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            pill.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            pill.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            pill.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 1, 4, 1));
            pill.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            pill.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var rowGrid = new FrameworkElementFactory(typeof(Grid));
            var colDot = new FrameworkElementFactory(typeof(ColumnDefinition));
            colDot.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var colHeader = new FrameworkElementFactory(typeof(ColumnDefinition));
            colHeader.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var colTrail = new FrameworkElementFactory(typeof(ColumnDefinition));
            colTrail.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            rowGrid.AppendChild(colDot);
            rowGrid.AppendChild(colHeader);
            rowGrid.AppendChild(colTrail);

            var dot = new FrameworkElementFactory(typeof(Ellipse), "ProfileDot");
            dot.SetValue(Grid.ColumnProperty, 0);
            dot.SetValue(FrameworkElement.WidthProperty, 8.0);
            dot.SetValue(FrameworkElement.HeightProperty, 8.0);
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            rowGrid.AppendChild(dot);

            var header = new FrameworkElementFactory(typeof(ContentPresenter));
            header.SetValue(Grid.ColumnProperty, 1);
            header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            rowGrid.AppendChild(header);

            var checkBorder = new FrameworkElementFactory(typeof(Border), "CheckBoxBdr");
            checkBorder.SetValue(Grid.ColumnProperty, 2);
            checkBorder.SetValue(FrameworkElement.WidthProperty, 13.0);
            checkBorder.SetValue(FrameworkElement.HeightProperty, 13.0);
            checkBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            checkBorder.SetValue(Border.BackgroundProperty, res["TrayCheckBoxFill"]);
            checkBorder.SetValue(Border.BorderBrushProperty, res["TrayCheckBoxUncheckedBorder"]);
            checkBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            checkBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));
            checkBorder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBorder.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            var checkGlyph = new FrameworkElementFactory(typeof(Path), "CheckGlyph");
            checkGlyph.SetValue(Path.DataProperty, Geometry.Parse("M2,6.2 L4.8,9 L9.8,3"));
            checkGlyph.SetValue(Shape.StrokeProperty, res["TrayCheckMarkColor"]);
            checkGlyph.SetValue(Shape.StrokeThicknessProperty, 1.8);
            checkGlyph.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            checkGlyph.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            checkGlyph.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            checkBorder.AppendChild(checkGlyph);
            rowGrid.AppendChild(checkBorder);

            var chevron = new FrameworkElementFactory(typeof(Path), "SubMenuChevron");
            chevron.SetValue(Grid.ColumnProperty, 2);
            chevron.SetValue(Path.DataProperty, Geometry.Parse("M0,0 L4,4 L0,8"));
            chevron.SetValue(Shape.StrokeProperty, res["TrayChevronColor"]);
            chevron.SetValue(Shape.StrokeThicknessProperty, 1.5);
            chevron.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            chevron.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            chevron.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));
            chevron.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chevron.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            rowGrid.AppendChild(chevron);

            pill.AppendChild(rowGrid);
            rootGrid.AppendChild(pill);

            // Nested Submenu Popup matching root surface colors
            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(UIElement.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetBinding(Popup.IsOpenProperty,
                new System.Windows.Data.Binding("IsSubmenuOpen")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });

            var subBorder = new FrameworkElementFactory(typeof(Border));
            subBorder.SetValue(Border.BackgroundProperty, res["TraySurfaceBg"]);
            subBorder.SetValue(Border.BorderBrushProperty, res["TraySurfaceBorder"]);
            subBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            subBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            subBorder.SetValue(Border.PaddingProperty, new Thickness(2, 4, 2, 4));
            subBorder.SetValue(FrameworkElement.MinWidthProperty, SubMenuMinWidth);
            subBorder.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var subItems = new FrameworkElementFactory(typeof(StackPanel));
            subItems.SetValue(Panel.IsItemsHostProperty, true);
            subItems.SetValue(System.Windows.Input.KeyboardNavigation.DirectionalNavigationProperty,
                System.Windows.Input.KeyboardNavigationMode.Cycle);
            subBorder.AppendChild(subItems);
            popup.AppendChild(subBorder);
            rootGrid.AppendChild(popup);

            template.VisualTree = rootGrid;

            var tDotVisible = new Trigger { Property = FrameworkElement.TagProperty, Value = "profile-dot" };
            tDotVisible.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ProfileDot"));
            template.Triggers.Add(tDotVisible);

            var tCheckable = new Trigger { Property = MenuItem.IsCheckableProperty, Value = true };
            tCheckable.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckBoxBdr"));
            template.Triggers.Add(tCheckable);

            var tChecked = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            tChecked.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));
            tChecked.Setters.Add(new Setter(Border.BorderBrushProperty, res["TrayCheckBoxCheckedBorder"], "CheckBoxBdr"));
            template.Triggers.Add(tChecked);

            var tSubmenu = new Trigger { Property = ItemsControl.HasItemsProperty, Value = true };
            tSubmenu.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubMenuChevron"));
            template.Triggers.Add(tSubmenu);

            var tHighlight = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            tHighlight.Setters.Add(new Setter(Border.BackgroundProperty, res["TrayHoverFill"], "ItemPill"));
            tHighlight.Setters.Add(new Setter(Border.BorderBrushProperty, res["TrayHoverBorder"], "ItemPill"));
            template.Triggers.Add(tHighlight);

            var tDisabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            tDisabled.Setters.Add(new Setter(Control.ForegroundProperty, res["TrayTextDisabled"]));
            tDisabled.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Arrow));
            template.Triggers.Add(tDisabled);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static MenuItem CreateMenuItem(string text, Action? onClick, bool isPrimary = false, bool isCheckable = false)
        {
            var item = new MenuItem
            {
                Header = text,
                IsCheckable = isCheckable,
                StaysOpenOnClick = false
            };
            if (isPrimary)
            {
                item.FontWeight = FontWeights.SemiBold;
            }
            if (onClick != null)
            {
                item.Click += (_, _) => onClick();
            }
            return item;
        }

        private static Separator CreateSeparator() => new();

        public void SetCoolingPauseUntil(DateTime? pauseUntilUtc)
        {
            lock (_renderLock)
            {
                _coolingPausedUntilUtc = pauseUntilUtc;
            }

            RefreshPauseMenuText();
            UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, false,
                _lastGpuLoad, _lastCpuLoad, _lastRamPercent);
        }

        private int GetPauseRemainingMinutes()
        {
            DateTime? until;
            lock (_renderLock)
            {
                until = _coolingPausedUntilUtc;
            }

            if (!until.HasValue || until.Value <= DateTime.UtcNow)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling((until.Value - DateTime.UtcNow).TotalMinutes));
        }

        private void RefreshPowerProfileMenu(bool force)
        {
            if (_disposed)
            {
                return;
            }

            Dispatch(() =>
            {
                if (_disposed || _itemPowerProfiles == null)
                {
                    return;
                }

                var settings = SettingsService.Instance.Current;
                var cooling = PowerProfiles.GetVisibleCoolingProfiles(settings);
                var gaming = PowerProfiles.GetVisibleGamingProfiles(settings);
                string signature = string.Join("|",
                    cooling.Concat(gaming).Select(p =>
                        $"{p.Key}:{(p.IsCooling ? 'C' : 'G')}:{p.Palette.Accent}:{LocalizationService.GetProfileName(p)}"));

                if (!force && string.Equals(signature, _lastPowerProfileMenuSignature, StringComparison.Ordinal))
                {
                    return;
                }

                _lastPowerProfileMenuSignature = signature;
                PopulatePowerProfileSubmenu(_itemPowerProfiles, cooling, gaming);
                RefreshPowerProfileChecks();
            });
        }

        private void PopulatePowerProfileSubmenu(
            MenuItem parent,
            IReadOnlyList<PowerProfileDefinition> cooling,
            IReadOnlyList<PowerProfileDefinition> gaming)
        {
            parent.Items.Clear();
            _powerProfileItems.Clear();

            void AddEntries(IReadOnlyList<PowerProfileDefinition> profiles)
            {
                foreach (var profile in profiles)
                {
                    string key = profile.Key;
                    Color dotColor = ParseHexColor(profile.Palette.Accent, CpuCyan);
                    var entry = CreateMenuItem(
                        LocalizationService.GetProfileName(profile),
                        () => OnPowerProfileRequested?.Invoke(key),
                        isCheckable: true);
                    entry.Tag = "profile-dot";
                    entry.BorderBrush = FreezeBrush(dotColor);
                    parent.Items.Add(entry);
                    _powerProfileItems[key] = entry;
                }
            }

            AddEntries(cooling);
            parent.Items.Add(CreateSeparator());
            AddEntries(gaming);
        }

        private void RefreshPowerProfileChecks()
        {
            if (_disposed)
            {
                return;
            }

            var active = PowerPlanService.Instance.GetActiveProfile();
            foreach (var pair in _powerProfileItems)
            {
                pair.Value.IsChecked = active != null &&
                    string.Equals(active.Key, pair.Key, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void RefreshKeepAwakeMenuText()
        {
            if (_disposed)
            {
                return;
            }

            Dispatch(() =>
            {
                if (_disposed || _itemKeepAwake == null)
                {
                    return;
                }

                var status = KeepAwakeService.Instance.GetStatus();
                _itemKeepAwake.Header = status.Mode switch
                {
                    KeepAwakeMode.Timed => LocalizationService.Format(
                        "Tray_KeepAwakeActive",
                        KeepAwakeService.FormatRemaining(status.Remaining ?? TimeSpan.Zero)),
                    KeepAwakeMode.Indefinite => LocalizationService.Get("Tray_KeepAwakeIndefiniteActive"),
                    _ => LocalizationService.Get("Tray_KeepAwake")
                };

                _itemKeepAwakeOff!.IsChecked = status.Mode == KeepAwakeMode.Off;
                _itemKeepAwake15!.IsChecked = IsTimedDuration(status, 15);
                _itemKeepAwake30!.IsChecked = IsTimedDuration(status, 30);
                _itemKeepAwake60!.IsChecked = IsTimedDuration(status, 60);
                _itemKeepAwake120!.IsChecked = IsTimedDuration(status, 120);
                _itemKeepAwakeIndefinite!.IsChecked = status.Mode == KeepAwakeMode.Indefinite;
                _itemKeepAwakeStop!.IsEnabled = status.IsActive;
                _itemKeepAwakeDisplayOff!.IsChecked = SettingsService.Instance.Current.KeepAwakeAllowDisplayOff;
                _itemKeepAwakeDisplayOff.Header = LocalizationService.Get("Tray_KeepAwakeDisplayOff");
            });
        }

        private static bool IsTimedDuration(KeepAwakeStatus status, int minutes)
        {
            return status.Mode == KeepAwakeMode.Timed &&
                status.Duration.HasValue &&
                (int)Math.Round(status.Duration.Value.TotalMinutes) == minutes;
        }

        private void RefreshPauseMenuText()
        {
            if (_disposed)
            {
                return;
            }

            Dispatch(() =>
            {
                if (_disposed || _itemPauseCooling == null)
                {
                    return;
                }

                int mins = GetPauseRemainingMinutes();
                _itemPauseCooling.Header = mins > 0
                    ? LocalizationService.Format("Tray_PauseCoolingActive", mins)
                    : LocalizationService.Get("Tray_PauseCooling");
                _itemResumeNow!.IsEnabled = mins > 0;
            });
        }

        public void ShowNotification(string title, string message, Action? onClick = null,
            TrayNotificationIcon icon = TrayNotificationIcon.Info)
        {
            if (_hwnd == IntPtr.Zero || _disposed)
            {
                return;
            }

            TraySlot slot = _cpuSlot.Visible
                ? _cpuSlot
                : _gpuSlot.Visible
                    ? _gpuSlot
                    : _ramSlot.Visible
                        ? _ramSlot
                        : _cpuSlot;

            slot.Visible = true;
            _notificationSlot = slot;
            _notificationHoldUntilUtc = DateTime.UtcNow.AddSeconds(12);
            _notificationAction = onClick;

            if (!slot.Added)
            {
                AddSlot(slot, visible: true);
            }

            var nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = slot.Id,
                uFlags = NativeMethods.NIF_INFO | NativeMethods.NIF_STATE | NativeMethods.NIF_TIP,
                dwState = 0,
                dwStateMask = NativeMethods.NIS_HIDDEN,
                szTip = slot.Tooltip,
                szInfoTitle = ClipString(title, 63),
                szInfo = ClipString(message, 255),
                dwInfoFlags = icon switch
                {
                    TrayNotificationIcon.Warning => NativeMethods.NIIF_WARNING,
                    TrayNotificationIcon.Info => NativeMethods.NIIF_INFO,
                    _ => NativeMethods.NIIF_NONE
                }
            };
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref nid);
        }

        internal (BitmapSource MainMenu, BitmapSource SubMenu) RenderContextMenusForTest(bool selectFirstItem, bool overlayChecked)
        {
            EnsureMenuBuilt();
            _itemOverlayClickThrough!.IsChecked = overlayChecked;
            RefreshPauseMenuText();
            ApplyMenuPaletteToVisuals();

            BitmapSource mainBmp = RenderMenuControlToBitmap(_contextMenu!, _contextMenu!.Items, MainMenuMinWidth, selectFirstIndex: selectFirstItem ? 0 : -1);
            BitmapSource subBmp = RenderMenuControlToBitmap(_contextMenu, _itemPauseCooling!.Items, SubMenuMinWidth, selectFirstIndex: selectFirstItem ? 0 : -1);
            return (mainBmp, subBmp);
        }

        private BitmapSource RenderMenuControlToBitmap(ContextMenu templateOwner, ItemCollection items, double minWidth, int selectFirstIndex)
        {
            var host = new Border
            {
                Background = FreezeBrush(_surfaceBg),
                BorderBrush = FreezeBrush(_surfaceBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 4, 2, 4),
                MinWidth = minWidth,
                FlowDirection = templateOwner.FlowDirection
            };

            var stack = new StackPanel();
            int index = 0;
            foreach (object raw in items)
            {
                if (raw is Separator)
                {
                    stack.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 4, 8, 4),
                        Background = FreezeBrush(_separatorLine)
                    });
                    continue;
                }

                if (raw is MenuItem src)
                {
                    bool highlighted = index == selectFirstIndex && src.IsEnabled;
                    index++;

                    var pill = new Border
                    {
                        Background = highlighted ? FreezeBrush(_hoverFill) : Brushes.Transparent,
                        BorderBrush = highlighted ? FreezeBrush(_hoverBorder) : Brushes.Transparent,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(3, 1, 4, 1),
                        Padding = new Thickness(12, 6, 12, 6)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    if (Equals(src.Tag, "profile-dot") && src.BorderBrush != null)
                    {
                        var dot = new Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = src.BorderBrush,
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(dot, 0);
                        grid.Children.Add(dot);
                    }

                    var txt = new TextBlock
                    {
                        Text = src.Header?.ToString() ?? "",
                        Foreground = src.IsEnabled ? FreezeBrush(_textNormal) : FreezeBrush(_textDisabled),
                        FontFamily = MenuFontFamily,
                        FontSize = 12.5,
                        FontWeight = src.FontWeight,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(txt, 1);
                    grid.Children.Add(txt);

                    if (src.IsCheckable)
                    {
                        var box = new Border
                        {
                            Width = 13,
                            Height = 13,
                            CornerRadius = new CornerRadius(2),
                            Background = FreezeBrush(_checkBoxFill),
                            BorderBrush = FreezeBrush(src.IsChecked ? _checkBoxCheckedBorder : _checkBoxUncheckedBorder),
                            BorderThickness = new Thickness(1),
                            Margin = new Thickness(16, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        if (src.IsChecked)
                        {
                            box.Child = new Path
                            {
                                Data = Geometry.Parse("M2,6.2 L4.8,9 L9.8,3"),
                                Stroke = FreezeBrush(_checkMarkColor),
                                StrokeThickness = 1.8,
                                StrokeStartLineCap = PenLineCap.Round,
                                StrokeEndLineCap = PenLineCap.Round
                            };
                        }
                        Grid.SetColumn(box, 2);
                        grid.Children.Add(box);
                    }
                    else if (src.HasItems)
                    {
                        var chevron = new Path
                        {
                            Data = Geometry.Parse("M0,0 L4,4 L0,8"),
                            Stroke = FreezeBrush(_chevronColor),
                            StrokeThickness = 1.5,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round,
                            Margin = new Thickness(16, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(chevron, 2);
                        grid.Children.Add(chevron);
                    }

                    pill.Child = grid;
                    stack.Children.Add(pill);
                }
            }

            host.Child = stack;
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = host.DesiredSize;
            int width = Math.Max(1, (int)Math.Ceiling(desired.Width));
            int height = Math.Max(1, (int)Math.Ceiling(desired.Height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            rtb.Freeze();
            return rtb;
        }

        public void UpdateBadges(double gpuTemp, double cpuTemp, bool coolingModeActive,
            bool force = false, double gpuLoad = double.NaN, double cpuLoad = double.NaN,
            double ramPercent = double.NaN)
        {
            lock (_renderLock)
            {
                if (_disposed)
                {
                    return;
                }

                var settings = SettingsService.Instance.Current;
                bool holdCpu = _notificationSlot == _cpuSlot && DateTime.UtcNow < _notificationHoldUntilUtc;
                bool holdGpu = _notificationSlot == _gpuSlot && DateTime.UtcNow < _notificationHoldUntilUtc;
                bool holdRam = _notificationSlot == _ramSlot && DateTime.UtcNow < _notificationHoldUntilUtc;

                if (!holdCpu && !holdGpu && !holdRam)
                {
                    _notificationSlot = null;
                }

                bool isFahrenheit = settings.TemperatureUnit == "Fahrenheit";
                var battery = BatteryTelemetryService.Read();
                string batteryText = settings.ShowBatteryStatus && battery.HasBattery
                    ? LocalizationService.Format(
                        "Tray_BatteryTooltip",
                        battery.Percent,
                        BatteryTelemetryService.GetStateLabel(battery))
                    : "";
                TrayBadgeMetric gpuMetric = NormalizeBadgeMetric(settings.GpuTrayBadgeMetric);
                TrayBadgeMetric cpuMetric = NormalizeBadgeMetric(settings.CpuTrayBadgeMetric);
                string gpuUnit = GetBadgeMetricUnit(gpuMetric, isFahrenheit);
                string cpuUnit = GetBadgeMetricUnit(cpuMetric, isFahrenheit);
                int? gpuInt = ResolveBadgeValue(gpuMetric, gpuTemp, gpuLoad, ramPercent, isFahrenheit);
                int? cpuInt = ResolveBadgeValue(cpuMetric, cpuTemp, cpuLoad, ramPercent, isFahrenheit);
                int? ramInt = ResolveBadgeValue(TrayBadgeMetric.Ram, gpuTemp, gpuLoad, ramPercent, isFahrenheit);
                string gpuText = gpuInt.HasValue ? gpuInt.Value.ToString(CultureInfo.InvariantCulture) : "--";
                string cpuText = cpuInt.HasValue ? cpuInt.Value.ToString(CultureInfo.InvariantCulture) : "--";
                string ramText = ramInt.HasValue ? ramInt.Value.ToString(CultureInfo.InvariantCulture) : "--";
                int pauseMins = (!_coolingPausedUntilUtc.HasValue || _coolingPausedUntilUtc.Value <= DateTime.UtcNow)
                    ? 0
                    : Math.Max(1, (int)Math.Ceiling((_coolingPausedUntilUtc.Value - DateTime.UtcNow).TotalMinutes));
                var activeProfile = PowerPlanService.Instance.ActiveProfile;
                string activeProfileKey = activeProfile?.Key ?? "";
                string activeProfileAccent = activeProfile?.Palette.Accent ?? "";
                string signature = string.Join(
                    "|",
                    gpuText, cpuText, settings.ShowRamBadge ? ramText : "",
                    coolingModeActive, settings.MergeTrayIcons,
                    settings.ShowGpuBadge, settings.ShowCpuBadge, settings.ShowRamBadge,
                    isFahrenheit, holdCpu, holdGpu, holdRam, pauseMins,
                    settings.CpuTrayWhiteDigits, settings.SyncCpuTrayBadgeColor,
                    settings.SyncCpuTrayBadgeColor ? activeProfileKey : "",
                    settings.SyncCpuTrayBadgeColor ? activeProfileAccent : "",
                    settings.GpuTrayWhiteDigits, settings.RamTrayWhiteDigits,
                    settings.RamTrayBadgeColor, cpuMetric, gpuMetric, cpuUnit, gpuUnit, batteryText);

                _lastGpuTemp = gpuTemp;
                _lastCpuTemp = cpuTemp;
                _lastGpuLoad = gpuLoad;
                _lastCpuLoad = cpuLoad;
                _lastRamPercent = ramPercent;
                _lastCoolingModeActive = coolingModeActive;

                if (!force && signature == _lastBadgeSignature)
                {
                    return;
                }

                _lastBadgeSignature = signature;
                Color cpuColor = ResolveCpuBadgeColor(
                    settings.SyncCpuTrayBadgeColor, activeProfile, coolingModeActive,
                    settings.CpuTrayWhiteDigits, merged: false);
                Color gpuColor = settings.GpuTrayWhiteDigits ? GpuGreenWhite : GpuGreen;
                Color ramColor = ResolveRamBadgeColor(settings.RamTrayBadgeColor, settings.RamTrayWhiteDigits);

                _badgeSurface ??= new BadgeSurface();
                bool showRam = settings.ShowRamBadge || holdRam;
                _ramSlot.Visible = showRam;
                if (showRam)
                {
                    ReplaceSlotIcon(_ramSlot, _badgeSurface.RenderSingleToHIcon(ramInt, ramColor, settings.RamTrayWhiteDigits));
                    _ramSlot.Tooltip = ComposeTooltip(
                        LocalizationService.Format("Tray_RamTooltip", ramText), batteryText);
                    UpdateSlotNative(_ramSlot);
                }
                else
                {
                    HideAndClearSlot(_ramSlot);
                }

                if (settings.MergeTrayIcons)
                {
                    _gpuSlot.Visible = false;
                    HideAndClearSlot(_gpuSlot);

                    _cpuSlot.Visible = settings.ShowCpuBadge || settings.ShowGpuBadge || holdCpu || holdGpu;
                    if (_cpuSlot.Visible)
                    {
                        Color mergedCpuBg = ResolveCpuBadgeColor(
                            settings.SyncCpuTrayBadgeColor, activeProfile, coolingModeActive,
                            settings.CpuTrayWhiteDigits, merged: true);
                        Color mergedGpuBg = settings.GpuTrayWhiteDigits ? GpuGreenWhite : MergedGpuGreen;
                        ReplaceSlotIcon(_cpuSlot, _badgeSurface.RenderMergedToHIcon(
                            cpuInt, gpuInt, mergedCpuBg, mergedGpuBg,
                            settings.CpuTrayWhiteDigits, settings.GpuTrayWhiteDigits));
                        string mergedTooltip = string.Equals(cpuUnit, gpuUnit, StringComparison.Ordinal)
                            ? LocalizationService.Format("Tray_MergedTooltip", cpuText, cpuUnit, gpuText)
                            : LocalizationService.Format("Tray_MergedTooltipMixed", cpuText, cpuUnit, gpuText, gpuUnit);
                        _cpuSlot.Tooltip = ComposeTooltip(mergedTooltip, batteryText);
                        UpdateSlotNative(_cpuSlot);
                    }
                    else
                    {
                        HideAndClearSlot(_cpuSlot);
                    }

                    return;
                }

                _gpuSlot.Visible = settings.ShowGpuBadge || holdGpu;
                _cpuSlot.Visible = settings.ShowCpuBadge || holdCpu;

                if (_gpuSlot.Visible)
                {
                    ReplaceSlotIcon(_gpuSlot, _badgeSurface.RenderSingleToHIcon(gpuInt, gpuColor, settings.GpuTrayWhiteDigits));
                    _gpuSlot.Tooltip = ComposeTooltip(
                        LocalizationService.Format("Tray_GpuTooltip", gpuText, gpuUnit), batteryText);
                    UpdateSlotNative(_gpuSlot);
                }
                else
                {
                    HideAndClearSlot(_gpuSlot);
                }

                if (_cpuSlot.Visible)
                {
                    ReplaceSlotIcon(_cpuSlot, _badgeSurface.RenderSingleToHIcon(cpuInt, cpuColor, settings.CpuTrayWhiteDigits));
                    string coolingText = LocalizationService.Get(
                        coolingModeActive ? "Common_On" : "Common_Off");
                    string cpuTooltip = pauseMins > 0 && !coolingModeActive
                        ? LocalizationService.Format("Tray_CpuTooltipPaused", cpuText, cpuUnit, pauseMins)
                        : LocalizationService.Format("Tray_CpuTooltip", cpuText, cpuUnit, coolingText);
                    _cpuSlot.Tooltip = ComposeTooltip(cpuTooltip, batteryText);
                    UpdateSlotNative(_cpuSlot);
                }
                else
                {
                    HideAndClearSlot(_cpuSlot);
                }
            }
        }

        private void ReplaceSlotIcon(TraySlot slot, IntPtr newHandle)
        {
            IntPtr oldHandle = slot.HIcon;
            slot.HIcon = newHandle;
            if (oldHandle != IntPtr.Zero && oldHandle != newHandle)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }
        }

        private void HideAndClearSlot(TraySlot slot)
        {
            slot.Visible = false;
            IntPtr oldHandle = slot.HIcon;
            slot.HIcon = IntPtr.Zero;
            UpdateSlotNative(slot);
            if (oldHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }
        }

        private static string ComposeTooltip(string baseText, string batteryText)
        {
            string text = string.IsNullOrEmpty(batteryText)
                ? baseText
                : $"{baseText} | {batteryText}";
            return ClipString(text, 127);
        }

        private static string ClipString(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
        }

        internal enum TrayBadgeMetric
        {
            Temperature,
            Load,
            Ram
        }

        internal static TrayBadgeMetric NormalizeBadgeMetric(string? value)
        {
            if (string.Equals(value, "Load", StringComparison.OrdinalIgnoreCase))
            {
                return TrayBadgeMetric.Load;
            }

            if (string.Equals(value, "Ram", StringComparison.OrdinalIgnoreCase))
            {
                return TrayBadgeMetric.Ram;
            }

            return TrayBadgeMetric.Temperature;
        }

        internal static int? ResolveBadgeValue(TrayBadgeMetric metric, double temperature,
            double load, double ramPercent, bool isFahrenheit)
        {
            double value = metric switch
            {
                TrayBadgeMetric.Load => load,
                TrayBadgeMetric.Ram => ramPercent,
                _ => double.IsFinite(temperature) && temperature > 0
                    ? (isFahrenheit ? (temperature * 9 / 5) + 32 : temperature)
                    : double.NaN
            };

            if (!double.IsFinite(value))
            {
                return null;
            }

            if (metric == TrayBadgeMetric.Temperature && value <= 0)
            {
                return null;
            }

            return (int)Math.Round(Math.Clamp(value, 0, 999));
        }

        internal static string GetBadgeMetricUnit(TrayBadgeMetric metric, bool isFahrenheit)
        {
            return metric == TrayBadgeMetric.Temperature
                ? (isFahrenheit ? "°F" : "°C")
                : "%";
        }

        internal static Color ResolveCpuBadgeColor(
            bool syncWithProfile,
            PowerProfileDefinition? activeProfile,
            bool coolingModeActive,
            bool whiteDigits,
            bool merged)
        {
            if (!syncWithProfile || activeProfile == null)
            {
                if (merged && !whiteDigits)
                {
                    return coolingModeActive ? MergedCpuOn : MergedCpuOff;
                }

                return whiteDigits
                    ? (coolingModeActive ? CpuCyanWhite : CpuAmberWhite)
                    : (coolingModeActive ? CpuCyan : CpuAmber);
            }

            Color fallback = coolingModeActive ? CpuCyan : CpuAmber;
            Color accent = ParseHexColor(activeProfile.Palette.Accent, fallback);
            return EnsureBadgeContrast(accent, whiteDigits ? Colors.White : BadgeInk);
        }

        internal static Color ResolveRamBadgeColor(string? configuredHex, bool whiteDigits)
        {
            Color configured = ParseHexColor(configuredHex, RamBadgeBlue);
            return EnsureBadgeContrast(configured, whiteDigits ? Colors.White : BadgeInk);
        }

        private static Color ParseHexColor(string? configuredHex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(configuredHex))
            {
                return fallback;
            }

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

            return fallback;
        }

        private static Color EnsureBadgeContrast(Color background, Color textColor)
        {
            const double minimum = 4.5;
            if (GetContrastRatio(background, textColor) >= minimum)
            {
                return background;
            }

            Color target = GetRelativeLuminance(textColor) < 0.5 ? Colors.White : Colors.Black;
            for (int step = 1; step <= 20; step++)
            {
                Color candidate = BlendColor(background, target, step / 20.0);
                if (GetContrastRatio(candidate, textColor) >= minimum)
                {
                    return candidate;
                }
            }

            return target;
        }

        private static double GetContrastRatio(Color a, Color b)
        {
            double first = GetRelativeLuminance(a);
            double second = GetRelativeLuminance(b);
            return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Channel(byte value)
            {
                double channel = value / 255.0;
                return channel <= 0.03928
                    ? channel / 12.92
                    : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.R)) +
                (0.7152 * Channel(color.G)) +
                (0.0722 * Channel(color.B));
        }

        private static Color BlendColor(Color from, Color to, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
                (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
                (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
        }

        internal BitmapSource RenderSingleBadgeForTest(int? temp, bool isCpu, bool coolingModeActive, bool whiteDigits)
        {
            lock (_renderLock)
            {
                Color bg = isCpu
                    ? (whiteDigits ? (coolingModeActive ? CpuCyanWhite : CpuAmberWhite) : (coolingModeActive ? CpuCyan : CpuAmber))
                    : (whiteDigits ? GpuGreenWhite : GpuGreen);
                _badgeSurface ??= new BadgeSurface();
                return _badgeSurface.RenderSingleToBitmapSource(temp, bg, whiteDigits);
            }
        }

        internal BitmapSource RenderRamBadgeForTest(int? percent, bool whiteDigits, string? colorHex)
        {
            lock (_renderLock)
            {
                _badgeSurface ??= new BadgeSurface();
                return _badgeSurface.RenderSingleToBitmapSource(percent, ResolveRamBadgeColor(colorHex, whiteDigits), whiteDigits);
            }
        }

        internal BitmapSource RenderMergedBadgeForTest(int? cpuTemp, int? gpuTemp, bool coolingModeActive, bool cpuWhiteDigits, bool gpuWhiteDigits)
        {
            lock (_renderLock)
            {
                Color cpuBg = cpuWhiteDigits
                    ? (coolingModeActive ? CpuCyanWhite : CpuAmberWhite)
                    : (coolingModeActive ? MergedCpuOn : MergedCpuOff);
                Color gpuBg = gpuWhiteDigits ? GpuGreenWhite : MergedGpuGreen;
                _badgeSurface ??= new BadgeSurface();
                return _badgeSurface.RenderMergedToBitmapSource(cpuTemp, gpuTemp, cpuBg, gpuBg, cpuWhiteDigits, gpuWhiteDigits);
            }
        }

        internal static BitmapSource RenderSingleBadgeBitmap(int? temp, Color background, bool whiteDigits)
        {
            using var surface = new BadgeSurface();
            return surface.RenderSingleToBitmapSource(temp, background, whiteDigits);
        }

        private static int GetSingleDigitFontSize(int? temp)
        {
            if (!temp.HasValue)
            {
                return 18;
            }
            if (temp.Value >= 100 || temp.Value <= -10)
            {
                return 15;
            }
            return temp.Value >= 10 || temp.Value < 0 ? 18 : 22;
        }

        internal static BitmapSource RenderSolidBadgeBitmap(Color color)
        {
            return RenderSingleBadgeBitmap(null, color, whiteDigits: true);
        }

        private sealed class BadgeSurface : IDisposable
        {
            private readonly IntPtr _hdc;
            private readonly IntPtr _hbmColor;
            private readonly IntPtr _hbmMask;
            private readonly IntPtr _pBits;
            private readonly IntPtr _font13;
            private readonly IntPtr _font15;
            private readonly IntPtr _font18;
            private readonly IntPtr _font22;
            private bool _disposed;

            public BadgeSurface()
            {
                _hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
                var bmi = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = IconSize,
                    biHeight = -IconSize, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                _hbmColor = NativeMethods.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
                _hbmMask = NativeMethods.CreateBitmap(IconSize, IconSize, 1, 1, IntPtr.Zero);
                _font13 = CreateBadgeFont(13);
                _font15 = CreateBadgeFont(15);
                _font18 = CreateBadgeFont(18);
                _font22 = CreateBadgeFont(22);
            }

            public IntPtr RenderSingleToHIcon(int? temp, Color background, bool whiteDigits)
            {
                DrawSingle(temp, background, whiteDigits);
                return CreateHIconFromSurface();
            }

            public IntPtr RenderMergedToHIcon(
                int? cpuTemp,
                int? gpuTemp,
                Color cpuBackground,
                Color gpuBackground,
                bool cpuWhiteDigits,
                bool gpuWhiteDigits)
            {
                DrawMerged(cpuTemp, gpuTemp, cpuBackground, gpuBackground, cpuWhiteDigits, gpuWhiteDigits);
                return CreateHIconFromSurface();
            }

            public BitmapSource RenderSingleToBitmapSource(int? temp, Color background, bool whiteDigits)
            {
                DrawSingle(temp, background, whiteDigits);
                return CopySurfaceToBitmapSource();
            }

            public BitmapSource RenderMergedToBitmapSource(
                int? cpuTemp,
                int? gpuTemp,
                Color cpuBackground,
                Color gpuBackground,
                bool cpuWhiteDigits,
                bool gpuWhiteDigits)
            {
                DrawMerged(cpuTemp, gpuTemp, cpuBackground, gpuBackground, cpuWhiteDigits, gpuWhiteDigits);
                return CopySurfaceToBitmapSource();
            }

            private static IntPtr CreateBadgeFont(int pixelHeight)
            {
                return NativeMethods.CreateFontW(
                    -pixelHeight, 0, 0, 0,
                    700, // FW_BOLD
                    0, 0, 0,
                    0,   // DEFAULT_CHARSET
                    0, 0,
                    4,   // ANTIALIASED_QUALITY (clean alpha-blended edges without ClearType color fringes)
                    0,
                    "Segoe UI");
            }

            private IntPtr GetFontForHeight(int pixelHeight) => pixelHeight switch
            {
                13 => _font13,
                15 => _font15,
                22 => _font22,
                _ => _font18
            };

            private unsafe void DrawSingle(int? temp, Color background, bool whiteDigits)
            {
                if (_hdc == IntPtr.Zero || _hbmColor == IntPtr.Zero || _pBits == IntPtr.Zero)
                {
                    return;
                }

                uint* pixels = (uint*)_pBits;
                uint bgPixel = ToOpaqueBgra(background);
                for (int i = 0; i < IconSize * IconSize; i++)
                {
                    pixels[i] = bgPixel;
                }

                string text = temp.HasValue ? temp.Value.ToString(CultureInfo.InvariantCulture) : "--";
                IntPtr prevBmp = NativeMethods.SelectObject(_hdc, _hbmColor);
                IntPtr prevFont = NativeMethods.SelectObject(_hdc, GetFontForHeight(GetSingleDigitFontSize(temp)));
                try
                {
                    DrawCenteredText(_hdc, text, new NativeMethods.RECT { Left = 0, Top = 1, Right = IconSize, Bottom = IconSize }, whiteDigits);
                    NativeMethods.GdiFlush();
                }
                finally
                {
                    NativeMethods.SelectObject(_hdc, prevFont);
                    NativeMethods.SelectObject(_hdc, prevBmp);
                }

                // Restore opaque alpha on pixels touched by GDI DrawTextW.
                for (int i = 0; i < IconSize * IconSize; i++)
                {
                    pixels[i] |= 0xFF000000u;
                }
            }

            private unsafe void DrawMerged(
                int? cpuTemp,
                int? gpuTemp,
                Color cpuBackground,
                Color gpuBackground,
                bool cpuWhiteDigits,
                bool gpuWhiteDigits)
            {
                if (_hdc == IntPtr.Zero || _hbmColor == IntPtr.Zero || _pBits == IntPtr.Zero)
                {
                    return;
                }

                uint* pixels = (uint*)_pBits;
                uint cpuPixel = ToOpaqueBgra(cpuBackground);
                uint divPixel = ToOpaqueBgra(DividerColor);
                uint gpuPixel = ToOpaqueBgra(gpuBackground);

                int dividerRow = IconHalfHeight - 1;
                for (int y = 0; y < IconSize; y++)
                {
                    uint rowPixel = y < dividerRow ? cpuPixel : (y == dividerRow ? divPixel : gpuPixel);
                    int rowOffset = y * IconSize;
                    for (int x = 0; x < IconSize; x++)
                    {
                        pixels[rowOffset + x] = rowPixel;
                    }
                }

                string cpuText = cpuTemp.HasValue ? cpuTemp.Value.ToString(CultureInfo.InvariantCulture) : "--";
                string gpuText = gpuTemp.HasValue ? gpuTemp.Value.ToString(CultureInfo.InvariantCulture) : "--";

                IntPtr prevBmp = NativeMethods.SelectObject(_hdc, _hbmColor);
                IntPtr prevFont = NativeMethods.SelectObject(_hdc, _font13);
                try
                {
                    DrawCenteredText(_hdc, cpuText, new NativeMethods.RECT { Left = 0, Top = 0, Right = IconSize, Bottom = IconHalfHeight - 1 }, cpuWhiteDigits);
                    DrawCenteredText(_hdc, gpuText, new NativeMethods.RECT { Left = 0, Top = IconHalfHeight, Right = IconSize, Bottom = IconSize }, gpuWhiteDigits);
                    NativeMethods.GdiFlush();
                }
                finally
                {
                    NativeMethods.SelectObject(_hdc, prevFont);
                    NativeMethods.SelectObject(_hdc, prevBmp);
                }

                // Restore opaque alpha inside interior and apply 5px rounded-corner AA mask.
                const double Radius = 5.0;
                const double InnerMin = Radius;
                const double InnerMax = IconSize - Radius;

                for (int y = 0; y < IconSize; y++)
                {
                    int rowOffset = y * IconSize;
                    double py = y + 0.5;
                    bool yInCornerBand = py < InnerMin || py > InnerMax;
                    double cy = py < InnerMin ? InnerMin : (py > InnerMax ? InnerMax : py);

                    for (int x = 0; x < IconSize; x++)
                    {
                        int idx = rowOffset + x;
                        double px = x + 0.5;
                        if (!yInCornerBand || (px >= InnerMin && px <= InnerMax))
                        {
                            pixels[idx] |= 0xFF000000u;
                            continue;
                        }

                        double cx = px < InnerMin ? InnerMin : InnerMax;
                        double dx = px - cx;
                        double dy = py - cy;
                        double dist = Math.Sqrt((dx * dx) + (dy * dy));
                        if (dist >= Radius)
                        {
                            pixels[idx] = 0u;
                        }
                        else if (dist > Radius - 1.0)
                        {
                            double cov = Radius - dist;
                            uint rgb = pixels[idx];
                            byte a = (byte)Math.Round(255.0 * cov);
                            byte r = (byte)Math.Round(((rgb >> 16) & 0xFF) * cov);
                            byte g = (byte)Math.Round(((rgb >> 8) & 0xFF) * cov);
                            byte b = (byte)Math.Round((rgb & 0xFF) * cov);
                            pixels[idx] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                        }
                        else
                        {
                            pixels[idx] |= 0xFF000000u;
                        }
                    }
                }
            }

            private static void DrawCenteredText(IntPtr hdc, string text, NativeMethods.RECT rect, bool whiteDigits)
            {
                Color fg = whiteDigits ? Colors.White : BadgeInk;
                NativeMethods.SetBkMode(hdc, 1); // TRANSPARENT
                NativeMethods.SetTextColor(hdc, (uint)(fg.R | (fg.G << 8) | (fg.B << 16)));
                // DT_CENTER (0x1) | DT_VCENTER (0x4) | DT_SINGLELINE (0x20) | DT_NOPREFIX (0x800)
                NativeMethods.DrawTextW(hdc, text, text.Length, ref rect, 0x0825);
            }

            private static uint ToOpaqueBgra(Color c) =>
                0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

            private IntPtr CreateHIconFromSurface()
            {
                if (_hbmColor == IntPtr.Zero || _hbmMask == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                var iconInfo = new NativeMethods.ICONINFO
                {
                    fIcon = true,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmMask = _hbmMask,
                    hbmColor = _hbmColor
                };
                return NativeMethods.CreateIconIndirect(ref iconInfo);
            }

            private BitmapSource CopySurfaceToBitmapSource()
            {
                byte[] pixels = new byte[IconSize * IconSize * 4];
                if (_pBits != IntPtr.Zero)
                {
                    Marshal.Copy(_pBits, pixels, 0, pixels.Length);
                }

                var bmp = BitmapSource.Create(
                    IconSize,
                    IconSize,
                    96,
                    96,
                    PixelFormats.Pbgra32,
                    null,
                    pixels,
                    IconSize * 4);
                bmp.Freeze();
                return bmp;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_font13 != IntPtr.Zero) NativeMethods.DeleteObject(_font13);
                if (_font15 != IntPtr.Zero) NativeMethods.DeleteObject(_font15);
                if (_font18 != IntPtr.Zero) NativeMethods.DeleteObject(_font18);
                if (_font22 != IntPtr.Zero) NativeMethods.DeleteObject(_font22);
                if (_hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(_hbmColor);
                if (_hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(_hbmMask);
                if (_hdc != IntPtr.Zero) NativeMethods.DeleteDC(_hdc);
            }
        }

        internal static IntPtr CreateHIconFromBitmap(BitmapSource bitmap)
        {
            byte[] pixels = new byte[IconSize * IconSize * 4];
            bitmap.CopyPixels(pixels, IconSize * 4, 0);

            var bmi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = IconSize,
                biHeight = -IconSize, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            IntPtr hbmColor = NativeMethods.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
            if (hbmColor == IntPtr.Zero || pBits == IntPtr.Zero)
            {
                if (hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(hbmColor);
                return IntPtr.Zero;
            }

            Marshal.Copy(pixels, 0, pBits, pixels.Length);
            IntPtr hbmMask = NativeMethods.CreateBitmap(IconSize, IconSize, 1, 1, IntPtr.Zero);
            try
            {
                var iconInfo = new NativeMethods.ICONINFO
                {
                    fIcon = true,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmMask = hbmMask,
                    hbmColor = hbmColor
                };
                return NativeMethods.CreateIconIndirect(ref iconInfo);
            }
            finally
            {
                NativeMethods.DeleteObject(hbmColor);
                if (hbmMask != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hbmMask);
                }
            }
        }

        public void Dispose()
        {
            lock (_renderLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
                KeepAwakeService.Instance.StatusChanged -= OnKeepAwakeStatusChanged;
                PowerPlanService.Instance.ProfileChanged -= OnPowerPlanProfileChanged;

                RemoveSlot(_gpuSlot);
                RemoveSlot(_cpuSlot);
                RemoveSlot(_ramSlot);
                _badgeSurface?.Dispose();
                _badgeSurface = null;
                _notificationSlot = null;
                _notificationAction = null;

                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                NativeMethods.UnregisterClassW(_windowClassName, _hInstance);
                _wndProcDelegate = null;
            }

            Dispatch(() =>
            {
                if (_contextMenu != null)
                {
                    _contextMenu.IsOpen = false;
                    _contextMenu = null;
                }

                if (_menuHostWindow != null)
                {
                    try { _menuHostWindow.Close(); } catch { }
                    _menuHostWindow = null;
                }
            });
        }
    }
}
