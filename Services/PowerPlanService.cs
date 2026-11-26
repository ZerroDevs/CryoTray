using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

        public const string CryoTrayCoolingSchemeName = "CryoTray Cooling - Legacy";
        public const string ObsoleteCryoTraySchemeName = "CryoTray Cooling";
        public static readonly Guid BalancedGuid = NativeMethods.GUID_POWERSCHEME_BALANCED;

        private const int PowerCfgTimeoutMs = 15000;
        private static readonly string PowerCfgExePath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        private static readonly Regex PowerCfgSchemeLineRegex = new(
            @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)",
            RegexOptions.Compiled);

        private static readonly Guid[] BuiltInSchemeGuids =
        {
            NativeMethods.GUID_POWERSCHEME_BALANCED,
            NativeMethods.GUID_POWERSCHEME_HIGH_PERF,
            NativeMethods.GUID_POWERSCHEME_POWER_SAVER
        };

        private static readonly HashSet<Guid> CryoTraySchemeGuids =
            new(PowerProfiles.SchemeGuids);

        private readonly object _switchLock = new();
        private bool _obsoleteMigrationAttempted;

        public event Action<bool>? CoolingModeChanged;
        public event Action? ProfileChanged;

        /// <summary>Raised when the active plan changes away from a CryoTray profile by something else.</summary>
        public event Action<PowerPlanDrift>? PlanDrifted;

        public sealed class PowerPlanDrift
        {
            public string PreviousName { get; init; } = "";
            public string NewName { get; init; } = "";
            public Guid NewSchemeGuid { get; init; }
        }

        private bool _suppressDrift;

        public PowerProfileDefinition? ActiveProfile { get; private set; }

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

        /// <summary>Returns the installed legacy cooling scheme, creating it from Balanced when missing.</summary>
        public Guid EnsureCryoTrayCoolingScheme()
        {
            return EnsureProfileScheme(PowerProfiles.LegacyProfile);
        }

        /// <summary>
        /// Returns the profile's deterministic GUID, duplicating Balanced into that exact GUID when absent.
        /// No other scheme is touched here.
        /// </summary>
        public Guid EnsureProfileScheme(PowerProfileDefinition profile)
        {
            if (profile == null)
            {
                return Guid.Empty;
            }

            if (IsSchemeInstalled(profile.SchemeGuid))
            {
                RenameScheme(profile.SchemeGuid, profile.SchemeName);
                return profile.SchemeGuid;
            }

            bool duplicated = TryRunPowerCfg(
                $"/duplicatescheme {BalancedGuid:D} {profile.SchemeGuid:D}");
            if (!duplicated && !IsSchemeInstalled(profile.SchemeGuid))
            {
                return Guid.Empty;
            }

            RenameScheme(profile.SchemeGuid, profile.SchemeName);

            return IsSchemeInstalled(profile.SchemeGuid) ? profile.SchemeGuid : Guid.Empty;
        }

        public void RefreshState()
        {
            Guid activeGuid = GetActiveSchemeGuid();
            var activeScheme = GetInstalledSchemes()
                .FirstOrDefault(scheme => scheme.Guid == activeGuid);

            // One-time upgrade path: the old single "CryoTray Cooling" plan becomes Legacy.
            if (activeScheme != null &&
                activeScheme.Name.Equals(ObsoleteCryoTraySchemeName, StringComparison.OrdinalIgnoreCase) &&
                !_obsoleteMigrationAttempted)
            {
                _obsoleteMigrationAttempted = true;
                if (SwitchToProfile(PowerProfiles.LegacyProfile))
                {
                    return;
                }
            }

            if (!_suppressDrift && IsExternalDrift(ActiveProfile, activeGuid))
            {
                PlanDrifted?.Invoke(new PowerPlanDrift
                {
                    PreviousName = ActiveProfile!.SchemeName,
                    NewName = activeScheme?.Name ?? activeGuid.ToString(),
                    NewSchemeGuid = activeGuid
                });
            }

            SetActiveProfile(PowerProfiles.FromSchemeGuid(activeGuid));
        }

        internal static bool IsExternalDrift(PowerProfileDefinition? previous, Guid activeGuid)
        {
            return previous != null &&
                CryoTraySchemeGuids.Contains(previous.SchemeGuid) &&
                activeGuid != previous.SchemeGuid;
        }

        public void SyncCoolingStateFromSystem()
        {
            RefreshState();
        }

        public PowerProfileDefinition? GetActiveProfile()
        {
            return PowerProfiles.FromSchemeGuid(GetActiveSchemeGuid());
        }

        public Guid GetNormalPowerPlanGuid()
        {
            var schemes = GetInstalledSchemes();
            string original = SettingsService.Instance.Current.OriginalActivePowerPlanGuid;

            if (Guid.TryParse(original, out Guid originalGuid))
            {
                var originalScheme = schemes.FirstOrDefault(scheme => scheme.Guid == originalGuid);
                if (originalScheme != null && !IsOwnedCryoTrayScheme(originalScheme))
                {
                    return originalGuid;
                }
            }

            string configured = SettingsService.Instance.Current.NormalPowerPlanGuid;
            if (Guid.TryParse(configured, out Guid configuredGuid))
            {
                var configuredScheme = schemes.FirstOrDefault(scheme => scheme.Guid == configuredGuid);
                if (configuredScheme != null && !IsOwnedCryoTrayScheme(configuredScheme))
                {
                    return configuredGuid;
                }
            }

            return BalancedGuid;
        }

        private void SetActiveProfile(PowerProfileDefinition? profile)
        {
            bool cooling = profile?.IsCooling == true;
            bool profileChanged = !ReferenceEquals(ActiveProfile, profile);
            bool coolingChanged = IsCoolingModeActive != cooling;

            ActiveProfile = profile;
            IsCoolingModeActive = cooling;

            if (coolingChanged)
            {
                CoolingModeChanged?.Invoke(cooling);
            }

            if (profileChanged || coolingChanged)
            {
                ProfileChanged?.Invoke();
            }
        }

        public void SetCoolingMode(bool enable)
        {
            var settings = SettingsService.Instance.Current;
            PowerProfileDefinition target = enable
                ? PowerProfiles.GetCooling(settings.PreferredCoolingProfile)
                : PowerProfiles.GetGaming(settings.PreferredGamingProfile);

            SwitchToProfile(target);
        }

        public void ToggleCoolingMode()
        {
            SetCoolingMode(!IsCoolingModeActive);
        }

        /// <summary>
        /// Creates or updates a CryoTray profile, activates it, then removes every other CryoTray
        /// scheme from the hardcoded whitelist. Windows Balanced is activated directly and removes
        /// every CryoTray scheme. Built-in and third-party plans are never deleted.
        /// </summary>
        public bool SwitchToProfile(PowerProfileDefinition? profile)
        {
            if (profile == null)
            {
                return false;
            }

            lock (_switchLock)
            {
                CaptureOriginalPlan();

                if (profile.IsWindowsDefault)
                {
                    Guid balanced = profile.SchemeGuid;
                    uint balancedStatus = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref balanced);
                    if (balancedStatus != 0 && GetActiveSchemeGuid() != balanced)
                    {
                        RefreshState();
                        return false;
                    }

                    DeleteOtherCryoTraySchemes(balanced);
                    SetActiveProfile(profile);
                    return true;
                }

                Guid target = EnsureProfileScheme(profile);
                if (target == Guid.Empty)
                {
                    RefreshState();
                    return false;
                }

                WriteProfileValues(profile, target);

                uint status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref target);
                if (status != 0 && GetActiveSchemeGuid() != target)
                {
                    RefreshState();
                    return false;
                }

                DeleteOtherCryoTraySchemes(target);
                SetActiveProfile(profile);
                return true;
            }
        }

        /// <summary>Reapplies the preferred profile when the active plan is a known profile.</summary>
        public void RefreshActiveProfile()
        {
            PowerProfileDefinition? active = GetActiveProfile();
            if (active == null)
            {
                RefreshState();
                return;
            }

            var settings = SettingsService.Instance.Current;
            PowerProfileDefinition target = active.IsCooling
                ? PowerProfiles.GetCooling(settings.PreferredCoolingProfile)
                : PowerProfiles.GetGaming(settings.PreferredGamingProfile);

            if (!ReferenceEquals(target, active))
            {
                SwitchToProfile(target);
                return;
            }

            if (active.IsWindowsDefault)
            {
                SetActiveProfile(active);
                return;
            }

            WriteProfileValues(active, active.SchemeGuid);
            if (GetActiveSchemeGuid() == active.SchemeGuid)
            {
                Guid guid = active.SchemeGuid;
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref guid);
            }

            SetActiveProfile(active);
        }

        /// <summary>Restores the user's captured non-CryoTray plan, used when the app exits.</summary>
        public void RestoreOriginalPowerPlan()
        {
            _suppressDrift = true;
            try
            {
                Guid target = GetNormalPowerPlanGuid();
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref target);
                RefreshState();
            }
            finally
            {
                _suppressDrift = false;
            }
        }

        public void ApplyCpuCoolingParameters(int maxThrottlePercent, int perfBoostMode)
        {
            Guid legacyGuid = PowerProfiles.LegacyProfile.SchemeGuid;
            if (!IsSchemeInstalled(legacyGuid))
            {
                // Never create the legacy plan just to store slider values; they are applied
                // when the user actually activates that profile.
                return;
            }

            WriteCpuCoolingValues(legacyGuid, maxThrottlePercent, perfBoostMode);

            if (GetActiveSchemeGuid() == legacyGuid)
            {
                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref legacyGuid);
            }
        }

        public void ResetCryoTraySchemeToDefault()
        {
            // Restore exact legacy baseline: PERFBOOSTMODE = Disabled (0), PROCTHROTTLEMAX = 99%
            ApplyCpuCoolingParameters(99, 0);
        }

        /// <summary>Restores a non-CryoTray power plan and removes every CryoTray-owned scheme on uninstall.</summary>
        public void CleanupForUninstall()
        {
            _suppressDrift = true;
            try
            {
                var schemes = GetInstalledSchemes();
                Guid fallback = GetNormalPowerPlanGuid();

                NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref fallback);

                foreach (var scheme in schemes)
                {
                    if (IsOwnedCryoTrayScheme(scheme))
                    {
                        DeleteScheme(scheme.Guid);
                    }
                }

                SetActiveProfile(null);
            }
            finally
            {
                _suppressDrift = false;
            }
        }

        private void CaptureOriginalPlan()
        {
            var settings = SettingsService.Instance.Current;
            if (!string.IsNullOrWhiteSpace(settings.OriginalActivePowerPlanGuid))
            {
                return;
            }

            Guid active = GetActiveSchemeGuid();
            if (active == Guid.Empty)
            {
                return;
            }

            var activeScheme = GetInstalledSchemes()
                .FirstOrDefault(scheme => scheme.Guid == active);
            if (CryoTraySchemeGuids.Contains(active) ||
                (activeScheme != null && IsOwnedCryoTrayScheme(activeScheme)))
            {
                return;
            }

            settings.OriginalActivePowerPlanGuid = active.ToString();
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void WriteProfileValues(PowerProfileDefinition profile, Guid schemeGuid)
        {
            foreach (var value in profile.Values)
            {
                WriteValue(schemeGuid, value.SubGroup, value.Setting, value.Ac, value.Dc);
            }

            if (profile.Key == PowerProfiles.LegacyKey)
            {
                var settings = SettingsService.Instance.Current;
                WriteCpuCoolingValues(
                    schemeGuid,
                    settings.MaxCpuThrottlePercent,
                    settings.PerfBoostMode);
            }
        }

        private static void WriteCpuCoolingValues(Guid schemeGuid, int maxThrottlePercent, int perfBoostMode)
        {
            uint throttleVal = (uint)Math.Clamp(maxThrottlePercent, 50, 100);
            uint boostVal = (uint)Math.Clamp(perfBoostMode, 0, 1);

            WriteValue(
                schemeGuid,
                PowerGuids.SubProcessor,
                PowerGuids.ThrottleMax,
                throttleVal,
                throttleVal);
            WriteValue(
                schemeGuid,
                PowerGuids.SubProcessor,
                PowerGuids.PerfBoostMode,
                boostVal,
                boostVal);
        }

        private static void WriteValue(Guid schemeGuid, Guid subGroup, Guid setting, uint ac, uint dc)
        {
            NativeMethods.PowerWriteACValueIndex(
                IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, ac);
            NativeMethods.PowerWriteDCValueIndex(
                IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, dc);
        }

        private void DeleteOtherCryoTraySchemes(Guid keepGuid)
        {
            foreach (var scheme in GetInstalledSchemes())
            {
                if (scheme.Guid == keepGuid || BuiltInSchemeGuids.Contains(scheme.Guid))
                {
                    continue;
                }

                if (IsOwnedCryoTrayScheme(scheme))
                {
                    DeleteScheme(scheme.Guid);
                }
            }
        }

        private static bool IsOwnedCryoTrayScheme(PowerSchemeInfo scheme)
        {
            if (CryoTraySchemeGuids.Contains(scheme.Guid))
            {
                return true;
            }

            if (BuiltInSchemeGuids.Contains(scheme.Guid))
            {
                return false;
            }

            if (scheme.Name.Equals(ObsoleteCryoTraySchemeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string storedOldGuid = SettingsService.Instance.Current.CoolingPowerPlanGuid;
            return Guid.TryParse(storedOldGuid, out Guid stored) &&
                   stored == scheme.Guid &&
                   scheme.Name.StartsWith("CryoTray", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSchemeInstalled(Guid schemeGuid)
        {
            return GetInstalledSchemes().Any(scheme => scheme.Guid == schemeGuid);
        }

        private static void RenameScheme(Guid schemeGuid, string name)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(name + "\0");
            uint result = NativeMethods.PowerWriteFriendlyName(
                IntPtr.Zero,
                ref schemeGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                bytes,
                (uint)bytes.Length);

            if (result != 0)
            {
                TryRunPowerCfg($"/changename {schemeGuid:D} \"{name}\" \"CryoTray power profile\"");
            }
        }

        private static void DeleteScheme(Guid schemeGuid)
        {
            if (NativeMethods.PowerDeleteScheme(IntPtr.Zero, ref schemeGuid) != 0)
            {
                TryRunPowerCfg($"/delete {schemeGuid:D}");
            }
        }

        private static bool TryRunPowerCfg(string arguments, int timeoutMs = PowerCfgTimeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PowerCfgExePath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return false;
                }

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }

                _ = stdoutTask.GetAwaiter().GetResult();
                _ = stderrTask.GetAwaiter().GetResult();
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private List<PowerSchemeInfo> ParsePowerCfgList()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PowerCfgExePath,
                    Arguments = "/list",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return new List<PowerSchemeInfo>();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                if (!proc.WaitForExit(PowerCfgTimeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return new List<PowerSchemeInfo>();
                }

                string output = stdoutTask.GetAwaiter().GetResult();
                return ParsePowerCfgOutput(output);
            }
            catch
            {
                // Ignore
            }

            return new List<PowerSchemeInfo>();
        }

        internal static List<PowerSchemeInfo> ParsePowerCfgOutput(string? output)
        {
            var list = new List<PowerSchemeInfo>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return list;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                var match = PowerCfgSchemeLineRegex.Match(line);
                if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out Guid guid))
                {
                    continue;
                }

                list.Add(new PowerSchemeInfo
                {
                    Guid = guid,
                    Name = match.Groups[2].Value.Trim(),
                    IsActive = line.EndsWith("*", StringComparison.Ordinal)
                });
            }

            return list;
        }
    }
}
