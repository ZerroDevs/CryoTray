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
        private int _appliedOpacityPercent = -1;

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

            ClampToWorkArea();
            ApplyMetricVisibility(s);
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetryUpdated;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        }

        private void OverlayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetryUpdated;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            SettingsService.Instance.Current.OverlayLeft = Left;
            SettingsService.Instance.Current.OverlayTop = Top;
            SettingsService.Instance.Save();
        }

        private void ClampToWorkArea()
        {
            var workArea = SystemParameters.WorkArea;
            double minLeft = workArea.Left + 8;
            double maxLeft = Math.Max(minLeft, workArea.Right - Width - 8);
            double minTop = workArea.Top + 8;
            double maxTop = Math.Max(minTop, workArea.Bottom - Height - 8);

            Left = Math.Min(Math.Max(Left, minLeft), maxLeft);
            Top = Math.Min(Math.Max(Top, minTop), maxTop);
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

                ApplyMetricVisibility(s);

                TxtCpuTemp.Text = $"{Math.Round(cpuT)}{unit}";
                TxtCpuTemp.Foreground = snapshot.CoolingModeActive
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E9FDB"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9835B"));
                TxtCpuLoad.Text = $"{Math.Round(snapshot.CpuLoad)}%";
                TxtCpuPower.Text = snapshot.CpuPackageWatts.HasValue
                    ? $"{snapshot.CpuPackageWatts.Value}W"
                    : "--W";
                TxtCpuClock.Text = $"{snapshot.CpuGhz:0.00}G";

                TxtGpuTemp.Text = $"{Math.Round(gpuT)}{unit}";
                TxtGpuLoad.Text = $"{Math.Round(snapshot.GpuLoad)}%";
                TxtGpuPower.Text = snapshot.GpuPowerWatts.HasValue
                    ? $"{snapshot.GpuPowerWatts.Value}W"
                    : "--W";
            });
        }

        private void ApplyMetricVisibility(AppSettings settings)
        {
            bool showCpu = settings.OverlayShowCpuTemp ||
                settings.OverlayShowCpuLoad ||
                settings.OverlayShowCpuPower ||
                settings.OverlayShowCpuClock;
            bool showGpu = settings.OverlayShowGpuTemp ||
                settings.OverlayShowGpuLoad ||
                settings.OverlayShowGpuPower;

            CpuOverlayGroup.Visibility = showCpu
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuOverlayGroup.Visibility = showGpu
                ? Visibility.Visible
                : Visibility.Collapsed;

            TxtCpuTemp.Visibility = settings.OverlayShowCpuTemp
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtCpuLoad.Visibility = settings.OverlayShowCpuLoad
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtCpuPower.Visibility = settings.OverlayShowCpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtCpuClock.Visibility = settings.OverlayShowCpuClock
                ? Visibility.Visible
                : Visibility.Collapsed;

            TxtGpuTemp.Visibility = settings.OverlayShowGpuTemp
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtGpuLoad.Visibility = settings.OverlayShowGpuLoad
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtGpuPower.Visibility = settings.OverlayShowGpuPower
                ? Visibility.Visible
                : Visibility.Collapsed;

            TxtOverlayEmpty.Visibility = showCpu || showGpu
                ? Visibility.Collapsed
                : Visibility.Visible;

            ApplySurfaceOpacity(settings);
        }

        private void ApplySurfaceOpacity(AppSettings settings)
        {
            int percent = Math.Clamp(settings.OverlayOpacityPercent, 0, 100);
            if (percent == _appliedOpacityPercent)
            {
                return;
            }

            _appliedOpacityPercent = percent;
            byte alpha = (byte)Math.Round(percent * 255.0 / 100.0);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x16, 0x16, 0x16));
            brush.Freeze();
            OverlaySurface.Background = brush;
        }

        private void OnTelemetryUpdated(HardwareSnapshot snapshot)
        {
            UpdateTelemetry(snapshot);
        }

        private void OnSettingsChanged()
        {
            if (!IsLoaded)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
                ApplyMetricVisibility(SettingsService.Instance.Current)));
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
