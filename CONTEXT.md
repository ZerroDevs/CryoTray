# CONTEXT

## Architecture

- `App.xaml.cs`: software-rendered, single-instance WPF shell, tray manager, message-only hotkey `HwndSource`, on-demand `MainWindow`, App-owned overlay, and working-set trimming; `/dump` bypasses the mutex and writes `cpu-sensors.txt`.
- `Views/MainWindow`: 444 x 626 baseline dashboard (expands by 38 px when `PawnIoBanner` is visible and adjusts for `GpuHealthRow`, `OverlayRow`, `AutopilotRow`), hero CPU card, six-petal animated vector cooling fan, CPU and GPU charts with hover cards and unavailable states, dedicated timeframe strip, cooling toggle, telemetry rows, per-core hover popup. Created per open and destroyed on close or deactivation.
- `Views/OverlayWindow`, `SettingsWindow`, `StressTestWindow`, `SystemInfoWindow`: companion windows. Overlay is created only when toggled on; the other three are created per open.
- `Views/ConfirmExitWindow`: app-styled Yes/No confirmation shown when exiting would disable Cooling Mode.
- `Services/HardwareMonitorService`: 1 s background poll of LibreHardwareMonitor with runtime PawnIO detection (`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`), automatic reopen if PawnIO is installed later, per-GPU telemetry isolation, primary GPU selection (`AppSettings.PreferredGpuIdentifier`), and LHM storage metadata caching. Produces `HardwareSnapshot` and appends to a 3600-sample ring buffer.
- `Services/PowerPlanService`: imports and switches the CryoTray Cooling power scheme.
- `Services/AutopilotService`: process-whitelist polling every 2.5 s to pause Cooling Mode while whitelisted apps run.
- `Services/SystemInfoQueryService`: lazy WMI, EDID, boot, and BSOD queries. Matches LHM storage telemetry to `Win32_DiskDrive` via serial, physical disk index, and model, and queries Cooling-Mode-only CPU temp stats.
- `Services/NativeMethods`: Win32 imports, including `EmptyWorkingSet` for working-set trimming.
- `Tray/TrayIconManager`: GPU, CPU, or merged split tray badges (`--` when unavailable, skipping redundant HICON redraws via signature checks).

## Dependencies

- `LibreHardwareMonitorLib` 0.9.6 for hardware sensors.
- `System.Management` for WMI.
- `UseWindowsForms` is enabled for tray icons and monitor bounds lookup.

## Styling tokens

- Window base `#141414`, chart base `#121212`, borders `#262626`, separators `#202020`.
- Text primary `#FFFFFF`, secondary `#CCCCCC`, muted `#888888`.
- Cooling OFF accent `#E07D62`, cooling ON accent `#2196F3`, fan cyan `#76C6EF`, fan ring `#2B88D8`, gold hub `#F3C765`.
- Popup white `#FFFFFF`, grid border `#E2E2E2`, popup text `#1A1A1A`.
- Body font `Segoe UI Variable Text`; display and tray font `Segoe UI Variable Display`.
- Tray palette: GPU `#ABD068`, CPU off `#FAAA9A`, CPU on `#76C6ED`.

## Domain rules

- Unavailable numeric telemetry (`CpuTemp`, `PeakCpuTemp`, `MaxCoreTemp`, `CpuGhz`, `CpuBaseGhz`, `CpuMaxGhz`, `CpuLoad`, `GpuTemp`, `GpuLoad`, `DriveTelemetry.Temperature`) uses `double.NaN` (and `null` for `int?` / `double?`), never synthetic values; UI renders `--` or an unavailable message.
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
- `SystemInfoQueryService` CPU temp stats (`System_TempStats`) report `CoolingModeActive == true` samples only (`TelemetryRingBuffer.GetCpuStats(coolingOnly: true)`).
- **UI Verification in Agent Shell (`exebox`)**: Shell commands run in an isolated Win32 desktop (`exebox-...`), separate from `WinSta0\Default`, so `Graphics.CopyFromScreen` fails (`Invalid handle`) and `SetCursorPos` stays `(0,0)`. Interactive UI verification uses Windows `UIAutomation` patterns plus Win32 `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT = 2)` across `CryoTray` `HwndWrapper` windows (`MainWindow` `BtnCoolingToggle` and `BtnCpuTempCores` were upgraded to accessible `Button` controls to support keyboard and `InvokePattern` activation).
