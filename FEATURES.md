# FEATURES

Exhaustive catalog of CryoTray features. Check before planning or adding features to enforce zero duplication.

## Telemetry & Hardware Monitoring

- **1 Hz Background Polling (`HardwareMonitorService`)**: Polls CPU, motherboard, multi-GPU, storage, and Win32 `GlobalMemoryStatusEx` physical RAM sensors via LibreHardwareMonitorLib 0.9.6 plus PDH `% Processor Performance`. Uses `double.NaN` / `null` when sensors are unavailable rather than synthetic fallbacks.
- **Runtime PawnIO Driver Check & Recovery**: Checks `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`, shows actionable install/download banners in `MainWindow` and `SettingsWindow` when missing, and automatically reopens LHM once PawnIO is installed.
- **Primary GPU Selection (`Settings > GPU`)**: Isolates telemetry per GPU, defaults to Auto (discrete GPU priority), and lets users pick any detected GPU via `PreferredGpuIdentifier`.
- **Per-Core CPU Breakdown**: Maps direct Intel core diodes or weighted AMD CCD/Tctl temperatures into `CPU00..` cells in the hero hover bubble, underlining cores tied for hottest and showing package wattage.
- **60-Minute Ring Buffer, Charts & CSV Export (`TelemetryRingBuffer`, `TelemetryCsvService`)**: Stores 3,600 one-second samples in memory; powers CPU and GPU charts across 1, 5, 10, 30, and 60 minute timeframes with hover tooltips, unavailable-sensor states, ring-buffer CSV export, and live 1 Hz CSV logging from the `Tools` menu.
- **Storage Telemetry, SMART Health & Per-Drive Filtering**: Displays drive temperature, SMART percentage health (`Good` / `Fair` / `Worn` / `Replace`), and model tooltip on the dashboard, with per-drive checkboxes (`Settings > Disk`, `ExcludedDrives`) to hide cold/external drives from the dashboard and disk temperature alerts.
- **Zero-WMI Physical RAM Metric**: Displays live used/total GB and utilization percentage in the dashboard (`RamRow`) and optionally in the floating overlay (`OverlayShowRam`).

## Cooling Mode & Power Management

- **CryoTray Cooling Power Plan (`PowerPlanService`)**: Automatically imports and activates the embedded `CryoTray.pow` power scheme (`CryoTray Cooling`), syncing external Windows power-plan changes every 2 seconds, and cleanly deletes the scheme on uninstall via `/uninstall-cleanup`.
- **Battery Auto-Cooling Rule**: Optional `EnableCoolingOnBattery` (`Settings > General`) via `SystemEvents.PowerModeChanged` enables Cooling Mode automatically on battery and restores the normal plan on AC reconnect only when battery activated it.
- **Configurable CPU Cooling Controls**: Adjusts `PERFBOOSTMODE` (Disabled/Enabled) and `PROCTHROTTLEMAX` (50% to 100%) on the CryoTray Cooling plan, plus selectable normal fallback power plan.
- **Custom Global Hotkey Recorder (`GlobalHotkeyService`)**: Interactive shortcut recorder in `Settings > General` supporting `Ctrl`, `Alt`, `Shift`, and `Win` plus `A-Z`, `0-9`, or `F1-F12` via message-only `HwndSource`, with conflict detection feedback.
- **Safe Exit Behavior (`ConfirmExitWindow`)**: Optional `DisableCoolingModeOnExit` prompt that disables Cooling Mode and restores the normal power plan when exiting.

## Autopilot & Alerts

- **Toolhelp32 Process Scan & Fullscreen 3D Autopilot (`AutopilotService`)**: Scans running processes every 2.5 seconds via `CreateToolhelp32Snapshot`, temporarily pausing Cooling Mode while any whitelisted app runs or (optionally via `AutopilotPauseOnFullscreen`) when a fullscreen Direct3D/presentation/borderless game is active via `SHQueryUserNotificationState`.
- **Temperature Alerts (`TemperatureAlertService`)**: Per-component thresholds (CPU, GPU, non-excluded Disks) with 10 minute cooldown, 3 C re-arm hysteresis, mute toggle, and test notification button.

## System Tray & Companion Windows

