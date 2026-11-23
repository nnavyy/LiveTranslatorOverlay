# Live Translator Overlay 1.1.3

Open-source (MIT) UI refinement, app icon integration, smooth scrolling, authenticated Community Rankings & User Profile, and developer-only Owner Dashboard.

## Highlights in v1.1.3

- **Clean Zinc UI & App Branding (`Assets/app-icon.ico`, `Assets/app-logo.png`)**:
  - Replaced repetitive `"live word-by-word"` dropdown labels with concise speech engine names (`Local · Balanced (CPU)`, `Local · Higher Accuracy (CPU)`, `Local · Auto (GPU / CPU)`, `Local · High Accuracy (GPU)`) and tightened localization copy across `en`, `id`, `es`, and `ru`.
  - Applied the custom Live Translator symbol across the MainWindow header, Community window header, all dialog window icons, Windows desktop shortcuts, and the NSIS Setup installer (`MUI_ICON` / `MUI_UNICON`).
  - Unified dark Zinc surfaces across `HistoryWindow`, `FeedbackWindow`, and `CommunityWindow`, removed all decorative pill badges from the Support tab and profile popups, and hid the owner-only `Dev` item from the Release Update Channel selector.
- **Smooth Bounded Mouse-Wheel Scrolling (`Core/SmoothScrollBehavior.cs`)**:
  - Added damped per-notch wheel step clamping (`±24 DIP`) on the Community Rankings, Support panels, and Feedback dialog so long scroll views no longer jump.
- **Authenticated User Profile & Live Rankings (`20261123220000_enable_authenticated_rankings.sql`)**:
  - Added `UserProfileCard` with nickname and privacy opt-ins (`lt_my_profile`, `lt_save_profile`, `lt_submit_usage_segment`) plus immediate local device rankings preview (`BuildLocalPreviewPage`) before global server publication.
- **Developer-Only Owner Dashboard (`Core/OwnerGrantsWindow.cs`, `--developer`)**:
  - Added an in-app **Payments & Beta Grants** window (visible only when launched with `--developer`) to inspect pending account requests, guest `User#XXXXX` donations, verified Beta Insiders, approve/revoke 1-by-1 grants, and toggle global Community Rankings (`lt_admin_set_rankings_open`), alongside `scripts/owner-grants.ps1 -Action open-rankings`.

---

# Live Translator Overlay 1.1.2

Open-source (MIT) release introducing a one-click Windows Setup Wizard (`.exe`), server-gated private Beta access (`$100` Insiders verified 1-by-1 by the developer), and strict separation between public `origin` (`main`) and private `beta`/`dev` repositories.

## Highlights in v1.1.2

- **One-Click Windows Setup Installer (`LiveTranslatorOverlay-Setup-1.1.2.exe`)**:
  - Updated [windows-setup.nsi](scripts/windows-setup.nsi), [build-setup.ps1](scripts/build-setup.ps1), and [release.yml](.github/workflows/release.yml) so GitHub Releases publish a per-user NSIS Setup Wizard (no admin prompt required) with Start Menu + Desktop shortcuts, uninstaller, and speech model setup alongside the portable `.zip`.
- **Developer-Granted Private Beta Access & 1-by-1 Verification**:
  - Removed public `updates/ml-beta.json` from `origin` and moved `beta` / `dev` branches exclusively to the private repository (`private`).
  - Added migration `20261123210000_owner_grants_and_private_beta.sql`, Supabase Edge Function `supabase/functions/beta-release/index.ts`, and [scripts/owner-grants.ps1](scripts/owner-grants.ps1) so the developer can list pending requests, verify/revoke users 1-by-1 (`grant`, `revoke`, `grant-guest`, `set-beta`), and serve private Beta downloads over time-limited signed Storage URLs.
  - Added **Refresh access status** in Community & Support (`Core` & `MacApp`), locked the Beta channel option until verified, and restricted `Dev` channel visibility to owner/dev mode.
- **Owner Quick Launch Shortcut**:
  - `scripts/publish-channel.ps1 -Channel dev` now creates/updates `Live Translator Overlay (Dev).lnk` directly in the parent workspace folder alongside syncing `DevApp`.

---

# Live Translator Overlay 1.1.1

Open-source (MIT) security, `livetranslator.app` email identity, and login-optional guest donation update on the 1.x machine-learning line.

## Highlights in v1.1.1

- **Login-Optional App & Guest Donations (`User#XXXXX`)**:
  - Core audio/screen translation and custom **Donate** contributions work **without signing in**. Guest donors appear deterministically on the `supporters` leaderboard as `User#10000`–`User#99999` (`guest-donations.dat`, DPAPI/0600 encrypted).
  - **Email OTP sign-in** is required only when participating in named **Community Rankings** or claiming **Lifetime Supporter (`US$19.99`)** / **Lifetime Insider (`US$100`)** badges.
