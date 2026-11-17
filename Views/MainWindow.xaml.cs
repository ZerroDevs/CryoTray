using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;

namespace CryoTray.Views
{
    public partial class MainWindow : Window
    {
        private readonly Storyboard _fanStoryboard = new();
        private DoubleAnimation? _fanAnimation;
        private int _timeframeSamples = 60; // Default 1 min
        private OverlayWindow? _overlayWindow;

        public MainWindow()
        {
            InitializeComponent();
            SetupFanAnimation();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void SetupFanAnimation()
        {
            _fanAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(3.5),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(_fanAnimation, FanCanvas);
            Storyboard.SetTargetProperty(_fanAnimation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
            _fanStoryboard.Children.Add(_fanAnimation);
            _fanStoryboard.Begin();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            SettingsService.Instance.SettingsChanged += ApplySettings;

            PowerPlanService.Instance.CoolingModeChanged += OnCoolingModeChanged;
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;

            UpdateTheme(PowerPlanService.Instance.IsCoolingModeActive, false);
            UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        public void ApplySettings()
        {
            Dispatcher.Invoke(() =>
            {
                var s = SettingsService.Instance.Current;
                TxtAppTitle.Text = s.ShowComputerName
                    ? $"CryoTray - {Environment.MachineName}"
                    : "CryoTray";

                Topmost = s.AlwaysOnTop;
                ShowInTaskbar = s.PinToTaskbar;

                // Overlay
                if (s.OverlayEnabled)
                {
                    if (_overlayWindow == null)
                    {
                        _overlayWindow = new OverlayWindow();
                    }
                    _overlayWindow.Show();
                }
                else
                {
                    _overlayWindow?.Hide();
                }
            });
        }

        public void PositionAboveTray()
        {
            var s = SettingsService.Instance.Current;
            if (!s.AnchorToTray && s.WindowLeft.HasValue && s.WindowTop.HasValue)
            {
                Left = s.WindowLeft.Value;
                Top = s.WindowTop.Value;
                return;
            }

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 12;
        }

        private bool _isChildDialogOpen = false;

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_isChildDialogOpen) return;

            var s = SettingsService.Instance.Current;
            if (!s.AlwaysOnTop)
            {
                Hide();
            }
        }

        private void OnCoolingModeChanged(bool isCooling)
        {
            Dispatcher.Invoke(() => UpdateTheme(isCooling, true));
        }

        private void UpdateTheme(bool isCooling, bool animate)
        {
            Color target1 = (Color)ColorConverter.ConvertFromString(isCooling ? "#1B5E8A" : "#8C4327");
            Color target2 = (Color)ColorConverter.ConvertFromString(isCooling ? "#123E5C" : "#592816");

            if (animate)
            {
                var anim1 = new ColorAnimation(target1, TimeSpan.FromMilliseconds(400));
                var anim2 = new ColorAnimation(target2, TimeSpan.FromMilliseconds(400));
                GradStop1.BeginAnimation(GradientStop.ColorProperty, anim1);
                GradStop2.BeginAnimation(GradientStop.ColorProperty, anim2);
            }
            else
            {
                GradStop1.Color = target1;
                GradStop2.Color = target2;
            }

            // Fan Speed & Glow matching Camomile
            if (isCooling)
            {
                _fanStoryboard.Stop();
                _fanAnimation!.Duration = TimeSpan.FromSeconds(0.9); // Fast spin
                _fanStoryboard.Begin();

                FanGlowCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B88D8"));
                FanGlowCircle.Opacity = 0.25;
                FanBladePath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#76C6EF"));

                // Toggle Switch state: ON (Bright Blue)
                TogglePillBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
                TxtToggleState.Text = "ON";
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Left;
                TxtToggleState.Margin = new Thickness(8, 0, 0, 0);

                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleThumb.Margin = new Thickness(0, 0, 1, 0);
            }
            else
            {
                _fanStoryboard.Stop();
                _fanAnimation!.Duration = TimeSpan.FromSeconds(3.5); // Idle slow spin
                _fanStoryboard.Begin();

                FanGlowCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
                FanGlowCircle.Opacity = 0.12;
                FanBladePath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));

                // Toggle Switch state: OFF (Terracotta / Salmon)
                TogglePillBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E07D62"));
                TxtToggleState.Text = "OFF";
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Right;
                TxtToggleState.Margin = new Thickness(0, 0, 7, 0);

                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleThumb.Margin = new Thickness(1, 0, 0, 0);
            }

