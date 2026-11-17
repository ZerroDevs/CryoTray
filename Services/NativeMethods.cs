using System;
using System.Runtime.InteropServices;

namespace CryoTray.Services
{
    public static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        // Modifiers for RegisterHotKey
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;

        // Power Management APIs
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr pActivePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerEnumerate(
            IntPtr RootPowerKey,
            IntPtr SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            uint AccessFlags,
            uint Index,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint DcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint AcValueIndex);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);

        // Power Guid Constants
        public static readonly Guid GUID_POWERSCHEME_BALANCED = new("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid GUID_POWERSCHEME_HIGH_PERF = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid GUID_POWERSCHEME_POWER_SAVER = new("a1841308-3541-4fab-bc81-f71556f20b4a");

        // Processor Power Management (SUB_PROCESSOR)
        public static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MAX = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MIN = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid GUID_PROCESSOR_PERFBOOST_MODE = new("be337238-0d82-4146-a960-4f3749d470c7");

        public const uint ACCESS_SCHEME = 16;
    }
}
