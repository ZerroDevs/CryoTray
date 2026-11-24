using System;
using System.ComponentModel;
using System.Windows;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class DownloadTimerWarningWindow : Window
    {
        public event Action? Cancelled;
        private bool _closingProgrammatically;

        public DownloadTimerWarningWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.IsArabic
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            LocalizationService.Instance.ApplyWindow(this);
            Loaded += (_, _) => PositionNearTaskbar();
        }

        public void UpdateStatus(DownloadTimerStatus status)
        {
            int seconds = Math.Max(0, status.WarningSecondsRemaining);
            TxtWarningBody.Text = LocalizationService.Format(
                "DownloadTimer_WarningBody",
                ActionLabel(status.Action),
                seconds);
        }

        public void CloseWarning()
        {
            try
            {
                _closingProgrammatically = true;
                Close();
            }
            catch
            {
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_closingProgrammatically)
            {
                return;
            }

            // Alt+F4, a task-manager close, or any other dismissal is a cancel.
            // Never leave the countdown running with no visible way to stop it.
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() => Cancelled?.Invoke()));
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Cancelled?.Invoke();
        }

        private void PositionNearTaskbar()
        {
            Rect work = SystemParameters.WorkArea;
            double left = work.Right - ActualWidth - 20;
            double top = work.Bottom - ActualHeight - 20;
            Left = Math.Max(work.Left + 8, left);
            Top = Math.Max(work.Top + 8, top);
        }

        private static string ActionLabel(DownloadPowerAction action)
        {
            return action switch
            {
                DownloadPowerAction.Hibernate => LocalizationService.Get("Settings_DownloadTimerHibernate"),
                DownloadPowerAction.ShutDown => LocalizationService.Get("Settings_DownloadTimerShutDown"),
                _ => LocalizationService.Get("Settings_DownloadTimerSleep")
            };
        }
    }
}
