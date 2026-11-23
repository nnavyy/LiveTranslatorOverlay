# Email Domain Setup (`livetranslator.app`)

This folder contains ready-to-import DNS, SMTP, and transactional email templates for **`livetranslator.app`** so Supabase Auth sends branded 6-digit login OTPs from **`noreply@livetranslator.app`**.

## Addresses & Roles
- **`noreply@livetranslator.app`** — Supabase Auth OTP login & verification emails.
- **`support@livetranslator.app`** — License/donation support receipts & user assistance (Cloudflare Email Routing forward to your personal mailbox).
- **`updates@livetranslator.app`** — Release channel announcements (`main` / `beta` / `dev`).
- **`security@livetranslator.app`** — Security & vulnerability disclosures.

## Automated Setup Steps (Zero Secrets in Git)

1. **Register `livetranslator.app` on Cloudflare** (Registrar or external DNS pointed to Cloudflare nameservers).
2. **Verify sending domain in [Resend](https://resend.com)** (`livetranslator.app`):
   - Copy Resend's DKIM TXT key (`resend._domainkey`), return-path MX/TXT values into `dns-records-livetranslator.app.json` (or import `livetranslator.app.zone` in Cloudflare DNS and update `<RESEND_DKIM_KEY>`).
   - Enable Cloudflare Email Routing for `support@`, `updates@`, and `security@` -> forward to your personal developer email.
3. **Configure Supabase Auth SMTP automatically** via the provided script (never hardcode API tokens in files):

```powershell
# Dry-run preview (verifies templates and prints payload without calling Supabase API):
powershell -NoProfile -ExecutionPolicy Bypass -File .\supabase\email\configure-supabase-smtp.ps1 -DryRun

# Live configuration against your Supabase project:
$env:SUPABASE_PROJECT_REF = "<your-project-ref>"
$env:SUPABASE_ACCESS_TOKEN = "<your-supabase-management-pat>"
$env:RESEND_API_KEY = "<your-resend-api-key>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\supabase\email\configure-supabase-smtp.ps1
```

## Runtime Override in App
The app defaults to **`livetranslator.app`**. To test a staging/custom domain without rebuilding, set environment variable `LIVETRANSLATOR_EMAIL_DOMAIN=your-staging-domain.example`.
