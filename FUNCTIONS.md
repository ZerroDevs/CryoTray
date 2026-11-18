# FUNCTIONS

Reuse these before adding a duplicate helper. Update this file when adding shared code.

## App lifecycle

- `App.DefaultCoolingOnFrameColor` / `DefaultCoolingOffFrameColor`, `ResolveFrameColor(string?, string)`, `NormalizeHexColor(Color)`: safely parse and normalize custom Cooling ON/OFF window frame accent hex colors.
- `App.ShowDashboard()`: creates `MainWindow` when absent, restores it above the tray, and activates it.
- `App.ToggleDashboard()`: closes the visible dashboard, otherwise calls `ShowDashboard()`.
- `App.ToggleOverlay()`: creates and shows `OverlayWindow` on enable; closes and clears the field on disable.
- `App.ToggleOverlayClickThrough()`: toggles `Settings.OverlayClickThrough`, saves settings, applies `WS_EX_TRANSPARENT` to `OverlayWindow` if open, and updates the tray menu state.
- `App.TrimWorkingSet()`: runs an aggressive GC, waits for finalizers, and calls `NativeMethods.EmptyWorkingSet` for the current process.
- `App.RegisterHotkeyIfEnabled()`: creates a message-only `HwndSource` (`CryoTrayHotkey`) when Cooling Mode or RAM hotkey is enabled and registers/unregisters `CoolingHotkeyId` (`9001`) and `RamOptimizeHotkeyId` (`9002`).
- `App.TrySignalExistingInstance()`: signals the named `EventWaitHandle` (`CryoTray-Show-Dashboard-Event-99210`) when a non-`/autostart` second `CryoTray.exe` launch finds an active primary instance.
- `App.RegisterActivationWaitHandle()`, `UnregisterActivationWaitHandle()`, `ActivateDashboardFromSignal()`: listen for second-launch activation signals on a thread-pool wait handle and foreground `MainWindow`.
- `App.PauseCoolingFor(TimeSpan)`, `CancelCoolingPause(bool, bool)`, `CoolingPauseTimer_Tick()`, `IsCoolingPausedByTimer`, `CoolingPausedUntilUtc`: manage timed Cooling Mode pauses (`15m`, `30m`, `1h`), tray countdowns, manual/battery guards, and autopilot handoff; `StartupTaskService.EnsureSynchronized(settings.LaunchOnStartup)` heals moved `.exe` paths at startup.
- `App.EvaluateBatteryCoolingRule()`: handles `SystemEvents.PowerModeChanged` (`EnableCoolingOnBattery`) so battery power enables Cooling Mode (unless timed pause is active) and AC restores normal only when battery activated it.
- `App.ExitApplication()`: asks `ConfirmExitWindow` when the setting is enabled and Cooling Mode is active; otherwise stops pause timer, stops `MemoryOptimizerService`, unregisters both hotkeys, and exits without changing the power plan.
- `App.DisableCoolingForExit()`: safety net used by `OnExit` for other shutdown paths, including updates.
- `MainWindow.OpenSettingsDialog()`, `OpenSystemInfoDialog()`, `OpenStressTestDialog()`, `RunWithChildDialogGuard(Action)`: manage child dialogs cleanly and preserve `_isChildDialogOpen` across nested dialogs like CSV `SaveFileDialog`.

## Models

