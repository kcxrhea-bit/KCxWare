# KCxWare

KCxWare is a Windows 11 desktop utility for switching a PC between **Gaming Mode**, **Programming Mode**, and **Normal Mode**.

It is designed around temporary, reversible session changes rather than permanent Windows tuning.

## Safety and reversibility

The main WPF application runs without administrator rights.

When a privileged operation is required, KCxWare launches `KCxWare.Helper.exe` through Windows UAC. The helper captures the relevant baseline state before making changes, applies the requested mode, verifies the result, and records transactional state under:

`C:\ProgramData\KCxWare`

Gaming and Programming modes are temporary session modes. Returning to Normal Mode restores the captured baseline where safe. A new Windows boot also normalizes the session back to Normal rather than permanently reapplying Gaming or Programming settings.

KCxWare uses allowlisted behavior rather than blanket system disabling.

It does not intentionally disable or interfere with:

- Microsoft Defender
- Windows Firewall
- core RPC or networking services
- Wi-Fi
- audio
- NVIDIA display components
- NVIDIA Overlay / Highlights
- Chrome
- Logitech input software
- Gaming Services
- GameInput
- Easy Anti-Cheat
- BattlEye

KCxWare does not modify game files, tamper with anti-cheat, overclock hardware, or apply undocumented performance registry tweaks.

## Gaming Mode

Gaming Mode applies temporary cleanup and prioritization in the current Windows session.

Depending on what is actually installed and running, KCxWare can:

- select an appropriate installed performance-oriented power plan
- stop allowlisted background services and processes
- shut down WSL when WSL is detected and active
- suppress Windows Search for the Gaming session using reversible service configuration handling
- verify cleanup results before declaring the transition successful

Gaming Mode uses sustained verification rather than assuming a process or service stayed stopped after a single check.

Some Windows-managed components can legitimately respawn through Windows infrastructure. KCxWare treats appropriate shell/DCOM-managed components as best-effort targets instead of disabling Windows infrastructure to keep them stopped.

KCxWare does not guarantee a particular FPS increase or performance improvement. Results depend on the PC, installed software, workload, game, drivers, and existing system configuration.

## Programming Mode

Programming Mode prepares the session for development-oriented use without blindly launching heavy applications.

KCxWare detects supported development capabilities that are actually available and adapts accordingly.

Examples of detected capabilities include:

- Git / Git Bash
- Node.js / npm
- WSL
- Docker
- Ollama and supported local-AI tooling
- .NET SDK
- Visual Studio
- Visual Studio Code

Programming Mode does not require all of these tools to be installed.

## Normal Mode

Normal Mode restores the original baseline captured before temporary mode changes.

Where safe and applicable, KCxWare restores:

- previously running services
- temporary service configuration changes
- the original power plan

KCxWare intentionally does not attempt to reconstruct and relaunch arbitrary applications that were closed, because their original command lines, documents, arguments, and application state cannot always be restored safely.

## Hardware and software awareness

KCxWare detects supported capabilities instead of assuming every PC has the same hardware or software.

Current capability awareness includes supported detection for:

- NVIDIA graphics
- AMD graphics
- Intel graphics
- Git / Git Bash
- Node.js / npm
- WSL
- Docker
- Ollama / supported local-AI tooling
- .NET SDK
- Visual Studio
- Visual Studio Code
- installed Windows and vendor power plans

Optional hardware and software may be absent. KCxWare adapts to capabilities that are actually available.

KCxWare is intended for normal supported Windows 11 gaming and development PCs. It does not claim identical behavior on every possible Windows configuration.

## Recovery

State changes are handled transactionally.

If a transition is interrupted or cannot be completed safely, KCxWare can enter `RecoveryRequired` rather than pretending the requested mode succeeded.

Use **Run Safe Recovery** to restore the recorded baseline where possible.

KCxWare persists recovery/state information under:

`C:\ProgramData\KCxWare`

## Requirements

### Running KCxWare

- Windows 11 x64
- .NET 8 Windows Desktop Runtime
- administrator approval through UAC when KCxWare performs privileged operations

The current release is framework-dependent.

### Building from source

- Windows 11 x64
- .NET 8 SDK
- Git, if cloning the repository

## Build and test

```powershell
dotnet restore .\KCxWare.sln
dotnet build .\KCxWare.sln --configuration Release
dotnet test .\KCxWare.sln
```

## Publish

Create the framework-dependent Windows x64 release:

```powershell
.\scripts\Publish.ps1
```

The publish script verifies the required application files, creative assets, license, and third-party notices.

Published output is written under:

`publish\win-x64`

Build and publish output are intentionally excluded from Git.

## Install

After publishing, open an elevated PowerShell window and run:

```powershell
.\installer\Install-KCxWare.ps1
```

KCxWare installs under:

`C:\Program Files\KCxWare`

The installer creates the intended Windows shortcuts and verifies that required application files and release notices are present.

## Uninstall

From an elevated PowerShell window:

```powershell
.\installer\Uninstall-KCxWare.ps1
```

The uninstaller removes the application and its shortcuts while preserving recovery evidence under `C:\ProgramData\KCxWare`.

## Logs and state

Helper log:

`C:\ProgramData\KCxWare\logs\helper.log`

State:

`C:\ProgramData\KCxWare\state.json`

## Third-party compatibility and assets

KCxWare may detect or interoperate with third-party hardware and software. Those products are not bundled with KCxWare merely because support or detection exists.

Third-party product, company, and trademark names are used for factual compatibility and interoperability identification. Their respective trademarks remain the property of their owners.

Compatibility references do not imply sponsorship, endorsement, or affiliation.

Creative-asset provenance and additional third-party information are documented in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## License

KCxWare source code is licensed under the MIT License.

See [`LICENSE`](LICENSE).

Creative assets and third-party materials are documented separately in `THIRD_PARTY_NOTICES.md`.