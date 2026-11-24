using System;
using System.Runtime.InteropServices;
using System.Text;
using CryoTray.Models;

namespace CryoTray.Services.GpuPower
{
    /// <summary>
    /// AMD OverdriveN (ADL2) power limit path. This covers the legacy ADL surface
    /// requested for AMD adapters and works alongside AMD's newer tuning stack.
    /// </summary>
    internal sealed class AdlPowerBackend : IGpuPowerBackend
    {
        private const int AdlOk = 0;
        private const int OdnControlManual = 3;
        private const int OdnControlDefault = 1;

        private IntPtr _module;
        private IntPtr _context;
        private int _adapterIndex = -1;
        private GpuPowerBounds? _bounds;

        private AdlMainControlCreateDelegate? _create;
        private AdlMainControlDestroyDelegate? _destroy;
        private AdlNumberOfAdaptersDelegate? _numberOfAdapters;
        private AdlAdapterInfoGetDelegate? _adapterInfoGet;
        private AdlOdnCapabilitiesDelegate? _capabilities;
        private AdlOdnPowerLimitGetDelegate? _powerLimitGet;
        private AdlOdnPowerLimitSetDelegate? _powerLimitSet;

        private static readonly AdlMallocCallback MallocCallback = size => Marshal.AllocHGlobal(size);

        public string Vendor => "AMD";
        public GpuPowerBounds? Bounds => _bounds;

