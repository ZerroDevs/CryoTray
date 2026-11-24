using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class SessionReportWindow : Window
    {
        private SessionReport? _report;
        private bool _isLoading;
        private bool _isClosed;

        public SessionReportWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            ThemeService.ThemeChanged += OnThemeChanged;
            Loaded += SessionReportWindow_Loaded;
            Closed += SessionReportWindow_Closed;
        }

        private async void SessionReportWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        private void SessionReportWindow_Closed(object? sender, EventArgs e)
        {
            _isClosed = true;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            Loaded -= SessionReportWindow_Loaded;
            Closed -= SessionReportWindow_Closed;
            ThermalPanel.Children.Clear();
            TimelinePanel.Children.Clear();
            UpdatesPanel.Children.Clear();
            Content = null;
            DataContext = null;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            RenderReport();
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            if (!_isClosed)
            {
                RenderReport();
            }
        }

        private async Task LoadReportAsync()
        {
            if (_isLoading || _isClosed)
            {
                return;
            }

            _isLoading = true;
            BtnRefresh.IsEnabled = false;
            TxtStatus.Text = LocalizationService.Get("Session_Loading");
            TxtStatus.Foreground = ThemeService.GetBrush("TextSecondaryBrush");
            TxtStatus.ToolTip = null;
            try
            {
                SessionReport report = await Task.Run(SessionReportService.Build);
                if (_isClosed)
                {
                    return;
                }

                _report = report;
                RenderReport();
                TxtStatus.Text = "";
                BtnClose.Focus();
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    TxtStatus.Text = LocalizationService.Get("Session_LoadFailed");
                    TxtStatus.Foreground = ThemeService.GetBrush("DangerTextBrush");
                    TxtStatus.ToolTip = ex.Message;
                }
            }
            finally
            {
                _isLoading = false;
                if (!_isClosed)
                {
                    BtnRefresh.IsEnabled = true;
                }
            }
        }

        private void RenderReport()
        {
            if (_report == null || _isClosed)
            {
                return;
            }

            SessionReport report = _report;
            TxtRange.Text = LocalizationService.Format(
                "Session_Range",
                report.StartLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                report.EndLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
            TxtSampleMeta.Text = LocalizationService.Format(
                "Session_SampleSummary",
                report.SampleCount,
                FormatDuration(report.Duration));
            TxtCpuPeak.Text = report.CpuPeak > 0
                ? FormatTemp(report.CpuPeak, report.IsFahrenheit)
                : "—";
            TxtCpuAverage.Text = report.CpuPeak > 0
                ? LocalizationService.Format(
                    "Session_Average",
                    FormatTemp(report.CpuAverage, report.IsFahrenheit))
                : "";
            TxtGpuPeak.Text = report.GpuPeak > 0
                ? FormatTemp(report.GpuPeak, report.IsFahrenheit)
                : "—";
            TxtGpuAverage.Text = report.GpuPeak > 0
                ? LocalizationService.Format(
                    "Session_Average",
                    FormatTemp(report.GpuAverage, report.IsFahrenheit))
                : "";
            TxtHotspotPeak.Text = report.HotspotPeak.HasValue
                ? FormatTemp(report.HotspotPeak.Value, report.IsFahrenheit)
                : "—";
            TxtHotspotDetail.Text = report.HotspotEdgeAverage.HasValue
                ? LocalizationService.Format(
                    "Session_HotspotEdgeAverage",
                    FormatDelta(report.HotspotEdgeAverage.Value, report.IsFahrenheit))
                : "";
            TimeSpan above = report.CpuAboveThreshold > report.GpuAboveThreshold
                ? report.CpuAboveThreshold
                : report.GpuAboveThreshold;
            TxtTimeAbove.Text = above.TotalSeconds > 0 ? FormatDuration(above) : LocalizationService.Get("Session_None");
            TxtTimeAboveDetail.Text = LocalizationService.Format(
                "Session_Thresholds",
                FormatThreshold(report.CpuThreshold, report.IsFahrenheit),
                FormatThreshold(report.GpuThreshold, report.IsFahrenheit));

            ThermalPanel.Children.Clear();
            AddDetailRow(
                ThermalPanel,
                LocalizationService.Get("Session_CpuThreshold"),
                LocalizationService.Format(
                    "Session_ThresholdValue",
                    report.CpuPeak > 0
                        ? FormatTemp(report.CpuPeak, report.IsFahrenheit)
                        : "—",
                    FormatThreshold(report.CpuThreshold, report.IsFahrenheit),
                    FormatDuration(report.CpuAboveThreshold)));
            AddDetailRow(
                ThermalPanel,
                LocalizationService.Get("Session_GpuThreshold"),
                LocalizationService.Format(
                    "Session_ThresholdValue",
                    report.GpuPeak > 0
                        ? FormatTemp(report.GpuPeak, report.IsFahrenheit)
                        : "—",
                    FormatThreshold(report.GpuThreshold, report.IsFahrenheit),
                    FormatDuration(report.GpuAboveThreshold)));
            AddDetailRow(
                ThermalPanel,
                LocalizationService.Get("Session_HotspotDelta"),
                LocalizationService.Format(
                    "Session_HotspotDeltaValue",
                    report.HotspotEdgeAverage.HasValue
                        ? FormatDelta(report.HotspotEdgeAverage.Value, report.IsFahrenheit)
                        : "—",
                    FormatDeltaThreshold(report.HotspotDeltaThreshold, report.IsFahrenheit),
                    FormatDuration(report.HotspotDeltaAboveThreshold)));
            AddDetailRow(
                ThermalPanel,
                LocalizationService.Get("Session_Changes"),
                LocalizationService.Format(
                    "Session_ChangesValue",
                    report.CoolingChanges,
                    report.ProfileChanges,
                    report.AlertCount,
                    report.FanEventCount));

            TimelinePanel.Children.Clear();
            TxtTimelineEmpty.Visibility = report.Timeline.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            int index = 0;
            foreach (SessionTimelineEvent item in report.Timeline)
            {
                TimelinePanel.Children.Add(CreateTimelineRow(item, index++));
            }

            UpdatesPanel.Children.Clear();
            TxtUpdatesEmpty.Visibility = report.Updates.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            index = 0;
            foreach (SessionTimelineEvent item in report.Updates)
            {
                UpdatesPanel.Children.Add(CreateTimelineRow(item, index++));
            }
        }

        private static void AddDetailRow(StackPanel panel, string label, string value)
        {
            var border = new Border
            {
                Background = panel.Children.Count % 2 == 1
                    ? ThemeService.GetBrush("AlternateRowBrush")
                    : Brushes.Transparent,
                BorderBrush = ThemeService.GetBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 7, 10, 7)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.LeftToRight
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            border.Child = grid;
            panel.Children.Add(border);
        }

        private static Border CreateTimelineRow(SessionTimelineEvent item, int index)
        {
            var border = new Border
            {
                Background = index % 2 == 1
                    ? ThemeService.GetBrush("AlternateRowBrush")
                    : Brushes.Transparent,
                BorderBrush = ThemeService.GetBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 7, 10, 7)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var time = new TextBlock
            {
                Text = item.TimestampLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                Foreground = ThemeService.GetBrush("TextMutedBrush"),
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Top
            };
            var kind = new TextBlock
            {
                Text = LocalizationService.Get(KindKey(item.Kind)),
                Foreground = KindBrush(item),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            var text = new TextBlock
            {
                Text = item.Text,
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(kind, 1);
            Grid.SetColumn(text, 2);
            grid.Children.Add(time);
            grid.Children.Add(kind);
            grid.Children.Add(text);
            AutomationProperties.SetName(
                border,
                $"{item.TimestampLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}. " +
                $"{LocalizationService.Get(KindKey(item.Kind))}. {item.Text}");
            border.Child = grid;
            return border;
        }

        private static Brush KindBrush(SessionTimelineEvent item) => item.Kind switch
        {
            "alert" => ThemeService.GetBrush("DangerTextBrush"),
            "fan" => ThemeService.GetBrush("WarningTextBrush"),
            "update" => ThemeService.GetBrush("SuccessTextBrush"),
            "profile" => ThemeService.GetBrush("AccentTextBrush"),
            "cooling" => ThemeService.GetBrush("CoolingOnTextBrush"),
            _ => ThemeService.GetBrush("TextSecondaryBrush")
        };

        private static string KindKey(string kind) => kind switch
        {
            "alert" => "Session_KindAlert",
            "fan" => "Session_KindFan",
            "update" => "Session_KindUpdate",
            "profile" => "Session_KindProfile",
            "cooling" => "Session_KindCooling",
            _ => "Session_KindEvent"
        };

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        private void BtnCopyReport_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null)
            {
                return;
            }

            try
            {
                Clipboard.SetText(SessionReportService.BuildText(_report));
                TxtStatus.Text = LocalizationService.Get("Session_Copied");
            }
            catch
            {
                TxtStatus.Text = LocalizationService.Get("Session_CopyFailed");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string FormatTemp(double value, bool isFahrenheit) =>
            isFahrenheit
                ? $"{Math.Round((value * 9 / 5) + 32)} °F"
                : $"{Math.Round(value)} °C";

        private static string FormatDelta(double value, bool isFahrenheit) =>
            isFahrenheit
                ? $"{Math.Round(value * 9 / 5)} °F"
                : $"{Math.Round(value)} °C";

        private static string FormatThreshold(int celsius, bool isFahrenheit) =>
            FormatTemp(celsius, isFahrenheit);

        private static string FormatDeltaThreshold(int celsius, bool isFahrenheit) =>
            FormatDelta(celsius, isFahrenheit);

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalMinutes >= 1)
            {
                return $"{(int)value.TotalMinutes}m {value.Seconds}s";
            }

            return $"{Math.Max(0, (int)Math.Round(value.TotalSeconds))}s";
        }
    }
}
