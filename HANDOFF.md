# HANDOFF

## Performance & accuracy baselines (prior passes)

- WPF runs in `RenderMode.SoftwareOnly`, set before any window or `HwndSource` is created.
- `MainWindow` is created on demand, closed on deactivation or tray close, and recreated from the tray while the 3,600-sample `TelemetryRingBuffer` persists in `HardwareMonitorService`.
- Removed synthetic telemetry fallbacks across `HardwareSnapshot`, `HardwareMonitorService`, `TrayIconManager`, `MainWindow`, `OverlayWindow`, and `StressTestWindow` (now `double.NaN` / `null` rendered as `--` or unavailable notices), removed hardcoded `"C, Z"` / `"E"` drive letters, isolated multi-GPU telemetry with `Settings > GPU` primary picker, matched WMI/LHM drives by serial/disk index/model, added runtime PawnIO registry detection with install/download banners, and scoped `System_TempStats` to `CoolingModeActive == true`.

## Completed this turn (all 8 enhancements)

1. **Click-Through Overlay Mode**: Wired `AppSettings.OverlayClickThrough` to Win32 `WS_EX_TRANSPARENT` and collapsed close `X` in `Views/OverlayWindow.xaml.cs`; added `ChkOverlayClickThrough` in `Settings > View` and synced checkable `Tray_OverlayClickThrough` menu item in `Tray/TrayIconManager.cs`.
2. **Per-Drive Hide Toggles**: Wired `AppSettings.ExcludedDrives` to per-drive checkboxes (`PnlDiskDrives`) in `Settings > Disk` keyed by `HardwareMonitorService.GetDriveExclusionKey(...)`, filtering dashboard SSD rows (`MainWindow`) and `TemperatureAlertService` disk alerts while preserving saved exclusions if opened unelevated.
3. **Custom Global Hotkey Recorder**: Extended `Services/GlobalHotkeyService.cs` (`TryCaptureShortcut`, `FormatShortcutDisplay`, `IsRegistered`) and replaced hardcoded `Ctrl + Shift + <A-Z>` in `Settings > General` with interactive shortcut capture (`Ctrl`/`Alt`/`Shift`/`Win` + `A-Z`, `0-9`, or `F1-F12`) plus conflict notice when OS registration fails.
4. **Toolhelp32 Autopilot Scan & Fullscreen 3D Auto-Detect**: Replaced `Process.GetProcesses()` in `Services/AutopilotService.cs` with `CreateToolhelp32Snapshot` (`Process32FirstW`/`Process32NextW`), added `AutopilotPauseOnFullscreen` via `SHQueryUserNotificationState` + borderless full-monitor foreground window check excluding Windows shell processes, and added the `Settings > Autopilot` toggle.
5. **Laptop Battery Rule & Clean Uninstall Power Restore**: Added `AppSettings.EnableCoolingOnBattery` wired to `SystemEvents.PowerModeChanged` (`EvaluateBatteryCoolingRule` in `App.xaml.cs`), and added `/uninstall-cleanup` (`PowerPlanService.CleanupForUninstall`) invoked from `Installer/CryoTray.iss` on uninstall.
6. **Second-Launch Window Activation**: Added named `EventWaitHandle` (`CryoTray-Show-Dashboard-Event-99210`) signaling in `App.xaml.cs` so relaunching `CryoTray.exe` opens and foregrounds the running instance's `MainWindow`.
7. **Zero-WMI RAM Readout & CSV Session Export**: Added `GlobalMemoryStatusEx` physical RAM sampling (`RamUsedGb`, `RamTotalGb`, `RamUsagePercent`) rendered in `MainWindow` (`RamRow`) and optional `OverlayWindow` (`OverlayShowRam`), plus `Services/TelemetryCsvService.cs` (ring-buffer CSV export and 1 Hz live logging in `MainWindow > Tools`, and `BtnExportCsv` in `StressTestWindow`). Switched `StressTestWindow` CPU load workers to `ThreadPriority.BelowNormal` background threads and styled `StressActionBtnStyle` so `Export CSV` works cleanly under 100% load.
8. **CI Installer Build, `/test` Gate & Hygiene**: Updated `.github/workflows/release.yml` to run `/test`, publish `win-x64`, build `CryoTray-Setup-<version>.exe` via `ISCC.exe`, optionally sign both binaries, publish `.sha256` checksums for both `.exe` and `.zip`, added `cpu-sensors.txt` and `release/` to `.gitignore`, and untracked `cpu-sensors.txt`.

