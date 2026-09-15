# KCxWare

KCxWare is a lightweight native Windows 11 WPF control panel for switching one PC between Gaming, Programming, and Normal Everyday modes. Its black, cyan, and magenta interface uses the supplied `assets/1.png` background and the exact `assets/KCxWare.ico` application branding.

## Safety model

The WPF interface runs without administrator rights. After a clear restart confirmation, it starts `KCxWare.Helper.exe` through Windows UAC. The helper records a transaction under `C:\ProgramData\KCxWare`, creates one elevated logon task, and schedules a restart. The task applies the armed profile once and removes itself. KCxWare does not remain resident during gaming.

Gaming suppression is allowlisted and session-oriented: service startup types are never disabled. Defender, Firewall, RPC, networking, Wi-Fi, audio, NVIDIA display essentials, Logitech input, Gaming Services, GameInput, Easy Anti-Cheat, and BattlEye are explicitly protected. Unknown and missing services are not modified. KCxWare never changes Fortnite files, tampers with anti-cheat, overclocks hardware, or applies undocumented registry tweaks.

## Modes

- **Gaming Mode** stops installed, running allowlisted development/sync/indexing/overlay workloads for the session and selects AMD Ryzen High Performance (`9935e61f-1661-40c5-ae2f-8495027d5d5d`) when present.
- **Programming Mode** restores services that KCxWare recorded as running before gaming and prefers installed AMD Ryzen High Performance, AMD Ryzen Balanced, then Windows Balanced. It does not launch every heavy development application.
- **Normal Mode** restores captured services and prefers AMD Ryzen Balanced (`9897998c-92de-4669-853f-b7cd3ecb2790`), then Windows Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`).

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
