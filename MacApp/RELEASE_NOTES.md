# Mac release notes — v1.1.2

## What's new in v1.1.2

- **Developer-Gated Beta Access & Refresh Status**: Added **Refresh access status** to the Community & Support window, restricted the `Beta` channel toggle to developer-verified `$100` Lifetime Insider accounts (`lt_my_account_summary` / `beta_grants`), and hid the owner-only `Dev` channel from standard builds.

## What's new in v1.1.1

- **Login-Optional Donations (`User#XXXXX`) & Rankings Gate**: Custom donations and all core Mac translation features work without sign-in (anonymous `User#10000`–`User#99999` handles stored locally in `guest-donations.dat` with `0600` permissions); email OTP sign-in unlocks Community Rankings and Supporter (`US$19.99`) / Insider (`US$100`) badges.
- **HardwareID Salted Hashing & ECDSA P-256 Provenance (`ml-3`)**: Uses macOS `IOPlatformUUID` (`ioreg -rd1 -c IOPlatformExpertDevice`, hashed one-way with salt — raw UUID never leaves the Mac) and displays salted device hash + running binary SHA-256 alongside `livetranslator.app` OTP messaging and `ml-3` privacy/terms updates.

## What's new in v1.1.0

- **Metal GPU & Auto Tiering Support (`macOS 14+`)**: Bundled `Whisper.net.Runtime.Metal` (`1.9.1`) alongside `Whisper.net.Runtime`, adding **Auto (Metal GPU / CPU)** and **Large-v3-turbo (Metal GPU)** model choices (`ggml-large-v3-turbo-q5_0.bin`, `574 MB`, SHA-256 verified) with automatic fallback to `Balanced` (`ggml-base.bin`) / `Larger model` (`ggml-small.bin`) on CPU.
- **Shared Account Login, Donation Tracker & Update Channels**: Updated the Community & Support window with email OTP account sign-in (with local staging ledger fallback), tracked donations and selectable Lifetime Supporter (`US$19.99`) / Lifetime Insider (`US$100`) offers, solo-developer gratitude notices, and Stable/Beta/Dev release channel preferences.
- **Bug fixes & parity**: Shared Local Agreement-2 stabilizer (`StreamingAgreement`) and Win/Mac `1.1.0` version parity across `.csproj` manifests.

**Unchanged macOS limits:** Screen OCR and automatic in-app self-update remain Windows-only. Prebuilt Mac bundles remain ad-hoc signed, not notarized by Apple.

## Mac downloads (`macOS 14+`)

Choose the archive matching your Mac's architecture from [GitHub Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases):

| Your Mac | Archive |
| --- | --- |
| Apple Silicon (M-series chip) | `LiveTranslatorOverlay-Mac-preview-osx-arm64.zip` |
| Intel processor | `LiveTranslatorOverlay-Mac-preview-osx-x64.zip` |

See [MacApp/INSTALL.md](INSTALL.md) for ad-hoc Gatekeeper instructions.