- `CryoTray.Models.CoreTelemetry`: `Index`, `Name` (`CPU00`), `Temperature` (raw C), `DisplayTemp` (rounded C), `IsHottest`.
- `CryoTray.Models.CoreReadout`: `Text`, `IsHottest`. UI-only popup item.
- `CryoTray.Models.GpuTelemetry`, `GpuDeviceOption`: per-GPU sensor snapshot and UI dropdown option (`Identifier`, `Name`, `Vendor`, `IsDiscrete`, `DisplayName`).
- `CryoTray.Models.MemoryBreakdown`: physical `InstalledBytes`, `TotalUsableBytes`, `AvailableBytes`, `InUseBytes`, `HardwareReservedBytes`, `MemoryLoadPercent`, `HasPageListBreakdown`, `FreeBytes`, `StandbyBytes`, `ModifiedBytes`, computed `CachedBytes`, `InUsePercent`, `AvailablePercent`.
- `CryoTray.Models.HardwareSnapshot`: includes `CpuName`, `CpuVendor`, `CpuTemp` (`double.NaN` when unavailable), `PeakCpuTemp`, `MaxCoreTemp`, `CpuCoresTelemetry`, `Gpus`, `StorageDrives`, `RamUsedGb`, `RamTotalGb`, `RamUsagePercent`, `Memory` (`MemoryBreakdown?`), `PawnIoInstalled`.
- `CryoTray.Models.DriveTelemetry`: includes `PhysicalDriveNumber`, `SerialNumber`, `HardwareIdentifier`, `IsSolidState`, `DriveLetters`, `SmartHealthPercent`, `Temperature` (`double.NaN` when unavailable).
- `CryoTray.Models.TelemetrySample`, `TelemetrySample.FromSnapshot(HardwareSnapshot)`, & `TelemetrySample.TryGetDriveTemp(string, out double)`: unified 1 Hz sample builder carrying timestamps, CPU/GPU temps, CPU/GPU power watts, CPU/GPU load, clock GHz, RAM metrics, and per-drive `DriveTemps` keyed by `HardwareMonitorService.GetDriveExclusionKey`.
- `CryoTray.Models.AppSettings`, `SystemInfoData`.

## HardwareMonitorService & CSV Telemetry

- `IsPawnIoInstalled()`: checks `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO` without throwing.
- `InstallOrOpenPawnIo()`: runs bundled `PawnIO_setup.exe` when present or opens `https://pawnio.eu/`.
- `IsDiscreteGpu(HardwareType, string?)`: classifies NVIDIA, non-APU AMD, and Intel Arc GPUs as discrete.
- `SelectPrimaryGpu(IReadOnlyList<GpuTelemetry>, string?)`: resolves explicit `PreferredGpuIdentifier` or Auto (discrete GPU priority).
- `GetAvailableGpus()`: thread-safe snapshot of detected GPU options for `SettingsWindow`.
- `GetDriveExclusionKey(DriveTelemetry)`, `IsDriveExcluded(DriveTelemetry, IReadOnlyCollection<string>?)`: canonical drive exclusion key (`HardwareIdentifier` or normalized model) used across Settings, `MainWindow`, and `TemperatureAlertService`.
- `MemoryTelemetryService`: `TryReadBreakdown(out MemoryBreakdown)`, `FormatBytes(ulong)`, `ComputeCpuUsagePercent(...)`, `MeasureCpuUsageAsync(TimeSpan, CancellationToken)`: reads `GetPhysicallyInstalledSystemMemory`, `GlobalMemoryStatusEx`, x64 `NtQuerySystemInformation(80)` (`176`-byte `SYSTEM_MEMORY_LIST_INFORMATION`), and `GetSystemTimes` CPU utilization.
- `MemoryOptimizerService.Instance`: `OptimizeAsync(MemoryOptimizationTrigger, CancellationToken)`, `ExecuteKernelCommands(bool, bool, Func<int, (int, int)>?)`, `Start()`, `Stop()`, `Dispose()`, `IsOptimizing`, `LastResult`, `LastSuccessfulResult`: runs `RtlAdjustPrivilege(13)` + `NtSetSystemInformation(80)` (`2, 3, 4`), clipboard clear, tray toast notifications, and CPU-idle guarded auto-optimization.
- `TryReadMemoryMetrics(out double usedGb, out double totalGb, out double usagePercent)`: delegates to `MemoryTelemetryService.TryReadBreakdown`.
- `RefreshMemorySnapshotNow()` & `CloneSnapshot(HardwareSnapshot)`: clones `CurrentSnapshot` (preserving `CpuVendor` and all 8 primary `Gpu*` telemetry fields), refreshes RAM breakdown fields immediately, and notifies telemetry subscribers.
- `ReadHardware()`: one poll tick. Updates CPU, motherboard, per-GPU telemetry (`ReadGpuTelemetry`, `ResolveGpuVendor`), storage nodes (`CreateDriveTelemetry` + `ReadDriveMetadata`), and physical RAM, reopens LHM if PawnIO becomes installed, and sets `double.NaN` when sensors are absent.
- `ReadCpuRegistryInfo()`, `ResolveCpuVendor(string?, string?)`, `ResolveGpuVendor(HardwareType, string?)`: reads processor name, `VendorIdentifier`, and nominal MHz from the central processor registry key (plus WMI `Manufacturer`), mapping real CPU and GPU vendors cleanly without hardcoding fallback brands.
- `CleanCpuName(string?)`: removes `\b\d+-Core\s+Processor\b` suffixes across arbitrary core counts and trailing `Processor` labels.
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
- `TelemetryCsvService.ExportSamples(IReadOnlyList<TelemetrySample>, string)` / `StartRecording(string)` / `StopRecording()`: invariant-culture CSV snapshot export and live 1 Hz append logging.

