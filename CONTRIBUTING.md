# Contributing to Live Translator Overlay

Thank you for your interest in contributing. All source code and contributions in this repository are licensed under the [MIT License](LICENSE).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Windows development**: Windows 10/11 x64 (`net10.0-windows10.0.19041.0`)
- **macOS development**: macOS 14+ with Xcode Command Line Tools (`swiftc`, `codesign`)
- **Database checks (optional)**: Node.js (for `tests/CommunityDatabase/verify.mjs`)

## Project layout

- `Shared/` (`LiveTranslatorOverlay.Shared.csproj`) — Cross-platform logic shared by Windows and macOS (`net10.0`): translation providers, localization, history, community client, settings, terms rendering, and update metadata.
- `Core/` & root XAML/CS files (`LiveTranslatorOverlay.csproj`) — Windows WPF desktop application (`net10.0-windows10.0.19041.0`).
- `MacApp/` (`LiveTranslatorMac.csproj`) — macOS Avalonia application + `NativeAudio.swift` capture bridge.
- `tests/` — Offline verification suites (`RegressionChecks`, `AuditChecks`, `SpeakerChecks`, `LocalCpuChecks`, `CommunityDatabase`) and manual probes.
- `supabase/` — PostgreSQL migrations and staging instructions for Community features.
- `runtime/` & `research/` — Optional local Python workers and speech recognition experiments.

## Building and running checks

### Windows

```powershell
dotnet build LiveTranslatorOverlay.csproj -c Release --nologo
dotnet run --project tests/RegressionChecks/RegressionChecks.csproj -c Release --nologo
dotnet run --project tests/AuditChecks/AuditChecks.csproj -c Release --nologo
dotnet run --project tests/SpeakerChecks/SpeakerChecks.csproj -c Release --nologo
dotnet run --project tests/LocalCpuChecks/LocalCpuChecks.csproj -c Release --nologo
```

### Cross-platform & macOS

```sh
dotnet build Shared/LiveTranslatorOverlay.Shared.csproj -c Release --nologo
dotnet build MacApp/LiveTranslatorMac.csproj -c Release --nologo
# On macOS 14+:
bash MacApp/package-macos.sh osx-arm64 ./out
```

### Supabase migrations (offline)

```sh
npm install --prefix /tmp/pglite @electric-sql/pglite
node tests/CommunityDatabase/verify.mjs /tmp/pglite
```

## Coding conventions

- Target `.NET 10` with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- Prefer file-scoped namespaces in new files and keep shared logic in `Shared/` so Windows and macOS stay aligned.
- Preserve existing `AutomationProperties` names so accessibility and UI tests keep working.
- Keep user-facing text and bundled Markdown (`APP_LICENSE.md`, `PRIVACY.md`) aligned across Windows and macOS via `TermsNotice` and `LocalizationManager`.
- Include deterministic offline checks in `tests/` for bug fixes whenever possible (mocked HTTP or synthetic audio; never require live API keys or real network calls in CI suites).

## Pull requests and hygiene

1. Never commit API keys, secrets, signing certificates, `appsettings.json`, `community.json`, `local-speech.json`, or large binary model/audio files.
2. Keep pull requests focused on a single fix or feature and include the exact test commands and results you ran.
3. By submitting a pull request, you agree that your contribution is licensed under the project's [MIT License](LICENSE).
