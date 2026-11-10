# Community staging

The Community window is available on Windows and Mac. It always shows the local audio total privately. Its online boards use the `lt_community_board` RPC. The app does not upload audio usage, create accounts, or accept payments in this slice.

1. Apply `migrations/20261110152000_community_rankings.sql` to an isolated Supabase staging project. The migration keeps the board closed by default and grants anonymous clients access only to the consented board projection.
2. For staging, create test accounts and fixture rows. Set `community_private.settings.rankings_open` to `true` only in that staging database. The private profile and usage write functions are deliberately ungranted until account login and versioned consent are ready.
3. Put the following file at `community.json` in the app data directory. On Windows this is `%LOCALAPPDATA%\LiveTranslatorOverlay`. On Mac it is `~/Library/Application Support/LiveTranslatorOverlay`. Use a Supabase publishable key. The app rejects service/secret keys and HTTP hosts other than loopback.

```json
{
  "url": "https://YOUR-STAGING-PROJECT.supabase.co/",
  "publishableKey": "sb_publishable_YOUR_STAGING_KEY"
}
```

Without a config file, the app makes no community requests. Do not add this config to release packages until the production rollout is approved and the full account, privacy, and manual payment workflow has been verified. The approved prices are visible in the app while payments are closed. Never record unverified payments in `verified_support`.

For an offline migration check, install `@electric-sql/pglite` in a scratch directory, then run `node tests/CommunityDatabase/verify.mjs <scratch-directory>` from the repo root. The test uses an ephemeral Postgres database and a small stand-in for Supabase Auth. It does not contact a real Supabase project.
