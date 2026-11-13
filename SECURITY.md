# Security Policy

## Supported Versions

Security fixes apply to the active 1.0.x open-source release line (`release/ml-1.0` / `main`).

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report security issues privately using **GitHub Private Vulnerability Reporting** on the official repository:

- Open the repository **Security** tab → **Advisories** → **Report a vulnerability**:  
  `https://github.com/nnavyy/LiveTranslatorOverlay/security/advisories/new`

Include as much detail as possible to reproduce the issue:
- Affected component (Windows app, macOS app, `Shared/`, `supabase/` SQL, or installer/packaging scripts)
- Steps to reproduce or proof of concept
- Potential impact (for example, key exposure, SQL privilege escalation, path traversal, or remote code execution)

## Security Boundaries & Design Notes

- **API Keys**: On Windows, Groq and Deepgram keys are encrypted with Windows DPAPI (`DataProtectionScope.CurrentUser`) in `%LOCALAPPDATA%\LiveTranslatorOverlay\appsettings.json`. On macOS, Groq keys are held in session memory only. Never share or commit `appsettings.json`.
- **Supabase Client (`Shared/Community/CommunityClient.cs`)**: The desktop client accepts only `sb_publishable_` keys over HTTPS (or loopback HTTP for local testing) and rejects service-role/secret keys. Database schemas (`supabase/migrations/`) revoke direct table/function access from `anon` and `authenticated`, exposing only narrow `SECURITY DEFINER` RPCs.
- **Model & Update Verification**: Downloaded speech models and update ZIP archives are checked against expected file sizes and SHA-256 digests (`SpeechModelDownload.cs`, `StableRelease.cs`) before loading or staging.
- **In-Memory Processing**: Live audio loopback and Screen OCR images remain in volatile RAM and are not written to disk.