## Verification Summary

- `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false`: passed (`0 Warning(s), 0 Error(s)`), binary at `bin\Release\net8.0-windows\CryoTray.exe` (`388,096` bytes).
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1`: built `publish\win-x64\` and `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,442,164` bytes / `50.01 MB`).
- Elevated Administrator verification (`task-903`, `IsAdmin=True`, `High Mandatory Level`, exit code `0`):
  - `dotnet .\bin\Release\net8.0-windows\CryoTray.dll /test`: passed `20 / 20` checks with live PawnIO ring-0 sensors (`8 cores, Hottest: 54°C, 2 tied hottest`, `292 processes scanned`, `11.80/15.92 GB, 74.1%` RAM).
  - Live `CryoTray.exe` UI verification via `UIAutomation` + Win32 `PrintWindow(..., 2)` passed (`VERIFICATION_COMPLETE`) across `verify-01` through `verify-08` plus `verify-04c2-settings-disk-hidden.png`: verified live `50 °C` hero / `50W` package power / orange CPU graph, 8-core `CpuCoresPopup` with underlined hottest core, Cooling Mode ON drop to `38 °C` (`3.62 / 3.80 GHZ`, `30W`), `Settings > Disk` real NVMe list (`Argo N512SSD (C)`, `Argo N512SSD (E)`) and live dashboard row hiding, `OverlayWindow` (`CPU 40°C`, `GPU 50°C`, `RAM 11.8G 74%`), `SystemInfoWindow` (`SSD - 96% Good C`, `SSD - 100% Good E`, Cooling Mode temp `37 / 38 / 38 °C`), `StressTestWindow` (`80 °C`, `4.46 GHz`, `112W`, enabled `Export CSV`), and Arabic RTL.
- Updated `README.md` across all feature, overlay, hotkey, autopilot, stress test, CI, and architecture sections.

## Current status

- Release executable ready and elevated-verified: `bin\Release\net8.0-windows\CryoTray.exe` (`388,096` bytes, `Start-Process .\bin\Release\net8.0-windows\CryoTray.exe`).
- Windows installer ready: `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,482,163` bytes / `50.05 MB`).
- `0` running `CryoTray` processes; active Windows power plan restored to `Balanced` (`381b4222-f694-41f0-9685-ff5bb260df2e`).

## Environment

- WPF, .NET 8 (`net8.0-windows`), x64, Windows 10/11, per-monitor DPI aware, administrator manifest.
- `LibreHardwareMonitorLib` 0.9.6, `System.Management` 10.0.2, PawnIO 2.x (`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`), Inno Setup 6.7.3 (`ISCC.exe`).

## Wave 3 completion (Verified elevated)

- Implemented and verified all 4 Wave 3 items plus root-cause diagnosis of `"Launch app on PC startup"` (`Get-ScheduledTask -TaskName "CryoTray"` returned `NO_TASK_FOUND` while Settings saved `true` unconditionally; replaced with Task 1.2 UTF-16 LE BOM XML, `InteractiveToken`, `HighestAvailable`, `PT5S` delay, battery logon, `PT0S` timeout, `/autostart` duplicate-launch suppression, moved-`.exe` self-healing, and Settings error revert + Import/Reset sync).
- Release build: `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` passed with `0 Warning(s), 0 Error(s)` (`bin\Release\net8.0-windows\CryoTray.exe`, `388,096` bytes).
- Elevated `/test`: `24 / 24 PASSED` (`>>> ALL VERIFICATION TESTS PASSED SUCCESSFULLY! <<<`), including `TestStartupTaskXmlAndSync` (live elevated `schtasks` round-trip), `TestSsdRowFormatting`, `TestTimedCoolingPause`, and `TestStressCoolingDelta`.
- Installer build: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1` exited `0`, creating `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,446,080` bytes / `50.02 MB`).
- Live elevated UI verification (`ui_verify.ps1`, `IsAdmin=True`): verified live Ryzen 7 5800X + SSD (`C` 96%, `E` 100%) readouts, Stress Test two-phase delta (`PhaseOff='81 °C · 107 W'`, `PhaseOn='54 °C · 60 W'`, `Delta='-27 °C · -47 W'`), two-color chart (`#E07D62` OFF / `#2B88D8` ON), and Arabic RTL LTR `SSD (C:)` fix across `verify-01`..`verify-08`.
- Post-run host state verified clean: `PROCS=0`, active scheme `Balanced` (`381b4222-f694-41f0-9685-ff5bb260df2e`), `TASKS=0` (`CryoTray*`).

