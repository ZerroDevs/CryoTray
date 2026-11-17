using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CryoTray.Models;
using CryoTray.Services;
using Microsoft.Win32;

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

        public bool OpenAboutOnLoad { get; set; }

        public SettingsWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            Loaded += SettingsWindow_Loaded;
            Closed += SettingsWindow_Closed;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            _isClosed = true;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateCancellation = null;
            Content = null;
            DataContext = null;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUI();
            if (OpenAboutOnLoad)
            {
                TabAbout.IsChecked = true;
            }

            ApplyLocalizedText();
            RefreshRollbackUi();
            _isBinding = false;
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
            ChkLaunchStartup.IsChecked = s.LaunchOnStartup;
            ChkLaunchBackground.IsChecked = s.LaunchInBackground;
            ChkEnableCoolingOnLaunch.IsChecked = s.EnableCoolingOnLaunch;
            ChkPinToTaskbar.IsChecked = s.PinToTaskbar;
            ChkAlwaysOnTop.IsChecked = s.AlwaysOnTop;
            ChkHideOnFocusLoss.IsChecked = s.HideOnFocusLoss;

            ChkUseHotkey.IsChecked = s.UseHotkey;
            TxtHotkeyKey.Text = string.IsNullOrEmpty(s.HotkeyKey) ? "C" : s.HotkeyKey;
            ChkMuteTemperatureAlerts.IsChecked = s.MuteTemperatureAlerts;
            ChkAutomaticUpdates.IsChecked = s.AutomaticUpdateChecks;

            // View Tab
            ChkShowGpuBadge.IsChecked = s.ShowGpuBadge;
            ChkShowCpuBadge.IsChecked = s.ShowCpuBadge;
            ChkAnchorToTray.IsChecked = s.AnchorToTray;
            ChkShowOverlayRow.IsChecked = s.ShowOverlayRow;
            ChkShowAutopilotRow.IsChecked = s.ShowAutopilotRow;

            // CPU Tab - Cooling Controls
            CmbPerfBoostMode.SelectedIndex = s.PerfBoostMode == 1 ? 1 : 0;
            SldThrottle.Value = s.MaxCpuThrottlePercent;
            TxtThrottleVal.Text = $"{s.MaxCpuThrottlePercent}%";

            ChkAutopilot.IsChecked = s.AutopilotEnabled;
            SldIdleThreshold.Value = s.AutopilotIdleThresholdPercent;
            SldBusyThreshold.Value = s.AutopilotBusyThresholdPercent;

            // CPU, disk, and GPU alert thresholds
            SldCpuThreshold.Value = s.HighTempThresholdCpu;
            SldDiskThreshold.Value = s.HighTempThresholdDisk;
            SldGpuThreshold.Value = s.HighTempThresholdGpu;
            UpdateThresholdLabels();

            SetUpdateState("Settings_UpdateDefault", "Settings_CheckUpdates");
        }

        private void ApplyLocalizedText()
        {
            TxtAboutVersion.Text = LocalizationService.Format("Settings_AboutVersion", UpdateService.CurrentVersion);
            UpdateThresholdLabels();
            ApplyUpdateState();
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            ApplyLocalizedText();
            RefreshRollbackUi();
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

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null) return;

            PanelGeneral.Visibility = Visibility.Collapsed;
            PanelView.Visibility = Visibility.Collapsed;
            PanelCpu.Visibility = Visibility.Collapsed;
            PanelDisk.Visibility = Visibility.Collapsed;
            PanelGpu.Visibility = Visibility.Collapsed;
            PanelAbout.Visibility = Visibility.Collapsed;

            if (TabGeneral.IsChecked == true) PanelGeneral.Visibility = Visibility.Visible;
            else if (TabView.IsChecked == true) PanelView.Visibility = Visibility.Visible;
            else if (TabCpu.IsChecked == true) PanelCpu.Visibility = Visibility.Visible;
            else if (TabDisk.IsChecked == true) PanelDisk.Visibility = Visibility.Visible;
            else if (TabGpu.IsChecked == true) PanelGpu.Visibility = Visibility.Visible;
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
            s.PinToTaskbar = ChkPinToTaskbar.IsChecked == true;
            s.AlwaysOnTop = ChkAlwaysOnTop.IsChecked == true;
            s.HideOnFocusLoss = ChkHideOnFocusLoss.IsChecked == true;

            s.UseHotkey = ChkUseHotkey.IsChecked == true;
            s.HotkeyKey = TxtHotkeyKey.Text.Trim();
            s.MuteTemperatureAlerts = ChkMuteTemperatureAlerts.IsChecked == true;
            s.AutomaticUpdateChecks = ChkAutomaticUpdates.IsChecked == true;

            s.ShowGpuBadge = ChkShowGpuBadge.IsChecked == true;
            s.ShowCpuBadge = ChkShowCpuBadge.IsChecked == true;
            s.AnchorToTray = ChkAnchorToTray.IsChecked == true;
            s.ShowOverlayRow = ChkShowOverlayRow.IsChecked == true;
            s.ShowAutopilotRow = ChkShowAutopilotRow.IsChecked == true;

            s.NormalPowerPlanGuid = CmbNormalPowerPlan.SelectedItem is PowerSchemeInfo normalPlan
                ? normalPlan.Guid.ToString()
                : PowerPlanService.BalancedGuid.ToString();

            s.PerfBoostMode = CmbPerfBoostMode.SelectedIndex == 1 ? 1 : 0;
            s.MaxCpuThrottlePercent = (int)SldThrottle.Value;

            s.AutopilotEnabled = ChkAutopilot.IsChecked == true;
            s.AutopilotIdleThresholdPercent = (int)SldIdleThreshold.Value;
            s.AutopilotBusyThresholdPercent = (int)SldBusyThreshold.Value;

            s.HighTempThresholdCpu = (int)SldCpuThreshold.Value;
            s.HighTempThresholdDisk = (int)SldDiskThreshold.Value;
            s.HighTempThresholdGpu = (int)SldGpuThreshold.Value;

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
            }
        }

        private void CmbNormalPowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
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
            StartupTaskService.SetStartup(enable);
            SaveUiIntoSettings();
        }

        private void ChkUseHotkey_Changed(object sender, RoutedEventArgs e)
        {
            if (_isBinding) return;
            SaveUiIntoSettings();
        }

        private void TxtHotkeyKey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.A && e.Key <= Key.Z)
            {
                TxtHotkeyKey.Text = e.Key.ToString();
                e.Handled = true;
                SaveUiIntoSettings();
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
                _isBinding = true;
                LoadSettingsIntoUI();
                _isBinding = false;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            // Restore defaults in settings.json
            SettingsService.Instance.Reset();

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
