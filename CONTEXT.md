# CONTEXT

## Architecture

- `App.xaml.cs`: software-rendered, single-instance WPF shell, tray manager, message-only hotkey `HwndSource`, on-demand `MainWindow`, App-owned overlay, and working-set trimming; `/dump` bypasses the mutex and writes `cpu-sensors.txt`.
- `Views/MainWindow`: 430 x 630 dashboard, hero CPU card, six-petal animated vector cooling fan, CPU and GPU charts with hover cards, dedicated timeframe strip, cooling toggle, telemetry rows, per-core hover popup. Created per open and destroyed on close or deactivation.
- `Views/OverlayWindow`, `SettingsWindow`, `StressTestWindow`, `SystemInfoWindow`: companion windows. Overlay is created only when toggled on; the other three are created per open.
- `Views/ConfirmExitWindow`: app-styled Yes/No confirmation shown when exiting would disable Cooling Mode.
- `Services/HardwareMonitorService`: 1 s background poll of LibreHardwareMonitor. Produces `HardwareSnapshot` and appends to a 3600-sample ring buffer. No WMI queries on the startup path; CPU name and base clock come from the registry and LHM names.
- `Services/PowerPlanService`: imports and switches the CryoTray Cooling power scheme.
- `Services/AutopilotService`: idle/busy load rules for cooling.
- `Services/SystemInfoQueryService`: lazy WMI, EDID, boot and BSOD queries. Runs only when System Info opens and disposes its WMI searchers, collections, and rows.
- `Services/NativeMethods`: Win32 imports, including `EmptyWorkingSet` for working-set trimming.
- `Tray/TrayIconManager`: two rendered badge icons, CPU and GPU.

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

- `snapshot.CpuTemp` is the integer display max across mapped cores. Hero, tray badge, overlay, chart, and stress test all read it.
- `snapshot.PeakCpuTemp` is the raw sensor peak before per-core mapping.
- Ignore CPU temperature sensors containing `Distance to TjMax` or `SoC`.
- Peak prefers the hottest CCD Tdie when present, then Tctl/Tdie, then the hottest CPU reading. On this Ryzen, Tctl/Tdie reads 43 C while CCD1 Tdie reads 40 C, and Camomile shows 40 C.
- Direct per-core sensor mapping is used only when no AMD `Tctl/Tdie` sensor exists.
- Floor is `peak - 3.5` so the bubble keeps a Camomile-sized few-degree span.
- Per-core sensor names are matched by `^(?:CPU\s*)?(P-Core|E-Core|Core)\s*#\s*(\d+)$`.
- Hottest cores are every core whose `DisplayTemp` equals the maximum.
- Popup columns are 4 up to 16 cores, 6 above 16.