## MainWindow popup, RAM & Tools CSV

- `CoreItems` (`ObservableCollection<CoreReadout>`): popup item source.
- `CoreColumns`: 4 when core count is 16 or less, 6 above 16.
- `UpdateCpuCoreItems(HardwareSnapshot)`: builds display text using `DisplayTemp`, applies Fahrenheit if selected, refreshes `CoreItems`.
- `PositionCorePopup()`: measures the bubble, right-aligns it over the hero digits, clamps to the active monitor, flips below when needed.
- `TryGetMonitorWorkArea(out Rect)`: monitor work area in DIPs via `Screen.FromHandle` and `VisualTreeHelper.GetDpi`.
- `CpuCoresPopup_Opened`: runs positioning after the popup lays out.
- `OnPropertyChanged`: `INotifyPropertyChanged` helper.
- `MainWindow.UpdateWindowHeight()`: adjusts height dynamically across `673` baseline, `PawnIoBanner`, `RamRow`, `GpuHealthRow`, `OverlayRow`, and `AutopilotRow`.
- `MainWindow.ExportRingBufferCsv()` / `ToggleLiveCsvLogging()`: exports recent ring-buffer telemetry or starts/stops 1 Hz CSV file recording from `Tools`.
- `MainWindow.BtnToolsMenu_Click`, `ToolsContextMenu_Opened`, `MenuToolsStress_Click`, `MenuToolsExportCsv_Click`, `MenuToolsRecordCsv_Click`, `MenuToolsAbout_Click`, `RenderWpfContextMenusForTest()`: drive declarative dark `ToolsContextMenu` (`RamDarkContextMenuStyle` + `DarkMenuSeparatorStyle`), sync RTL `FlowDirection` and recording label state, open dark `Settings > About`, and support pixel verification alongside `RamScopeMenu`.

## Chart hover

- `MainWindow.UpdateCpuChartTooltip(double)`: maps the cursor x to the nearest CPU sample, draws the guide and dot, and fills the CPU tooltip with temperature and time.
- `MainWindow.UpdateGpuChartTooltip(double)`: same mapping for the GPU chart, with the GPU value only.
- `MainWindow.ShowChartTooltip(Popup, Canvas, double, double, double)`: measures the card, clamps it to the chart width, and opens it above the sample.
- `MainWindow.DrawChartHoverMarker(Canvas, double, double, double, Color)`: clears and redraws the vertical guide and white dot.
- `MainWindow.HideChartTooltips()`: closes both cards and clears both hover layers.

## Charts and fan

- `MainWindow.CmbTimeframe_SelectionChanged(...)`: maps 1/5/10/30/60 min to 60/300/600/1800/3600 samples and redraws both charts. The initial XAML selection is guarded until all chart canvases exist.
- `MainWindow.DrawCpuChart()`, `DrawCpuChartCore(...)`, `DrawCpuSegment(...)`, and `DrawGpuChart()`: slice the ring buffer with `_timeframeSamples`; Large and Small CPU charts share two-color segment drawing (`DrawCpuSegment`) and inline gridlines (`DrawInlineGridLines`), plus `DrawDriveChart` + `UpdateDriveChartTooltip` for per-drive `SsdChartTooltip` charts.
- `MainWindow.FormatCpuClockReadout(double, double, double, bool)`, `FormatCpuSmallRowLabel(string?, string?)`, & `CompactGpuName(string)`: format live/base GHz clock text, detect real CPU vendor (`CPU (<Vendor>)`) via `HardwareSnapshot.CpuVendor` / `ResolveCpuVendor`, and strip vendor prefixes (`NVIDIA`, `AMD`, `Intel(R)`, `Intel`, `Qualcomm(R)`, `Qualcomm`) from compact GPU titles.
- `MainWindow.SetupFanAnimation()`: starts the six-petal rotor at 3.5 s per turn; `UpdateTheme` changes it to 0.9 s when cooling is active.
- `MainWindow.ApplyFanTheme(bool)`: sets the ON/OFF tile, rim, petal, and glow colors for the vector fan.
- `MainWindow.UpdateTheme(bool isCooling, bool animate)`: applies the fan theme, updates the toggle colors and dashboard accent, and redraws the CPU chart.
- `MainWindow.BtnCoolingToggle_Click(object, RoutedEventArgs)`: accessible `Button` handler for toggling Cooling Mode (`PowerPlanService.Instance.ToggleCoolingMode()`).
- `MainWindow.BtnCpuTempCores_Click` / `GotKeyboardFocus` / `LostKeyboardFocus`: opens or closes `CpuCoresPopup` on click or keyboard focus cues.

