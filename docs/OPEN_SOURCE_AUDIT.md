# Open-Source Secrets, History & License Audit

**Branch audited:** `release/ml-1.0` (245 tracked files at audit baseline, 77 commits reachable from `release/ml-1.0`), plus full local `.git` store (`git rev-list --all`, 106 commits total across all local branches and tags).  
**Verdict:** **SAFE TO PUBLISH `release/ml-1.0` WITH RESPECT TO CREDENTIALS & BINARIES**, subject to the **Owner Decisions** listed below (owner email in feedback/developer UI, `--developer` links, and internal payment-ops review notes). **DO NOT push the disconnected `v1.0.0` tag or `review/windows-setup-stt` branch** without addressing the historical `TestTranslation.cs` leak (`1f362ae`) and unmerged scripts.

---

## 1. Audit Scope & Methodology

1. **Working tree & tracked files (`release/ml-1.0`)**: Audited all tracked C#, XAML, Swift, PowerShell, Bash, Python, SQL, Node.js, Markdown, JSON, and workflow files, including `supabase/`, `scripts/`, `runtime/`, `research/`, `.github/`, `licenses/`, and `appsettings.example.json`.
2. **Full git history (`git rev-list --all`)**: Generated a complete `7,840,329`-byte patch stream (`git log --all -p`) and checked every added/deleted file path and diff hunk across all 106 commits for:
   - API keys & tokens (`gsk_*`, `sk-*`, `ghp_*`, `github_pat_*`, `hf_*`, `AKIA*`, `re_*`, `sb_publishable_*`, `sb_secret_*`, JWTs, `service_role`)
   - Windows DPAPI blobs (`AQAAANCM...`, `*Protected` JSON blobs), `appsettings.json`, `.env*`, `community.json`, `local-speech.json`
   - SMTP/Resend credentials, signing certificates (`.pfx`, `.p12`, `.pem`, `.key`, `.snk`, `.jks`), wallet addresses
   - Personal emails, local user paths (`C:\Users\...`, `B:\Projects\...`), private repository URLs, owner/admin tooling
   - Large binary objects, audio recordings, model weights, executables
3. **Dependency & model license compatibility**: Inspected every `.csproj`, resolved `obj/project.assets.json` (41 NuGet packages across 8 asset files), `scripts/package-windows.ps1`, `Core/SpeechModelDownload.cs`, `runtime/*.py`, and `research/speech/` model/dataset citations.

---

## 2. Findings Table

