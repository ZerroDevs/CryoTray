# FUNCTIONS

Reuse these before adding a duplicate helper. Update this file when adding shared code.

## App lifecycle

- `App.ShowDashboard()`: creates `MainWindow` when absent, restores it above the tray, and activates it.
- `App.ToggleDashboard()`: closes the visible dashboard, otherwise calls `ShowDashboard()`.
- `App.ToggleOverlay()`: creates and shows `OverlayWindow` on enable; closes and clears the field on disable.
- `App.TrimWorkingSet()`: runs an aggressive GC, waits for finalizers, and calls `NativeMethods.EmptyWorkingSet` for the current process.
- `App.RegisterHotkeyIfEnabled()`: creates a message-only `HwndSource` for the hotkey and registers it, or disposes the source when the hotkey is disabled.
- `App.ExitApplication()`: asks `ConfirmExitWindow` when the setting is enabled and Cooling Mode is active; otherwise exits without changing the power plan.
- `App.DisableCoolingForExit()`: safety net used by `OnExit` for other shutdown paths, including updates.
- `MainWindow.OpenSettingsDialog()`, `OpenSystemInfoDialog()`, `OpenStressTestDialog()`: create one modal child at a time, set `_isChildDialogOpen`, and clear it when the dialog closes.

## Models

- `CryoTray.Models.CoreTelemetry`: `Index`, `Name` (`CPU00`), `Temperature` (raw C), `DisplayTemp` (rounded C), `IsHottest`.
- `CryoTray.Models.CoreReadout`: `Text`, `IsHottest`. UI-only popup item.
- `CryoTray.Models.GpuTelemetry`, `GpuDeviceOption`: per-GPU sensor snapshot and UI dropdown option (`Identifier`, `Name`, `Vendor`, `IsDiscrete`, `DisplayName`).
- `CryoTray.Models.HardwareSnapshot`: includes `CpuTemp` (`double.NaN` when unavailable), `PeakCpuTemp`, `MaxCoreTemp`, `CpuCoresTelemetry`, `Gpus`, `StorageDrives`, `PawnIoInstalled`.
- `CryoTray.Models.DriveTelemetry`: includes `PhysicalDriveNumber`, `SerialNumber`, `HardwareIdentifier`, `IsSolidState`, `DriveLetters`, `SmartHealthPercent`, `Temperature` (`double.NaN` when unavailable).
- `CryoTray.Models.AppSettings`, `SystemInfoData`, `TelemetrySample`.

## HardwareMonitorService

- `IsPawnIoInstalled()`: checks `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO` without throwing.
- `InstallOrOpenPawnIo()`: runs bundled `PawnIO_setup.exe` when present or opens `https://pawnio.eu/`.
- `IsDiscreteGpu(HardwareType, string?)`: classifies NVIDIA, non-APU AMD, and Intel Arc GPUs as discrete.
- `SelectPrimaryGpu(IReadOnlyList<GpuTelemetry>, string?)`: resolves explicit `PreferredGpuIdentifier` or Auto (discrete GPU priority).
- `GetAvailableGpus()`: thread-safe snapshot of detected GPU options for `SettingsWindow`.
- `ReadHardware()`: one poll tick. Updates CPU, motherboard, per-GPU telemetry (`ReadGpuTelemetry`), and storage nodes (`CreateDriveTelemetry` + `ReadDriveMetadata`), reopens LHM if PawnIO becomes installed, and sets `double.NaN` when sensors are absent.
- `ReadCpuRegistryInfo()`: reads the processor name and nominal MHz from the central processor registry key, avoiding WMI at startup.
- `CleanCpuName(string)`: removes redundant core-count and Processor suffixes.
- `ReadHardwareNamesFromLhm()`: fills CPU name from the opened LHM hardware list.
- `CollectCpuSensors(IReadOnlyList<ISensor>, CpuSensorSet)`: filters invalid temperatures and classifies per-core, package, CCD, load, and clock sensors.
- `ResolvePeak(CpuSensorSet, double?)`: hottest CCD Tdie, then Tctl/Tdie, then hottest CPU reading, then motherboard, else `null`.
- `ResolveFloor(double)`: `peak - 3.5`, clamped, so the bubble keeps a 3.5 C span.
- `BuildCoreTemperatures(CpuSensorSet, double, int)`: Mode A direct sensors plus Mode B fill.
- `BuildWeightedTemperatures(CpuSensorSet, double, double, int)`: 70 percent load plus 30 percent clock activity, min-max normalized, scaled between floor and peak.
- `SampleMax(IReadOnlyList<double>, int, int)`: groups contiguous sensor values when sensor count differs from physical core count.
- `CreateCoreTelemetry(int, double)`: builds `CPU00...` with raw and display temperatures.
- `GetKindOrder(string)`: P-Core 0, E-Core 1, Core 2.
- `UpsertCoreTemperature(...)`, `UpsertCoreValue(...)`: dedupe same core key, keep the higher value.
- `Start()`, `Stop()`, `Dispose()`, `RingBuffer`, `CurrentSnapshot`, `TelemetryUpdated`.
- `TelemetryRingBuffer.GetCpuStats(bool coolingOnly = false)` / `GetGpuStats(bool coolingOnly = false)`: compute min/avg/max over finite `> 0` samples, optionally scoped to `CoolingModeActive == true`.