## Diagnostics & Verification

- `CpuSensorDump.Run()`: one-shot `/dump` mode. Opens a CPU-only `Computer`, writes every temperature and clock sensor with type, value, min, max, and identifier to `cpu-sensors.txt` in the project root, then exits. Must run elevated for real values.
- `VerificationTests.RunAll()`: `/test` mode (36 checks covering all prior suites plus custom frame colors & Wave 5 persistence, Black vs White tray badge digits + handle stability, CPU clock/base speed + Small/Large graph + drive sample lookup, zero-hardcoding hardware vendor/snapshot clone integrity, and `TestWpfContextMenusDarkRendering` / `AssertDarkWpfContextMenuBitmap` for dark WPF `ToolsContextMenu` / `RamScopeMenu` rendering in LTR/RTL).

## Power, Hotkeys, Autopilot & Settings

- `PowerPlanService.Instance`: `ToggleCoolingMode`, `SetCoolingMode`, `ApplyCpuCoolingParameters`, `EnsureCryoTrayCoolingScheme`, `ResetCryoTraySchemeToDefault`, `CleanupForUninstall()`, `TryDeleteSchemeViaPowerCfg(Guid)`.
- `StartupTaskService`: `TaskName` (`CryoTray`), `AutostartArg` (`/autostart`), `StartupTaskState`, `IsCurrentProcessElevated()`, `ResolveExecutablePath()`, `BuildTaskXml(string, string, string)`, `QueryState()`, `QueryStateForTask(string)`, `IsStartupConfigured()`, `SetStartup(bool)`, `TrySetStartup(bool, out string?)`, `TrySetStartupForTask(string, bool, out string?)`, `EnsureSynchronized(bool)`.
- `GlobalHotkeyService.Instance`: `CoolingHotkeyId` (`9001`), `RamOptimizeHotkeyId` (`9002`), `IsRegistered`, `IsRamHotkeyRegistered`, `Register`, `Unregister`, `RegisterRamHotkey`, `UnregisterRamHotkey`, `TryCaptureShortcut(Key, Key, ModifierKeys, out string, out string)`, `FormatShortcutDisplay(string?, string?)`.
- `SettingsService.Instance`: `Current`, `Save`, `Export`, `Import`, `Reset`, `SettingsChanged`.
- `AutopilotService.Instance`: `ProcessTelemetry(HardwareSnapshot)`, `EnumerateProcessesByPidToolhelp32()`, `IsFullscreen3DOrBorderlessActive(...)`, `ShouldHoldPause`, `IsPauseConditionActive`, `ExternalPauseHoldCheck`, `TransferTimedPauseToAutopilot()`.

## Overlay, Tray & UI helpers

