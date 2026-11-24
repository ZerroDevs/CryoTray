using System;
using System.Runtime.InteropServices;
using System.Text;
using CryoTray.Models;

namespace CryoTray.Services.GpuPower
{
    /// <summary>
    /// AMD ADLX manual power tuning. ADLX exposes a COM-like C ABI with no managed
    /// wrapper, so this class uses the documented vtable slots directly and never
    /// needs a native shim. ADL OverdriveN remains the fallback for older drivers.
    /// </summary>
    internal sealed class AdlxPowerBackend : IGpuPowerBackend
    {
        private const int AdlxOk = 0;
        private const int AdlxAlreadyEnabled = 1;
        private const int AdlxAlreadyInitialized = 2;
        private const int AdlxGpuTypeDiscrete = 2;
        private const ulong AdlxMinimumVersion = 1UL << 48; // 1.0.0.0

        private IntPtr _module;
        private IntPtr _system;
        private IntPtr _gpuTuningServices;
        private IntPtr _gpu;
        private IntPtr _manualPowerTuning;
        private IntPtr _adlMapping;
        private AdlxTerminateDelegate? _terminate;
        private GpuPowerBounds? _bounds;

        public string Vendor => "AMD";
        public GpuPowerBounds? Bounds => _bounds;

        public AdlxPowerBackend(string gpuName)
        {
            try
            {
                _module = NativeLibrary.Load(
                    "amdadlx64.dll",
                    typeof(AdlxPowerBackend).Assembly,
                    DllImportSearchPath.System32);

                _terminate = GetExport<AdlxTerminateDelegate>("ADLXTerminate")
                    ?? throw new InvalidOperationException("ADLX terminate is unavailable.");
                var initialize = GetExport<AdlxInitializeDelegate>("ADLXInitialize");
                var initialize2 = GetExport<AdlxInitialize2Delegate>("ADLXInitialize2");
                if (initialize == null && initialize2 == null)
                {
                    throw new InvalidOperationException("ADLX initialization is unavailable.");
                }

                ulong version = AdlxMinimumVersion;
                var queryVersion = GetExport<AdlxQueryFullVersionDelegate>("ADLXQueryFullVersion");
                if (queryVersion != null)
                {
                    try
                    {
                        if (queryVersion(out ulong runtimeVersion) == AdlxOk && runtimeVersion != 0)
                        {
                            version = runtimeVersion;
                        }
                    }
                    catch
                    {
                    }
                }

                int result;
                if (initialize != null)
                {
                    result = initialize(version, out _system);
                }
                else
                {
                    result = initialize2!(version, out _system, out _adlMapping);
                }

                if (!Succeeded(result) || _system == IntPtr.Zero)
                {
                    // Some runtimes reject their own full version when the caller
                    // asks for a newer SDK surface than it exposes.
                    ResetFailedInitialization();
                    result = initialize != null
                        ? initialize(AdlxMinimumVersion, out _system)
                        : initialize2!(AdlxMinimumVersion, out _system, out _adlMapping);
                }

                if (!Succeeded(result) || _system == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"ADLX initialization failed (code {result}).");
                }

                _gpuTuningServices = GetGpuTuningServices(_system, out result);
                if (!Succeeded(result) || _gpuTuningServices == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"ADLX GPU tuning is unavailable (code {result}).");
                }

                _gpu = SelectGpuFromSystem(_system, gpuName, out result);
                if (!Succeeded(result) || _gpu == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"ADLX did not expose a matching GPU (code {result}).");
                }

                var supported = GetVtblDelegate<IsSupportedManualPowerTuningDelegate>(
                    _gpuTuningServices,
                    11);
                result = supported(_gpuTuningServices, _gpu, out byte isSupported);
                if (!Succeeded(result) || isSupported == 0)
                {
                    throw new InvalidOperationException("The VBIOS does not support ADLX manual power tuning.");
                }