## MainWindow popup

- `CoreItems` (`ObservableCollection<CoreReadout>`): popup item source.
- `CoreColumns`: 4 when core count is 16 or less, 6 above 16.
- `UpdateCpuCoreItems(HardwareSnapshot)`: builds display text using `DisplayTemp`, applies Fahrenheit if selected, refreshes `CoreItems`.
- `PositionCorePopup()`: measures the bubble, right-aligns it over the hero digits, clamps to the active monitor, flips below when needed.
- `TryGetMonitorWorkArea(out Rect)`: monitor work area in DIPs via `Screen.FromHandle` and `VisualTreeHelper.GetDpi`.
- `CpuCoresPopup_Opened`: runs positioning after the popup lays out.
- `OnPropertyChanged`: `INotifyPropertyChanged` helper.

## Chart hover

- `MainWindow.UpdateCpuChartTooltip(double)`: maps the cursor x to the nearest CPU sample, draws the guide and dot, and fills the CPU tooltip with temperature and time.
- `MainWindow.UpdateGpuChartTooltip(double)`: same mapping for the GPU chart, with the GPU value only.
- `MainWindow.ShowChartTooltip(Popup, Canvas, double, double, double)`: measures the card, clamps it to the chart width, and opens it above the sample.
- `MainWindow.DrawChartHoverMarker(Canvas, double, double, double, Color)`: clears and redraws the vertical guide and white dot.
- `MainWindow.HideChartTooltips()`: closes both cards and clears both hover layers.

## Charts and fan

- `MainWindow.CmbTimeframe_SelectionChanged(...)`: maps 1/5/10/30/60 min to 60/300/600/1800/3600 samples and redraws both charts. The initial XAML selection is guarded until all chart canvases exist.
- `MainWindow.DrawCpuChart()` and `DrawGpuChart()`: both slice the ring buffer with `_timeframeSamples`.
- `MainWindow.SetupFanAnimation()`: starts the six-petal rotor at 3.5 s per turn; `UpdateTheme` changes it to 0.9 s when cooling is active.
- `MainWindow.ApplyFanTheme(bool)`: sets the ON/OFF tile, rim, petal, and glow colors for the vector fan.
- `MainWindow.UpdateTheme(bool isCooling, bool animate)`: applies the fan theme, updates the toggle colors and dashboard accent, and redraws the CPU chart.
- `MainWindow.BtnCoolingToggle_Click(object, RoutedEventArgs)`: accessible `Button` handler for toggling Cooling Mode (`PowerPlanService.Instance.ToggleCoolingMode()`).
- `MainWindow.BtnCpuTempCores_Click` / `GotKeyboardFocus` / `LostKeyboardFocus`: opens or closes `CpuCoresPopup` on click or keyboard focus cues.

## Diagnostics

- `CpuSensorDump.Run()`: one-shot `/dump` mode. Opens a CPU-only `Computer`, writes every temperature and clock sensor with type, value, min, max, and identifier to `cpu-sensors.txt` in the project root, then exits. Must run elevated for real values.

## Power and settings

- `PowerPlanService.Instance`: `ToggleCoolingMode`, `SetCoolingMode`, `ApplyCpuCoolingParameters`, `EnsureCryoTrayCoolingScheme`, `ResetCryoTraySchemeToDefault`.
- `SettingsService.Instance`: `Current`, `Save`, `Export`, `Import`, `Reset`, `SettingsChanged`.
- `AutopilotService.Instance.ProcessTelemetry(HardwareSnapshot)`.

## Tray and UI helpers

- `NativeMethods.EmptyWorkingSet(IntPtr)`: asks Windows to trim the process working set.
- `SystemInfoQueryService.QuerySystemInfo()`: lazy WMI, EDID, boot, and BSOD read. Runs only when System Info opens; disposes each searcher, collection, and row.
- `SystemInfoQueryService.MatchDriveTelemetry(...)`: matches WMI drives against LHM `DriveTelemetry` by normalized serial number, `PhysicalDriveNumber`, and model + capacity without positional fallback.
- `TrayIconManager.UpdateBadges(gpuTemp, cpuTemp, coolingModeActive, force)`: renders numeric or `--` badges, skips identical frames via signature check, and destroys previous icon handles.
- `MainWindow.DockSideBySide(Window)`: places companion windows beside the dashboard.
- `SettingsWindow.RefreshGpuOptionsSafely()` / `LoadGpuOptions()` / `RefreshPawnIoBanner()` / `PrimaryGpuChoice.ToString()`: populates Primary GPU selector (with accessible display string) and missing-PawnIO notice.
- `SystemInfoQueryService.FormatUptime(TimeSpan)`: `d h m s` text.
- `SystemInfoQueryService.GenerateTextReport(SystemInfoData)`: save-to text report.
