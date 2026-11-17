using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace CryoTray.Services
{
    public class GlobalHotkeyService
    {
        private static GlobalHotkeyService? _instance;
        public static GlobalHotkeyService Instance => _instance ??= new GlobalHotkeyService();

        private const int HotkeyId = 9001;
        private IntPtr _hWnd = IntPtr.Zero;
        private bool _isRegistered;

        public bool Register(IntPtr hWnd, string modifiers, string key)
        {
            Unregister();
            _hWnd = hWnd;

            if (hWnd == IntPtr.Zero) return false;

            uint fsModifiers = 0;
            if (modifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_CONTROL;
            if (modifiers.Contains("Shift", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_SHIFT;
            if (modifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_ALT;
            if (modifiers.Contains("Win", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_WIN;

            fsModifiers |= NativeMethods.MOD_NOREPEAT;

            uint vk = 0;
            if (Enum.TryParse<Keys>(key, true, out var keyVal))
            {
                vk = (uint)keyVal;
            }
            else if (!string.IsNullOrEmpty(key) && key.Length == 1)
            {
                vk = (uint)char.ToUpperInvariant(key[0]);
            }
            else
            {
                vk = (uint)Keys.C;
            }

            _isRegistered = NativeMethods.RegisterHotKey(_hWnd, HotkeyId, fsModifiers, vk);
            Debug.WriteLine($"RegisterHotKey for {modifiers}+{key} result: {_isRegistered}");
            return _isRegistered;
        }

        public void Unregister()
        {
            if (_isRegistered && _hWnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_hWnd, HotkeyId);
                _isRegistered = false;
            }
        }
    }
}
