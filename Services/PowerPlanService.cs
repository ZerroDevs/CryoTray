using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CryoTray.Models;

namespace CryoTray.Services
{
    public class PowerSchemeInfo
    {
        public Guid Guid { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class PowerPlanService
    {
        private static PowerPlanService? _instance;
        public static PowerPlanService Instance => _instance ??= new PowerPlanService();

        public const string CryoTrayCoolingSchemeName = "CryoTray Cooling";
        public static readonly Guid BalancedGuid = NativeMethods.GUID_POWERSCHEME_BALANCED;

        public event Action<bool>? CoolingModeChanged;

        private Guid? _cryoTrayCoolingGuid;

        public bool IsCoolingModeActive { get; private set; }

        public PowerPlanService()
        {
            RefreshState();
        }

        public List<PowerSchemeInfo> GetInstalledSchemes()
        {
            var schemes = new List<PowerSchemeInfo>();
            uint index = 0;
            uint bufferSize = 16;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

            Guid activeGuid = GetActiveSchemeGuid();

            try
            {
                while (NativeMethods.PowerEnumerate(
                           IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                           NativeMethods.ACCESS_SCHEME, index, buffer, ref bufferSize) == 0)
                {
                    byte[] guidBytes = new byte[16];
                    Marshal.Copy(buffer, guidBytes, 0, 16);
                    Guid schemeGuid = new(guidBytes);

                    string name = ReadSchemeFriendlyName(schemeGuid);
                    schemes.Add(new PowerSchemeInfo
                    {
                        Guid = schemeGuid,
                        Name = name,
                        IsActive = (schemeGuid == activeGuid)
                    });

                    index++;
                    bufferSize = 16;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            // Fallback to powercfg parsing if Win32 API returned empty
            if (schemes.Count == 0)
            {
                schemes = ParsePowerCfgList();
            }

            return schemes;
        }

        public Guid GetActiveSchemeGuid()
        {
            uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid);
            if (result == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    Guid active = Marshal.PtrToStructure<Guid>(pGuid);
                    return active;
                }
                finally
                {
                    NativeMethods.LocalFree(pGuid);
                }
            }

            return BalancedGuid;
        }

        private string ReadSchemeFriendlyName(Guid schemeGuid)
        {
            uint bufferSize = 512;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                uint result = NativeMethods.PowerReadFriendlyName(
                    IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize);

                if (result == 0 && bufferSize > 0)
                {
                    return Marshal.PtrToStringUni(buffer) ?? schemeGuid.ToString();
                }
            }
            catch
            {
                // Ignore
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return schemeGuid.ToString();
        }

        public Guid EnsureCryoTrayCoolingScheme()
        {
            if (_cryoTrayCoolingGuid.HasValue)
            {
                // Verify it still exists in system
                var existing = GetInstalledSchemes().FirstOrDefault(s => s.Guid == _cryoTrayCoolingGuid.Value);
                if (existing != null)
                {
                    return _cryoTrayCoolingGuid.Value;
                }
            }

            // Check installed schemes by name
            var schemes = GetInstalledSchemes();
            var found = schemes.FirstOrDefault(s => s.Name.Equals(CryoTrayCoolingSchemeName, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                _cryoTrayCoolingGuid = found.Guid;
                return found.Guid;
            }

            // Not found -> import from embedded Resources\CryoTray.pow
            Guid newGuid = Guid.NewGuid();
            string tempPowPath = Path.Combine(Path.GetTempPath(), "CryoTray.pow");

            try
            {
                // Extract embedded resource
                byte[]? powBytes = null;
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("CryoTray.Resources.CryoTray.pow")
                                   ?? assembly.GetManifestResourceNames()
                                       .Where(n => n.EndsWith("CryoTray.pow", StringComparison.OrdinalIgnoreCase))
                                       .Select(n => assembly.GetManifestResourceStream(n))
                                       .FirstOrDefault())
                {
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        powBytes = ms.ToArray();
                    }
                }

                // Fallback to local file if not found in assembly stream
                if (powBytes == null)
                {
                    string localFile = Path.Combine(AppContext.BaseDirectory, "Resources", "CryoTray.pow");
                    if (File.Exists(localFile))
                    {
                        powBytes = File.ReadAllBytes(localFile);
                    }
                }

                if (powBytes != null)
                {
                    File.WriteAllBytes(tempPowPath, powBytes);

                    // Import scheme silently
                    var psiImport = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = $"/import \"{tempPowPath}\" {newGuid}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var procImport = Process.Start(psiImport);
                    procImport?.WaitForExit();

                    // Rename to CryoTray Cooling
                    var psiRename = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = $"-changename {newGuid} \"{CryoTrayCoolingSchemeName}\" \"Low-temperature CPU power plan\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var procRename = Process.Start(psiRename);
                    procRename?.WaitForExit();

                    _cryoTrayCoolingGuid = newGuid;
                    return newGuid;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to import CryoTray.pow: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPowPath)) File.Delete(tempPowPath);
                }
                catch
                {
                    // Ignore
                }
            }

            _cryoTrayCoolingGuid = newGuid;
            return newGuid;
        }

        public void RefreshState()
        {
            Guid activeGuid = GetActiveSchemeGuid();
            Guid coolingGuid = EnsureCryoTrayCoolingScheme();
            SetCoolingState(activeGuid == coolingGuid);
        }

