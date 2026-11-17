# Security Policy

## Supported Versions

CryoTray actively maintains security patches for the following versions:

| Version | Supported | Notes |
| :--- | :---: | :--- |
| `>= 0.0.100` / `main` | :white_check_mark: | Actively supported (baseline `0.0.100`; incrementing `0.0.101`, `0.0.102`, etc.) |
| `< 0.0.100` | :x: | Unsupported / unreleased builds |

We strongly recommend always running the latest official release available from the [CryoTray Releases](https://github.com/ZerroDevs/CryoTray/releases) page.

---

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.**

If you discover a security vulnerability or potential exploit in CryoTray, report it privately through one of the following channels:

1. **GitHub Private Vulnerability Reporting (Preferred)**:
   - Navigate to the [CryoTray Security Advisories](https://github.com/ZerroDevs/CryoTray/security/advisories/new) page.
   - Click **Report a vulnerability** and submit the advisory form.
2. **Confidential Email**:
   - Send an email to the project maintainer: **ZeroNuxi** at `usamau32o@gmail.com`.
   - Please include "[CryoTray Security]" in the subject line.

### What to Include in Your Report

To help us investigate and resolve the issue quickly, please provide:
- A clear description of the vulnerability and its potential impact.
- Step-by-step instructions or proof-of-concept (PoC) demonstrating how to reproduce the issue.
- Operating system version and architecture (e.g., Windows 11 x64, build 26100).
- Any specific configuration or permissions needed to trigger the vulnerability.

### Response SLA & Disclosure Process

- **Acknowledgment**: We aim to acknowledge receipt of your vulnerability report within **48 hours**.
- **Investigation**: We will validate and investigate the issue within **5 business days**, keeping you informed of findings.
- **Coordinated Disclosure**: Once a fix is verified, a patched release will be published alongside an advisory. We follow responsible disclosure principles and will credit you for your discovery (unless you prefer to remain anonymous).

---

## Threat Model & Security Boundaries

CryoTray runs locally on Windows and interacts directly with low-level system services. Understanding our security boundaries helps identify what constitutes a vulnerability:

### 1. Elevated Privileges (`requireAdministrator`)
CryoTray requires elevation in its application manifest (`app.manifest`) to access kernel-level CPU/GPU hardware sensors via `LibreHardwareMonitorLib` and the `PawnIO` driver.
- **In Scope**: Privilege escalation attacks, DLL hijacking/planting in the application directory, arbitrary code execution, or unsanitized input passing into system APIs.
- **Out of Scope**: Attacks that require an attacker to already possess interactive Administrator or SYSTEM privileges on the machine.

### 2. Windows Power Scheme Management
CryoTray creates and applies the dedicated `"CryoTray Cooling"` power scheme using direct Win32 API calls (`PowerWriteACValueIndex`, `PowerWriteDCValueIndex`, and `PowerSetActiveScheme` in `powrprof.dll`).
- **In Scope**: Injections or corruption allowing unauthorized modification of system-wide security settings or arbitrary power schemes.
- **Out of Scope**: Standard CPU throttling behavior intended by Windows power management policies.

### 3. Update Verification & Authenticode
CryoTray includes automated update checking and downloading via GitHub Releases (`UpdateService.cs`).
- **Security Controls**:
  - Downloads are strictly retrieved over TLS from `https://api.github.com` and `https://github.com`.
  - Cryptographic SHA-256 hash digests are verified against the corresponding release `.sha256` asset before any update archive is extracted.
  - Digital signatures are validated through `AuthenticodeVerifier.cs` (`WinVerifyTrust` via Win32 `wintrust.dll`).
  - Automatic backup and rollback mechanisms are maintained by `UpdateBackupService.cs`.
- **In Scope**: Checksum validation bypasses, trust verification flaws, or path traversal during update extraction.

### 4. Privacy & Data Handling
- CryoTray runs entirely offline and contains **zero telemetry, analytics, or tracking**.
- The only outbound network requests occur during explicit or daily background update checks against GitHub Releases.
- User settings are stored locally in plaintext JSON at `%APPDATA%\CryoTray\settings.json`. No credentials, personal data, or encryption keys are handled or stored.

### 5. Third-Party Hardware Drivers
CryoTray relies on `LibreHardwareMonitorLib` and the `PawnIO` kernel driver for hardware sensing.
- Vulnerabilities originating strictly within upstream drivers should be reported to the respective maintainers ([LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)). If an issue in our usage pattern enables exploitation, please report it to us directly.
