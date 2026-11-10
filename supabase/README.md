# Community staging

The Community window is available on Windows and Mac. It always shows the local audio total privately. Its online boards use the `lt_community_board` RPC. The app does not upload audio usage, register new accounts, or accept payments in this slice.

1. Apply `migrations/20261110152000_community_rankings.sql` to an isolated Supabase staging project. The migration keeps the board closed by default and grants anonymous clients access only to the consented board projection.
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