| Severity | Location (File / Commit) | Reachable from `release/ml-1.0`? | Finding (Redacted) | Action / Recommendation |
| --- | --- | --- | --- | --- |
| **CRITICAL (Historical, Disconnected)** | `TestTranslation.cs` in commit `1f362ae` (`2026-08-22`, deleted in `81666f7`, ancestor of tag `v1.0.0`) | **No** (disconnected root; `release/ml-1.0` and `origin/main` start at root `2cda7ee`) | Contained a 56-character Groq API key (`gsk_4C32…[REDACTED]`). | **Rotate/revoke** that Groq key if still active. **Do not push tag `v1.0.0` or commit `1f362ae`** to public remotes; keep `release/ml-1.0` rooted at `2cda7ee`. |
| **MEDIUM (Owner Decision)** | `FeedbackWindow.xaml.cs` (L19–20, L358, L373–374); commits `e11d6cf`, `917b6c4`, `393158f`, `d643973`, `5431924` (and `LocalizationManager.cs` in `e11d6cf`, already removed in HEAD) | **Yes** | Owner personal email `nandazhafran@gmail.com` hardcoded in `TargetEmail` and `FormSubmitEndpoint` (`https://formsubmit.co/ajax/...`), plus `noreply@livetranslator.app` / `https://livetranslator.app` headers. | **Owner decision (left untouched):** Confirm whether exposing `nandazhafran@gmail.com` publicly in source is acceptable (spam/privacy trade-off), or replace with a FormSubmit random token / project relay email before pushing. |
| **MEDIUM (Owner Decision)** | `MainWindow.Setup.cs` (L64–98); commit `08169fe` | **Yes** | `--developer` mode opens a "Developer tools" window linking `https://github.com/nnavyy/LiveTranslatorOverlay-private/releases` (L80) and Gmail inbox search for `authuser=nandazhafran%40gmail.com` (L81). No secrets or auth tokens are stored; uses user's local browser session. | **Owner decision (left untouched):** Decide whether to remove the `LiveTranslatorOverlay-private` and personal Gmail links (or keep `--developer` tooling as-is / point releases link to public repo). |
| **LOW / OPS (Owner Decision)** | `docs/reviews/2026-11-11-community-terms-pricing-references.md` (L44–53, L62–70); commits `238f39c`, `9ad7c19` | **Yes** | Internal payment-operations review notes discussing selling from Indonesia, evaluating Telegram Wallet / Walt's DeFi Account (no wallet address/seed/private key present), Bank Indonesia rules, and Singapore Stripe eligibility. | **Owner decision:** Safe to publish from a secret standpoint (zero keys/addresses), or trim/move internal business-ops planning out of `docs/reviews/` before making public. |
| **LOW / DOCS** | `docs/reviews/2026-11-10-russian-live-caption-quality.md` (L5, L66, L69); commit `169b860` | **Yes** | Mentions "private candidate" and includes local path `B:\Projects\live translator\offline-research\...`. | Optional cleanup if desired; contains no credentials. |
| **LOW / PATHS** | 19 files in `research/speech/` (`next-accuracy-round.md`, `results/2026-11-04-accuracy-round-prepared/`, etc.) & `docs/reviews/2026-10-31/results.txt` | **Yes** | Local filesystem paths (`C:\Users\akung\...`, `B:\Projects\live translator\...`). | **Owner decision:** Normal for research logs, low sensitivity; sanitize only if you prefer not exposing local Windows username `akung` or drive paths. |
| **LOW / GIT METADATA** | `git log release/ml-1.0` | **Yes** | Commit author/committer metadata contains `Akung <akung@example.com>` (103 commits across `--all`) and `Nanda Zhafran (Wongkiiy) <nandazhafran@gmail.com>` (3 commits). | **Owner decision:** Acceptable if `nandazhafran@gmail.com` may appear in public commit metadata; otherwise rewrite/squash author headers before publishing. |
| **INFO (Unmerged Branches)** | Branches `review/windows-setup-stt`, `docs/supporter-community-handoff` | **No** | `review/windows-setup-stt` contains `scripts/obfuscate-windows.ps1` and `scripts/write-integrity-manifest.ps1` (`7b84cd1`). | Keep off public release branch `release/ml-1.0` (obfuscation contradicts straightforward open-source builds). |
| **CLEAN** | `appsettings.json`, `appsettings.example.json`, `Core/AppSettings.cs` | **Yes** | `appsettings.json` was **never committed** in any of the 106 commits (`0` history hits). `appsettings.example.json` has empty keys. Zero DPAPI blobs (`AQAAANCM...` / `*Protected`) exist in tracked files or git history. | Keep `appsettings.json` in `.gitignore` and keep `scripts/package-windows.ps1` credential verification active. |
| **CLEAN** | `.env*`, certificates, keys, SMTP/Resend, wallets | **Yes** | Zero `.env` files, `.pfx`/`.p12`/`.pem`/`.key`/`.snk`/`.jks` files, SMTP passwords, Resend keys (`re_*`), Stripe keys, or crypto wallet addresses in HEAD or git history. | Maintain `.gitignore` rules added in this prep pass. |
| **CLEAN** | `supabase/`, `Shared/Community/CommunityClient.cs`, `tests/CommunityDatabase/verify.mjs` | **Yes** | Zero real `*.supabase.co` URLs (`YOUR-STAGING-PROJECT.supabase.co` placeholder only); zero `service_role` or `sb_secret_` keys (`CommunityClient` rejects non-`sb_publishable_` keys and non-HTTPS non-loopback URLs). Only `sb_publishable_YOUR_STAGING_KEY` (`supabase/README.md`) and `sb_publishable_test` (`RegressionChecks`) appear. SQL revokes all direct table/schema privileges from `anon`/`authenticated`. | Keep `community.json` out of Git (added to `.gitignore`). |
| **CLEAN** | Binaries, audio, model weights | **Yes** | Zero `.wav`, `.mp3`, `.flac`, `.ogg`, `.bin`, `.onnx`, `.safetensors`, `.exe`, or `.dll` files in HEAD or history. Largest blob in `release/ml-1.0` is `research/speech/results/2026-11-03-russian-training/dataset-index.json` (1.45 MB JSON metadata). One tracked research PDF (`126 KB`). | Continue keeping models/datasets outside Git. |

