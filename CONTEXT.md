# CONTEXT

## Architecture

- `App.xaml.cs`: software-rendered, single-instance WPF shell with named `EventWaitHandle` (`CryoTray-Show-Dashboard-Event-99210`) second-launch window activation, `SystemEvents.PowerModeChanged` battery auto-cooling rule, `/uninstall-cleanup` CLI mode, tray manager, message-only hotkey `HwndSource`, on-demand `MainWindow`, App-owned overlay, and working-set trimming; `/dump` bypasses the mutex and writes `cpu-sensors.txt`.
- `Views/MainWindow`: 444 x 673 baseline dashboard (dynamically adjusts height for `PawnIoBanner`, `RamRow`, `GpuHealthRow`, `OverlayRow`, and `AutopilotRow`), hero CPU card, six-petal animated vector cooling fan, CPU and GPU charts with hover cards and unavailable states, dedicated timeframe strip, cooling toggle, RAM/CPU/GPU telemetry rows, filtered SSD rows, per-core hover popup, and `Tools` menu CSV export/live 1 Hz logging. Created per open and destroyed on close or deactivation (guarded by `RunWithChildDialogGuard` during dialogs).
- `Views/OverlayWindow`: App-owned translucent HUD created when overlay is enabled, supporting Win32 `WS_EX_TRANSPARENT` click-through pass-through and optional `RamOverlayGroup` (`Width` 430 when CPU+GPU+RAM visible, else 360).
- `Views/SettingsWindow`, `StressTestWindow`, `SystemInfoWindow`: companion windows created per open. `SettingsWindow` includes General (battery cooling rule, interactive hotkey recorder `TxtHotkeyShortcut`), View (frame colors, Black/White tray badge digits, RAM row, overlay click-through/RAM), CPU (clock/base speed, Large/Small graph), Memory (RAM cleaner & auto-optimization rules), Disk (master toggle, per-drive hide & inline graphs), GPU, Autopilot (fullscreen 3D pause checkbox), and About. `StressTestWindow` runs stress workers on `ThreadPriority.BelowNormal` background threads so 1 Hz polling stays responsive and supports session CSV export.
- `Views/ConfirmExitWindow`: app-styled Yes/No confirmation shown when exiting would disable Cooling Mode.
- `Services/HardwareMonitorService`: 1 s background poll of LibreHardwareMonitor with runtime PawnIO detection (`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`), automatic reopen if PawnIO is installed later, per-GPU telemetry isolation, primary GPU selection (`AppSettings.PreferredGpuIdentifier`), LHM storage metadata caching, drive exclusion key helpers (`GetDriveExclusionKey`, `IsDriveExcluded`), and zero-WMI RAM telemetry (`GlobalMemoryStatusEx`). Produces `HardwareSnapshot` and appends `TelemetrySample.FromSnapshot` to a 3,600-sample ring buffer.
- `Services/TelemetryCsvService`: invariant-culture CSV snapshot writer and continuous 1 Hz file logger fed by `TelemetryUpdated`.
- `Services/PowerPlanService`: imports and switches the CryoTray Cooling power scheme, and deletes it cleanly via `PowerDeleteScheme` + `powercfg /delete` fallback on uninstall (`/uninstall-cleanup`).
- `Services/AutopilotService`: process-whitelist polling every 2.5 s using Win32 `CreateToolhelp32Snapshot` plus optional fullscreen game/presentation detection via `SHQueryUserNotificationState` and borderless monitor-rect check.
- `Services/SystemInfoQueryService`: lazy WMI, EDID, boot, and BSOD queries. Matches LHM storage telemetry to `Win32_DiskDrive` via serial, physical disk index, and model, and queries Cooling-Mode-only CPU temp stats.
- `Services/NativeMethods`: Win32 P/Invokes (`EmptyWorkingSet`, `CreateToolhelp32Snapshot`, `Process32FirstW`, `Process32NextW`, `CloseHandle`, `SHQueryUserNotificationState`, `GlobalMemoryStatusEx`, `PowerDeleteScheme`, `GetForegroundWindow`, `SetForegroundWindow`, `GetWindowRect`, `GetWindowThreadProcessId`).
- `Tray/TrayIconManager`: GPU, CPU, or merged split tray badges (`--` when unavailable, skipping redundant HICON redraws via signature checks) and dark context menu including checkable `Overlay Click-Through`.

