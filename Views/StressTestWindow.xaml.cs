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
using Microsoft.Win32;

namespace CryoTray.Views
{
    public partial class StressTestWindow : Window
    {
        private bool _isRunning;
        private CancellationTokenSource? _testCts;
        private readonly DispatcherTimer _timer;
        private Stopwatch _stopwatch = new();
        private readonly List<double> _testTemps = new();
        private readonly List<bool> _testCoolingStates = new();
        private readonly List<TelemetrySample> _recordedSamples = new();

        internal readonly record struct StressCoolingComparison(
            bool HasOffPhase,
            double OffTemp,
            double OffWatts,
            bool HasOnPhase,
            double OnTemp,
            double OnWatts,
            double DeltaTemp,
            double DeltaWatts);

        private static readonly Brush PhaseOffChartBrush = CreateFrozenBrush("#E07D62");
        private static readonly Brush PhaseOnChartBrush = CreateFrozenBrush("#2B88D8");
        private static readonly Brush DeltaGoodBrush = CreateFrozenBrush("#ABD068");
        private static readonly Brush DeltaWarmBrush = CreateFrozenBrush("#E5987E");
        private static readonly Brush DeltaHintBrush = CreateFrozenBrush("#B0B0B0");

        private static Brush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

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

            if (_isRunning)
            {
                _recordedSamples.Add(TelemetrySample.FromSnapshot(snapshot));
                BtnExportCsv.IsEnabled = _recordedSamples.Count > 0;
                if (hasTemp)
                {
                    _testTemps.Add(snapshot.CpuTemp);
                    _testCoolingStates.Add(snapshot.CoolingModeActive);
                    DrawChart();
                }
                UpdateComparisonDisplay();
            }
        }

        internal static StressCoolingComparison ComputeCoolingComparison(
            IReadOnlyList<TelemetrySample> samples,
            bool isFahrenheit)
        {
            const int trailingWindow = 5;

            var offSamples = new List<TelemetrySample>();
            var onSamples = new List<TelemetrySample>();
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (!double.IsFinite(sample.CpuTemp) || sample.CpuTemp <= 0)
                {
                    continue;
                }

                if (sample.CoolingModeActive)
                {
                    onSamples.Add(sample);
                }
                else
                {
                    offSamples.Add(sample);
                }
            }

            (bool hasOff, double offTemp, double offWatts) = SummarizePhase(offSamples, trailingWindow, isFahrenheit);
            (bool hasOn, double onTemp, double onWatts) = SummarizePhase(onSamples, trailingWindow, isFahrenheit);

            double deltaTemp = hasOff && hasOn ? onTemp - offTemp : double.NaN;
            double deltaWatts = hasOff && hasOn && double.IsFinite(offWatts) && double.IsFinite(onWatts)
                ? onWatts - offWatts
                : double.NaN;

