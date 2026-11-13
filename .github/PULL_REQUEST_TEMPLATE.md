## Summary

Describe the change and why it is needed.

## Scope

- [ ] Windows (`LiveTranslatorOverlay`)
- [ ] macOS (`MacApp`)
- [ ] Shared (`Shared/`)
- [ ] Supabase / Community (`supabase/`)
- [ ] Documentation / CI

## Verification

List the exact build and test commands you ran and their results:

- [ ] `dotnet build LiveTranslatorOverlay.csproj -c Release --nologo`
- [ ] Offline Windows suites (`RegressionChecks`, `AuditChecks`, `SpeakerChecks`, `LocalCpuChecks`)
- [ ] `dotnet build Shared/LiveTranslatorOverlay.Shared.csproj -c Release --nologo`
- [ ] `dotnet build MacApp/LiveTranslatorMac.csproj -c Release --nologo` / `MacApp/package-macos.sh`
- [ ] `node tests/CommunityDatabase/verify.mjs <dir>` (if SQL migrations changed)
- [ ] Confirmed no API keys, secrets, `appsettings.json`, `community.json`, or binary models/audio are included
