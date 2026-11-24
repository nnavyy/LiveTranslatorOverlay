# Mac release notes — v1.1.6

## What's new in v1.1.6

- **All-Tier Metal GPU Acceleration & Beam-2 Speech Accuracy**: Shared `LocalSpeechService` now unlocks Metal GPU acceleration across `Balanced (GPU / CPU)` (`ggml-base.bin`), `Higher Accuracy (GPU / CPU)` (`ggml-small.bin`), `Auto (GPU / CPU)`, and `High Accuracy (GPU)` (`ggml-large-v3-turbo-q5_0.bin`) with automatic CPU fallback and Beam-2 GPU decoding.
- **Continuous Clause Boundary Deduplication & Faster Translation**: `TranslatorWindow.Audio` applies `HallucinationDetector.StripWhisperArtifacts` and `SentenceBoundaryDeduplicator.StripOverlapPrefix` across consecutive 4-second native audio chunks, paired with batched anti-repetition offline M2M-100 translation (`repetition_penalty=1.15`, `no_repeat_ngram_size=3`) and `SocketsHttpHandler` HTTP/2 pooling on `GoogleFreeTranslateProvider` and `GroqTranslateProvider`.

## What's new in v1.1.5

## What's new in v1.1.5

- **Version Parity & Sentence Stability**: Synchronized `1.1.5` release parity alongside Windows per-sentence caption smoothing while retaining shared `LocalSpeechService` VAD speech-span trimming and `HallucinationDetector` watermark filtering.

## What's new in v1.1.4

- **Whisper Hallucination Fix & Faster Speech Decoding**: Shared `LocalSpeechService` now trims audio to Silero VAD speech spans before Whisper inference, disables high-temperature fallback loops (`WithTemperatureInc(0)`), filters high `NoSpeechProbability` segments, and activates `HallucinationDetector.StripWhisperArtifacts` to drop fansub watermarks such as `Subtitle by DimaTorzok` cleanly.
- **Accelerated Translation Providers**: Shared `LocalTranslateProvider` (`fastInterim` beam-1 mode) and `GoogleFreeTranslateProvider` bounded phrase caching reduce repeat translation overhead.

## What's new in v1.1.3

- **Clean UI & Support Tab Refinement**: Removed redundant badge pills from the Support & Account cards and profile popup, tightened speech model names (`Balanced (CPU)`, `Higher Accuracy (CPU)`, `Auto (Metal GPU / CPU)`, `High Accuracy (GPU)`), and removed the internal `Dev` option from the user-facing Release Channel picker.
- **User Profile & Local Rankings Preview**: Added an inline User Profile card (`UserProfileStore`, `lt_my_profile` / `lt_save_profile`) and local device standings preview (`BuildLocalPreviewPage`) ahead of live global rankings.

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