            DrawCpuChart();
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            Dispatcher.Invoke(() => UpdateTelemetryUI(snapshot));
        }

        private void UpdateTelemetryUI(HardwareSnapshot snapshot)
        {
            var s = SettingsService.Instance.Current;
            bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";

            double cpuT = isFahrenheit ? (snapshot.CpuTemp * 9 / 5 + 32) : snapshot.CpuTemp;
            double gpuT = isFahrenheit ? (snapshot.GpuTemp * 9 / 5 + 32) : snapshot.GpuTemp;

            // Hero Card
            TxtCpuModel.Text = snapshot.CpuName;
            TxtCpuCores.Text = $"{snapshot.CpuCores}-Core Processor";
            TxtHeroTemp.Text = $"{Math.Round(cpuT)}";
            TxtHeroUnit.Text = unit;

            // CPU Speed
            TxtCpuClockRatio.Text = $"{snapshot.CpuGhz:0.00} / {snapshot.CpuBaseGhz:0.00}";

            // GPU Row
            TxtGpuLabel.Text = $"GPU ({snapshot.GpuVendor})";

            // SSD Rows
            RenderSsdRows(snapshot.StorageDrives, isFahrenheit, unit);

            // Draw Charts
            DrawCpuChart();
            DrawGpuChart(snapshot);

            if (CpuCoresPopup != null && CpuCoresPopup.IsOpen)
            {
                UpdateCpuCoresPopup(snapshot);
            }
        }

        private void RenderSsdRows(List<DriveTelemetry> drives, bool isFahrenheit, string unit)
        {
            SsdRowsContainer.Children.Clear();

            for (int i = 0; i < drives.Count; i++)
            {
                var drive = drives[i];
                double temp = isFahrenheit ? (drive.Temperature * 9 / 5 + 32) : drive.Temperature;

                var grid = new Grid { Height = 44 };

                // Left: Drive Icon with Index + Label
                var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var iconBorder = new Border
                {
                    Width = 26,
                    Height = 26,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D1D1D")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var txtIndex = new TextBlock
                {
                    Text = drive.Index.ToString(),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = txtIndex;
                leftStack.Children.Add(iconBorder);

                var txtLabel = new TextBlock
                {
                    Text = "SSD t°",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                leftStack.Children.Add(txtLabel);

                grid.Children.Add(leftStack);

                // Right: Boxed Value + Boxed Unit
                var rightStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var valBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D1D1D")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var txtVal = new TextBlock
                {
                    Text = $"{Math.Round(temp)}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD")),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                valBorder.Child = txtVal;
                rightStack.Children.Add(valBorder);

                var unitBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D1D1D")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4)
                };

                var txtUnit = new TextBlock
                {
                    Text = unit,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                unitBorder.Child = txtUnit;
                rightStack.Children.Add(unitBorder);

                grid.Children.Add(rightStack);
                SsdRowsContainer.Children.Add(grid);

                var divider = new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202020")),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                SsdRowsContainer.Children.Add(divider);
            }
        }

        private void DrawCpuChart()
        {
            CpuChartCanvas.Children.Clear();
            double w = CpuChartCanvas.ActualWidth;
            double h = CpuChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Horizontal grid lines
            for (int i = 1; i <= 2; i++)
            {
                double y = (h / 3.0) * i;
                var line = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222222")),
                    StrokeThickness = 1
                };
                CpuChartCanvas.Children.Add(line);
            }

            // Vertical time grid lines
            for (int i = 1; i <= 2; i++)
            {
                double x = (w / 3.0) * i;
                var vline = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = h,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E")),
                    StrokeThickness = 1
                };
                CpuChartCanvas.Children.Add(vline);
            }

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(_timeframeSamples);
            if (samples.Count < 2) return;

            double minT = 25;
            double maxT = 90;

            int n = samples.Count;
            double stepX = w / Math.Max(1, n - 1);

            int start = 0;
            while (start < n - 1)
            {
                bool mode = samples[start].CoolingModeActive;
                int end = start;
                while (end < n - 1 && samples[end + 1].CoolingModeActive == mode)
                {
                    end++;
                }

                int segEnd = Math.Min(end + 1, n - 1);
                DrawCpuSegment(samples, start, segEnd, mode, minT, maxT, stepX, h);
                start = end + 1;
            }
        }

