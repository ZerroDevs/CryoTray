using System;

namespace CryoTray.Models
{
    public enum StartupSourceKind
    {
        RegistryCurrentUserRun,
        RegistryLocalMachineRun,
        RegistryLocalMachineRun32,
        StartupFolderUser,
        StartupFolderCommon,
        ScheduledTask
    }

    public enum StartupEntryMode
    {
        Enabled,
        Disabled,
        Delayed
    }

    public enum StartupDelayItemState
    {
        None,
        Pending,
        WaitingForIdle,
        Launching,
        Launched,
        AlreadyRunning,
        SkippedMissing,
        Cancelled,
        Failed
    }

    public sealed class StartupDelaySetting
    {
        public string EntryId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public StartupSourceKind SourceKind { get; set; } = StartupSourceKind.RegistryCurrentUserRun;
        public string SourceName { get; set; } = "";
        public string TaskPath { get; set; } = "";
        public bool RequiresElevation { get; set; }
        public int DelaySeconds { get; set; } = 45;
        public int PriorityOrder { get; set; } = 1;
        public bool WaitForIdle { get; set; } = true;
        public bool AutoRestoreOnUninstall { get; set; } = true;
    }

    public sealed class StartupItemRecord
    {
        public string EntryId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public StartupSourceKind SourceKind { get; set; }
        public string SourceName { get; set; } = "";
        public string TaskPath { get; set; } = "";
        public bool RequiresElevation { get; set; }
        public StartupEntryMode Mode { get; set; }
        public StartupEntryMode NativeWindowsMode { get; set; }
        public bool IsReadOnly { get; set; }
        public string ReadOnlyReasonKey { get; set; } = "";
        public int DelaySeconds { get; set; } = 45;
        public int PriorityOrder { get; set; } = 1;
        public bool WaitForIdle { get; set; } = true;
        public StartupDelayItemState RuntimeState { get; set; } = StartupDelayItemState.None;
        public string LastError { get; set; } = "";
    }

    public sealed record CryoBootQueueStatus(
        bool IsActive,
        int TotalCount,
        int CompletedCount,
        int RemainingCount,
        string NextEntryName,
        int SecondsUntilNext,
        bool WaitingForIdle);
}
