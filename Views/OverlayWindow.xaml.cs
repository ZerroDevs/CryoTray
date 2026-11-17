using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            Loaded += OverlayWindow_Loaded;
            Closing += OverlayWindow_Closing;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance.Current;
            if (s.OverlayLeft.HasValue && s.OverlayTop.HasValue)
            {
                Left = s.OverlayLeft.Value;
                Top = s.OverlayTop.Value;
            }
            else
            {
                Left = SystemParameters.WorkArea.Right - Width - 30;
                Top = 30;
            }

            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
        }

        private void OverlayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            SettingsService.Instance.Current.OverlayLeft = Left;
            SettingsService.Instance.Current.OverlayTop = Top;
            SettingsService.Instance.Save();
        }

        public void UpdateTelemetry(HardwareSnapshot snapshot)
        {
            Dispatcher.Invoke(() =>
            {
                var s = SettingsService.Instance.Current;
                bool isFahrenheit = s.TemperatureUnit == "Fahrenheit";

                double cpuT = isFahrenheit ? (snapshot.CpuTemp * 9 / 5 + 32) : snapshot.CpuTemp;
                double gpuT = isFahrenheit ? (snapshot.GpuTemp * 9 / 5 + 32) : snapshot.GpuTemp;
                string unit = isFahrenheit ? "°F" : "°C";

                TxtCpuTemp.Text = $"{Math.Round(cpuT)}{unit}";
                TxtCpuTemp.Foreground = snapshot.CoolingModeActive
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E9FDB"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9835B"));

                TxtCpuGhz.Text = $" {snapshot.CpuGhz:0.00}G";
                TxtGpuTemp.Text = $"{Math.Round(gpuT)}{unit}";
            });
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            UpdateTelemetry(snapshot);
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
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