---

## 3. Dependency & Model License Compatibility Audit

### A. .NET NuGet Dependencies (41 resolved packages across 8 `project.assets.json` files)

| Package(s) | Version | License | MIT Distribution Compatible? | Notes |
| --- | --- | --- | --- | --- |
| `Whisper.net`, `Whisper.net.Runtime`, `Whisper.net.Runtime.NoAvx` | 1.9.1 | **MIT** (`FILE:LICENSE` / `licenses/Whisper.net.txt`) | **Yes** | Wraps `whisper.cpp` and `ggml` (MIT). |
| `NAudio` + transitive (`Core`, `Wasapi`, `Asio`, `Midi`, `WinMM`, `WinForms`) | 3.0.1 | **MIT** | **Yes** | Audio loopback & device enumeration on Windows. |
| `System.Drawing.Common` | 10.0.11 | **MIT** | **Yes** | Windows OCR helper imaging. |
| `System.Security.Cryptography.ProtectedData` | 10.0.14 | **MIT** | **Yes** | Windows DPAPI key protection. |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `SkiaSharp`, `HarfBuzzSharp`, `MicroCom.Runtime`, `Tmds.DBus.Protocol` | 12.1.4 / deps | **MIT** | **Yes** | macOS desktop UI (`MacApp`). |
| `Avalonia.Angle.Windows.Natives` (transitive via `Avalonia.Desktop`) | 2.1.27548.20260419 | **BSD-3-Clause** (`FILE:LICENSE`) | **Yes** | Permissive; ANGLE copyright/license retained in `THIRD_PARTY_NOTICES.md`. |
| Windows Release Packaging: `.NET 10 Runtime` + `Visual C++ CRT DLLs` (`scripts/package-windows.ps1`) | — | .NET = **MIT**; VC++ CRT = **Microsoft Redistributable** | **Yes** (binary release) | CRT DLLs are copied only during `package-windows.ps1` binary packaging (never committed to Git); covered in `THIRD_PARTY_NOTICES.md`. |

### B. Bundled / Runtime-Downloaded / Optional Models & Research Assets

