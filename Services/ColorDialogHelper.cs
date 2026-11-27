using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CryoTray.Services
{
    /// <summary>
    /// Native Win32 ChooseColorW color picker wrapper replacing System.Windows.Forms.ColorDialog.
    /// </summary>
    internal static class ColorDialogHelper
    {
        private const uint CC_RGBINIT = 0x00000001;
        private const uint CC_FULLOPEN = 0x00000002;
        private const uint CC_SOLIDCOLOR = 0x00000080;
        private const uint CC_ANYCOLOR = 0x00000100;

        private static readonly uint[] CustomColors = new uint[16];

        public static Color? PickColor(Window? owner, Color initialColor)
        {
            IntPtr ownerHwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
            return PickColor(ownerHwnd, initialColor);
        }

        public static Color? PickColor(IntPtr ownerHwnd, Color initialColor)
        {
            IntPtr custPtr = Marshal.AllocHGlobal(CustomColors.Length * sizeof(uint));
            try
            {
                int[] signed = Array.ConvertAll(CustomColors, c => unchecked((int)c));
                Marshal.Copy(signed, 0, custPtr, signed.Length);

                var cc = new NativeMethods.CHOOSECOLORW
                {
                    lStructSize = (uint)Marshal.SizeOf<NativeMethods.CHOOSECOLORW>(),
                    hwndOwner = ownerHwnd,
                    rgbResult = ColorToColorRef(initialColor),
                    lpCustColors = custPtr,
                    Flags = CC_RGBINIT | CC_FULLOPEN | CC_ANYCOLOR | CC_SOLIDCOLOR
                };

                if (!NativeMethods.ChooseColorW(ref cc))
                {
                    return null;
                }

                Marshal.Copy(custPtr, signed, 0, signed.Length);
                for (int i = 0; i < CustomColors.Length; i++)
                {
                    CustomColors[i] = unchecked((uint)signed[i]);
                }

                return ColorRefToColor(cc.rgbResult);
            }
            finally
            {
                Marshal.FreeHGlobal(custPtr);
            }
        }

        public static uint ColorToColorRef(Color color) =>
            (uint)(color.R | (color.G << 8) | (color.B << 16));

        public static Color ColorRefToColor(uint colorRef) =>
            Color.FromRgb(
                (byte)(colorRef & 0xFF),
                (byte)((colorRef >> 8) & 0xFF),
                (byte)((colorRef >> 16) & 0xFF));
    }
}
