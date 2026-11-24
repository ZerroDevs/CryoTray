using System.Windows;
using System.Windows.Input;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class KeepAwakeConfirmWindow : Window
    {
        public KeepAwakeConfirmWindow(bool allowDisplayOff)
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            TxtDisplayNote.Text = LocalizationService.Get(
                allowDisplayOff
                    ? "KeepAwake_ConfirmDisplayOff"
                    : "KeepAwake_ConfirmDisplayOn");
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
