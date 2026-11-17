using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;
using Microsoft.Win32;

namespace CryoTray.Views
{
    public partial class SystemInfoWindow : Window
    {
        private SystemInfoData _data = new();
        private readonly DispatcherTimer _uptimeTimer;
        private TextBlock? _uptimeTextBlock;

        public SystemInfoWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);

            _uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uptimeTimer.Tick += (s, e) =>
            {
                if (_uptimeTextBlock != null)
                {
                    _uptimeTextBlock.Text = SystemInfoQueryService.FormatUptime(
                        TimeSpan.FromMilliseconds(Environment.TickCount64));
                }
            };

            Loaded += SystemInfoWindow_Loaded;
            Closed += SystemInfoWindow_Closed;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void SystemInfoWindow_Closed(object? sender, EventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _uptimeTimer.Stop();
            _uptimeTextBlock = null;
            InfoContainer.Children.Clear();
            _data = new SystemInfoData();
            Content = null;
            DataContext = null;

            if (System.Windows.Application.Current is App app)
            {
                app.TrimWorkingSet();
            }
        }

        private void SystemInfoWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _data = SystemInfoQueryService.Instance.QuerySystemInfo();
            RenderRows();
            _uptimeTimer.Start();
        }

        private void OnLanguageChanged()
        {
            LocalizationService.Instance.ApplyWindow(this);
            _data = SystemInfoQueryService.Instance.QuerySystemInfo();
            RenderRows();
        }

        private void RenderRows()
        {
            InfoContainer.Children.Clear();
            int rowIndex = 0;

            void AddRow(string label, string value, string? subtitle = null, bool isUptime = false, string? detail = null)
            {
                bool isAlternate = (rowIndex % 2 == 1);
                var border = new Border
                {
                    Background = isAlternate ? new SolidColorBrush(Color.FromArgb(255, 39, 39, 39)) : Brushes.Transparent,
                    CornerRadius = new CornerRadius(0),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 0)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Label
                var txtLabel = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D6D6D6")),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(txtLabel, 0);
                grid.Children.Add(txtLabel);

                // Value stack
                var valStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var txtValue = new TextBlock
                {
                    Text = value,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = System.Windows.FlowDirection.LeftToRight
                };
                valStack.Children.Add(txtValue);

                if (isUptime)
                {
                    _uptimeTextBlock = txtValue;
                }

                if (!string.IsNullOrEmpty(subtitle))
                {
                    var txtSub = new TextBlock
                    {
                        Text = subtitle,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B4B4B4")),
                        FontSize = 12,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    valStack.Children.Add(txtSub);
                }

                if (!string.IsNullOrEmpty(detail))
                {
                    var txtDetail = new TextBlock
                    {
                        Text = detail,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E")),
                        FontSize = 11.5,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    valStack.Children.Add(txtDetail);
                }

                Grid.SetColumn(valStack, 1);
                grid.Children.Add(valStack);

                border.Child = grid;
                InfoContainer.Children.Add(border);
                rowIndex++;
            }

            AddRow(LocalizationService.Get("System_Pc"), _data.PcName);
            AddRow(LocalizationService.Get("System_Processor"), _data.ProcessorName, _data.ProcessorCores);
            AddRow(LocalizationService.Get("System_Memory"), _data.Memory);
            AddRow(LocalizationService.Get("System_GraphicsCard"), _data.GraphicsCard);

            foreach (var d in _data.StorageDevices)
            {
                AddRow(d.Header, d.Title, d.Subtitle, false, d.FreeSpace);
            }

            AddRow(LocalizationService.Get("System_OperatingSystem"), _data.OperatingSystem);
            AddRow(LocalizationService.Get("System_Uptime"), _data.WindowsUptime, null, true);
            AddRow(LocalizationService.Get("System_StartupSpeed"), _data.StartupSpeed);
            AddRow(LocalizationService.Get("System_TempStats"), _data.TempStats);
            AddRow(LocalizationService.Get("System_Monitors"), _data.Monitors);
            AddRow(LocalizationService.Get("System_Network"), _data.Network);
            AddRow(LocalizationService.Get("System_SystemErrors"), _data.SystemErrors);
        }

        private void BtnSaveTo_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = LocalizationService.Get("Common_TextFilter"),
                FileName = $"CryoTray_SystemInfo_{Environment.MachineName}.txt"
            };

            if (sfd.ShowDialog() == true)
            {
                string report = SystemInfoQueryService.Instance.GenerateTextReport(_data);
                File.WriteAllText(sfd.FileName, report);
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
