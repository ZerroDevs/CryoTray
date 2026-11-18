using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CryoTray.Models;
using CryoTray.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace CryoTray.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _isBinding = true;
        private bool _isClosed;
        private bool _updateBusy;
        private ReleaseInfo? _availableUpdate;
        private CancellationTokenSource? _updateCancellation;
        private string _updateStatusKey = "Settings_UpdateDefault";
        private object?[] _updateStatusArgs = Array.Empty<object?>();
        private string? _updateStatusLiteral;
        private string _updateButtonKey = "Settings_CheckUpdates";
        private string _hotkeyModifiers = "Ctrl+Shift";
        private string _hotkeyKey = "C";
        private string _ramHotkeyModifiers = "Ctrl+Alt";
        private string _ramHotkeyKey = "M";
        private string _coolingOnFrameColor = App.DefaultCoolingOnFrameColor;
        private string _coolingOffFrameColor = App.DefaultCoolingOffFrameColor;
        private readonly List<DiskDriveControlRow> _diskDriveRows = new();

        private sealed class DiskDriveControlRow
        {
            public required string Key { get; init; }
            public required CheckBox VisibleCheckBox { get; init; }
            public required CheckBox GraphCheckBox { get; init; }
            public required ComboBox TimeframeCombo { get; init; }
            public required Grid GraphSubRow { get; init; }
        }

        private sealed class Win32WindowOwner : WinForms.IWin32Window
        {
            public IntPtr Handle { get; }
            public Win32WindowOwner(IntPtr handle) => Handle = handle;
        }

        private static readonly Brush WhitelistTextBrush =
            CreateFrozenBrush(Color.FromRgb(224, 224, 224));
        private static readonly Brush HotkeyDefaultBorderBrush =
            CreateFrozenBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
        private static readonly Brush HotkeyActiveBorderBrush =
            CreateFrozenBrush(Color.FromRgb(0x2B, 0x88, 0xD8));
        private static readonly Brush HotkeyHintBrush =
            CreateFrozenBrush(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly Brush HotkeyWarningBrush =
            CreateFrozenBrush(Color.FromRgb(0xE5, 0x98, 0x7E));


        public bool OpenAboutOnLoad { get; set; }
        public bool OpenAutopilotOnLoad { get; set; }
        public bool OpenMemoryOnLoad { get; set; }

        public void SelectAboutTab()
        {
            TabAbout.IsChecked = true;
        }

        public void SelectAutopilotTab()
        {
            TabAutopilot.IsChecked = true;
        }

        public void SelectMemoryTab()
        {
            TabMemory.IsChecked = true;
        }

        public SettingsWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            Loaded += SettingsWindow_Loaded;
            Activated += SettingsWindow_Activated;
            Closed += SettingsWindow_Closed;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _isClosed = true;
            Activated -= SettingsWindow_Activated;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnSettingsTelemetryUpdated;
            SettingsService.Instance.SettingsChanged -= OnExternalSettingsChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateCancellation = null;
            Content = null;
            DataContext = null;
        }

        private void SettingsWindow_Activated(object? sender, EventArgs e)
        {
            RefreshPawnIoBanner();
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUI();
            if (OpenAboutOnLoad)
            {
                TabAbout.IsChecked = true;
            }
            else if (OpenAutopilotOnLoad)
            {
                TabAutopilot.IsChecked = true;
            }
            else if (OpenMemoryOnLoad)
            {
                TabMemory.IsChecked = true;
            }

            ApplyLocalizedText();
            RefreshPawnIoBanner();
            RefreshRollbackUi();
            HardwareMonitorService.Instance.TelemetryUpdated += OnSettingsTelemetryUpdated;
            SettingsService.Instance.SettingsChanged += OnExternalSettingsChanged;
            _isBinding = false;
        }

        private void OnSettingsTelemetryUpdated(HardwareSnapshot _)
        {
            if (_isClosed || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosed || PanelView == null || PanelView.Visibility != Visibility.Visible)
                {
                    return;
                }

                UpdateTrayBadgeSampleTexts();
            }));
        }

        private void OnExternalSettingsChanged()
        {
            if (_isClosed)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosed)
                {
                    return;
                }

                var s = SettingsService.Instance.Current;
                bool previous = _isBinding;
                _isBinding = true;
                try
                {
                    if (ChkOverlayClickThrough != null && ChkOverlayClickThrough.IsChecked != s.OverlayClickThrough)
                    {
                        ChkOverlayClickThrough.IsChecked = s.OverlayClickThrough;
                    }
                    if (ChkRamFreeWorkingSets != null && ChkRamFreeWorkingSets.IsChecked != s.RamFreeWorkingSets)
                    {
                        ChkRamFreeWorkingSets.IsChecked = s.RamFreeWorkingSets;
                    }
                    if (ChkRamClearStandby != null && ChkRamClearStandby.IsChecked != s.RamClearStandby)
                    {
                        ChkRamClearStandby.IsChecked = s.RamClearStandby;
                    }
                    if (CmbCpuGraphTimeframe != null && ReadSelectedTimeframeMinutes(CmbCpuGraphTimeframe) != s.CpuGraphTimeframeMinutes)
                    {
                        SelectTimeframeCombo(CmbCpuGraphTimeframe, s.CpuGraphTimeframeMinutes);
                    }
                }
                finally
                {
                    _isBinding = previous;
                }
            }));
        }

        private void LoadSettingsIntoUI()
        {
            var s = SettingsService.Instance.Current;

            LoadNormalPowerPlans();

            // Language
            CmbLanguage.SelectedIndex = s.Language == "العربية" ? 1 : 0;

            // Temperature Units
            CmbTempUnits.SelectedIndex = s.TemperatureUnit == "Fahrenheit" ? 1 : 0;

            // Checkboxes
            ChkShowComputerName.IsChecked = s.ShowComputerName;
            bool taskConfigured = StartupTaskService.IsStartupConfigured();
            if (s.LaunchOnStartup && !taskConfigured)
            {
                StartupTaskService.EnsureSynchronized(true);
                taskConfigured = StartupTaskService.IsStartupConfigured();
                if (!taskConfigured)
                {
                    s.LaunchOnStartup = false;
                }
            }
            ChkLaunchStartup.IsChecked = s.LaunchOnStartup && taskConfigured;
            ChkLaunchBackground.IsChecked = s.LaunchInBackground;
            ChkEnableCoolingOnLaunch.IsChecked = s.EnableCoolingOnLaunch;
            ChkEnableCoolingOnBattery.IsChecked = s.EnableCoolingOnBattery;
            ChkDisableCoolingOnExit.IsChecked = s.DisableCoolingModeOnExit;
            ChkPinToTaskbar.IsChecked = s.PinToTaskbar;
            ChkAlwaysOnTop.IsChecked = s.AlwaysOnTop;
            ChkHideOnFocusLoss.IsChecked = s.HideOnFocusLoss;

            ChkUseHotkey.IsChecked = s.UseHotkey;
            _hotkeyKey = string.IsNullOrWhiteSpace(s.HotkeyKey) ? "C" : s.HotkeyKey.Trim();
            _hotkeyModifiers = string.IsNullOrWhiteSpace(s.HotkeyKey) ? "Ctrl+Shift" : (s.HotkeyModifiers ?? "");
            TxtHotkeyShortcut.Text = GlobalHotkeyService.FormatShortcutDisplay(_hotkeyModifiers, _hotkeyKey);
            TxtHotkeyShortcut.IsEnabled = s.UseHotkey;
            ChkMuteTemperatureAlerts.IsChecked = s.MuteTemperatureAlerts;
            ChkAutomaticUpdates.IsChecked = s.AutomaticUpdateChecks;

            // View Tab
            ChkShowGpuBadge.IsChecked = s.ShowGpuBadge;
            RdoGpuBlackDigits.IsChecked = !s.GpuTrayWhiteDigits;
            RdoGpuWhiteDigits.IsChecked = s.GpuTrayWhiteDigits;
            ChkShowCpuBadge.IsChecked = s.ShowCpuBadge;
            RdoCpuBlackDigits.IsChecked = !s.CpuTrayWhiteDigits;
            RdoCpuWhiteDigits.IsChecked = s.CpuTrayWhiteDigits;
            ChkMergeTrayIcons.IsChecked = s.MergeTrayIcons;
            _coolingOnFrameColor = App.NormalizeHexColor(App.ResolveFrameColor(s.CoolingOnFrameColor, App.DefaultCoolingOnFrameColor));
            _coolingOffFrameColor = App.NormalizeHexColor(App.ResolveFrameColor(s.CoolingOffFrameColor, App.DefaultCoolingOffFrameColor));
            UpdateFrameColorSwatchesUi();
            UpdateTrayBadgeSampleTexts();
            ChkAnchorToTray.IsChecked = s.AnchorToTray;
            ChkShowRamRow.IsChecked = s.ShowRamRow;
            ChkShowOverlayRow.IsChecked = s.ShowOverlayRow;
            ChkShowAutopilotRow.IsChecked = s.ShowAutopilotRow;
            ChkOverlayClickThrough.IsChecked = s.OverlayClickThrough;
            ChkOverlayCpuTemp.IsChecked = s.OverlayShowCpuTemp;
            ChkOverlayCpuLoad.IsChecked = s.OverlayShowCpuLoad;
            ChkOverlayCpuPower.IsChecked = s.OverlayShowCpuPower;
            ChkOverlayCpuClock.IsChecked = s.OverlayShowCpuClock;
            ChkOverlayGpuTemp.IsChecked = s.OverlayShowGpuTemp;
            ChkOverlayGpuLoad.IsChecked = s.OverlayShowGpuLoad;
            ChkOverlayGpuPower.IsChecked = s.OverlayShowGpuPower;
            ChkOverlayShowRam.IsChecked = s.OverlayShowRam;
            SldOverlayOpacity.Value = Math.Clamp(s.OverlayOpacityPercent, 0, 100);
            TxtOverlayOpacity.Text = $"{(int)Math.Round(SldOverlayOpacity.Value)}%";
            ChkShowCpuPower.IsChecked = s.ShowCpuPower;
            ChkShowGpuPower.IsChecked = s.ShowGpuPower;
            ChkShowGpuTemp.IsChecked = s.ShowGpuTemp;
            ChkShowGpuName.IsChecked = s.ShowGpuName;
            ChkShowGpuHotspot.IsChecked = s.ShowGpuHotspot;
            ChkShowGpuVram.IsChecked = s.ShowGpuVram;
            ChkShowGpuFan.IsChecked = s.ShowGpuFan;

            // CPU Tab - Display & Cooling Controls
            ChkShowCpuClock.IsChecked = s.ShowCpuClock;
            ChkShowCpuBaseSpeed.IsChecked = s.ShowCpuBaseSpeed;
            ChkShowCpuGraph.IsChecked = s.ShowCpuGraph;
            SelectTimeframeCombo(CmbCpuGraphTimeframe, s.CpuGraphTimeframeMinutes);
            RdoCpuGraphLarge.IsChecked = !s.CpuGraphSmall;
            RdoCpuGraphSmall.IsChecked = s.CpuGraphSmall;
            UpdateCpuDisplayControlStates();

            CmbPerfBoostMode.SelectedIndex = s.PerfBoostMode == 1 ? 1 : 0;
            SldThrottle.Value = s.MaxCpuThrottlePercent;
            TxtThrottleVal.Text = $"{s.MaxCpuThrottlePercent}%";

            // Memory Tab
            ChkRamFreeWorkingSets.IsChecked = s.RamFreeWorkingSets;
            ChkRamClearStandby.IsChecked = s.RamClearStandby;
            ChkRamClearClipboard.IsChecked = s.RamClearClipboard;
            ChkRamShowNotification.IsChecked = s.RamShowNotification;
            ChkAutoRamEnabled.IsChecked = s.AutoRamOptimizeEnabled;
            SldAutoRamThreshold.Value = Math.Clamp(s.AutoRamThresholdPercent, 10, 50);
            TxtAutoRamThreshold.Text = $"{(int)SldAutoRamThreshold.Value}%";
            ChkAutoRamCpuIdle.IsChecked = s.AutoRamCpuIdleOnly;
            SelectAutoRamInterval(s.AutoRamIntervalHours);
            UpdateMemoryRuleControlStates();

            ChkUseRamHotkey.IsChecked = s.UseRamHotkey;
            _ramHotkeyKey = string.IsNullOrWhiteSpace(s.RamHotkeyKey) ? "M" : s.RamHotkeyKey.Trim();
            _ramHotkeyModifiers = string.IsNullOrWhiteSpace(s.RamHotkeyKey) ? "Ctrl+Alt" : (s.RamHotkeyModifiers ?? "");
            TxtRamHotkeyShortcut.Text = GlobalHotkeyService.FormatShortcutDisplay(_ramHotkeyModifiers, _ramHotkeyKey);
            TxtRamHotkeyShortcut.IsEnabled = s.UseRamHotkey;

            ChkAutopilotEnabled.IsChecked = s.AutopilotEnabled;
            ChkAutopilotFullscreen.IsChecked = s.AutopilotPauseOnFullscreen;
            RefreshAutopilotWhitelist();

            // CPU, disk, and GPU alert thresholds
            SldCpuThreshold.Value = s.HighTempThresholdCpu;
            ChkShowDiskTemp.IsChecked = s.ShowDiskTemp;
            SldDiskThreshold.Value = s.HighTempThresholdDisk;
            SldGpuThreshold.Value = s.HighTempThresholdGpu;
            LoadGpuOptions();
            RefreshDiskDriveList();
            UpdateDiskControlStates();
            RefreshHotkeyFeedback();
            RefreshRamHotkeyFeedback();
            UpdateThresholdLabels();

            SetUpdateState("Settings_UpdateDefault", "Settings_CheckUpdates");
        }

        private void ApplyLocalizedText()
        {
            TxtAboutVersion.Text = LocalizationService.Format("Settings_AboutVersion", UpdateService.CurrentVersion);
            UpdateThresholdLabels();
            UpdateTrayBadgeSampleTexts();
            ApplyUpdateState();
            RefreshAutopilotWhitelist();
            RefreshDiskDriveListSafely();
            RefreshHotkeyFeedback();
            RefreshRamHotkeyFeedback();
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            RefreshGpuOptionsSafely();
            ApplyLocalizedText();
            RefreshPawnIoBanner();
            RefreshRollbackUi();
        }

        private void RefreshGpuOptionsSafely()
        {
            bool previousBinding = _isBinding;
            _isBinding = true;
            try
            {
                LoadGpuOptions();
            }
            finally
            {
                _isBinding = previousBinding;
            }
        }

        private void RefreshPawnIoBanner()
        {
            if (PawnIoSettingsBanner == null || _isClosed)
            {
                return;
            }

            PawnIoSettingsBanner.Visibility = HardwareMonitorService.IsPawnIoInstalled()
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnInstallPawnIo_Click(object sender, RoutedEventArgs e)
        {
            HardwareMonitorService.InstallOrOpenPawnIo();
        }

        private void UpdateThresholdLabels()
        {
            if (TxtCpuThreshold == null || TxtDiskThreshold == null || TxtGpuThreshold == null ||
                SldCpuThreshold == null || SldDiskThreshold == null || SldGpuThreshold == null)
            {
                return;
            }

            TxtCpuThreshold.Text = FormatThreshold((int)SldCpuThreshold.Value);
            TxtDiskThreshold.Text = FormatThreshold((int)SldDiskThreshold.Value);
            TxtGpuThreshold.Text = FormatThreshold((int)SldGpuThreshold.Value);
        }

        private static string FormatThreshold(int celsius)
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            int value = isFahrenheit ? (int)Math.Round((celsius * 9 / 5.0) + 32) : celsius;
            return $"{value}°{(isFahrenheit ? "F" : "C")}";
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void RefreshAutopilotWhitelist()
        {
            if (PnlAutopilotWhitelist == null || TxtAutopilotEmpty == null ||
                TxtAutopilotCount == null)
            {
                return;
            }

            PnlAutopilotWhitelist.Children.Clear();
            var whitelist = SettingsService.Instance.Current.AutopilotWhitelist ??
                new List<string>();
            var entries = whitelist
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToList();

            foreach (string entry in entries)
            {
                var row = new Border
                {
                    Background = CreateFrozenBrush(Color.FromRgb(37, 37, 37)),
                    BorderBrush = CreateFrozenBrush(Color.FromRgb(48, 48, 48)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 5, 4, 5)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

                var nameText = new TextBlock
                {
                    Text = entry,
                    Foreground = WhitelistTextBrush,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = entry,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(nameText, 0);

                var removeButton = new Button
                {
                    Content = "×",
                    Tag = entry,
                    Style = (Style)FindResource("AutopilotRemoveButton"),
                    ToolTip = LocalizationService.Get("Settings_AutopilotRemove")
                };
                AutomationProperties.SetName(
                    removeButton,
                    LocalizationService.Format("Settings_AutopilotRemoveAutomation", entry));
                removeButton.Click += BtnRemoveAutopilotApp_Click;
                Grid.SetColumn(removeButton, 1);

                rowGrid.Children.Add(nameText);
                rowGrid.Children.Add(removeButton);
                row.Child = rowGrid;
                PnlAutopilotWhitelist.Children.Add(row);
            }

            TxtAutopilotEmpty.Visibility = entries.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtAutopilotCount.Text = entries.Count.ToString();
        }

        private void BtnRemoveAutopilotApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string entry })
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            settings.AutopilotWhitelist ??= new List<string>();
            settings.AutopilotWhitelist.RemoveAll(existing =>
                string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));
            SettingsService.Instance.Save();
            RefreshAutopilotWhitelist();
        }

        private void BtnAddRunningApp_Click(object sender, RoutedEventArgs e)
        {
            if (CmbRunningApps.SelectedItem is RunningAppOption runningApp)
            {
                AddAutopilotApp(runningApp.ProcessName);
            }
        }

        private void BtnBrowseAutopilotApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Settings_AutopilotBrowseTitle"),
                Filter = LocalizationService.Get("Common_ExeFilter"),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                AddAutopilotApp(Path.GetFileName(dialog.FileName));
            }
        }

        private void AddAutopilotApp(string entry)
        {
            string displayName = Path.GetFileName(entry.Trim());
            string processName = Path.GetFileNameWithoutExtension(displayName);
            if (displayName.Length == 0 || processName.Length == 0)
            {
                return;
            }

            string currentExecutable = Path.GetFileNameWithoutExtension(
                Environment.ProcessPath ?? "");
            if (string.Equals(processName, currentExecutable,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            settings.AutopilotWhitelist ??= new List<string>();
            bool exists = settings.AutopilotWhitelist.Any(existing =>
                !string.IsNullOrWhiteSpace(existing) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(existing.Trim()),
                    processName,
                    StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                settings.AutopilotWhitelist.Add(displayName);
                SettingsService.Instance.Save();
                RefreshAutopilotWhitelist();
            }
        }

        private void RefreshRunningApps()
        {
            if (CmbRunningApps == null || _isClosed)
            {
                return;
            }

            string? selectedProcess = (CmbRunningApps.SelectedItem as RunningAppOption)
                ?.ProcessName;
            var apps = new Dictionary<string, RunningAppOption>(
                StringComparer.OrdinalIgnoreCase);

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Running app enumeration error: {ex.Message}");
                return;
            }

            try
            {
                foreach (var process in processes)
                {
                    if (process == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (process.Id == Environment.ProcessId)
                        {
                            continue;
                        }

                        string title = process.MainWindowTitle;
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            continue;
                        }

                        string processName = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(processName) ||
                            apps.ContainsKey(processName))
                        {
                            continue;
                        }

                        if (title.Length > 64)
                        {
                            title = title[..61] + "...";
                        }

                        apps[processName] = new RunningAppOption
                        {
                            ProcessName = processName,
                            DisplayName = $"{processName} - {title}"
                        };
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            var list = apps.Values
                .OrderBy(app => app.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            CmbRunningApps.ItemsSource = list;

            if (selectedProcess != null)
            {
                CmbRunningApps.SelectedItem = list.FirstOrDefault(app =>
                    string.Equals(app.ProcessName, selectedProcess,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (CmbRunningApps.SelectedItem == null && list.Count > 0)
            {
                CmbRunningApps.SelectedIndex = 0;
            }
        }

        private sealed class RunningAppOption
        {
            public string ProcessName { get; init; } = "";
            public string DisplayName { get; init; } = "";
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null) return;

            SettingsContentScroll?.ScrollToTop();
            PanelGeneral.Visibility = Visibility.Collapsed;
            PanelView.Visibility = Visibility.Collapsed;
            PanelCpu.Visibility = Visibility.Collapsed;
            PanelMemory.Visibility = Visibility.Collapsed;
            PanelDisk.Visibility = Visibility.Collapsed;
            PanelGpu.Visibility = Visibility.Collapsed;
            PanelAutopilot.Visibility = Visibility.Collapsed;
            PanelAbout.Visibility = Visibility.Collapsed;

            if (TabGeneral.IsChecked == true) PanelGeneral.Visibility = Visibility.Visible;
            else if (TabView.IsChecked == true)
            {
                PanelView.Visibility = Visibility.Visible;
                UpdateTrayBadgeSampleTexts();
                UpdateFrameColorSwatchesUi();
            }
            else if (TabCpu.IsChecked == true) PanelCpu.Visibility = Visibility.Visible;
            else if (TabMemory.IsChecked == true) PanelMemory.Visibility = Visibility.Visible;
            else if (TabDisk.IsChecked == true)
            {
                PanelDisk.Visibility = Visibility.Visible;
                RefreshDiskDriveListSafely();
            }
            else if (TabGpu.IsChecked == true)
            {
                PanelGpu.Visibility = Visibility.Visible;
                RefreshGpuOptionsSafely();
            }
            else if (TabAutopilot.IsChecked == true)
            {
                PanelAutopilot.Visibility = Visibility.Visible;
                RefreshRunningApps();
            }
            else if (TabAbout.IsChecked == true) PanelAbout.Visibility = Visibility.Visible;
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isBinding) return;
            SaveUiIntoSettings();
        }

        private void SaveUiIntoSettings()
        {
            var s = SettingsService.Instance.Current;

            s.Language = CmbLanguage.SelectedIndex == 1 ? "العربية" : "English";
            s.TemperatureUnit = CmbTempUnits.SelectedIndex == 1 ? "Fahrenheit" : "Celsius";

            s.ShowComputerName = ChkShowComputerName.IsChecked == true;
            s.LaunchOnStartup = ChkLaunchStartup.IsChecked == true;
            s.LaunchInBackground = ChkLaunchBackground.IsChecked == true;
            s.EnableCoolingOnLaunch = ChkEnableCoolingOnLaunch.IsChecked == true;
            s.EnableCoolingOnBattery = ChkEnableCoolingOnBattery.IsChecked == true;
            s.DisableCoolingModeOnExit = ChkDisableCoolingOnExit.IsChecked == true;
            s.PinToTaskbar = ChkPinToTaskbar.IsChecked == true;
            s.AlwaysOnTop = ChkAlwaysOnTop.IsChecked == true;
            s.HideOnFocusLoss = ChkHideOnFocusLoss.IsChecked == true;

            s.UseHotkey = ChkUseHotkey.IsChecked == true;
            s.HotkeyModifiers = _hotkeyModifiers;
            s.HotkeyKey = _hotkeyKey;
            s.MuteTemperatureAlerts = ChkMuteTemperatureAlerts.IsChecked == true;
            s.AutomaticUpdateChecks = ChkAutomaticUpdates.IsChecked == true;

            s.ShowGpuBadge = ChkShowGpuBadge.IsChecked == true;
            s.GpuTrayWhiteDigits = RdoGpuWhiteDigits.IsChecked == true;
            s.ShowCpuBadge = ChkShowCpuBadge.IsChecked == true;
            s.CpuTrayWhiteDigits = RdoCpuWhiteDigits.IsChecked == true;
            s.MergeTrayIcons = ChkMergeTrayIcons.IsChecked == true;
            s.CoolingOnFrameColor = _coolingOnFrameColor;
            s.CoolingOffFrameColor = _coolingOffFrameColor;
            s.AnchorToTray = ChkAnchorToTray.IsChecked == true;
            s.ShowRamRow = ChkShowRamRow.IsChecked == true;
            s.ShowOverlayRow = ChkShowOverlayRow.IsChecked == true;
            s.ShowAutopilotRow = ChkShowAutopilotRow.IsChecked == true;
            s.OverlayClickThrough = ChkOverlayClickThrough.IsChecked == true;
            s.OverlayShowCpuTemp = ChkOverlayCpuTemp.IsChecked == true;
            s.OverlayShowCpuLoad = ChkOverlayCpuLoad.IsChecked == true;
            s.OverlayShowCpuPower = ChkOverlayCpuPower.IsChecked == true;
            s.OverlayShowCpuClock = ChkOverlayCpuClock.IsChecked == true;
            s.OverlayShowGpuTemp = ChkOverlayGpuTemp.IsChecked == true;
            s.OverlayShowGpuLoad = ChkOverlayGpuLoad.IsChecked == true;
            s.OverlayShowGpuPower = ChkOverlayGpuPower.IsChecked == true;
            s.OverlayShowRam = ChkOverlayShowRam.IsChecked == true;
            s.OverlayOpacityPercent = (int)Math.Round(SldOverlayOpacity.Value);
            s.ShowCpuPower = ChkShowCpuPower.IsChecked == true;
            s.ShowGpuPower = ChkShowGpuPower.IsChecked == true;
            s.ShowGpuTemp = ChkShowGpuTemp.IsChecked == true;
            s.ShowGpuName = ChkShowGpuName.IsChecked == true;
            s.ShowGpuHotspot = ChkShowGpuHotspot.IsChecked == true;
            s.ShowGpuVram = ChkShowGpuVram.IsChecked == true;
            s.ShowGpuFan = ChkShowGpuFan.IsChecked == true;

            s.ShowCpuClock = ChkShowCpuClock.IsChecked == true;
            s.ShowCpuBaseSpeed = ChkShowCpuBaseSpeed.IsChecked == true;
            s.ShowCpuGraph = ChkShowCpuGraph.IsChecked == true;
            s.CpuGraphSmall = RdoCpuGraphSmall.IsChecked == true;
            s.CpuGraphTimeframeMinutes = ReadSelectedTimeframeMinutes(CmbCpuGraphTimeframe);

            s.NormalPowerPlanGuid = CmbNormalPowerPlan.SelectedItem is PowerSchemeInfo normalPlan
                ? normalPlan.Guid.ToString()
                : PowerPlanService.BalancedGuid.ToString();

            s.PerfBoostMode = CmbPerfBoostMode.SelectedIndex == 1 ? 1 : 0;
            s.MaxCpuThrottlePercent = (int)SldThrottle.Value;

            s.RamFreeWorkingSets = ChkRamFreeWorkingSets.IsChecked == true;
            s.RamClearStandby = ChkRamClearStandby.IsChecked == true;
            s.RamClearClipboard = ChkRamClearClipboard.IsChecked == true;
            s.RamShowNotification = ChkRamShowNotification.IsChecked == true;
            s.AutoRamOptimizeEnabled = ChkAutoRamEnabled.IsChecked == true;
            s.AutoRamThresholdPercent = (int)Math.Round(SldAutoRamThreshold.Value);
            s.AutoRamCpuIdleOnly = ChkAutoRamCpuIdle.IsChecked == true;
            s.AutoRamIntervalHours = ReadSelectedAutoRamIntervalHours();
            s.UseRamHotkey = ChkUseRamHotkey.IsChecked == true;
            s.RamHotkeyModifiers = _ramHotkeyModifiers;
            s.RamHotkeyKey = _ramHotkeyKey;

            s.AutopilotEnabled = ChkAutopilotEnabled.IsChecked == true;
            s.AutopilotPauseOnFullscreen = ChkAutopilotFullscreen.IsChecked == true;

            s.HighTempThresholdCpu = (int)SldCpuThreshold.Value;
            s.ShowDiskTemp = ChkShowDiskTemp.IsChecked == true;
            s.HighTempThresholdDisk = (int)SldDiskThreshold.Value;
            s.HighTempThresholdGpu = (int)SldGpuThreshold.Value;
            s.PreferredGpuIdentifier = CmbPrimaryGpu?.SelectedValue as string ?? "";

            if (_diskDriveRows.Count > 0)
            {
                s.ExcludedDrives ??= new List<string>();
                s.DiskGraphDrives ??= new List<string>();
                s.DiskGraphTimeframes ??= new Dictionary<string, int>();

                var renderedKeys = new HashSet<string>(_diskDriveRows.Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
                s.ExcludedDrives.RemoveAll(k => renderedKeys.Contains(k));
                s.DiskGraphDrives.RemoveAll(k => renderedKeys.Contains(k));

                foreach (var row in _diskDriveRows)
                {
                    if (row.VisibleCheckBox.IsChecked != true)
                    {
                        s.ExcludedDrives.Add(row.Key);
                    }
                    if (row.GraphCheckBox.IsChecked == true)
                    {
                        s.DiskGraphDrives.Add(row.Key);
                    }
                    s.DiskGraphTimeframes[row.Key] = ReadSelectedTimeframeMinutes(row.TimeframeCombo);
                }
            }

            SettingsService.Instance.Save();

            // Apply live power plan changes
            PowerPlanService.Instance.ApplyCpuCoolingParameters(s.MaxCpuThrottlePercent, s.PerfBoostMode);
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void BtnTestAlert_Click(object sender, RoutedEventArgs e)
        {
            (System.Windows.Application.Current as App)?.ShowTestNotification();
        }

        private void CmbTempUnits_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding)
            {
                SaveUiIntoSettings();
                UpdateThresholdLabels();
                UpdateTrayBadgeSampleTexts();
            }
        }

        private void ChkTrayBadge_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTrayBadgeSampleTexts();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void UpdateTrayBadgeSampleTexts()
        {
            if (TxtSampleGpuBlack == null || TxtSampleCpuOffBlack == null)
            {
                return;
            }

            if (PnlGpuBadgeDigits != null && ChkShowGpuBadge != null)
            {
                PnlGpuBadgeDigits.IsEnabled = ChkShowGpuBadge.IsChecked == true;
            }
            if (PnlCpuBadgeDigits != null && ChkShowCpuBadge != null)
            {
                PnlCpuBadgeDigits.IsEnabled = ChkShowCpuBadge.IsChecked == true;
            }

            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            var snap = HardwareMonitorService.Instance.CurrentSnapshot;

            string cpuSample = double.IsFinite(snap.CpuTemp) && snap.CpuTemp > 0
                ? ((int)Math.Round(isFahrenheit ? (snap.CpuTemp * 9 / 5) + 32 : snap.CpuTemp)).ToString(CultureInfo.InvariantCulture)
                : "--";
            string gpuSample = double.IsFinite(snap.GpuTemp) && snap.GpuTemp > 0
                ? ((int)Math.Round(isFahrenheit ? (snap.GpuTemp * 9 / 5) + 32 : snap.GpuTemp)).ToString(CultureInfo.InvariantCulture)
                : "--";

            TxtSampleCpuOffBlack.Text = cpuSample;
            TxtSampleCpuOnBlack.Text = cpuSample;
            TxtSampleCpuOffWhite.Text = cpuSample;
            TxtSampleCpuOnWhite.Text = cpuSample;
            TxtSampleGpuBlack.Text = gpuSample;
            TxtSampleGpuWhite.Text = gpuSample;
        }

        private void BtnColorCoolingOn_Click(object sender, RoutedEventArgs e)
        {
            PickFrameColor(_coolingOnFrameColor, App.DefaultCoolingOnFrameColor, hex => _coolingOnFrameColor = hex);
        }

        private void BtnColorCoolingOff_Click(object sender, RoutedEventArgs e)
        {
            PickFrameColor(_coolingOffFrameColor, App.DefaultCoolingOffFrameColor, hex => _coolingOffFrameColor = hex);
        }

        private void BtnResetColorCoolingOn_Click(object sender, RoutedEventArgs e)
        {
            _coolingOnFrameColor = App.DefaultCoolingOnFrameColor;
            UpdateFrameColorSwatchesUi();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void BtnResetColorCoolingOff_Click(object sender, RoutedEventArgs e)
        {
            _coolingOffFrameColor = App.DefaultCoolingOffFrameColor;
            UpdateFrameColorSwatchesUi();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void PickFrameColor(string currentHex, string fallbackHex, Action<string> apply)
        {
            Color wpfCurrent = App.ResolveFrameColor(currentHex, fallbackHex);
            using var dialog = new WinForms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
                Color = System.Drawing.Color.FromArgb(wpfCurrent.R, wpfCurrent.G, wpfCurrent.B)
            };

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            WinForms.DialogResult result = hwnd != IntPtr.Zero
                ? dialog.ShowDialog(new Win32WindowOwner(hwnd))
                : dialog.ShowDialog();

            if (result == WinForms.DialogResult.OK)
            {
                Color picked = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                apply(App.NormalizeHexColor(picked));
                UpdateFrameColorSwatchesUi();
                if (!_isBinding) SaveUiIntoSettings();
            }
        }

        private void UpdateFrameColorSwatchesUi()
        {
            if (BtnColorCoolingOn == null || BtnColorCoolingOff == null)
            {
                return;
            }

            BtnColorCoolingOn.Background = CreateFrozenBrush(
                App.ResolveFrameColor(_coolingOnFrameColor, App.DefaultCoolingOnFrameColor));
            BtnColorCoolingOff.Background = CreateFrozenBrush(
                App.ResolveFrameColor(_coolingOffFrameColor, App.DefaultCoolingOffFrameColor));

            if (BtnResetColorCoolingOn != null)
            {
                BtnResetColorCoolingOn.Visibility = string.Equals(
                    _coolingOnFrameColor, App.DefaultCoolingOnFrameColor, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            if (BtnResetColorCoolingOff != null)
            {
                BtnResetColorCoolingOff.Visibility = string.Equals(
                    _coolingOffFrameColor, App.DefaultCoolingOffFrameColor, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void ChkCpuDisplay_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCpuDisplayControlStates();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void CmbCpuGraphTimeframe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void UpdateCpuDisplayControlStates()
        {
            if (ChkShowCpuClock == null || ChkShowCpuBaseSpeed == null ||
                ChkShowCpuGraph == null || CmbCpuGraphTimeframe == null || PnlCpuGraphSize == null)
            {
                return;
            }

            ChkShowCpuBaseSpeed.IsEnabled = ChkShowCpuClock.IsChecked == true;
            bool graphEnabled = ChkShowCpuGraph.IsChecked == true;
            CmbCpuGraphTimeframe.IsEnabled = graphEnabled;
            PnlCpuGraphSize.IsEnabled = graphEnabled;
        }

        private void ChkShowDiskTemp_Changed(object sender, RoutedEventArgs e)
        {
            UpdateDiskControlStates();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void UpdateDiskControlStates()
        {
            bool showDisk = ChkShowDiskTemp == null || ChkShowDiskTemp.IsChecked == true;
            if (BdrMonitoredDrives != null)
            {
                BdrMonitoredDrives.IsEnabled = showDisk;
                BdrMonitoredDrives.Opacity = showDisk ? 1.0 : 0.48;
            }

            foreach (var row in _diskDriveRows)
            {
                bool driveEnabled = showDisk && row.VisibleCheckBox.IsChecked == true;
                row.GraphSubRow.IsEnabled = driveEnabled;
                row.TimeframeCombo.IsEnabled = driveEnabled && row.GraphCheckBox.IsChecked == true;
            }
        }

        private static void SelectTimeframeCombo(ComboBox combo, int minutes)
        {
            int target = minutes switch
            {
                5 => 5,
                10 => 10,
                30 => 30,
                60 => 60,
                _ => 1
            };

            foreach (object item in combo.Items)
            {
                if (item is ComboBoxItem cbi &&
                    int.TryParse(cbi.Tag?.ToString(), out int parsed) &&
                    parsed == target)
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static int ReadSelectedTimeframeMinutes(ComboBox? combo)
        {
            if (combo?.SelectedItem is ComboBoxItem cbi &&
                int.TryParse(cbi.Tag?.ToString(), out int parsed))
            {
                return parsed switch
                {
                    5 => 5,
                    10 => 10,
                    30 => 30,
                    60 => 60,
                    _ => 1
                };
            }

            return 1;
        }

        private void CmbNormalPowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void CmbPrimaryGpu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void LoadGpuOptions()
        {
            if (CmbPrimaryGpu == null)
            {
                return;
            }

            var currentSnapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            GpuTelemetry? autoGpu = HardwareMonitorService.SelectPrimaryGpu(currentSnapshot.Gpus, "");
            string autoLabel = autoGpu != null && !string.IsNullOrWhiteSpace(autoGpu.Name)
                ? LocalizationService.Format("Settings_PrimaryGpuAutoNamed", autoGpu.Name)
                : LocalizationService.Get("Settings_PrimaryGpuAuto");

            var choices = new List<PrimaryGpuChoice>
            {
                new()
                {
                    Identifier = "",
                    DisplayName = autoLabel
                }
            };

            foreach (var option in HardwareMonitorService.Instance.GetAvailableGpus())
            {
                choices.Add(new PrimaryGpuChoice
                {
                    Identifier = option.Identifier,
                    DisplayName = option.DisplayName
                });
            }

            CmbPrimaryGpu.ItemsSource = choices;

            string savedIdentifier = SettingsService.Instance.Current.PreferredGpuIdentifier ?? "";
            PrimaryGpuChoice? selected = choices.FirstOrDefault(choice =>
                string.Equals(choice.Identifier, savedIdentifier, StringComparison.OrdinalIgnoreCase));
            CmbPrimaryGpu.SelectedItem = selected ?? choices[0];
        }

        private sealed class PrimaryGpuChoice
        {
            public string Identifier { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public override string ToString() => DisplayName;
        }

        private void LoadNormalPowerPlans()
        {
            var plans = PowerPlanService.Instance.GetInstalledSchemes()
                .Where(plan => !plan.Name.Equals(PowerPlanService.CryoTrayCoolingSchemeName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(plan => plan.Guid == PowerPlanService.BalancedGuid ? 0 : 1)
                .ThenBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            CmbNormalPowerPlan.ItemsSource = plans;

            Guid selectedGuid = PowerPlanService.Instance.GetNormalPowerPlanGuid();
            CmbNormalPowerPlan.SelectedItem = plans.FirstOrDefault(plan => plan.Guid == selectedGuid)
                ?? plans.FirstOrDefault(plan => plan.Guid == PowerPlanService.BalancedGuid);
        }

        private void ChkLaunchStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (_isBinding) return;
            bool enable = ChkLaunchStartup.IsChecked == true;
            if (!StartupTaskService.TrySetStartup(enable, out string? errorMessage))
            {
                _isBinding = true;
                ChkLaunchStartup.IsChecked = !enable;
                _isBinding = false;
                SaveUiIntoSettings();

                string detail = string.IsNullOrWhiteSpace(errorMessage) ? "" : $"\n\n{errorMessage}";
                MessageBox.Show(
                    LocalizationService.Format("Settings_StartupTaskFailed", detail).TrimEnd(),
                    LocalizationService.Get("App_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SaveUiIntoSettings();
        }

        private void ChkUseHotkey_Changed(object sender, RoutedEventArgs e)
        {
            if (_isBinding) return;
            TxtHotkeyShortcut.IsEnabled = ChkUseHotkey.IsChecked == true;
            SaveUiIntoSettings();
            RefreshHotkeyFeedback();
            RefreshRamHotkeyFeedback();
        }

        private void ChkUseRamHotkey_Changed(object sender, RoutedEventArgs e)
        {
            if (_isBinding) return;
            TxtRamHotkeyShortcut.IsEnabled = ChkUseRamHotkey.IsChecked == true;
            SaveUiIntoSettings();
            RefreshHotkeyFeedback();
            RefreshRamHotkeyFeedback();
        }

        private void ChkAutoRamEnabled_Changed(object sender, RoutedEventArgs e)
        {
            UpdateMemoryRuleControlStates();
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldAutoRamThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtAutoRamThreshold != null)
            {
                TxtAutoRamThreshold.Text = $"{(int)Math.Round(SldAutoRamThreshold.Value)}%";
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void CmbAutoRamInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void UpdateMemoryRuleControlStates()
        {
            if (ChkAutoRamEnabled == null || SldAutoRamThreshold == null || ChkAutoRamCpuIdle == null || CmbAutoRamInterval == null)
            {
                return;
            }

            bool autoEnabled = ChkAutoRamEnabled.IsChecked == true;
            SldAutoRamThreshold.IsEnabled = autoEnabled;
            ChkAutoRamCpuIdle.IsEnabled = autoEnabled;
            CmbAutoRamInterval.IsEnabled = autoEnabled;
        }

        private void SelectAutoRamInterval(int hours)
        {
            if (CmbAutoRamInterval == null)
            {
                return;
            }

            foreach (object item in CmbAutoRamInterval.Items)
            {
                if (item is ComboBoxItem cbi &&
                    int.TryParse(cbi.Tag?.ToString(), out int parsed) &&
                    parsed == hours)
                {
                    CmbAutoRamInterval.SelectedItem = cbi;
                    return;
                }
            }

            CmbAutoRamInterval.SelectedIndex = 0;
        }

        private int ReadSelectedAutoRamIntervalHours()
        {
            if (CmbAutoRamInterval?.SelectedItem is ComboBoxItem cbi &&
                int.TryParse(cbi.Tag?.ToString(), out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private void TxtHotkeyShortcut_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HandleHotkeyFocus(TxtHotkeyShortcut);
        }

        private void TxtHotkeyShortcut_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HandleHotkeyBlur(TxtHotkeyShortcut, _hotkeyModifiers, _hotkeyKey, RefreshHotkeyFeedback);
        }

        private void TxtHotkeyShortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            HandleHotkeyPreviewKeyDown(e, TxtHotkeyShortcut, ref _hotkeyModifiers, ref _hotkeyKey, () =>
            {
                RefreshHotkeyFeedback();
                RefreshRamHotkeyFeedback();
            });
        }

        private void TxtRamHotkeyShortcut_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HandleHotkeyFocus(TxtRamHotkeyShortcut);
        }

        private void TxtRamHotkeyShortcut_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HandleHotkeyBlur(TxtRamHotkeyShortcut, _ramHotkeyModifiers, _ramHotkeyKey, RefreshRamHotkeyFeedback);
        }

        private void TxtRamHotkeyShortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            HandleHotkeyPreviewKeyDown(e, TxtRamHotkeyShortcut, ref _ramHotkeyModifiers, ref _ramHotkeyKey, () =>
            {
                RefreshHotkeyFeedback();
                RefreshRamHotkeyFeedback();
            });
        }

        private static void HandleHotkeyFocus(TextBox box)
        {
            if (!box.IsEnabled)
            {
                return;
            }

            box.Text = LocalizationService.Get("Settings_HotkeyListening");
            box.BorderBrush = HotkeyActiveBorderBrush;
        }

        private static void HandleHotkeyBlur(TextBox box, string modifiers, string key, Action refreshFeedback)
        {
            box.Text = GlobalHotkeyService.FormatShortcutDisplay(modifiers, key);
            box.BorderBrush = HotkeyDefaultBorderBrush;
            refreshFeedback();
        }

        private void HandleHotkeyPreviewKeyDown(
            System.Windows.Input.KeyEventArgs e,
            TextBox box,
            ref string modifiers,
            ref string key,
            Action refreshFeedback)
        {
            if (e.Key == Key.Tab)
            {
                return;
            }

            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                box.Text = GlobalHotkeyService.FormatShortcutDisplay(modifiers, key);
                box.BorderBrush = HotkeyDefaultBorderBrush;
                Keyboard.ClearFocus();
                return;
            }

            if (GlobalHotkeyService.TryCaptureShortcut(e.Key, e.SystemKey, Keyboard.Modifiers, out string mods, out string capturedKey))
            {
                modifiers = mods;
                key = capturedKey;
                box.Text = GlobalHotkeyService.FormatShortcutDisplay(modifiers, key);
                box.BorderBrush = HotkeyDefaultBorderBrush;
                SaveUiIntoSettings();
                refreshFeedback();
            }
        }

        private void RefreshHotkeyFeedback()
        {
            if (TxtHotkeyHint == null)
            {
                return;
            }

            string formatted = GlobalHotkeyService.FormatShortcutDisplay(_hotkeyModifiers, _hotkeyKey);
            if (ChkUseHotkey.IsChecked == true && !GlobalHotkeyService.Instance.IsRegistered)
            {
                TxtHotkeyHint.Text = LocalizationService.Format("Settings_HotkeyConflict", formatted);
                TxtHotkeyHint.Foreground = HotkeyWarningBrush;
                return;
            }

            TxtHotkeyHint.Text = LocalizationService.Get("Settings_HotkeyHint");
            TxtHotkeyHint.Foreground = HotkeyHintBrush;
        }

        private void RefreshRamHotkeyFeedback()
        {
            if (TxtRamHotkeyHint == null)
            {
                return;
            }

            string ramFormatted = GlobalHotkeyService.FormatShortcutDisplay(_ramHotkeyModifiers, _ramHotkeyKey);
            string coolingFormatted = GlobalHotkeyService.FormatShortcutDisplay(_hotkeyModifiers, _hotkeyKey);

            if (ChkUseRamHotkey.IsChecked == true &&
                ChkUseHotkey.IsChecked == true &&
                string.Equals(ramFormatted, coolingFormatted, StringComparison.OrdinalIgnoreCase))
            {
                TxtRamHotkeyHint.Text = LocalizationService.Get("Settings_RamHotkeyConflictCooling");
                TxtRamHotkeyHint.Foreground = HotkeyWarningBrush;
                return;
            }

            if (ChkUseRamHotkey.IsChecked == true && !GlobalHotkeyService.Instance.IsRamHotkeyRegistered)
            {
                TxtRamHotkeyHint.Text = LocalizationService.Format("Settings_HotkeyConflict", ramFormatted);
                TxtRamHotkeyHint.Foreground = HotkeyWarningBrush;
                return;
            }

            TxtRamHotkeyHint.Text = LocalizationService.Get("Settings_HotkeyHint");
            TxtRamHotkeyHint.Foreground = HotkeyHintBrush;
        }

        private void RefreshDiskDriveListSafely()
        {
            bool previousBinding = _isBinding;
            _isBinding = true;
            try
            {
                RefreshDiskDriveList();
            }
            finally
            {
                _isBinding = previousBinding;
            }
        }

        private void RefreshDiskDriveList()
        {
            if (PnlDiskDrives == null || TxtNoDrivesDetected == null)
            {
                return;
            }

            PnlDiskDrives.Children.Clear();
            _diskDriveRows.Clear();
            var drives = HardwareMonitorService.Instance.CurrentSnapshot.StorageDrives;
            var s = SettingsService.Instance.Current;
            var excluded = s.ExcludedDrives;
            var graphDrives = s.DiskGraphDrives ?? new List<string>();
            var graphTimeframes = s.DiskGraphTimeframes ?? new Dictionary<string, int>();

            if (drives.Count == 0)
            {
                TxtNoDrivesDetected.Visibility = Visibility.Visible;
                return;
            }

            TxtNoDrivesDetected.Visibility = Visibility.Collapsed;
            var miniComboStyle = (Style)FindResource("SettingsMiniComboStyle");
            var dividerBrush = CreateFrozenBrush(Color.FromRgb(42, 42, 42));

            for (int i = 0; i < drives.Count; i++)
            {
                var drive = drives[i];
                string key = HardwareMonitorService.GetDriveExclusionKey(drive);
                string model = string.IsNullOrWhiteSpace(drive.Model)
                    ? LocalizationService.Format("Alert_DriveName", drive.Index + 1)
                    : drive.Model.Trim();
                string label = string.IsNullOrWhiteSpace(drive.DriveLetters)
                    ? model
                    : $"{model} ({drive.DriveLetters.Trim()})";

                var container = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

                var visibleCb = new CheckBox
                {
                    Content = label,
                    Tag = key,
                    IsChecked = !HardwareMonitorService.IsDriveExcluded(drive, excluded),
                    Margin = new Thickness(0, 2, 0, 4)
                };
                AutomationProperties.SetAutomationId(visibleCb, $"ChkDriveVisible_{i}");

                var subGrid = new Grid { Margin = new Thickness(28, 2, 0, 2) };
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string graphText = drive.IsSolidState == false
                    ? LocalizationService.Get("Settings_ShowHddGraph")
                    : LocalizationService.Get("Settings_ShowSsdGraph");
                bool isGraphEnabled = graphDrives.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

                var graphCb = new CheckBox
                {
                    Content = graphText,
                    IsChecked = isGraphEnabled,
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                AutomationProperties.SetAutomationId(graphCb, $"ChkDriveGraph_{i}");
                Grid.SetColumn(graphCb, 0);

                var tfCombo = new ComboBox
                {
                    Style = miniComboStyle,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                PopulateTimeframeComboItems(tfCombo);
                int savedMinutes = graphTimeframes.TryGetValue(key, out int mins) ? mins : 1;
                SelectTimeframeCombo(tfCombo, savedMinutes);
                AutomationProperties.SetAutomationId(tfCombo, $"CmbDriveTimeframe_{i}");
                AutomationProperties.SetName(tfCombo, $"{label} {graphText}");
                Grid.SetColumn(tfCombo, 1);

                subGrid.Children.Add(graphCb);
                subGrid.Children.Add(tfCombo);
                container.Children.Add(visibleCb);
                container.Children.Add(subGrid);

                if (i < drives.Count - 1)
                {
                    container.Children.Add(new System.Windows.Shapes.Rectangle
                    {
                        Height = 1,
                        Fill = dividerBrush,
                        Margin = new Thickness(0, 8, 0, 0)
                    });
                }

                visibleCb.Checked += (_, _) => { UpdateDiskControlStates(); if (!_isBinding) SaveUiIntoSettings(); };
                visibleCb.Unchecked += (_, _) => { UpdateDiskControlStates(); if (!_isBinding) SaveUiIntoSettings(); };
                graphCb.Checked += (_, _) => { UpdateDiskControlStates(); if (!_isBinding) SaveUiIntoSettings(); };
                graphCb.Unchecked += (_, _) => { UpdateDiskControlStates(); if (!_isBinding) SaveUiIntoSettings(); };
                tfCombo.SelectionChanged += (_, _) => { if (!_isBinding) SaveUiIntoSettings(); };

                _diskDriveRows.Add(new DiskDriveControlRow
                {
                    Key = key,
                    VisibleCheckBox = visibleCb,
                    GraphCheckBox = graphCb,
                    TimeframeCombo = tfCombo,
                    GraphSubRow = subGrid
                });
                PnlDiskDrives.Children.Add(container);
            }

            UpdateDiskControlStates();
        }

        private static void PopulateTimeframeComboItems(ComboBox combo)
        {
            (int minutes, string resourceKey)[] entries =
            {
                (1, "Main_Timeframe1"),
                (5, "Main_Timeframe5"),
                (10, "Main_Timeframe10"),
                (30, "Main_Timeframe30"),
                (60, "Main_Timeframe60")
            };

            foreach ((int minutes, string key) in entries)
            {
                var item = new ComboBoxItem { Tag = minutes.ToString(CultureInfo.InvariantCulture) };
                item.SetResourceReference(ContentControl.ContentProperty, key);
                combo.Items.Add(item);
            }
        }

        private void CmbPerfBoostMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldThrottle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtThrottleVal != null)
            {
                TxtThrottleVal.Text = $"{(int)SldThrottle.Value}%";
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldOverlayOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOverlayOpacity != null)
            {
                TxtOverlayOpacity.Text = $"{(int)Math.Round(SldOverlayOpacity.Value)}%";
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldCpuThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtCpuThreshold != null)
            {
                UpdateThresholdLabels();
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldDiskThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDiskThreshold != null)
            {
                UpdateThresholdLabels();
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldGpuThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtGpuThreshold != null)
            {
                UpdateThresholdLabels();
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = LocalizationService.Get("Common_JsonFilter"),
                FileName = "CryoTray_Settings.json"
            };

            if (sfd.ShowDialog() == true)
            {
                SettingsService.Instance.Export(sfd.FileName);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = LocalizationService.Get("Common_JsonFilter")
            };

            if (ofd.ShowDialog() == true)
            {
                SettingsService.Instance.Import(ofd.FileName);
                StartupTaskService.TrySetStartup(SettingsService.Instance.Current.LaunchOnStartup, out _);
                _isBinding = true;
                LoadSettingsIntoUI();
                _isBinding = false;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            // Restore defaults in settings.json
            SettingsService.Instance.Reset();
            StartupTaskService.TrySetStartup(SettingsService.Instance.Current.LaunchOnStartup, out _);

            // Restore CryoTray Cooling Windows power scheme to exact baseline .pow state
            PowerPlanService.Instance.ResetCryoTraySchemeToDefault();

            _isBinding = true;
            LoadSettingsIntoUI();
            _isBinding = false;
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_updateBusy)
            {
                return;
            }

            if (_availableUpdate != null)
            {
                await InstallUpdateAsync(_availableUpdate);
                return;
            }

            await CheckForUpdatesAsync();
        }

        private void SetUpdateState(string statusKey, string buttonKey, params object?[] args)
        {
            _updateStatusKey = statusKey;
            _updateStatusArgs = args;
            _updateStatusLiteral = null;
            _updateButtonKey = buttonKey;
            ApplyUpdateState();
        }

        private void SetUpdateError(string message)
        {
            _updateStatusLiteral = message;
            _updateButtonKey = _availableUpdate != null ? "Settings_DownloadInstall" : "Settings_CheckAgain";
            ApplyUpdateState();
        }

        private void ApplyUpdateState()
        {
            if (TxtUpdateStatus == null || BtnUpdate == null)
            {
                return;
            }

            TxtUpdateStatus.Text = _updateStatusLiteral ??
                LocalizationService.Format(_updateStatusKey, _updateStatusArgs);
            BtnUpdate.Content = LocalizationService.Get(_updateButtonKey);
        }

        private async Task CheckForUpdatesAsync()
        {
            CancellationToken cancellationToken = StartUpdateOperation();
            _updateBusy = true;
            _availableUpdate = null;
            BtnUpdate.IsEnabled = false;
            SetUpdateState("Settings_CheckingReleases", "Settings_Checking");

            try
            {
                var release = await UpdateService.Instance.GetLatestReleaseAsync(cancellationToken);

                if (_isClosed)
                {
                    return;
                }

                if (release == null)
                {
                    SetUpdateState("Settings_NoReleases", "Settings_CheckAgain");
                }
                else if (!UpdateService.IsNewer(release.Version, UpdateService.CurrentVersion))
                {
                    SetUpdateState("Settings_UpToDate", "Settings_CheckAgain", UpdateService.CurrentVersion);
                }
                else
                {
                    _availableUpdate = release;
                    SetUpdateState("Settings_Available", "Settings_DownloadInstall", release.Version);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException ex)
            {
                if (!_isClosed)
                {
                    SetUpdateError(ex.Message);
                }
            }
            catch
            {
                if (!_isClosed)
                {
                    SetUpdateState("Settings_CheckFailed", "Settings_CheckAgain");
                }
            }
            finally
            {
                if (!_isClosed)
                {
                    _updateBusy = false;
                    BtnUpdate.IsEnabled = true;
                }
            }
        }

        private async Task InstallUpdateAsync(ReleaseInfo release)
        {
            CancellationToken cancellationToken = StartUpdateOperation();
            _updateBusy = true;
            BtnUpdate.IsEnabled = false;
            bool updateLaunched = false;

            try
            {
                SetUpdateState("Settings_Downloading", "Settings_DownloadingButton", release.Version);

                var progress = new Progress<int>(percent =>
                {
                    if (!_isClosed)
                    {
                        SetUpdateState(
                            "Settings_DownloadingPercent",
                            "Settings_DownloadingButton",
                            release.Version,
                            percent);
                    }
                });

                PreparedUpdate update = await UpdateService.Instance.DownloadAsync(release, progress, cancellationToken);

                if (_isClosed)
                {
                    return;
                }

                SetUpdateState("Settings_Backup", "Settings_DownloadInstall");
                UpdateBackupService.CreateBackup(UpdateService.CurrentVersion);

                SetUpdateState("Settings_Restarting", "Settings_DownloadingButton");
                UpdateService.LaunchUpdater(update);
                updateLaunched = true;
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException ex)
            {
                if (!_isClosed)
                {
                    SetUpdateError(ex.Message);
                }
            }
            catch (IOException)
            {
                if (!_isClosed)
                {
                    SetUpdateError(LocalizationService.Get("Settings_BackupFailed"));
                }
            }
            catch
            {
                if (!_isClosed)
                {
                    SetUpdateState("Settings_DownloadFailed", "Settings_DownloadInstall");
                }
            }
            finally
            {
                if (!_isClosed && !updateLaunched)
                {
                    _updateBusy = false;
                    BtnUpdate.IsEnabled = true;
                    RefreshRollbackUi();
                }
            }
        }

        private void RefreshRollbackUi()
        {
            if (TxtRollbackStatus == null || BtnRollback == null)
            {
                return;
            }

            var backup = UpdateBackupService.GetCurrent();
            if (backup == null)
            {
                TxtRollbackStatus.Visibility = Visibility.Collapsed;
                BtnRollback.Visibility = Visibility.Collapsed;
                return;
            }

            TxtRollbackStatus.Text = LocalizationService.Format("Settings_RollbackAvailable", backup.Version);
            TxtRollbackStatus.Visibility = Visibility.Visible;
            BtnRollback.Visibility = Visibility.Visible;
            BtnRollback.IsEnabled = !_updateBusy;
        }

        private void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            var backup = UpdateBackupService.GetCurrent();
            if (backup == null)
            {
                SetUpdateError(LocalizationService.Get("Update_BackupMissing"));
                return;
            }

            var result = MessageBox.Show(
                LocalizationService.Format("Settings_RollbackConfirm", backup.Version),
                LocalizationService.Get("Settings_RollbackConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                BtnRollback.IsEnabled = false;
                TxtUpdateStatus.Text = LocalizationService.Get("Settings_RollbackRestarting");
                UpdateService.LaunchRollback(backup.Path);
                Application.Current.Shutdown();
            }
            catch (InvalidOperationException ex)
            {
                SetUpdateError(ex.Message);
                BtnRollback.IsEnabled = true;
            }
            catch
            {
                SetUpdateError(LocalizationService.Get("Settings_RollbackFailed"));
                BtnRollback.IsEnabled = true;
            }
        }

        private CancellationToken StartUpdateOperation()
        {
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateCancellation = new CancellationTokenSource();
            return _updateCancellation.Token;
        }

        private void RepoLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/ZerroDevs/CryoTray")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                if (!_isClosed)
                {
                    SetUpdateError(LocalizationService.Get("Settings_RepoError"));
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
