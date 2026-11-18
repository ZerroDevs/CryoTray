using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Tray
{
    public class TrayIconManager : IDisposable
    {
        private const int IconSize = 32;
        private const int IconHalfHeight = IconSize / 2;

        private readonly NotifyIcon _gpuIcon;
        private readonly NotifyIcon _cpuIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _itemOpen;
        private readonly ToolStripMenuItem _itemCooling;
        private readonly ToolStripMenuItem _itemStress;
        private readonly ToolStripMenuItem _itemSettings;
        private readonly ToolStripMenuItem _itemExit;

        private readonly Bitmap _iconBitmap;
        private readonly Graphics _iconGraphics;
        private readonly SolidBrush _backgroundBrush;
        private readonly SolidBrush _textBrush;
        private readonly SolidBrush _dividerBrush;
        private readonly Font _singleDigitFont;
        private readonly Font _doubleDigitFont;
        private readonly Font _tripleDigitFont;
        private readonly Font _mergedFont;
        private readonly StringFormat _centerFormat;
        private readonly GraphicsPath _roundedPath;

        private Icon? _currentGpuIcon;
        private Icon? _currentCpuIcon;
        private IntPtr _currentGpuHIcon;
        private IntPtr _currentCpuHIcon;
        private Action? _notificationAction;
        private NotifyIcon? _notificationIcon;
        private DateTime _notificationHoldUntilUtc;
        private double _lastGpuTemp;
        private double _lastCpuTemp;
        private bool _lastCoolingModeActive;
        private string _lastBadgeSignature = "";
        private bool _disposed;
        private readonly object _renderLock = new();

        public event Action? OnOpenDashboardRequested;
        public event Action? OnToggleCoolingRequested;
        public event Action? OnOpenStressTestRequested;
        public event Action? OnOpenSettingsRequested;
        public event Action? OnExitRequested;

        // Camomile tray badge palette.
        private static readonly Color GpuGreen = ColorTranslator.FromHtml("#B3D872");
        private static readonly Color CpuAmber = ColorTranslator.FromHtml("#FFBDAF");
        private static readonly Color CpuCyan = ColorTranslator.FromHtml("#8AD2F1");
        private static readonly Color MergedCpuOff = ColorTranslator.FromHtml("#E5987E");
        private static readonly Color MergedCpuOn = ColorTranslator.FromHtml("#4FC3F7");
        private static readonly Color MergedGpuGreen = ColorTranslator.FromHtml("#7CB342");
        private static readonly Color DividerColor = ColorTranslator.FromHtml("#141414");

        public TrayIconManager()
        {
            _contextMenu = CreateDarkContextMenu();
            _itemOpen = (ToolStripMenuItem)_contextMenu.Items[0];
            _itemCooling = (ToolStripMenuItem)_contextMenu.Items[2];
            _itemStress = (ToolStripMenuItem)_contextMenu.Items[3];
            _itemSettings = (ToolStripMenuItem)_contextMenu.Items[4];
            _itemExit = (ToolStripMenuItem)_contextMenu.Items[6];

            _iconBitmap = new Bitmap(IconSize, IconSize);
            _iconGraphics = Graphics.FromImage(_iconBitmap);
            _iconGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            _iconGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _iconGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _iconGraphics.CompositingQuality = CompositingQuality.HighQuality;

            _backgroundBrush = new SolidBrush(Color.Transparent);
            _textBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
            _dividerBrush = new SolidBrush(DividerColor);
            _singleDigitFont = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
            _doubleDigitFont = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            _tripleDigitFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            _mergedFont = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            _centerFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.None
            };
            _roundedPath = CreateRoundedPath();

            _gpuIcon = new NotifyIcon
            {
                Text = "CryoTray",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _gpuIcon.MouseClick += OnTrayIconClick;
            _gpuIcon.BalloonTipClicked += OnBalloonTipClicked;

            _cpuIcon = new NotifyIcon
            {
                Text = "CryoTray",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _cpuIcon.MouseClick += OnTrayIconClick;
            _cpuIcon.BalloonTipClicked += OnBalloonTipClicked;

            SettingsService.Instance.SettingsChanged += OnSettingsChanged;

            ApplyLanguage();
            UpdateBadges(52, 37, false);
        }

        private static GraphicsPath CreateRoundedPath()
        {
            const int radius = 5;
            var path = new GraphicsPath();
            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            path.AddArc(IconSize - (radius * 2), 0, radius * 2, radius * 2, 270, 90);
            path.AddArc(IconSize - (radius * 2), IconSize - (radius * 2),
                radius * 2, radius * 2, 0, 90);
            path.AddArc(0, IconSize - (radius * 2), radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void OnSettingsChanged()
        {
            if (_disposed)
            {
                return;
            }

            UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, true);
        }

        public void ApplyLanguage()
        {
            _contextMenu.RightToLeft = LocalizationService.Instance.IsArabic
                ? RightToLeft.Yes
                : RightToLeft.No;

            _itemOpen.Text = LocalizationService.Get("Tray_OpenDashboard");
            _itemCooling.Text = LocalizationService.Get("Tray_ToggleCooling");
            _itemStress.Text = LocalizationService.Get("Tray_CpuTest");
            _itemSettings.Text = LocalizationService.Get("Tray_Settings");
            _itemExit.Text = LocalizationService.Get("Tray_Exit");

            if (_lastGpuTemp > 0 || _lastCpuTemp > 0)
            {
                UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive, true);
            }
        }

        public void ShowNotification(string title, string message, Action? onClick = null,
            ToolTipIcon icon = ToolTipIcon.Info)
        {
            NotifyIcon notifyIcon = _cpuIcon.Visible
                ? _cpuIcon
                : _gpuIcon.Visible
                    ? _gpuIcon
                    : _cpuIcon;

            notifyIcon.Visible = true;
            _notificationIcon = notifyIcon;
            _notificationHoldUntilUtc = DateTime.UtcNow.AddSeconds(12);

            _notificationAction = onClick;
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = message;
            notifyIcon.BalloonTipIcon = icon;
            notifyIcon.ShowBalloonTip(10000);
        }

        private void OnBalloonTipClicked(object? sender, EventArgs e)
        {
            Action? action = _notificationAction;
            _notificationAction = null;
            action?.Invoke();
        }

        private void OnTrayIconClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                OnOpenDashboardRequested?.Invoke();
            }
        }

        private ContextMenuStrip CreateDarkContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false
            };

            var itemOpen = new ToolStripMenuItem("Open Dashboard");
            itemOpen.Click += (s, e) => OnOpenDashboardRequested?.Invoke();

            var itemCooling = new ToolStripMenuItem("Toggle Cooling Mode");
            itemCooling.Click += (s, e) => OnToggleCoolingRequested?.Invoke();

            var itemStress = new ToolStripMenuItem("CPU Temperature Test");
            itemStress.Click += (s, e) => OnOpenStressTestRequested?.Invoke();

            var itemSettings = new ToolStripMenuItem("Settings");
            itemSettings.Click += (s, e) => OnOpenSettingsRequested?.Invoke();

            var itemExit = new ToolStripMenuItem("Exit");
            itemExit.Click += (s, e) => OnExitRequested?.Invoke();

            menu.Items.AddRange(new ToolStripItem[]
            {
                itemOpen,
                new ToolStripSeparator(),
                itemCooling,
                itemStress,
                itemSettings,
                new ToolStripSeparator(),
                itemExit
            });

            return menu;
        }

        public void UpdateBadges(double gpuTemp, double cpuTemp, bool coolingModeActive, bool force = false)
        {
            lock (_renderLock)
            {
                if (_disposed)
                {
                    return;
                }

                var settings = SettingsService.Instance.Current;
            bool holdCpu = _notificationIcon == _cpuIcon && DateTime.UtcNow < _notificationHoldUntilUtc;
            bool holdGpu = _notificationIcon == _gpuIcon && DateTime.UtcNow < _notificationHoldUntilUtc;

            if (!holdCpu && !holdGpu)
            {
                _notificationIcon = null;
            }

            bool isFahrenheit = settings.TemperatureUnit == "Fahrenheit";
            double displayGpuTemp = isFahrenheit ? (gpuTemp * 9 / 5) + 32 : gpuTemp;
            double displayCpuTemp = isFahrenheit ? (cpuTemp * 9 / 5) + 32 : cpuTemp;
            string unit = isFahrenheit ? "°F" : "°C";
            int gpuInt = (int)Math.Round(displayGpuTemp);
            int cpuInt = (int)Math.Round(displayCpuTemp);
            string signature = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                gpuInt, cpuInt, coolingModeActive, settings.MergeTrayIcons,
                settings.ShowGpuBadge, settings.ShowCpuBadge, isFahrenheit, holdCpu, holdGpu);

            _lastGpuTemp = gpuTemp;
            _lastCpuTemp = cpuTemp;
            _lastCoolingModeActive = coolingModeActive;

            if (!force && signature == _lastBadgeSignature)
            {
                return;
            }

            _lastBadgeSignature = signature;
            Color cpuColor = coolingModeActive ? CpuCyan : CpuAmber;

            if (settings.MergeTrayIcons)
            {
                _gpuIcon.Visible = false;
                _cpuIcon.Visible = settings.ShowCpuBadge || settings.ShowGpuBadge || holdCpu || holdGpu;
                ClearGpuIcon();

                if (_cpuIcon.Visible)
                {
                    DrawMergedBadge(cpuInt, gpuInt,
                        coolingModeActive ? MergedCpuOn : MergedCpuOff);
                    ReplaceCpuIcon();
                    _cpuIcon.Text = LocalizationService.Format(
                        "Tray_MergedTooltip", cpuInt, unit, gpuInt);
                }
                else
                {
                    ClearCpuIcon();
                }

                return;
            }

            _gpuIcon.Visible = settings.ShowGpuBadge || holdGpu;
            _cpuIcon.Visible = settings.ShowCpuBadge || holdCpu;

            if (_gpuIcon.Visible)
            {
                DrawSingleBadge(gpuInt, GpuGreen);
                ReplaceGpuIcon();
                _gpuIcon.Text = LocalizationService.Format("Tray_GpuTooltip", gpuInt, unit);
            }
            else
            {
                ClearGpuIcon();
            }

            if (_cpuIcon.Visible)
            {
                DrawSingleBadge(cpuInt, cpuColor);
                ReplaceCpuIcon();
                string coolingText = LocalizationService.Get(
                    coolingModeActive ? "Common_On" : "Common_Off");
                _cpuIcon.Text = LocalizationService.Format(
                    "Tray_CpuTooltip", cpuInt, unit, coolingText);
            }
            else
            {
                ClearCpuIcon();
            }
            }
        }

        private void DrawSingleBadge(int temp, Color background)
        {
            PrepareCanvas();
            _backgroundBrush.Color = background;
            _iconGraphics.FillRectangle(_backgroundBrush, 0, 0, IconSize, IconSize);
            DrawTemperature(temp, GetSingleDigitFont(temp),
                new RectangleF(0, 2, IconSize, IconSize - 2));
        }

        private void DrawMergedBadge(int cpuTemp, int gpuTemp, Color cpuBackground)
        {
            PrepareCanvas();
            _iconGraphics.SetClip(_roundedPath);

            _backgroundBrush.Color = cpuBackground;
            _iconGraphics.FillRectangle(_backgroundBrush, 0, 0, IconSize, IconHalfHeight);
            _backgroundBrush.Color = MergedGpuGreen;
            _iconGraphics.FillRectangle(_backgroundBrush, 0, IconHalfHeight,
                IconSize, IconHalfHeight);
            _iconGraphics.FillRectangle(_dividerBrush, 0, IconHalfHeight - 1,
                IconSize, 1);

            DrawTemperature(cpuTemp, _mergedFont,
                new RectangleF(0, 0, IconSize, IconHalfHeight - 1));
            DrawTemperature(gpuTemp, _mergedFont,
                new RectangleF(0, IconHalfHeight, IconSize, IconHalfHeight));
            _iconGraphics.ResetClip();
        }

        private void PrepareCanvas()
        {
            _iconGraphics.ResetClip();
            _iconGraphics.Clear(Color.Transparent);
        }

        private Font GetSingleDigitFont(int temp)
        {
            if (temp >= 100)
            {
                return _tripleDigitFont;
            }

            return temp >= 10 ? _doubleDigitFont : _singleDigitFont;
        }

        private void DrawTemperature(int temp, Font font, RectangleF bounds)
        {
            string text = temp.ToString(CultureInfo.InvariantCulture);
            _iconGraphics.DrawString(text, font, _textBrush, bounds, _centerFormat);
        }

        private void ReplaceCpuIcon()
        {
            IntPtr newHandle = _iconBitmap.GetHicon();
            Icon newIcon = Icon.FromHandle(newHandle);
            Icon? oldIcon = _currentCpuIcon;
            IntPtr oldHandle = _currentCpuHIcon;

            _cpuIcon.Icon = newIcon;
            _currentCpuIcon = newIcon;
            _currentCpuHIcon = newHandle;

            if (oldHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }

            oldIcon?.Dispose();
        }

        private void ReplaceGpuIcon()
        {
            IntPtr newHandle = _iconBitmap.GetHicon();
            Icon newIcon = Icon.FromHandle(newHandle);
            Icon? oldIcon = _currentGpuIcon;
            IntPtr oldHandle = _currentGpuHIcon;

            _gpuIcon.Icon = newIcon;
            _currentGpuIcon = newIcon;
            _currentGpuHIcon = newHandle;

            if (oldHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }

            oldIcon?.Dispose();
        }

        private void ClearCpuIcon()
        {
            var oldIcon = _currentCpuIcon;
            IntPtr oldHandle = _currentCpuHIcon;
            _cpuIcon.Icon = null;
            _currentCpuIcon = null;
            _currentCpuHIcon = IntPtr.Zero;

            if (oldHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }

            oldIcon?.Dispose();
        }

        private void ClearGpuIcon()
        {
            var oldIcon = _currentGpuIcon;
            IntPtr oldHandle = _currentGpuHIcon;
            _gpuIcon.Icon = null;
            _currentGpuIcon = null;
            _currentGpuHIcon = IntPtr.Zero;

            if (oldHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(oldHandle);
            }

            oldIcon?.Dispose();
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

                _gpuIcon.Visible = false;
                _cpuIcon.Visible = false;
                ClearGpuIcon();
                ClearCpuIcon();
                _notificationIcon = null;

                _gpuIcon.Dispose();
                _cpuIcon.Dispose();
                _contextMenu.Dispose();

                _centerFormat.Dispose();
                _roundedPath.Dispose();
                _singleDigitFont.Dispose();
                _doubleDigitFont.Dispose();
                _tripleDigitFont.Dispose();
                _mergedFont.Dispose();
                _backgroundBrush.Dispose();
                _textBrush.Dispose();
                _dividerBrush.Dispose();
                _iconGraphics.Dispose();
                _iconBitmap.Dispose();
            }
        }

        private class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Color.FromArgb(235, 235, 235);
                base.OnRenderItemText(e);
            }
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 24);
            public override Color MenuBorder => Color.FromArgb(45, 45, 45);
            public override Color MenuItemSelected => Color.FromArgb(42, 42, 42);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(42, 42, 42);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(42, 42, 42);
            public override Color MenuItemBorder => Color.Transparent;
            public override Color SeparatorDark => Color.FromArgb(40, 40, 40);
            public override Color SeparatorLight => Color.FromArgb(40, 40, 40);
        }
    }
}
