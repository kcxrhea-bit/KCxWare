# KCxWare

KCxWare is a lightweight native Windows 11 WPF control panel for switching one PC between Gaming, Programming, and Normal Everyday modes. Its black, cyan, and magenta interface uses the supplied `assets/1.png` background and the exact `assets/KCxWare.ico` application branding.

## Safety model

The WPF interface runs without administrator rights. After a clear confirmation, it starts `KCxWare.Helper.exe` through Windows UAC and applies the selected mode in the current session. The helper records a transaction under `C:\ProgramData\KCxWare`, verifies the result, and refreshes the UI from authoritative state. Gaming and Programming are temporary session modes; a new Windows boot restores the recorded baseline and returns KCxWare to Normal. Legacy `KCxWare Apply Armed Mode` invocations are normalized safely and never reapply an armed Gaming or Programming mode.

Gaming suppression is allowlisted and session-oriented: service startup types are never disabled. Defender, Firewall, RPC, networking, Wi-Fi, audio, NVIDIA display and recording components, Chrome, Logitech input, Gaming Services, GameInput, Easy Anti-Cheat, and BattlEye are explicitly protected. Unknown and missing services or processes, including unlisted Chrome native hosts, are not modified. KCxWare never changes Fortnite files, tampers with anti-cheat, overclocks hardware, or applies undocumented registry tweaks.

## Modes

- **Gaming Mode** applies immediately in the current session with no fixed startup-settle delay. Before mutation, KCxWare performs a read-only capability and power-plan preflight, then safely shuts down WSL only when it is available and stops only running allowlisted services/processes. Services must reach a confirmed `STOPPED` state before their remaining processes are terminated. A 35-second continuously clean verification window remains required for success; this covers WSearch's observed delayed restart action, and a late restart triggers another cleanup attempt within the existing three-attempt limit. KCxWare selects the first installed Gaming candidate (the existing KCx/Ryzen plan, then Windows High Performance) and preserves the active plan if neither exists. NVIDIA, AMD, and Intel graphics are supported without vendor-specific driver mutation. Chrome, graphics drivers, NVIDIA App/GeForce Experience Overlay, recording/Highlights, security, networking, audio, input, Gaming Services, and anti-cheat remain protected. OpenRGB and RustDesk remain required cleanup targets. Windows-managed PhoneExperienceHost, CrossDeviceService, and CrossDeviceResume are logged best-effort targets; Windows DCOM-owned WidgetService is excluded from failure verification. A residual `wslservice` host process is benign only when WSLService, vmcompute, and `vmmemWSL` are stopped.
- **Programming Mode** restores services that KCxWare recorded as running before Gaming, detects common installed development capabilities (Git/Git Bash, Node/npm, WSL, Docker, Ollama/local-AI, .NET, Visual Studio, and VS Code), and chooses the first supported installed performance/development power plan. Detection is informational and does not launch IDEs, terminals, containers, Node processes, or AI models.
- **Normal Mode** restores only services captured as running before Gaming Mode and restores the captured pre-Gaming power plan when it is still installed, otherwise preferring AMD Ryzen Balanced (`9897998c-92de-4669-853f-b7cd3ecb2790`) then Windows Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`). KCxWare intentionally does not relaunch arbitrary applications it closed because it cannot safely reconstruct their command lines or session state.

Optional software, services, tools, GPU vendors, and supported power-plan candidates may be absent; absence is a normal supported configuration. Capability results are ephemeral and logged for the transition rather than persisted as a hardware/software inventory. A transition fails before mutation when KCxWare cannot capture the active power plan needed for a safely reversible baseline.

The in-app **Offline Guide** documents setup, defaults, permissions, risks, failures, recovery, examples, and related status features without requiring a network connection.

## Build and test

Requirements: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore .\KCxWare.sln
dotnet build .\KCxWare.sln -c Release
dotnet test .\KCxWare.sln -c Release --no-build
```

Create the Release x64 folder:

```powershell
.\scripts\Publish.ps1
```

Framework-dependent x64 publishing was selected because this target PC already has the .NET 8 Windows Desktop runtime and the result is substantially smaller than a self-contained bundle. The current machine's NuGet configuration exposes only Visual Studio offline packages, so the self-contained Windows runtime packs are unavailable without changing package-source policy. Build output and `publish/` are intentionally ignored by Git.

## Install and uninstall

From an elevated PowerShell window after publishing:

```powershell
.\installer\Install-KCxWare.ps1
```

This installs under `%ProgramFiles%\KCxWare` and creates all-users Desktop and Start Menu shortcuts whose icon comes from the embedded `KCxWare.ico`. Uninstall with `installer\Uninstall-KCxWare.ps1`. The uninstaller removes the one-shot task, application files, and shortcuts, but intentionally preserves `C:\ProgramData\KCxWare` recovery evidence.

## Recovery

State writes use a temporary file, write-through flush, atomic replacement, and a backup. An incomplete transaction loads as `RecoveryRequired`. Open KCxWare and choose **Run Safe Recovery** to restart services recorded as previously running, restore the previous installed power plan when possible, and remove the one-shot task. Choose **Cancel Armed Transition** before reboot to remove an armed task.

Logs: `C:\ProgramData\KCxWare\logs\helper.log`. State: `C:\ProgramData\KCxWare\state.json`. Neither contains secrets.

## Validation boundary

Automated tests cover transitions, interruption recovery, idempotency, missing services, protected-service policy, power-plan fallback, and stale-task cleanup. Build and launch validation do not by themselves prove a physical reboot/profile transition; privileged live acceptance should be performed deliberately after saving all work.