## Dependencies

- `LibreHardwareMonitorLib` 0.9.6 for hardware sensors.
- `System.Management` for lazy WMI in `SystemInfoQueryService`.
- `UseWindowsForms` is enabled for tray icons and monitor bounds lookup.
- Inno Setup 6 (`ISCC.exe`) compiles `Installer/CryoTray.iss` into `Installer/Output/CryoTray-Setup-<version>.exe`.

## Styling tokens

- Window base `#141414`, chart base `#121212`, borders `#262626`, separators `#202020`.
- Disabled/secondary button base `#242424`, hover `#2E2E2E`, disabled opacity `0.42`.
- Text primary `#FFFFFF`, secondary `#CCCCCC`, muted `#888888`.
- Cooling OFF accent `#E07D62`, cooling ON accent `#2196F3`, fan cyan `#76C6EF`, fan ring `#2B88D8`, gold hub `#F3C765`.
- Popup white `#FFFFFF`, grid border `#E2E2E2`, popup text `#1A1A1A`.
- Body font `Segoe UI Variable Text`; display and tray font `Segoe UI Variable Display`.
- Tray palette: GPU `#ABD068`, CPU off `#FAAA9A`, CPU on `#76C6ED`.

## Domain rules

- Unavailable numeric telemetry (`CpuTemp`, `PeakCpuTemp`, `MaxCoreTemp`, `CpuGhz`, `CpuBaseGhz`, `CpuMaxGhz`, `CpuLoad`, `GpuTemp`, `GpuLoad`, `RamUsedGb`, `RamTotalGb`, `RamUsagePercent`, `DriveTelemetry.Temperature`) uses `double.NaN` (and `null` for `int?` / `double?`), never synthetic values; UI renders `--` or an unavailable message.
- `snapshot.CpuTemp` is the integer display max across mapped cores when finite sensors exist (`double.NaN` when unavailable). Hero, tray badge, overlay, chart, and stress test all check `double.IsFinite(v) && v > 0`.
- `snapshot.PeakCpuTemp` is the raw sensor peak before per-core mapping (`double.NaN` when unavailable).
- Ignore CPU temperature sensors containing `Distance to TjMax` or `SoC`.
- Peak prefers the hottest CCD Tdie when present, then Tctl/Tdie, then the hottest CPU reading, then motherboard CPU reading.
- Direct per-core sensor mapping is used only when no AMD `Tctl/Tdie` sensor exists.
- Floor is `peak - 3.5` so the bubble keeps a compact few-degree span.
- Per-core sensor names are matched by `^(?:CPU\s*)?(P-Core|E-Core|Core)\s*#\s*(\d+)$`.
- Hottest cores are every core whose `DisplayTemp` equals the maximum.
- Popup columns are 4 up to 16 cores, 6 above 16.
- Primary GPU Auto mode prefers discrete GPUs (NVIDIA, non-APU AMD, Intel Arc) with valid temperature over integrated graphics; users can override via `AppSettings.PreferredGpuIdentifier` in `Settings > GPU`.
- Storage telemetry pairs with WMI drives by normalized serial number, then `PhysicalDriveNumber`, then model name + closest capacity, never by positional index.
- Excluded storage drives (`AppSettings.ExcludedDrives` keyed via `HardwareMonitorService.GetDriveExclusionKey`) are omitted from `MainWindow` SSD rows and `TemperatureAlertService` disk notifications.
- `SystemInfoQueryService` CPU temp stats (`System_TempStats`) report `CoolingModeActive == true` samples only (`TelemetryRingBuffer.GetCpuStats(coolingOnly: true)`).
- Hotkey recorder accepts standalone `F1-F12` or modifier (`Ctrl`/`Alt`/`Shift`/`Win`) + `A-Z`, `0-9`, or `F1-F12`.
- Stress test load workers use dedicated `ThreadPriority.BelowNormal` threads rather than `Task.Run` so ThreadPool timers and `HardwareMonitorService` 1 Hz polling stay unstarved under 100% CPU load.
- **UI Verification in Agent Shell (`exebox`)**: Under normal (unelevated) launch, the agent shell runs at `Medium Mandatory Level` (`IsAdmin=False`) inside `exebox-...`, where `Start-Process -Verb RunAs` fails with `The request is not supported`, `Graphics.CopyFromScreen` fails (`Invalid handle`), and `SetCursorPos` stays `(0,0)`. Running Antigravity as Administrator elevates the shell to `High Mandatory Level` (`IsAdmin=True`), allowing `scratch/ui_verify.ps1` to execute elevated `/test` and launch `CryoTray.exe` via `CreateProcess` (`UseShellExecute = $false`) for live PawnIO CPU + NVMe storage sensor captures via Windows `UIAutomation` (`ComboBoxItem` queried from its `ComboBox` parent peer, not `PopupRoot`) and Win32 `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT = 2)`.

