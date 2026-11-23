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
