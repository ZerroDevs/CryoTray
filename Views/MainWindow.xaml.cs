using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;
using WinForms = System.Windows.Forms;

namespace CryoTray.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<CoreReadout> CoreItems { get; } = new();

        private readonly Storyboard _fanStoryboard = new();
        private DoubleAnimation? _fanAnimation;
        private bool _fanRunning;
        private int _timeframeSamples = 60; // Default 1 min
        private int _coreColumns = 4;
        private bool _isClosing;
        private bool _isExitConfirmationOpen;
        private bool _showKeyboardFocusCues;
        private readonly double _expandedHeight;

        private List<TelemetrySample> _cpuChartSamples = new();
        private List<TelemetrySample> _gpuChartSamples = new();
        private bool _cpuHoverActive;
        private bool _gpuHoverActive;
        private double _cpuHoverX;
        private double _gpuHoverX;

        private readonly List<SsdRowView> _ssdRows = new();
        private string? _ssdRowSignature;

        private sealed class SsdRowView
        {
            public required Grid Root { get; init; }
            public required Border TempBorder { get; init; }
            public required TextBlock TempText { get; init; }
            public required TextBlock UnitText { get; init; }
            public required TextBlock TooltipTitle { get; init; }
            public required TextBlock TooltipHealth { get; init; }
        }

        public int CoreColumns
        {
            get => _coreColumns;
            private set
            {
                if (_coreColumns == value)
                {
                    return;
                }

                _coreColumns = value;
                OnPropertyChanged();
            }
        }

        public bool ShowKeyboardFocusCues
        {
            get => _showKeyboardFocusCues;
            private set
            {
                if (_showKeyboardFocusCues == value)
                {
                    return;
                }

                _showKeyboardFocusCues = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _expandedHeight = Height;
            LocalizationService.Instance.ApplyWindow(this);
            DataContext = this;
            CpuCoresPopup.Opened += CpuCoresPopup_Opened;
            SetupFanAnimation();

            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
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

            // The fan is decorative; 30 fps keeps it smooth with less render work.
            Timeline.SetDesiredFrameRate(_fanAnimation, 30);
            Storyboard.SetTarget(_fanAnimation, FanRotor);
            Storyboard.SetTargetProperty(_fanAnimation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
            _fanStoryboard.Children.Add(_fanAnimation);
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            StartFanAnimation();
            if (IsLoaded)
            {
                UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            StopFanAnimation();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                StartFanAnimation();
                if (IsLoaded)
                {
                    UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
                }
            }
            else
            {
                StopFanAnimation();
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                StopFanAnimation();
                return;
            }

            StartFanAnimation();
            if (IsLoaded)
            {
                UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
            }
        }

        private bool CanAnimateFan =>
            IsVisible && IsActive && WindowState != WindowState.Minimized;

        private void StartFanAnimation()
        {
            if (_fanRunning || _fanAnimation == null || !CanAnimateFan)
            {
                return;
            }

            _fanStoryboard.Begin();
            _fanRunning = true;
        }

        private void StopFanAnimation()
        {
            if (!_fanRunning)
            {
                return;
            }

            _fanStoryboard.Stop();
            _fanRunning = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            SettingsService.Instance.SettingsChanged += ApplySettings;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            PowerPlanService.Instance.CoolingModeChanged += OnCoolingModeChanged;
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;

            UpdateTheme(PowerPlanService.Instance.IsCoolingModeActive, false);
            UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
            StartFanAnimation();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            HideChartTooltips();
            CpuCoresPopup.IsOpen = false;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            IsVisibleChanged -= MainWindow_IsVisibleChanged;
            Activated -= MainWindow_Activated;
            Deactivated -= MainWindow_Deactivated;
            StateChanged -= MainWindow_StateChanged;
            CpuCoresPopup.Opened -= CpuCoresPopup_Opened;
            SettingsService.Instance.SettingsChanged -= ApplySettings;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            PowerPlanService.Instance.CoolingModeChanged -= OnCoolingModeChanged;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            StopFanAnimation();
            DataContext = null;
            Content = null;
        }

        public void ApplySettings()
        {
            Dispatcher.Invoke(() =>
            {
                var s = SettingsService.Instance.Current;
                TxtAppTitle.Text = s.ShowComputerName
                    ? LocalizationService.Format("Main_AppWithPc", Environment.MachineName)
                    : LocalizationService.Get("App_Title");

                TxtCpuModel.TextAlignment = LocalizationService.Instance.IsArabic
                    ? TextAlignment.Right
                    : TextAlignment.Left;

                Topmost = s.AlwaysOnTop;
                ShowInTaskbar = s.PinToTaskbar;
                ApplyTelemetryVisibility(s);

                OverlayRow.Visibility = s.ShowOverlayRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                AutopilotRow.Visibility = s.ShowAutopilotRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                AutopilotSeparator.Visibility = s.ShowOverlayRow && s.ShowAutopilotRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                UpdateWindowHeight(s.ShowOverlayRow, s.ShowAutopilotRow);
            });
        }

        private void UpdateWindowHeight(bool showOverlay, bool showAutopilot)
        {
            double height = _expandedHeight;
            if (!showOverlay)
            {
                height -= OverlayRow.Height;
            }
            if (!showAutopilot)
            {
                height -= AutopilotRow.Height;
            }
            if (!showOverlay || !showAutopilot)
            {
                height -= AutopilotSeparator.Height
                    + AutopilotSeparator.Margin.Top
                    + AutopilotSeparator.Margin.Bottom;
            }
            if (GpuHealthRow.Visibility != Visibility.Visible)
            {
                height -= GpuHealthRow.Height;
            }

            if (Math.Abs(Height - height) < 0.1)
            {
                return;
            }

            Height = height;
            if (SettingsService.Instance.Current.AnchorToTray)
            {
                Top = SystemParameters.WorkArea.Bottom - Height - 12;
            }
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

        private static readonly Brush InactiveTitleIconBrush = CreateInactiveTitleIconBrush();
        private SettingsWindow? _settingsWindow;
        private SystemInfoWindow? _systemInfoWindow;
        private StressTestWindow? _stressTestWindow;
        private bool _isChildDialogOpen;

        private static Brush CreateInactiveTitleIconBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xC4));
            brush.Freeze();
            return brush;
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_isChildDialogOpen || _isClosing || _isExitConfirmationOpen) return;

            var s = SettingsService.Instance.Current;
            if (!s.HideOnFocusLoss || s.AlwaysOnTop) return;

            HideChartTooltips();
            Close();
        }

        private void OnCoolingModeChanged(bool isCooling)
        {
            Dispatcher.Invoke(() => UpdateTheme(isCooling, true));
        }

        private void UpdateTheme(bool isCooling, bool animate)
        {
            Color target1 = (Color)ColorConverter.ConvertFromString(isCooling ? "#255D8E" : "#9E4531");
            Color target2 = (Color)ColorConverter.ConvertFromString(isCooling ? "#1E9FD0" : "#C26A45");

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

            ApplyFanTheme(isCooling);

            if (isCooling)
            {
                // Toggle Switch state: ON (Blue track, white thumb)
                TogglePillBorder.Width = LocalizationService.Instance.IsArabic ? 68 : 48;
                TogglePillBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B88D8"));
                TxtToggleState.Text = LocalizationService.Get("Common_On");
                TxtToggleState.Foreground = Brushes.White;
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Left;
                TxtToggleState.Margin = new Thickness(7, 0, 0, 0);

                ToggleThumb.Fill = Brushes.White;
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleThumb.Margin = new Thickness(0, 0, 0, 0);
            }
            else
            {
                // Toggle Switch state: OFF (Brown track, coral thumb)
                TogglePillBorder.Width = LocalizationService.Instance.IsArabic ? 68 : 48;
                TogglePillBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A2A1E"));
                TxtToggleState.Text = LocalizationService.Get("Common_Off");
                TxtToggleState.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9835B"));
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Right;
                TxtToggleState.Margin = new Thickness(0, 0, 7, 0);

                ToggleThumb.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E68A57"));
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleThumb.Margin = new Thickness(0, 0, 0, 0);
            }

            DrawCpuChart();
        }

        private void ApplyFanTheme(bool isCooling)
        {
            Color accent = (Color)ColorConverter.ConvertFromString(isCooling ? "#35D7FF" : "#F08A4B");
            Color ring = (Color)ColorConverter.ConvertFromString(isCooling ? "#1A7A99" : "#8A4728");
            Color circuits = (Color)ColorConverter.ConvertFromString(isCooling ? "#1F9FC4" : "#C56B3C");
            Color dots = (Color)ColorConverter.ConvertFromString(isCooling ? "#8FF4FF" : "#FFD0A3");
            Color tileTop = (Color)ColorConverter.ConvertFromString(isCooling ? "#071A2B" : "#1A0E0A");
            Color tileMiddle = (Color)ColorConverter.ConvertFromString(isCooling ? "#0B2A40" : "#2A160F");
            Color tileBottom = (Color)ColorConverter.ConvertFromString(isCooling ? "#071722" : "#140B08");
            Color petalTop = (Color)ColorConverter.ConvertFromString(isCooling ? "#E9FDFF" : "#FFE3B5");
            Color petalMiddle = (Color)ColorConverter.ConvertFromString(isCooling ? "#9DE8FF" : "#FFAD5C");
            Color petalBottom = (Color)ColorConverter.ConvertFromString(isCooling ? "#3FAFD9" : "#C94A1E");
            Color petalOutline = (Color)ColorConverter.ConvertFromString(isCooling ? "#C8F7FF" : "#FFC07A");
            Color petalVein = (Color)ColorConverter.ConvertFromString(isCooling ? "#E8FFFF" : "#FFE2B8");

            FanTile.BorderBrush = new SolidColorBrush(accent);
            FanGlowEffect.Color = accent;
            FanGlowEffect.Opacity = isCooling ? 0.55 : 0.5;
            FanTileStop1.Color = tileTop;
            FanTileStop2.Color = tileMiddle;
            FanTileStop3.Color = tileBottom;
            FanPetalStop1.Color = petalTop;
            FanPetalStop2.Color = petalMiddle;
            FanPetalStop3.Color = petalBottom;
            FanPetals.Stroke = new SolidColorBrush(petalOutline);
            FanPetalVeins.Stroke = new SolidColorBrush(petalVein);
            FanOuterRing.Stroke = new SolidColorBrush(accent);
            FanInnerRing.Stroke = new SolidColorBrush(ring);
            FanCircuitTraces.Stroke = new SolidColorBrush(circuits);
            FanRimDots.Fill = new SolidColorBrush(dots);
            FanPetalGlint.Opacity = isCooling ? 0.8 : 0.6;

            if (_fanAnimation != null)
            {
                StopFanAnimation();
                _fanAnimation.Duration = TimeSpan.FromSeconds(isCooling ? 0.9 : 3.5);
                StartFanAnimation();
            }
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    return;
                }

                UpdateTelemetryUI(snapshot);
            }));
        }

        private void UpdateTelemetryUI(HardwareSnapshot snapshot)
        {
            var s = SettingsService.Instance.Current;
            bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";

            double cpuT = isFahrenheit ? (snapshot.CpuTemp * 9 / 5 + 32) : snapshot.CpuTemp;
            double gpuT = isFahrenheit ? (snapshot.GpuTemp * 9 / 5 + 32) : snapshot.GpuTemp;

            // Hero Card
            TxtCpuModel.Text = string.IsNullOrWhiteSpace(snapshot.CpuName)
                ? LocalizationService.Get("Common_NotReported")
                : snapshot.CpuName;
            TxtCpuCores.Text = snapshot.CpuCores > 0
                ? LocalizationService.Format("Main_CpuProcessor", snapshot.CpuCores)
                : string.Empty;
            TxtHeroTemp.Text = $"{Math.Round(cpuT)}";
            TxtHeroUnit.Text = unit;

            // CPU Speed
            TxtCpuClockRatio.Text = $"{snapshot.CpuGhz:0.00} / {snapshot.CpuMaxGhz:0.00}";
            TxtCpuPackageWatts.Text = snapshot.CpuPackageWatts.HasValue
                ? $"{snapshot.CpuPackageWatts.Value}W"
                : "--W";
            TxtCpuPackagePowerPopup.Text = FormatPackagePower(snapshot.CpuPackageWatts);

            // GPU Row
            TxtGpuLabel.Text = LocalizationService.Get("Main_GpuLabel");
            TxtGpuTemp.Text = $"{Math.Round(gpuT)}{unit}";
            TxtGpuPower.Text = snapshot.GpuPowerWatts.HasValue
                ? $"{snapshot.GpuPowerWatts.Value}W"
                : "--W";
            TxtGpuVendor.Text = snapshot.GpuVendor?.Trim() ?? "";
            TxtGpuVendor.Visibility = string.IsNullOrWhiteSpace(TxtGpuVendor.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            string fullGpuName = snapshot.GpuName?.Trim() ?? "";
            TxtGpuName.Text = CompactGpuName(fullGpuName);
            TxtGpuName.ToolTip = string.IsNullOrWhiteSpace(fullGpuName)
                ? null
                : fullGpuName;

            double? hotspotT = snapshot.GpuHotspotTemp.HasValue
                ? (isFahrenheit ? snapshot.GpuHotspotTemp.Value * 9 / 5 + 32 : snapshot.GpuHotspotTemp.Value)
                : null;
            double? vramT = snapshot.GpuVramTemp.HasValue
                ? (isFahrenheit ? snapshot.GpuVramTemp.Value * 9 / 5 + 32 : snapshot.GpuVramTemp.Value)
                : null;
            TxtGpuHotspot.Text = hotspotT.HasValue
                ? $"{Math.Round(hotspotT.Value)}{unit}"
                : "--";
            TxtGpuVram.Text = vramT.HasValue
                ? $"{Math.Round(vramT.Value)}{unit}"
                : "--";
            TxtGpuFan.Text = snapshot.GpuFanRpm.HasValue
                ? $"{snapshot.GpuFanRpm.Value} {LocalizationService.Get("Main_RpmUnit")}"
                : "--";
            ApplyTelemetryVisibility(s);

            // SSD Rows
            RenderSsdRows(snapshot.StorageDrives, isFahrenheit, unit);

            // Draw Charts
            DrawCpuChart();
            DrawGpuChart();

            if (CpuCoresPopup.IsOpen)
            {
                UpdateCpuCoreItems(snapshot);
            }
        }

        private void RenderSsdRows(List<DriveTelemetry> drives, bool isFahrenheit, string unit)
        {
            string signature = string.Join("\n", drives.Select(drive => $"{drive.Index}|{drive.Model}"));
            if (!string.Equals(signature, _ssdRowSignature, StringComparison.Ordinal))
            {
                BuildSsdRows(drives);
                _ssdRowSignature = signature;
            }

            for (int i = 0; i < _ssdRows.Count && i < drives.Count; i++)
            {
                UpdateSsdRow(_ssdRows[i], drives[i], isFahrenheit, unit);
            }
        }

        private void BuildSsdRows(List<DriveTelemetry> drives)
        {
            SsdRowsContainer.Children.Clear();
            _ssdRows.Clear();

            if (drives.Count == 0)
            {
                var empty = new Grid { Height = 38 };
                empty.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Get("Main_StorageUnavailable"),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8A8A")),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                SsdRowsContainer.Children.Add(empty);
                return;
            }

            foreach (var drive in drives)
            {
                SsdRowView row = BuildSsdRow(drive);
                _ssdRows.Add(row);
                SsdRowsContainer.Children.Add(row.Root);
            }

            SsdRowsContainer.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
                Margin = new Thickness(0, 1, 0, 1)
            });
        }

        private SsdRowView BuildSsdRow(DriveTelemetry drive)
        {
            var grid = new Grid { Height = 38 };
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var iconHost = new Grid
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 10, 0),
                FlowDirection = System.Windows.FlowDirection.LeftToRight
            };

            iconHost.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3,5 L23,5 L23,20 L3,20 Z M18,5 L18,20"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5D5D5")),
                StrokeThickness = 1.2,
                Stretch = Stretch.Uniform,
                Width = 20,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            iconHost.Children.Add(new TextBlock
            {
                Text = drive.Index.ToString(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5D5D5")),
                FontSize = 10.5,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });

            var iconLines = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            };
            for (int line = 0; line < 3; line++)
            {
                iconLines.Children.Add(new Rectangle
                {
                    Width = 1,
                    Height = 4,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6A6A6A")),
                    Margin = new Thickness(0, line == 1 ? 1 : 0, 0, line == 1 ? 0 : 1)
                });
            }
            iconHost.Children.Add(iconLines);
            leftStack.Children.Add(iconHost);
            leftStack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("Main_SsdLabel"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5D5D5")),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            grid.Children.Add(leftStack);

            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = System.Windows.FlowDirection.LeftToRight
            };

            var valBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#303030")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 3, 12, 3),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var txtVal = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8D8D8")),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            valBorder.Child = txtVal;
            rightStack.Children.Add(valBorder);

            var unitBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A4A4A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3, 7, 3)
            };

            var txtUnit = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A9A9A")),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            unitBorder.Child = txtUnit;
            rightStack.Children.Add(unitBorder);
            grid.Children.Add(rightStack);

            var tooltipTitle = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            };
            var tooltipHealth = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8B8B8")),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var tooltipContent = new StackPanel();
            tooltipContent.Children.Add(tooltipTitle);
            tooltipContent.Children.Add(tooltipHealth);

            var tooltip = new ToolTip
            {
                Style = (Style)FindResource("SsdToolTipStyle"),
                Content = tooltipContent,
                Placement = PlacementMode.Top,
                VerticalOffset = -6
            };
            ToolTipService.SetInitialShowDelay(valBorder, 250);
            ToolTipService.SetShowDuration(valBorder, 8000);
            valBorder.ToolTip = tooltip;

            return new SsdRowView
            {
                Root = grid,
                TempBorder = valBorder,
                TempText = txtVal,
                UnitText = txtUnit,
                TooltipTitle = tooltipTitle,
                TooltipHealth = tooltipHealth
            };
        }

        private static void UpdateSsdRow(SsdRowView row, DriveTelemetry drive, bool isFahrenheit, string unit)
        {
            bool hasTemperature = !double.IsNaN(drive.Temperature);
            double displayTemp = hasTemperature
                ? (isFahrenheit ? (drive.Temperature * 9 / 5 + 32) : drive.Temperature)
                : 0;

            row.TempText.Text = hasTemperature ? $"{Math.Round(displayTemp)}" : "--";
            row.UnitText.Text = unit;

            string model = string.IsNullOrWhiteSpace(drive.Model) ? "SSD" : drive.Model.Trim();
            row.TooltipTitle.Text = hasTemperature
                ? LocalizationService.Format("Main_SsdModelTemp", model, Math.Round(displayTemp), unit)
                : model;
            row.TooltipHealth.Text = drive.SmartHealthPercent.HasValue
                ? LocalizationService.Format(
                    "Main_SsdHealth",
                    drive.SmartHealthPercent.Value,
                    LocalizationService.Health(drive.StatusText))
                : LocalizationService.Get("Main_SsdHealthUnknown");

            AutomationProperties.SetHelpText(row.TempBorder,
                $"{row.TooltipTitle.Text}. {row.TooltipHealth.Text}");
        }

        private void DrawCpuChart()
        {
            CpuChartCanvas.Children.Clear();
            CpuChartHoverCanvas.Children.Clear();
            double w = CpuChartCanvas.ActualWidth;
            double h = CpuChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double plotH = Math.Max(10, h - 26);

            // Horizontal grid lines
            for (int i = 1; i <= 3; i++)
            {
                double y = (plotH / 4.0) * i;
                var line = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A")),
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
                    Y2 = plotH,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C")),
                    StrokeThickness = 1
                };
                CpuChartCanvas.Children.Add(vline);
            }

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(_timeframeSamples);
            _cpuChartSamples = samples;
            if (samples.Count < 2)
            {
                CpuChartTooltip.IsOpen = false;
                return;
            }

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
                DrawCpuSegment(samples, start, segEnd, mode, minT, maxT, stepX, plotH, h);
                start = end + 1;
            }

            if (_cpuHoverActive)
            {
                UpdateCpuChartTooltip(_cpuHoverX);
            }
            else
            {
                CpuChartTooltip.IsOpen = false;
            }
        }

        private void DrawCpuSegment(List<TelemetrySample> samples, int start, int end, bool isCooling,
                                    double minT, double maxT, double stepX, double h, double fillBottom)
        {
            var points = new List<Point>(end - start + 1);
            for (int i = start; i <= end; i++)
            {
                double t = samples[i].CpuTemp;
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 16) + 8);
                double x = i * stepX;
                points.Add(new Point(x, y));
            }

            if (points.Count < 2)
            {
                return;
            }

            Color lineColor = isCooling
                ? (Color)ColorConverter.ConvertFromString("#3B9BCB")
                : (Color)ColorConverter.ConvertFromString("#C77950");
            Color areaTop = isCooling
                ? (Color)ColorConverter.ConvertFromString("#1A5A7E")
                : (Color)ColorConverter.ConvertFromString("#6A3320");

            var areaBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            areaBrush.GradientStops.Add(new GradientStop(
                Color.FromArgb(160, areaTop.R, areaTop.G, areaTop.B), 0));
            areaBrush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0, areaTop.R, areaTop.G, areaTop.B), 1));

            var area = new System.Windows.Shapes.Path
            {
                Data = CreateSmoothAreaGeometry(points, fillBottom),
                Fill = areaBrush
            };
            CpuChartCanvas.Children.Add(area);

            Geometry lineGeometry = CreateSmoothLineGeometry(points);
            var glow = new System.Windows.Shapes.Path
            {
                Data = lineGeometry,
                Stroke = new SolidColorBrush(Color.FromArgb(42, lineColor.R, lineColor.G, lineColor.B)),
                StrokeThickness = 3.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            CpuChartCanvas.Children.Add(glow);

            var line = new System.Windows.Shapes.Path
            {
                Data = lineGeometry,
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            CpuChartCanvas.Children.Add(line);
        }

        private static Geometry CreateSmoothLineGeometry(IReadOnlyList<Point> points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                AddSmoothCurve(context, points);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Geometry CreateSmoothAreaGeometry(IReadOnlyList<Point> points, double baseline)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var first = points[0];
                var last = points[points.Count - 1];
                context.BeginFigure(new Point(first.X, baseline), true, true);
                context.LineTo(first, false, false);
                AddSmoothCurve(context, points);
                context.LineTo(new Point(last.X, baseline), false, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static void AddSmoothCurve(StreamGeometryContext context, IReadOnlyList<Point> points)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i + 2 < points.Count ? points[i + 2] : p2;

                var cp1 = new Point(
                    p1.X + (p2.X - p0.X) / 6.0,
                    p1.Y + (p2.Y - p0.Y) / 6.0);
                var cp2 = new Point(
                    p2.X - (p3.X - p1.X) / 6.0,
                    p2.Y - (p3.Y - p1.Y) / 6.0);

                context.BezierTo(cp1, cp2, p2, true, false);
            }
        }

        private void DrawGpuChart()
        {
            GpuChartCanvas.Children.Clear();
            GpuChartHoverCanvas.Children.Clear();
            double w = GpuChartCanvas.ActualWidth;
            double h = GpuChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(_timeframeSamples);
            _gpuChartSamples = samples;
            if (samples.Count < 2)
            {
                GpuChartTooltip.IsOpen = false;
                return;
            }

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

            for (int i = 1; i <= 3; i++)
            {
                double x = (w / 4.0) * i;
                GpuChartCanvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = h,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#343434")),
                    StrokeThickness = 1
                });
            }

            GpuChartCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = h / 2,
                Y2 = h / 2,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#343434")),
                StrokeThickness = 1
            });

            var line = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7CB342")),
                StrokeThickness = 1.5
            };
            GpuChartCanvas.Children.Add(line);

            if (_gpuHoverActive)
            {
                UpdateGpuChartTooltip(_gpuHoverX);
            }
            else
            {
                GpuChartTooltip.IsOpen = false;
            }
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

            // The initial selection fires while XAML is still creating the GPU chart.
            if (CpuChartCanvas == null || CpuChartHoverCanvas == null ||
                GpuChartCanvas == null || GpuChartHoverCanvas == null)
            {
                return;
            }

            DrawCpuChart();
            DrawGpuChart();
        }

        private void CpuChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = true;
            _cpuHoverX = e.GetPosition(CpuChartCanvas).X;
            UpdateCpuChartTooltip(_cpuHoverX);
        }

        private void CpuChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = false;
            CpuChartTooltip.IsOpen = false;
            CpuChartHoverCanvas.Children.Clear();
        }

        private void GpuChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _gpuHoverActive = true;
            _gpuHoverX = e.GetPosition(GpuChartCanvas).X;
            UpdateGpuChartTooltip(_gpuHoverX);
        }

        private void GpuChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _gpuHoverActive = false;
            GpuChartTooltip.IsOpen = false;
            GpuChartHoverCanvas.Children.Clear();
        }

        private void UpdateCpuChartTooltip(double hoverX)
        {
            if (_cpuChartSamples.Count < 2)
            {
                return;
            }

            double width = CpuChartCanvas.ActualWidth;
            double height = CpuChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double plotH = Math.Max(10, height - 26);
            int count = _cpuChartSamples.Count;
            double stepX = width / Math.Max(1, count - 1);
            int index = Math.Clamp((int)Math.Round(hoverX / stepX), 0, count - 1);
            var sample = _cpuChartSamples[index];
            double x = index * stepX;
            double temperature = sample.CpuTemp;
            double normalized = Math.Clamp((temperature - 25) / (90 - 25), 0, 1);
            double y = plotH - (normalized * (plotH - 16) + 8);

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double displayTemp = isFahrenheit ? (temperature * 9 / 5) + 32 : temperature;
            string unit = isFahrenheit ? "°F" : "°C";

            TxtCpuChartTooltipValue.Text = LocalizationService.Format(
                "Main_CpuChartValue", Math.Round(displayTemp), unit);
            TxtCpuChartTooltipTime.Text = sample.Timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);

            Color accent = sample.CoolingModeActive
                ? (Color)ColorConverter.ConvertFromString("#4FC3F7")
                : (Color)ColorConverter.ConvertFromString("#E5987E");

            DrawChartHoverMarker(CpuChartHoverCanvas, x, y, plotH, accent);
            ShowChartTooltip(CpuChartTooltip, CpuChartCanvas, x, y, width);
        }

        private void UpdateGpuChartTooltip(double hoverX)
        {
            if (_gpuChartSamples.Count < 2)
            {
                return;
            }

            double width = GpuChartCanvas.ActualWidth;
            double height = GpuChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int count = _gpuChartSamples.Count;
            double stepX = width / Math.Max(1, count - 1);
            int index = Math.Clamp((int)Math.Round(hoverX / stepX), 0, count - 1);
            var sample = _gpuChartSamples[index];
            double x = index * stepX;
            double temperature = sample.GpuTemp;
            double normalized = Math.Clamp((temperature - 30) / (90 - 30), 0, 1);
            double y = height - (normalized * (height - 8) + 4);

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double displayTemp = isFahrenheit ? (temperature * 9 / 5) + 32 : temperature;
            string unit = isFahrenheit ? "°F" : "°C";

            TxtGpuChartTooltipValue.Text = $"{Math.Round(displayTemp)}{unit}";
            TxtGpuChartTooltipTime.Text = sample.Timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);

            DrawChartHoverMarker(
                GpuChartHoverCanvas,
                x,
                y,
                height,
                (Color)ColorConverter.ConvertFromString("#7CB342"));
            ShowChartTooltip(GpuChartTooltip, GpuChartCanvas, x, y, width);
        }

        private static void DrawChartHoverMarker(Canvas overlay, double x, double y, double height, Color accent)
        {
            overlay.Children.Clear();

            overlay.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = height,
                Stroke = new SolidColorBrush(Color.FromRgb(165, 165, 165)),
                StrokeThickness = 1,
                Opacity = 0.55
            });

            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 2
            };
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, y - 3.5);
            overlay.Children.Add(dot);
        }

        private static void ShowChartTooltip(Popup popup, Canvas target, double x, double y, double chartWidth)
        {
            popup.PlacementTarget = target;
            popup.Placement = PlacementMode.Relative;

            if (popup.Child is FrameworkElement child)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double popupWidth = child.DesiredSize.Width;
                double popupHeight = child.DesiredSize.Height;
                double left = Math.Clamp(x - (popupWidth / 2), 4, Math.Max(4, chartWidth - popupWidth - 4));

                popup.HorizontalOffset = left;
                popup.VerticalOffset = y - popupHeight - 8;
            }

            popup.IsOpen = true;
        }

        private void HideChartTooltips()
        {
            _cpuHoverActive = false;
            _gpuHoverActive = false;
            CpuChartTooltip.IsOpen = false;
            GpuChartTooltip.IsOpen = false;
            CpuChartHoverCanvas.Children.Clear();
            GpuChartHoverCanvas.Children.Clear();
        }

        private void ToggleSwitch_Click(object sender, MouseButtonEventArgs e)
        {
            PowerPlanService.Instance.ToggleCoolingMode();
        }

        private void BtnStressTest_Click(object sender, RoutedEventArgs e)
        {
            ToggleStressTestDialog();
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
                Header = LocalizationService.Get("Main_TooltipStressTest"),
                Foreground = Brushes.White
            };
            itemStress.Click += (s, args) => OpenStressTestDialog();

            var itemAbout = new MenuItem
            {
                Header = LocalizationService.Get("Main_AboutTitle"),
                Foreground = Brushes.White
            };
            itemAbout.Click += (s, args) =>
            {
                MessageBox.Show(
                    LocalizationService.Get("Main_AboutBody"),
                    LocalizationService.Get("Main_AboutTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };

            menu.Items.Add(itemStress);
            menu.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C")) });
            menu.Items.Add(itemAbout);

            menu.PlacementTarget = BtnToolsMenu;
            menu.IsOpen = true;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ToggleSettingsDialog();
        }

        private void BtnSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            ToggleSystemInfoDialog();
        }

        public void OpenStressTestDialog()
        {
            if (_stressTestWindow != null)
            {
                _stressTestWindow.Activate();
                return;
            }

            var win = new StressTestWindow { Owner = this };
            DockSideBySide(win);
            _stressTestWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            UpdateTitleIconStates();
            win.Show();
            win.Activate();
        }

        private void ToggleStressTestDialog()
        {
            if (_stressTestWindow != null)
            {
                _stressTestWindow.Close();
                return;
            }

            OpenStressTestDialog();
        }

        public void OpenSettingsDialog()
        {
            OpenSettingsDialog(false);
        }

        public void OpenSettingsDialog(bool openAbout)
        {
            ShowSettingsWindow(openAbout, false);
        }

        public void OpenAutopilotSettingsDialog()
        {
            ShowSettingsWindow(false, true);
        }

        private void ToggleSettingsDialog()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                return;
            }

            ShowSettingsWindow(false, false);
        }

        private void ShowSettingsWindow(bool openAbout, bool openAutopilot)
        {
            if (_settingsWindow != null)
            {
                if (openAbout) _settingsWindow.SelectAboutTab();
                if (openAutopilot) _settingsWindow.SelectAutopilotTab();
                _settingsWindow.Activate();
                return;
            }

            var win = new SettingsWindow
            {
                Owner = this,
                OpenAboutOnLoad = openAbout,
                OpenAutopilotOnLoad = openAutopilot
            };
            DockSideBySide(win);
            _settingsWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            UpdateTitleIconStates();
            win.Show();
            win.Activate();
        }

        public void OpenSystemInfoDialog()
        {
            if (_systemInfoWindow != null)
            {
                _systemInfoWindow.Activate();
                return;
            }

            var win = new SystemInfoWindow { Owner = this };
            DockSideBySide(win);
            _systemInfoWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            UpdateTitleIconStates();
            win.Show();
            win.Activate();
        }

        private void ToggleSystemInfoDialog()
        {
            if (_systemInfoWindow != null)
            {
                _systemInfoWindow.Close();
                return;
            }

            OpenSystemInfoDialog();
        }

        private void UntrackChildWindow(Window window)
        {
            if (ReferenceEquals(window, _settingsWindow)) _settingsWindow = null;
            if (ReferenceEquals(window, _systemInfoWindow)) _systemInfoWindow = null;
            if (ReferenceEquals(window, _stressTestWindow)) _stressTestWindow = null;

            UpdateChildDialogState();
            UpdateTitleIconStates();
        }

        private void UpdateChildDialogState()
        {
            _isChildDialogOpen = _settingsWindow != null ||
                _systemInfoWindow != null ||
                _stressTestWindow != null;
        }

        private void UpdateTitleIconStates()
        {
            bool settingsOpen = _settingsWindow != null;
            bool systemInfoOpen = _systemInfoWindow != null;
            bool stressTestOpen = _stressTestWindow != null;

            SetTitleIconActive(StressIconRing, Shape.StrokeProperty, stressTestOpen);
            SetTitleIconActive(StressIconBlades, Shape.FillProperty, stressTestOpen);
            SetTitleIconActive(StressIconHub, Shape.FillProperty, stressTestOpen);
            SetTitleIconActive(SettingsIconPath, Shape.FillProperty, settingsOpen);
            SetTitleIconActive(SystemInfoIconPath, Shape.FillProperty, systemInfoOpen);
        }

        private static void SetTitleIconActive(Shape icon, DependencyProperty property, bool active)
        {
            if (active)
            {
                icon.SetResourceReference(property, "WindowBorderBrush");
            }
            else
            {
                icon.SetValue(property, InactiveTitleIconBrush);
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
            if (System.Windows.Application.Current is App app)
            {
                app.ToggleOverlay();
            }
        }

        private void BtnAutopilot_Click(object sender, RoutedEventArgs e)
        {
            OpenAutopilotSettingsDialog();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void SetExitConfirmationOpen(bool isOpen)
        {
            _isExitConfirmationOpen = isOpen;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ShowKeyboardFocusCues = true;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ShowKeyboardFocusCues = false;
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
            UpdateCpuCoreItems(HardwareMonitorService.Instance.CurrentSnapshot);
            CpuCoresPopup.IsOpen = true;
        }

        private void CpuTempHoverZone_MouseLeave(object sender, MouseEventArgs e)
        {
            CpuCoresPopup.IsOpen = false;
        }

        private void UpdateCpuCoreItems(HardwareSnapshot snapshot)
        {
            TxtCpuPackagePowerPopup.Text = FormatPackagePower(snapshot.CpuPackageWatts);

            var cores = snapshot.CpuCoresTelemetry;
            if (cores.Count == 0)
            {
                return;
            }

            var s = SettingsService.Instance.Current;
            bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";

            CoreColumns = cores.Count <= 16 ? 4 : 6;

            CoreItems.Clear();
            foreach (var core in cores)
            {
                int displayTemp = isFahrenheit
                    ? (int)Math.Round((core.DisplayTemp * 9 / 5.0) + 32)
                    : core.DisplayTemp;

                CoreItems.Add(new CoreReadout
                {
                    Text = $"{core.Name} {displayTemp}{unit}",
                    IsHottest = core.IsHottest
                });
            }

            PositionCorePopup();
        }

        private static string FormatPackagePower(int? watts)
        {
            string value = watts.HasValue
                ? watts.Value.ToString(CultureInfo.InvariantCulture)
                : "--";
            return LocalizationService.Format("Main_PackagePower", value);
        }

        private static string CompactGpuName(string fullName)
        {
            if (fullName.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase))
            {
                return fullName["NVIDIA ".Length..].Trim();
            }

            if (fullName.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase))
            {
                return fullName["AMD ".Length..].Trim();
            }

            if (fullName.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase))
            {
                return fullName["Intel ".Length..].Trim();
            }

            return fullName;
        }

        private void ApplyTelemetryVisibility(AppSettings settings)
        {
            TxtCpuPackageWatts.Visibility = settings.ShowCpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;
            CpuPackagePowerFooter.Visibility = settings.ShowCpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtGpuTemp.Visibility = settings.ShowGpuTemp
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtGpuPower.Visibility = settings.ShowGpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtGpuName.Visibility = settings.ShowGpuName &&
                !string.IsNullOrWhiteSpace(TxtGpuName.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuHotspotGroup.Visibility = settings.ShowGpuHotspot
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuVramGroup.Visibility = settings.ShowGpuVram
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuFanGroup.Visibility = settings.ShowGpuFan
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuHealthRow.Visibility = settings.ShowGpuHotspot || settings.ShowGpuVram || settings.ShowGpuFan
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CpuCoresPopup_Opened(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionCorePopup));
        }

        private void PositionCorePopup()
        {
            if (!CpuCoresPopup.IsOpen)
            {
                return;
            }

            if (CpuCoresPopup.Child is not FrameworkElement popupRoot)
            {
                return;
            }

            popupRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double popupWidth = popupRoot.DesiredSize.Width;
            double popupHeight = popupRoot.DesiredSize.Height;
            if (popupWidth <= 0 || popupHeight <= 0)
            {
                return;
            }

            double desiredOffsetX = CpuTempHoverZone.ActualWidth - popupWidth;
            var placement = PlacementMode.Top;

            if (TryGetMonitorWorkArea(out Rect workArea))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                Point targetScreen = CpuTempHoverZone.PointToScreen(new Point(0, 0));
                double targetLeft = targetScreen.X / dpi.DpiScaleX;
                double targetTop = targetScreen.Y / dpi.DpiScaleY;

                double popupLeft = targetLeft + desiredOffsetX;
                double minLeft = workArea.Left + 4;
                double maxLeft = Math.Max(minLeft, workArea.Right - popupWidth - 4);
                popupLeft = Math.Clamp(popupLeft, minLeft, maxLeft);

                bool fitsAbove = targetTop - popupHeight - 6 >= workArea.Top + 4;
                if (!fitsAbove)
                {
                    placement = PlacementMode.Bottom;
                }

                CpuCoresPopup.Placement = placement;
                CpuCoresPopup.HorizontalOffset = popupLeft - targetLeft;
                CpuCoresPopup.VerticalOffset = 6;
                return;
            }

            CpuCoresPopup.Placement = PlacementMode.Top;
            CpuCoresPopup.HorizontalOffset = desiredOffsetX;
            CpuCoresPopup.VerticalOffset = 6;
        }

        private bool TryGetMonitorWorkArea(out Rect workArea)
        {
            workArea = Rect.Empty;

            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                var screen = WinForms.Screen.FromHandle(handle);
                var dpi = VisualTreeHelper.GetDpi(this);
                var bounds = screen.WorkingArea;

                workArea = new Rect(
                    bounds.Left / dpi.DpiScaleX,
                    bounds.Top / dpi.DpiScaleY,
                    bounds.Width / dpi.DpiScaleX,
                    bounds.Height / dpi.DpiScaleY);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            _ssdRowSignature = null;
            ApplySettings();
            UpdateTheme(PowerPlanService.Instance.IsCoolingModeActive, false);
            UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