## Wave 3 Architecture & Domain Rules

- **Task Scheduler 1.2 XML (`Services/StartupTaskService.cs`)**: `schtasks /create /xml` requires UTF-16 LE BOM (`Encoding.Unicode`) XML conforming to Task 1.2 schema (`InteractiveToken`, `HighestAvailable`, `PT5S`, `PT0S`, battery logon allowed). `/autostart` suppresses second-launch dashboard focus signaling; `ResolveExecutablePath()` avoids capturing `dotnet.exe`; `EnsureSynchronized(bool)` repairs stale or moved `.exe` paths at app startup.
- **Timed Cooling Pause Coordination (`App.xaml.cs`, `AutopilotService.cs`)**: Timed pauses track `CoolingPausedUntilUtc`, expose `IsCoolingPausedByTimer` via `AutopilotService.ExternalPauseHoldCheck`, defer battery auto-cooling while active, cancel on manual toggle, and hand off to `AutopilotService.TransferTimedPauseToAutopilot()` when expiry coincides with an active pause trigger.
- **RTL Bidi Protection & Stress Delta Aggregation**: Mixed technical labels (`SSD (C:)`) in `MainWindow` require `FlowDirection.LeftToRight` on `rowTitleText` inside `RightToLeft` Arabic UI to avoid reversed parentheses. `StressTestWindow.ComputeCoolingComparison` aggregates trailing 5 samples per Cooling ON/OFF phase, handling nullable `int?` `TelemetrySample.CpuPowerWatts`. Verified with `24 / 24` elevated `/test` checks and `CryoTray-Setup-0.0.100.exe` (`52,446,080` bytes).

## Wave 4 Architecture & Domain Rules

