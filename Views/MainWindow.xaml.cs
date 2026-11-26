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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;
using CryoTray.Tray;
using Microsoft.Win32;
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
        private bool _isApplyingSettings;
        private ReleaseInfo? _visibleUpdate;
        private DispatcherTimer? _updateSuccessTimer;
        private DispatcherTimer? _updateSnoozeTimer;
        private readonly double _expandedHeight;
        private const double BaselineSsdSectionHeight = 79;

        private List<TelemetrySample> _cpuChartSamples = new();
        private List<TelemetrySample> _gpuChartSamples = new();
        private List<SessionChartEvent> _cpuChartEvents = new();
        private List<SessionChartEvent> _gpuChartEvents = new();
        private double _gpuChartMinimum = 30;
        private double _gpuChartMaximum = 90;
        private string _gpuChartMetric = "Temperature";
        private string? _gpuChartGpuIdentifier;
        private bool _cpuHoverActive;
        private bool _cpuHoverSmall;
        private bool _gpuHoverActive;
        private bool _chartsDirty;
        private double _cpuHoverX;
        private double _gpuHoverX;
        private IDisposable? _telemetryLease;
        private DispatcherTimer? _chartRenderTimer;
        private SsdRowView? _activeHoverDriveRow;
        private string _profileDurationKey = "";
        private DateTime _profileStartedUtc;
        private string _profileBaseText = "";
        private string _profileDurationText = "";
        private int _lastProfileDurationMinutes = -1;
        private string _lastActiveUtilitySignature = "";
        private string _activeUtilityPrimaryKind = "";

        private readonly List<SsdRowView> _ssdRows = new();
        private string? _ssdRowSignature;
        private DispatcherTimer? _ramFeedbackTimer;
        private DispatcherTimer? _copyFeedbackTimer;
        private bool _isCopyFeedbackActive;
        private bool _ramTooltipPinned;

        private static Brush SsdHealthGoodBrush => ThemeService.GetBrush("SuccessTextBrush");
        private static Brush SsdHealthFairBrush => ThemeService.GetBrush("WarningTextBrush");
        private static Brush SsdHealthWarnBrush => ThemeService.GetBrush("DangerTextBrush");
        private static Brush RamFooterMutedBrush => ThemeService.GetBrush("RamFooterMutedBrush");
        private static Brush RamFooterTextBrush => ThemeService.GetBrush("RamFooterTextBrush");
        private static Brush RamAccentBrush => ThemeService.GetBrush("RamAccentBrush");
        private static Brush RamWarnBrush => ThemeService.GetBrush("WarningTextBrush");

        private static Brush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private sealed class SsdRowView
        {
            public required string DriveKey { get; init; }
            public required bool ShowGraph { get; init; }
            public required Grid Root { get; init; }
            public required TextBlock BadgeText { get; init; }
            public required TextBlock RowTitleText { get; init; }
            public required TextBlock HealthText { get; init; }
            public required TextBlock TooltipTitle { get; init; }
            public required TextBlock TooltipHealth { get; init; }
            public TextBlock? TooltipFreeSpace { get; init; }
            public Border? TempBorder { get; init; }
            public TextBlock? TempText { get; init; }
            public TextBlock? UnitText { get; init; }
            public Canvas? ChartCanvas { get; init; }
            public Canvas? ChartHoverCanvas { get; init; }
            public TextBlock? ChartUnavailableText { get; init; }
            public List<TelemetrySample> ChartSamples { get; } = new();
            public bool HoverActive { get; set; }
            public double HoverX { get; set; }
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
            TextScaleService.Attach(this, scaleHeight: false);
            _expandedHeight = Height;
            LocalizationService.Instance.ApplyWindow(this);
            DataContext = this;
            CpuCoresPopup.Opened += CpuCoresPopup_Opened;
            RamBreakdownToolTip.PlacementTarget = BtnRamBreakdown;
            SetupFanAnimation();

            _chartRenderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _chartRenderTimer.Tick += ChartRenderTimer_Tick;

            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            AutopilotService.Instance.StateChanged += OnAutopilotStateChanged;
            UpdateCpuGhzTooltip(HardwareMonitorService.Instance.CurrentSnapshot);
            UpdateGpuOverviewTooltip(HardwareMonitorService.Instance.CurrentSnapshot);
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
            if (!_isClosing)
            {
                MemoryTrimService.Instance.ScheduleTrim(TimeSpan.FromSeconds(2));
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            (System.Windows.Application.Current as App)?.OnDashboardVisibilityChanged(IsVisible);
            UpdateDashboardDemand();

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
            UpdateDashboardDemand();
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

        private void UpdateDashboardDemand()
        {
            bool active = IsVisible && WindowState != WindowState.Minimized;
            if (active)
            {
                if (_telemetryLease == null)
                {
                    _telemetryLease = TelemetryDemandTracker.Instance.Acquire(
                        "MainWindow", TelemetryDemand.Dashboard);
                }

                _chartRenderTimer?.Start();
                return;
            }

            _telemetryLease?.Dispose();
            _telemetryLease = null;
            _chartsDirty = false;
            _chartRenderTimer?.Stop();
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
            if (System.Windows.Application.Current is App app)
            {
                app.UpdateAvailabilityChanged += RefreshUpdateBanner;
            }
            UpdateStatusService.Instance.Changed += OnUpdateStatusChanged;
            RefreshUpdateBanner();
            RefreshSuccessBanner();
            if (System.Windows.Application.Current is App startupApp &&
                startupApp.TryConsumeStartupHealthCheck(out bool postUpdate, out bool firstRun))
            {
                Dispatcher.BeginInvoke(
                    new Action(() => ShowHealthCheckDialog(postUpdate, firstRun)),
                    DispatcherPriority.Background);
            }
            SettingsService.Instance.SettingsChanged += ApplySettings;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            ThemeService.ThemeChanged += OnThemeChanged;

            PowerPlanService.Instance.CoolingModeChanged += OnCoolingModeChanged;
            PowerPlanService.Instance.ProfileChanged += OnProfileChanged;
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
            KeepAwakeService.Instance.StatusChanged += OnUtilityTimerChanged;
            DownloadTimerService.Instance.StatusChanged += OnDownloadTimerChanged;
            if (System.Windows.Application.Current is App utilityApp)
            {
                utilityApp.CoolingPauseChanged += OnUtilityTimerChanged;
            }
            MemoryOptimizerService.Instance.OptimizingStateChanged += OnRamOptimizingStateChanged;
            MemoryOptimizerService.Instance.OptimizationCompleted += OnRamOptimizationCompleted;

            UpdateDashboardDemand();
            UpdateTheme(PowerPlanService.Instance.ActiveProfile, false);
            UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
            DrawCpuChart();
            DrawGpuChart();
            SetRamOptimizingVisualState(MemoryOptimizerService.Instance.IsOptimizing);
            StartFanAnimation();
            MemoryTrimService.Instance.ScheduleTrim(MemoryTrimService.LoadedDelay);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            HideChartTooltips();
            CloseRamBreakdownToolTip();
            if (RamScopeMenu != null)
            {
                RamScopeMenu.IsOpen = false;
            }
            if (ToolsContextMenu != null)
            {
                ToolsContextMenu.IsOpen = false;
            }
            if (PowerProfileMenu != null)
            {
                PowerProfileMenu.IsOpen = false;
            }
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
            if (System.Windows.Application.Current is App app)
            {
                app.UpdateAvailabilityChanged -= RefreshUpdateBanner;
            }
            UpdateStatusService.Instance.Changed -= OnUpdateStatusChanged;
            AutopilotService.Instance.StateChanged -= OnAutopilotStateChanged;
            _updateSnoozeTimer?.Stop();
            _updateSnoozeTimer = null;
            if (UpdateSuccessBanner.Visibility == Visibility.Visible)
            {
                HideUpdateSuccessBanner(consume: true);
            }
            else
            {
                _updateSuccessTimer?.Stop();
                _updateSuccessTimer = null;
            }
            SettingsService.Instance.SettingsChanged -= ApplySettings;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            PowerPlanService.Instance.CoolingModeChanged -= OnCoolingModeChanged;
            PowerPlanService.Instance.ProfileChanged -= OnProfileChanged;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            KeepAwakeService.Instance.StatusChanged -= OnUtilityTimerChanged;
            DownloadTimerService.Instance.StatusChanged -= OnDownloadTimerChanged;
            if (System.Windows.Application.Current is App pauseApp)
            {
                pauseApp.CoolingPauseChanged -= OnUtilityTimerChanged;
            }
            MemoryOptimizerService.Instance.OptimizingStateChanged -= OnRamOptimizingStateChanged;
            MemoryOptimizerService.Instance.OptimizationCompleted -= OnRamOptimizationCompleted;
            _ramFeedbackTimer?.Stop();
            _ramFeedbackTimer = null;
            _copyFeedbackTimer?.Stop();
            _copyFeedbackTimer = null;
            _chartRenderTimer?.Stop();
            _telemetryLease?.Dispose();
            _telemetryLease = null;
            _activeHoverDriveRow = null;
            _ssdRows.Clear();
            _cpuChartSamples.Clear();
            _gpuChartSamples.Clear();
            _cpuChartEvents.Clear();
            _gpuChartEvents.Clear();
            CpuChartCanvas?.Children.Clear();
            CpuChartHoverCanvas?.Children.Clear();
            GpuChartCanvas?.Children.Clear();
            GpuChartHoverCanvas?.Children.Clear();
            SsdRowsContainer?.Children.Clear();
            OptGlyphHost?.BeginAnimation(OpacityProperty, null);
            StopFanAnimation();
            DataContext = null;
            Content = null;
        }

        public void ApplySettings()
        {
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                ApplySettingsCore();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isClosing)
                {
                    ApplySettingsCore();
                }
            }));
        }

        private void ApplySettingsCore()
        {
            var s = SettingsService.Instance.Current;
            _isApplyingSettings = true;
            UpdateActiveProfileLabel();
            RefreshPowerProfileMenuChecks();
            try
            {
                TxtAppTitle.Text = s.ShowComputerName
                    ? LocalizationService.Format("Main_AppWithPc", Environment.MachineName)
                    : LocalizationService.Get("App_Title");

                TxtCpuModel.TextAlignment = LocalizationService.Instance.IsArabic
                    ? TextAlignment.Right
                    : TextAlignment.Left;

                Topmost = s.AlwaysOnTop;
                ShowInTaskbar = s.PinToTaskbar;

                _timeframeSamples = TimeframeMinutesToSamples(s.CpuGraphTimeframeMinutes);
                int targetIdx = TimeframeMinutesToIndex(s.CpuGraphTimeframeMinutes);
                if (CmbTimeframe != null && CmbTimeframe.SelectedIndex != targetIdx)
                {
                    CmbTimeframe.SelectedIndex = targetIdx;
                }

                ApplyTelemetryVisibility(s);
                _ssdRowSignature = null;

                RamRow.Visibility = s.ShowRamRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                RamSeparator.Visibility = RamRow.Visibility;
                OverlayRow.Visibility = s.ShowOverlayRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                AutopilotRow.Visibility = s.ShowAutopilotRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                RefreshAutopilotStatus();
                AutopilotSeparator.Visibility = s.ShowOverlayRow && s.ShowAutopilotRow
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
                string unit = isFahrenheit ? "°F" : "°C";
                RenderSsdRows(HardwareMonitorService.Instance.CurrentSnapshot.StorageDrives, isFahrenheit, unit);
                RefreshActiveUtilityBar(updateHeight: false);
                UpdateWindowHeight(s.ShowOverlayRow, s.ShowAutopilotRow);
                DrawCpuChart();
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        private void OnAutopilotStateChanged(AutopilotState state)
        {
            if (Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(RefreshAutopilotStatus));
        }

        private void RefreshAutopilotStatus()
        {
            if (TxtAutopilotStatus == null)
            {
                return;
            }

            AutopilotState state = AutopilotService.Instance.CurrentState;
            if (!state.IsActive)
            {
                TxtAutopilotStatus.Text = "";
                TxtAutopilotStatus.ToolTip = null;
                return;
            }

            var resolvedProfile = PowerProfiles.Resolve(state.ProfileKey, SettingsService.Instance.Current);
            string profileName = resolvedProfile != null
                ? LocalizationService.GetProfileName(resolvedProfile)
                : LocalizationService.Get("Common_Unknown");
            TxtAutopilotStatus.Text = LocalizationService.Format(
                "Main_AutopilotGameMode", profileName);
            TxtAutopilotStatus.ToolTip = string.IsNullOrWhiteSpace(state.ProcessName)
                ? LocalizationService.Get("Main_AutopilotFullscreen")
                : state.ProcessName;
        }

        private void UpdateWindowHeight(bool showOverlay, bool showAutopilot)
        {
            var s = SettingsService.Instance.Current;
            double height = _expandedHeight;

            if (!s.ShowCpuGraph)
            {
                height -= 80;
            }
            else if (s.CpuGraphSmall)
            {
                height -= 80;
                height += CpuSmallChartRow.Height
                    + CpuSmallChartSeparator.Height
                    + CpuSmallChartSeparator.Margin.Top
                    + CpuSmallChartSeparator.Margin.Bottom;
            }

            if (CpuSpeedRow.Visibility != Visibility.Visible)
            {
                height -= CpuSpeedRow.Height
                    + CpuSpeedSeparator.Height
                    + CpuSpeedSeparator.Margin.Top
                    + CpuSpeedSeparator.Margin.Bottom;
            }

            if (RamRow.Visibility != Visibility.Visible)
            {
                height -= RamRow.Height
                    + RamSeparator.Height
                    + RamSeparator.Margin.Top
                    + RamSeparator.Margin.Bottom;
            }
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
            if (s.ShowGpuDetails)
            {
                height += GpuDetailsRow.Height;
            }

            double actualSsdHeight;
            if (!s.ShowDiskTemp || SsdRowsContainer.Visibility != Visibility.Visible || SsdRowsContainer.Children.Count == 0)
            {
                actualSsdHeight = 0;
            }
            else if (_ssdRows.Count == 0)
            {
                actualSsdHeight = 38;
            }
            else
            {
                actualSsdHeight = _ssdRows.Sum(r => r.Root.Height) + 3;
            }
            height += actualSsdHeight - BaselineSsdSectionHeight;

            if (PawnIoBanner.Visibility == Visibility.Visible)
            {
                height += PawnIoBanner.Height;
            }
            if (UpdateBanner.Visibility == Visibility.Visible)
            {
                height += UpdateBanner.Height;
            }
            if (UpdateSuccessBanner.Visibility == Visibility.Visible)
            {
                height += UpdateSuccessBanner.Height;
            }
            if (ActiveUtilityBar != null && ActiveUtilityBar.Visibility == Visibility.Visible)
            {
                height += ActiveUtilityBar.Height + ActiveUtilityBar.Margin.Top + ActiveUtilityBar.Margin.Bottom;
            }

            height *= TextScaleService.Factor;

            Rect monitorWorkArea = WindowPlacementHelper.GetMonitorWorkAreaDips(this);
            double maxWorkHeight = Math.Max(260, monitorWorkArea.Height - 24);
            height = Math.Clamp(height, 260, maxWorkHeight);

            if (Math.Abs(Height - height) < 0.1)
            {
                return;
            }

            Height = height;
            if (s.AnchorToTray)
            {
                Top = monitorWorkArea.Bottom - Height - 12;
            }
        }

        public void PositionAboveTray()
        {
            var s = SettingsService.Instance.Current;
            if (!s.AnchorToTray && s.WindowLeft.HasValue && s.WindowTop.HasValue)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                Rect savedMonitor = WindowPlacementHelper.GetWorkAreaForSavedPositionDips(
                    s.WindowLeft.Value,
                    s.WindowTop.Value,
                    Width,
                    Height,
                    dpi.DpiScaleX,
                    dpi.DpiScaleY);
                var (clampedLeft, clampedTop) = WindowPlacementHelper.ClampToWorkArea(
                    savedMonitor,
                    s.WindowLeft.Value,
                    s.WindowTop.Value,
                    Width,
                    Height,
                    margin: 8);
                Left = clampedLeft;
                Top = clampedTop;
                return;
            }

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 12;
        }

        private SettingsWindow? _settingsWindow;
        private SystemInfoWindow? _systemInfoWindow;
        private StorageCleanupWindow? _storageCleanupWindow;
        private StressTestWindow? _stressTestWindow;
        private HealthCheckWindow? _healthCheckWindow;
        private SessionReportWindow? _sessionReportWindow;
        private bool _isChildDialogOpen;

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_isChildDialogOpen || _isClosing || _isExitConfirmationOpen) return;

            var s = SettingsService.Instance.Current;
            if (!s.HideOnFocusLoss || s.AlwaysOnTop) return;

            HideChartTooltips();
            CloseRamBreakdownToolTip();
            if (RamScopeMenu != null)
            {
                RamScopeMenu.IsOpen = false;
            }
            if (ToolsContextMenu != null)
            {
                ToolsContextMenu.IsOpen = false;
            }
            if (PowerProfileMenu != null)
            {
                PowerProfileMenu.IsOpen = false;
            }
            Close();
        }

        private void OnCoolingModeChanged(bool isCooling)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTheme(PowerPlanService.Instance.ActiveProfile, true);
                UpdateCpuGhzTooltip(HardwareMonitorService.Instance.CurrentSnapshot);
            });
        }

        private void OnProfileChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateTheme(PowerPlanService.Instance.ActiveProfile, true);
                UpdateActiveProfileLabel();
                UpdateCpuGhzTooltip(HardwareMonitorService.Instance.CurrentSnapshot);
            }));
        }

        private void UpdateActiveProfileLabel()
        {
            var profile = PowerPlanService.Instance.ActiveProfile;
            if (TxtActivePowerProfile == null)
            {
                return;
            }

            string key = profile?.Key ?? "";
            DateTime serviceStartedUtc = PowerPlanService.Instance.ActiveProfileStartedUtc;
            bool planChanged = !string.Equals(_profileDurationKey, key, StringComparison.Ordinal);
            if (planChanged)
            {
                if (_isCopyFeedbackActive)
                {
                    _copyFeedbackTimer?.Stop();
                    _isCopyFeedbackActive = false;
                }
                _profileDurationKey = key;
                _profileStartedUtc = serviceStartedUtc != default ? serviceStartedUtc : DateTime.UtcNow;
                _profileDurationText = "";
                _lastProfileDurationMinutes = -1;
            }
            else if (serviceStartedUtc != default && serviceStartedUtc != _profileStartedUtc)
            {
                _profileStartedUtc = serviceStartedUtc;
                _profileDurationText = "";
                _lastProfileDurationMinutes = -1;
            }

            _profileBaseText = profile == null
                ? ""
                : $"{LocalizationService.GetProfileName(profile)} · {LocalizationService.GetProfileBadge(profile)}";
            _lastProfileDurationMinutes = -1;
            RefreshProfileDurationText();
            TxtActivePowerProfile.ToolTip = profile == null
                ? null
                : LocalizationService.GetProfileDescription(profile);
        }

        /// <summary>
        /// Appends how long the current profile has been active. Formatting runs once per
        /// minute, not per telemetry tick, and no timer or extra poll is involved.
        /// </summary>
        private void RefreshProfileDurationText()
        {
            if (_isCopyFeedbackActive ||
                TxtActivePowerProfile == null ||
                _profileStartedUtc == default ||
                string.IsNullOrEmpty(_profileDurationKey))
            {
                return;
            }

            int minutes = (int)(DateTime.UtcNow - _profileStartedUtc).TotalMinutes;
            if (minutes == _lastProfileDurationMinutes)
            {
                return;
            }

            _lastProfileDurationMinutes = minutes;
            _profileDurationText = minutes <= 0
                ? ""
                : minutes < 60
                    ? LocalizationService.Format("Main_ProfileDurationMinutes", minutes)
                    : LocalizationService.Format("Main_ProfileDurationHours", minutes / 60, minutes % 60);

            TxtActivePowerProfile.Text = _profileDurationText.Length == 0
                ? _profileBaseText
                : $"{_profileBaseText} · {_profileDurationText}";
        }

        private void BtnPowerProfileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            PowerProfileMenu.PlacementTarget = BtnPowerProfileMenu;
            PowerProfileMenu.Placement = PlacementMode.Bottom;
            PowerProfileMenu.HorizontalOffset = 0;
            PowerProfileMenu.VerticalOffset = 2;
            PowerProfileMenu.FlowDirection = FlowDirection;
            PowerProfileMenu.IsOpen = true;
        }

        private void PowerProfileMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            PowerProfileMenu.FlowDirection = FlowDirection;
            BuildPowerProfileMenu();
        }

        private void BuildPowerProfileMenu()
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            PowerProfileMenu.Items.Clear();

            var settings = SettingsService.Instance.Current;
            string coolingKey = PowerProfiles.GetCooling(settings.PreferredCoolingProfile, settings).Key;
            string gamingKey = PowerProfiles.GetGaming(settings.PreferredGamingProfile, settings).Key;

            int rowIndex = 0;
            AddPowerProfileGroup("Main_ProfileMenu_Cooling", PowerProfiles.GetVisibleCoolingProfiles(settings), coolingKey, ref rowIndex);
            AddPowerProfileMenuSeparator();
            AddPowerProfileGroup("Main_ProfileMenu_CoolingOff", PowerProfiles.GetVisibleGamingProfiles(settings), gamingKey, ref rowIndex);
        }

        private void AddPowerProfileGroup(
            string headerKey,
            IReadOnlyList<PowerProfileDefinition> profiles,
            string selectedKey,
            ref int rowIndex)
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            string headerText = LocalizationService.Get(headerKey);
            var header = new TextBlock
            {
                Text = headerText,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 1, 0, 0)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var headerItem = new MenuItem
            {
                Header = header,
                IsEnabled = false,
                Focusable = false,
                Padding = new Thickness(10, 7, 10, 4)
            };
            AutomationProperties.SetName(headerItem, headerText);
            PowerProfileMenu.Items.Add(headerItem);

            foreach (var profile in profiles)
            {
                var item = new MenuItem
                {
                    Header = CreatePowerProfileMenuHeader(profile),
                    Tag = profile.Key,
                    IsCheckable = true,
                    IsChecked = string.Equals(profile.Key, selectedKey, StringComparison.OrdinalIgnoreCase),
                    Style = TryFindResource("PowerProfileMenuItemStyle") as Style
                };
                if (rowIndex % 2 == 1)
                {
                    item.SetResourceReference(Control.BackgroundProperty, "AlternateRowBrush");
                }
                AutomationProperties.SetName(
                    item,
                    $"{LocalizationService.GetProfileName(profile)} · {LocalizationService.GetProfileBadge(profile)}");
                AutomationProperties.SetHelpText(item, LocalizationService.GetProfileDescription(profile));
                item.Click += PowerProfileMenuItem_Click;
                PowerProfileMenu.Items.Add(item);
                rowIndex++;
            }
        }

        private void AddPowerProfileMenuSeparator()
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            var separator = new Separator();
            if (TryFindResource("DarkMenuSeparatorStyle") is Style separatorStyle)
            {
                separator.Style = separatorStyle;
            }
            PowerProfileMenu.Items.Add(separator);
        }

        private static Grid CreatePowerProfileMenuHeader(PowerProfileDefinition profile)
        {
            var header = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = CreateFrozenBrush(profile.Palette.Accent),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            header.Children.Add(dot);

            var name = new TextBlock
            {
                Text = LocalizationService.GetProfileName(profile),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(name, 1);
            header.Children.Add(name);

            var badge = new TextBlock
            {
                Text = LocalizationService.GetProfileBadge(profile),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);

            return header;
        }

        private void PowerProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string key })
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            var profile = PowerProfiles.Resolve(key, settings);
            if (profile == null)
            {
                return;
            }

            if (profile.IsCooling)
            {
                settings.PreferredCoolingProfile = profile.Key;
            }
            else
            {
                settings.PreferredGamingProfile = profile.Key;
            }

            SettingsService.Instance.Save();
            PowerPlanService.Instance.RefreshActiveProfile();
            RefreshPowerProfileMenuChecks();
        }

        private void RefreshPowerProfileMenuChecks()
        {
            if (PowerProfileMenu == null)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            string coolingKey = PowerProfiles.GetCooling(settings.PreferredCoolingProfile, settings).Key;
            string gamingKey = PowerProfiles.GetGaming(settings.PreferredGamingProfile, settings).Key;

            foreach (var item in PowerProfileMenu.Items.OfType<MenuItem>())
            {
                if (item.Tag is not string key)
                {
                    continue;
                }

                var profile = PowerProfiles.Resolve(key, settings);
                if (profile == null)
                {
                    continue;
                }

                item.IsChecked = string.Equals(
                    key,
                    profile.IsCooling ? coolingKey : gamingKey,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static bool ShouldUseProfileCoolingToggleColors(PowerProfileDefinition? profile, bool isHighContrast) =>
            !isHighContrast &&
            profile is { IsCooling: true } &&
            !string.Equals(profile.Key, PowerProfiles.CoolingKey, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(profile.Key, PowerProfiles.LegacyKey, StringComparison.OrdinalIgnoreCase);

        private void UpdateTheme(PowerProfileDefinition? profile, bool animate)
        {
            bool isCooling = profile?.IsCooling == true;
            ProfilePalette palette = profile?.Palette ?? PowerProfiles.GamingHighProfile.Palette;

            Color target1 = ParseColor(palette.BannerTop);
            Color target2 = ParseColor(palette.BannerBottom);
            if (ThemeService.IsHighContrast)
            {
                target1 = SystemColors.WindowColor;
                target2 = SystemColors.WindowColor;
            }

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

            ApplyFanTheme(palette, isCooling);

            if (isCooling)
            {
                // Cooling and Legacy keep the exact original blue ON treatment; Eco uses its own palette.
                bool useProfileToggle = ShouldUseProfileCoolingToggleColors(profile, ThemeService.IsHighContrast);
                TogglePillBorder.Width = LocalizationService.Instance.IsArabic ? 68 : 48;
                TogglePillBorder.Background = useProfileToggle
                    ? new SolidColorBrush(ParseColor(palette.ToggleTrack))
                    : ThemeService.GetBrush(ThemeService.IsHighContrast ? "AccentBrush" : "AccentButtonBrush");
                TxtToggleState.Text = LocalizationService.Get("Common_On");
                TxtToggleState.Foreground = useProfileToggle
                    ? new SolidColorBrush(ParseColor(palette.ToggleText))
                    : ThemeService.GetBrush("AccentButtonTextBrush");
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Left;
                TxtToggleState.Margin = new Thickness(7, 0, 0, 0);

                ToggleThumb.Fill = useProfileToggle
                    ? new SolidColorBrush(ParseColor(palette.ToggleThumb))
                    : ThemeService.GetBrush("AccentButtonTextBrush");
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleThumb.Margin = new Thickness(0, 0, 0, 0);
            }
            else if (profile != null)
            {
                // Gaming profiles carry their own tier accent on the OFF toggle.
                TogglePillBorder.Width = LocalizationService.Instance.IsArabic ? 68 : 48;
                TogglePillBorder.Background = new SolidColorBrush(ParseColor(palette.ToggleTrack));
                TxtToggleState.Text = LocalizationService.Get("Common_Off");
                TxtToggleState.Foreground = new SolidColorBrush(ParseColor(palette.ToggleText));
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Right;
                TxtToggleState.Margin = new Thickness(0, 0, 7, 0);

                ToggleThumb.Fill = new SolidColorBrush(ParseColor(palette.ToggleThumb));
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleThumb.Margin = new Thickness(0, 0, 0, 0);
            }
            else
            {
                // No CryoTray profile is active; keep the original neutral OFF state.
                TogglePillBorder.Width = LocalizationService.Instance.IsArabic ? 68 : 48;
                TogglePillBorder.Background = ThemeService.GetBrush("ToggleOffTrackBrush");
                TxtToggleState.Text = LocalizationService.Get("Common_Off");
                TxtToggleState.Foreground = ThemeService.GetBrush("ToggleOffTextBrush");
                TxtToggleState.HorizontalAlignment = HorizontalAlignment.Right;
                TxtToggleState.Margin = new Thickness(0, 0, 7, 0);

                ToggleThumb.Fill = ThemeService.GetBrush("ToggleOffThumbBrush");
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleThumb.Margin = new Thickness(0, 0, 0, 0);
            }

            if (ThemeService.IsHighContrast && !isCooling)
            {
                TogglePillBorder.Background = ThemeService.GetBrush("WindowBackgroundBrush");
                TxtToggleState.Foreground = ThemeService.GetBrush("TextPrimaryBrush");
                ToggleThumb.Fill = ThemeService.GetBrush("TextPrimaryBrush");
            }

            DrawCpuChart();
        }

        private static Color ParseColor(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);

        private void ApplyFanTheme(ProfilePalette palette, bool isCooling)
        {
            Color accent = ParseColor(palette.FanAccent);
            Color ring = ParseColor(palette.FanRing);
            Color circuits = ParseColor(palette.FanCircuit);
            Color dots = ParseColor(palette.FanDots);
            Color tileTop = ParseColor(palette.FanTileTop);
            Color tileMiddle = ParseColor(palette.FanTileMiddle);
            Color tileBottom = ParseColor(palette.FanTileBottom);
            Color petalTop = ParseColor(palette.PetalTop);
            Color petalMiddle = ParseColor(palette.PetalMiddle);
            Color petalBottom = ParseColor(palette.PetalBottom);
            Color petalOutline = ParseColor(palette.PetalOutline);
            Color petalVein = ParseColor(palette.PetalVein);

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

            PawnIoBanner.Visibility = snapshot.PawnIoInstalled
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Hero Card
            TxtCpuModel.Text = string.IsNullOrWhiteSpace(snapshot.CpuName)
                ? LocalizationService.Get("Common_NotReported")
                : snapshot.CpuName;
            TxtCpuCores.Text = snapshot.CpuCores > 0
                ? LocalizationService.Format("Main_CpuProcessor", snapshot.CpuCores)
                : string.Empty;
            if (double.IsFinite(snapshot.CpuTemp) && snapshot.CpuTemp > 0)
            {
                double cpuT = isFahrenheit ? (snapshot.CpuTemp * 9 / 5 + 32) : snapshot.CpuTemp;
                TxtHeroTemp.Text = $"{Math.Round(cpuT)}";
            }
            else
            {
                TxtHeroTemp.Text = "--";
            }
            TxtHeroUnit.Text = unit;

            // CPU Speed & Small CPU label
            TxtCpuSmallLabel.Text = FormatCpuSmallRowLabel(snapshot.CpuName, snapshot.CpuVendor);
            TxtCpuClockRatio.Text = FormatCpuClockReadout(
                snapshot.CpuGhz, snapshot.CpuBaseGhz, snapshot.CpuMaxGhz, s.ShowCpuBaseSpeed);
            UpdateCpuGhzTooltip(snapshot);
            TxtCpuLoad.Text = double.IsFinite(snapshot.CpuLoad)
                ? $"{Math.Round(snapshot.CpuLoad)}%"
                : "--%";
            RefreshProfileDurationText();
            TxtCpuPackageWatts.Text = snapshot.CpuPackageWatts.HasValue
                ? $"{snapshot.CpuPackageWatts.Value}W"
                : "--W";
            TxtCpuPackagePowerPopup.Text = FormatPackagePower(snapshot.CpuPackageWatts);

            // RAM Row
            if (double.IsFinite(snapshot.RamUsedGb) &&
                double.IsFinite(snapshot.RamTotalGb) &&
                snapshot.RamTotalGb > 0)
            {
                TxtRamRatio.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.0} / {1:0.0}",
                    snapshot.RamUsedGb,
                    snapshot.RamTotalGb);
                TxtRamPercent.Text = double.IsFinite(snapshot.RamUsagePercent)
                    ? $"{Math.Round(snapshot.RamUsagePercent)}%"
                    : "--%";
            }
            else
            {
                TxtRamRatio.Text = "-- / --";
                TxtRamPercent.Text = "--%";
            }
            UpdateMemoryBreakdownUi(snapshot);

            // GPU Row
            TxtGpuLabel.Text = LocalizationService.Get("Main_GpuLabel");
            if (double.IsFinite(snapshot.GpuTemp) && snapshot.GpuTemp > 0)
            {
                double gpuT = isFahrenheit ? (snapshot.GpuTemp * 9 / 5 + 32) : snapshot.GpuTemp;
                TxtGpuTemp.Text = $"{Math.Round(gpuT)}{unit}";
            }
            else
            {
                TxtGpuTemp.Text = $"--{unit}";
            }
            TxtGpuPower.Text = snapshot.GpuPowerWatts.HasValue
                ? $"{snapshot.GpuPowerWatts.Value}W"
                : "--W";
            TxtGpuLoad.Text = double.IsFinite(snapshot.GpuLoad)
                ? $"{Math.Round(snapshot.GpuLoad)}%"
                : "--%";
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
            TxtGpuVramUsage.Text = FormatGpuVramUsage(snapshot);
            TxtGpuClocks.Text = FormatGpuClocks(snapshot);
            TxtGpuThrottle.Text = CompactThrottleReason(snapshot.GpuThrottleReason);
            TxtGpuThrottle.ToolTip = string.IsNullOrWhiteSpace(snapshot.GpuThrottleReason)
                ? null
                : snapshot.GpuThrottleReason;
            UpdateGpuOverviewTooltip(snapshot);
            ApplyTelemetryVisibility(s);

            // SSD Rows (rendered before height update so height reflects visible & graphed drives)
            RenderSsdRows(snapshot.StorageDrives, isFahrenheit, unit);
            RefreshActiveUtilityBar(updateHeight: false);
            UpdateWindowHeight(s.ShowOverlayRow, s.ShowAutopilotRow);

            InvalidateCharts();

            if (CpuCoresPopup.IsOpen)
            {
                UpdateCpuCoreItems(snapshot);
            }
        }

        private void InvalidateCharts()
        {
            _chartsDirty = true;
        }

        private void ChartRenderTimer_Tick(object? sender, EventArgs e)
        {
            if (!_chartsDirty || !IsVisible || WindowState == WindowState.Minimized)
            {
                return;
            }

            _chartsDirty = false;
            DrawCpuChart();
            DrawGpuChart();
        }

        private static string FormatGpuVramUsage(HardwareSnapshot snapshot)
        {
            if (!snapshot.GpuVramUsedMb.HasValue ||
                !snapshot.GpuVramTotalMb.HasValue ||
                !double.IsFinite(snapshot.GpuVramUsedMb.Value) ||
                !double.IsFinite(snapshot.GpuVramTotalMb.Value) ||
                snapshot.GpuVramTotalMb.Value <= 0)
            {
                return "--";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.0} / {1:0.0} GB",
                snapshot.GpuVramUsedMb.Value / 1024.0,
                snapshot.GpuVramTotalMb.Value / 1024.0);
        }

        internal static string FormatGpuClocks(HardwareSnapshot snapshot)
        {
            if (!snapshot.GpuCoreClockMhz.HasValue && !snapshot.GpuMemoryClockMhz.HasValue)
            {
                return "--";
            }

            string core = snapshot.GpuCoreClockMhz.HasValue
                ? snapshot.GpuCoreClockMhz.Value.ToString("0", CultureInfo.InvariantCulture)
                : "--";
            string memory = snapshot.GpuMemoryClockMhz.HasValue
                ? snapshot.GpuMemoryClockMhz.Value.ToString("0", CultureInfo.InvariantCulture)
                : "--";
            return $"{core}/{memory} MHz";
        }

        internal static string CompactThrottleReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "--";
            }

            string compact = reason.Trim();
            int separator = compact.LastIndexOf('-');
            if (separator >= 0 && separator + 1 < compact.Length)
            {
                compact = compact[(separator + 1)..].Trim();
            }

            return compact.Length > 18 ? compact[..18] : compact;
        }

        internal static string FormatCpuClockReadout(double cpuGhz, double cpuBaseGhz, double cpuMaxGhz, bool showBaseSpeed)
        {
            string clockNow = double.IsFinite(cpuGhz) && cpuGhz > 0
                ? cpuGhz.ToString("0.00", CultureInfo.InvariantCulture)
                : "--";
            if (!showBaseSpeed)
            {
                return clockNow;
            }

            double baseVal = double.IsFinite(cpuBaseGhz) && cpuBaseGhz > 0 ? cpuBaseGhz : cpuMaxGhz;
            string clockBase = double.IsFinite(baseVal) && baseVal > 0
                ? baseVal.ToString("0.00", CultureInfo.InvariantCulture)
                : "--";
            return $"{clockNow} / {clockBase}";
        }

        private void UpdateCpuGhzTooltip(HardwareSnapshot? snapshot)
        {
            if (TxtCpuGhzTooltipTitle == null || TxtCpuGhzTooltipDetail == null ||
                BdrCpuGhzUnit == null || CpuGhzToolTip == null)
            {
                return;
            }

            double baseGhz = snapshot != null && double.IsFinite(snapshot.CpuBaseGhz) && snapshot.CpuBaseGhz > 0
                ? snapshot.CpuBaseGhz
                : (snapshot != null && double.IsFinite(snapshot.CpuMaxGhz) && snapshot.CpuMaxGhz > 0
                    ? snapshot.CpuMaxGhz
                    : double.NaN);
            double boostPeakGhz = snapshot != null ? snapshot.CpuBoostPeakGhz : double.NaN;

            CpuPlanPowerLimits limits = PowerPlanService.Instance.GetActiveCpuPowerLimits();
            var (title, detail) = FormatCpuPlanSpeedTooltip(limits, baseGhz, boostPeakGhz);

            TxtCpuGhzTooltipTitle.Text = title;
            TxtCpuGhzTooltipDetail.Text = detail;
            CpuGhzToolTip.FlowDirection = LocalizationService.Instance.IsArabic
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight;
            AutomationProperties.SetHelpText(BdrCpuGhzUnit, $"{title}. {detail}");
        }

        private void UpdateGpuOverviewTooltip(HardwareSnapshot? snapshot)
        {
            if (TxtGpuOverviewTitle == null || TxtGpuOverviewDetail == null ||
                GpuOverviewPanel == null || GpuOverviewToolTip == null)
            {
                return;
            }

            var (title, detail) = FormatGpuOverviewTooltip(snapshot);
            TxtGpuOverviewTitle.Text = title;
            TxtGpuOverviewDetail.Text = detail;
            GpuOverviewToolTip.FlowDirection = LocalizationService.Instance.IsArabic
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight;
            AutomationProperties.SetHelpText(GpuOverviewPanel, $"{title}. {detail}");
        }

        internal static (string Title, string Detail) FormatGpuOverviewTooltip(HardwareSnapshot? snapshot)
        {
            string fullName = snapshot?.GpuName?.Trim() ?? "";
            string title = string.IsNullOrWhiteSpace(fullName)
                ? LocalizationService.Get("Main_GpuLabel")
                : fullName;

            if (snapshot == null)
            {
                return (title, LocalizationService.Get("Main_GpuDetailsUnavailable"));
            }

            var parts = new List<string>(3);
            string vram = FormatGpuVramUsage(snapshot);
            if (!string.Equals(vram, "--", StringComparison.Ordinal))
            {
                parts.Add($"{LocalizationService.Get("Main_GpuVramUsageLabel")}: {vram}");
            }

            string clocks = FormatGpuClocks(snapshot);
            if (!string.Equals(clocks, "--", StringComparison.Ordinal))
            {
                parts.Add($"{LocalizationService.Get("Main_GpuClocksLabel")}: {clocks}");
            }

            string throttle = CompactThrottleReason(snapshot.GpuThrottleReason);
            if (!string.Equals(throttle, "--", StringComparison.Ordinal))
            {
                parts.Add($"{LocalizationService.Get("Main_GpuThrottleLabel")}: {throttle}");
            }

            string detail = parts.Count > 0
                ? string.Join(" · ", parts)
                : LocalizationService.Get("Main_GpuDetailsUnavailable");
            return (title, detail);
        }

        private void OnUtilityTimerChanged()
        {
            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                RefreshActiveUtilityBar(updateHeight: true);
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isClosing)
                {
                    RefreshActiveUtilityBar(updateHeight: true);
                }
            }));
        }

        private void OnDownloadTimerChanged(DownloadTimerStatus _)
        {
            OnUtilityTimerChanged();
        }

        private void RefreshActiveUtilityBar(bool updateHeight)
        {
            if (ActiveUtilityBar == null || TxtActiveUtilityStatus == null)
            {
                return;
            }

            bool keepActive = KeepAwakeService.Instance.IsActive;
            bool downloadActive = DownloadTimerService.Instance.IsArmed;
            DateTime? pausedUntil = (System.Windows.Application.Current as App)?.CoolingPausedUntilUtc;
            TimeSpan? pauseRemaining = pausedUntil.HasValue && pausedUntil.Value > DateTime.UtcNow
                ? pausedUntil.Value - DateTime.UtcNow
                : null;

            void CollapseUtilityBar()
            {
                if (ActiveUtilityBar.Visibility == Visibility.Collapsed && _lastActiveUtilitySignature.Length == 0)
                {
                    return;
                }

                _lastActiveUtilitySignature = "";
                _activeUtilityPrimaryKind = "";
                TxtActiveUtilityStatus.Text = "";
                if (BtnStopActiveUtility != null)
                {
                    string fallback = LocalizationService.Get("Main_ActiveTimer_StopAutomation");
                    BtnStopActiveUtility.ToolTip = fallback;
                    AutomationProperties.SetName(BtnStopActiveUtility, fallback);
                }
                ActiveUtilityBar.Visibility = Visibility.Collapsed;
                if (updateHeight)
                {
                    var settings = SettingsService.Instance.Current;
                    UpdateWindowHeight(settings.ShowOverlayRow, settings.ShowAutopilotRow);
                }
            }

            if (!keepActive && !downloadActive && !pauseRemaining.HasValue)
            {
                CollapseUtilityBar();
                return;
            }

            KeepAwakeStatus? keepStatus = keepActive ? KeepAwakeService.Instance.GetStatus() : null;
            DownloadTimerStatus? downloadStatus = downloadActive ? DownloadTimerService.Instance.CurrentStatus : null;
            var (text, primaryKind, anyActive) = FormatActiveUtilityDisplay(keepStatus, downloadStatus, pauseRemaining);
            if (!anyActive)
            {
                CollapseUtilityBar();
                return;
            }

            _activeUtilityPrimaryKind = primaryKind;
            string signature = $"{primaryKind}|{text}";
            bool visibilityChanged = ActiveUtilityBar.Visibility != Visibility.Visible;
            if (!visibilityChanged && string.Equals(_lastActiveUtilitySignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            _lastActiveUtilitySignature = signature;
            TxtActiveUtilityStatus.Text = text;
            TxtActiveUtilityStatus.ToolTip = text;
            if (BtnStopActiveUtility != null)
            {
                string stopTip = ResolveActiveUtilityStopTooltip(primaryKind);
                BtnStopActiveUtility.ToolTip = stopTip;
                AutomationProperties.SetName(BtnStopActiveUtility, stopTip);
            }
            if (visibilityChanged)
            {
                ActiveUtilityBar.Visibility = Visibility.Visible;
                if (updateHeight)
                {
                    var settings = SettingsService.Instance.Current;
                    UpdateWindowHeight(settings.ShowOverlayRow, settings.ShowAutopilotRow);
                }
            }
        }

        internal static (string Text, string PrimaryKind, bool AnyActive) FormatActiveUtilityDisplay(
            KeepAwakeStatus? keepStatus,
            DownloadTimerStatus? downloadStatus,
            TimeSpan? pauseRemaining)
        {
            var segments = new List<string>(3);
            string primaryKind = "";

            if (downloadStatus != null && downloadStatus.Phase != DownloadTimerPhase.Disarmed)
            {
                primaryKind = "download";
                segments.Add(FormatDownloadTimerSummary(downloadStatus));
            }

            if (keepStatus != null && keepStatus.IsActive)
            {
                if (primaryKind.Length == 0)
                {
                    primaryKind = "keepawake";
                }

                segments.Add(keepStatus.Mode == KeepAwakeMode.Timed
                    ? LocalizationService.Format(
                        "Main_ActiveTimer_KeepAwake",
                        KeepAwakeService.FormatRemaining(keepStatus.Remaining ?? TimeSpan.Zero))
                    : LocalizationService.Get("Main_ActiveTimer_KeepAwakeOn"));
            }

            if (pauseRemaining.HasValue && pauseRemaining.Value > TimeSpan.Zero)
            {
                if (primaryKind.Length == 0)
                {
                    primaryKind = "pause";
                }

                int mins = Math.Max(1, (int)Math.Ceiling(pauseRemaining.Value.TotalMinutes));
                segments.Add(LocalizationService.Format("Main_ActiveTimer_CoolingPause", mins));
            }

            return segments.Count == 0
                ? ("", "", false)
                : (string.Join(" · ", segments), primaryKind, true);
        }

        internal static string ResolveActiveUtilityStopTooltip(string primaryKind) => primaryKind switch
        {
            "download" => LocalizationService.Get("Main_ActiveTimer_StopDownload"),
            "keepawake" => LocalizationService.Get("Tray_KeepAwakeStop"),
            "pause" => LocalizationService.Get("Tray_ResumeCoolingNow"),
            _ => LocalizationService.Get("Main_ActiveTimer_StopAutomation")
        };

        private static string FormatDownloadTimerSummary(DownloadTimerStatus status)
        {
            switch (status.Phase)
            {
                case DownloadTimerPhase.Warning:
                    string actionName = status.Action switch
                    {
                        DownloadPowerAction.Hibernate => LocalizationService.Get("Settings_DownloadTimerHibernate"),
                        DownloadPowerAction.ShutDown => LocalizationService.Get("Settings_DownloadTimerShutDown"),
                        _ => LocalizationService.Get("Settings_DownloadTimerSleep")
                    };
                    return LocalizationService.Format(
                        "Main_ActiveTimer_DownloadWarning",
                        actionName,
                        Math.Max(0, status.WarningSecondsRemaining));
                case DownloadTimerPhase.WaitingForDownload:
                    return LocalizationService.Get("Main_ActiveTimer_DownloadWaiting");
                case DownloadTimerPhase.WaitingForDisk:
                    return LocalizationService.Get("Main_ActiveTimer_DownloadDisk");
                default:
                    TimeSpan remaining = status.QuietDuration > status.QuietElapsed
                        ? status.QuietDuration - status.QuietElapsed
                        : TimeSpan.Zero;
                    int mins = (int)remaining.TotalMinutes;
                    int secs = remaining.Seconds;
                    return LocalizationService.Format(
                        "Main_ActiveTimer_DownloadQuiet",
                        $"{mins}:{secs:00}");
            }
        }

        private void BtnStopActiveUtility_Click(object sender, RoutedEventArgs e)
        {
            switch (_activeUtilityPrimaryKind)
            {
                case "download":
                    DownloadTimerService.Instance.CancelAndDisarm();
                    break;
                case "keepawake":
                    KeepAwakeService.Instance.Stop();
                    break;
                case "pause":
                    (System.Windows.Application.Current as App)?.CancelCoolingPause(resumeCooling: true, forceResume: true);
                    break;
            }

            RefreshActiveUtilityBar(updateHeight: true);
        }

        private static string GetBoostModeResourceKey(int boostMode) => boostMode switch
        {
            0 => "Main_BoostModeDisabled",
            1 => "Main_BoostModeEnabled",
            2 or 5 => "Main_BoostModeAggressive",
            3 => "Main_BoostModeEfficientEnabled",
            4 or 6 => "Main_BoostModeEfficientAggressive",
            _ => "Main_BoostModeEnabled"
        };

        internal static (string Title, string Detail) FormatCpuPlanSpeedTooltip(
            CpuPlanPowerLimits limits,
            double baseGhz,
            double boostPeakGhz)
        {
            string planName = string.IsNullOrWhiteSpace(limits.PlanName)
                ? LocalizationService.Get("Common_Unknown")
                : limits.PlanName.Trim();
            string powerSource = LocalizationService.Get(
                limits.IsOnBattery ? "Main_PowerSourceBattery" : "Main_PowerSourceAc");

            bool hasBase = double.IsFinite(baseGhz) && baseGhz > 0;
            bool hasBoostPeak = double.IsFinite(boostPeakGhz) &&
                boostPeakGhz > 0 &&
                (!hasBase || boostPeakGhz > baseGhz * 1.02);

            double? freqCapGhz = limits.MaxFrequencyMhz > 0
                ? Math.Round(limits.MaxFrequencyMhz / 1000.0, 2)
                : null;
            double? pctCapGhz = hasBase
                ? Math.Round(baseGhz * (Math.Clamp(limits.MaxThrottlePercent, 0, 100) / 100.0), 2)
                : null;

            bool boostAllowed = limits.BoostMode > 0 &&
                limits.MaxThrottlePercent >= 100 &&
                (!freqCapGhz.HasValue || !hasBase || freqCapGhz.Value > baseGhz * 1.01);

            if (boostAllowed)
            {
                double? upperGhz = null;
                if (freqCapGhz.HasValue && hasBoostPeak)
                {
                    upperGhz = Math.Min(freqCapGhz.Value, boostPeakGhz);
                }
                else if (freqCapGhz.HasValue)
                {
                    upperGhz = freqCapGhz.Value;
                }
                else if (hasBoostPeak)
                {
                    upperGhz = boostPeakGhz;
                }

                string title = upperGhz.HasValue
                    ? LocalizationService.Format(
                        "Main_CpuPlanBoostPeakTitle",
                        planName,
                        upperGhz.Value.ToString("0.00", CultureInfo.InvariantCulture))
                    : hasBase
                        ? LocalizationService.Format(
                            "Main_CpuPlanBoostAboveTitle",
                            planName,
                            baseGhz.ToString("0.00", CultureInfo.InvariantCulture))
                        : LocalizationService.Format("Main_CpuPlanBoostTitle", planName);

                string boostLabel = LocalizationService.Get(GetBoostModeResourceKey(limits.BoostMode));
                string detail = hasBase
                    ? LocalizationService.Format(
                        "Main_CpuPlanDetailBoost",
                        baseGhz.ToString("0.00", CultureInfo.InvariantCulture),
                        boostLabel,
                        limits.MinThrottlePercent,
                        limits.MaxThrottlePercent,
                        powerSource)
                    : LocalizationService.Format(
                        "Main_CpuPlanDetailFallback",
                        limits.MinThrottlePercent,
                        limits.MaxThrottlePercent,
                        boostLabel,
                        powerSource);

                return (title, detail);
            }

            double? cappedGhz = pctCapGhz.HasValue && freqCapGhz.HasValue
                ? Math.Min(pctCapGhz.Value, freqCapGhz.Value)
                : pctCapGhz ?? freqCapGhz;

            string capTitle = cappedGhz.HasValue
                ? LocalizationService.Format(
                    "Main_CpuPlanCapTitle",
                    planName,
                    cappedGhz.Value.ToString("0.00", CultureInfo.InvariantCulture))
                : LocalizationService.Format("Main_CpuPlanCapPercentTitle", planName, limits.MaxThrottlePercent);

            string capDetail;
            if (hasBase && freqCapGhz.HasValue && (!pctCapGhz.HasValue || freqCapGhz.Value <= pctCapGhz.Value))
            {
                capDetail = LocalizationService.Format(
                    "Main_CpuPlanDetailFreqCap",
                    baseGhz.ToString("0.00", CultureInfo.InvariantCulture),
                    freqCapGhz.Value.ToString("0.00", CultureInfo.InvariantCulture),
                    powerSource);
            }
            else if (hasBase && limits.BoostMode == 0)
            {
                capDetail = LocalizationService.Format(
                    "Main_CpuPlanDetailCapped",
                    baseGhz.ToString("0.00", CultureInfo.InvariantCulture),
                    limits.MaxThrottlePercent,
                    powerSource);
            }
            else if (hasBase)
            {
                capDetail = LocalizationService.Format(
                    "Main_CpuPlanDetailCappedPct",
                    baseGhz.ToString("0.00", CultureInfo.InvariantCulture),
                    limits.MinThrottlePercent,
                    limits.MaxThrottlePercent,
                    powerSource);
            }
            else
            {
                capDetail = LocalizationService.Format(
                    "Main_CpuPlanDetailFallback",
                    limits.MinThrottlePercent,
                    limits.MaxThrottlePercent,
                    LocalizationService.Get(GetBoostModeResourceKey(limits.BoostMode)),
                    powerSource);
            }

            return (capTitle, capDetail);
        }

        internal static string FormatCpuSmallRowLabel(string? cpuName, string? cpuVendor = null)
        {
            string vendor = !string.IsNullOrWhiteSpace(cpuVendor)
                ? cpuVendor.Trim()
                : HardwareMonitorService.ResolveCpuVendor(null, cpuName);

            return !string.IsNullOrWhiteSpace(vendor)
                ? LocalizationService.Format("Main_CpuVendorFormat", vendor)
                : LocalizationService.Get("Common_Cpu");
        }

        private void RenderSsdRows(List<DriveTelemetry> drives, bool isFahrenheit, string unit)
        {
            var s = SettingsService.Instance.Current;
            if (!s.ShowDiskTemp)
            {
                SsdChartTooltip.IsOpen = false;
                _activeHoverDriveRow = null;
                if (_ssdRowSignature != "HIDDEN")
                {
                    SsdRowsContainer.Children.Clear();
                    _ssdRows.Clear();
                    _ssdRowSignature = "HIDDEN";
                }
                return;
            }

            var excluded = s.ExcludedDrives;
            var graphDrives = s.DiskGraphDrives ?? new List<string>();
            var graphTimeframes = s.DiskGraphTimeframes ?? new Dictionary<string, int>();
            var visibleDrives = drives
                .Where(d => !HardwareMonitorService.IsDriveExcluded(d, excluded))
                .ToList();

            string signature = $"1|{drives.Count}|" + string.Join("\n", visibleDrives.Select(drive =>
            {
                string key = HardwareMonitorService.GetDriveExclusionKey(drive);
                bool showGraph = graphDrives.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                int tf = graphTimeframes.TryGetValue(key, out int mins) ? mins : 1;
                return $"{key}|{drive.Model}|{drive.DriveLetters}|{drive.IsSolidState}|{showGraph}|{tf}";
            }));

            if (!string.Equals(signature, _ssdRowSignature, StringComparison.Ordinal))
            {
                BuildSsdRows(visibleDrives, drives.Count, graphDrives);
                _ssdRowSignature = signature;
            }

            for (int i = 0; i < _ssdRows.Count && i < visibleDrives.Count; i++)
            {
                var row = _ssdRows[i];
                var drive = visibleDrives[i];
                int tfMinutes = graphTimeframes.TryGetValue(row.DriveKey, out int mins) ? mins : 1;
                UpdateSsdRow(row, drive, tfMinutes, isFahrenheit, unit);
            }
        }

        private static string[] ParseDriveLetterTokens(string? driveLetters)
        {
            if (string.IsNullOrWhiteSpace(driveLetters))
            {
                return Array.Empty<string>();
            }

            return driveLetters
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim().TrimEnd(':').ToUpperInvariant())
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string FormatSsdBadgeText(DriveTelemetry drive)
        {
            string[] tokens = ParseDriveLetterTokens(drive.DriveLetters);
            return tokens.Length > 0
                ? tokens[0]
                : (drive.Index + 1).ToString(CultureInfo.InvariantCulture);
        }

        internal static string FormatSsdRowLabel(DriveTelemetry drive)
        {
            bool isHdd = drive.IsSolidState == false;
            string[] tokens = ParseDriveLetterTokens(drive.DriveLetters);
            if (tokens.Length > 0)
            {
                string formatted = string.Join(", ", tokens.Select(t => $"{t}:"));
                return LocalizationService.Format(isHdd ? "Main_HddWithDrive" : "Main_SsdWithDrive", formatted);
            }

            return LocalizationService.Format(isHdd ? "Main_HddIndexed" : "Main_SsdIndexed", drive.Index + 1);
        }

        internal static string FormatSsdFreeSpaceText(DriveTelemetry drive)
        {
            if (!double.IsFinite(drive.FreeSpaceGb) ||
                !double.IsFinite(drive.MountedTotalSpaceGb) ||
                drive.FreeSpaceGb < 0 ||
                drive.MountedTotalSpaceGb <= 0)
            {
                return "";
            }

            int freePercent = Math.Clamp(
                (int)Math.Round(drive.FreeSpaceGb / drive.MountedTotalSpaceGb * 100.0),
                0,
                100);
            return LocalizationService.Format(
                "Main_SsdFreeSpace",
                drive.FreeSpaceGb.ToString("0.0", CultureInfo.InvariantCulture),
                drive.MountedTotalSpaceGb.ToString("0.0", CultureInfo.InvariantCulture),
                freePercent);
        }

        private void BuildSsdRows(List<DriveTelemetry> visibleDrives, int totalDetectedDrives, IReadOnlyCollection<string> graphDrives)
        {
            SsdChartTooltip.IsOpen = false;
            _activeHoverDriveRow = null;
            SsdRowsContainer.Children.Clear();
            _ssdRows.Clear();

            if (totalDetectedDrives == 0)
            {
                var empty = new Grid { Height = 38 };
                empty.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Get("Main_StorageUnavailable"),
                    Foreground = ThemeService.GetBrush("TextMutedBrush"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                SsdRowsContainer.Children.Add(empty);
                return;
            }

            if (visibleDrives.Count == 0)
            {
                return;
            }

            foreach (var drive in visibleDrives)
            {
                string key = HardwareMonitorService.GetDriveExclusionKey(drive);
                bool showGraph = graphDrives.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                SsdRowView row = BuildSsdRow(drive, key, showGraph);
                _ssdRows.Add(row);
                SsdRowsContainer.Children.Add(row.Root);
            }

            SsdRowsContainer.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = ThemeService.GetBrush("DividerBrush"),
                Margin = new Thickness(0, 1, 0, 1)
            });
        }

        private SsdRowView BuildSsdRow(DriveTelemetry drive, string driveKey, bool showGraph)
        {
            var grid = new Grid { Height = showGraph ? 46 : 38 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(leftStack, 0);

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
                Stroke = ThemeService.GetBrush("TextTitleBrush"),
                StrokeThickness = 1.2,
                Stretch = Stretch.Uniform,
                Width = 20,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var badgeText = new TextBlock
            {
                Text = FormatSsdBadgeText(drive),
                Foreground = ThemeService.GetBrush("TextTitleBrush"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            iconHost.Children.Add(badgeText);

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
                    Fill = ThemeService.GetBrush("TextTertiaryBrush"),
                    Margin = new Thickness(0, line == 1 ? 1 : 0, 0, line == 1 ? 0 : 1)
                });
            }
            iconHost.Children.Add(iconLines);
            leftStack.Children.Add(iconHost);

            var rowTitleText = new TextBlock
            {
                Text = FormatSsdRowLabel(drive),
                Foreground = ThemeService.GetBrush("TextTitleBrush"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = System.Windows.FlowDirection.LeftToRight
            };
            leftStack.Children.Add(rowTitleText);

            var healthText = new TextBlock
            {
                Foreground = SsdHealthGoodBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = System.Windows.FlowDirection.LeftToRight,
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            leftStack.Children.Add(healthText);
            grid.Children.Add(leftStack);

            var tooltipTitle = new TextBlock
            {
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            };
            var tooltipHealth = new TextBlock
            {
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var tooltipFreeSpace = new TextBlock
            {
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var tooltipContent = new StackPanel
            {
                FlowDirection = LocalizationService.Instance.IsArabic
                    ? System.Windows.FlowDirection.RightToLeft
                    : System.Windows.FlowDirection.LeftToRight
            };
            tooltipContent.Children.Add(tooltipTitle);
            tooltipContent.Children.Add(tooltipHealth);
            tooltipContent.Children.Add(tooltipFreeSpace);

            var tooltip = new ToolTip
            {
                Style = (Style)FindResource("SsdToolTipStyle"),
                Content = tooltipContent,
                Placement = PlacementMode.Top,
                VerticalOffset = -6
            };

            Border? valBorder = null;
            TextBlock? txtVal = null;
            TextBlock? txtUnit = null;
            Canvas? chartCanvas = null;
            Canvas? chartHoverCanvas = null;
            TextBlock? chartUnavailableText = null;

            SsdRowView row;
            if (showGraph)
            {
                ToolTipService.SetInitialShowDelay(leftStack, 250);
                ToolTipService.SetShowDuration(leftStack, 8000);
                leftStack.ToolTip = tooltip;

                var chartHostGrid = new Grid();
                chartCanvas = new Canvas
                {
                    FlowDirection = System.Windows.FlowDirection.LeftToRight,
                    Background = Brushes.Transparent,
                    ClipToBounds = true
                };
                chartHoverCanvas = new Canvas
                {
                    FlowDirection = System.Windows.FlowDirection.LeftToRight,
                    IsHitTestVisible = false,
                    ClipToBounds = true
                };
                chartUnavailableText = new TextBlock
                {
                    Text = "--",
                    Foreground = ThemeService.GetBrush("TextTertiaryBrush"),
                    FontSize = 10.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed
                };

                chartHostGrid.Children.Add(chartCanvas);
                chartHostGrid.Children.Add(chartHoverCanvas);
                chartHostGrid.Children.Add(chartUnavailableText);

                var chartBorder = new Border
                {
                    Width = 238,
                    Height = 36,
                    Background = ThemeService.GetBrush("InsetBackgroundBrush"),
                    BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    FlowDirection = System.Windows.FlowDirection.LeftToRight,
                    Child = chartHostGrid
                };
                Grid.SetColumn(chartBorder, 1);
                grid.Children.Add(chartBorder);

                row = new SsdRowView
                {
                    DriveKey = driveKey,
                    ShowGraph = true,
                    Root = grid,
                    BadgeText = badgeText,
                    RowTitleText = rowTitleText,
                    HealthText = healthText,
                    TooltipTitle = tooltipTitle,
                    TooltipHealth = tooltipHealth,
                    TooltipFreeSpace = tooltipFreeSpace,
                    ChartCanvas = chartCanvas,
                    ChartHoverCanvas = chartHoverCanvas,
                    ChartUnavailableText = chartUnavailableText
                };

                chartCanvas.SizeChanged += (_, _) =>
                {
                    var s = SettingsService.Instance.Current;
                    bool isF = s.TemperatureUnit == "Fahrenheit";
                    int tf = s.DiskGraphTimeframes != null && s.DiskGraphTimeframes.TryGetValue(row.DriveKey, out int m) ? m : 1;
                    DrawDriveChart(row, drive, tf, isF, isF ? "°F" : "°C");
                };
                chartCanvas.MouseMove += (_, e) =>
                {
                    if (_activeHoverDriveRow != null && !ReferenceEquals(_activeHoverDriveRow, row))
                    {
                        _activeHoverDriveRow.HoverActive = false;
                        _activeHoverDriveRow.ChartHoverCanvas?.Children.Clear();
                    }
                    _activeHoverDriveRow = row;
                    row.HoverActive = true;
                    row.HoverX = e.GetPosition(chartCanvas).X;
                    UpdateDriveChartTooltip(row);
                };
                chartCanvas.MouseLeave += (_, _) =>
                {
                    row.HoverActive = false;
                    chartHoverCanvas.Children.Clear();
                    if (ReferenceEquals(_activeHoverDriveRow, row))
                    {
                        _activeHoverDriveRow = null;
                        SsdChartTooltip.IsOpen = false;
                    }
                };
            }
            else
            {
                var rightStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    FlowDirection = System.Windows.FlowDirection.LeftToRight
                };
                Grid.SetColumn(rightStack, 1);

                valBorder = new Border
                {
                    Background = ThemeService.GetBrush("CardBackgroundBrush"),
                    BorderBrush = ThemeService.GetBrush("BorderSubtleBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(12, 3, 12, 3),
                    Margin = new Thickness(0, 0, 6, 0)
                };

                txtVal = new TextBlock
                {
                    Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                valBorder.Child = txtVal;
                rightStack.Children.Add(valBorder);

                var unitBorder = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = ThemeService.GetBrush("InputBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(7, 3, 7, 3)
                };

                txtUnit = new TextBlock
                {
                    Foreground = ThemeService.GetBrush("TextTertiaryBrush"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                unitBorder.Child = txtUnit;
                rightStack.Children.Add(unitBorder);
                grid.Children.Add(rightStack);

                ToolTipService.SetInitialShowDelay(valBorder, 250);
                ToolTipService.SetShowDuration(valBorder, 8000);
                valBorder.ToolTip = tooltip;

                row = new SsdRowView
                {
                    DriveKey = driveKey,
                    ShowGraph = false,
                    Root = grid,
                    BadgeText = badgeText,
                    RowTitleText = rowTitleText,
                    HealthText = healthText,
                    TooltipTitle = tooltipTitle,
                    TooltipHealth = tooltipHealth,
                    TooltipFreeSpace = tooltipFreeSpace,
                    TempBorder = valBorder,
                    TempText = txtVal,
                    UnitText = txtUnit
                };
            }

            return row;
        }

        private void UpdateSsdRow(SsdRowView row, DriveTelemetry drive, int timeframeMinutes, bool isFahrenheit, string unit)
        {
            row.BadgeText.Text = FormatSsdBadgeText(drive);
            row.RowTitleText.Text = FormatSsdRowLabel(drive);

            if (drive.SmartHealthPercent.HasValue)
            {
                int pct = drive.SmartHealthPercent.Value;
                row.HealthText.Text = $"{pct}%";
                row.HealthText.Foreground = pct >= 80
                    ? SsdHealthGoodBrush
                    : pct >= 50
                        ? SsdHealthFairBrush
                        : SsdHealthWarnBrush;
                row.HealthText.Visibility = Visibility.Visible;
            }
            else
            {
                row.HealthText.Visibility = Visibility.Collapsed;
            }

            bool hasTemperature = double.IsFinite(drive.Temperature) && drive.Temperature > 0;
            double displayTemp = hasTemperature
                ? (isFahrenheit ? (drive.Temperature * 9 / 5 + 32) : drive.Temperature)
                : 0;

            string model = !string.IsNullOrWhiteSpace(drive.Model)
                ? drive.Model.Trim()
                : drive.IsSolidState == false
                    ? "HDD"
                    : drive.IsSolidState == true
                        ? "SSD"
                        : LocalizationService.Format("Alert_DriveName", drive.Index + 1);
            row.TooltipTitle.Text = hasTemperature
                ? LocalizationService.Format("Main_SsdModelTemp", model, Math.Round(displayTemp), unit)
                : model;
            row.TooltipHealth.Text = drive.SmartHealthPercent.HasValue
                ? LocalizationService.Format(
                    "Main_SsdHealth",
                    drive.SmartHealthPercent.Value,
                    LocalizationService.Health(drive.StatusText))
                : LocalizationService.Get("Main_SsdHealthUnknown");
            string freeSpaceText = FormatSsdFreeSpaceText(drive);
            if (row.TooltipFreeSpace != null)
            {
                row.TooltipFreeSpace.Text = freeSpaceText;
                row.TooltipFreeSpace.Visibility = string.IsNullOrEmpty(freeSpaceText)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (row.ShowGraph)
            {
                DrawDriveChart(row, drive, timeframeMinutes, isFahrenheit, unit);
            }
            else if (row.TempBorder != null && row.TempText != null && row.UnitText != null)
            {
                row.TempText.Text = hasTemperature ? $"{Math.Round(displayTemp)}" : "--";
                row.UnitText.Text = unit;
                string helpText = string.IsNullOrEmpty(freeSpaceText)
                    ? $"{row.RowTitleText.Text} {row.TempText.Text}{row.UnitText.Text}. {row.TooltipTitle.Text}. {row.TooltipHealth.Text}"
                    : $"{row.RowTitleText.Text} {row.TempText.Text}{row.UnitText.Text}. {row.TooltipTitle.Text}. {row.TooltipHealth.Text}. {freeSpaceText}";
                AutomationProperties.SetHelpText(row.TempBorder, helpText);
            }
        }

        private void DrawDriveChart(SsdRowView row, DriveTelemetry drive, int timeframeMinutes, bool isFahrenheit, string unit)
        {
            if (row.ChartCanvas == null || row.ChartHoverCanvas == null || row.ChartUnavailableText == null)
            {
                return;
            }

            Canvas canvas = row.ChartCanvas;
            canvas.Children.Clear();
            row.ChartHoverCanvas.Children.Clear();
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            DrawInlineGridLines(canvas, w, h, horizontalBands: 4, verticalBands: 4, "#2C2C2C");

            int windowSamples = TimeframeMinutesToSamples(timeframeMinutes);
            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(windowSamples)
                .Where(sample => sample.TryGetDriveTemp(row.DriveKey, out _))
                .ToList();

            if (samples.Count == 1)
            {
                samples.Add(samples[0]);
            }
            else if (samples.Count == 0 && double.IsFinite(drive.Temperature) && drive.Temperature > 0)
            {
                var fallback = TelemetrySample.FromSnapshot(HardwareMonitorService.Instance.CurrentSnapshot);
                samples.Add(fallback);
                samples.Add(fallback);
            }

            row.ChartSamples.Clear();
            row.ChartSamples.AddRange(samples);

            if (samples.Count < 2)
            {
                row.ChartUnavailableText.Visibility = Visibility.Visible;
                if (ReferenceEquals(_activeHoverDriveRow, row))
                {
                    SsdChartTooltip.IsOpen = false;
                }
                return;
            }

            row.ChartUnavailableText.Visibility = Visibility.Collapsed;

            const double minT = 20;
            const double maxT = 80;
            double stepX = w / Math.Max(1, samples.Count - 1);
            var points = new List<Point>(samples.Count);

            for (int i = 0; i < samples.Count; i++)
            {
                samples[i].TryGetDriveTemp(row.DriveKey, out double t);
                double norm = Math.Clamp((t - minT) / (maxT - minT), 0, 1);
                double y = h - (norm * (h - 8) + 4);
                points.Add(new Point(i * stepX, y));
            }

            var areaBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            areaBrush.GradientStops.Add(new GradientStop(Color.FromArgb(110, 176, 176, 176), 0));
            areaBrush.GradientStops.Add(new GradientStop(Color.FromArgb(14, 176, 176, 176), 1));

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = CreateSmoothAreaGeometry(points, h),
                Fill = areaBrush
            });

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = CreateSmoothLineGeometry(points),
                Stroke = ThemeService.GetBrush("ChartLineBrush"),
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            });

            if (row.HoverActive && ReferenceEquals(_activeHoverDriveRow, row))
            {
                UpdateDriveChartTooltip(row);
            }
        }

        private void UpdateDriveChartTooltip(SsdRowView row)
        {
            if (row.ChartCanvas == null || row.ChartHoverCanvas == null || row.ChartSamples.Count < 2)
            {
                return;
            }

            double width = row.ChartCanvas.ActualWidth;
            double height = row.ChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int count = row.ChartSamples.Count;
            double stepX = width / Math.Max(1, count - 1);
            int index = Math.Clamp((int)Math.Round(row.HoverX / stepX), 0, count - 1);
            var sample = row.ChartSamples[index];
            if (!sample.TryGetDriveTemp(row.DriveKey, out double temperature))
            {
                SsdChartTooltip.IsOpen = false;
                return;
            }

            double x = index * stepX;
            double normalized = Math.Clamp((temperature - 20) / (80 - 20), 0, 1);
            double y = height - (normalized * (height - 8) + 4);

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double displayTemp = isFahrenheit ? (temperature * 9 / 5) + 32 : temperature;
            string unit = isFahrenheit ? "°F" : "°C";

            TxtSsdChartTooltipValue.Text = $"{Math.Round(displayTemp)}{unit}";
            TxtSsdChartTooltipTime.Text = sample.Timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);

            DrawChartHoverMarker(row.ChartHoverCanvas, x, y, height, ThemeService.GetColor("ChartLineBrush"));
            ShowChartTooltip(SsdChartTooltip, row.ChartCanvas, x, y, width);
        }

        private static void DrawInlineGridLines(Canvas canvas, double w, double h, int horizontalBands, int verticalBands, string strokeHex)
        {
            Brush brush = ThemeService.GetBrush("ChartGridBrush");
            for (int i = 1; i < horizontalBands; i++)
            {
                double y = (h / horizontalBands) * i;
                canvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y,
                    Y2 = y,
                    Stroke = brush,
                    StrokeThickness = 1
                });
            }

            for (int i = 1; i < verticalBands; i++)
            {
                double x = (w / verticalBands) * i;
                canvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = h,
                    Stroke = brush,
                    StrokeThickness = 1
                });
            }
        }

        private void DrawCpuChart()
        {
            var s = SettingsService.Instance.Current;
            if (!s.ShowCpuGraph)
            {
                CpuChartCanvas.Children.Clear();
                CpuChartHoverCanvas.Children.Clear();
                CpuSmallChartCanvas.Children.Clear();
                CpuSmallChartHoverCanvas.Children.Clear();
                _cpuChartEvents.Clear();
                CpuChartTooltip.IsOpen = false;
                return;
            }

            if (s.CpuGraphSmall)
            {
                CpuChartCanvas.Children.Clear();
                CpuChartHoverCanvas.Children.Clear();
                DrawCpuChartCore(CpuSmallChartCanvas, CpuSmallChartHoverCanvas, TxtCpuSmallChartUnavailable, isSmall: true);
            }
            else
            {
                CpuSmallChartCanvas.Children.Clear();
                CpuSmallChartHoverCanvas.Children.Clear();
                DrawCpuChartCore(CpuChartCanvas, CpuChartHoverCanvas, TxtCpuChartUnavailable, isSmall: false);
            }
        }

        private void DrawCpuChartCore(Canvas plotCanvas, Canvas hoverCanvas, TextBlock unavailableText, bool isSmall)
        {
            plotCanvas.Children.Clear();
            hoverCanvas.Children.Clear();
            double w = plotCanvas.ActualWidth;
            double h = plotCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double plotH = isSmall ? h : Math.Max(10, h - 26);

            if (isSmall)
            {
                DrawInlineGridLines(plotCanvas, w, plotH, horizontalBands: 4, verticalBands: 4, "#2C2C2C");
            }
            else
            {
                for (int i = 1; i <= 3; i++)
                {
                    double y = (plotH / 4.0) * i;
                    plotCanvas.Children.Add(new Line
                    {
                        X1 = 0,
                        X2 = w,
                        Y1 = y,
                        Y2 = y,
                        Stroke = ThemeService.GetBrush("ChartGridBrush"),
                        StrokeThickness = 1
                    });
                }

                for (int i = 1; i <= 2; i++)
                {
                    double x = (w / 3.0) * i;
                    plotCanvas.Children.Add(new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = 0,
                        Y2 = plotH,
                        Stroke = ThemeService.GetBrush("ChartGridBrush"),
                        StrokeThickness = 1
                    });
                }
            }

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(_timeframeSamples)
                .Where(sample => double.IsFinite(sample.CpuTemp) && sample.CpuTemp > 0)
                .ToList();
            _cpuChartSamples = samples;
            if (samples.Count < 2)
            {
                _cpuChartEvents.Clear();
                CpuChartTooltip.IsOpen = false;
                var current = HardwareMonitorService.Instance.CurrentSnapshot;
                bool cpuUnavailable = !(double.IsFinite(current.CpuTemp) && current.CpuTemp > 0);
                unavailableText.Visibility = cpuUnavailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                return;
            }

            unavailableText.Visibility = Visibility.Collapsed;

                        double minT = 25;
            double maxT = 90;
            double padY = isSmall ? 4.0 : 8.0;

            if (!isSmall)
            {
                // Scale tags sit under the series, so the fill tints them instead of boxing them in.
                bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
                AddChartScaleLabel(plotCanvas, FormatScaleTemperature(maxT, isFahrenheit), 1);
                AddChartScaleLabel(plotCanvas, FormatScaleTemperature(minT, isFahrenheit), plotH - 13);
            }

            int n = samples.Count;
            double stepX = w / Math.Max(1, n - 1);
            int maxPoints = Math.Max(16, (int)Math.Ceiling(w * 2));

            int start = 0;
            while (start < n - 1)
            {
                PowerProfileDefinition segmentProfile = GetCpuSampleProfile(samples[start]);
                string segmentKey = segmentProfile.Key;
                int end = start;
                while (end < n - 1 &&
                       string.Equals(GetCpuSampleProfile(samples[end + 1]).Key, segmentKey, StringComparison.Ordinal))
                {
                    end++;
                }

                int segEnd = Math.Min(end + 1, n - 1);
                int segmentLength = segEnd - start + 1;
                int segmentBudget = Math.Max(4,
                    (int)Math.Ceiling(maxPoints * segmentLength / (double)n));
                var indices = ChartSampleReducer.ReduceRange(
                    samples, start, segEnd, segmentBudget, sample => sample.CpuTemp);
                DrawCpuSegment(plotCanvas, samples, indices, segmentProfile, minT, maxT, stepX, plotH, padY, h, includeGlow: !isSmall);
                start = end + 1;
            }

            _cpuChartEvents = SessionReportService.GetChartEvents(samples).ToList();
            DrawChartEventMarkers(plotCanvas, samples, _cpuChartEvents, stepX, plotH, isSmall);

            if (_cpuHoverActive && _cpuHoverSmall == isSmall)
            {
                UpdateCpuChartTooltip(_cpuHoverX, isSmall);
            }
            else if (!_cpuHoverActive)
            {
                CpuChartTooltip.IsOpen = false;
            }
        }

        private static string FormatScaleTemperature(double celsius, bool isFahrenheit)
        {
            double value = isFahrenheit ? (celsius * 9 / 5) + 32 : celsius;
            return $"{Math.Round(value)}°";
        }

        /// <summary>Small muted scale tag drawn inside a chart canvas. No layout cost beyond redraw.</summary>
        private static void AddChartScaleLabel(Canvas canvas, string text, double top)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 9,
                Opacity = 0.7,
                IsHitTestVisible = false,
                Foreground = ThemeService.GetBrush("TextTertiaryBrush")
            };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, top);
            canvas.Children.Add(label);
        }

        private static PowerProfileDefinition GetCpuSampleProfile(TelemetrySample sample)
        {
            return PowerProfiles.Resolve(sample.PowerProfileKey, SettingsService.Instance.Current)
                ?? (sample.CoolingModeActive ? PowerProfiles.CoolingProfile : PowerProfiles.BalancedProfile);
        }

        private static void DrawCpuSegment(
            Canvas targetCanvas,
            List<TelemetrySample> samples,
            IReadOnlyList<int> indices,
            PowerProfileDefinition profile,
            double minT,
            double maxT,
            double stepX,
            double h,
            double padY,
            double fillBottom,
            bool includeGlow)
        {
            var points = new List<Point>(indices.Count);
            foreach (int index in indices)
            {
                double t = samples[index].CpuTemp;
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - (padY * 2)) + padY);
                double x = index * stepX;
                points.Add(new Point(x, y));
            }

            if (points.Count < 2)
            {
                return;
            }

            Color lineColor = SystemParameters.HighContrast
                ? ThemeService.GetColor("ChartLineBrush")
                : ParseColor(profile.Palette.ChartLine);
            Color areaTop = SystemParameters.HighContrast
                ? ThemeService.GetColor("ChartBackgroundBrush")
                : ParseColor(profile.Palette.ChartArea);

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
            targetCanvas.Children.Add(area);

            Geometry lineGeometry = CreateSmoothLineGeometry(points);
            if (includeGlow)
            {
                var glow = new System.Windows.Shapes.Path
                {
                    Data = lineGeometry,
                    Stroke = new SolidColorBrush(Color.FromArgb(42, lineColor.R, lineColor.G, lineColor.B)),
                    StrokeThickness = 3.4,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                targetCanvas.Children.Add(glow);
            }

            var line = new System.Windows.Shapes.Path
            {
                Data = lineGeometry,
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            targetCanvas.Children.Add(line);
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

        private static string NormalizeGpuGraphMetric(string? metric)
        {
            return metric switch
            {
                "Power" => "Power",
                "Load" => "Load",
                _ => "Temperature"
            };
        }

        private static bool TryGetGpuChartValue(
            TelemetrySample sample, string metric, string? gpuIdentifier, out double value)
        {
            switch (metric)
            {
                case "Power":
                    return sample.TryGetGpuPower(gpuIdentifier, out value);
                case "Load":
                    return sample.TryGetGpuLoad(gpuIdentifier, out value);
                default:
                    return sample.TryGetGpuTemp(gpuIdentifier, out value);
            }
        }

        private static bool IsGpuChartMetricAvailable(HardwareSnapshot snapshot, string metric)
        {
            return metric switch
            {
                "Power" => snapshot.GpuPowerWatts.HasValue,
                "Load" => double.IsFinite(snapshot.GpuLoad),
                _ => double.IsFinite(snapshot.GpuTemp) && snapshot.GpuTemp > 0
            };
        }

        private static Color ResolveGpuChartColor(string metric)
        {
            if (SystemParameters.HighContrast)
            {
                return ThemeService.GetColor("ChartLineBrush");
            }

            return metric switch
            {
                "Power" => ThemeService.GetColor("CoolingOffTextBrush"),
                "Load" => ThemeService.GetColor("AccentTextBrush"),
                _ => ThemeService.GetColor("GpuAccentBrush")
            };
        }

        private static string FormatGpuChartValue(double value, string metric)
        {
            if (metric == "Power")
            {
                return $"{Math.Round(value)}W";
            }

            if (metric == "Load")
            {
                return $"{Math.Round(value)}%";
            }

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double displayTemp = isFahrenheit ? (value * 9 / 5) + 32 : value;
            return $"{Math.Round(displayTemp)}°{(isFahrenheit ? "F" : "C")}";
        }

        private void DrawGpuChart()
        {
            GpuChartCanvas.Children.Clear();
            GpuChartHoverCanvas.Children.Clear();
            double w = GpuChartCanvas.ActualWidth;
            double h = GpuChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            string metric = NormalizeGpuGraphMetric(SettingsService.Instance.Current.GpuGraphMetric);
            var current = HardwareMonitorService.Instance.CurrentSnapshot;
            string? activeGpuId = string.IsNullOrWhiteSpace(current.GpuIdentifier)
                ? null
                : current.GpuIdentifier;

            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(_timeframeSamples)
                .Where(sample => TryGetGpuChartValue(sample, metric, activeGpuId, out _))
                .ToList();
            _gpuChartSamples = samples;
            _gpuChartMetric = metric;
            _gpuChartGpuIdentifier = activeGpuId;
            if (samples.Count < 2)
            {
                _gpuChartEvents.Clear();
                GpuChartTooltip.IsOpen = false;
                bool gpuUnavailable = !IsGpuChartMetricAvailable(current, metric);
                TxtGpuChartUnavailable.Visibility = gpuUnavailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                return;
            }

            TxtGpuChartUnavailable.Visibility = Visibility.Collapsed;

            double minValue = metric == "Temperature" ? 30 : 0;
            double maxValue = metric switch
            {
                "Temperature" => 90,
                "Load" => 100,
                _ => Math.Max(
                    50,
                    Math.Ceiling(
                        samples.Max(sample => TryGetGpuChartValue(
                            sample, metric, activeGpuId, out double power) ? power : 0) / 50.0) * 50)
            };
            _gpuChartMinimum = minValue;
            _gpuChartMaximum = maxValue;

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            string GpuScaleText(double value) => metric switch
            {
                "Temperature" => FormatScaleTemperature(value, isFahrenheit),
                "Load" => $"{Math.Round(value)}%",
                _ => $"{Math.Round(value)}W"
            };


            double stepX = w / Math.Max(1, samples.Count - 1);
            int maxPoints = Math.Max(16, (int)Math.Ceiling(w * 2));
            var reducedIndices = ChartSampleReducer.ReduceRange(
                samples,
                0,
                samples.Count - 1,
                maxPoints,
                sample =>
                {
                    TryGetGpuChartValue(sample, metric, activeGpuId, out double value);
                    return value;
                });

            AddChartScaleLabel(GpuChartCanvas, GpuScaleText(maxValue), 1);
            AddChartScaleLabel(GpuChartCanvas, GpuScaleText(minValue), h - 13);

            var areaPoints = new PointCollection { new Point(0, h) };
            var linePoints = new PointCollection();

            foreach (int index in reducedIndices)
            {
                TryGetGpuChartValue(samples[index], metric, activeGpuId, out double value);
                double norm = (value - minValue) / Math.Max(1, maxValue - minValue);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 8) + 4);
                double x = index * stepX;

                areaPoints.Add(new Point(x, y));
                linePoints.Add(new Point(x, y));
            }

            areaPoints.Add(new Point(w, h));

            Color accent = ResolveGpuChartColor(metric);
            var area = new Polygon
            {
                Points = areaPoints,
                Fill = new SolidColorBrush(Color.FromArgb(100, accent.R, accent.G, accent.B))
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
                    Stroke = ThemeService.GetBrush("ChartGridBrush"),
                    StrokeThickness = 1
                });
            }

            GpuChartCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = h / 2,
                Y2 = h / 2,
                Stroke = ThemeService.GetBrush("ChartGridBrush"),
                StrokeThickness = 1
            });

            var line = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 1.5
            };
            GpuChartCanvas.Children.Add(line);

            _gpuChartEvents = SessionReportService.GetChartEvents(samples).ToList();
            DrawChartEventMarkers(GpuChartCanvas, samples, _gpuChartEvents, stepX, h, compact: true);

            if (_gpuHoverActive)
            {
                UpdateGpuChartTooltip(_gpuHoverX);
            }
            else
            {
                GpuChartTooltip.IsOpen = false;
            }
        }

        private static int TimeframeMinutesToSamples(int minutes) => minutes switch
        {
            5 => 300,
            10 => 600,
            30 => 1800,
            60 => 3600,
            _ => 60
        };

        private static int TimeframeMinutesToIndex(int minutes) => minutes switch
        {
            5 => 1,
            10 => 2,
            30 => 3,
            60 => 4,
            _ => 0
        };

        private static int IndexToTimeframeMinutes(int index) => index switch
        {
            1 => 5,
            2 => 10,
            3 => 30,
            4 => 60,
            _ => 1
        };

        private void CpuChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCpuChart();
        }

        private void CpuSmallChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCpuChart();
        }

        private void CmbTimeframe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int minutes = IndexToTimeframeMinutes(CmbTimeframe.SelectedIndex);
            _timeframeSamples = TimeframeMinutesToSamples(minutes);

            // The initial selection fires while XAML is still creating the GPU chart.
            if (CpuChartCanvas == null || CpuChartHoverCanvas == null ||
                GpuChartCanvas == null || GpuChartHoverCanvas == null)
            {
                return;
            }

            if (!_isApplyingSettings && IsLoaded)
            {
                var settings = SettingsService.Instance.Current;
                if (settings.CpuGraphTimeframeMinutes != minutes)
                {
                    settings.CpuGraphTimeframeMinutes = minutes;
                    SettingsService.Instance.Save();
                }
            }

            DrawCpuChart();
            DrawGpuChart();
        }

        private void CpuChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = true;
            _cpuHoverSmall = false;
            _cpuHoverX = e.GetPosition(CpuChartCanvas).X;
            UpdateCpuChartTooltip(_cpuHoverX, false);
        }

        private void CpuChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = false;
            CpuChartTooltip.IsOpen = false;
            CpuChartHoverCanvas.Children.Clear();
        }

        private void CpuSmallChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = true;
            _cpuHoverSmall = true;
            _cpuHoverX = e.GetPosition(CpuSmallChartCanvas).X;
            UpdateCpuChartTooltip(_cpuHoverX, true);
        }

        private void CpuSmallChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _cpuHoverActive = false;
            CpuChartTooltip.IsOpen = false;
            CpuSmallChartHoverCanvas.Children.Clear();
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

        private void UpdateCpuChartTooltip(double hoverX, bool isSmall)
        {
            if (_cpuChartSamples.Count < 2)
            {
                return;
            }

            Canvas targetCanvas = isSmall ? CpuSmallChartCanvas : CpuChartCanvas;
            Canvas hoverCanvas = isSmall ? CpuSmallChartHoverCanvas : CpuChartHoverCanvas;
            double width = targetCanvas.ActualWidth;
            double height = targetCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double plotH = isSmall ? height : Math.Max(10, height - 26);
            double padY = isSmall ? 4.0 : 8.0;
            int count = _cpuChartSamples.Count;
            double stepX = width / Math.Max(1, count - 1);
            int index = Math.Clamp((int)Math.Round(hoverX / stepX), 0, count - 1);
            var sample = _cpuChartSamples[index];
            double x = index * stepX;
            double temperature = sample.CpuTemp;
            if (!double.IsFinite(temperature) || temperature <= 0)
            {
                CpuChartTooltip.IsOpen = false;
                return;
            }
            double normalized = Math.Clamp((temperature - 25) / (90 - 25), 0, 1);
            double y = plotH - (normalized * (plotH - (padY * 2)) + padY);

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            double displayTemp = isFahrenheit ? (temperature * 9 / 5) + 32 : temperature;
            string unit = isFahrenheit ? "°F" : "°C";

            TxtCpuChartTooltipValue.Text = LocalizationService.Format(
                "Main_CpuChartValue", Math.Round(displayTemp), unit);
            TxtCpuChartTooltipTime.Text = sample.Timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            UpdateChartEventTooltip(TxtCpuChartTooltipEvent, _cpuChartEvents, sample.Timestamp);

            PowerProfileDefinition hoverProfile = GetCpuSampleProfile(sample);
            DrawChartHoverMarker(hoverCanvas, x, y, plotH, ParseColor(hoverProfile.Palette.ChartLine));
            ShowChartTooltip(CpuChartTooltip, targetCanvas, x, y, width);
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
            if (!TryGetGpuChartValue(
                sample, _gpuChartMetric, _gpuChartGpuIdentifier, out double value))
            {
                GpuChartTooltip.IsOpen = false;
                return;
            }
            double normalized = Math.Clamp(
                (value - _gpuChartMinimum) / Math.Max(1, _gpuChartMaximum - _gpuChartMinimum),
                0,
                1);
            double y = height - (normalized * (height - 8) + 4);

            TxtGpuChartTooltipValue.Text = FormatGpuChartValue(value, _gpuChartMetric);
            TxtGpuChartTooltipTime.Text = sample.Timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            UpdateChartEventTooltip(TxtGpuChartTooltipEvent, _gpuChartEvents, sample.Timestamp);

            DrawChartHoverMarker(
                GpuChartHoverCanvas,
                x,
                y,
                height,
                ResolveGpuChartColor(_gpuChartMetric));
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
                Stroke = ThemeService.GetBrush("ChartHoverLineBrush"),
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

        private static void DrawChartEventMarkers(
            Canvas canvas,
            IReadOnlyList<TelemetrySample> samples,
            IReadOnlyList<SessionChartEvent> events,
            double stepX,
            double height,
            bool compact)
        {
            if (events.Count == 0 || samples.Count == 0)
            {
                return;
            }

            double width = canvas.ActualWidth;
            foreach (SessionChartEvent item in events)
            {
                int index = FindNearestSampleIndex(samples, item.TimestampLocal);
                if (index < 0)
                {
                    continue;
                }

                double x = index * stepX;
                if (x < 2 || x > width - 2)
                {
                    continue;
                }

                Color color = ResolveChartEventColor(item);
                if (!compact)
                {
                    canvas.Children.Add(new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = 0,
                        Y2 = height,
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 1,
                        Opacity = 0.18,
                        IsHitTestVisible = false
                    });
                }

                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x - 3, 0),
                        new Point(x + 3, 0),
                        new Point(x, compact ? 4 : 5)
                    },
                    Fill = new SolidColorBrush(color),
                    IsHitTestVisible = false
                });
            }
        }

        private static Color ResolveChartEventColor(SessionChartEvent item)
        {
            string key = item.Kind switch
            {
                "alert" => "DangerTextBrush",
                "fan" => "WarningTextBrush",
                "update" => "SuccessTextBrush",
                "profile" => "AccentBrush",
                "cooling" => "CoolingOnTextBrush",
                "heldback" => "WarningTextBrush",
                _ => "TextSecondaryBrush"
            };
            Brush brush = ThemeService.GetBrush(key);
            return brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        }

        private static int FindNearestSampleIndex(
            IReadOnlyList<TelemetrySample> samples,
            DateTime timestamp)
        {
            int bestIndex = -1;
            long bestTicks = long.MaxValue;
            for (int i = 0; i < samples.Count; i++)
            {
                long ticks = Math.Abs((samples[i].Timestamp - timestamp).Ticks);
                if (ticks < bestTicks)
                {
                    bestTicks = ticks;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static void UpdateChartEventTooltip(
            TextBlock target,
            IReadOnlyList<SessionChartEvent> events,
            DateTime timestamp)
        {
            SessionChartEvent? nearest = null;
            long bestTicks = TimeSpan.FromSeconds(30).Ticks;
            foreach (SessionChartEvent item in events)
            {
                long ticks = Math.Abs((item.TimestampLocal - timestamp).Ticks);
                if (ticks <= bestTicks)
                {
                    bestTicks = ticks;
                    nearest = item;
                }
            }

            if (nearest == null)
            {
                target.Text = "";
                target.Visibility = Visibility.Collapsed;
                return;
            }

            target.Text = LocalizationService.Format("Main_ChartEventTooltip", nearest.Text);
            target.Visibility = Visibility.Visible;
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
                double maxLeft = chartWidth - popupWidth - 4;
                double left = maxLeft >= 4
                    ? Math.Clamp(x - (popupWidth / 2), 4, maxLeft)
                    : Math.Max(0, (chartWidth - popupWidth) / 2);

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
            SsdChartTooltip.IsOpen = false;
            CpuChartHoverCanvas.Children.Clear();
            CpuSmallChartHoverCanvas.Children.Clear();
            GpuChartHoverCanvas.Children.Clear();
            foreach (var row in _ssdRows)
            {
                row.HoverActive = false;
                row.ChartHoverCanvas?.Children.Clear();
            }
            _activeHoverDriveRow = null;
        }

        private void BtnCoolingToggle_Click(object sender, RoutedEventArgs e)
        {
            PowerPlanService.Instance.ToggleCoolingMode();
        }

        private void BtnStressTest_Click(object sender, RoutedEventArgs e)
        {
            ToggleStressTestDialog();
        }

        private void BtnToolsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ToolsContextMenu == null)
            {
                return;
            }

            ToolsContextMenu.PlacementTarget = BtnToolsMenu;
            ToolsContextMenu.Placement = PlacementMode.Bottom;
            ToolsContextMenu.HorizontalOffset = 0;
            ToolsContextMenu.VerticalOffset = 4;
            ToolsContextMenu.FlowDirection = FlowDirection;
            ToolsContextMenu.IsOpen = true;
        }

        private void ToolsContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (ToolsContextMenu == null)
            {
                return;
            }

            ToolsContextMenu.FlowDirection = FlowDirection;
            bool isRecordingCsv = TelemetryCsvService.Instance.IsRecording;
            MenuToolsRecordCsv.Header = LocalizationService.Get(isRecordingCsv ? "Main_ToolsStopCsvLog" : "Main_ToolsStartCsvLog");
        }

        private void MenuToolsStress_Click(object sender, RoutedEventArgs e)
        {
            OpenStressTestDialog();
        }

        private void MenuToolsExportCsv_Click(object sender, RoutedEventArgs e)
        {
            ExportRingBufferCsv();
        }

        private void MenuToolsRecordCsv_Click(object sender, RoutedEventArgs e)
        {
            ToggleLiveCsvLogging();
        }

        private void MenuToolsHealthCheck_Click(object sender, RoutedEventArgs e)
        {
            ShowHealthCheckDialog();
        }

        private void MenuToolsSessionReport_Click(object sender, RoutedEventArgs e)
        {
            ShowSessionReportDialog();
        }

        private void MenuToolsCopySnapshot_Click(object sender, RoutedEventArgs e)
        {
            CopyDashboardSnapshot();
        }

        private void CopyDashboardSnapshot()
        {
            var s = SettingsService.Instance.Current;
            bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";
            string tempUnit = isFahrenheit ? "°F" : "°C";
            string text = FormatDashboardSnapshot(
                HardwareMonitorService.Instance.CurrentSnapshot,
                PowerPlanService.Instance.ActiveProfile,
                isFahrenheit,
                tempUnit,
                s.ShowDiskTemp,
                s.ExcludedDrives);

            try
            {
                Clipboard.SetText(text);
                ShowCopyFeedbackMessage(LocalizationService.Get("Main_ToolsCopiedSnapshot"));
            }
            catch
            {
                ShowCopyFeedbackMessage(LocalizationService.Get("System_CopyFailed"));
            }
        }

        private void ShowCopyFeedbackMessage(string message)
        {
            if (TxtActivePowerProfile == null)
            {
                return;
            }

            _copyFeedbackTimer?.Stop();
            _isCopyFeedbackActive = true;
            TxtActivePowerProfile.Text = message;

            if (_copyFeedbackTimer == null)
            {
                _copyFeedbackTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1600)
                };
                _copyFeedbackTimer.Tick += (_, _) =>
                {
                    _copyFeedbackTimer?.Stop();
                    _isCopyFeedbackActive = false;
                    if (!_isClosing)
                    {
                        UpdateActiveProfileLabel();
                        if (PowerPlanService.Instance.ActiveProfile == null && TxtActivePowerProfile != null)
                        {
                            TxtActivePowerProfile.Text = "";
                        }
                    }
                };
            }

            _copyFeedbackTimer.Start();
        }

        internal static string FormatDashboardSnapshot(
            HardwareSnapshot? snapshot,
            PowerProfileDefinition? activeProfile,
            bool isFahrenheit,
            string tempUnit,
            bool showDiskTemp = true,
            IReadOnlyCollection<string>? excludedDrives = null)
        {
            string FormatTempValue(double celsius)
            {
                if (!double.IsFinite(celsius))
                {
                    return "--";
                }

                double converted = isFahrenheit ? (celsius * 9.0 / 5.0) + 32.0 : celsius;
                return $"{Math.Round(converted).ToString("0", CultureInfo.InvariantCulture)}{tempUnit}";
            }

            var sb = new System.Text.StringBuilder();
            bool isCooling = activeProfile?.IsCooling == true;
            string modeState = LocalizationService.Get(isCooling ? "Common_On" : "Common_Off");
            string planSuffix = activeProfile != null
                ? $" ({LocalizationService.GetProfileName(activeProfile)})"
                : "";
            sb.AppendLine($"{LocalizationService.Get("Main_CoolingMode")}: {modeState}{planSuffix}");

            string cpuLabel = LocalizationService.Get("Common_Cpu");
            if (snapshot == null)
            {
                sb.AppendLine($"{cpuLabel}: --");
                sb.AppendLine($"{LocalizationService.Get("Main_LabelRam")}: --");
                return sb.ToString().TrimEnd();
            }

            string cpuName = string.IsNullOrWhiteSpace(snapshot.CpuName)
                ? LocalizationService.Get("Common_NotReported")
                : snapshot.CpuName.Trim();
            var cpuParts = new List<string>(4) { FormatTempValue(snapshot.CpuTemp) };
            if (double.IsFinite(snapshot.CpuGhz) && snapshot.CpuGhz > 0)
            {
                string clockText = FormatCpuClockReadout(
                    snapshot.CpuGhz,
                    snapshot.CpuBaseGhz,
                    snapshot.CpuMaxGhz,
                    showBaseSpeed: true);
                cpuParts.Add($"{clockText} {LocalizationService.Get("Main_LabelGhz")}");
            }
            if (double.IsFinite(snapshot.CpuLoad))
            {
                cpuParts.Add($"{Math.Round(snapshot.CpuLoad).ToString("0", CultureInfo.InvariantCulture)}%");
            }
            if (snapshot.CpuPackageWatts.HasValue)
            {
                cpuParts.Add(FormatPackagePower(snapshot.CpuPackageWatts));
            }
            sb.AppendLine($"{cpuLabel}: {cpuName} — {string.Join(" | ", cpuParts)}");

            bool hasGpu = !string.IsNullOrWhiteSpace(snapshot.GpuName) ||
                double.IsFinite(snapshot.GpuTemp) ||
                double.IsFinite(snapshot.GpuLoad) ||
                snapshot.GpuPowerWatts.HasValue ||
                snapshot.Gpus.Count > 0;
            if (hasGpu)
            {
                string gpuName = string.IsNullOrWhiteSpace(snapshot.GpuName)
                    ? LocalizationService.Get("Common_NotReported")
                    : snapshot.GpuName.Trim();
                var gpuParts = new List<string>(6) { FormatTempValue(snapshot.GpuTemp) };
                if (snapshot.GpuPowerWatts.HasValue)
                {
                    gpuParts.Add(FormatPackagePower(snapshot.GpuPowerWatts));
                }
                if (double.IsFinite(snapshot.GpuLoad))
                {
                    gpuParts.Add($"{Math.Round(snapshot.GpuLoad).ToString("0", CultureInfo.InvariantCulture)}%");
                }
                string vram = FormatGpuVramUsage(snapshot);
                if (!string.Equals(vram, "--", StringComparison.Ordinal))
                {
                    gpuParts.Add($"{LocalizationService.Get("Main_GpuVramUsageLabel")} {vram}");
                }
                string clocks = FormatGpuClocks(snapshot);
                if (!string.Equals(clocks, "--", StringComparison.Ordinal))
                {
                    gpuParts.Add(clocks);
                }
                string throttle = CompactThrottleReason(snapshot.GpuThrottleReason);
                if (!string.Equals(throttle, "--", StringComparison.Ordinal))
                {
                    gpuParts.Add($"{LocalizationService.Get("Main_GpuThrottleLabel")} {throttle}");
                }
                sb.AppendLine($"{LocalizationService.Get("Main_GpuLabel")}: {gpuName} — {string.Join(" | ", gpuParts)}");
            }

            string ramLabel = LocalizationService.Get("Main_LabelRam");
            if (double.IsFinite(snapshot.RamUsedGb) &&
                double.IsFinite(snapshot.RamTotalGb) &&
                snapshot.RamTotalGb > 0)
            {
                string ramRatio = $"{snapshot.RamUsedGb.ToString("0.0", CultureInfo.InvariantCulture)} / {snapshot.RamTotalGb.ToString("0.0", CultureInfo.InvariantCulture)} {LocalizationService.Get("Main_LabelGb")}";
                string ramPct = double.IsFinite(snapshot.RamUsagePercent)
                    ? $" ({Math.Round(snapshot.RamUsagePercent).ToString("0", CultureInfo.InvariantCulture)}%)"
                    : "";
                sb.AppendLine($"{ramLabel}: {ramRatio}{ramPct}");
            }
            else
            {
                sb.AppendLine($"{ramLabel}: --");
            }

            if (showDiskTemp && snapshot.StorageDrives != null)
            {
                foreach (var drive in snapshot.StorageDrives)
                {
                    if (drive == null || HardwareMonitorService.IsDriveExcluded(drive, excludedDrives))
                    {
                        continue;
                    }

                    var driveParts = new List<string>(3) { FormatTempValue(drive.Temperature) };
                    if (drive.SmartHealthPercent.HasValue)
                    {
                        driveParts.Add($"{drive.SmartHealthPercent.Value.ToString(CultureInfo.InvariantCulture)}%");
                    }
                    if (!string.IsNullOrWhiteSpace(drive.Model))
                    {
                        driveParts.Add(drive.Model.Trim());
                    }
                    sb.AppendLine($"{FormatSsdRowLabel(drive)}: {string.Join(" | ", driveParts)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private void MenuToolsAbout_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsDialog(true);
        }

        private void RunWithChildDialogGuard(Action action)
        {
            bool previous = _isChildDialogOpen;
            _isChildDialogOpen = true;
            try
            {
                action();
            }
            finally
            {
                UpdateChildDialogState();
                if (previous)
                {
                    _isChildDialogOpen = true;
                }
            }
        }

        private void ExportRingBufferCsv()
        {
            var samples = HardwareMonitorService.Instance.RingBuffer.GetRecentSamples(3600);
            if (samples.Count == 0)
            {
                RunWithChildDialogGuard(() => MessageBox.Show(
                    LocalizationService.Get("Main_CsvNoSamples"),
                    LocalizationService.Get("App_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
                return;
            }

            RunWithChildDialogGuard(() =>
            {
                var sfd = new SaveFileDialog
                {
                    Filter = LocalizationService.Get("Common_CsvFilter"),
                    FileName = $"CryoTray_Telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (sfd.ShowDialog(this) == true)
                {
                    TelemetryCsvService.ExportSamples(samples, sfd.FileName);
                    MessageBox.Show(
                        LocalizationService.Format("Main_CsvExportDone", samples.Count, sfd.FileName),
                        LocalizationService.Get("App_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            });
        }

        private void ToggleLiveCsvLogging()
        {
            if (TelemetryCsvService.Instance.IsRecording)
            {
                TelemetryCsvService.Instance.StopRecording();
                return;
            }

            RunWithChildDialogGuard(() =>
            {
                var sfd = new SaveFileDialog
                {
                    Filter = LocalizationService.Get("Common_CsvFilter"),
                    FileName = $"CryoTray_LiveLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (sfd.ShowDialog(this) == true)
                {
                    TelemetryCsvService.Instance.StartRecording(sfd.FileName);
                }
            });
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

        private void RefreshUpdateBanner()
        {
            if (UpdateBanner == null)
            {
                return;
            }

            var release = (System.Windows.Application.Current as App)?.AvailableUpdate;
            var settings = SettingsService.Instance.Current;
            _updateSnoozeTimer?.Stop();
            _updateSnoozeTimer = null;

            bool dismissed = release != null &&
                string.Equals(settings.DismissedUpdateVersion, release.Version, StringComparison.Ordinal);
            bool snoozed = release != null &&
                string.Equals(settings.UpdateSnoozedVersion, release.Version, StringComparison.Ordinal) &&
                settings.UpdateSnoozedUntilUtc.HasValue &&
                settings.UpdateSnoozedUntilUtc.Value > DateTime.UtcNow;

            if (!snoozed && release != null &&
                string.Equals(settings.UpdateSnoozedVersion, release.Version, StringComparison.Ordinal) &&
                settings.UpdateSnoozedUntilUtc.HasValue)
            {
                settings.UpdateSnoozedVersion = "";
                settings.UpdateSnoozedUntilUtc = null;
                SettingsService.Instance.Save();
            }

            if (release == null || dismissed || snoozed)
            {
                _visibleUpdate = null;
                UpdateBanner.Visibility = Visibility.Collapsed;

                if (snoozed && settings.UpdateSnoozedUntilUtc.HasValue)
                {
                    TimeSpan remaining = settings.UpdateSnoozedUntilUtc.Value - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        _updateSnoozeTimer = new DispatcherTimer { Interval = remaining };
                        _updateSnoozeTimer.Tick += UpdateSnoozeTimer_Tick;
                        _updateSnoozeTimer.Start();
                    }
                }
            }
            else
            {
                _visibleUpdate = release;
                TxtUpdateBannerVersion.Text = LocalizationService.Format("Update_BannerVersion", release.Version);
                UpdateBanner.Visibility = Visibility.Visible;
            }

            UpdateWindowHeight(settings.ShowOverlayRow, settings.ShowAutopilotRow);
        }

        private void UpdateSnoozeTimer_Tick(object? sender, EventArgs e)
        {
            _updateSnoozeTimer?.Stop();
            _updateSnoozeTimer = null;
            RefreshUpdateBanner();
        }

        private void OnUpdateStatusChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnUpdateStatusChanged));
                return;
            }

            RefreshUpdateBanner();
        }

        private void RefreshSuccessBanner()
        {
            if (UpdateSuccessBanner == null || TxtUpdateSuccess == null)
            {
                return;
            }

            var completion = (System.Windows.Application.Current as App)?.PendingUpdateSuccess;
            if (completion == null)
            {
                if (UpdateSuccessBanner.Visibility == Visibility.Visible)
                {
                    HideUpdateSuccessBanner(consume: false);
                }
                return;
            }

            TxtUpdateSuccess.Text = LocalizationService.Format(
                "Update_SuccessBanner", completion.FromVersion, completion.ToVersion);

            if (UpdateSuccessBanner.Visibility == Visibility.Visible)
            {
                return;
            }

            UpdateSuccessBanner.Visibility = Visibility.Visible;
            _updateSuccessTimer?.Stop();
            _updateSuccessTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            _updateSuccessTimer.Tick += UpdateSuccessTimer_Tick;
            _updateSuccessTimer.Start();
            UpdateWindowHeight(
                SettingsService.Instance.Current.ShowOverlayRow,
                SettingsService.Instance.Current.ShowAutopilotRow);
        }

        private void UpdateSuccessTimer_Tick(object? sender, EventArgs e)
        {
            HideUpdateSuccessBanner(consume: true);
        }

        private void HideUpdateSuccessBanner(bool consume)
        {
            _updateSuccessTimer?.Stop();
            _updateSuccessTimer = null;

            if (UpdateSuccessBanner.Visibility == Visibility.Visible)
            {
                UpdateSuccessBanner.Visibility = Visibility.Collapsed;
                UpdateWindowHeight(
                    SettingsService.Instance.Current.ShowOverlayRow,
                    SettingsService.Instance.Current.ShowAutopilotRow);
            }

            if (consume)
            {
                (System.Windows.Application.Current as App)?.ConsumeUpdateSuccess();
            }
        }

        private void BtnUpdateSuccessClose_Click(object sender, RoutedEventArgs e)
        {
            HideUpdateSuccessBanner(consume: true);
        }

        private void BtnUpdateBannerUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_visibleUpdate != null)
            {
                OpenSettingsDialog(true, _visibleUpdate, true);
            }
        }

        private void BtnUpdateBannerAbout_Click(object sender, RoutedEventArgs e)
        {
            if (_visibleUpdate != null)
            {
                OpenSettingsDialog(true, _visibleUpdate, false);
            }
            else
            {
                OpenSettingsDialog(true);
            }
        }

        private void BtnUpdateBannerLater_Click(object sender, RoutedEventArgs e)
        {
            if (_visibleUpdate != null)
            {
                var settings = SettingsService.Instance.Current;
                settings.UpdateSnoozedVersion = _visibleUpdate.Version;
                settings.UpdateSnoozedUntilUtc = DateTime.UtcNow.AddHours(24);
                SettingsService.Instance.Save();
            }

            RefreshUpdateBanner();
        }

        private void BtnUpdateBannerSkip_Click(object sender, RoutedEventArgs e)
        {
            if (_visibleUpdate != null)
            {
                var settings = SettingsService.Instance.Current;
                settings.DismissedUpdateVersion = _visibleUpdate.Version;
                settings.UpdateSnoozedVersion = "";
                settings.UpdateSnoozedUntilUtc = null;
                SettingsService.Instance.Save();
            }

            RefreshUpdateBanner();
        }

        public void OpenSettingsDialog()
        {
            OpenSettingsDialog(false);
        }

        public void OpenSettingsDialog(bool openAbout)
        {
            ShowSettingsWindow(openAbout, false);
        }

        public void OpenSettingsDialog(bool openAbout, ReleaseInfo? update, bool installUpdate)
        {
            ShowSettingsWindow(openAbout, false, update, installUpdate);
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

        private void ShowSettingsWindow(bool openAbout, bool openAutopilot,
            ReleaseInfo? update = null, bool installUpdate = false)
        {
            if (_settingsWindow != null)
            {
                if (openAbout) _settingsWindow.SelectAboutTab();
                if (openAutopilot) _settingsWindow.SelectAutopilotTab();
                _settingsWindow.Activate();
                if (update != null)
                {
                    if (installUpdate) _settingsWindow.BeginUpdateInstall(update);
                    else _settingsWindow.ShowAvailableUpdate(update);
                }
                return;
            }

            var win = new SettingsWindow
            {
                Owner = this,
                OpenAboutOnLoad = openAbout,
                OpenAutopilotOnLoad = openAutopilot,
                PendingUpdate = update,
                PendingUpdateInstall = installUpdate
            };
            DockSideBySide(win);
            _settingsWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            UpdateTitleIconStates();
            win.Show();
            win.Activate();
        }

        public void OpenSystemInfoDialog(string? highlightFanKey = null)
        {
            if (_systemInfoWindow != null)
            {
                _systemInfoWindow.Activate();
                if (!string.IsNullOrWhiteSpace(highlightFanKey))
                {
                    _systemInfoWindow.HighlightFan(highlightFanKey);
                }

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
            if (!string.IsNullOrWhiteSpace(highlightFanKey))
            {
                win.HighlightFan(highlightFanKey);
            }
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

        public void ShowHealthCheckDialog(bool postUpdate = false, bool firstRun = false)
        {
            if (_healthCheckWindow != null)
            {
                if (!_healthCheckWindow.IsVisible)
                {
                    _healthCheckWindow.Show();
                }
                _healthCheckWindow.Activate();
                return;
            }

            var win = new HealthCheckWindow(postUpdate, firstRun) { Owner = this };
            win.OpenSettingsRequested += () => OpenSettingsDialog(false);
            win.OpenSystemInformationRequested += () => OpenSystemInfoDialog();
            _healthCheckWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            win.Show();
            win.Activate();
        }

        public void ShowSessionReportDialog()
        {
            if (_sessionReportWindow != null)
            {
                if (!_sessionReportWindow.IsVisible)
                {
                    _sessionReportWindow.Show();
                }
                _sessionReportWindow.Activate();
                return;
            }

            var win = new SessionReportWindow { Owner = this };
            DockSideBySide(win);
            _sessionReportWindow = win;
            win.Closed += (_, _) => UntrackChildWindow(win);
            UpdateChildDialogState();
            win.Show();
            win.Activate();
        }

        private void UntrackChildWindow(Window window)
        {
            if (ReferenceEquals(window, _settingsWindow)) _settingsWindow = null;
            if (ReferenceEquals(window, _systemInfoWindow)) _systemInfoWindow = null;
            if (ReferenceEquals(window, _storageCleanupWindow)) _storageCleanupWindow = null;
            if (ReferenceEquals(window, _stressTestWindow)) _stressTestWindow = null;
            if (ReferenceEquals(window, _healthCheckWindow)) _healthCheckWindow = null;
            if (ReferenceEquals(window, _sessionReportWindow)) _sessionReportWindow = null;

            UpdateChildDialogState();
            UpdateTitleIconStates();
            if (!_isClosing)
            {
                MemoryTrimService.Instance.ScheduleTrim();
            }
        }

        private void UpdateChildDialogState()
        {
            _isChildDialogOpen = _settingsWindow != null ||
                _systemInfoWindow != null ||
                _storageCleanupWindow != null ||
                _stressTestWindow != null ||
                _healthCheckWindow != null ||
                _sessionReportWindow != null;
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
                icon.SetResourceReference(property, "IconBrush");
            }
        }

        public void DockSideBySide(Window win)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;

            Rect workArea = WindowPlacementHelper.GetMonitorWorkAreaDips(this);
            var anchorBounds = new Rect(Left, Top, Width, Height);
            var (targetLeft, targetTop) = WindowPlacementHelper.ComputeSideBySidePosition(
                workArea,
                anchorBounds,
                win.Width,
                win.Height,
                gap: 6);

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
            if (e.Key == Key.C &&
                Keyboard.Modifiers == ModifierKeys.Control &&
                !IsTextInputFocused())
            {
                CopyDashboardSnapshot();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && CpuCoresPopup.IsOpen)
            {
                CpuCoresPopup.IsOpen = false;
                e.Handled = true;
            }
            if (e.Key == Key.Escape && RamBreakdownToolTip.IsOpen)
            {
                CloseRamBreakdownToolTip();
                e.Handled = true;
            }
        }

        private static bool IsTextInputFocused() =>
            Keyboard.FocusedElement is TextBoxBase or PasswordBox;

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ShowKeyboardFocusCues = false;
            UpdateCpuTempFocusRing();
            if (_ramTooltipPinned && e.OriginalSource is DependencyObject source && !IsDescendantOf(source, BtnRamBreakdown))
            {
                CloseRamBreakdownToolTip();
            }
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            DependencyObject? current = node;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        private void BtnRamBreakdown_Click(object sender, RoutedEventArgs e)
        {
            if (RamBreakdownToolTip.IsOpen && _ramTooltipPinned)
            {
                CloseRamBreakdownToolTip();
                return;
            }

            CpuCoresPopup.IsOpen = false;
            _ramTooltipPinned = true;
            UpdateMemoryBreakdownUi(HardwareMonitorService.Instance.CurrentSnapshot);
            RamBreakdownToolTip.IsOpen = true;
        }

        private void BtnRamBreakdown_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_ramTooltipPinned)
            {
                CloseRamBreakdownToolTip();
            }
        }

        private void RamBreakdownToolTip_Opened(object sender, RoutedEventArgs e)
        {
            UpdateMemoryBreakdownUi(HardwareMonitorService.Instance.CurrentSnapshot);
        }

        private void RamBreakdownToolTip_Closed(object sender, RoutedEventArgs e)
        {
            _ramTooltipPinned = false;
        }

        private void CloseRamBreakdownToolTip()
        {
            _ramTooltipPinned = false;
            RamBreakdownToolTip.IsOpen = false;
        }

        internal void ExerciseRamTooltipLifecycleForTest()
        {
            if (RamTelemetryZone.ToolTip != null ||
                TxtRamPercent.ToolTip != null ||
                !ReferenceEquals(BtnRamBreakdown.ToolTip, RamBreakdownToolTip))
            {
                throw new InvalidOperationException("RamBreakdownToolTip must be scoped strictly to BtnRamBreakdown.");
            }

            var clickArgs = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, BtnRamBreakdown);
            BtnRamBreakdown_Click(BtnRamBreakdown, clickArgs);
            if (!RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen || !_ramTooltipPinned)
            {
                throw new InvalidOperationException("Click-pin open state invalid on RamBreakdownToolTip.");
            }

            CloseRamBreakdownToolTip();
            if (RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen || _ramTooltipPinned)
            {
                throw new InvalidOperationException("CloseRamBreakdownToolTip must leave ToolTip.StaysOpen true.");
            }

            var pcsType = typeof(FrameworkElement).Assembly.GetType("System.Windows.Controls.PopupControlService")
                ?? throw new InvalidOperationException("WPF PopupControlService type not found.");
            var pcs = pcsType.GetProperty("Current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null)
                ?? throw new InvalidOperationException("WPF PopupControlService.Current is null.");
            var findMethod = pcsType.GetMethod("FindToolTipOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("WPF PopupControlService.FindToolTipOwner not found.");
            var showMethod = pcsType.GetMethod("ShowToolTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("WPF PopupControlService.ShowToolTip not found.");
            var dismissMethod = pcsType.GetMethod("DismissToolTips", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("WPF PopupControlService.DismissToolTips not found.");

            var triggerType = findMethod.GetParameters()[1].ParameterType;
            object mouseTrigger = Enum.Parse(triggerType, "Mouse");
            if (findMethod.Invoke(pcs, new object[] { RamTelemetryZone, mouseTrigger }) != null ||
                findMethod.Invoke(pcs, new object[] { TxtRamPercent, mouseTrigger }) != null ||
                !ReferenceEquals(findMethod.Invoke(pcs, new object[] { BtnRamBreakdown, mouseTrigger }), BtnRamBreakdown) ||
                !ReferenceEquals(findMethod.Invoke(pcs, new object[] { TxtRamRatio, mouseTrigger }), BtnRamBreakdown))
            {
                throw new InvalidOperationException("FindToolTipOwner did not scope hover exclusively to BtnRamBreakdown.");
            }

            // Hover #1
            showMethod.Invoke(pcs, new object[] { BtnRamBreakdown, false });
            if (!RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen)
            {
                throw new InvalidOperationException("Hover #1 failed to open RamBreakdownToolTip.");
            }

            dismissMethod.Invoke(pcs, Array.Empty<object>());
            if (RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen || _ramTooltipPinned)
            {
                throw new InvalidOperationException("Hover #1 dismiss state invalid on RamBreakdownToolTip.");
            }

            // Hover #2 (regression check after closing)
            showMethod.Invoke(pcs, new object[] { BtnRamBreakdown, false });
            if (!RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen)
            {
                throw new InvalidOperationException("Hover #2 failed to open RamBreakdownToolTip.");
            }

            dismissMethod.Invoke(pcs, Array.Empty<object>());

            // Pinned + Hover
            BtnRamBreakdown_Click(BtnRamBreakdown, clickArgs);
            showMethod.Invoke(pcs, new object[] { BtnRamBreakdown, false });
            if (!RamBreakdownToolTip.IsOpen || !RamBreakdownToolTip.StaysOpen)
            {
                throw new InvalidOperationException("Pinned + Hover state invalid on RamBreakdownToolTip.");
            }

            dismissMethod.Invoke(pcs, Array.Empty<object>());
            CloseRamBreakdownToolTip();
        }

        private async void BtnOptimizeRam_Click(object sender, RoutedEventArgs e)
        {
            await MemoryOptimizerService.Instance.OptimizeAsync(MemoryOptimizationTrigger.Manual);
        }

        private void BtnOptimizeRamMenu_Click(object sender, RoutedEventArgs e)
        {
            if (RamScopeMenu == null)
            {
                return;
            }

            RamScopeMenu.PlacementTarget = BtnOptimizeRamMenu;
            RamScopeMenu.Placement = PlacementMode.Bottom;
            RamScopeMenu.HorizontalOffset = 0;
            RamScopeMenu.VerticalOffset = 4;
            RamScopeMenu.IsOpen = true;
        }

        private void RamScopeMenu_Opened(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            RamScopeMenu.FlowDirection = FlowDirection;
            MenuRamFreeWorkingSets.IsChecked = settings.RamFreeWorkingSets;
            MenuRamClearStandby.IsChecked = settings.RamClearStandby;
        }

        internal (System.Drawing.Bitmap ToolsMenu, System.Drawing.Bitmap RamMenu) RenderWpfContextMenusForTest()
        {
            ToolsContextMenu_Opened(ToolsContextMenu, new RoutedEventArgs());
            RamScopeMenu_Opened(RamScopeMenu, new RoutedEventArgs());
            MenuRamFreeWorkingSets.IsChecked = true;
            MenuRamClearStandby.IsChecked = false;

            return (
                RenderContextMenuBitmapForTest(ToolsContextMenu),
                RenderContextMenuBitmapForTest(RamScopeMenu));
        }

        private static System.Drawing.Bitmap RenderContextMenuBitmapForTest(ContextMenu menu)
        {
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = menu.DesiredSize;
            int width = Math.Max(1, (int)Math.Ceiling(desired.Width));
            int height = Math.Max(1, (int)Math.Ceiling(desired.Height));
            menu.Arrange(new Rect(0, 0, width, height));
            menu.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(menu);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            using var temp = new System.Drawing.Bitmap(ms);
            return new System.Drawing.Bitmap(temp);
        }

        private void MenuRamStorageCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (_storageCleanupWindow != null)
            {
                _storageCleanupWindow.Activate();
                return;
            }

            var window = new StorageCleanupWindow { Owner = this };
            DockSideBySide(window);
            _storageCleanupWindow = window;
            window.Closed += (_, _) => UntrackChildWindow(window);
            UpdateChildDialogState();
            window.Show();
            window.Activate();
        }

        private void MenuRamScope_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            settings.RamFreeWorkingSets = MenuRamFreeWorkingSets.IsChecked;
            settings.RamClearStandby = MenuRamClearStandby.IsChecked;
            SettingsService.Instance.Save();
            UpdateMemoryTooltipFooter();
        }

        private void OnRamOptimizingStateChanged(bool isOptimizing)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing)
                {
                    return;
                }

                SetRamOptimizingVisualState(isOptimizing);
                UpdateMemoryTooltipFooter();
            }));
        }

        private void OnRamOptimizationCompleted(MemoryOptimizationResult result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing)
                {
                    return;
                }

                UpdateMemoryBreakdownUi(HardwareMonitorService.Instance.CurrentSnapshot);

                if (result.Succeeded)
                {
                    FlashRamSuccessCheckmark();
                }
            }));
        }

        private void SetRamOptimizingVisualState(bool isOptimizing)
        {
            BtnOptimizeRam.IsEnabled = !isOptimizing;
            if (isOptimizing)
            {
                RamCheckIcon.Visibility = Visibility.Collapsed;
                RamCleanIcon.Visibility = Visibility.Visible;
                var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(350))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                OptGlyphHost.BeginAnimation(OpacityProperty, pulse);
            }
            else
            {
                OptGlyphHost.BeginAnimation(OpacityProperty, null);
                OptGlyphHost.Opacity = 1.0;
            }
        }

        private void FlashRamSuccessCheckmark()
        {
            _ramFeedbackTimer?.Stop();
            RamCleanIcon.Visibility = Visibility.Collapsed;
            RamCheckIcon.Visibility = Visibility.Visible;

            _ramFeedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _ramFeedbackTimer.Tick += (_, _) =>
            {
                _ramFeedbackTimer?.Stop();
                if (!_isClosing)
                {
                    RamCheckIcon.Visibility = Visibility.Collapsed;
                    RamCleanIcon.Visibility = Visibility.Visible;
                }
            };
            _ramFeedbackTimer.Start();
        }

        private void UpdateMemoryBreakdownUi(HardwareSnapshot snapshot)
        {
            MemoryBreakdown? mem = snapshot.Memory;
            if (mem == null && MemoryTelemetryService.TryReadBreakdown(out var live))
            {
                mem = live;
            }

            if (mem == null || mem.TotalUsableBytes == 0)
            {
                RamBarInUseCol.Width = new GridLength(0, GridUnitType.Star);
                RamBarStandbyCol.Width = new GridLength(0, GridUnitType.Star);
                RamBarFreeCol.Width = new GridLength(1, GridUnitType.Star);

                TxtTipInstalled.Text = LocalizationService.Format("Ram_InstalledFormat", "--");
                TxtTipInUse.Text = "--";
                TxtTipAvailable.Text = "--";
                TxtTipFree.Text = "--";
                TxtTipStandby.Text = "--";
                TxtTipModified.Text = "--";
                TxtTipCached.Text = "--";
                TxtTipReserved.Text = "--";
                TipBarInUseCol.Width = new GridLength(0, GridUnitType.Star);
                TipBarModifiedCol.Width = new GridLength(0, GridUnitType.Star);
                TipBarStandbyCol.Width = new GridLength(0, GridUnitType.Star);
                TipBarFreeCol.Width = new GridLength(1, GridUnitType.Star);
                UpdateMemoryTooltipFooter();
                return;
            }

            double totalUsable = mem.TotalUsableBytes;
            double rowInUseRatio = mem.InUseBytes / totalUsable;
            double rowStandbyRatio = mem.HasPageListBreakdown
                ? Math.Min(mem.StandbyBytes / totalUsable, Math.Max(0.0, 1.0 - rowInUseRatio))
                : 0.0;
            double rowFreeRatio = Math.Max(0.0, 1.0 - rowInUseRatio - rowStandbyRatio);

            RamBarInUseCol.Width = new GridLength(rowInUseRatio, GridUnitType.Star);
            RamBarStandbyCol.Width = new GridLength(rowStandbyRatio, GridUnitType.Star);
            RamBarFreeCol.Width = new GridLength(rowFreeRatio, GridUnitType.Star);

            ulong barModified = mem.HasPageListBreakdown ? mem.ModifiedBytes : 0UL;
            ulong barStandby = mem.HasPageListBreakdown ? mem.StandbyBytes : 0UL;
            ulong barFree = mem.HasPageListBreakdown ? mem.FreeBytes : mem.AvailableBytes;
            double barDenom = Math.Max(1.0, (double)mem.InUseBytes + barModified + barStandby + barFree);

            TipBarInUseCol.Width = new GridLength(mem.InUseBytes / barDenom, GridUnitType.Star);
            TipBarModifiedCol.Width = new GridLength(barModified / barDenom, GridUnitType.Star);
            TipBarStandbyCol.Width = new GridLength(barStandby / barDenom, GridUnitType.Star);
            TipBarFreeCol.Width = new GridLength(barFree / barDenom, GridUnitType.Star);

            TxtTipInstalled.Text = LocalizationService.Format(
                "Ram_InstalledFormat",
                MemoryTelemetryService.FormatBytes(mem.InstalledBytes));

            int inUsePct = (int)Math.Round(mem.InUsePercent);
            int availPct = Math.Max(0, 100 - inUsePct);

            TxtTipInUse.Text = $"{MemoryTelemetryService.FormatBytes(mem.InUseBytes)} ({inUsePct}%)";
            TxtTipAvailable.Text = $"{MemoryTelemetryService.FormatBytes(mem.AvailableBytes)} ({availPct}%)";
            TxtTipFree.Text = mem.HasPageListBreakdown ? MemoryTelemetryService.FormatBytes(mem.FreeBytes) : "--";
            TxtTipStandby.Text = mem.HasPageListBreakdown ? MemoryTelemetryService.FormatBytes(mem.StandbyBytes) : "--";
            TxtTipModified.Text = mem.HasPageListBreakdown ? MemoryTelemetryService.FormatBytes(mem.ModifiedBytes) : "--";
            TxtTipCached.Text = mem.HasPageListBreakdown ? MemoryTelemetryService.FormatBytes(mem.CachedBytes) : "--";
            TxtTipReserved.Text = MemoryTelemetryService.FormatBytes(mem.HardwareReservedBytes);

            UpdateMemoryTooltipFooter();
        }

        private void UpdateMemoryTooltipFooter()
        {
            if (TxtTipFooter == null)
            {
                return;
            }

            TxtTipFooter.Inlines.Clear();
            var optimizer = MemoryOptimizerService.Instance;

            if (optimizer.IsOptimizing)
            {
                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run(
                    LocalizationService.Get("Ram_FooterOptimizing"))
                {
                    Foreground = RamAccentBrush,
                    FontWeight = FontWeights.SemiBold
                });
                return;
            }

            if (optimizer.LastResult is { RequiresElevation: true })
            {
                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run(
                    LocalizationService.Get("Ram_FooterAdminRequired"))
                {
                    Foreground = RamWarnBrush
                });
                return;
            }

            var settings = SettingsService.Instance.Current;
            if ((!settings.RamFreeWorkingSets && !settings.RamClearStandby) ||
                optimizer.LastResult is { NoScopeSelected: true })
            {
                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run(
                    LocalizationService.Get("Ram_FooterNoScope"))
                {
                    Foreground = RamWarnBrush
                });
                return;
            }

            if (optimizer.LastSuccessfulResult is { } lastOk)
            {
                string prefix = LocalizationService.Get("Ram_LastFreedPrefix").TrimEnd();
                string atTime = LocalizationService.Format(
                    "Ram_LastFreedAt",
                    lastOk.TimestampLocal.ToString("hh:mm tt", CultureInfo.CurrentCulture)).TrimStart();

                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run($"{prefix} ")
                {
                    Foreground = RamFooterTextBrush
                });
                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run(
                    MemoryTelemetryService.FormatBytes(lastOk.FreedBytes))
                {
                    Foreground = RamAccentBrush,
                    FontWeight = FontWeights.SemiBold
                });
                TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run($" {atTime}")
                {
                    Foreground = RamFooterTextBrush
                });
                return;
            }

            TxtTipFooter.Inlines.Add(new System.Windows.Documents.Run(
                LocalizationService.Get("Ram_FooterHint"))
            {
                Foreground = RamFooterMutedBrush
            });
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnInstallPawnIo_Click(object sender, RoutedEventArgs e)
        {
            HardwareMonitorService.InstallOrOpenPawnIo();
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

        private void BtnCpuTempCores_Click(object sender, RoutedEventArgs e)
        {
            if (CpuCoresPopup.IsOpen)
            {
                CpuCoresPopup.IsOpen = false;
                return;
            }

            UpdateCpuCoreItems(HardwareMonitorService.Instance.CurrentSnapshot);
            CpuCoresPopup.IsOpen = true;
        }

        private void BtnCpuTempCores_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateCpuTempFocusRing();
            if (!ShowKeyboardFocusCues)
            {
                return;
            }

            UpdateCpuCoreItems(HardwareMonitorService.Instance.CurrentSnapshot);
            CpuCoresPopup.IsOpen = true;
        }

        private void BtnCpuTempCores_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateCpuTempFocusRing();
            CpuCoresPopup.IsOpen = false;
        }

        /// <summary>
        /// The ring marks keyboard focus only. A mouse click also focuses the button, but the
        /// white outline there reads as a stray border, so cues stay keyboard-only.
        /// </summary>
        private void UpdateCpuTempFocusRing()
        {
            if (BtnCpuTempCores.Template?.FindName("TempFocusBorder", BtnCpuTempCores) is not Border ring)
            {
                return;
            }

            if (ShowKeyboardFocusCues && BtnCpuTempCores.IsKeyboardFocused)
            {
                ring.BorderThickness = new Thickness(2);
                ring.SetResourceReference(Border.BorderBrushProperty, "TextPrimaryBrush");
            }
            else
            {
                ring.BorderThickness = new Thickness(0);
                ring.ClearValue(Border.BorderBrushProperty);
            }
        }

        private void UpdateCpuCoreItems(HardwareSnapshot snapshot)
        {
            TxtCpuPackagePowerPopup.Text = FormatPackagePower(snapshot.CpuPackageWatts);

            var cores = snapshot.CpuCoresTelemetry;
            if (cores.Count == 0)
            {
                CoreItems.Clear();
                TxtCpuCoresUnavailable.Visibility = Visibility.Visible;
                PositionCorePopup();
                return;
            }

            TxtCpuCoresUnavailable.Visibility = Visibility.Collapsed;

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

        internal static string CompactGpuName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return "";
            }

            string trimmed = fullName.Trim();
            string[] prefixes =
            {
                "NVIDIA ",
                "AMD ",
                "Intel(R) ",
                "Intel ",
                "Qualcomm(R) ",
                "Qualcomm "
            };

            foreach (string prefix in prefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[prefix.Length..].Trim();
                }
            }

            return trimmed;
        }

        private void ApplyTelemetryVisibility(AppSettings settings)
        {
            CpuLargeChartContainer.Visibility = settings.ShowCpuGraph && !settings.CpuGraphSmall
                ? Visibility.Visible
                : Visibility.Collapsed;
            CpuSmallChartRow.Visibility = settings.ShowCpuGraph && settings.CpuGraphSmall
                ? Visibility.Visible
                : Visibility.Collapsed;
            CpuSmallChartSeparator.Visibility = CpuSmallChartRow.Visibility;

            CpuSpeedRow.Visibility = settings.ShowCpuClock
                ? Visibility.Visible
                : Visibility.Collapsed;
            CpuSpeedSeparator.Visibility = CpuSpeedRow.Visibility;
            SsdRowsContainer.Visibility = settings.ShowDiskTemp
                ? Visibility.Visible
                : Visibility.Collapsed;

            TxtCpuPackageWatts.Visibility = settings.ShowCpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtCpuLoad.Visibility = settings.ShowCpuLoad
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
            TxtGpuLoad.Visibility = settings.ShowGpuLoad
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
            GpuDetailsRow.Visibility = settings.ShowGpuDetails
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

            double desiredOffsetX = BtnCpuTempCores.ActualWidth - popupWidth;

            if (TryGetMonitorWorkArea(out Rect workArea))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                Point targetScreen = BtnCpuTempCores.PointToScreen(new Point(0, 0));
                double targetLeft = targetScreen.X / dpi.DpiScaleX;
                double targetTop = targetScreen.Y / dpi.DpiScaleY;

                double popupLeft = targetLeft + desiredOffsetX;
                double minLeft = workArea.Left + 4;
                double maxLeft = Math.Max(minLeft, workArea.Right - popupWidth - 4);
                popupLeft = Math.Clamp(popupLeft, minLeft, maxLeft);

                // The bubble sits above the digits. When the dashboard is docked at the top of the
                // screen there is no room for it, so shift it down just enough to stay on screen.
                double popupTop = targetTop - popupHeight - 6;
                double minTop = workArea.Top + 4;
                double maxTop = Math.Max(minTop, workArea.Bottom - popupHeight - 4);
                popupTop = Math.Clamp(popupTop, minTop, maxTop);

                ApplyCorePopupAt(popupLeft, popupTop);
                return;
            }

            double fallbackTop = BtnCpuTempCores.PointToScreen(new Point(0, 0)).Y -
                popupHeight - 6;
            ApplyCorePopupAt(
                BtnCpuTempCores.PointToScreen(new Point(0, 0)).X + desiredOffsetX,
                Math.Max(0, fallbackTop));
        }

        /// <summary>Absolute placement in screen pixels, so the bubble never flips or clips.</summary>
        private void ApplyCorePopupAt(double left, double top)
        {
            CpuCoresPopup.Placement = PlacementMode.Absolute;
            CpuCoresPopup.HorizontalOffset = left;
            CpuCoresPopup.VerticalOffset = top;
            // The bubble sits above the digits, so the tail points down at them.
            CpuCoresCaretUp.Visibility = Visibility.Collapsed;
            CpuCoresCaretDown.Visibility = Visibility.Visible;
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
            _lastActiveUtilitySignature = "";
            ApplySettings();
            if (PowerProfileMenu?.IsOpen == true)
            {
                BuildPowerProfileMenu();
            }
            UpdateTheme(PowerPlanService.Instance.ActiveProfile, false);
            UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ssdRowSignature = null;
                UpdateTheme(PowerPlanService.Instance.ActiveProfile, false);
                UpdateTitleIconStates();
                UpdateTelemetryUI(HardwareMonitorService.Instance.CurrentSnapshot);
            }));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