- `NativeMethods`: includes `EmptyWorkingSet`, `CreateToolhelp32Snapshot`/`Process32FirstW`/`Process32NextW`, `SHQueryUserNotificationState`, `GlobalMemoryStatusEx`, `GetPhysicallyInstalledSystemMemory`, `GetSystemTimes`, `NtQuerySystemInformation`, `NtSetSystemInformation`, `RtlAdjustPrivilege`, `SYSTEM_MEMORY_LIST_INFORMATION`, `PowerDeleteScheme`, `SetForegroundWindow`, `GetForegroundWindow`, `GetWindowRect`, `GetWindowThreadProcessId`.
- `OverlayWindow.ApplyClickThrough(bool)`: toggles `WS_EX_TRANSPARENT` (`0x00000020`) and collapses `BtnCloseOverlay` while click-through is enabled.
- `SystemInfoQueryService.QuerySystemInfo()`: lazy WMI, EDID, boot, and BSOD read. Runs only when System Info opens; disposes each searcher, collection, and row.
- `SystemInfoQueryService.SelectGraphicsCardSummary(IEnumerable<string?>, string?)` / `IsVirtualDisplayAdapter(string)`: deduplicates `Win32_VideoController` GPUs, filters virtual/indirect display adapters when physical GPUs exist, puts the active primary GPU first, and falls back to live LHM GPU name or `Common_NotReported`.
- `SystemInfoQueryService.MatchDriveTelemetry(...)`: matches WMI drives against LHM `DriveTelemetry` by normalized serial number, `PhysicalDriveNumber`, and model + capacity without positional fallback.
- `TrayIconManager`: `UpdateBadges(gpuTemp, cpuTemp, coolingModeActive, force)`, `RenderSingleBadgeForTest(...)`, `RenderMergedBadgeForTest(...)`, `OnToggleOverlayClickThroughRequested`, `SetCoolingPauseUntil(DateTime?)`, `OnPauseCoolingRequested`, `OnCancelCoolingPauseRequested`, `ConfigureDarkDropDown(...)`, `CreateMenuItem(...)`, `CreateSeparator()`, `UpdateDropDownSizes(...)`, `RenderContextMenusForTest(bool, bool)`, owner-drawn `DarkMenuRenderer : ToolStripRenderer`.
- `MainWindow.DockSideBySide(Window)`: places companion windows beside the dashboard.
- `MainWindow.FormatSsdBadgeText(DriveTelemetry)`, `FormatSsdRowLabel(DriveTelemetry)`, `ParseDriveLetterTokens(string?)`: format SSD icon drive badge (`C`, `E`), `SSD (C:)` title with `FlowDirection.LeftToRight` RTL protection, and WCAG-AA SMART % color.
- `StressTestWindow.ComputeCoolingComparison(IReadOnlyList<TelemetrySample>, bool)`, `UpdateComparisonDisplay()`, `StressCoolingComparison`: summarize trailing 5-sample Cooling ON/OFF phases, render delta (`-°C` / `-W`), and draw two-color chart segments (`#E07D62` OFF vs `#2B88D8` ON).
- `MainWindow`: `UpdateMemoryBreakdownUi(HardwareSnapshot)`, `UpdateMemoryTooltipFooter()`, `SetRamOptimizingVisualState(bool)`, `FlashRamSuccessCheckmark()`, `CloseRamBreakdownToolTip()`, `ExerciseRamTooltipLifecycleForTest()`, `BtnOptimizeRam_Click`, `BtnOptimizeRamMenu_Click`, `MenuRamScope_Click`: drive the `2px` stacked RAM divider, dark scope context menu, live `Memory Breakdown` tooltip on `BtnRamBreakdown`, `FindToolTipOwner` / hover/pin test hook (verifying `null` outside `BtnRamBreakdown`), and checkmark feedback.
- `SettingsWindow.RefreshGpuOptionsSafely()` / `LoadGpuOptions()` / `RefreshPawnIoBanner()` / `PrimaryGpuChoice.ToString()` / `HandleHotkeyFocus`/`Blur`/`PreviewKeyDown` / `RefreshRamHotkeyFeedback()` / `UpdateTrayBadgeSampleTexts()` / `OnSettingsTelemetryUpdated(HardwareSnapshot)` / `PickFrameColor(...)` / `UpdateFrameColorSwatchesUi()` / `PopulateTimeframeComboItems(...)`: populates Primary GPU selector, View badge digit radios + live-updating preview badges (`--` when unavailable), Cooling ON/OFF frame color pickers, CPU display options + timeframe sync, per-drive visibility + graph/timeframe rows, shared Cooling + RAM shortcut recorders and conflict notices, Memory tab cleaning/auto rules, missing-PawnIO notice, and live startup task reconciliation + error revert in `ChkLaunchStartup_Changed`, `BtnImport_Click`, and `BtnReset_Click`.
- `SystemInfoQueryService.FormatUptime(TimeSpan)`: `d h m s` text.
- `SystemInfoQueryService.GenerateTextReport(SystemInfoData)`: save-to text report.
