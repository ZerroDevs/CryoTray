using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum HealthCheckLevel
    {
        Ready,
        Warning,
        ActionRequired,
        Unavailable
    }

    public enum HealthCheckActionKind
    {
        None,
        InstallPawnIo,
        EnableStartupTask,
        RemoveStartupTask,
        RepairUpdateStaging,
        OpenSettings,
        OpenSystemInformation,
        OpenUpdateLog,
        Retry
    }

    public sealed class HealthCheckItem
    {
        public string Id { get; init; } = "";
        public string TitleKey { get; init; } = "";
        public string DetailKey { get; init; } = "";
        public object?[] DetailArgs { get; init; } = Array.Empty<object?>();
        public HealthCheckLevel Level { get; init; }
        public HealthCheckActionKind Action { get; init; }
        public string ActionKey { get; init; } = "";
    }

    public sealed class HealthCheckReport
    {
        public DateTime CheckedAtLocal { get; init; } = DateTime.Now;
        public bool PostUpdate { get; init; }
        public bool Elevated { get; init; }
        public IReadOnlyList<HealthCheckItem> Items { get; init; } = Array.Empty<HealthCheckItem>();

        public int ActionCount => Items.Count(item => item.Level == HealthCheckLevel.ActionRequired);
        public int WarningCount => Items.Count(item => item.Level == HealthCheckLevel.Warning);
        public int IssueCount => ActionCount + WarningCount;
        public int ReadyCount => Items.Count(item => item.Level == HealthCheckLevel.Ready);
    }

    public static class HealthCheckService
    {
        public static HealthCheckReport Run(bool postUpdate = false)
        {
            var settings = SettingsService.Instance.Current;
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            var items = new List<HealthCheckItem>();
            bool elevated = StartupTaskService.IsCurrentProcessElevated();
            bool pawnIo = HardwareMonitorService.IsPawnIoInstalled();

            bool hasCpuSensor = IsFinitePositive(snapshot.CpuTemp) ||
                IsFinitePositive(snapshot.MaxCoreTemp) ||
                snapshot.CpuCoresTelemetry.Any(core => IsFinitePositive(core.Temperature));
            if (!pawnIo)
            {
                items.Add(Item(
                    "sensors", "Health_SensorsTitle", "Health_SensorsBlocked",
                    HealthCheckLevel.Unavailable,
                    HealthCheckActionKind.InstallPawnIo, "Health_FixInstallPawnIo"));
            }
            else if (!hasCpuSensor)
            {
                items.Add(Item(
                    "sensors", "Health_SensorsTitle", "Health_SensorsBlank",
                    HealthCheckLevel.Warning,
                    HealthCheckActionKind.Retry, "Health_FixRetry"));
            }
            else
            {
                items.Add(Item(
                    "sensors", "Health_SensorsTitle", "Health_SensorsReady",
                    HealthCheckLevel.Ready));
            }

            bool hasGpu = snapshot.Gpus.Count > 0 || !string.IsNullOrWhiteSpace(snapshot.GpuName);
            if (!hasGpu)
            {
                items.Add(Item(
                    "gpu", "Health_GpuTitle", "Health_GpuMissing",
                    HealthCheckLevel.ActionRequired,
                    HealthCheckActionKind.OpenSystemInformation, "Health_FixOpenSystemInfo"));
            }
            else if (!IsFinitePositive(snapshot.GpuTemp))
            {
                items.Add(Item(
                    "gpu", "Health_GpuTitle", "Health_GpuNoTemp",
                    HealthCheckLevel.Warning,
                    HealthCheckActionKind.OpenSystemInformation, "Health_FixOpenSystemInfo",
                    snapshot.GpuName));
            }
            else
            {
                items.Add(Item(
                    "gpu", "Health_GpuTitle", "Health_GpuReady",
                    HealthCheckLevel.Ready,
                    args: new object?[] { snapshot.GpuName }));
            }

            if (pawnIo)
            {
                items.Add(Item(
                    "pawnio", "Health_PawnIoTitle", "Health_PawnIoReady",
                    HealthCheckLevel.Ready));
            }
            else
            {
                items.Add(Item(
                    "pawnio", "Health_PawnIoTitle", "Health_PawnIoMissing",
                    HealthCheckLevel.ActionRequired,
                    HealthCheckActionKind.InstallPawnIo, "Health_FixInstallPawnIo"));
            }

            items.Add(Item(
                "admin", "Health_AdminTitle",
                elevated ? "Health_AdminElevated" : "Health_AdminStandard",
                HealthCheckLevel.Ready));

            var startup = StartupTaskService.QueryState();
            bool startupWanted = settings.LaunchOnStartup;
            if (!startupWanted && !startup.Exists)
            {
                items.Add(Item(
                    "startup", "Health_StartupTitle", "Health_StartupOff",
                    HealthCheckLevel.Ready));
            }
            else if (!startupWanted)
            {
                items.Add(Item(
                    "startup", "Health_StartupTitle", "Health_StartupUnexpected",
                    HealthCheckLevel.Warning,
                    HealthCheckActionKind.RemoveStartupTask, "Health_FixRemoveStartup"));
            }
            else if (!startup.Exists)
            {
                items.Add(Item(
                    "startup", "Health_StartupTitle", "Health_StartupMissing",
                    HealthCheckLevel.ActionRequired,
                    HealthCheckActionKind.EnableStartupTask, "Health_FixEnableStartup"));
            }
            else
            {
                string expectedExe = StartupTaskService.ResolveExecutablePath();
                bool pathMatches = false;
                try
                {
                    pathMatches = !string.IsNullOrWhiteSpace(startup.CommandPath) &&
                        string.Equals(
                            Path.GetFullPath(startup.CommandPath),
                            expectedExe,
                            StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                }
                bool hasAutostart = !string.IsNullOrWhiteSpace(startup.Arguments) &&
                    startup.Arguments.Contains(StartupTaskService.AutostartArg, StringComparison.OrdinalIgnoreCase);
                if (!pathMatches || !hasAutostart)
                {
                    items.Add(Item(
                        "startup", "Health_StartupTitle", "Health_StartupStale",
                        HealthCheckLevel.Warning,
                        HealthCheckActionKind.EnableStartupTask, "Health_FixEnableStartup"));
                }
                else
                {
                    items.Add(Item(
                        "startup", "Health_StartupTitle", "Health_StartupReady",
                        HealthCheckLevel.Ready));
                }
            }

            try
            {
                Guid activeGuid = PowerPlanService.Instance.GetActiveSchemeGuid();
                List<PowerSchemeInfo> schemes = PowerPlanService.Instance.GetInstalledSchemes();
                PowerSchemeInfo? active = schemes.FirstOrDefault(
                    scheme => scheme.Guid == activeGuid);
                if (activeGuid == Guid.Empty || active == null)
                {
                    items.Add(Item(
                        "power", "Health_PowerPlanTitle", "Health_PowerPlanMissing",
                        HealthCheckLevel.ActionRequired,
                        HealthCheckActionKind.OpenSettings, "Health_FixOpenSettings"));
                }
                else
                {
                    items.Add(Item(
                        "power", "Health_PowerPlanTitle",
                        PowerPlanService.Instance.ActiveProfile == null
                            ? "Health_PowerPlanExternal"
                            : "Health_PowerPlanReady",
                        HealthCheckLevel.Ready,
                        args: new object?[] { active.Name }));
                }
            }
            catch
            {
                items.Add(Item(
                    "power", "Health_PowerPlanTitle", "Health_PowerPlanMissing",
                    HealthCheckLevel.ActionRequired,
                    HealthCheckActionKind.OpenSettings, "Health_FixOpenSettings"));
            }

            try
            {
                bool downloadsReady = Directory.Exists(UpdateStorage.DownloadsRoot);
                bool protectedRoot = UpdateStorage.IsDirectoryProtected(UpdateStorage.UpdateRoot);
                if (!downloadsReady)
                {
                    items.Add(Item(
                        "updates", "Health_UpdateTitle", "Health_UpdateMissing",
                        HealthCheckLevel.ActionRequired,
                        HealthCheckActionKind.RepairUpdateStaging, "Health_FixRepairUpdates"));
                }
                else if (!protectedRoot)
                {
                    items.Add(Item(
                        "updates", "Health_UpdateTitle", "Health_UpdateUnprotected",
                        HealthCheckLevel.Warning,
                        HealthCheckActionKind.RepairUpdateStaging, "Health_FixRepairUpdates"));
                }
                else
                {
                    items.Add(Item(
                        "updates", "Health_UpdateTitle", "Health_UpdateReady",
                        HealthCheckLevel.Ready));
                }
            }
            catch
            {
                items.Add(Item(
                    "updates", "Health_UpdateTitle", "Health_UpdateUnavailable",
                    HealthCheckLevel.ActionRequired,
                    HealthCheckActionKind.RepairUpdateStaging, "Health_FixRepairUpdates"));
            }

            return new HealthCheckReport
            {
                CheckedAtLocal = DateTime.Now,
                PostUpdate = postUpdate,
                Elevated = elevated,
                Items = items
            };
        }

        private static HealthCheckItem Item(
            string id,
            string titleKey,
            string detailKey,
            HealthCheckLevel level,
            HealthCheckActionKind action = HealthCheckActionKind.None,
            string actionKey = "",
            params object?[] args)
        {
            return new HealthCheckItem
            {
                Id = id,
                TitleKey = titleKey,
                DetailKey = detailKey,
                DetailArgs = args,
                Level = level,
                Action = action,
                ActionKey = actionKey
            };
        }

        private static bool IsFinitePositive(double value) =>
            double.IsFinite(value) && value > 0;
    }
}
