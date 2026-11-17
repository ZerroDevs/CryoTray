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

        private Icon? _currentGpuIcon;
        private Icon? _currentCpuIcon;

        public event Action? OnOpenDashboardRequested;
        public event Action? OnToggleCoolingRequested;
        public event Action? OnOpenStressTestRequested;
        public event Action? OnOpenSettingsRequested;
        public event Action? OnExitRequested;

        // Exact Camomile pastel palette
        private static readonly Color GpuGreen = ColorTranslator.FromHtml("#A2D065"); // Light lime green
        private static readonly Color CpuAmber = ColorTranslator.FromHtml("#F8AFA6"); // Light peach / salmon
        private static readonly Color CpuCyan = ColorTranslator.FromHtml("#76C6EF");  // Light sky cyan

        public TrayIconManager()
        {
            _contextMenu = CreateDarkContextMenu();

            _gpuIcon = new NotifyIcon
            {
                Text = "CryoTray GPU Monitor",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _gpuIcon.MouseClick += OnTrayIconClick;

            _cpuIcon = new NotifyIcon
            {
                Text = "CryoTray CPU Monitor",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _cpuIcon.MouseClick += OnTrayIconClick;

            // Initial blank icons
            UpdateBadges(52, 37, false);
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

            _gpuIcon.Visible = settings.ShowGpuBadge;
            _cpuIcon.Visible = settings.ShowCpuBadge;

            int gpuInt = (int)Math.Round(gpuTemp);
            int cpuInt = (int)Math.Round(cpuTemp);

            Color cpuColor = coolingModeActive ? CpuCyan : CpuAmber;

            // Update GPU icon with GDI handle destruction
            Icon newGpuIcon = CreateBadgeIcon(gpuInt, GpuGreen);
            var oldGpu = _currentGpuIcon;
            _gpuIcon.Icon = newGpuIcon;
            _currentGpuIcon = newGpuIcon;
            oldGpu?.Dispose();

            _gpuIcon.Text = $"GPU: {gpuInt}°C";

            // Update CPU icon with GDI handle destruction
            Icon newCpuIcon = CreateBadgeIcon(cpuInt, cpuColor);
            var oldCpu = _currentCpuIcon;
            _cpuIcon.Icon = newCpuIcon;
            _currentCpuIcon = newCpuIcon;
            oldCpu?.Dispose();

            _cpuIcon.Text = $"CPU: {cpuInt}°C (Cooling: {(coolingModeActive ? "ON" : "OFF")})";
        }

        private Icon CreateBadgeIcon(int temp, Color bgColor)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Sharp square badge with subtle 2px soft radius matching Camomile
                using var path = CreateRoundedRectanglePath(new Rectangle(1, 1, 30, 30), 2);
                using var brush = new SolidBrush(bgColor);
                g.FillPath(brush, path);

                // Bold dark charcoal text matching Camomile (#111111)
                string text = temp.ToString();
                using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.FromArgb(17, 17, 17));
                using var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                g.DrawString(text, font, textBrush, new RectangleF(0, 0, 32, 31), sf);
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

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new(rect.X, rect.Y, diameter, diameter);

            // top left
            path.AddArc(arc, 180, 90);
            // top right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            // bottom right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            // bottom left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _gpuIcon.Visible = false;
            _gpuIcon.Dispose();
            _cpuIcon.Visible = false;
            _cpuIcon.Dispose();

            _currentGpuIcon?.Dispose();
            _currentCpuIcon?.Dispose();
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
