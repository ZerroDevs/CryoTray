# HANDOFF

## Performance pass

- WPF now runs in `RenderMode.SoftwareOnly`, set before any window or `HwndSource` is created.
- `MainWindow` is created on demand, closed on deactivation or tray close, and recreated from the tray. The 3600-sample ring buffer remains in `HardwareMonitorService`, so chart history survives the window cycle.
- `App` owns `OverlayWindow` and creates it only when the overlay is toggled on. Closing the overlay calls `Close()`, clears `_overlayWindow`, and trims the working set.
- `SettingsWindow`, `SystemInfoWindow`, and `StressTestWindow` are created per open. Settings and System Info clear their content on close, and System Info also clears its WMI data and stops its timer.
- Hotkeys use a message-only `HwndSource` owned by `App`, so they survive `MainWindow` destruction.
- WMI was removed from `HardwareMonitorService`. CPU name and base clock come from the registry and LHM hardware names. `SystemInfoQueryService` runs only when System Info opens and disposes its searchers, collections, and rows.
- `System.Management` still appears in the loaded module list because LibreHardwareMonitor references it. CryoTray itself does not issue a WMI query until System Info opens.
- `EmptyWorkingSet` runs 2 seconds after startup and after the dashboard, overlay, or System Info closes.
- Measured idle private working set: 23.11 to 23.80 MB. Total working set: 51.82 to 52.50 MB. GPU Engine utilization for the process: 0 percent. The test process ran through `dotnet`, not the elevated apphost.
- `dotnet build -c Release` passes with 0 warnings and 0 errors.
- Not click-tested: tray dashboard reopen, overlay toggle, Settings, and System Info dialogs.

## UI pass

- Added CPU and GPU chart hover cards with a dot, vertical guide, white rounded card, temperature, and sample time.
- Window is now 430 x 630, body text uses Segoe UI Variable Text, and the hero temperature uses Segoe UI Variable Display at 54 px.
- Tray badges fill the icon canvas, use Segoe UI Variable Display at 20 px for two-digit values, and use the sampled Camomile palette: green `#ABD068`, peach `#FAAA9A`, blue `#76C6ED`.
- Widened the GPU chart to 210 x 36 and shifted the hero circuit chip right to match Camomile's composition.
- Rebuilt the cooling fan as a layered 38 px rotor: 7 swept gradient blades, outer and inner rings, four screws, a gold hub, and a blurred blue glow. The blade group spins at 0.9 s per turn when cooling is on and 3.5 s when off, and the glow pulses only while cooling is on.
- Moved the timeframe selector out of the plot into a dedicated 26 px strip below the 78 px CPU chart. `1 min`, `5 min`, `10 min`, `30 min`, and `60 min` now redraw both the CPU and GPU charts from the same sample window.
- `dotnet build -c Release` passes with 0 warnings and 0 errors.

## Completed this turn