## Wave 4 completion & Hover Tooltip Fix (Verified elevated)

- Implemented RAM Telemetry Breakdown & Intelligent Memory Optimizer across `MemoryTelemetryService`, `MemoryOptimizerService`, `NativeMethods` (176-byte x64 `SYSTEM_MEMORY_LIST_INFORMATION`, `NtQuerySystemInformation(80)`, `NtSetSystemInformation(80)`, `RtlAdjustPrivilege(13)`, `GetPhysicallyInstalledSystemMemory`, `GetSystemTimes`), `HardwareMonitorService`, `GlobalHotkeyService` (dual IDs 9001/9002), `App.xaml.cs`, `MainWindow` (`RamRow` 26 px split button, dark scope `ContextMenu`, `2 px` stacked bar, live `#1E1E1E` `Memory Breakdown` tooltip), `Settings > Memory` tab, EN/AR RTL strings, and `README.md`.
- **Tooltip Hover Fix & Strict Pill Scoping (`Views/MainWindow.xaml`, `.xaml.cs`)**: Removed `RamBreakdownToolTip.StaysOpen = false` from `RamBreakdownToolTip_Closed` and `CloseRamBreakdownToolTip` (fixing `NotSupportedException` in `PopupControlService.ShowToolTip`), then moved `RamBreakdownToolTip` from row-wide `RamTelemetryZone` directly onto `BtnRamBreakdown` (`[ Used / Total ]` pill) so middle-row space and other controls never trigger the tooltip on hover. Extended `ExerciseRamTooltipLifecycleForTest()` / `TestRamTooltipHoverLifecycle()` to assert `FindToolTipOwner` returns `null` for `RamTelemetryZone` / `TxtRamPercent` and `BtnRamBreakdown` for `BtnRamBreakdown` / `TxtRamRatio`.
- **Owner-Drawn Dark Tray Menu Redesign (`Tray/TrayIconManager.cs`, `Views/OverlayWindow.xaml.cs`)**: Eliminated WinForms' `#F8F8F8` white check-margin strip (`ShowCheckMargin = false` + empty `OnRenderImageMargin`) and `.NET 8` `ToolStripProfessionalRenderer` VisualStyle `#B5D7F2` hover highlight by replacing `DarkMenuRenderer` with a pure `ToolStripRenderer` (`#1E1E1E` surface, `#3A3A3A` outer border, `#2D2D2D` 4 px rounded hover pill with `#424242` border, aligned trailing `13x13` `#29B6F6` checkbox and vector chevron, `#787878` disabled text, dynamic width reserve `MenuTextHorizontalReserve = 64` across main menu & `Pause Cooling Mode` submenu, and Arabic RTL mirroring). Added `RenderContextMenusForTest` and pixel regression test `TestTrayContextMenuDarkRendering()` (`MainMenu`/`SubMenu` LTR + Arabic RTL), plus dispatcher-safe `OverlayWindow.OnLanguageChanged()`.
- Release rebuild: `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` passed with `0 Warning(s), 0 Error(s)`.
- Elevated `/test`: `31 / 31 PASSED` (`Kernel MemoryBreakdown Telemetry`, `MemoryTelemetryService Dynamic GB/MB Formatting`, `GetSystemTimes CPU Idle Guard`, `MemoryOptimizerService Commands & Elevation Fallback` with live `Freed=3.74 GB`, `RAM Settings Persistence & Dual Hotkey IDs`, `RAM Breakdown Tooltip Hover & Pin Lifecycle`, and `Tray ContextMenu Dark Owner-Drawn Rendering`).
- Elevated live UI click-through (`task-1648`) & rendered tray menu capture (`verify-09-tray-context-menu.png`): verified `MAIN_WINDOW_BOUNDS: Width=444 Height=582`, Stress Test delta `-27 °C · -37 W`, Arabic RTL layout, dark tray menus (`924x276`, zero white gutter/baby-blue hover/truncation), and `CRASH_LOG_BEFORE=62` / `CRASH_LOG_AFTER=62`.
- Installer build (`task-1652`): `Exit 0`, `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,458,375` bytes / `50.03 MB`).
- Repo text hygiene & post-run state: `EM_DASH_HITS=0`, `PROCS=0`, active scheme `Balanced` (`381b4222-f694-41f0-9685-ff5bb260df2e`).