        private void DrawCpuSegment(List<TelemetrySample> samples, int start, int end, bool isCooling,
                                    double minT, double maxT, double stepX, double h)
        {
            var areaPoints = new PointCollection();
            var linePoints = new PointCollection();

            double startX = start * stepX;
            double endX = end * stepX;

            areaPoints.Add(new Point(startX, h));

            for (int i = start; i <= end; i++)
            {
                double t = samples[i].CpuTemp;
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 16) + 8);
                double x = i * stepX;

                areaPoints.Add(new Point(x, y));
                linePoints.Add(new Point(x, y));
            }

            areaPoints.Add(new Point(endX, h));

            Color areaColor = isCooling
                ? (Color)ColorConverter.ConvertFromString("#154868")
                : (Color)ColorConverter.ConvertFromString("#522616");

            var polygon = new Polygon
            {
                Points = areaPoints,
                Fill = new SolidColorBrush(Color.FromArgb(160, areaColor.R, areaColor.G, areaColor.B))
            };
            CpuChartCanvas.Children.Add(polygon);

            Color strokeColor = isCooling
                ? (Color)ColorConverter.ConvertFromString("#4FC3F7")
                : (Color)ColorConverter.ConvertFromString("#E5987E");

            var polyline = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1.6
            };
            CpuChartCanvas.Children.Add(polyline);
        }

        private void DrawGpuChart(HardwareSnapshot snapshot)
        {
            GpuChartCanvas.Children.Clear();
            double w = GpuChartCanvas.ActualWidth;
            double h = GpuChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            GpuChartCanvas.ToolTip = $"GPU: {Math.Round(snapshot.GpuTemp)}°C, Load: {Math.Round(snapshot.GpuLoad)}%";

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(60);
            if (samples.Count < 2) return;

            var hLine = new Line
            {
                X1 = 0, X2 = w, Y1 = h / 2, Y2 = h / 2,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202020")),
                StrokeThickness = 1
            };
            GpuChartCanvas.Children.Add(hLine);

            double minT = 30;
            double maxT = 90;
            double stepX = w / Math.Max(1, samples.Count - 1);

            var areaPoints = new PointCollection { new Point(0, h) };
            var linePoints = new PointCollection();

            for (int i = 0; i < samples.Count; i++)
            {
                double t = samples[i].GpuTemp;
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 8) + 4);
                double x = i * stepX;

                areaPoints.Add(new Point(x, y));
                linePoints.Add(new Point(x, y));
            }

            areaPoints.Add(new Point(w, h));

            var area = new Polygon
            {
                Points = areaPoints,
                Fill = new SolidColorBrush(Color.FromArgb(100, 124, 179, 66))
            };
            GpuChartCanvas.Children.Add(area);

            var line = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7CB342")),
                StrokeThickness = 1.5
            };
            GpuChartCanvas.Children.Add(line);
        }

        private void CpuChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCpuChart();
        }

        private void CmbTimeframe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _timeframeSamples = CmbTimeframe.SelectedIndex switch
            {
                1 => 300,  // 5 min
                2 => 600,  // 10 min
                3 => 1800, // 30 min
                4 => 3600, // 60 min
                _ => 60    // 1 min
            };

            DrawCpuChart();
        }

        private void ToggleSwitch_Click(object sender, MouseButtonEventArgs e)
        {
            PowerPlanService.Instance.ToggleCoolingMode();
        }

        private void BtnStressTest_Click(object sender, RoutedEventArgs e)
        {
            var win = new StressTestWindow { Owner = this };
            DockSideBySide(win);
            _isChildDialogOpen = true;
            try
            {
                win.ShowDialog();
            }
            finally
            {
                _isChildDialogOpen = false;
            }
        }

        private void BtnToolsMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C"))
            };

            var itemStress = new MenuItem
            {
                Header = "CPU Temperature Test",
                Foreground = Brushes.White
            };
            itemStress.Click += (s, args) =>
            {
                var win = new StressTestWindow { Owner = this };
                DockSideBySide(win);
                _isChildDialogOpen = true;
                try
                {
                    win.ShowDialog();
                }
                finally
                {
                    _isChildDialogOpen = false;
                }
            };

            var itemAbout = new MenuItem
            {
                Header = "About CryoTray",
                Foreground = Brushes.White
            };
            itemAbout.Click += (s, args) =>
            {
                MessageBox.Show(
                    "CryoTray by ZerroDevs\nUltra-lightweight native Windows hardware monitor & cooling utility.\nZero paywalls. Zero GDI leaks.",
                    "About CryoTray", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            menu.Items.Add(itemStress);
            menu.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C")) });
            menu.Items.Add(itemAbout);

            menu.PlacementTarget = BtnToolsMenu;
            menu.IsOpen = true;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow { Owner = this };
            DockSideBySide(win);
            _isChildDialogOpen = true;
            try
            {
                win.ShowDialog();
            }
            finally
            {
                _isChildDialogOpen = false;
            }
        }

        private void BtnSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            var win = new SystemInfoWindow { Owner = this };
            DockSideBySide(win);
            _isChildDialogOpen = true;
            try
            {
                win.ShowDialog();
            }
            finally
            {
                _isChildDialogOpen = false;
            }
        }

        public void DockSideBySide(Window win)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;

            double targetLeft = Left - win.Width - 6;
            double targetTop = Top + Height - win.Height;

            // If would render off-screen to the left, dock to the right
            if (targetLeft < SystemParameters.WorkArea.Left)
            {
                targetLeft = Left + Width + 6;
            }

            // Ensure top is within work area
            if (targetTop < SystemParameters.WorkArea.Top)
            {
                targetTop = SystemParameters.WorkArea.Top + 6;
            }

            win.Left = targetLeft;
            win.Top = targetTop;
        }

        private void BtnOverlay_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance.Current;
            s.OverlayEnabled = !s.OverlayEnabled;
            SettingsService.Instance.Save();
            ApplySettings();
        }

        private void BtnAutopilot_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance.Current;
            s.AutopilotEnabled = !s.AutopilotEnabled;
            SettingsService.Instance.Save();
            MessageBox.Show(
                $"Autopilot is now {(s.AutopilotEnabled ? "ENABLED" : "DISABLED")}.\nRules: Idle < {s.AutopilotIdleThresholdPercent}% turns Cooling ON.\nHeavy Load > {s.AutopilotBusyThresholdPercent}% switches to Balanced.",
                "Autopilot Cooling", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CpuTempHoverZone_MouseEnter(object sender, MouseEventArgs e)
        {
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            if (snapshot != null)
            {
                UpdateCpuCoresPopup(snapshot);
            }
            CpuCoresPopup.IsOpen = true;
        }

        private void CpuTempHoverZone_MouseLeave(object sender, MouseEventArgs e)
        {
            CpuCoresPopup.IsOpen = false;
        }

        private void UpdateCpuCoresPopup(HardwareSnapshot snapshot)
        {
            if (snapshot == null || snapshot.CpuCoresTelemetry == null || snapshot.CpuCoresTelemetry.Count == 0) return;

            var s = SettingsService.Instance.Current;
            bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";

            var cores = snapshot.CpuCoresTelemetry;
            int total = cores.Count;
            int cols = 4;
            int rows = (int)Math.Ceiling(total / (double)cols);

            CoreTempsGrid.Columns = cols;
            CoreTempsGrid.Rows = rows;
            CoreTempsGrid.Children.Clear();

            int maxTemp = -999;
            foreach (var c in cores)
            {
                double t = isFahrenheit ? (c.Temperature * 9 / 5 + 32) : c.Temperature;
                int intT = (int)Math.Round(t);
                if (intT > maxTemp) maxTemp = intT;
            }

            for (int i = 0; i < total; i++)
            {
                var core = cores[i];
                double t = isFahrenheit ? (core.Temperature * 9 / 5 + 32) : core.Temperature;
                int intT = (int)Math.Round(t);
                bool isHottest = (intT == maxTemp);

                int col = i % cols;
                int row = i / cols;

                var border = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E2E2")),
                    BorderThickness = new Thickness(0, 0, (col < cols - 1 ? 1 : 0), (row < rows - 1 ? 1 : 0))
                };

                var tb = new TextBlock
                {
                    Text = $"{core.Name} {intT}{unit}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
                    FontSize = 11.5,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.Normal,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(8, 5, 8, 5),
                    TextDecorations = isHottest ? TextDecorations.Underline : null
                };

                border.Child = tb;
                CoreTempsGrid.Children.Add(border);
            }
        }
    }
}
