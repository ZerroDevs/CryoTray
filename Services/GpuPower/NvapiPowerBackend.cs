using System;
using System.Runtime.InteropServices;
using System.Text;
using CryoTray.Models;

namespace CryoTray.Services.GpuPower
{
    /// <summary>
    /// NVIDIA power policies. nvapi64.dll is resolved only from System32 so an
    /// attacker-writable application folder cannot win the DLL search order.
    /// </summary>
    internal sealed class NvapiPowerBackend : IGpuPowerBackend
    {
        private const uint NvApiInitializeId = 0x0150E828;
        private const uint NvApiEnumPhysicalGpusId = 0xE5AC921F;
        private const uint NvApiGpuGetFullNameId = 0xCEEE8E9F;
        private const uint PowerPoliciesGetInfoId = 0x34206D86;
        private const uint PowerPoliciesGetStatusId = 0x70916171;
        private const uint PowerPoliciesSetStatusId = 0xAD95F5ED;
        private const int MaxPhysicalGpus = 64;

        private IntPtr _module;
        private NvApiInitializeDelegate? _initialize;
        private NvApiEnumPhysicalGpusDelegate? _enumPhysicalGpus;
        private NvApiGetFullNameDelegate? _getFullName;
        private NvApiPowerInfoDelegate? _getInfo;
        private NvApiPowerStatusDelegate? _getStatus;
        private NvApiPowerStatusDelegate? _setStatus;
        private NvGpuPowerInfo? _lastInfo;
        private GpuPowerBounds? _bounds;
        private IntPtr _selectedHandle;

        public string Vendor => "Nvidia";
        public GpuPowerBounds? Bounds => _bounds;

