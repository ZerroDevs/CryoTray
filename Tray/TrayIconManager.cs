using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Tray
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _gpuIcon;
        private readonly NotifyIcon _cpuIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _itemOpen;
        private readonly ToolStripMenuItem _itemCooling;
        private readonly ToolStripMenuItem _itemStress;
        private readonly ToolStripMenuItem _itemSettings;
        private readonly ToolStripMenuItem _itemExit;

        private Icon? _currentGpuIcon;
        private Icon? _currentCpuIcon;
        private Action? _notificationAction;
        private NotifyIcon? _notificationIcon;
        private DateTime _notificationHoldUntilUtc;
        private double _lastGpuTemp;
        private double _lastCpuTemp;
        private bool _lastCoolingModeActive;

        public event Action? OnOpenDashboardRequested;
        public event Action? OnToggleCoolingRequested;
        public event Action? OnOpenStressTestRequested;
        public event Action? OnOpenSettingsRequested;
        public event Action? OnExitRequested;

        // Camomile tray badge palette.
        private static readonly Color GpuGreen = ColorTranslator.FromHtml("#B3D872");
        private static readonly Color CpuAmber = ColorTranslator.FromHtml("#FFBDAF");
        private static readonly Color CpuCyan = ColorTranslator.FromHtml("#8AD2F1");

        public TrayIconManager()
        {
            _contextMenu = CreateDarkContextMenu();
            _itemOpen = (ToolStripMenuItem)_contextMenu.Items[0];
            _itemCooling = (ToolStripMenuItem)_contextMenu.Items[2];
            _itemStress = (ToolStripMenuItem)_contextMenu.Items[3];
            _itemSettings = (ToolStripMenuItem)_contextMenu.Items[4];
            _itemExit = (ToolStripMenuItem)_contextMenu.Items[6];

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

            ApplyLanguage();
            UpdateBadges(52, 37, false);
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
                UpdateBadges(_lastGpuTemp, _lastCpuTemp, _lastCoolingModeActive);
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

            // Keep a badge visible long enough for the balloon to appear even when
            // both tray badges are hidden in Settings.
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

        public void UpdateBadges(double gpuTemp, double cpuTemp, bool coolingModeActive)
        {
            var settings = SettingsService.Instance.Current;
            bool holdCpu = _notificationIcon == _cpuIcon && DateTime.UtcNow < _notificationHoldUntilUtc;
            bool holdGpu = _notificationIcon == _gpuIcon && DateTime.UtcNow < _notificationHoldUntilUtc;

            _gpuIcon.Visible = settings.ShowGpuBadge || holdGpu;
            _cpuIcon.Visible = settings.ShowCpuBadge || holdCpu;

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

            Color cpuColor = coolingModeActive ? CpuCyan : CpuAmber;

            // Update GPU icon with GDI handle destruction
            Icon newGpuIcon = CreateBadgeIcon(gpuInt, GpuGreen);
            var oldGpu = _currentGpuIcon;
            _gpuIcon.Icon = newGpuIcon;
            _currentGpuIcon = newGpuIcon;
            oldGpu?.Dispose();

            _lastGpuTemp = gpuTemp;
            _lastCpuTemp = cpuTemp;
            _lastCoolingModeActive = coolingModeActive;

            _gpuIcon.Text = LocalizationService.Format("Tray_GpuTooltip", gpuInt, unit);

            // Update CPU icon with GDI handle destruction
            Icon newCpuIcon = CreateBadgeIcon(cpuInt, cpuColor);
            var oldCpu = _currentCpuIcon;
            _cpuIcon.Icon = newCpuIcon;
            _currentCpuIcon = newCpuIcon;
            oldCpu?.Dispose();

            string coolingText = LocalizationService.Get(coolingModeActive ? "Common_On" : "Common_Off");
            _cpuIcon.Text = LocalizationService.Format("Tray_CpuTooltip", cpuInt, unit, coolingText);
        }

        private Icon CreateBadgeIcon(int temp, Color bgColor)
        {
            // The shell draws NotifyIcon at its own small-icon size. Rendering at that
            // size keeps the number crisp instead of downscaling a 32 px bitmap.
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Camomile's left pair are flat, sharp-cornered full-bleed tiles.
                // A border or rounded corner makes the fixed 16 px tray slot look smaller.
                var bounds = new Rectangle(0, 0, size, size);
                using var brush = new SolidBrush(bgColor);
                g.FillRectangle(brush, bounds);

                string text = temp.ToString();
                float fontSize = size * (text.Length >= 3 ? 0.55f : 0.70f);
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
                using var sf = StringFormat.GenericTypographic;
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                // The typographic metrics sit slightly high in the square; nudge the
                // digits down so the margin reads even at native tray size.
                g.DrawString(text, font, textBrush, new RectangleF(0, size * 0.065f, size, size), sf);
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        public void Dispose()
        {
            _gpuIcon.Visible = false;
            _gpuIcon.Dispose();
            _cpuIcon.Visible = false;
            _cpuIcon.Dispose();

            _currentGpuIcon?.Dispose();
            _currentCpuIcon?.Dispose();
            _notificationIcon = null;
            _contextMenu.Dispose();
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