| Asset | Scope | License | MIT Compatible? | Notes |
| --- | --- | --- | --- | --- |
| Whisper `ggml-base.bin` / `ggml-small.bin` (`ggerganov/whisper.cpp`) | Runtime download (`SpeechModelDownload.cs`) | **MIT** | **Yes** | SHA-256 verified on download; notice in `licenses/Whisper-models.txt`. |
| Silero VAD `ggml-silero-v6.2.0.bin` (`ggml-org/whisper-vad`) | Runtime download (`SpeechModelDownload.cs`) | **MIT** | **Yes** | SHA-256 verified on download; notice in `licenses/Silero-VAD.txt`. |
| `distil-whisper/distil-large-v3` | Optional local/GPU (`research/` & `runtime/`) | **MIT** | **Yes** | Not bundled in app release ZIPs. |
| `GigaAM v2_rnnt` & `v3_rnnt` (`salute-developers/GigaAM`) | Optional local/GPU (`research/` & `runtime/`) | **MIT** | **Yes** | Code and checkpoints are MIT; not bundled in app release ZIPs. |
| `facebook/m2m100_418M` (`auralmira/m2m100-418m-ct2-int8`) | Optional local translation (`runtime/translation_worker.py`) | **MIT** | **Yes** | Separately prepared; not bundled in app release ZIPs. |
| `openai/whisper-tiny` | Research pilot only (`research/speech/`) | **MIT** | **Yes** | Not bundled. |
| Python optional deps (`ctranslate2`, `transformers`, `peft`, `sentencepiece`, `opencc`, `torch`, `numpy`, `psutil`, `soundfile`) | Optional local Python worker / CUDA research only | MIT / Apache-2.0 / BSD-3-Clause (`libsndfile` LGPL via `soundfile`) | **Yes** (optional external runtime) | Not bundled into `.NET` release artifacts; noted in `THIRD_PARTY_NOTICES.md`. |
| Research datasets (`FLEURS`, `SOVA RuDevices`, `OpenSLR 12`, `OpenSLR 96 RuLS`, synthetic controls) | Metadata/manifests only in `research/` | CC-BY-4.0 / Public Domain / CC0-1.0 | **Yes** (attribution preserved) | Audio recordings stay outside Git; attribution preserved in `research/speech/` & `THIRD_PARTY_NOTICES.md`. |
| `NLLB-200 distilled 600M` | Documentation mention only (`docs/reviews/2026-11-01-offline-accuracy.md`) | **CC-BY-NC-4.0 (Non-Commercial)** | **Rejected / Not used** | Explicitly flagged as incompatible and **not** downloaded, referenced in code, or bundled. |

---

## 4. History Publication Recommendation

- **Recommendation for `release/ml-1.0`:** **History is clean of secrets/binaries and safe to publish as-is**, unless the owner prefers to scrub `nandazhafran@gmail.com` / `akung` local paths / `2026-11-11` payment-planning notes from commit history before making the repository public.
  - Because `release/ml-1.0` starts from a clean root commit (`2cda7ee`, 2026-09-23), the disconnected `1f362ae` (`TestTranslation.cs`) leak is **not** in `release/ml-1.0` history.
  - If the owner wants a clean single-commit or scrubbed public history anyway (for example, to remove `nandazhafran@gmail.com` from commits `e11d6cf`, `5431924`, `08169fe`, or `docs/reviews/2026-11-11-community-terms-pricing-references.md` commits `238f39c`/`9ad7c19`), squash or run `git filter-repo` **before** pushing `release/ml-1.0` to `origin`.
- **Important warning:** Do **not** push tag `v1.0.0` (commit `1f362ae` line) or `review/windows-setup-stt` to the public remote.

---

## 5. Owner Go / No-Go Checklist Before Publishing

- [ ] **Rotate old Groq key** from disconnected commit `1f362ae` (`gsk_4C32…[REDACTED]`) if not already revoked, and ensure tag `v1.0.0` (`1f362ae` line) is not pushed publicly.
- [ ] **Decide on `nandazhafran@gmail.com`** in `FeedbackWindow.xaml.cs` and `MainWindow.Setup.cs` (keep as public contact/FormSubmit endpoint, or swap to a dedicated alias/FormSubmit hash).
- [ ] **Decide on `--developer` links** in `MainWindow.Setup.cs` (remove or update the `LiveTranslatorOverlay-private/releases` and personal Gmail links).
- [ ] **Decide on internal ops notes** in `docs/reviews/2026-11-11-community-terms-pricing-references.md` and local `C:\Users\akung\` / `B:\Projects\` paths in `research/speech/`.
- [ ] **Decide publish strategy**: push `release/ml-1.0` history as-is (from `2cda7ee`) vs. squash/filter commits first, then merge/update public `main`.
- [ ] **Verify CI green after push**: confirm `.github/workflows/release.yml` and `.github/workflows/ci.yml` pass on GitHub Actions.
- [ ] **Enable GitHub Private Vulnerability Reporting** in repository Settings → Code security and analysis (referenced by `SECURITY.md`, `APP_LICENSE.md`, and `PRIVACY.md`).
- [ ] **Tag release only when ready**: remember pushing a branch updates source only; creating a `v*` tag triggers release asset publishing.