- **x64 NT Memory List Struct (`Services/NativeMethods.cs`, `Services/MemoryTelemetryService.cs`)**: `NtQuerySystemInformation(SystemMemoryListInformation = 80)` on Windows 10/11 x64 requires a `176`-byte `SYSTEM_MEMORY_LIST_INFORMATION` struct (`22` sequential `nuint` fields ending with `ModifiedPageCountPageFile`); omitting the trailing field triggers `STATUS_INFO_LENGTH_MISMATCH`.
- **Non-Blocking STA Dispatcher Optimization (`Services/MemoryOptimizerService.cs`)**: `/test` invokes `OptimizeAsync` from the STA startup thread before the Dispatcher pumps; completion notifications and `Clipboard.Clear()` must use fire-and-forget `Dispatcher.BeginInvoke` (never `await InvokeAsync`) to prevent STA deadlocks.
- **`GetSystemTimes` Idle Accounting**: Win32 `lpKernelTime` includes `lpIdleTime`, so `ComputeCpuUsagePercent` computes `busy = (kernelDelta + userDelta) - idleDelta`.
- **WPF ToolTip & XAML Spacing Details**: Inline `<ToolTip>` footer `Run` formatting in `MainWindow` uses `xml:space="preserve"` on `Ram_LastFreedPrefix` / `Ram_LastFreedAt` plus explicit spacing around the freed span so XAML whitespace normalization never collapses `Last freed: <X> at <Time>`. Named elements meant as code-behind fields on `Button` controls (`TxtRamRatio`, `OptGlyphHost`) live in `Button.Content`, not inside `ControlTemplate`. `RamBreakdownToolTip` is attached strictly to `BtnRamBreakdown` (`RamTelemetryZone.ToolTip == null` so row empty space does not trigger it) and must keep `ToolTip.StaysOpen == true`; WPF `PopupControlService.ShowToolTip` throws `NotSupportedException` on hover if `StaysOpen` is set to `false`.
- **Owner-Drawn WinForms Tray Menu Rule (`Tray/TrayIconManager.cs`, `Views/OverlayWindow.xaml.cs`)**: On `.NET 8`, `ToolStripProfessionalRenderer` + `ShowCheckMargin = true` paints WinForms' default `#F8F8F8` `ImageMargin` gutter and VisualStyle `#B5D7F2` hover highlight over white text. `TrayIconManager.DarkMenuRenderer` therefore inherits `ToolStripRenderer` directly, disables both `ShowImageMargin` and `ShowCheckMargin` across both `_contextMenu` and `_itemPauseCooling.DropDown`, sizes items dynamically (`MenuTextHorizontalReserve = 64`), and owner-draws `#1E1E1E` background, `#2D2D2D` 4 px hover pills, aligned trailing `#29B6F6` checkboxes / vector chevrons, `#787878` disabled text, and Arabic RTL mirroring. `OverlayWindow.OnLanguageChanged` marshals across `Dispatcher` when needed so runtime `LocalizationService.Apply` changes remain thread-safe after secondary STA tests shut down their dispatchers.
- **`R-37` Owner Decision & Verification**: Owner explicitly chose `#3A3A3A` 1 px border on `BtnOptimizeRam` / `BtnOptimizeRamMenu` to match adjacent dashboard pills. Verified `0 Warning(s), 0 Error(s)`, `31 / 31 PASSED` elevated `/test` (including `FindToolTipOwner` pill-scoping, `TestRamTooltipHoverLifecycle`, and `TestTrayContextMenuDarkRendering`), `EM_DASH_HITS=0`, `MAIN_WINDOW_BOUNDS: Width=444 Height=582`, `verify-09-tray-context-menu.png` (`924x276`), and `CryoTray-Setup-0.0.100.exe` (`52,458,375` bytes).

## Wave 5 Architecture & Domain Rules

