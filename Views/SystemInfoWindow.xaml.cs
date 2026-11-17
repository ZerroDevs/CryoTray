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
            Closed += (s, e) => _uptimeTimer.Stop();
        }

        private void SystemInfoWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _data = SystemInfoQueryService.Instance.QuerySystemInfo();
            RenderRows();
            _uptimeTimer.Start();
        }

        private void RenderRows()
        {
            InfoContainer.Children.Clear();
            int rowIndex = 0;

            void AddRow(string label, string value, string? subtitle = null, bool isUptime = false)
            {
                bool isAlternate = (rowIndex % 2 == 1);
                var border = new Border
                {
                    Background = isAlternate ? new SolidColorBrush(Color.FromArgb(255, 33, 33, 33)) : Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 1, 0, 1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Label
                var txtLabel = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E8E8E")),
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
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6E6E6")),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
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
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0")),
                        FontSize = 12,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    valStack.Children.Add(txtSub);
                }

                Grid.SetColumn(valStack, 1);
                grid.Children.Add(valStack);

                border.Child = grid;
                InfoContainer.Children.Add(border);
                rowIndex++;
            }

            AddRow("PC", _data.PcName);
            AddRow("Processor", _data.ProcessorName, _data.ProcessorCores);
            AddRow("Memory", _data.Memory);
            AddRow("Graphics card", _data.GraphicsCard);

            foreach (var d in _data.StorageDevices)
            {
                AddRow(d.Header, d.Title, d.Subtitle);
            }

            AddRow("Operating system", _data.OperatingSystem);
            AddRow("Windows uptime", _data.WindowsUptime, null, true);
            AddRow("Startup speed", _data.StartupSpeed);
            AddRow("PC temperature with\nCooling Mode on", _data.TempStats);
            AddRow("Monitors", _data.Monitors);
            AddRow("Network", _data.Network);
            AddRow("System errors", _data.SystemErrors);
        }

        private void BtnSaveTo_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
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