        public NvapiPowerBackend(string gpuName)
        {
            try
            {
                _module = NativeLibrary.Load(
                    "nvapi64.dll",
                    typeof(NvapiPowerBackend).Assembly,
                    DllImportSearchPath.System32);

                IntPtr queryPointer = NativeLibrary.GetExport(_module, "nvapi_QueryInterface");
                var query = Marshal.GetDelegateForFunctionPointer<NvApiQueryInterfaceDelegate>(queryPointer);

                _initialize = GetDelegate<NvApiInitializeDelegate>(query, NvApiInitializeId);
                _enumPhysicalGpus = GetDelegate<NvApiEnumPhysicalGpusDelegate>(query, NvApiEnumPhysicalGpusId);
                _getFullName = GetDelegate<NvApiGetFullNameDelegate>(query, NvApiGpuGetFullNameId);
                _getInfo = GetDelegate<NvApiPowerInfoDelegate>(query, PowerPoliciesGetInfoId);
                _getStatus = GetDelegate<NvApiPowerStatusDelegate>(query, PowerPoliciesGetStatusId);
                _setStatus = GetDelegate<NvApiPowerStatusDelegate>(query, PowerPoliciesSetStatusId);

                if (_initialize() != 0)
                {
                    throw new InvalidOperationException("NVAPI initialization failed.");
                }

                _selectedHandle = FindGpuHandle(gpuName);
                if (_selectedHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("The requested NVIDIA GPU was not found.");
                }

                var info = CreateInfo();
                if (_getInfo(_selectedHandle, ref info) != 0)
                {
                    throw new InvalidOperationException("NVAPI did not expose power policies.");
                }

                int policyIndex = FindUsablePolicy(info);
                if (policyIndex < 0)
                {
                    throw new InvalidOperationException("The VBIOS power range is locked.");
                }

                NvGpuPowerInfoEntry policy = info.Entries[policyIndex];
                _lastInfo = info;
                _bounds = GpuPowerMath.NormalizeBounds(
                    policy.MinPower / 1000.0,
                    policy.DefaultPower / 1000.0,
                    policy.MaxPower / 1000.0);

                if (!GpuPowerMath.IsUsable(_bounds))
                {
                    throw new InvalidOperationException("The VBIOS power range is not adjustable.");
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
            if (_bounds == null || _lastInfo == null || _getStatus == null)
            {
                error = "NVAPI is not initialized.";
                return false;
            }

            try
            {
                var status = CreateStatus();
                if (_selectedHandle == IntPtr.Zero || _getStatus!(_selectedHandle, ref status) != 0 || status.Count == 0)
                {
                    error = "The current NVIDIA power limit could not be read.";
                    return false;
                }

                NvGpuPowerInfo info = _lastInfo.Value;
                int policyIndex = FindUsablePolicy(info);
                if (policyIndex < 0)
                {
                    error = "The VBIOS power policy is unavailable.";
                    return false;
                }

                uint pstate = info.Entries[policyIndex].PState;
                NvGpuPowerStatusEntry entry = status.Entries[0];
                for (uint i = 0; i < status.Count && i < status.Entries.Length; i++)
                {
                    if (status.Entries[i].PState == pstate)
                    {
                        entry = status.Entries[i];
                        break;
                    }
                }

                if (entry.Power == 0)
                {
                    percent = _bounds.DefaultPercent;
                    return true;
                }

                percent = Math.Round(entry.Power / 1000.0, 2);
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
            if (_bounds == null || _lastInfo == null || _getStatus == null || _setStatus == null)
            {
                error = "NVAPI is not initialized.";
                return false;
            }

            try
            {
                GpuPowerBounds bounds = _bounds;
                double target = GpuPowerMath.Clamp(bounds, percent);
                IntPtr handle = _selectedHandle;
                if (handle == IntPtr.Zero)
                {
                    error = "The NVIDIA GPU handle is unavailable.";
                    return false;
                }

                var info = CreateInfo();
                if (_getInfo!(handle, ref info) != 0)
                {
                    error = "The VBIOS power range could not be re-read.";
                    return false;
                }

                int policyIndex = FindUsablePolicy(info);
                if (policyIndex < 0)
                {
                    error = "The VBIOS power range is locked.";
                    return false;
                }

                NvGpuPowerInfoEntry policy = info.Entries[policyIndex];
                GpuPowerBounds freshBounds = GpuPowerMath.NormalizeBounds(
                    policy.MinPower / 1000.0,
                    policy.DefaultPower / 1000.0,
                    policy.MaxPower / 1000.0);
                if (!GpuPowerMath.IsUsable(freshBounds))
                {
                    error = "The VBIOS power range is not adjustable.";
                    return false;
                }

                target = GpuPowerMath.Clamp(freshBounds, target);
                var status = CreateStatus();
                status.Count = 1;
                status.Entries[0] = new NvGpuPowerStatusEntry
                {
                    PState = policy.PState,
                    Unknown1 = 0,
                    Power = (uint)Math.Round(target * 1000.0),
                    Unknown2 = 0
                };

                int setResult = _setStatus!(handle, ref status);
                if (setResult != 0)
                {
                    error = $"NVAPI rejected the power limit (status 0x{setResult:X8}).";
                    return false;
                }

                _lastInfo = info;
                _bounds = freshBounds;

                if (TryReadPercent(out double readback, out string readError))
                {
                    if (Math.Abs(readback - target) > 0.75)
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
            if (_bounds == null)
            {
                error = "NVAPI is not initialized.";
                return false;
            }

            bool restored = TryApplyPercent(_bounds.DefaultPercent, out _, out error);
            if (restored)
            {
                error = "";
                return true;
            }

            error = string.IsNullOrWhiteSpace(error)
                ? $"The default {_bounds.DefaultPercent:0.#}% limit could not be restored."
                : error;
            return false;
        }

        private IntPtr FindGpuHandle(string preferredName)
        {
            if (_enumPhysicalGpus == null || _getFullName == null)
            {
                return IntPtr.Zero;
            }

            var handles = new IntPtr[MaxPhysicalGpus];
            int result = _enumPhysicalGpus(handles, out uint count);
            if (result != 0 || count == 0)
            {
                return IntPtr.Zero;
            }

            string wanted = NormalizeName(preferredName);
            IntPtr first = IntPtr.Zero;
            uint limit = Math.Min(count, (uint)handles.Length);
            for (uint i = 0; i < limit; i++)
            {
                if (handles[i] == IntPtr.Zero)
                {
                    continue;
                }

                if (first == IntPtr.Zero)
                {
                    first = handles[i];
                }

                var name = new StringBuilder(64);
                if (_getFullName(handles[i], name) != 0)
                {
                    continue;
                }

                string candidate = NormalizeName(name.ToString());
                if (wanted.Length > 0 &&
                    (candidate.Contains(wanted, StringComparison.Ordinal) ||
                     wanted.Contains(candidate, StringComparison.Ordinal)))
                {
                    return handles[i];
                }
            }

            return string.IsNullOrWhiteSpace(wanted) ? first : IntPtr.Zero;
        }

        private static int FindUsablePolicy(NvGpuPowerInfo info)
        {
            int available = Math.Min(4, info.Entries.Length);
            for (int i = 0; i < available; i++)
            {
                NvGpuPowerInfoEntry entry = info.Entries[i];
                if (entry.DefaultPower > entry.MinPower && entry.MaxPower >= entry.DefaultPower)
                {
                    return i;
                }
            }
            return -1;
        }

        private static NvGpuPowerInfo CreateInfo()
        {
            var info = new NvGpuPowerInfo
            {
                Version = (uint)(Marshal.SizeOf<NvGpuPowerInfo>() | (1 << 16)),
                Entries = new NvGpuPowerInfoEntry[4]
            };
            return info;
        }

        private static NvGpuPowerStatus CreateStatus()
        {
            return new NvGpuPowerStatus
            {
                Version = (uint)(Marshal.SizeOf<NvGpuPowerStatus>() | (1 << 16)),
                Entries = new NvGpuPowerStatusEntry[4]
            };
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

        private static T GetDelegate<T>(NvApiQueryInterfaceDelegate query, uint id) where T : Delegate
        {
            IntPtr pointer = query(id);
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException($"NVAPI function 0x{id:X8} is unavailable.");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        public void Dispose()
        {
            _selectedHandle = IntPtr.Zero;
            _initialize = null;
            _enumPhysicalGpus = null;
            _getFullName = null;
            _getInfo = null;
            _getStatus = null;
            _setStatus = null;
            _lastInfo = null;
            _bounds = null;

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
        private delegate IntPtr NvApiQueryInterfaceDelegate(uint functionId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvApiInitializeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvApiEnumPhysicalGpusDelegate(
            [Out] IntPtr[] handles,
            out uint count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int NvApiGetFullNameDelegate(
            IntPtr gpuHandle,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvApiPowerInfoDelegate(IntPtr gpuHandle, ref NvGpuPowerInfo info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvApiPowerStatusDelegate(IntPtr gpuHandle, ref NvGpuPowerStatus status);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvGpuPowerInfo
        {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public NvGpuPowerInfoEntry[] Entries;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvGpuPowerInfoEntry
        {
            public uint PState;
            public uint Unknown1;
            public uint Unknown2;
            public uint MinPower;
            public uint Unknown3;
            public uint Unknown4;
            public uint DefaultPower;
            public uint Unknown5;
            public uint Unknown6;
            public uint MaxPower;
            public uint Unknown7;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvGpuPowerStatus
        {
            public uint Version;
            public uint Count;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public NvGpuPowerStatusEntry[] Entries;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvGpuPowerStatusEntry
        {
            public uint PState;
            public uint Unknown1;
            public uint Power;
            public uint Unknown2;
        }
    }
}
