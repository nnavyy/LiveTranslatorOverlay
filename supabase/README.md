# Community staging

The Community window is available on Windows and Mac. It always shows the local audio total privately. Its online boards use the `lt_community_board` RPC. The app does not upload audio usage, register new accounts, or accept payments in this slice.

1. Apply `migrations/20261110152000_community_rankings.sql` to an isolated Supabase staging project. The migration keeps the board closed by default and grants anonymous clients access only to the consented board projection.
   Apply `migrations/20261111210000_public_profile_support.sql` to add verified net contributions to profiles that opted into public support. This uses the existing rankings RPC and keeps the amount private when that preference is off.
2. For staging, create test accounts and fixture rows. Set `community_private.settings.rankings_open` to `true` only in that staging database. The private profile and usage write functions are deliberately ungranted until account login and versioned consent are ready.
3. Put the following file at `community.json` in the app data directory. On Windows this is `%LOCALAPPDATA%\LiveTranslatorOverlay`. On Mac it is `~/Library/Application Support/LiveTranslatorOverlay`. Use a Supabase publishable key. The app rejects service/secret keys and HTTP hosts other than loopback.

```json
{
  "url": "https://YOUR-STAGING-PROJECT.supabase.co/",
  "publishableKey": "sb_publishable_YOUR_STAGING_KEY"
}
```

Without a config file, the app makes no community requests. Keep the staging config out of release packages. The approved prices are visible in the app while payments are closed. Never record unverified payments in `verified_support`.

## Developer-assisted purchase preview

1. Apply `migrations/20261111100000_manual_purchase_preview.sql` after the rankings migration in the isolated staging project. Set `community_private.settings.manual_purchase_preview_open` to `true` there only.
2. Provision a confirmed test account in that project. Configure the email template for a six-digit OTP using `{{ .Token }}`. The preview requests a code for an existing user and does not create accounts. [Supabase email OTP setup](https://supabase.com/docs/guides/auth/auth-email-passwordless#with-otp)
3. Add `"previewManualRequests": true` to the local staging `community.json`. Open **Community → Support development → Purchase options → Developer-assisted request (staging)**. Select a lifetime offer, request the email code, and submit it. The app displays a private pending request ID. The same account and offer reuse the same request ID on retry.
4. Inspect the staging records in `community_private.manual_purchase_requests` as the database operator. The request contains the auth account ID, offer ID, and timestamp. The preview has no payment instructions, donation form, buyer inbox, owner approval, or fulfillment. It never writes to the verified support ledger.

## Offline verification

For an offline migration check, install `@electric-sql/pglite` in a scratch directory, then run `node tests/CommunityDatabase/verify.mjs <scratch-directory>` from the repo root. The test uses an ephemeral Postgres database and a small stand-in for Supabase Auth. It does not contact a real Supabase project.

## `v1.1.1`: `livetranslator.app` Email Automation & Anti-Abuse Migration

1. **Migration (`20261123180000_guest_donations_hwid_and_antiabuse.sql`)**:
   - Adds salted HWID columns (`hardware_fingerprint_hash`), `community_private.guest_donation_requests`, and `community_private.guest_tag_from_hwid()` (unsigned 32-bit parity with `HardwareFingerprint.DeriveGuestDisplayTag`).
   - Exposes `public.lt_submit_guest_donation(p_cents, p_hwid_hash, p_note)` to `anon, authenticated` with a 5-requests-per-24-hours per-device rate limit (`42501`).
   - Adds `community_private.submit_usage_with_device(...)` enforcing overlapping-interval and 86,400 s/day per-HWID Sybil protection across accounts, and updates `lt_community_board('supporters')` to include verified guest donations as `User#XXXXX`.
2. **Email Domain (`livetranslator.app`) Automation (`supabase/email/`)**:
   - Import DNS records from `supabase/email/dns-records-livetranslator.app.json` or `supabase/email/livetranslator.app.zone` (Resend SPF/DKIM/DMARC).
   - Dry-run SMTP & auth configuration:
     ```powershell
     powershell -NoProfile -ExecutionPolicy Bypass -File .\supabase\email\configure-supabase-smtp.ps1 -DryRun
     ```
   - Apply to your live Supabase project by setting `SUPABASE_ACCESS_TOKEN`, `SUPABASE_PROJECT_REF`, and `RESEND_API_KEY` environment variables, then re-running without `-DryRun` (configured templates live in `supabase/email/templates/`).

## `v1.1.2`: Owner 1-by-1 Grants & Private Server-Gated Beta (`supabase/migrations/20261123210000_owner_grants_and_private_beta.sql`)

Beta (`$100` Insider) and `dev` branches live only on the private repository (`private`), not on public `origin`. Approved Beta binaries are served through a private Supabase Storage bucket (`beta-releases`) + Edge Function (`supabase/functions/beta-release/index.ts`), never via public GitHub URLs.

1. Apply `migrations/20261123210000_owner_grants_and_private_beta.sql` and create a **private** Supabase Storage bucket named `beta-releases`.
2. Deploy the Edge Function (`supabase functions deploy beta-release`).
3. Manage requests and grants 1-by-1 using `scripts/owner-grants.ps1` (requires `SUPABASE_URL` and `SUPABASE_SERVICE_ROLE_KEY` in environment variables; never commit keys):
   ```powershell
   # List pending requests (accounts + guest donations) and verified Insiders
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action list -Status pending

   # Approve a paid user request and unlock Beta access
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action grant -RequestId "<uuid>" -GrantBeta -Note "Verified payment"

   # Revoke support & deactivate Beta access
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action revoke -RequestId "<uuid>" -Note "Refunded"

   # Verify a guest User#XXXXX donation so it appears on Supporters
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action grant-guest -RequestId "<uuid>"

   # Build & publish a private Beta archive (outputs outside git under publish-beta/)
   .\scripts\publish-channel.ps1 -Channel beta
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action publish-beta -Version 1.1.2 -Runtime win-x64 -ZipPath .\publish-beta\LiveTranslatorOverlay-Windows-x64.zip
   ```

## `v1.1.3`: Live Profile & Rankings RPCs + Owner GUI (`supabase/migrations/20261123220000_enable_authenticated_rankings.sql`)

1. Apply `migrations/20261123220000_enable_authenticated_rankings.sql` to expose authenticated profile read/write (`lt_my_profile`, `lt_save_profile`), device-bound usage submission (`lt_submit_usage_segment`), and service-role board toggle (`lt_admin_set_rankings_open`), while preserving `rankings_open = false` by default.
2. Manage requests, guest donations, Beta grants, and the global rankings toggle either through the in-app **Payments & Beta Grants** dialog (launch `LiveTranslatorOverlay.exe --developer` -> Developer Tools) or via CLI:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action open-rankings -Active 1
   ```