- **Dynamic Tray Badges & Second-Launch Activation (`TrayIconManager`, `App`)**: Separate GPU (green) and CPU (coral/cyan) badges or one combined split badge showing temperature or `--`, owner-drawn dark `#1E1E1E` tray context menu + pause submenu (`#2D2D2D` hover pill, trailing `#29B6F6` checkbox, vector chevron, and Arabic RTL flip) with checkable `Overlay Click-Through`, and named `EventWaitHandle` activation so relaunching `CryoTray.exe` focuses the existing dashboard.
- **Dashboard (`MainWindow`)**: Tray-anchored or freely positioned dark window (`444 x 673` baseline) with animated six-petal vector cooling fan, keyboard-accessible `BtnCoolingToggle` and `BtnCpuTempCores`, CPU/GPU charts, RAM row, filtered SSD rows, `Tools` CSV export/logging actions, and optional Overlay/Autopilot rows.
- **Floating Telemetry Overlay (`OverlayWindow`)**: Always-on-top draggable pill with configurable opacity (0% to 100%), optional click-through `WS_EX_TRANSPARENT` pass-through (`OverlayClickThrough`), and selectable CPU, GPU, and RAM metrics.
- **CPU Temperature Test (`StressTestWindow`)**: Multi-core CPU stress generator running on `BelowNormal` background worker threads with live temperature/clock readouts, 60-sample chart, inline Cooling Mode switch, and session CSV export (`Export CSV`).
- **System Information (`SystemInfoWindow`)**: On-demand WMI, EDID monitor, active NIC, Event 100 boot duration, BSOD count, drive matching (serial, disk index, model), Cooling-Mode-scoped CPU temp stats, and plain-text report export.

## Settings, Localization, Updates & Installer

- **Settings Persistence & Portability (`SettingsService`)**: Stores JSON settings in `%APPDATA%\CryoTray\settings.json` with Export, Import, Reset, and Task Scheduler startup entry (`StartupTaskService`).
- **Bilingual UI & RTL (`LocalizationService`)**: Full English (`Strings.en.xaml`) and Arabic (`Strings.ar.xaml`) resource dictionaries with dynamic RTL layout switching while keeping numeric metrics LTR.
- **Signed & Checksummed GitHub Updater (`UpdateService` & `UpdateBackupService`)**: Automatic/manual GitHub Releases checks, SHA-256 verification, Authenticode validation, local rollback backup, and one-click version restore.
- **CI Installer & CLI Diagnostics**: `.github/workflows/release.yml` runs the 35-check `/test` verification suite before publishing `CryoTray-Setup-<version>.exe` (Inno Setup + bundled `PawnIO_setup.exe` + `/uninstall-cleanup`) alongside the portable zip; `/dump` writes `cpu-sensors.txt` via `CpuSensorDump`.

## Wave 3 Features (Completed & Verified)

- **Self-healing Task Scheduler 1.2 Startup Registration (`Services/StartupTaskService.cs`, `App.xaml.cs`, `SettingsWindow.xaml.cs`)**: Registers UTF-16 LE BOM XML task with `InteractiveToken`, `HighestAvailable`, `PT5S` delay, battery logon enabled, `PT0S` limit, `/autostart` silent background flag, startup `.exe` path repair, Import/Reset task sync, and UI revert/error banner on registration failure.
- **Dashboard SSD Drive Letters & SMART % (`Views/MainWindow.xaml.cs`)**: Displays drive badge letters (`C`, `E`), `SSD (C:)` / `SSD (E:)` labels with `FlowDirection.LeftToRight` RTL protection, and inline WCAG-AA color-coded SMART percentages (`#ABD068`, `#F3C765`, `#FAAA9A`).
- **Timed Cooling Mode Pause (`Tray/TrayIconManager.cs`, `App.xaml.cs`, `Services/AutopilotService.cs`)**: Adds `Pause Cooling Mode` tray submenu (`15m`, `30m`, `1h`, `Resume Cooling Now`), remaining-minutes menu/tooltip countdowns, battery-rule deferral while paused, manual override cancellation, and `AutopilotService` pause handoff.
- **Stress Test Cooling OFF vs ON Delta & Two-Color Chart (`Views/StressTestWindow.xaml`, `.xaml.cs`)**: Adds 3-column `COOLING OFF` / `COOLING ON` / `DELTA` comparison card (`ComputeCoolingComparison` trailing 5-sample averages) and two-color thermal curve (`#E07D62` OFF vs `#2B88D8` ON).

## Wave 5 Features (Completed & Verified)

