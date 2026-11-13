# Repository working preferences

- Live Translator Overlay is open source under the [MIT License](LICENSE). The public GitHub repository (`https://github.com/nnavyy/LiveTranslatorOverlay`, remote `origin`) is the source of truth for all application source, documentation, and releases.
- Always ask the repository owner before pushing commits or tags, or creating and publishing a release.
- Never commit secrets, API keys, service-role or secret Supabase keys, signing certificates, `appsettings.json`, `community.json`, `local-speech.json`, or large binary model/audio artifacts. Keep unrelated work out of commits and never force-push shared history without explicit owner approval.
- Run the appropriate build and verification checks before claiming a bug is fixed or marking work complete (for Windows changes, `LiveTranslatorOverlay.csproj` plus `RegressionChecks`, `AuditChecks`, `SpeakerChecks`, and `LocalCpuChecks`; for macOS changes, `MacApp/LiveTranslatorMac.csproj` and `MacApp/package-macos.sh`; for database migrations, `tests/CommunityDatabase/verify.mjs`).
- For user-facing application fixes or features, keep versions consistent across `LiveTranslatorOverlay.csproj`, `MacApp/LiveTranslatorMac.csproj`, and `Shared/LiveTranslatorOverlay.Shared.csproj`, update `RELEASE_NOTES.md` and `MacApp/RELEASE_NOTES.md` with verified behavior, and publish through the owner-approved `v*` tag workflow so the in-app updater discovers the release.
- Documentation-only changes, including review reports and audits, should be committed without creating an application release.