        public AdlPowerBackend(string gpuName)
        {
            try
            {
                _module = NativeLibrary.Load(
                    "atiadlxx.dll",
                    typeof(AdlPowerBackend).Assembly,
                    DllImportSearchPath.System32);

                _create = GetDelegate<AdlMainControlCreateDelegate>("ADL2_Main_Control_Create");
                _destroy = GetDelegate<AdlMainControlDestroyDelegate>("ADL2_Main_Control_Destroy");
                _numberOfAdapters = GetDelegate<AdlNumberOfAdaptersDelegate>("ADL2_Adapter_NumberOfAdapters_Get");
                _adapterInfoGet = GetDelegate<AdlAdapterInfoGetDelegate>("ADL2_Adapter_AdapterInfo_Get");
                _capabilities = GetDelegate<AdlOdnCapabilitiesDelegate>("ADL2_OverdriveN_CapabilitiesX2_Get");
                _powerLimitGet = GetDelegate<AdlOdnPowerLimitGetDelegate>("ADL2_OverdriveN_PowerLimit_Get");
                _powerLimitSet = GetDelegate<AdlOdnPowerLimitSetDelegate>("ADL2_OverdriveN_PowerLimit_Set");

                IntPtr mallocPointer = Marshal.GetFunctionPointerForDelegate(MallocCallback);
                int createResult = _create(mallocPointer, 1, out _context);
                if (createResult != AdlOk || _context == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"ADL initialization failed (code {createResult}).");
                }

                if (_numberOfAdapters(_context, out int adapterCount) != AdlOk || adapterCount <= 0)
                {
                    throw new InvalidOperationException("ADL returned no adapters.");
                }

                _adapterIndex = FindAdapter(gpuName, adapterCount);
                if (_adapterIndex < 0)
                {
                    throw new InvalidOperationException("The requested AMD GPU was not found.");
                }

                if (!TryReadCapabilities(out AdlOdnCapabilitiesX2 caps, out string capsError))
                {
                    throw new InvalidOperationException(capsError);
                }

                int defaultPercent = caps.Power.Default > 0 ? caps.Power.Default : 100;
                _bounds = GpuPowerMath.NormalizeBounds(
                    caps.Power.Min,
                    defaultPercent,
                    caps.Power.Max);

                if (!GpuPowerMath.IsUsable(_bounds))
                {
                    throw new InvalidOperationException("The VBIOS power range is locked.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool TryReadPercent(out double percent, out string error)
        {
            percent = double.NaN;
            error = "";
            if (_context == IntPtr.Zero || _adapterIndex < 0 || _powerLimitGet == null)
            {
                error = "ADL is not initialized.";
                return false;
            }

            try
            {
                var setting = new AdlOdnPowerLimitSetting();
                int result = _powerLimitGet(_context, _adapterIndex, out setting);
                if (result != AdlOk)
                {
                    error = $"ADL could not read the power limit (code {result}).";
                    return false;
                }

                percent = Math.Round((double)setting.TdpLimit, 2);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryApplyPercent(double percent, out double appliedPercent, out string error)
        {
            appliedPercent = double.NaN;
            error = "";
            if (_context == IntPtr.Zero || _adapterIndex < 0 ||
                _powerLimitGet == null || _powerLimitSet == null)
            {
                error = "ADL is not initialized.";
                return false;
            }

            try
            {
                if (!TryReadCapabilities(out AdlOdnCapabilitiesX2 caps, out string capsError))
                {
                    error = capsError;
                    return false;
                }

                int defaultPercent = caps.Power.Default > 0 ? caps.Power.Default : 100;
                GpuPowerBounds fresh = GpuPowerMath.NormalizeBounds(
                    caps.Power.Min,
                    defaultPercent,
                    caps.Power.Max);
                if (!GpuPowerMath.IsUsable(fresh))
                {
                    error = "The VBIOS power range is not adjustable.";
                    return false;
                }

                double target = GpuPowerMath.Clamp(fresh, percent);
                int result = _powerLimitGet(_context, _adapterIndex, out AdlOdnPowerLimitSetting setting);
                if (result != AdlOk)
                {
                    error = $"ADL could not read the current power limit (code {result}).";
                    return false;
                }

                setting.Mode = OdnControlManual;
                setting.TdpLimit = (int)Math.Round(target);
                result = _powerLimitSet(_context, _adapterIndex, ref setting);
                if (result != AdlOk)
                {
                    error = $"ADL rejected the power limit (code {result}).";
                    return false;
                }

                _bounds = fresh;
                if (TryReadPercent(out double readback, out string readError))
                {
                    if (Math.Abs(readback - target) > 1.0)
                    {
                        error = $"The driver reported {readback:0.#}% after applying {target:0.#}%.";
                        return false;
                    }
                    appliedPercent = readback;
                    error = "";
                }
                else
                {
                    appliedPercent = target;
                    error = readError;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryRestoreDefault(out string error)
        {
            error = "";
            if (_context == IntPtr.Zero || _adapterIndex < 0 || _powerLimitSet == null || _bounds == null)
            {
                error = "ADL is not initialized.";
                return false;
            }

            try
            {
                int result = _powerLimitGet!(_context, _adapterIndex, out AdlOdnPowerLimitSetting setting);
                if (result != AdlOk)
                {
                    error = $"ADL could not read the power limit before reset (code {result}).";
                    return false;
                }

                setting.Mode = OdnControlDefault;
                setting.TdpLimit = (int)Math.Round(_bounds.DefaultPercent);
                result = _powerLimitSet(_context, _adapterIndex, ref setting);
                if (result != AdlOk)
                {
                    error = $"ADL could not restore the default power limit (code {result}).";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool TryReadCapabilities(out AdlOdnCapabilitiesX2 caps, out string error)
        {
            caps = new AdlOdnCapabilitiesX2();
            error = "";
            if (_capabilities == null || _context == IntPtr.Zero || _adapterIndex < 0)
            {
                error = "ADL capabilities are unavailable.";
                return false;
            }

            int result = _capabilities(_context, _adapterIndex, out caps);
            if (result != AdlOk)
            {
                error = $"ADL could not read VBIOS power ranges (code {result}).";
                return false;
            }

            if (caps.Power.Max <= 0 || caps.Power.Max > 200)
            {
                error = "The VBIOS power range is not expressed as a safe percentage.";
                return false;
            }

            return true;
        }

        private int FindAdapter(string preferredName, int adapterCount)
        {
            if (_adapterInfoGet == null || adapterCount <= 0)
            {
                return -1;
            }

            int count = Math.Min(adapterCount, 64);
            int structSize = Marshal.SizeOf<AdlAdapterInfo>();
            IntPtr buffer = Marshal.AllocHGlobal(structSize * count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var empty = new AdlAdapterInfo { Size = structSize };
                    Marshal.StructureToPtr(empty, IntPtr.Add(buffer, i * structSize), false);
                }

                if (_adapterInfoGet(_context, buffer, structSize * count) != AdlOk)
                {
                    return -1;
                }

                string wanted = NormalizeName(preferredName);
                int fallback = -1;
                for (int i = 0; i < count; i++)
                {
                    AdlAdapterInfo info = Marshal.PtrToStructure<AdlAdapterInfo>(
                        IntPtr.Add(buffer, i * structSize));
                    if (info.BusNumber < 0)
                    {
                        continue;
                    }

                    if (fallback < 0)
                    {
                        fallback = info.AdapterIndex;
                    }

                    string adapterName = NormalizeName(
                        string.IsNullOrWhiteSpace(info.AdapterName) ? info.DisplayName : info.AdapterName);
                    if (wanted.Length > 0 &&
                        (adapterName.Contains(wanted, StringComparison.Ordinal) ||
                         wanted.Contains(adapterName, StringComparison.Ordinal)))
                    {
                        return info.AdapterIndex;
                    }
                }

                return string.IsNullOrWhiteSpace(wanted) ? fallback : -1;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private T GetDelegate<T>(string exportName) where T : Delegate
        {
            IntPtr pointer = NativeLibrary.GetExport(_module, exportName);
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            if (_destroy != null && _context != IntPtr.Zero)
            {
                try
                {
                    _destroy(_context);
                }
                catch
                {
                }
            }

            _context = IntPtr.Zero;
            _adapterIndex = -1;
            _bounds = null;
            _create = null;
            _destroy = null;
            _numberOfAdapters = null;
            _adapterInfoGet = null;
            _capabilities = null;
            _powerLimitGet = null;
            _powerLimitSet = null;

            if (_module != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(_module);
                }
                catch
                {
                }
                _module = IntPtr.Zero;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AdlMallocCallback(int size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlMainControlCreateDelegate(
            IntPtr mallocCallback,
            int enumConnectedAdapters,
            out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlMainControlDestroyDelegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlNumberOfAdaptersDelegate(IntPtr context, out int numberOfAdapters);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlAdapterInfoGetDelegate(IntPtr context, IntPtr info, int size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlOdnCapabilitiesDelegate(
            IntPtr context,
            int adapterIndex,
            out AdlOdnCapabilitiesX2 capabilities);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlOdnPowerLimitGetDelegate(
            IntPtr context,
            int adapterIndex,
            out AdlOdnPowerLimitSetting setting);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlOdnPowerLimitSetDelegate(
            IntPtr context,
            int adapterIndex,
            ref AdlOdnPowerLimitSetting setting);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlAdapterInfo
        {
            public int Size;
            public int AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Udid;
            public int BusNumber;
            public int DeviceNumber;
            public int FunctionNumber;
            public int VendorId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DisplayName;
            public int Present;
            public int Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string PnpString;
            public int OsDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlOdnParameterRange
        {
            public int Mode;
            public int Min;
            public int Max;
            public int Step;
            public int Default;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlOdnCapabilitiesX2
        {
            public int MaxPerformanceLevels;
            public int Flags;
            public AdlOdnParameterRange EngineClock;
            public AdlOdnParameterRange MemoryClock;
            public AdlOdnParameterRange Vddc;
            public AdlOdnParameterRange Power;
            public AdlOdnParameterRange PowerTuneTemperature;
            public AdlOdnParameterRange FanTemperature;
            public AdlOdnParameterRange FanSpeed;
            public AdlOdnParameterRange MinimumPerformanceClock;
            public AdlOdnParameterRange ThrottleNotification;
            public AdlOdnParameterRange AutoSystemClock;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlOdnPowerLimitSetting
        {
            public int Mode;
            public int TdpLimit;
            public int MaxOperatingTemperature;
        }
    }
}
