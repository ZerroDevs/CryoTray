# CryoTray

> Ultra-lightweight native Windows System Tray hardware monitor & CPU cooling utility by **ZerroDevs**.

![Windows 10 / 11](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20WPF%20%2B%20WinForms-purple)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Features

- **Dual Dynamic System Tray Badges**: Crisp 32x32 rounded badges (GPU light lime `#A2D065`, CPU light peach `#F8AFA6` / sky cyan `#76C6EF`) updated every second with zero GDI handle leaks via Win32 `DestroyIcon`.
- **Hottest-Core Readout & Hover Speech Bubble**: Large thin hero temperature readout tracks the hottest core. Hovering over the temperature reveals a custom white speech bubble showing all CPU cores (`CPU00`..`CPU07`) with real-time temperatures and an underline highlight on all tied hottest cores.
- **Integrated Cooling Power Scheme**: Embeds `Resources\CryoTray.pow` and auto-imports as `"CryoTray Cooling"` via `powrprof.dll`. Exposes live `PERFBOOSTMODE` (Boost Disabled/Enabled) and `PROCTHROTTLEMAX` (50–100%) controls in Settings.
- **Hardware Telemetry**:
  - Live CPU clock frequency ratio (`3.66 / 3.80 GHZ`).
  - GPU inline live area chart and load monitoring.
  - Per-drive NVMe/SSD temperature and SMART health status.
- **Side-by-Side Window Docking**: Settings and System Information modals dock directly adjacent to the main dashboard above the taskbar.
- **Autopilot Intelligent Cooling**: Optional rule-based cooling switching during idle vs. heavy system load.
- **Translucent Floating Overlay**: Optional mini HUD pill displaying live telemetry over full-screen apps and games.

---

## Build & Run

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11 (x64)

### Build Release
```powershell
dotnet build -c Release
```

### Run Tests
```powershell
dotnet bin\Release\net8.0-windows\CryoTray.dll /test
```

### Run Application
Launch with administrator privileges (required for kernel-level sensor access):
```powershell
Start-Process .\bin\Release\net8.0-windows\CryoTray.exe
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
Developed by ZerroDevs.
