# Contributing to CryoTray

Thank you for your interest in contributing to **CryoTray**!

CryoTray is an ultra-lightweight, native Windows system tray hardware monitor and CPU cooling utility created by **ZeroNuxi** under **ZerroDevs**. We welcome bug reports, hardware compatibility improvements, localization enhancements, and clean, focused pull requests.

---

## Guiding Principles

When proposing or implementing changes, keep these core design values in mind:

1. **Ultra-Lightweight Footprint**: Idle memory usage should remain around ~20–25 MB. Never introduce bloated dependencies, runtimes, or unneeded background workers.
2. **Zero GDI Leaks**: System tray badges update on telemetry ticks (typically 1-2 seconds). Any dynamic Win32 icon handles (`HICON`) must be explicitly destroyed with `NativeMethods.DestroyIcon(handle)`.
3. **No Paywalls, No Telemetry**: CryoTray is completely free, open-source, and private. Never add telemetry, analytics, ad frameworks, or locked features.
4. **Dark, Pixel-Perfect UI**: Maintain the dark aesthetic, crisp vector artwork, smooth animations, and modeless docking behavior across all views.
5. **Full Bilingual Support**: Every user-facing string must exist in both English (`Strings.en.xaml`) and Arabic (`Strings.ar.xaml`) with correct right-to-left (RTL) handling.
6. **Opt-In System Controls**: GPU power and download-timer features default off. GPU vendor libraries load only from System32, GPU writes clamp to the VBIOS range, and the native default is restored on disable, exit, unhandled exceptions, and session ending. The timer keeps its 60-second warning and cancel path and persists auto-disarm.

---

## Development Environment Setup