- Removed synthetic fallback telemetry across `HardwareSnapshot`, `HardwareMonitorService`, `TrayIconManager`, `MainWindow`, `OverlayWindow`, and `StressTestWindow`: unavailable sensors now stay `double.NaN` / `null` and render `--` or inline unavailable messages instead of invented CPU/GPU numbers.
- Removed hardcoded `"C, Z"` / `"E"` drive-letter fallback in `Services/SystemInfoQueryService.cs`; WMI partition mapping now falls back only to LHM `DriveLetters` metadata or `Common_NotReported`.
- Fixed multi-GPU sensor mixing by isolating per-GPU collection (`ReadGpuTelemetry`), classifying discrete vs integrated GPUs (`IsDiscreteGpu`, `SelectPrimaryGpu`), persisting `AppSettings.PreferredGpuIdentifier`, and adding the Primary GPU selector in `SettingsWindow` (`Settings > GPU`).
- Replaced positional index pairing in `SystemInfoQueryService` with `MatchDriveTelemetry(...)` matching by normalized `SerialNumber`, `PhysicalDriveNumber`, and `Model` (tie-broken by capacity), backed by reflection metadata from LHM storage objects (`DriveNumber`, `SerialNumber`, `IsSSD`, `Partitions`).
- Added runtime PawnIO driver detection (`IsPawnIoInstalled` / `InstallOrOpenPawnIo`), automatic LHM reopen once PawnIO becomes installed, and actionable missing-driver banners (`PawnIoBanner` in `MainWindow`, `PawnIoSettingsBanner` in `SettingsWindow`).
- Scoped `TelemetryRingBuffer.GetCpuStats(bool coolingOnly)` so `SystemInfoQueryService` reports Cooling Mode temperatures only (`System_TempStatsNoCooling` when only non-cooling samples exist) and filters non-finite/non-positive readings.
- Updated `Tests/VerificationTests.cs` (13 checks total) and `README.md`.
- Upgraded `Views/MainWindow.xaml` and `Views/MainWindow.xaml.cs` so the Cooling Mode switch (`BtnCoolingToggle`) and hero CPU temperature (`BtnCpuTempCores`) use zero-chrome keyboard-accessible `Button` controls with `AutomationPeer` support; added `PrimaryGpuChoice.ToString()` in `Views/SettingsWindow.xaml.cs`.
- Ran live UI verification (`dotnet bin\Release\net8.0-windows\CryoTray.dll`) inside the agent's isolated `exebox` desktop using `UIAutomation` patterns and Win32 `PrintWindow(..., 2)`, capturing and visually inspecting `verify-01-dashboard-initial.png` through `verify-04-settings-gpu-selector.png` (`MainWindow` Cooling OFF/ON, `CpuCoresPopup`, `SettingsWindow > GPU > CmbPrimaryGpu`).
- Verified compilation of all edits including `PrimaryGpuChoice.ToString()` via `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` (0 warnings, 0 errors), producing `bin\Release\net8.0-windows\CryoTray.exe` (388,096 B) and `bin\Release\net8.0-windows\CryoTray.dll` (1,113,600 B, `FileVersion 0.0.100`).
- Ran `dotnet .\bin\Release\net8.0-windows\CryoTray.dll /test` after final build: all 13/13 checks passed and restored `Balanced` power scheme (`381b4222-f694-41f0-9685-ff5bb260df2e`), with 0 running `CryoTray` processes.

## Current status

- `dotnet build CryoTray.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false` passes with 0 warnings and 0 errors.
- `dotnet .\bin\Release\net8.0-windows\CryoTray.dll /test` passes 13/13 checks.
- Latest binary ready at `bin\Release\net8.0-windows\CryoTray.exe` (`Start-Process .\bin\Release\net8.0-windows\CryoTray.exe`).
- Live GUI verification completed for `MainWindow` Cooling OFF/ON, `CpuCoresPopup`, and `SettingsWindow > GPU > CmbPrimaryGpu` (`verify-01` to `verify-04`); `scratch\ui_verify.ps1` is ready for optional capture of `OverlayWindow`, `SystemInfoWindow`, `StressTestWindow`, and Arabic RTL (`verify-05` to `verify-08`).

## Environment

- WPF, .NET 8 (`net8.0-windows`), x64, Windows 10/11, per-monitor DPI aware, administrator manifest.
- `LibreHardwareMonitorLib` 0.9.6, `System.Management` 10.0.2, PawnIO 2.x (`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`).

## Next steps

- Run `Start-Process .\bin\Release\net8.0-windows\CryoTray.exe` on desktop (with UAC prompt) to inspect elevated CPU thermal diodes and drive telemetry live.
- Optionally run `scratch\ui_verify.ps1` when no CryoTray instance is running to capture `verify-05` to `verify-08`.
- Address remaining enhancement groups on request (e.g., wiring `OverlayClickThrough` and `ExcludedDrives`, WCAG AA contrast fixes on `#2B88D8` / hero gradients).
