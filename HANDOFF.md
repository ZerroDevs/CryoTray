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

- Installed PawnIO 2.2.0, the driver LibreHardwareMonitor 0.9.6 needs for low-level sensor access.
- Elevated dump now reads real values: `Core (Tctl/Tdie)` 43 C and `CCD1 (Tdie)` 40 C, plus live clocks.
- Peak now prefers the hottest CCD Tdie, then Tctl/Tdie. The previous `AllTemps.Max()` would show 43 C where Camomile shows 40 C.
- Floor is now `peak - 3.5` so the popup keeps a 3.5 C span instead of flattening when peak is a CCD reading.

- Refactored `Services/HardwareMonitorService.cs` for universal CPU sensor discovery and per-core mapping.
- Added `PeakCpuTemp` to `HardwareSnapshot` and `DisplayTemp` to `CoreTelemetry`.
- Added `Models/CoreReadout.cs` for hover popup binding.
- Replaced the code-built `UniformGrid` popup with a bound `ItemsControl` in `Views/MainWindow.xaml`.
- Added dynamic 4/6 column layout, underline binding for hottest cores, and monitor-aware popup positioning.
- Corrected peak selection to use the hottest valid CPU reading and added the 3.5 C floor guard band after live readings came out about 4 C below Camomile.

## Current status

- Added `CryoTray.exe /dump`: writes every CPU temperature and clock sensor (name, type, value, min, max, identifier) to `cpu-sensors.txt` in the project root.
- The dump uses `Elevated: True/False` in its header so an unelevated run is obvious. Sensor values are 0 when LHM cannot access the hardware.
- `dotnet build -c Release` passes with 0 warnings and 0 errors after the CCD peak change.
- Output: `bin/Release/net8.0-windows/CryoTray.exe`.
- PawnIO is a machine-level dependency: without it, all CPU sensors return 0 and the synthetic fallback takes over.
- The old running instance (PID 16300) is closed, so the normal `bin/Release` output is writable again.
- `dotnet build -c Release` passes with 0 warnings and 0 errors after the fan and timeframe changes.
- The rebuilt app loaded the new fan geometry and selector strip without a XAML exception. The final visual check is still pending.

## Environment

- WPF, .NET 8 (`net8.0-windows`), x64.
- LibreHardwareMonitorLib 0.9.6, System.Management 10.0.2.
- Windows, per-monitor DPI aware, administrator manifest.

## Next steps

- Launch `bin\Release\net8.0-windows\CryoTray.exe` elevated and check the layered fan, its glow pulse, the selector strip, and both charts switching together.
- Open and close the dashboard from the tray, toggle Overlay on and off, and open Settings and System Info once to verify the lazy window lifecycle.
- Hover the hero temperature to confirm the popup layout on the target machine.
- Run `CryoTray.exe /test` if a hardware-level regression check is needed.
- Review dual-socket behavior if that hardware ever becomes a target. Current core keys are not namespaced per socket.