            return new StressCoolingComparison(hasOff, offTemp, offWatts, hasOn, onTemp, onWatts, deltaTemp, deltaWatts);
        }

        private static (bool HasSamples, double AvgTemp, double AvgWatts) SummarizePhase(
            List<TelemetrySample> samples,
            int trailingWindow,
            bool isFahrenheit)
        {
            if (samples.Count == 0)
            {
                return (false, double.NaN, double.NaN);
            }

            int start = Math.Max(0, samples.Count - trailingWindow);
            int count = samples.Count - start;
            double tempSum = 0;
            double wattsSum = 0;
            int wattsCount = 0;

            for (int i = start; i < samples.Count; i++)
            {
                double temp = isFahrenheit ? (samples[i].CpuTemp * 9 / 5) + 32 : samples[i].CpuTemp;
                tempSum += temp;

                int? cpuPower = samples[i].CpuPowerWatts;
                if (cpuPower.HasValue && cpuPower.Value > 0)
                {
                    wattsSum += cpuPower.Value;
                    wattsCount++;
                }
            }

            double avgTemp = tempSum / count;
            double avgWatts = wattsCount > 0 ? wattsSum / wattsCount : double.NaN;
            return (true, avgTemp, avgWatts);
        }

        private void UpdateComparisonDisplay()
        {
            bool isFahrenheit = SettingsService.Instance.Current.TemperatureUnit == "Fahrenheit";
            string unit = isFahrenheit ? "°F" : "°C";
            var cmp = ComputeCoolingComparison(_recordedSamples, isFahrenheit);

            TxtPhaseOff.Text = cmp.HasOffPhase
                ? FormatPhaseText(cmp.OffTemp, cmp.OffWatts, unit)
                : "--";
            TxtPhaseOn.Text = cmp.HasOnPhase
                ? FormatPhaseText(cmp.OnTemp, cmp.OnWatts, unit)
                : "--";

            if (cmp.HasOffPhase && cmp.HasOnPhase)
            {
                long deltaTempRounded = (long)Math.Round(cmp.DeltaTemp);
                string tempSign = deltaTempRounded > 0 ? "+" : "";
                string deltaText = $"{tempSign}{deltaTempRounded} {unit}";
                if (double.IsFinite(cmp.DeltaWatts))
                {
                    long deltaWattsRounded = (long)Math.Round(cmp.DeltaWatts);
                    string wattSign = deltaWattsRounded > 0 ? "+" : "";
                    deltaText += $" · {wattSign}{deltaWattsRounded} W";
                }

                TxtPhaseDelta.Text = deltaText;
                TxtPhaseDelta.Foreground = cmp.DeltaTemp <= 0 ? DeltaGoodBrush : DeltaWarmBrush;
            }
            else
            {
                TxtPhaseDelta.Text = LocalizationService.Get("Stress_DeltaHint");
                TxtPhaseDelta.Foreground = DeltaHintBrush;
            }
        }

        private static string FormatPhaseText(double temp, double watts, string unit)
        {
            string tempPart = $"{Math.Round(temp)} {unit}";
            return double.IsFinite(watts) && watts > 0
                ? $"{tempPart} · {Math.Round(watts)} W"
                : tempPart;
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

            int maxCount = 60;
            int startIdx = Math.Max(0, _testTemps.Count - maxCount);
            int count = _testTemps.Count - startIdx;
            double stepX = w / Math.Max(1, count - 1);

            double prevX = 0;
            double prevY = 0;
            for (int i = 0; i < count; i++)
            {
                double t = _testTemps[startIdx + i];
                double norm = (t - minT) / (maxT - minT);
                norm = Math.Clamp(norm, 0, 1);
                double y = h - (norm * (h - 10) + 5);
                double x = i * stepX;

                if (i > 0)
                {
                    bool segmentCoolingOn = startIdx + i < _testCoolingStates.Count && _testCoolingStates[startIdx + i];
                    ChartCanvas.Children.Add(new Line
                    {
                        X1 = prevX,
                        Y1 = prevY,
                        X2 = x,
                        Y2 = y,
                        Stroke = segmentCoolingOn ? PhaseOnChartBrush : PhaseOffChartBrush,
                        StrokeThickness = 2,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    });
                }

                prevX = x;
                prevY = y;
            }
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
            _testCoolingStates.Clear();
            _recordedSamples.Clear();
            _recordedSamples.Add(TelemetrySample.FromSnapshot(HardwareMonitorService.Instance.CurrentSnapshot));
            BtnExportCsv.IsEnabled = _recordedSamples.Count > 0;
            UpdateComparisonDisplay();
            _stopwatch.Restart();
            _timer.Start();

            BtnStartStop.Content = LocalizationService.Get("Stress_Stop");
            BtnStartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));

            _testCts = new CancellationTokenSource();
            var token = _testCts.Token;

            // Use dedicated BelowNormal OS threads so full CPU load never starves ThreadPool polling.
            int cores = Environment.ProcessorCount;
            for (int c = 0; c < cores; c++)
            {
                var worker = new Thread(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        double a = Math.Sqrt(12345.6789);
                        double b = Math.Sin(a);
                        _ = b;
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = $"CryoTray-Stress-{c}"
                };
                worker.Start();
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
            UpdateComparisonDisplay();
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

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_recordedSamples.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.Get("Main_CsvNoSamples"),
                    LocalizationService.Get("Stress_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = LocalizationService.Get("Common_CsvFilter"),
                FileName = $"CryoTray_StressTest_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                var exportCopy = new List<TelemetrySample>(_recordedSamples);
                TelemetryCsvService.ExportSamples(exportCopy, sfd.FileName);
                MessageBox.Show(
                    LocalizationService.Format("Main_CsvExportDone", exportCopy.Count, sfd.FileName),
                    LocalizationService.Get("Stress_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