- **Custom Cooling ON / OFF Window Frame Colors (`Settings > View`, `App.xaml.cs`)**: Lets users customize `CoolingOnFrameColor` (`#2B88D8` default) and `CoolingOffFrameColor` (`#C26A47` default) via `ColorDialog` swatches and reset buttons, updating `WindowBorderBrush` live across all open windows.
- **Black vs White Tray Digits & Live Badge Previews (`Settings > View`, `Tray/TrayIconManager.cs`)**: Adds Black digits (default) vs White digits contrast-verified palettes independently for GPU and CPU badges (and both halves of merged icons), accompanied by live-temperature preview samples in Settings.
- **CPU Clock Speed, Base Speed & Small/Large CPU Graph Modes (`Settings > CPU`, `Views/MainWindow.xaml`, `.xaml.cs`)**: Adds `ShowCpuClock`, `ShowCpuBaseSpeed`, `ShowCpuGraph`, bidirectional `1` to `60` minute timeframe selection, and compact `Small` (`CPU (AMD)` / `CPU (Intel)`) inline chart mode alongside `Large`.
- **SSD/HDD Master Visibility & Per-Drive Inline Temperature Graphs (`Settings > Disk`, `Models/TelemetrySample.cs`, `Views/MainWindow.xaml.cs`)**: Adds `ShowDiskTemp` master visibility toggle alongside per-drive hide (`ExcludedDrives`) and per-drive `Show SSD graph` / `Show HDD graph` + individual `1` to `60` minute timeframes (`DiskGraphDrives`, `DiskGraphTimeframes`).

- **Kernel RAM Telemetry Breakdown & 2 px Row Bar (`Services/MemoryTelemetryService.cs`, `Views/MainWindow.xaml`, `.xaml.cs`)**: Displays physical installed RAM (`GetPhysicallyInstalledSystemMemory`), usable/available totals (`GlobalMemoryStatusEx`), and exact x64 NT page-list breakdown (`NtQuerySystemInformation(80)`: Free, Modified, Standby, Cached, Hardware Reserved) inside a live-updating `#1E1E1E` `Memory Breakdown` tooltip triggered strictly on the `[ Used / Total ]` RAM pill (`BtnRamBreakdown`), plus a `2 px` stacked bar along the `RamRow` bottom divider without increasing `MainWindow` height (`444x582` / `673` expanded).
- **Safe On-Demand Kernel Memory Optimizer & Scope Menu (`Services/MemoryOptimizerService.cs`, `Views/MainWindow.xaml`, `.xaml.cs`)**: Compact `26 px` `Optimize RAM` split button executes background `RtlAdjustPrivilege(13)` + `NtSetSystemInformation(80)` commands (`MemoryEmptyWorkingSets = 2`, `MemoryFlushModifiedList = 3`, `MemoryPurgeStandbyList = 4`), flashes `#29B6F6` checkmark, updates session `Last freed`, optionally clears clipboard & raises tray notification, and exposes a dark right-click/chevron menu for cleaning scopes.
- **CPU-Idle Auto-Optimization, Settings Memory Tab & Global Hotkey (`Views/SettingsWindow.xaml`, `.xaml.cs`, `Services/GlobalHotkeyService.cs`, `App.xaml.cs`)**: Adds `Settings > Memory` tab with cleaning toggles, `10%` to `50%` available-RAM threshold (5-minute cooldown), 1 s `GetSystemTimes` `< 10%` CPU idle guard, `1h` to `6h` periodic schedule, and custom `RegisterHotKey` global shortcut (`Ctrl + Alt + M` default, ID `9002`) with conflict detection across Export/Import/Reset and English/Arabic RTL.

## Wave 6 Enhancements (Verified Elevated)

- **Vendor-Agnostic Real-Hardware Telemetry (`HardwareMonitorService`, `SystemInfoQueryService`, `MainWindow`, `SettingsWindow`)**: Populates real `HardwareSnapshot.CpuVendor` (`VendorIdentifier`, WMI `Manufacturer`, model tokens), handles arbitrary `<N>-Core Processor` strings in `CleanCpuName`, resolves GPU vendors via `ResolveGpuVendor` without defaulting unknown hardware to `"Intel"`, filters virtual display adapters when physical GPUs exist (`SelectGraphicsCardSummary`), preserves `CpuVendor` and all 8 `Gpu*` fields inside `CloneSnapshot` / `RefreshMemorySnapshotNow()`, avoids `"SSD"` fallback labels on rotational/unknown drives, and live-updates tray digit preview samples (`--` when unavailable).
