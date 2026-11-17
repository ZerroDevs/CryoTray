using System;
using System.IO;
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

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUI();
            _isBinding = false;
        }

        private void LoadSettingsIntoUI()
        {
            var s = SettingsService.Instance.Current;

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

            ChkUseHotkey.IsChecked = s.UseHotkey;
            TxtHotkeyKey.Text = string.IsNullOrEmpty(s.HotkeyKey) ? "C" : s.HotkeyKey;

            // View Tab
            ChkShowGpuBadge.IsChecked = s.ShowGpuBadge;
            ChkShowCpuBadge.IsChecked = s.ShowCpuBadge;
            ChkAnchorToTray.IsChecked = s.AnchorToTray;

            // CPU Tab - Cooling Controls
            CmbPerfBoostMode.SelectedIndex = s.PerfBoostMode == 1 ? 1 : 0;
            SldThrottle.Value = s.MaxCpuThrottlePercent;
            TxtThrottleVal.Text = $"{s.MaxCpuThrottlePercent}%";

            ChkAutopilot.IsChecked = s.AutopilotEnabled;
            SldIdleThreshold.Value = s.AutopilotIdleThresholdPercent;
            SldBusyThreshold.Value = s.AutopilotBusyThresholdPercent;

            // Disk & GPU
            SldDiskThreshold.Value = s.HighTempThresholdDisk;
            TxtDiskThreshold.Text = $"{s.HighTempThresholdDisk}°C";

            SldGpuThreshold.Value = s.HighTempThresholdGpu;
            TxtGpuThreshold.Text = $"{s.HighTempThresholdGpu}°C";
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null) return;

            PanelGeneral.Visibility = Visibility.Collapsed;
            PanelView.Visibility = Visibility.Collapsed;
            PanelCpu.Visibility = Visibility.Collapsed;
            PanelDisk.Visibility = Visibility.Collapsed;
            PanelGpu.Visibility = Visibility.Collapsed;

            if (TabGeneral.IsChecked == true) PanelGeneral.Visibility = Visibility.Visible;
            else if (TabView.IsChecked == true) PanelView.Visibility = Visibility.Visible;
            else if (TabCpu.IsChecked == true) PanelCpu.Visibility = Visibility.Visible;
            else if (TabDisk.IsChecked == true) PanelDisk.Visibility = Visibility.Visible;
            else if (TabGpu.IsChecked == true) PanelGpu.Visibility = Visibility.Visible;
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

            s.UseHotkey = ChkUseHotkey.IsChecked == true;
            s.HotkeyKey = TxtHotkeyKey.Text.Trim();

            s.ShowGpuBadge = ChkShowGpuBadge.IsChecked == true;
            s.ShowCpuBadge = ChkShowCpuBadge.IsChecked == true;
            s.AnchorToTray = ChkAnchorToTray.IsChecked == true;

            s.PerfBoostMode = CmbPerfBoostMode.SelectedIndex == 1 ? 1 : 0;
            s.MaxCpuThrottlePercent = (int)SldThrottle.Value;

            s.AutopilotEnabled = ChkAutopilot.IsChecked == true;
            s.AutopilotIdleThresholdPercent = (int)SldIdleThreshold.Value;
            s.AutopilotBusyThresholdPercent = (int)SldBusyThreshold.Value;

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

        private void CmbTempUnits_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isBinding) SaveUiIntoSettings();
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

        private void SldDiskThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDiskThreshold != null)
            {
                TxtDiskThreshold.Text = $"{(int)SldDiskThreshold.Value}°C";
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void SldGpuThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtGpuThreshold != null)
            {
                TxtGpuThreshold.Text = $"{(int)SldGpuThreshold.Value}°C";
            }
            if (!_isBinding) SaveUiIntoSettings();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
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
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
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
