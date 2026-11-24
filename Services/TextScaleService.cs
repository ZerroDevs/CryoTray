using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace CryoTray.Services
{
    /// <summary>
    /// WPF does not honor the Windows "Make text bigger" factor. Scale the window
    /// content and the matching window size so existing fixed layout dimensions do
    /// not clip the larger text.
    /// </summary>
    public static class TextScaleService
    {
        private const string AccessibilityKey = @"Software\Microsoft\Accessibility";
        private const string TextScaleValue = "TextScaleFactor";
        private const int MinimumPercent = 100;
        private const int MaximumPercent = 225;

        private static readonly DependencyProperty HasAppliedScaleProperty =
            DependencyProperty.RegisterAttached(
                "HasAppliedScale",
                typeof(bool),
                typeof(TextScaleService),
                new PropertyMetadata(false));

        private static double _factor = 1.0;

        public static double Factor => _factor;

        public static void Initialize()
        {
            _factor = ReadSystemFactor();
        }

        public static void Attach(Window window, bool scaleHeight = true)
        {
            if (_factor <= 1.0)
            {
                return;
            }

            ApplyWindowSizing(window, scaleHeight);
            if (window.Content is FrameworkElement root)
            {
                ApplyScale(root);
            }

            ApplyToPopups(window);
            window.Loaded += (_, _) => ApplyToPopups(window);
        }

        internal static double NormalizePercent(int percent)
        {
            return Math.Clamp(percent, MinimumPercent, MaximumPercent) / 100.0;
        }

        private static double ReadSystemFactor()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccessibilityKey);
                object? raw = key?.GetValue(TextScaleValue);
                return raw == null
                    ? 1.0
                    : NormalizePercent(Convert.ToInt32(raw));
            }
            catch
            {
                return 1.0;
            }
        }

        private static void ApplyWindowSizing(Window window, bool scaleHeight)
        {
            if (!double.IsNaN(window.Width) && window.Width > 0)
            {
                window.Width *= _factor;
            }
            if (scaleHeight && !double.IsNaN(window.Height) && window.Height > 0)
            {
                window.Height *= _factor;
            }
            if (!double.IsNaN(window.MinWidth) && window.MinWidth > 0)
            {
                window.MinWidth *= _factor;
            }
            if (scaleHeight && !double.IsNaN(window.MinHeight) && window.MinHeight > 0)
            {
                window.MinHeight *= _factor;
            }
        }

        private static void ApplyToPopups(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is Popup popup)
                {
                    if (popup.Child is FrameworkElement content)
                    {
                        ApplyScale(content);
                    }
                    continue;
                }

                if (child is DependencyObject dependencyObject)
                {
                    ApplyToPopups(dependencyObject);
                }
            }
        }

        private static void ApplyScale(FrameworkElement element)
        {
            if (GetHasAppliedScale(element))
            {
                return;
            }

            var scale = new ScaleTransform(_factor, _factor);
            if (element.LayoutTransform is Transform existing &&
                !existing.Value.IsIdentity)
            {
                var group = new TransformGroup();
                group.Children.Add(existing);
                group.Children.Add(scale);
                element.LayoutTransform = group;
            }
            else
            {
                element.LayoutTransform = scale;
            }

            SetHasAppliedScale(element, true);
        }

        private static bool GetHasAppliedScale(DependencyObject element)
        {
            return (bool)element.GetValue(HasAppliedScaleProperty);
        }

        private static void SetHasAppliedScale(DependencyObject element, bool value)
        {
            element.SetValue(HasAppliedScaleProperty, value);
        }
    }
}