- **Custom Cooling Frame Colors (`App.xaml.cs`, `Views/SettingsWindow.xaml.cs`)**: `WindowBorderBrush` resolves from `CoolingOnFrameColor` (`#2B88D8` default) or `CoolingOffFrameColor` (`#C26A47` default) via `App.ResolveFrameColor` and updates on startup, `CoolingModeChanged`, and `SettingsChanged` across all open windows using WinForms `ColorDialog` (`IWin32Window` owner wrapper).
- **Tray Badge Digit Polarity & Per-Drive Sample History (`Tray/TrayIconManager.cs`, `Models/TelemetrySample.cs`)**: Single and split-merged tray icons independently apply `CpuTrayWhiteDigits` and `GpuTrayWhiteDigits` (`#4C8028`, `#C44830`, `#1B6CB3` backgrounds for `#FFFFFF` ink vs pastel backgrounds for `#141414` ink) and hash both flags in the HICON cache signature. `TelemetrySample.DriveTemps` stores canonical `GetDriveExclusionKey` -> Celsius pairs (`TryGetDriveTemp`) so `MainWindow` renders per-drive `1` to `60` minute inline history charts without extra polling.
- **Dashboard Dynamic Height & Shared CPU Chart Engine (`Views/MainWindow.xaml`, `.xaml.cs`)**: `UpdateWindowHeight` recomputes height dynamically around `CpuLargeChartContainer` (`80 px` vs collapsed or `53 px` `CpuSmallChartRow`), `CpuSpeedRow` (`47 px`), `RamRow` (`47 px`), `GpuHealthRow` (`26 px`), actual `SsdRowsContainer` height (`0`, `38` unavailable, or `Sum(row.Height) + 3` against `79 px` baseline), and `OverlayRow` / `AutopilotRow`, clamped to `WorkArea.Height - 24`. Note: in `SettingsWindow.xaml`, custom `Style` declarations referencing `{StaticResource SettingsDarkComboBoxTemplate}` (`SettingsMiniComboStyle`) must be ordered after `SettingsDarkComboBoxTemplate` in `<Window.Resources>` to avoid forward-reference XAML load exceptions. Verified `0 Warning(s), 0 Error(s)` and `34 / 34 PASSED` elevated `/test` (`task-1854`).

## Wave 6 Architecture & Domain Rules

- **Vendor-Agnostic Hardware Detection & Snapshot Cloning (`Services/HardwareMonitorService.cs`, `Models/HardwareSnapshot.cs`, `Services/SystemInfoQueryService.cs`)**: `HardwareMonitorService.CloneSnapshot` copies `CpuVendor` and all 8 primary `Gpu*` fields (`GpuName`, `GpuVendor`, `GpuTemp`, `GpuLoad`, `GpuPowerWatts`, `GpuHotspotTemp`, `GpuVramTemp`, `GpuFanRpm`) so immediate `RefreshMemorySnapshotNow()` updates after RAM optimization keep live GPU telemetry intact. `ResolveCpuVendor` (`VendorIdentifier`, `Win32_Processor.Manufacturer`, and model tokens) and `ResolveGpuVendor` never default unknown vendors to `"Intel"`. `CleanCpuName` strips `\b\d+-Core\s+Processor\b` for any core count, `SystemInfoQueryService.SelectGraphicsCardSummary` excludes virtual display adapters when physical GPUs exist and lists the primary GPU first, `UpdateSsdRow` avoids `"SSD"` labels for rotational/unknown drives, and `SettingsWindow` View tray badge previews show live temperatures or `"--"` without hardcoded digits (`35 / 35 PASSED`).

## Wave 7 Dark Context Menu Rules

- **Dark WPF ContextMenu & Separator Styling (`Views/MainWindow.xaml`, `.xaml.cs`, `Views/SettingsWindow.xaml`)**: Every WPF context menu (`ToolsContextMenu` and `RamScopeMenu`) uses `RamDarkContextMenuStyle`, `RamDarkMenuItemStyle` (`OverridesDefaultStyle="True"`, collapsing `CheckBoxBdr` when `IsCheckable="False"` so non-checkable menus have zero icon gutter), and `DarkMenuSeparatorStyle` (`MenuItem.SeparatorStyleKey` override + explicit `#333333` 1 px `Border`). Both menus sync `FlowDirection` with `MainWindow`, `MenuToolsAbout` opens dark `Settings > About` via `OpenSettingsDialog(true)`, and read-only hotkey capture `TextBox`es (`TxtHotkeyShortcut`, `TxtRamHotkeyShortcut`) set `ContextMenu="{x:Null}"` (`36 / 36 PASSED`, `WHITE_GUTTER_HITS=0`).