## Wave 5 completion (Verified elevated)

- Implemented all 5 Wave 5 features across `Models/AppSettings.cs`, `Models/TelemetrySample.cs`, `App.xaml.cs`, `Tray/TrayIconManager.cs`, `Views/SettingsWindow.xaml`, `Views/SettingsWindow.xaml.cs`, `Views/MainWindow.xaml`, `Views/MainWindow.xaml.cs`, `Resources/Strings.en.xaml`, `Resources/Strings.ar.xaml`, `Tests/VerificationTests.cs`, and `README.md`:
  1. **Custom Frame Colors for Cooling ON / OFF (`Settings > View`)**: Clickable swatch buttons (`BtnColorCoolingOn`, `BtnColorCoolingOff`) with WinForms `ColorDialog` (`FullOpen = true, AnyColor = true`), reset-to-default buttons, and live `WindowBorderBrush` updates across all open windows (`App.ApplyWindowAccent`, `ResolveFrameColor`).
  2. **Black vs White Tray Badge Digits + Live Sample Previews (`Settings > View` & `TrayIconManager`)**: Indented radio choices under GPU (`[00]`) and CPU (`[53] [53]`) with live-temperature inline sample badges (`UpdateTrayBadgeSampleTexts`), backed by WCAG-AA white-digit palettes (`#4C8028`, `#C44830`, `#1B6CB3`) across both single and split-merged tray icons.
  3. **CPU Clock Speed & Base Speed Toggles (`Settings > CPU` & `MainWindow`)**: `ShowCpuClock` (hides/shows `CpuSpeedRow` and shrinks window height) and `ShowCpuBaseSpeed` (displays `4.47 / 3.80` nominal base GHz vs `4.47`).
  4. **CPU Temperature Graph Visibility, Timeframe & Large vs Small Mode (`Settings > CPU` & `MainWindow`)**: `ShowCpuGraph`, bidirectional `CmbCpuGraphTimeframe` <-> `MainWindow.CmbTimeframe` sync (`1/5/10/30/60 min`), and `Large` vs compact `Small` (`50 px` inline `CPU (AMD)` / `CPU (Intel)` chart row above GPU) sharing `DrawCpuChartCore`.
  5. **SSD/HDD Visibility, Per-Drive Hide & Inline Temperature Graphs (`Settings > Disk`, `TelemetrySample`, & `MainWindow`)**: Master `ShowDiskTemp` toggle, per-drive hide checkboxes (`ExcludedDrives`), and per-drive `Show SSD graph` / `Show HDD graph` + individual timeframe dropdowns (`DiskGraphDrives`, `DiskGraphTimeframes`) backed by `TelemetrySample.DriveTemps` + `DrawDriveChart` + `SsdChartTooltip`.
- Release rebuild: `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` passed (`0 Warning(s), 0 Error(s)`).
- Elevated `/test`: `34 / 34 PASSED` (`>>> ALL VERIFICATION TESTS PASSED SUCCESSFULLY! <<<`), including `TestCustomFrameColorsAndSettingsPersistence`, `TestTrayBadgeBlackAndWhiteDigits`, and `TestCpuClockBaseSmallGraphAndDriveSamples`.
- Elevated live UI click-through (`task-1854`): verified `MAIN_WINDOW_BOUNDS: Width=444 Height=582`, live `4.47 / 3.80 GHZ` base speed readout, Stress Test delta `-22 °C · -43 W`, live captures of `Settings > View` digit previews + frame swatches (`verify-04b2-settings-view-top.png`), `Settings > CPU` (`verify-04cpu-settings-cpu.png`), `Settings > Disk` graph toggles (`verify-04c-settings-disk.png`), compact `CPU (AMD)` Small graph + `SSD (C:)` inline chart (`verify-10-small-cpu-and-ssd-graph.png`), and Arabic RTL layout (`verify-08-arabic-rtl.png`).
- Installer build (`task-1865`) & hygiene: `Exit 0`, `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,480,671` bytes / `50.05 MB`), `APP_EM_DASH_HITS=0`, `PROCS=0`, `TASKS=0`, active scheme `Balanced` (`381b4222-f694-41f0-9685-ff5bb260df2e`).

