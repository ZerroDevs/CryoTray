using System;
using System.Windows;
using System.Windows.Input;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class KeepAwakeDurationWindow : Window
    {
        public TimeSpan? Duration { get; private set; }

        public KeepAwakeDurationWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            Loaded += (s, e) =>
            {
                TxtDuration.Focus();
                TxtDuration.SelectAll();
            };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            string unit = (CmbUnit.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString()
                ?? "Minutes";
            if (!KeepAwakeService.TryParseCustomDuration(TxtDuration.Text, unit, out TimeSpan duration))
            {
                TxtError.Text = LocalizationService.Get("KeepAwake_InvalidDuration");
                TxtError.Visibility = Visibility.Visible;
                TxtDuration.Focus();
                TxtDuration.SelectAll();
                return;
            }

            Duration = duration;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
