<p align="center">
  <img src="Images/Banner.jpg" alt="CryoTray - Hardware Monitor & Cooling Utility by ZeroNuxi - ZerroDevs" width="100%">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue" alt="Windows 10 / 11">
  <img src="https://img.shields.io/badge/.NET-10.0%20WPF-purple" alt=".NET 10.0 WPF">
  <img src="https://img.shields.io/badge/License-Apache--2.0-blue" alt="Apache-2.0">
</p>

<p align="center">
  <strong>A native Windows system tray hardware monitor and low-heat CPU power utility, created by ZeroNuxi under ZerroDevs.</strong>
</p>

CryoTray keeps CPU, GPU, and storage temperatures visible without leaving a large monitoring window open. It combines a compact tray badge, an on-demand dashboard, a floating telemetry overlay, eight CryoTray power profiles (including `Eco` and `Work`) plus the Windows Balanced default and optional pinned Windows plans. An opt-in GPU board-power limit and an opt-in download-completion timer add targeted heat and shutdown controls; both are off until enabled. Everything runs locally, with no account and no telemetry.

CryoTray does not control fans or voltages. Cooling Mode changes Windows processor power policy and is intended to reduce heat, not replace BIOS thermal protection.

## Contents

- [At a glance](#at-a-glance)
- [Feature tour](#feature-tour)
- [Cooling Mode and power profiles](#cooling-mode-and-power-profiles)
- [GPU power limit](#gpu-power-limit)
- [Autopilot](#autopilot)
- [Smart Download Timer](#smart-download-timer)
- [Tray behavior and alerts](#tray-behavior-and-alerts)
- [Floating overlay](#floating-overlay)
- [Utility windows](#utility-windows)
- [Settings reference](#settings-reference)
- [Install and run](#install-and-run)
- [Build from source](#build-from-source)
- [Testing and diagnostics](#testing-and-diagnostics)
- [Data, privacy, and updates](#data-privacy-and-updates)
- [Architecture](#architecture)
- [Project layout](#project-layout)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)
- [Security policy](#security-policy)
- [Contributing](#contributing)
- [License and attribution](#license-and-attribution)

## At a glance

| Item | Details |
| --- | --- |
| Platform | Windows 10 and Windows 11, x64 |
| Runtime | .NET 10 LTS with WPF |
| Privileges | Administrator, required by the application manifest for hardware sensor access and system-level power controls |
| Sensor source | LibreHardwareMonitorLib 0.9.6, with WMI used only for system inventory |
| Sensor driver | PawnIO 2.x for low-level CPU and motherboard access |
| Tray badges | GPU, CPU, or one combined split badge |
| Cooling method | Eight CryoTray Windows power profiles plus Windows Balanced and pinned custom Windows plans |
| Opt-in controls | GPU board-power limit and Smart Download Timer; both disabled by default |
| Overlay | Optional always-on-top telemetry panel with selectable metrics |
| Appearance | **Dark** (default), **Light**, or **System**; shared by every window and the tray menu |
| Localization | English and Arabic, including right-to-left layouts |
| Storage | One JSON settings file under `%APPDATA%\CryoTray` |
| Network use | GitHub Releases update checks and downloads only |
| License | Apache License 2.0 |

## Feature tour

### Dashboard

The dashboard is a compact, custom-drawn window that opens above the notification area by default. It can also be positioned freely. It is dark by default, with **Light** and **System** appearance options under **Settings > View**, and it is created only when needed rather than staying resident in the background. All companion windows and the tray menu follow the same appearance.

The CPU section includes:

- A large hottest-core temperature readout with the current cooling accent.
- A hover bubble listing every mapped core as `CPU00`, `CPU01`, and so on.
- An underline on every core tied for the hottest reading.
- Current CPU clock speed (with optional nominal base speed and CPU load toggles) and, when the processor exposes it, package power. Hovering the `GHz` badge beside `CPU SPEED` shows the active power plan's real clock ceiling (`PROCTHROTTLEMAX`, `PERFBOOSTMODE`, `PROCFREQMAX`, AC/DC source) and detected Boost limit (using SMBIOS Type 4, Intel `CPUID 0x16`, WMI, a two-sample spike filter, and a per-CPU cache with a **Clear** button under **Settings > CPU**).
- A live CPU chart with 1, 5, 10, 30, and 60 minute timeframes, switchable between the full-width **Large** graph and a compact inline **Small** `CPU (AMD)` / `CPU (Intel)` row (or hidden).
- An animated six-petal cooling fan and synced window frame / active titlebar icon / toggle accents that follow the active profile: ice blue for Cooling and Legacy, glacier cyan-teal (`#2699A8` frame/icon, `#1C7986` toggle track) for Eco, steel-slate for Work, emerald for Gaming (Low), amber for Gaming (Medium), copper for Gaming (High) and Windows Balanced, crimson for Gaming (Ultimate), and custom accents for pinned Windows plans. The CPU chart preserves each sample's plan color across profile switches.

The GPU section includes:

- Current GPU temperature and load, plus a **GPU** header hover tooltip showing full card model, VRAM used/total, core and memory clocks, and reported performance limit even while detail rows remain hidden.
- An inline GPU chart synchronized with the selected timeframe, plotting temperature, power, or load (selectable under **Settings > GPU**).
- GPU power, hotspot temperature, VRAM temperature, fan speed, and the GPU name when the hardware reports them.
- An optional details row with VRAM used/total, core and memory clocks, and any reported performance limit (off by default; enable **Show GPU details** under Settings > GPU).
- Per-readout visibility controls, so the row can stay compact.

The storage area lists detected storage drives with drive letters (`SSD (C:)` / `HDD (D:)`), badge letters inside the drive icon, inline SMART health percentages, and temperatures or optional per-drive live temperature graphs (`1` to `60` minute window). Hovering a drive shows its model, temperature, SMART health status, and mounted partition free space (`Free space: X / Y GB (%)`); individual drives can be hidden from **Settings > Disk**, or all storage rows can be toggled off via **Show SSD/HDD temperature**.

The RAM row shows live used and usable capacity, utilization percentage, a `2 px` real-time breakdown indicator (`In Use`, `Standby`, `Free`), an inline `Optimize RAM` split action button (with persisted working-set/modified and standby cleaning scopes), a dropdown that opens **Storage & Cache Cleanup...**, and a live-updating hover/click `Memory Breakdown` tooltip (`Installed`, `In Use`, `Available`, `Free`, `Standby`, `Modified`, `Cached`, `Hardware Reserved`, and session `Last freed`). The chevron menu uses the same row language as the profile selector: a muted `Cleanup scope` label, bare checkmarks, scope dots, compact titles with right-aligned descriptors, and a separated utility row.

The dashboard also exposes optional Overlay and Autopilot rows, a Cooling Mode switch with persistent elapsed profile duration across minimize-to-tray, an idle-zero active timer strip when `Keep Awake`, `Smart Download Timer`, or `Pause Cooling Mode` is armed (with a primary-timer Stop button), **Tools > Copy snapshot** (`Ctrl+C`, excluding hidden drives), and title-bar icons for Settings, System Information, and the CPU Temperature Test. The title icons use the cooling accent while their window is open and remain neutral when closed.

### Modeless utility windows

Settings, System Information, and CPU Temperature Test open as independent modeless windows. Opening one does not disable the dashboard, and the dashboard remains clickable. Clicking the same title icon again closes that utility. Storage & Cache Cleanup opens as a modeless companion from the RAM row, and all four can be open at the same time.

Keyboard focus remains visible with an accent border. Mouse interaction does not leave the white focus outline that previously appeared around title-bar buttons.

### Monitoring without constant overhead

CryoTray polls hardware once per second (backing off to 2–3 s when windows are closed) and keeps a 60 minute ring buffer of CPU and GPU samples. Charts read from that buffer, so changing the timeframe does not trigger extra hardware reads. Background timers for Autopilot and Automatic Standby Cleanup stay stopped whenever their features are disabled, and idle power-plan checks skip full scheme re-enumeration unless the active Windows plan actually changed. The dashboard and utility windows are created on demand, clear transient chart/row collections on close, and trigger a debounced off-UI memory trim (`MemoryTrimService`) when idle.

The application uses software rendering for predictable text and chart output across systems, including machines with limited or problematic GPU acceleration. Tray badge bitmaps and GDI resources are reused, and native icon handles are destroyed when replaced.

### Temperature alerts

CryoTray can raise a tray notification when the CPU, GPU, or a storage drive crosses its configured threshold. Each component has its own threshold:

- CPU: default 85 °C.
- GPU: default 80 °C.
- Storage: default 60 °C.

An alert fires once when a sensor crosses its threshold. The same sensor will not alert again for 10 minutes, and it must fall at least 3 °C below the threshold before it can re-arm. Notifications can be muted by category or disabled entirely, and Settings includes a test notification button.

A separate GPU hotspot-to-edge watch warns when the gap between the hotspot and edge temperatures stays at or above a threshold (default 25 °C, adjustable from 15 °C to 40 °C under **Settings > General**). It must persist continuously for 5 minutes before alerting; a dip below the threshold restarts the window, and after an alert it waits 30 minutes. A sustained large gap can mean dried thermal paste or a mounting problem.

Fan Health Watch is a separate optional notification. A fan header is armed only after spinning continuously above 300 RPM for one minute. If an armed fan then reports 0 RPM for 30 seconds while the CPU or GPU is under sustained load and at or above 60 °C, CryoTray shows a tray warning. Headers that never reported motion are ignored, state is cleared across sleep and resume with a 60 second grace period, and each fan has a 10 minute alert cooldown. The guard is deliberate: idle fan curves that stop fans at 0 RPM never arm the alert. Fan Health Watch is on by default and can be disabled under Settings > General.

System Information includes a live **Fan health** section under **Fan speeds**. It is collapsed by default; click the **Fan health** heading or its `>` chevron to open it. It lists every motherboard header and the primary GPU fan with current RPM, state (`Watching`, `Armed`, `Idle at 0 RPM`, or `Stopped`), arming or alert countdown, and the last alert time. **Hide idle fans** is on by default, hiding `Idle at 0 RPM` rows so active fans are not buried; `Stopped` rows always remain visible, and unchecking the filter shows every header. **Test fan notification** verifies the tray path. Clicking a fan alert opens System Information, expands the panel, and highlights that fan. Fan aliases set under **Settings > General > Fan names** appear in this panel, in fan alerts, and in copied diagnostics.

System Information also has a collapsed **Top processes** section. It lists the six processes with the highest CPU or GPU load, with CPU %, GPU %, and memory. The panel samples every 2 seconds only while it is open, and it notes when GPU process counters are unavailable.

Storage health has its own optional watch. CryoTray notifies when a drive enters `Worn` or `Replace`, or when its SMART health falls by 10 points. Excluded drives are ignored, and each drive has a 10 minute cooldown. The toggle is under **Settings > Disk**.

All temperature displays, thresholds, charts, and notifications honor the selected Celsius or Fahrenheit unit.

### Updates

Settings > About checks the latest GitHub release and can download and install a newer build in place. Automatic checks are enabled by default: every 5 minutes while the dashboard is open, every 30 minutes in the tray, and immediately after startup, resume, or a network reconnect. Updates require the release's SHA-256 checksum, the manifest version, and every per-file hash to match; Authenticode is checked when present but a signature is not required. About shows the release notes, last checked time and result, download speed, ETA, a Cancel button, and the downloaded-update cache size with a **Clear downloads** action. Startup creates and secures the update and rollback roots before any rollback metadata is read. Before installing, CryoTray saves a hash-verified rollback point under `%ProgramData%\CryoTray` and verifies it before launch; if the new build does not work, About can restore the previous version or delete that rollback copy to reclaim its disk space.

**Release channel** selects Stable (default) or Preview. Stable uses the latest non-prerelease release. Preview reads the latest 20 releases, skips drafts and invalid versions, and picks the highest base version; stable wins when both channels offer the same base. Each channel keeps its own cache and ETag, and switching channels clears the other channel's cached response. Preview builds must bump the numeric patch and use a tag such as `v0.0.104-preview.1`; the existing version comparison then sees `0.0.104-preview.1` as newer than `0.0.103`. A preview build that reuses an already released base version will not be offered.

**Verify rollback readiness** is a read-only drill. It checks the saved rollback manifest, every file hash, the protected ACLs, reparse safety, the app location, and that `CryoTray.exe` and `CryoTray.dll` are present. About reports the file count, size, and localized result, and the check is written to Recent activity. It never writes to the app directory; the existing restore action remains the only path that changes installed files.

Settings > About also includes **Recent activity**, a local timeline of app starts and exits, profile and cooling changes, alerts, update checks and installs, cleanup, RAM optimization, and Game Mode transitions. **Save support bundle...** writes a ZIP with a plain-text summary, the existing diagnostics text, the System Information report, the activity JSONL plus readable text, allowlisted settings (including preferred profiles, tray badge color sync, and display/sleep timeout overrides), and the tail of the updater log. The settings projection omits file paths, hotkeys, fan aliases, the Autopilot app list, pinned scheme definitions, and window positions.

If you are upgrading from `v0.0.100`, use the setup manually once. That release's updater prefers ZIP assets and can fail before the new installer runs. `v0.0.101` and later correctly choose the setup for installed copies.

## Cooling Mode and power profiles

Cooling Mode activates one of CryoTray's eight deterministic Windows power profiles, the built-in Windows Balanced plan, or any user-pinned Windows power scheme. Cooling Mode on uses the preferred cooling profile; Cooling Mode off uses the preferred off profile, which defaults to Windows Balanced. The active plan is synchronized every two seconds, so changes made through Windows' own power settings are reflected in the UI.

<p align="center">
  <img src="Images/Cooling_ON.png" alt="Cooling Mode on" width="260">
  <img src="Images/Cooling_OFF.png" alt="Cooling Mode off" width="260">
</p>

<p align="center"><em>Cooling ON in ice blue (or cyan-teal for Eco), OFF in copper, with slate, emerald, amber, and crimson tiers for Work and Gaming.</em></p>

| Profile | Badge | When to use it |
| --- | --- | --- |
| `CryoTray — Cooling` | Default | Maximum thermal reduction and quiet operation for browsing, downloads, and light desktop work |
| `CryoTray Cooling - Legacy` | Classic | The original CryoTray cooling tuning, with 0% AC EPP and every core unparked |
| `CryoTray — Eco` | Silent | Deep thermal and battery saver with passive AC/DC cooling, sub-base processor limits (`70%` AC / `55%` DC), boosted EPP (`95`/`100`), and aggressive core parking |
| `Windows Balanced` | Windows Default | Standard Windows plan, used by default when Cooling Mode is off |
| `CryoTray — Work` | Productivity | Steady desktop and productivity balance keeping `Efficient Aggressive` boost with moderate core parking and PCIe/USB/NVMe power saving |
| `CryoTray — Gaming (Low)` | Cool Gaming | Esports, indie games, or thin laptops where heat matters more than peak boost |
| `CryoTray — Gaming (Medium)` | Balanced | AAA games that need smooth frame times without unnecessary heat |
| `CryoTray — Gaming (High)` | Full Boost | Demanding games that need full CPU boost and no PCIe, USB, or NVMe power saving |
| `CryoTray — Gaming (Ultimate)` | Zero Latency | Competitive play and benchmarks, with 100% minimum processor state |

Under **Settings > CPU > Manage profiles & Windows plans**, unused built-in profiles can be hidden (with a one-click **Show all** reset; `Cooling` and `Windows Balanced` remain visible), and existing Windows schemes can be pinned as **Cooling Off** (`Windows Plan`) or **Cooling On** (`Windows Cooling`) with a custom display name and accent color. CryoTray never modifies or deletes built-in or user-pinned Windows plans, and automatically prunes and falls back cleanly if a pinned scheme is removed externally in Windows.

The default cooling profile disables boost (`PERFBOOSTMODE` 0) and caps the processor at 99% (`PROCTHROTTLEMAX`). The Legacy profile keeps the original tuning and still honors the Boost Mode and Maximum Processor State controls in Settings > CPU. Windows Balanced is the default off profile; pick `Work` or a CryoTray gaming profile if Cooling Mode off should use tuned values.

The active-plan label under the dashboard Cooling Mode switch is a compact dropdown. It groups the visible **Cooling** and **Cooling Off** choices in one menu and shows checkmarks for both preferred profiles from Settings. Selecting a profile saves it and applies it immediately when that mode is active; selecting from the inactive group updates the future preference. Changes made in Settings appear in the dropdown, and changes made in the dropdown appear in Settings.

At most one CryoTray plan is installed at a time. Switching duplicates the Windows Balanced scheme into the target profile's fixed GUID (`c7a00001`–`c7a00008`), writes its AC and DC values, activates it, then deletes the other CryoTray plans. Choosing Windows Balanced or a pinned Windows scheme activates it directly and deletes every CryoTray plan. Built-in Windows plans and third-party plans are never modified or deleted. The original active plan is captured before the first switch and restored when power management is disabled on exit or CryoTray is uninstalled. An old single `CryoTray Cooling` plan from an earlier version is migrated to the Legacy profile.

Cooling Mode is available from:

- The dashboard switch.
- The tray context menu.
- The CPU Temperature Test window.
- A configurable global hotkey supporting `Ctrl`, `Alt`, `Shift`, and `Win` plus `A-Z`, `0-9`, or `F1-F12`, disabled by default.
- The `Enable Cooling Mode on app launch` setting.

Because the application runs elevated, it can create and activate power schemes without an additional prompt after launch.

## GPU power limit

The GPU power limit is an opt-in control in **Settings > GPU**. It is off by default. Enabling it reads the board power range exposed by the VBIOS, applies the native default, and then enables:

- **Cool (70%)**: sets the board power limit to 70%, clamped up to the VBIOS minimum when that minimum is higher.
- **Balanced (85%)**: sets the board power limit to 85%, using the same VBIOS clamp.
- **Full / Default (100%)**: restores the native VBIOS default, capped at 100%.
- **Fine power limit**: a slider bounded by the VBIOS minimum and 100%.
- **Reset**: restores the native VBIOS default without turning the control off.

The chosen value is written through the installed NVIDIA or AMD driver. Every vendor call is serialized on a background thread, every write is read back, and the native default is restored when the control is switched off, when CryoTray exits normally, on an unhandled exception, and when Windows ends the session. A saved preset is re-applied at startup only when **Re-apply last GPU preset on CryoTray startup** is enabled; otherwise the native default is applied once.

Support depends on the GPU, driver, and VBIOS. Firmware that reports a locked or unusable range shows **Power limit control not supported by this GPU's VBIOS** and leaves the switch disabled. The control changes board power limits only. It does not change fan curves, voltages, or BIOS settings.

## Autopilot

Game Mode, still titled Autopilot in the navigation, monitors running processes every 2.5 seconds using a single Win32 `CreateToolhelp32Snapshot` scan and can optionally watch for active fullscreen 3D or borderless games via `SHQueryUserNotificationState`. Each whitelisted app maps to a profile. The first matching foreground app wins, then any running whitelisted app, then fullscreen Game Mode. CryoTray captures the profile that was active before the rule, switches once, and restores it when the rule clears.

Important behavior:

- Game Mode only switches away from a cooling profile that is already active. It does not turn Cooling Mode on by itself.
- Entries are stored by executable name, so a selected game can be matched regardless of its install path.
- Each Settings row lets you pick any visible Cooling or Cooling-Off profile (such as `Eco`, `Cooling`, a `Cooling On` pinned plan, `Work`, or a Gaming tier); entries without a per-app override fall back to the preferred off profile. Fullscreen auto-detection continues to use the preferred off profile.
- Fullscreen auto-switch can be enabled from Settings > Autopilot independently of or alongside the whitelist.
- Foreground-only matching is available under Settings > Autopilot. When enabled, a whitelisted app applies only while it owns the foreground window, so a background launcher cannot hold a profile after the game closes.
- Manual profile changes made while a rule is active are respected until that rule changes.
- If a timed Cooling Mode pause is still active, Game Mode does not resume cooling when the app closes.
- Running applications can be picked from a list, or a portable executable can be added with the file browser.
- Removing an entry will not affect any process that is already running; the next poll updates the state.

## Smart Download Timer

The Smart Download Timer is an opt-in control in **Settings > Autopilot** and the tray menu. It is disarmed by default.

When armed, CryoTray samples received traffic from non-loopback, non-tunnel network adapters once per second. It can wait for an active download before starting, then requires a continuous quiet window below the selected threshold. The defaults are 5 minutes below 200 KB/s. The optional disk guard holds the timer while total disk writes are at or above 2 MB/s, so unpacking or installation work is not interrupted; if the disk counter cannot be read, the guard fails closed.

After the quiet window, a topmost warning counts down for 60 seconds and offers **Cancel**. Returning download or disk activity cancels the warning. When the countdown ends, CryoTray performs the selected action:

- **Sleep** (default) through `SetSuspendState`.
- **Hibernate** through `SetSuspendState` when Windows hibernation is enabled.
- **Shut Down** through the System32 `shutdown.exe` with fixed `/s /t 0` arguments.

The timer disarms itself after firing or a manual cancel, and persists the disarmed state, so waking the PC cannot immediately put it back to sleep. Closing the warning window, including with Alt+F4, also cancels the action. Session ending stops the timer.

## Tray behavior and alerts

The tray icon is the primary entry point:

- Left-click toggles the dashboard.
- Re-launching `CryoTray.exe` while it is already running signals the tray instance to open and focus the dashboard.
- Right-click opens the context menu.
- The menu provides Open Dashboard, Toggle Cooling Mode, **Power Profile** (grouped Cooling and Cooling-Off profiles with 8 px color dots), **Pause Cooling Mode** (`15 minutes`, `30 minutes`, `1 hour`, and `Resume Cooling Now`), CPU Temperature Test, Click-Through Overlay, Settings, and Exit.
- Tooltips show CPU and GPU temperatures, plus the current cooling state or active pause countdown.

Badge options in Settings > View:

- Show the GPU badge in green.
- Show the CPU badge in coral when Cooling Mode is off and cyan when it is on.
- Choose temperature, load, or RAM percentage for the CPU and GPU badges independently. Temperature remains the default.
- Combine CPU and GPU temperatures into one split badge.
- Hide either individual badge.

Temperature alerts use the system tray balloon. Clicking a temperature alert opens the dashboard; an update notification opens Settings > About. Alerts are informational and do not change cooling settings.

The startup task is created through Windows Task Scheduler as `CryoTray` using Task 1.2 XML (`InteractiveToken`, `HighestAvailable`, 5 second logon delay, battery logon enabled, zero timeout limit, `/autostart` deduplication, and automatic executable path repair). Settings checks Task Scheduler status directly so any failure reverts cleanly instead of leaving the toggle out of sync. It is removed when `Launch app on PC startup` is disabled.

## Floating overlay

The floating overlay is a small always-on-top telemetry panel for use alongside games, benchmarks, or other full-window applications. Enable **Show Overlay row** under Settings > Overlay, then use that row to show or hide the panel. The overlay remembers its position between sessions.

Selectable metrics:

- CPU temperature, load, power, and clock speed.
- GPU temperature, load, and power.
- Optional RAM usage (used GB and percentage).
- Optional battery percentage and charging state on devices with a battery. Desktop systems hide both the metric and its setting.

The overlay groups CPU, GPU, RAM, and battery metrics separately and hides empty groups automatically. If every metric is disabled, it shows `No metrics selected` instead of an empty panel. Opacity is adjustable from 0% to 100%; at 0% the background becomes fully transparent while the text remains visible.

When **Click-through overlay** is enabled (`WS_EX_TRANSPARENT`), mouse clicks pass directly to games and windows underneath and the close button is hidden. You can disable click-through from Settings > Overlay or the system tray context menu. Otherwise, the overlay can be dragged by its surface and closed with its close button. Exclusive fullscreen applications may cover desktop overlays; borderless windowed mode generally works better.

## Utility windows

### CPU Temperature Test

The CPU Temperature Test is a controlled load test:

- Starts worker loops with selectable load modes (**All Cores (100%)**, **Gaming (50%)**, or **1-Core Boost**).
- Optionally runs 1-click **Auto A/B (60s)** mode (30 seconds on Cooling OFF followed by 30 seconds on Cooling ON), stopping automatically at 60 seconds and restoring the prior power profile.
- Shows live CPU temperature, peak temperature, clock speed, package wattage, active power profile alongside Cooling Mode, and elapsed time.
- Charts the last 60 temperature samples (copper when Cooling Mode is off, ice blue when on) with `°C`/`°F` baseline scale labels and empty-state prompt, and records full run telemetry for **Copy** (`Ctrl+C`) summary export and **Export CSV** (`TelemetryCsvService`).
- Automatically stops test workers if CPU temperature reaches the `95 °C` (`203 °F`) thermal safety cutoff.
- Compares stabilized **Cooling OFF vs Cooling ON** temperature and CPU wattage in real time to display exact `-°C · -W` reductions.
- Counts stress-loop iterations per second in each phase and shows `Performance retained: 93%` next to the temperature and wattage delta.
- Projects that percentage onto a selectable `Game FPS:` 60/100/120/144/165/240 reference (100 by default) using the preset dropdown under the estimate (`If 144 FPS: ~121 FPS (23 lost)`); the choice is saved, and the line updates immediately. A tooltip notes that actual loss may be less or more depending on the game, GPU limits, thread usage, and frame pacing, and that the estimate assumes a CPU-bound game.
- Allows Cooling Mode to be toggled while the test is running.
- Stops all workers when the test is stopped or the window closes.

This is a system stress test. Do not run it on a machine with inadequate cooling, and do not treat it as a substitute for hardware diagnostics. The window is modeless, so the dashboard remains usable while it runs.

### System Information

System Information opens immediately using live telemetry and session-cached inventory while deeper WMI, GPU diagnostics, display, network, and event-log details load asynchronously with clear inline loading states (keeping `Copy` and `Save to...` disabled only until the scan completes), and refreshes uptime every second. It reports:

- Computer name.
- Processor model and core count.
- Installed memory.
- Graphics card.
- Storage devices, capacity, health, and free space.
- Operating system.
- Windows uptime.
- Startup speed from the Windows event log.
- CPU temperature minimum, average, and peak from the current telemetry history.
- Read-only motherboard fan speeds (CPU, chassis, and other headers the sensor driver reports). Headers reading 0 RPM are hidden, and `--` appears when no fan is moving.
- A live Fan health section with per-fan state, last alert time, the primary GPU fan, a test notification button, and an optional **Hide idle fans** filter that always keeps stopped fans visible.
- A collapsed Top processes section with the six processes driving CPU or GPU load, their CPU %, GPU %, and memory. It refreshes every 2 seconds only while open.
- Connected monitors and network interfaces.
- System errors, including the BSOD count available from the event log.

`Save to...` writes a plain-text system report. **Copy** copies a shorter diagnostics snapshot containing the app version, CPU, GPU, temperatures, RAM, fan states, and drive health. Copied diagnostics omit serial numbers, PC names, and file paths. Missing values from unsupported hardware or unavailable WMI providers are shown as unknown or not reported instead of failing the window.

### Storage & Cache Cleanup

Open **Storage & Cache Cleanup...** from the dropdown beside **Optimize RAM**. It opens as a 392 x 660 modeless companion beside the dashboard with a slim dark scrollbar. The panel scans five categories asynchronously, then shows selected bytes and files found, `Selected X of Y` with a selection-aware color-matched category distribution bar, system-drive free space with a usage bar, reclaimable size/file count beside each category, and collapsible **Locations** breakdowns showing each scanned folder:

- **User & System Temporary Files** (checked by default): leftovers under `%TEMP%` and `%WINDIR%\Temp`, paired with a default-on **Skip temp files newer than 24 hours** guard so active installers and recent files are preserved. The Temp roots themselves are never removed.
- **Web Browser Cache** (opt-in): cache directories for Chrome, Edge, Brave, Opera/Opera GX, and Firefox only. Cookies, passwords, logins, history, bookmarks, sessions, local storage, and IndexedDB are never touched. If a supported browser is running, active files are skipped safely.
- **GPU & DirectX Shader Cache** (opt-in): compiled DirectX, NVIDIA, AMD, and Intel shader caches. Caches rebuild on first launch and may cause brief stutter, so this is mainly useful after a GPU driver update introduces stutter.
- **Crash Dumps & Error Reports** (opt-in): removes `%LocalAppData%\CrashDumps`, `%WINDIR%\Minidump`, and `%ProgramData%\Microsoft\Windows\WER\ReportArchive` / `ReportQueue`.
- **Windows Prefetch** (advanced, opt-in): only `.pf` and `.db` files. Windows rebuilds this data, so cold starts are temporarily slower after clearing it.

Reparse points are skipped, every path is checked against its category root, locked files are counted separately, and the freed size is counted only after a successful delete. The service refuses drive roots, Windows and System32, user profiles, LocalAppData, AppData, ProgramData, Program Files, and other broad system folders, so a redirected TEMP path cannot turn cleanup into a system-wide delete. Checkbox choices (including the 24-hour temp guard) persist in the settings file. The primary action shows the selected bytes (`Clean 3.84 GB`), stays disabled with no selection, while scanning, and before the first scan completes, and asks for confirmation only when browser cache, GPU shaders, crash dumps, or Prefetch are selected. Scan and cleanup progress are shown in the panel; Cancel stops active work without closing it, closing during work asks first, and scan failures offer Rescan. After cleanup, the result includes freed bytes by category.

### Settings

Settings opens immediately as a modeless companion window while startup task checks, update rollback/download cache size scans, and collapsed Power Profile enumeration complete off the UI thread. It includes nine tabs: General, View, Overlay, CPU, Memory, Disk, GPU, Autopilot, and About. The footer can export all settings to JSON, import them back, or reset them to defaults. Changes apply immediately and are saved to disk.

## Settings reference

### General

| Setting | Effect |
| --- | --- |
| Language | Switches all windows between English and Arabic, including right-to-left layout |
| Temperature units | Celsius or Fahrenheit for readouts, charts, thresholds, and alerts |
| Mute temperature alerts | Suppresses CPU, GPU, and drive notifications |
| Disable all notifications | Stops every tray notification, including temperature, fan, storage, update, and memory alerts |
| Warn when a running fan stops | Enables Fan Health Watch for armed fans that stop under sustained load (enabled by default) |
| Warn when the GPU hotspot gap is too large | Enables the sustained hotspot-to-edge watch and sets its threshold (25 °C default, 15–40 °C) |
| Fan names | Renames motherboard and GPU fan headers for the panel, fan alerts, and diagnostics; leave a field blank to use the sensor name |
| Send test notification | Verifies that tray notifications are visible |
| Show computer name | Adds the PC name to the dashboard title area |
| Launch app on PC startup | Creates or removes the `CryoTray` Task Scheduler entry |
| Launch app in background mode | Starts without opening the dashboard |
| Enable Cooling Mode on app launch | Activates the preferred cooling profile at startup |
| Auto-enable Cooling Mode on battery | Switches to the preferred cooling profile when AC power disconnects and to the preferred off profile when plugged back in |
| Disable Cooling Mode on closing the app | Prompts when Cooling Mode is on, then restores the original Windows plan on exit; enabled by default |
| Pin window to taskbar | Controls whether the dashboard appears in the taskbar |
| Always on top | Keeps the dashboard above other windows |
| Hide on focus loss | Hides the dashboard when it loses focus, unless Always on top is enabled |
| Use a key combo | Enables the custom cooling hotkey supporting `Ctrl`, `Alt`, `Shift`, `Win` plus `A-Z`, `0-9`, or `F1-F12` with conflict detection |
| Automatic update checks | Checks GitHub Releases at startup, every 5 minutes with the dashboard open, and every 30 minutes in the tray |

### View

| Setting | Effect |
| --- | --- |
| Theme | **Dark** (default), **Light**, or **System**; System follows the Windows app theme and updates live |
| Show GPU temperature in tray | Shows the green GPU tray badge with **Black digits** (default) or **White digits** and live preview badges |
| Show CPU temperature in tray | Shows the coral/cyan CPU tray badge with **Black digits** (default), **White digits**, live preview badges, and opt-in **Sync badge color with active power profile** |
| Combine tray icons | Replaces separate badges with one split CPU/GPU badge honoring both digit palettes |
| Window Frame Colors | Customizes the border/accent frame colors for `"Cooling ON"` mode (`#2B88D8` default) and `"Cooling OFF"` mode (`#C26A47` default) with reset buttons |
| Anchor dashboard to tray | Positions the dashboard above the notification area |
| Show RAM usage row | Displays zero-WMI physical memory usage (`GlobalMemoryStatusEx`) on the dashboard |
| Show Autopilot row | Adds the Autopilot control row to the dashboard |
| CPU / GPU badge value | Shows temperature, load, or RAM percentage in each tray badge (temperature by default) |
| Show motherboard fan speeds | Adds the read-only fan RPM row and Fan health section to System Information (enabled by default) |

### Overlay

| Setting | Effect |
| --- | --- |
| Show Overlay row | Adds the overlay control row to the dashboard |
| Click-through overlay | Sets `WS_EX_TRANSPARENT` so mouse clicks pass through the floating overlay |
| Overlay readouts | Selects CPU, GPU, optional RAM, and battery metrics for the floating panel |
| Show battery percentage | Adds charge level and charging state to the overlay and tray tooltip; hidden on systems without a battery |
| Overlay opacity | Sets the overlay background transparency from 0% to 100% |

### CPU

| Setting | Effect |
| --- | --- |
| Show CPU power usage | Shows or hides package power in the dashboard CPU section |
| Show CPU clock speed | Shows or hides the dashboard CPU clock speed row (with **Show base speed**, **Show CPU load**, and **Remembered CPU boost peak** + **Clear** button) |
| Watch for CPU clock below nominal | Opt-in, record-only watch logging busy intervals below nominal clock in the session report |
| Show CPU temperature graph | Shows or hides the dashboard CPU chart, selects timeframe (`1/5/10/30/60 min`), and chooses **Large** (default) or **Small** inline layout |
| Preferred cooling profile | Selects `CryoTray — Cooling` (default), `CryoTray Cooling - Legacy`, `CryoTray — Eco`, or a `Cooling On` pinned Windows plan for Cooling Mode on |
| Preferred profile when Cooling is off | Selects Windows Balanced (default), `CryoTray — Work`, Gaming Low/Medium/High/Ultimate, or a `Cooling Off` pinned Windows plan |
| Manage profiles & Windows plans | Hides unused built-in profiles (with **Show all** reset) and pins existing Windows schemes (`Cooling Off` / `Cooling On`, custom name & accent color) |
| Sleep & display timeouts | Optional AC/DC `Turn off display` and `Put computer to sleep` overrides separately for `Cooling On` and `Cooling Off` plans (`Follow Windows` default, contextual **Reset**, battery column auto-hidden on desktops) |
| Processor Performance Boost Mode | Tunes boost for the Legacy cooling profile |
| Maximum Processor State | Tunes the Legacy cooling profile limit, from 50% to 100% |
| High temperature alert threshold | Sets the CPU threshold from 60 °C to 100 °C |

### Memory

| Setting | Effect |
| --- | --- |
| Cleaning options | Enables or disables trimming working sets and flushing modified pages (`MemoryEmptyWorkingSets` + `MemoryFlushModifiedList`), purging standby cache (`MemoryPurgeStandbyList`), clearing the clipboard, and showing freed-memory tray notifications |
| Enable Auto RAM Optimization | Automatically cleans memory when available RAM drops below the configured threshold (10% to 50%, 5-minute cooldown) or on a periodic schedule (1h to 6h) |
| CPU idle guard | Checks real CPU utilization via `GetSystemTimes` over 1 second before running auto-optimization so cleanup never interrupts active games or heavy work (`< 10%` usage required) |
| Global RAM shortcut | Configures a custom hotkey (default `Ctrl + Alt + M`) via `RegisterHotKey` to optimize memory immediately from anywhere |

### Disk

The Disk tab includes a master **Show SSD/HDD temperature** toggle, an optional **Warn when drive health drops** watch, a storage alert threshold (40 °C to 85 °C), and per-drive checkboxes (`ExcludedDrives`) to hide them or enable per-drive **Show SSD graph** / **Show HDD graph** inline charts with individual `1` to `60` minute timeframes (`DiskGraphDrives`, `DiskGraphTimeframes`). Sensors that are missing or blocked by an uninstalled PawnIO driver display `--` or an unavailable notice instead of synthetic fallback values, and drives without a valid temperature sensor are skipped for alerts.

### GPU

The GPU tab chooses the Primary GPU (defaulting to Auto, which prioritizes discrete graphics) and sets the graphics alert threshold, from 50 °C to 95 °C. It also contains the dashboard readout toggles for GPU power, temperature, name, hotspot, VRAM, and fan visibility; the **Show GPU details** toggle with its VRAM, clock, and performance-limit hint; and the **GPU graph metric** selector for temperature, power, or load.

The same tab hosts the opt-in **GPU Power Limit Control** card. It is off by default. When enabled, it applies the native VBIOS default and exposes Cool, Balanced, Full / Default, Reset, and a fine slider clamped to the VBIOS range. The optional **Re-apply last GPU preset on CryoTray startup** checkbox is off by default. Unsupported or locked firmware leaves the card disabled with the exact VBIOS message.

### Autopilot

Enable Autopilot, toggle automatic pause during fullscreen 3D games and apps, choose a running application, or browse to an executable. The optional **Pause only while a whitelisted app is in the foreground** checkbox restricts the whitelist match to the current foreground process, so background launchers do not pause Cooling Mode on their own. The whitelist and both pause preferences are saved with the rest of the settings. Entries can be removed individually.

The tab also hosts the opt-in **Smart Download-Completion Timer**. It is disarmed by default. Choose Sleep, Hibernate, or Shut Down, a download speed threshold, a quiet confirmation duration, the disk guard, and whether to wait for an active download. The same toggle is available in the tray menu. A 60-second warning with Cancel always precedes the selected power action.

### About

The About tab shows the installed version, update status and release notes, last checked time and result, automatic update and save-rollback options, manual update button, download progress with speed/ETA and Cancel, the downloaded-update cache size with a **Clear downloads** action, and a rollback button plus a compact delete-backup action when a backup exists. The rollback status includes the backup date and disk size. **Save rollback on update** is enabled by default; turning it off installs future updates without creating a new rollback copy. Existing backups are kept until deleted or replaced.

## Install and run

### Prebuilt release

1. Open [GitHub Releases](https://github.com/ZerroDevs/CryoTray/releases) and choose a package:
   - `CryoTray-Setup-<version>.exe`: **Offline Full installer** (self-contained .NET 10, bundled PawnIO setup, clean power-plan uninstall cleanup, and silent post-install NTFS folder compression; recommended when you want zero extra downloads).
   - `CryoTray-Lite-Setup-<version>.exe`: **Lite installer** (~7.5 MB; bundles `LibreHardwareMonitorLib` and PawnIO setup offline, and automatically installs the Microsoft .NET 10 Desktop Runtime only if missing).
   - `CryoTray-<version>-win-x64.zip` (or `CryoTray-Lite-<version>-win-x64.zip`): **Portable zip archive**.
2. Download the matching `.sha256` file.
3. Verify the file before installing or extracting:

   ```powershell
   Get-FileHash .\CryoTray-Setup-<version>.exe -Algorithm SHA256
   ```

   The hash must match the value in the checksum file.
4. For the portable zip, extract the archive to an administrator-protected folder (for example, under `C:\Program Files`). Portable copies run elevated and refuse to start from Downloads, the Desktop, a user profile, or a user-writable data volume, because standard users could replace the executable before launch. If you extracted it under an elevated account, set the folder owner before launching:

   ```powershell
   icacls "C:\Program Files\CryoTrayPortable" /setowner *S-1-5-32-544 /T /C /Q
   ```

   It includes `PawnIO_setup.exe`; run it if PawnIO 2.x is not already installed, then run `CryoTray.exe` (approving UAC). Both installers always use the default Program Files location, set the protected owner themselves, run silent PawnIO setup automatically when missing, and keep in-app updates matched to your installed edition (`Full` vs `Lite`).

The installer does not reuse a custom folder; it always installs to the protected Program Files location. If a previous Inno installation is registered in the uninstall key and its folder contains both `CryoTray.exe` and `unins000.exe`, it removes that old folder after the new install succeeds, and only when the old path is not the new folder, an ancestor or child of it, or an exact Windows or System folder. Portable copies and unregistered custom copies are not detected, so remove those manually.

The Full package (`CryoTray-Setup` / Full zip) is self-contained for win-x64 so no separate .NET runtime installation is required; the Lite package (`CryoTray-Lite-Setup` / Lite zip) uses the .NET 10 Desktop Runtime (`Microsoft.WindowsDesktop.App 10.0`), which the Lite installer fetches silently if needed.

PawnIO is required for CPU temperature and other low-level sensors. Without it, CryoTray still starts, but unsupported sensors may show no data or an unavailable value.

### Requirements

- Windows 10 or Windows 11, x64.
- Administrator privileges, required by the application manifest.
- PawnIO 2.x for full sensor coverage.
- Internet access only for update checks and downloads.

### First run

The dashboard opens unless `Launch app in background mode` is enabled. Hiding the dashboard leaves the tray icon running. Use Exit from the tray menu to stop the application and release its hardware handles and power-plan monitoring.

## Build from source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Windows 10 or Windows 11, x64.
- Visual Studio is optional; the repository builds with the .NET CLI.
- PawnIO 2.x for running the sensor-dependent parts of the runtime tests.

### Build

```powershell
dotnet restore CryoTray.csproj
dotnet build CryoTray.csproj -c Release
```

The executable is written to:

```text
bin\Release\net10.0-windows\CryoTray.exe
```

For local UI work, build the Debug configuration and start it from an elevated terminal, or launch it normally and approve the UAC prompt:

```powershell
dotnet build CryoTray.csproj -c Debug
Start-Process .\bin\Debug\net10.0-windows\CryoTray.exe
```

Debug builds skip the installed-copy location guard so the app can run from the project folder. Release builds keep that guard unless the machine has a developer marker, which an administrator creates once:

```powershell
New-Item -ItemType Directory -Force "$env:ProgramData\CryoTray" | Out-Null
New-Item -ItemType File -Force "$env:ProgramData\CryoTray\dev-mode" | Out-Null
```

The marker lives in the administrator-protected CryoTray data folder, so a standard user cannot create it. It relaxes only the startup location guard; update installation still refuses a user-writable app folder. Delete `%ProgramData%\CryoTray\dev-mode` to restore the guard. Without the marker, run a Release build only from an administrator-protected location (the installer, or a copied `publish\win-x64` folder). The `/test`, `/test-updates`, `/dump`, and `/uninstall-cleanup` command-line modes still run directly from any build output.

### Local publish

The release workflow produces a self-contained win-x64 folder, an Inno Setup installer, and a portable zip (`build-installer.ps1`):

```powershell
dotnet publish CryoTray.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -o publish\win-x64
```

Copy the whole `publish\win-x64` folder, or run `.\build-installer.ps1` (`-Variant All|Full|Lite`) to generate both `Installer\Output\CryoTray-Setup-<version>.exe` / `CryoTray-Lite-Setup-<version>.exe` and portable archives (`release\CryoTray-*.zip`). Each zip includes `PawnIO_setup.exe` at its root. Stop any running CryoTray instance first so the Release output is not locked.

### Versioning and releases

The version is stored in the `VERSION` file as three numeric parts. The project reads that value into the assembly and file version. Pushing a change to `VERSION` on `main`, or running the Release workflow manually, triggers `.github/workflows/release.yml`. The workflow:

1. Builds the Release binary and runs the `/test` verification suite.
2. Publishes the self-contained `publish\win-x64` payload and signs `CryoTray.exe` when repository signing secrets are configured, then compiles `Installer\CryoTray.iss` into `CryoTray-Setup-<version>.exe` and signs the setup afterward.
3. Creates checksummed release assets (`CryoTray-Setup-<version>.exe`, `CryoTray-<version>-win-x64.zip`, `CryoTray-Update-<version>-win-x64.zip`, and their `.sha256` files). The update archive contains versioned per-file SHA-256 hashes and a cumulative delete list derived from the previous release's owned-file history.
4. Publishes a GitHub release unless that tag already exists.

To publish the current `VERSION` without editing it again, trigger the workflow manually from **Actions > Release > Run workflow**, or with the GitHub CLI:

```powershell
gh workflow run Release --ref main
gh run list --workflow release.yml --limit 1
```

Open the returned run URL to follow the build. A successful stable run creates the tag `v<version>`. Rerunning the same version after that tag exists is intentionally skipped, so bump `VERSION` before publishing another release. To publish a preview, dispatch the workflow with channel `preview` and a preview number; it creates `v<version>-preview.<number>` and marks the GitHub release as a prerelease. The base version must still be newer than the installed stable build, so bump the numeric patch. Signing is applied automatically when the repository signing secrets are configured; otherwise the assets are published unsigned with SHA-256 checksums.

## Testing and diagnostics

### Runtime verification suite

The executable includes an integration test harness:

```powershell
dotnet .\bin\Release\net10.0-windows\CryoTray.dll /test
```

Run it elevated for full sensor coverage, or run `dotnet .\bin\Release\net10.0-windows\CryoTray.dll /test-unit` for non-destructive checks. The runtime checks cover ring buffer stats, GPU detail formatting, GPU overview tooltip, sustained hotspot-to-edge alert and cooldown, live process CPU/GPU sampling, the eight-profile whitelist engine (`Eco`, `Work`, visibility hiding, and `Cooling Off`/`Cooling On` pinned Windows plans), per-mode sleep and display timeout overrides, Windows Balanced cleanup, active-plan preservation during Settings saves, power-plan lifecycle and uninstall cleanup, persistent profile uptime across minimize-to-tray, active timer strip and Stop tooltips, SMBIOS Type 4 / CPUID / two-sample boost peak auto-detection and `GHz` plan-speed tooltip formatting, cached boost-peak persistence and clearing, SSD mounted free-space tooltips, dashboard `Ctrl+C` snapshot copy (with hidden/excluded drive filtering) and `22 px` layout/PCB checks, opt-in tray badge color syncing, GPU power clamping and VBIOS-default handling, WMI/EDID system queries, GDI icon handle cleanup, English and Arabic RTL localization, theme persistence and normalization, WPF window instantiation across companion views, per-core temperature mapping and honest `NaN` fallback safety, alert cooldown and drive exclusion filtering, fan health arming, GPU fan and idle-stop guards, storage health transitions and cooldowns, storage cleanup 24h age guard, Crash Dumps targets, and broad-root guards, adaptive telemetry demand and storage policy, chart decimation, activity cap and sanitization, support bundle entries and redaction, Settings search ranking/disclosures and localization, Game Mode target resolution across Cooling and Cooling-Off profiles, update channel selection, rollback readiness, tray badge metric resolution, foreground-only Autopilot matching, multi-GPU selection, PawnIO detection, overlay `WS_EX_TRANSPARENT` click-through, custom hotkey capture/registration, `Toolhelp32` process scan and fullscreen state, `EventWaitHandle` activation signaling, `GlobalMemoryStatusEx` + NT page-list RAM breakdown, kernel memory optimization and privilege fallback, `TelemetryCsvService` export, `StartupTaskService` XML & elevated Task Scheduler sync, timed Cooling Mode pause/resume, stress-test Auto A/B, load modes, thermal cutoff, summary formatting, and comparisons, dark owner-drawn tray context menu rendering, zero-hardcoding hardware verification, download-timer safety, and dark WPF context menus without white icon gutters.

This is not an isolated unit test suite. It reads real hardware, creates or modifies CryoTray power profiles, resets stored parameters, and may require multiple CPU cores. Use it on a test machine or accept that it touches those system settings. The GPU power checks validate bounds, defaults, and unsupported copy without writing a real board-power limit, and the download-timer checks exercise the pure engine and sampler guards without performing a shutdown.

### CPU sensor dump

For diagnosing missing or incorrect CPU readings:

```powershell
dotnet .\bin\Release\net10.0-windows\CryoTray.dll /dump
```

Run it elevated. It opens a CPU-only LibreHardwareMonitor session and writes every temperature and clock sensor, including type, value, minimum, maximum, and identifier, to `cpu-sensors.txt`.

### Build verification used by this repository

```powershell
dotnet build CryoTray.csproj -c Release -o build-check -p:UseSharedCompilation=false
```

This builds the application into a separate output folder, which is useful when the normal Release executable is running.

## Data, privacy, and updates

CryoTray does not include analytics, advertising, accounts, or remote telemetry. Hardware readings stay on the machine. Local network-interface byte counters used by the Smart Download Timer also stay on the machine.

Local files:

- `%APPDATA%\CryoTray\settings.json`: all application settings, including window positions and the Autopilot whitelist.
- `%APPDATA%\CryoTray\activity.jsonl`: the bounded local activity timeline, capped at the latest 50 events.
- `%APPDATA%\CryoTray\crash.log`: unhandled exception details, when one is captured.

Update checks contact the GitHub Releases API:

```text
https://api.github.com/repos/ZerroDevs/CryoTray/releases/latest
```

Automatic checks run immediately when the app opens, every 5 minutes while the dashboard is visible, every 30 minutes in the tray, and after resume or a network reconnect. When a newer release is found, the dashboard shows a translucent strip with the version, **Update**, **About**, **Later**, and **Skip**. Later hides it for 24 hours; Skip keeps that version hidden until a newer release appears. Update opens Settings > About and starts the verified install; About opens the tab without installing. About shows the GitHub release notes and the last check result.

A downloaded update is verified against the release's SHA-256 checksum. Authenticode is checked when present but is not required. Downloads retry transient failures and resume with HTTP Range and a `.part` file in a stable locked work directory; a 512 MiB cap and free-space check prevent runaway downloads. Installed copies run `CryoTray-Setup-<version>.exe` (or `CryoTray-Lite-Setup-<version>.exe` for Lite installs) silently and wait for it before restarting. Portable copies use the `CryoTray-Update-<version>-win-x64.zip` archive, whose manifest carries the version, every file SHA-256, and a cumulative stale-file delete list. The updater and rollback files live in `%ProgramData%\CryoTray` with an Administrators owner, a protected Administrators/SYSTEM ACL, recursive reparse rejection, and untrusted-content clearing; the elevated helper validates the manifest, rejects reparse destinations, restores the verified rollback copy if a write fails, and only then records the one-time applied marker. Downloaded update files are retained for seven days by default and can be cleared immediately from About. The app itself must run from an administrator-protected folder; startup rejects user-writable locations and secures the application folder before continuing. The rollback copy has a SHA-256 file manifest that is verified before restore. After an actual update, the new version shows a green **CryoTray was successfully updated from vX to vY** strip with an X; it auto-hides after 20 seconds and is never shown for a fresh install. If About was left on a persisted **Available** result, it offers **Check again** until the release info is fetched again.

No setting, sensor log, machine name, or usage data is sent to the update service. Only the standard HTTP request needed to fetch the latest release is made.

## Architecture

CryoTray is a single WPF process with a lightweight tray shell. Hardware polling runs in the background once per second, while UI work stays on the WPF dispatcher. System inventory queries are deferred until System Information is opened.

```text
CryoTray.exe
├── App.xaml.cs
│   ├── single-instance mutex and EventWaitHandle activation
│   ├── tray manager and dashboard lifecycle
│   ├── global hotkey registration and battery power rule
│   ├── GPU power restore and download-timer warning/cleanup
│   ├── temperature alert routing
│   └── automatic update scheduling and /uninstall-cleanup
├── HardwareMonitorService
│   ├── LibreHardwareMonitor CPU, GPU, and storage polling
│   ├── kernel RAM telemetry delegation
│   ├── CPU core temperature mapping
│   └── 3,600-sample telemetry ring buffer
├── MemoryTelemetryService & MemoryOptimizerService
│   ├── GetPhysicallyInstalledSystemMemory, GlobalMemoryStatusEx, and NtQuerySystemInformation(80)
│   ├── background NtSetSystemInformation(80) cleanup with SeProfileSingleProcessPrivilege
│   └── GetSystemTimes CPU-idle guarded auto-optimization and threshold cooldowns
├── TelemetryCsvService
│   └── ring-buffer export and continuous 1 Hz CSV recording
├── PowerPlanService
│   ├── eight deterministic profile GUIDs with activate-first cleanup
│   ├── Windows Balanced & read-only pinned external scheme activation
│   ├── Balanced duplicate, AC/DC writes, and active scheme sync
│   └── Legacy PERFBOOSTMODE / PROCTHROTTLEMAX updates
├── GpuPowerControlService
│   ├── System32-only NVAPI, ADLX, and ADL backends
│   ├── serialized VBIOS-clamped writes and native-default restore
│   └── opt-in startup re-apply
├── AutopilotService
│   ├── CreateToolhelp32Snapshot process whitelist polling every 2.5 seconds
│   └── SHQueryUserNotificationState fullscreen 3D auto-pause
├── DownloadTimerService
│   ├── pure one-second DownloadTimerEngine
│   ├── network and PDH disk sampling
│   └── topmost 60-second warning and fixed power action
├── TemperatureAlertService
│   ├── per-component thresholds
│   └── 10 minute cooldown and 3 °C re-arm hysteresis
├── FanHealthService & StorageHealthAlertService
│   └── fan-stop and SMART health watchdogs
├── BatteryTelemetryService
│   └── PowerStatus battery percentage and charging state
├── ThemeService
│   └── Dark / Light / System palette and Windows theme tracking
├── SystemInfoQueryService
│   └── on-demand WMI, display, network, and event-log reads
├── TrayIconManager
│   └── cached badge rendering and native icon cleanup
└── Views
    ├── MainWindow
    ├── SettingsWindow
    ├── SystemInfoWindow
    ├── StressTestWindow
    ├── OverlayWindow
    └── ConfirmExitWindow
```

Key implementation choices:

- **Software rendering.** WPF renders in software to keep text and charts consistent across systems.
- **On-demand windows & idle trimming.** Companion windows are constructed per open, cancel in-flight background work cleanly on close, release chart/list buffers when closed, and coalesce compacting GC + working-set trimming off the UI thread via `MemoryTrimService`.
- **Non-blocking utilities.** `System Information` renders live telemetry immediately while WMI/EventLog queries run in parallel and reuse cached hardware inventory; `Settings` runs startup task checks, backup/cache size scans, and collapsed external-plan enumeration asynchronously without blocking open.
- **Adaptive telemetry.** Window and service leases choose 1 s, 2 s, or 3 s polling; CPU and GPU always update, while fans and storage are gated and cached. The ring buffer holds up to 3,600 telemetry samples, enough for the 60 minute chart range at 1 s.
- **Bounded chart work.** Charts reduce to bucket min/max within a point budget near twice the plot width, keep original indices for hover, and coalesce telemetry redraws on a 200 ms timer.
- **Resource ownership.** GDI objects and HICON handles are retained only as long as needed and destroyed when a badge is replaced or disposed.
- **Localization resources.** English and Arabic strings live in XAML resource dictionaries, and the application reapplies localization when the language changes.
- **Theme resources.** Shared brushes in `App.xaml` are swapped at runtime by `ThemeService`; dark is the default, light is a full warm-neutral palette, and System follows the Windows app theme.
- **Power-plan fallback.** The embedded `.pow` file is imported through `powercfg`; existing schemes are reused by name.
- **Opt-in system controls.** GPU power writes and the download timer stay disabled until enabled. GPU writes are serialized, clamped to the VBIOS range, and restored on exit; the timer shows a 60-second cancelable warning and disarms after firing or cancellation.
- **Single instance.** A named mutex prevents duplicate tray instances.

## Project layout

```text
CryoTray.csproj              .NET 10 WPF/WinForms project and package metadata
App.xaml / App.xaml.cs       application lifecycle and orchestration
VERSION                      release version, consumed by MSBuild
LICENSE                      Apache License 2.0
NOTICE                       attribution and redistribution notice
app.manifest                 administrator privilege and DPI settings
Models/                      settings, telemetry snapshots, and system info models
Services/                    hardware, power, settings, updates, alerts, localization, GPU power, timer
Services/GpuPower/           NVIDIA NVAPI, AMD ADLX, and AMD ADL power-limit backends
Tray/                        tray icons, badge rendering, and context menu
Views/                       dashboard, settings, overlay, stress test, system info, cleanup, timer warning
Images/                      banner, logo, application icon, and state artwork
Resources/                   XAML strings, theme resources, legacy power scheme
Tests/                       runtime verification and CPU sensor diagnostics
.github/workflows/           Windows release packaging and GitHub release publication
```

## Limitations

- **Windows only.** The implementation depends on WPF, Windows Forms, WMI, Task Scheduler, and Windows power-management APIs.
- **Administrator access is required.** Hardware sensor access and power-plan changes need elevation.
- **Sensor coverage varies by hardware.** Some processors, GPUs, and drives expose only a subset of the values shown in the UI. Missing cells and unavailable states are expected on unsupported sensors.
- **No fan or voltage control.** Cooling Mode is a Windows processor power policy. It does not issue fan commands, undervolt the CPU, or alter BIOS settings.
- **GPU power control depends on the GPU and VBIOS.** It needs a supported NVIDIA or AMD desktop GPU with an unlocked VBIOS power range. Unsupported or locked firmware leaves the control disabled. It does not change fan curves, voltages, or BIOS settings.
- **The download timer performs real power actions.** Sleep, Hibernate, and Shut Down execute after a visible 60-second warning. The timer disarms after firing or cancellation, but it should only be armed when an automatic power action is intended.
- **Per-core temperatures may not be available.** When a CPU exposes only a package or CCD sensor, CryoTray distributes the available reading across the reported cores for display. The hottest reading remains the authoritative sensor value.
- **Overlay limitations.** Exclusive fullscreen applications can cover any topmost desktop overlay. The overlay is not designed to be a game cheat overlay or to inject into another process.
- **Power settings have system-wide effects.** Enabling Cooling Mode changes the active Windows power profile. Review the preferred off profile before relying on it for battery life or performance.
- **Stress testing is real load.** The CPU Temperature Test spins work on every logical processor. Use it responsibly and monitor temperatures.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| CPU temperature is missing or zero | Confirm PawnIO 2.x is installed and rebooted, approve UAC, and run `/dump` from an elevated terminal to see which sensors are exposed |
| Tray badges are missing | Enable the desired badge in Settings > View; check the Windows notification-area overflow menu |
| Tray temperatures do not update | Make sure the dashboard or tray shell is running and that a supported sensor reports a positive temperature |
| Dashboard disappears when clicking elsewhere | Disable `Hide on focus loss`, or enable `Always on top` |
| Hotkey does nothing | Enable the hotkey, record a shortcut in Settings > General, and watch for any conflict warning shown below the box |
| Cooling Mode does not change | Run the app elevated and confirm the selected CryoTray profile appears in Windows power settings; the app recreates it if it was removed |
| Autopilot does not pause cooling | Confirm Cooling Mode is on, Autopilot is enabled, and the executable name is on the whitelist |
| Overlay is invisible in a game | Use borderless windowed mode; exclusive fullscreen can cover desktop overlays |
| Overlay is empty | Select at least one CPU or GPU metric in Settings > Overlay |
| Update check fails | Verify internet access to GitHub, then try again; releases must include a valid version and downloadable asset |
| Update download is rejected | Check that the `.sha256` asset is present and matches the downloaded archive |
| Arabic layout looks wrong | Switch back to English and then Arabic to force a full localization refresh, and report the affected window |

Crash information is appended to `%APPDATA%\CryoTray\crash.log`. Include the log and an exported system report when reporting a reproducible problem.

## Security policy

CryoTray takes security seriously. If you find a security vulnerability, please report it privately through GitHub Private Vulnerability Reporting or by emailing the maintainer directly. See our [Security Policy](SECURITY.md) for full details, SLA, and supported versions.

## Contributing

Contributions, bug reports, and hardware compatibility findings are welcome. Please read our [Contributing Guide](CONTRIBUTING.md) for architectural guidelines, the adaptive telemetry budget, GDI resource hygiene, RTL localization, and testing requirements before submitting a pull request. Useful reports include:

- Windows version and build.
- CPU, GPU, and storage models.
- Whether PawnIO is installed.
- Steps to reproduce the issue.
- The relevant application log or an exported System Information report.

Please keep changes consistent with the existing theme resources (Dark default plus Light and System), the one-second telemetry budget, English/Arabic resource dictionaries, and the existing WPF/WinForms stack.

## License and attribution

CryoTray is licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for attribution and redistribution requirements.

Copyright 2026 ZeroNuxi - ZerroDevs.

The application was created by ZeroNuxi under the ZerroDevs organization. Redistributions and derivative works must retain the attribution notice in [NOTICE](NOTICE).