                var getManualPowerTuning = GetVtblDelegate<GetManualPowerTuningDelegate>(
                    _gpuTuningServices,
                    17);
                IntPtr tuningInterface = IntPtr.Zero;
                result = getManualPowerTuning(_gpuTuningServices, _gpu, out tuningInterface);
                if (!Succeeded(result) || tuningInterface == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"ADLX did not expose manual power tuning (code {result}).");
                }

                try
                {
                    _manualPowerTuning = QueryInterface(tuningInterface, "IADLXManualPowerTuning");
                    if (_manualPowerTuning == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("The ADLX manual power interface could not be resolved.");
                    }
                }
                finally
                {
                    Release(tuningInterface);
                }

                if (!TryReadRange(out AdlxIntRange range, out string rangeError))
                {
                    throw new InvalidOperationException(rangeError);
                }

                _bounds = CreateBounds(range);
                if (!GpuPowerMath.IsUsable(_bounds))
                {
                    throw new InvalidOperationException("The ADLX VBIOS power range is not adjustable.");
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
            if (_manualPowerTuning == IntPtr.Zero)
            {
                error = "ADLX is not initialized.";
                return false;
            }

            try
            {
                var getPowerLimit = GetVtblDelegate<GetPowerLimitDelegate>(_manualPowerTuning, 4);
                int result = getPowerLimit(_manualPowerTuning, out int value);
                if (!Succeeded(result))
                {
                    error = $"ADLX could not read the power limit (code {result}).";
                    return false;
                }

                percent = Math.Round(Math.Clamp(100.0 + value, 0.0, 100.0), 2);
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
            if (_manualPowerTuning == IntPtr.Zero)
            {
                error = "ADLX is not initialized.";
                return false;
            }

            try
            {
                if (!TryReadRange(out AdlxIntRange range, out string rangeError))
                {
                    error = rangeError;
                    return false;
                }

                GpuPowerBounds fresh = CreateBounds(range);
                if (!GpuPowerMath.IsUsable(fresh))
                {
                    error = "The ADLX VBIOS power range is not adjustable.";
                    return false;
                }

                double absolute = GpuPowerMath.Clamp(fresh, percent);
                int offset = (int)Math.Round(absolute - 100.0);
                if (range.Step > 1)
                {
                    offset = range.MinValue +
                        (int)Math.Round((offset - range.MinValue) / (double)range.Step) * range.Step;
                }

                int target = (int)Math.Round(GpuPowerMath.Clamp(fresh, 100.0 + offset));
                offset = target - 100;
                var setPowerLimit = GetVtblDelegate<SetPowerLimitDelegate>(_manualPowerTuning, 5);
                int result = setPowerLimit(_manualPowerTuning, offset);
                if (!Succeeded(result))
                {
                    error = result == 18
                        ? "ADLX requires an overdrive factory reset before the power limit can change."
                        : $"ADLX rejected the power limit (code {result}).";
                    return false;
                }

                _bounds = fresh;
                if (TryReadPercent(out double readback, out string readError))
                {
                    if (Math.Abs(readback - target) > 1.0)
                    {
                        // The driver accepted the write but applied a different
                        // value. Report success so the service marks the cap as
                        // written and still restores the default on exit.
                        appliedPercent = readback;
                        error = $"The driver reported {readback:0.#}% after applying {target}%.";
                        return true;
                    }

                    appliedPercent = readback;
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
            if (_bounds == null)
            {
                error = "ADLX is not initialized.";
                return false;
            }

            bool restored = TryApplyPercent(_bounds.DefaultPercent, out _, out error);
            if (restored && string.IsNullOrWhiteSpace(error))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"The default {_bounds.DefaultPercent:0.#}% limit could not be restored.";
            }
            return false;
        }

        private bool TryReadRange(out AdlxIntRange range, out string error)
        {
            range = default;
            error = "";
            if (_manualPowerTuning == IntPtr.Zero)
            {
                error = "ADLX is not initialized.";
                return false;
            }

            try
            {
                var getRange = GetVtblDelegate<GetPowerLimitRangeDelegate>(_manualPowerTuning, 3);
                int result = getRange(_manualPowerTuning, out range);
                if (!Succeeded(result))
                {
                    error = $"ADLX could not read the VBIOS power range (code {result}).";
                    return false;
                }

                if (range.MinValue < -200 ||
                    range.MaxValue > 200 ||
                    100.0 + range.MaxValue <= 0)
                {
                    error = "The ADLX VBIOS power range is not expressed as a safe offset.";
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

        // ADLX reports the manual power limit as a signed percentage offset from
        // the VBIOS default (for example -30..+10), while CryoTray presents the
        // absolute 70/85/100 percentages used elsewhere in the UI.
        private static GpuPowerBounds CreateBounds(AdlxIntRange range)
        {
            return GpuPowerMath.NormalizeBounds(
                100.0 + range.MinValue,
                100.0,
                100.0 + range.MaxValue);
        }

        private static IntPtr GetGpuTuningServices(IntPtr system, out int result)
        {
            var getServices = GetVtblDelegate<GetGpuTuningServicesDelegate>(system, 8);
            result = getServices(system, out IntPtr services);
            return Succeeded(result) ? services : IntPtr.Zero;
        }

        private static IntPtr SelectGpuFromSystem(
            IntPtr system,
            string preferredName,
            out int result)
        {
            result = AdlxOk;
            var getGpus = GetVtblDelegate<GetGPUsDelegate>(system, 1);
            result = getGpus(system, out IntPtr list);
            if (!Succeeded(result) || list == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                var size = GetVtblDelegate<GpuListSizeDelegate>(list, 3);
                var at = GetVtblDelegate<GpuListAtDelegate>(list, 11);
                string wanted = NormalizeName(preferredName);
                IntPtr fallback = IntPtr.Zero;
                bool fallbackIsDiscrete = false;
                uint count = size(list);
                for (uint i = 0; i < count; i++)
                {
                    result = at(list, i, out IntPtr gpu);
                    if (!Succeeded(result) || gpu == IntPtr.Zero)
                    {
                        result = AdlxOk;
                        continue;
                    }

                    string name = ReadGpuName(gpu);
                    int type = ReadGpuType(gpu);
                    if (wanted.Length > 0 &&
                        string.Equals(NormalizeName(name), wanted, StringComparison.Ordinal))
                    {
                        Release(fallback);
                        return gpu;
                    }

                    if (fallback == IntPtr.Zero)
                    {
                        fallback = gpu;
                        fallbackIsDiscrete = type == AdlxGpuTypeDiscrete;
                    }
                    else if (type == AdlxGpuTypeDiscrete && !fallbackIsDiscrete)
                    {
                        Release(fallback);
                        fallback = gpu;
                        fallbackIsDiscrete = true;
                    }
                    else
                    {
                        Release(gpu);
                    }
                }

                return fallback;
            }
            finally
            {
                Release(list);
            }
        }

        private static string ReadGpuName(IntPtr gpu)
        {
            var getName = GetVtblDelegate<GpuNameDelegate>(gpu, 7);
            return Succeeded(getName(gpu, out IntPtr name)) && name != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(name) ?? ""
                : "";
        }

        private static int ReadGpuType(IntPtr gpu)
        {
            var getType = GetVtblDelegate<GpuTypeDelegate>(gpu, 5);
            return Succeeded(getType(gpu, out int type)) ? type : 0;
        }

        private static IntPtr QueryInterface(IntPtr source, string interfaceId)
        {
            var query = GetVtblDelegate<QueryInterfaceDelegate>(source, 2);
            return query(source, interfaceId, out IntPtr target) == AdlxOk ? target : IntPtr.Zero;
        }

        private T? GetExport<T>(string name) where T : Delegate
        {
            try
            {
                IntPtr pointer = NativeLibrary.GetExport(_module, name);
                return Marshal.GetDelegateForFunctionPointer<T>(pointer);
            }
            catch
            {
                return null;
            }
        }

        private static T GetVtblDelegate<T>(IntPtr instance, int index) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            IntPtr function = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(function);
        }

        private static int Release(IntPtr instance)
        {
            if (instance == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                var release = GetVtblDelegate<ReleaseDelegate>(instance, 1);
                return release(instance);
            }
            catch
            {
                return -1;
            }
        }

        private static bool Succeeded(int result)
        {
            return result == AdlxOk ||
                result == AdlxAlreadyEnabled ||
                result == AdlxAlreadyInitialized;
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
            Release(_manualPowerTuning);
            _manualPowerTuning = IntPtr.Zero;
            Release(_gpu);
            _gpu = IntPtr.Zero;
            Release(_gpuTuningServices);
            _gpuTuningServices = IntPtr.Zero;
            Release(_adlMapping);
            _adlMapping = IntPtr.Zero;

            if (_system != IntPtr.Zero && _terminate != null)
            {
                try
                {
                    _terminate();
                }
                catch
                {
                }
            }
            _system = IntPtr.Zero;
            _terminate = null;

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
            _bounds = null;
        }

        private void ResetFailedInitialization()
        {
            Release(_adlMapping);
            _adlMapping = IntPtr.Zero;
            if (_system != IntPtr.Zero && _terminate != null)
            {
                try
                {
                    _terminate();
                }
                catch
                {
                }
            }
            _system = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlxIntRange
        {
            public int MinValue;
            public int MaxValue;
            public int Step;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxQueryFullVersionDelegate(out ulong fullVersion);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxInitializeDelegate(ulong version, out IntPtr system);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxInitialize2Delegate(
            ulong version,
            out IntPtr system,
            out IntPtr adlMapping);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxTerminateDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetGpuTuningServicesDelegate(IntPtr system, out IntPtr services);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetGPUsDelegate(IntPtr system, out IntPtr gpus);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GpuListSizeDelegate(IntPtr list);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GpuListAtDelegate(IntPtr list, uint index, out IntPtr gpu);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GpuNameDelegate(IntPtr gpu, out IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GpuTypeDelegate(IntPtr gpu, out int gpuType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IsSupportedManualPowerTuningDelegate(
            IntPtr services,
            IntPtr gpu,
            out byte supported);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetManualPowerTuningDelegate(
            IntPtr services,
            IntPtr gpu,
            out IntPtr tuning);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPowerLimitRangeDelegate(
            IntPtr tuning,
            out AdlxIntRange range);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPowerLimitDelegate(IntPtr tuning, out int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPowerLimitDelegate(IntPtr tuning, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(
            IntPtr source,
            [MarshalAs(UnmanagedType.LPWStr)] string interfaceId,
            out IntPtr target);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr instance);
    }
}
