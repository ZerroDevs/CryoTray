using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class HealthCheckWindow : Window
    {
        private readonly bool _postUpdate;
        private readonly bool _firstRun;
        private bool _isChecking;
        private bool _isClosed;
        private HealthCheckReport? _report;

        public event Action? OpenSettingsRequested;
        public event Action? OpenSystemInformationRequested;

        public HealthCheckWindow(bool postUpdate, bool firstRun = false)
        {
            _postUpdate = postUpdate;
            _firstRun = firstRun;
            InitializeComponent();
            TextScaleService.Attach(this);
            LocalizationService.Instance.ApplyWindow(this);
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            ThemeService.ThemeChanged += OnThemeChanged;
            Loaded += HealthCheckWindow_Loaded;
            Closed += HealthCheckWindow_Closed;
        }

        private async void HealthCheckWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Rect workArea = WindowPlacementHelper.GetMonitorWorkAreaDips(this);
            (double left, double top) = WindowPlacementHelper.ClampToWorkArea(
                workArea,
                Left,
                Top,
                ActualWidth > 0 ? ActualWidth : Width,
                ActualHeight > 0 ? ActualHeight : Height,
                12);
            Left = left;
            Top = top;

            await Task.Delay(650);
            if (_isClosed)
            {
                return;
            }

            await RunCheckAsync();
        }

        private void HealthCheckWindow_Closed(object? sender, EventArgs e)
        {
            _isClosed = true;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            Loaded -= HealthCheckWindow_Loaded;
            Closed -= HealthCheckWindow_Closed;
            ItemsPanel.Children.Clear();
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

        private async Task RunCheckAsync()
        {
            if (_isChecking || _isClosed)
            {
                return;
            }

            _isChecking = true;
            BtnRecheck.IsEnabled = false;
            TxtSummary.Text = LocalizationService.Get("Health_Checking");
            TxtSummaryDetail.Text = "";
            TxtActionStatus.Text = "";
            ItemsPanel.Children.Clear();

            try
            {
                HealthCheckReport report = await Task.Run(
                    () => HealthCheckService.Run(_postUpdate));
                if (_isClosed)
                {
                    return;
                }

                _report = report;
                RenderReport();
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    TxtSummary.Text = LocalizationService.Get("Health_CheckFailed");
                    TxtSummary.Foreground = ThemeService.GetBrush("DangerTextBrush");
                    TxtSummaryDetail.Text = ex.Message;
                }
            }
            finally
            {
                _isChecking = false;
                if (!_isClosed)
                {
                    BtnRecheck.IsEnabled = true;
                }
            }
        }

        private void RenderReport()
        {
            if (_report == null || _isClosed)
            {
                return;
            }

            int issueCount = _report.IssueCount;
            TxtSummary.Text = LocalizationService.Get(
                _report.ActionCount > 0
                    ? "Health_SummaryAction"
                    : _report.WarningCount > 0
                        ? "Health_SummaryWarning"
                        : "Health_SummaryReady");
            TxtSummary.Foreground = ThemeService.GetBrush(
                _report.ActionCount > 0
                    ? "DangerTextBrush"
                    : _report.WarningCount > 0
                        ? "WarningTextBrush"
                        : "SuccessTextBrush");
            string context = LocalizationService.Get(
                _postUpdate
                    ? "Health_ContextPostUpdate"
                    : _firstRun
                        ? "Health_ContextFirstRun"
                        : "Health_ContextManual");
            TxtSummaryDetail.Text = LocalizationService.Format(
                "Health_SummaryDetail",
                _report.ReadyCount,
                _report.WarningCount,
                _report.ActionCount,
                _report.CheckedAtLocal.ToString("h:mm:ss tt"),
                context);

            ItemsPanel.Children.Clear();
            int index = 0;
            foreach (HealthCheckItem item in _report.Items
                .OrderBy(item => LevelRank(item.Level)))
            {
                ItemsPanel.Children.Add(CreateRow(item, index++));
            }

            if (issueCount == 0)
            {
                BtnClose.Focus();
            }
            else
            {
                Button? firstAction = FindFirstActionButton(ItemsPanel);
                (firstAction ?? (Button)BtnClose).Focus();
            }
        }

        private Border CreateRow(HealthCheckItem item, int index)
        {
            var row = new Border
            {
                Background = index % 2 == 1
                    ? ThemeService.GetBrush("AlternateRowBrush")
                    : Brushes.Transparent,
                BorderBrush = ThemeService.GetBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 9, 10, 9)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusGlyph = new TextBlock
            {
                Text = StatusGlyph(item.Level),
                Foreground = StatusBrush(item.Level),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 6, 0)
            };
            AutomationProperties.SetName(statusGlyph, LocalizationService.Get(StatusLabelKey(item.Level)));
            Grid.SetColumn(statusGlyph, 0);
            grid.Children.Add(statusGlyph);

            var textStack = new StackPanel();
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new TextBlock
            {
                Text = LocalizationService.Get(item.TitleKey),
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            titleRow.Children.Add(title);
            var statusLabel = new TextBlock
            {
                Text = LocalizationService.Get(StatusLabelKey(item.Level)),
                Foreground = StatusBrush(item.Level),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(statusLabel);
            textStack.Children.Add(titleRow);

            string detailText = LocalizationService.Format(item.DetailKey, item.DetailArgs);
            var detail = new TextBlock
            {
                Text = detailText,
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            textStack.Children.Add(detail);
            AutomationProperties.SetName(
                row,
                $"{LocalizationService.Get(item.TitleKey)}. {detailText}. " +
                LocalizationService.Get(StatusLabelKey(item.Level)));
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            if (item.Action != HealthCheckActionKind.None)
            {
                var action = new Button
                {
                    Content = LocalizationService.Get(item.ActionKey),
                    Style = (Style)FindResource("HealthRowActionButton"),
                    Tag = item,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TabIndex = 10 + index
                };
                AutomationProperties.SetName(action, LocalizationService.Get(item.ActionKey));
                action.Click += Action_Click;
                Grid.SetColumn(action, 2);
                grid.Children.Add(action);
            }

            row.Child = grid;
            return row;
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not HealthCheckItem item)
            {
                return;
            }

            button.IsEnabled = false;
            TxtActionStatus.Text = "";
            try
            {
                switch (item.Action)
                {
                    case HealthCheckActionKind.InstallPawnIo:
                        HardwareMonitorService.InstallOrOpenPawnIo();
                        TxtActionStatus.Text = LocalizationService.Get("Health_ActionPawnIo");
                        await Task.Delay(900);
                        break;
                    case HealthCheckActionKind.EnableStartupTask:
                        if (!StartupTaskService.TrySetStartup(true, out string? enableError))
                        {
                            TxtActionStatus.Text = FormatActionError(enableError);
                            return;
                        }
                        TxtActionStatus.Text = LocalizationService.Get("Health_ActionStartupUpdated");
                        break;
                    case HealthCheckActionKind.RemoveStartupTask:
                        if (!StartupTaskService.TrySetStartup(false, out string? removeError))
                        {
                            TxtActionStatus.Text = FormatActionError(removeError);
                            return;
                        }
                        TxtActionStatus.Text = LocalizationService.Get("Health_ActionStartupUpdated");
                        break;
                    case HealthCheckActionKind.RepairUpdateStaging:
                        try
                        {
                            await Task.Run(UpdateStorage.EnsureUpdateRoot);
                            TxtActionStatus.Text = LocalizationService.Get("Health_ActionUpdatesRepaired");
                        }
                        catch (Exception ex)
                        {
                            TxtActionStatus.Text = LocalizationService.Format(
                                "Health_ActionFailed", ex.Message);
                            return;
                        }
                        break;
                    case HealthCheckActionKind.OpenSettings:
                        OpenSettingsRequested?.Invoke();
                        return;
                    case HealthCheckActionKind.OpenSystemInformation:
                        OpenSystemInformationRequested?.Invoke();
                        return;
                    case HealthCheckActionKind.OpenUpdateLog:
                        OpenUpdateLog();
                        return;
                    case HealthCheckActionKind.Retry:
                        TxtActionStatus.Text = LocalizationService.Get("Health_ActionRetrying");
                        break;
                }

                await Task.Delay(450);
                await RunCheckAsync();
            }
            finally
            {
                if (!_isClosed)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private static string FormatActionError(string? error) =>
            LocalizationService.Format(
                "Health_ActionFailed",
                string.IsNullOrWhiteSpace(error)
                    ? LocalizationService.Get("Health_ActionFailedUnknown")
                    : error);

        private static void OpenUpdateLog()
        {
            try
            {
                string target = File.Exists(UpdateStorage.LogPath)
                    ? UpdateStorage.LogPath
                    : UpdateStorage.UpdateRoot;
                if (File.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{target}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Directory.CreateDirectory(UpdateStorage.UpdateRoot);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = UpdateStorage.UpdateRoot,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
            }
        }

        private static Button? FindFirstActionButton(DependencyObject parent)
        {
            if (parent is Button button && button.IsEnabled)
            {
                return button;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                Button? found = FindFirstActionButton(VisualTreeHelper.GetChild(parent, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static int LevelRank(HealthCheckLevel level) => level switch
        {
            HealthCheckLevel.ActionRequired => 0,
            HealthCheckLevel.Warning => 1,
            HealthCheckLevel.Unavailable => 2,
            _ => 3
        };

        private static string StatusGlyph(HealthCheckLevel level) => level switch
        {
            HealthCheckLevel.ActionRequired => "!",
            HealthCheckLevel.Warning => "!",
            HealthCheckLevel.Unavailable => "–",
            _ => "✓"
        };

        private static Brush StatusBrush(HealthCheckLevel level) => level switch
        {
            HealthCheckLevel.ActionRequired => ThemeService.GetBrush("DangerTextBrush"),
            HealthCheckLevel.Warning => ThemeService.GetBrush("WarningTextBrush"),
            HealthCheckLevel.Unavailable => ThemeService.GetBrush("TextMutedBrush"),
            _ => ThemeService.GetBrush("SuccessTextBrush")
        };

        private static string StatusLabelKey(HealthCheckLevel level) => level switch
        {
            HealthCheckLevel.ActionRequired => "Health_StatusAction",
            HealthCheckLevel.Warning => "Health_StatusWarning",
            HealthCheckLevel.Unavailable => "Health_StatusUnavailable",
            _ => "Health_StatusReady"
        };

        private void BtnRecheck_Click(object sender, RoutedEventArgs e)
        {
            _ = RunCheckAsync();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