        public void SyncCoolingStateFromSystem()
        {
            Guid coolingGuid = _cryoTrayCoolingGuid ?? EnsureCryoTrayCoolingScheme();
            SetCoolingState(GetActiveSchemeGuid() == coolingGuid);
        }

        public Guid GetNormalPowerPlanGuid()
        {
            Guid coolingGuid = EnsureCryoTrayCoolingScheme();
            var schemes = GetInstalledSchemes();
            string configured = SettingsService.Instance.Current.NormalPowerPlanGuid;

            if (Guid.TryParse(configured, out Guid configuredGuid) &&
                configuredGuid != coolingGuid &&
                schemes.Any(scheme => scheme.Guid == configuredGuid))
            {
                return configuredGuid;
            }

            return BalancedGuid;
        }

        private void SetCoolingState(bool isCooling)
        {
            if (IsCoolingModeActive == isCooling)
            {
                return;
            }

            IsCoolingModeActive = isCooling;
            CoolingModeChanged?.Invoke(IsCoolingModeActive);
        }

        public void SetCoolingMode(bool enable)
        {
            if (enable)
            {
                Guid coolingGuid = EnsureCryoTrayCoolingScheme();
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref coolingGuid);

                // Write current settings parameters
                var s = SettingsService.Instance.Current;
                ApplyCpuCoolingParameters(s.MaxCpuThrottlePercent, s.PerfBoostMode);
            }
            else
            {
                Guid target = GetNormalPowerPlanGuid();
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref target);
            }

            SetCoolingState(enable);
        }

        public void ToggleCoolingMode()
        {
            SetCoolingMode(!IsCoolingModeActive);
        }

        public void ApplyCpuCoolingParameters(int maxThrottlePercent, int perfBoostMode)
        {
            Guid coolingGuid = EnsureCryoTrayCoolingScheme();
            Guid subProcessor = NativeMethods.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid throttleMax = NativeMethods.GUID_PROCESSOR_THROTTLE_MAX;
            Guid perfBoost = NativeMethods.GUID_PROCESSOR_PERFBOOST_MODE;

            uint throttleVal = (uint)Math.Clamp(maxThrottlePercent, 50, 100);
            uint boostVal = (uint)Math.Clamp(perfBoostMode, 0, 1);

            // Write PROCTHROTTLEMAX AC & DC
            NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref coolingGuid, ref subProcessor, ref throttleMax, throttleVal);
            NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref coolingGuid, ref subProcessor, ref throttleMax, throttleVal);

            // Write PERFBOOSTMODE AC & DC
            NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref coolingGuid, ref subProcessor, ref perfBoost, boostVal);
            NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref coolingGuid, ref subProcessor, ref perfBoost, boostVal);

            // If Cooling Mode is currently active, immediately re-apply active scheme
            if (IsCoolingModeActive || GetActiveSchemeGuid() == coolingGuid)
            {
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref coolingGuid);
            }
        }

        public void ResetCryoTraySchemeToDefault()
        {
            // Restore exact baseline .pow parameters: PERFBOOSTMODE = Disabled (0), PROCTHROTTLEMAX = 99% (0x63)
            ApplyCpuCoolingParameters(99, 0);
        }

        /// <summary>Restores a non-CryoTray power plan and removes CryoTray Cooling schemes on uninstall.</summary>
        public void CleanupForUninstall()
        {
            var schemes = GetInstalledSchemes();
            var coolingSchemes = schemes
                .Where(s => s.Name.Equals(CryoTrayCoolingSchemeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var coolingIds = coolingSchemes.Select(s => s.Guid).ToHashSet();

            Guid fallback = BalancedGuid;
            if (Guid.TryParse(SettingsService.Instance.Current.NormalPowerPlanGuid, out Guid configured) &&
                !coolingIds.Contains(configured) &&
                schemes.Any(s => s.Guid == configured))
            {
                fallback = configured;
            }
            else if (schemes.All(s => s.Guid != fallback) || coolingIds.Contains(fallback))
            {
                var other = schemes.FirstOrDefault(s => !coolingIds.Contains(s.Guid));
                if (other != null)
                {
                    fallback = other.Guid;
                }
            }

            NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref fallback);

            foreach (var scheme in coolingSchemes)
            {
                Guid guid = scheme.Guid;
                if (NativeMethods.PowerDeleteScheme(IntPtr.Zero, ref guid) != 0)
                {
                    TryDeleteSchemeViaPowerCfg(guid);
                }
            }

            _cryoTrayCoolingGuid = null;
            SetCoolingState(false);
        }

        private static void TryDeleteSchemeViaPowerCfg(Guid schemeGuid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"/delete {schemeGuid}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch
            {
            }
        }

        private List<PowerSchemeInfo> ParsePowerCfgList()
        {
            var list = new List<PowerSchemeInfo>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/list",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return list;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    if (!line.Contains("Power Scheme GUID:")) continue;

                    bool isActive = line.EndsWith("*");
                    int guidStart = line.IndexOf("GUID:") + 5;
                    string rest = line.Substring(guidStart).Trim();
                    int parenStart = rest.IndexOf('(');
                    int parenEnd = rest.IndexOf(')');

                    if (parenStart > 0 && parenEnd > parenStart)
                    {
                        string guidStr = rest.Substring(0, parenStart).Trim();
                        string name = rest.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();

                        if (Guid.TryParse(guidStr, out Guid g))
                        {
                            list.Add(new PowerSchemeInfo
                            {
                                Guid = g,
                                Name = name,
                                IsActive = isActive
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return list;
        }
    }
}
