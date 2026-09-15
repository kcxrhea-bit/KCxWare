# KCxWare repository rules

Always inspect before editing and preserve unrelated dirty work.

KCxWare is a native Windows WPF application. Keep the normal UI unelevated and route privileged operations through `KCxWare.Helper` using UAC. Never disable Windows Security, Defender, Firewall, core networking/audio, display-driver essentials, Logitech input, game services, or anti-cheat. Unknown services and processes are never modified automatically. Gaming changes must be session-oriented and recoverable; do not add overclocking, game-file changes, undocumented registry tweaks, or permanent service-disable hacks.

The exact files `assets/KCxWare.ico` and `assets/1.png` are authoritative branding. Do not replace or regenerate them. API keys or other secrets must never be logged, exported, hardcoded, or returned to the UI.

Every user-facing feature, workflow, setting, command, integration, permission, error, or configuration change must update the packaged offline Guide in the same task. The Guide must cover purpose, use, setup, defaults, permissions/risks, failures, recovery, examples, and related features. A feature is incomplete while the Guide is missing or stale.

Before completion, run `dotnet build`, `dotnet test`, Release x64 publish, launch verification, icon verification, `git diff --check`, and inspect `git status`. Build/package proof is distinct from live privileged machine-transition acceptance.