- **Anti-Abuse Salted HardwareID & ECDSA P-256 Provenance**:
  - Computes a one-way salted SHA-256 device hash (`livetranslator.app:hwid:v1:`) without sending raw serials, enforced in Supabase migration `20261123180000_guest_donations_hwid_and_antiabuse.sql` (5 guest donations/24h per device, cross-account overlapping-interval Sybil usage rejection, and `guest_tag_from_hwid()`).
  - Added `EntitlementSignature` (ECDSA NIST P-256 SHA-256 entitlement tokens bound to HWID/tier/expiry + SHA-256 running binary provenance).
  - Bumped Terms & Privacy Policy to revision **`ml-3`** (`TERMS_REVISION.txt`, `PRIVACY.md`, `APP_LICENSE.md`) clarifying salted HWID disclosure and MIT vs. official brand provenance boundaries.
- **Automated `livetranslator.app` Email & DNS Automation Kit**:
  - Added `ProjectDomainConfig` (`noreply@livetranslator.app`, `support@livetranslator.app`, `security@livetranslator.app`), Cloudflare/Resend DNS JSON & BIND zone records, branded OTP & receipt HTML/text templates, and `supabase/email/configure-supabase-smtp.ps1` (`-DryRun`).

---

# Live Translator Overlay 1.1.0

Open-source (MIT) cross-vendor GPU streaming and multi-channel release on the 1.x machine-learning line.

## Highlights

- **Cross-Vendor GPU + CPU Auto Tiering (No NVIDIA/CUDA or Python Lock-In)**:
  - Windows packages bundle **`Whisper.net.Runtime.Vulkan` 1.9.1** (NVIDIA, AMD, and Intel GPUs) alongside `Cpu` and `NoAvx` runtimes.
  - Added two word-by-word modes (`local-auto` and `local-turbo`) using quantized **`ggml-large-v3-turbo-q5_0.bin`** (`574 MB`, SHA-256 verified) on GPU with automatic `ggml-base.bin` CPU fallback, Silero VAD v6.2.0 gating (tuned to preserve quiet speakers), and startup SPIR-V shader warm-up.
  - On RTX 2050 Vulkan held-out FLEURS test speech, `large-v3-turbo (Q5_0)` cut Russian WER from `13.63%` -> `5.00%`, Japanese CER from `25.56%` -> `4.92%`, and Indonesian WER from `26.84%` -> `11.33%` while running at `0.186x` final RTF (`5.4x` faster than real time).
- **Word-by-Word Local Agreement-2 Streaming (`300 ms` Windows)**:
  - Non-queuing `300 ms` sliding-window capture (`EnableStreamingWindows`) paired with Unicode/CJK-aware prefix stabilization (`StreamingAgreement`) displays stabilized words progressively instead of waiting 4–12 seconds per sentence.
- **Persistent Local MT Daemon (`--daemon`) & Kernel Process Guard**:
  - Reuses loaded M2M-100 CTranslate2 weights across captions (`11.18 s` cold boot -> `892.5 ms` warm mean), validates CUDA BLAS before use with clean `int8` CPU fallback, overlaps warmup with Whisper initialization, and binds the Python tree to a Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` handle so zero orphaned worker processes survive exit.
- **Email OTP Account Login, Tracked Donations & Selectable Tiers**:
  - Updated **Community & Support** dialog with email OTP sign-in (Supabase Auth + offline local staging ledger fallback), cumulative donation/support history (`20261123120000_account_login_donations_and_tiers.sql`), selectable **Lifetime Supporter (`US$19.99`)**, **Lifetime Insider (`US$100`)**, and custom donations, plus solo-developer gratitude notices. Core MIT functionality remains free and public checkout remains staged (`SupportCatalog.Closed`).
- **3-Channel Git & Updater Support (`main` / `beta` / `dev`)**:
  - Supports `stable` (`updates/ml-stable.json`), `beta` (`updates/ml-beta.json` for `$100` Insiders), and `dev` (local `channel.json` feed via `scripts/publish-channel.ps1`).
- **Research Paper & Reproducible Benchmarks**:
  - Added [research/speech/paper/PAPER_NOVELTY_AND_RESEARCH_GAP.md](research/speech/paper/PAPER_NOVELTY_AND_RESEARCH_GAP.md) and [research/speech/paper/streaming-benchmark-results.json](research/speech/paper/streaming-benchmark-results.json).
