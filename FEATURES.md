# FEATURES

Exhaustive catalog of CryoTray features. Check before planning or adding features to enforce zero duplication.

## Telemetry & Hardware Monitoring

- **1 Hz Background Polling (`HardwareMonitorService`)**: Polls CPU, motherboard, multi-GPU, and storage sensors via LibreHardwareMonitorLib 0.9.6 plus PDH `% Processor Performance`. Uses `double.NaN` / `null` when sensors are unavailable rather than synthetic fallbacks.
- **Runtime PawnIO Driver Check & Recovery**: Checks `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`, shows actionable install/download banners in `MainWindow` and `SettingsWindow` when missing, and automatically reopens LHM once PawnIO is installed.
- **Primary GPU Selection (`Settings > GPU`)**: Isolates telemetry per GPU, defaults to Auto (discrete GPU priority), and lets users pick any detected GPU via `PreferredGpuIdentifier`.
- **Per-Core CPU Breakdown**: Maps direct Intel core diodes or weighted AMD CCD/Tctl temperatures into `CPU00..` cells in the hero hover bubble, underlining cores tied for hottest and showing package wattage.
- **60-Minute Ring Buffer & Charts (`TelemetryRingBuffer`)**: Stores 3,600 one-second samples in memory; powers CPU and GPU charts across 1, 5, 10, 30, and 60 minute timeframes with hover sample tooltips and unavailable-sensor states.
- **Storage Telemetry & SMART Health**: Displays drive temperature, SMART percentage health (`Good` / `Fair` / `Worn` / `Replace`), and model tooltip on the dashboard.

## Cooling Mode & Power Management

- **CryoTray Cooling Power Plan (`PowerPlanService`)**: Automatically imports and activates the embedded `CryoTray.pow` power scheme (`CryoTray Cooling`), syncing external Windows power-plan changes every 2 seconds.
- **Configurable CPU Cooling Controls**: Adjusts `PERFBOOSTMODE` (Disabled/Enabled) and `PROCTHROTTLEMAX` (50% to 100%) on the CryoTray Cooling plan, plus selectable normal fallback power plan.
- **Global Hotkey (`GlobalHotkeyService`)**: Optional `Ctrl+Shift+<A-Z>` hotkey via message-only `HwndSource`.
- **Safe Exit Behavior (`ConfirmExitWindow`)**: Optional `DisableCoolingModeOnExit` prompt that disables Cooling Mode and restores the normal power plan when exiting.

## Autopilot & Alerts

- **Autopilot Process Whitelist (`AutopilotService`)**: Polls running processes every 2.5 seconds; temporarily pauses active Cooling Mode while any whitelisted game/app runs and restores it when closed.
- **Temperature Alerts (`TemperatureAlertService`)**: Per-component thresholds (CPU, GPU, Disk) with 10 minute cooldown, 3 C re-arm hysteresis, mute toggle, and test notification button.

## System Tray & Companion Windows

- **Dynamic Tray Badges (`TrayIconManager`)**: Separate GPU (green) and CPU (coral/cyan) badges or one combined split badge showing temperature or `--`, with dark context menu and balloon alerts.
- **Dashboard (`MainWindow`)**: Tray-anchored or freely positioned dark window with animated six-petal vector cooling fan, keyboard-accessible `BtnCoolingToggle` switch and `BtnCpuTempCores` per-core popup trigger, CPU/GPU charts, SSD rows, and optional Overlay/Autopilot rows.
- **Floating Telemetry Overlay (`OverlayWindow`)**: Always-on-top draggable pill with configurable opacity (0% to 100%) and selectable CPU/GPU metrics.
- **CPU Temperature Test (`StressTestWindow`)**: Multi-core CPU load generator with live temperature/clock readouts, 60-sample chart, and inline Cooling Mode switch.
- **System Information (`SystemInfoWindow`)**: On-demand WMI, EDID monitor, active NIC, Event 100 boot duration, BSOD count, drive matching (serial, disk index, model), Cooling-Mode-scoped CPU temp stats, and plain-text report export.

## Settings, Localization & Updates

- **Settings Persistence & Portability (`SettingsService`)**: Stores JSON settings in `%APPDATA%\CryoTray\settings.json` with Export, Import, Reset, and Task Scheduler startup entry (`StartupTaskService`).
- **Bilingual UI & RTL (`LocalizationService`)**: Full English (`Strings.en.xaml`) and Arabic (`Strings.ar.xaml`) resource dictionaries with dynamic RTL layout switching while keeping numeric metrics LTR.
- **Signed & Checksummed GitHub Updater (`UpdateService` & `UpdateBackupService`)**: Automatic/manual GitHub Releases checks, SHA-256 verification, Authenticode validation, local rollback backup, and one-click version restore.
- **CLI Verification & Diagnostics**: `/test` runs `VerificationTests` and `/dump` writes `cpu-sensors.txt` via `CpuSensorDump`.