### Prerequisites
- **Operating System**: Windows 10 (version 1809+) or Windows 11, x64 architecture.
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (version `8.0.300` or newer).
- **IDE / Editor**: Visual Studio 2022 (with *.NET desktop development* workload), Visual Studio Code (with *C# Dev Kit*), or JetBrains Rider.
- **Hardware Drivers**: [PawnIO 2.x](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) installed on the host machine for low-level CPU/motherboard sensor access.
- **GPU Driver**: A current NVIDIA or AMD display driver for the opt-in GPU power limit. Unsupported or locked VBIOS ranges leave the control disabled.

### Getting the Code
```powershell
# Fork and clone the repository
git clone https://github.com/<your-username>/CryoTray.git
cd CryoTray

# Create a focused feature or fix branch
git checkout -b feature/your-feature-name
```

---

## Building, Running, and Testing

### Build Release
```powershell
dotnet build -c Release
```
CryoTray targets `net8.0-windows` with both `UseWPF=true` and `UseWindowsForms=true`.

### Run Automated Verification Tests
CryoTray includes a built-in automated test suite that validates ring buffers, power plan integration, system queries, GDI leak prevention, WPF window instantiation, per-core sensor logic, storage cleanup guards, GPU power bounds and defaults, and download-timer warning/cancel safety:
```powershell
dotnet bin\Release\net8.0-windows\CryoTray.dll /test
```
*All verification tests must pass before submitting a pull request.*

### Run Hardware Sensor Dump (Diagnostics)
To inspect raw sensors detected on your machine:
```powershell
dotnet bin\Release\net8.0-windows\CryoTray.dll /dump
```

### Launch the Application
`app.manifest` requires Administrator privileges for kernel-level sensor access, and Release builds refuse to start from a user-writable folder such as the project output. Use the Debug build for IDE runs and launch via PowerShell:
```powershell
dotnet build -c Debug
Start-Process .\bin\Debug\net8.0-windows\CryoTray.exe
```
To run the Release configuration from the project folder instead, create the developer marker once from an elevated shell; delete it to restore the guard:
```powershell
New-Item -ItemType File -Force "$env:ProgramData\CryoTray\dev-mode" | Out-Null
```

---

## Architectural & Code Hygiene Guidelines

### 1. WPF and Windows Forms Interop
Because the project enables both `UseWPF` and `UseWindowsForms` (for `NotifyIcon`), `<ImplicitUsings>disable</ImplicitUsings>` is enforced in `CryoTray.csproj`.
- Always explicitly qualify or use aliases for ambiguous types such as `Color`, `Point`, `Brush`, `Application`, and `MessageBox` (e.g., `System.Drawing.Color` for GDI bitmaps vs. `System.Windows.Media.Color` for WPF elements).

### 2. GDI Object Management
Tray badges create a new GDI bitmap every second:
```csharp
IntPtr hIcon = bmp.GetHicon();
try
{
    using var tempIcon = Icon.FromHandle(hIcon);
    return (Icon)tempIcon.Clone();
}
finally
{
    NativeMethods.DestroyIcon(hIcon); // MANDATORY: prevents GDI handle exhaustion
}
```
Never remove or bypass `DestroyIcon`.

### 3. One-Second Telemetry Budget
Hardware polling runs on an adaptive 1/2/3-second interval from `TelemetryPollPolicy`, driven by the leases in `TelemetryDemandTracker`.
- Never perform blocking file I/O, heavy WMI queries, or network calls inside `PollingLoop`.
- Static system inventory (WMI) is queried once on startup or when explicitly opening the System Information window.

### 4. Modeless Utility Windows
`SettingsWindow`, `SystemInfoWindow`, `StressTestWindow`, and `StorageCleanupWindow` open as modeless, independently tracked windows:
- They must dock adjacent to `MainWindow` above the taskbar via `DockSideBySide()`.
- They must not lock the main dashboard or cause it to hide on deactivation.
- Storage Cleanup opens once as a 392 x 630 side panel, activates on reopen, and closes cleanly with Cancel or Escape.

### 5. Localization & RTL Rules
- Add new strings to `Resources/Strings.en.xaml` and `Resources/Strings.ar.xaml`.
- Use dynamic resource bindings (`Text="{DynamicResource StringKey}"`).
- In Arabic (RTL) mode: UI layouts mirror horizontally, but hardware model names, numbers, clocks, and charts must remain left-to-right (`FlowDirection="LeftToRight"`).

### 6. Opt-In System Controls
GPU power and download-timer features cross into hardware and OS power state. Keep both disabled by default, load GPU vendor libraries only from `%SystemRoot%\System32`, clamp every GPU write to the live VBIOS range, serialize driver calls, and restore the native default on disable, exit, unhandled exceptions, and session ending. Preserve the timer's 60-second warning and cancel path, persist auto-disarm, and fail closed when disk sampling is unavailable. Add tests for clamped presets, unsupported hardware, warning cancellation, and auto-disarm.

### 7. Commit Message Conventions
Follow [Conventional Commits](https://www.conventionalcommits.org/):
- `feat:` A new user-facing feature
- `fix:` A bug fix
- `docs:` Documentation updates
- `refactor:` Code reorganization without behavioral change
- `test:` Adding or refining verification tests
- `chore:` Maintenance, package updates, or build tooling

Example:
```text
feat(telemetry): add per-core CPU power readout support
fix(tray): prevent tooltip cutoff when merged badge is active
```

### 8. Versioning Scheme (`VERSION`)
CryoTray uses three-part semantic versioning rooted at `0.0.100`:
- **Current Baseline**: `0.0.104` (defined in the root `VERSION` file).
- **Subsequent Updates**: Increment sequentially (`0.0.104`, `0.0.105`, `0.0.106`, etc.).
- **Automatic Stamping**: `CryoTray.csproj` dynamically reads `VERSION` to stamp `AssemblyVersion`, `FileVersion`, and `InformationalVersion` on build.
- **Automated Releases**: The release workflow (`.github/workflows/release.yml`) monitors changes to `VERSION` on `main` and creates matching tags (e.g., `v0.0.100`, `v0.0.101`) and release artifacts with SHA-256 digests.

---

## Pull Request Checklist

Before opening a pull request, verify:

- [ ] Code compiles cleanly with zero warnings and zero errors (`dotnet build -c Release`).
- [ ] All automated tests pass (`dotnet bin\Release\net8.0-windows\CryoTray.dll /test`).
- [ ] Any new UI strings are localized in both `Strings.en.xaml` and `Strings.ar.xaml`.
- [ ] New system-level controls are opt-in, restore system state on exit, and have tests for unsupported hardware and cancellation.
- [ ] No GDI handle leaks are introduced (verify via test or Windows Task Manager GDI Objects column).
- [ ] If releasing an update, the patch version in `VERSION` is bumped sequentially (e.g., `0.0.100` -> `0.0.101`).
- [ ] Commit history is clean and follows Conventional Commits.
- [ ] `README.md` or documentation is updated if user-facing behavior changed.

---

## Licensing & Attribution

CryoTray is licensed under the [Apache License, Version 2.0](LICENSE). By contributing to CryoTray, you agree that your contributions will be licensed under Apache-2.0.

All redistributions and derivative works must preserve the original attribution notices in the [NOTICE](NOTICE) file.
