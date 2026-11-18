using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class StressTestWindow : Window
    {
        private bool _isRunning;
        private CancellationTokenSource? _testCts;
        private readonly DispatcherTimer _timer;
        private Stopwatch _stopwatch = new();
        private readonly List<double> _testTemps = new();

        public StressTestWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Loaded += StressTestWindow_Loaded;
            Closing += StressTestWindow_Closing;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void StressTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HardwareMonitorService.Instance.TelemetryUpdated += OnTelemetry;
            PowerPlanService.Instance.CoolingModeChanged += OnCoolingChanged;
            UpdateCoolingDisplay(PowerPlanService.Instance.IsCoolingModeActive);
            ApplyTelemetrySnapshot(HardwareMonitorService.Instance.CurrentSnapshot);
        }

        private void StressTestWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopTest();
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            HardwareMonitorService.Instance.TelemetryUpdated -= OnTelemetry;
            PowerPlanService.Instance.CoolingModeChanged -= OnCoolingChanged;
        }

        private void OnCoolingChanged(bool active)
        {
            Dispatcher.Invoke(() => UpdateCoolingDisplay(active));
        }

        private void UpdateCoolingDisplay(bool active)
        {
            TxtCoolingStatus.Text = LocalizationService.Get(active ? "Common_On" : "Common_Off");
            TxtCoolingStatus.Foreground = active
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E9FDB"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9835B"));
        }

        private void OnTelemetry(HardwareSnapshot snapshot)
        {
            Dispatcher.Invoke(() => ApplyTelemetrySnapshot(snapshot));
        }

        private void ApplyTelemetrySnapshot(HardwareSnapshot snapshot)
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";
            bool hasTemp = double.IsFinite(snapshot.CpuTemp) && snapshot.CpuTemp > 0;
            if (hasTemp)
            {
                double displayTemperature = isFahrenheit
                    ? (snapshot.CpuTemp * 9 / 5) + 32
                    : snapshot.CpuTemp;
                TxtTestTemp.Text = $"{Math.Round(displayTemperature)} {unit}";
            }
            else
            {
                TxtTestTemp.Text = $"-- {unit}";
            }

            TxtTestSpeed.Text = double.IsFinite(snapshot.CpuGhz) && snapshot.CpuGhz > 0
                ? $"{snapshot.CpuGhz:0.00} GHz"
                : "-- GHz";

            if (_isRunning && hasTemp)
            {
                _testTemps.Add(snapshot.CpuTemp);
                DrawChart();
            }
        }

        private void DrawChart()
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight;
            if (w <= 0 || h <= 0 || _testTemps.Count < 2) return;

            // Draw horizontal reference grid
            for (int i = 1; i <= 3; i++)
            {
                double y = (h / 4.0) * i;
                var line = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }

            double minT = 30;
            double maxT = 95;

            var points = new PointCollection();
            int maxCount = 60;
            int startIdx = Math.Max(0, _testTemps.Count - maxCount);
            int count = _testTemps.Count - startIdx;
            double stepX = w / Math.Max(1, count - 1);

            for (int i = 0; i < count; i++)
            {
                double t = _testTemps[startIdx + i];
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 10) + 5);
                double x = i * stepX;
                points.Add(new Point(x, y));
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B88D8")),
                StrokeThickness = 2
            };
            ChartCanvas.Children.Add(polyline);
        }

        private void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
            {
                StartTest();
            }
            else
            {
                StopTest();
            }
        }

        private void StartTest()
        {
            _isRunning = true;
            _testTemps.Clear();
            _stopwatch.Restart();
            _timer.Start();

            BtnStartStop.Content = LocalizationService.Get("Stress_Stop");
            BtnStartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));

            _testCts = new CancellationTokenSource();
            var token = _testCts.Token;

            // Run CPU load across all logical processors
            int cores = Environment.ProcessorCount;
            for (int c = 0; c < cores; c++)
            {
                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        double a = Math.Sqrt(12345.6789);
                        double b = Math.Sin(a);
                        _ = b;
                    }
                }, token);
            }
        }

        private void StopTest()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _testCts?.Cancel();
            _stopwatch.Stop();
            _timer.Stop();

            BtnStartStop.Content = LocalizationService.Get("Stress_Start");
            BtnStartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B88D8"));
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            UpdateCoolingDisplay(PowerPlanService.Instance.IsCoolingModeActive);
            BtnStartStop.Content = LocalizationService.Get(_isRunning ? "Stress_Stop" : "Stress_Start");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            TxtElapsed.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
        }

        private void BtnToggleCooling_Click(object sender, RoutedEventArgs e)
        {
            PowerPlanService.Instance.ToggleCoolingMode();
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