## Wave 6 Audit & Real-Hardware Fixes (Verified elevated)

- Completed codebase-wide hardware hardcoding audit across `Models/`, `Services/`, `Views/`, `Tray/`, `Resources/`, and `App.xaml*`: confirmed zero hardcoded CPU names, GPU names, drive models, or fake temperature fallbacks existed. Fixed 6 subtle edge cases so every vendor, multi-GPU rig, virtual display driver, core count, and blank state pulls 100% real data:
  1. **`CloneSnapshot` Primary GPU Preservation (`Services/HardwareMonitorService.cs`)**: Copied `CpuVendor` and all 8 `Gpu*` telemetry fields (`GpuName`, `GpuVendor`, `GpuTemp`, `GpuLoad`, `GpuPowerWatts`, `GpuHotspotTemp`, `GpuVramTemp`, `GpuFanRpm`) so `RefreshMemorySnapshotNow()` after RAM optimization never clears GPU telemetry between 1 s hardware ticks.
  2. **Dynamic CPU Vendor & Arbitrary Core-Count Cleaning (`Models/HardwareSnapshot.cs`, `Services/HardwareMonitorService.cs`, `Views/MainWindow.xaml.cs`)**: Added `HardwareSnapshot.CpuVendor` populated from registry `VendorIdentifier` (`AuthenticAMD`, `GenuineIntel`, `Qualcomm`, etc.), `Win32_Processor.Manufacturer`, and `ResolveCpuVendor`; replaced fixed `6/8/12/16-Core Processor` replacements in `CleanCpuName` with `\b\d+-Core\s+Processor\b`; updated `MainWindow.FormatCpuSmallRowLabel(cpuName, cpuVendor)` to fall back cleanly from reported vendor to `Common_Cpu` (`CPU`).
  3. **Vendor-Neutral GPU Detection & Prefix Compacting (`Services/HardwareMonitorService.cs`, `Views/MainWindow.xaml.cs`)**: Added `ResolveGpuVendor(HardwareType, string?)` so unknown adapters never misreport as `"Intel"`, and expanded `MainWindow.CompactGpuName` (`NVIDIA `, `AMD `, `Intel(R) `, `Intel `, `Qualcomm(R) `, `Qualcomm `).
  4. **Virtual Adapter Filtering & Multi-GPU Ordering (`Services/SystemInfoQueryService.cs`)**: Added `SelectGraphicsCardSummary` / `IsVirtualDisplayAdapter` to skip virtual adapters (`Parsec`, `Microsoft Basic/Remote Display`, `Hyper-V`, `Citrix`, `spacedesk`, `LuminonCore`, etc.) when physical GPUs exist and order the active primary GPU first.
  5. **Drive Fallback & Live Settings Preview Badges (`Views/MainWindow.xaml.cs`, `Views/SettingsWindow.xaml`, `.xaml.cs`)**: Replaced `"SSD"` blank-model fallback in `UpdateSsdRow` with `HDD` / `SSD` / `Alert_DriveName` by media type, replaced static `"53"` / `"00"` tray preview text with live CPU/GPU temperatures or `"--"`, and subscribed `SettingsWindow` View previews to `TelemetryUpdated`.
- Release rebuild: `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` passed (`0 Warning(s), 0 Error(s)`).
- Elevated `/test`: `35 / 35 PASSED` (`>>> ALL VERIFICATION TESTS PASSED SUCCESSFULLY! <<<`), including `TestZeroHardcodedHardwareInfoAndCloneIntegrity`.
- Installer build (`task-2028`): `Exit 0`, `Installer\Output\CryoTray-Setup-0.0.100.exe` (`52,482,163` bytes / `50.05 MB`).

## Next steps

- Run `git add -A` before committing so all 36 uncommitted changes since `a105f83` (32 modified files, 3 new `Services/*.cs` files, 1 removed `cpu-sensors.txt`) are staged together.
- Launch updated app cleanly with `Start-Process .\bin\Release\net8.0-windows\CryoTray.exe` (or run `Installer\Output\CryoTray-Setup-0.0.100.exe`).
- Restart Antigravity normally (without Administrator rights) once testing finishes.
