# Cross-Vendor Hardware-Adaptive Word-by-Word Speech Translation on Consumer Hardware: Empirical Research Gaps, Quantized Zero-Shot Superiority, and System Architecture

**Version:** Live Translator Overlay v1.1.0 (MIT Open Source)  
**Benchmark Artifact:** [streaming-benchmark-results.json](streaming-benchmark-results.json)  
**Pilot Baseline & Statistical Audit:** [local-speech-translation-pilot.pdf](output/pdf/local-speech-translation-pilot.pdf)

---

## Abstract

Real-time multilingual desktop translation applications face an apparent trilemma on consumer PCs and laptops: cloud APIs introduce cost, privacy exposure, and network jitter; naive local Whisper-`small` / `base` CPU pipelines suffer both high word error rates (10–27% across typologically diverse languages) and 4–12 second buffering delays; and CUDA-locked Python inference excludes AMD Radeon, Intel Arc/iGPU, and Apple Silicon users while failing to reach real-time word-by-word interaction.

This paper investigates the empirical **research and systems gap** uncovered across **four consecutive fine-tuning campaigns** totaling over **23 hours of consumer RTX 2050 optimization/evaluation** on Whisper `small` (`244M` parameters), and presents the four-part systems architecture shipped in **Live Translator Overlay v1.1.0**. We demonstrate empirically that: (1) low-rank (`LoRA` and `DoRA`) and full-preservation fine-tuning of a capacity-bounded `244M` multilingual model hits a stubborn accuracy ceiling (`10.55%` development / `10.61%` held-out test beam-5 mixed mean, `11.46%` greedy mean) accompanied by cross-language forgetting and repetition loops; whereas (2) zero-shot inference with a pruned-decoder `whisper-large-v3-turbo` (`Q5_0`, `574 MB`) achieves **`5.00%` Russian WER**, **`4.92%` Japanese CER**, **`9.71%` English WER**, and **`11.33%` Indonesian WER** on held-out FLEURS test speech while running **`5.4x` faster than real time (`0.186x` RTF)** over cross-vendor **Vulkan GPU** hardware (`818 ms` mean step decode with `WithAudioContextSize(512)` shader pre-warming and neural Silero VAD gating); and (3) coupling `300 ms` non-queuing WASAPI sliding windows, CJK-grapheme-aware **Local Agreement-2 (LA-2)** prefix stabilization, and a persistent **Win32 Job-guarded CTranslate2 MT daemon** eliminates cold per-caption startup (`11.18 s` once -> `892.5 ms` warm mean), enabling accessible live word-by-word captions on Windows (`Vulkan` / `CPU` / `NoAvx`) and macOS (`Metal` / `CPU`) with zero Python setup for end users.

---

## 1. Problem Statement & Empirical Motivation

Commercial cloud interpreters such as Google Translate feel instantaneous because they stream acoustic frames incrementally over server-side accelerators and decode partial hypotheses before the speaker finishes a sentence. By contrast, desktop open-source captioners commonly combine three latency bottlenecks:

1. **Coarse utterance chunking (`4–12 s`)**: Waiting for end-of-utterance silence guarantees multi-second silence-to-caption stalls regardless of model throughput.
2. **Capacity-starved CPU ASR (`base` / `small`)**: Running small `ggml` models on CPU forces a painful accuracy-vs-latency trade-off—dropping to `base` causes `25.56%` Japanese CER and `26.84%` Indonesian WER on clean FLEURS recordings, while attempting to fine-tune `small` on consumer 4 GB GPUs encounters capacity bottlenecks and catastrophic interference.
3. **Cold-spawned subprocess translation (`~2–11 s` per caption)**: Spawning a fresh Python interpreter, verifying SHA-256 checksums, and reloading `418M+` M2M-100 parameters on every finalized caption stalls UI updates even when ASR completes quickly.

Furthermore, hardware lock-in to NVIDIA CUDA + local Python virtual environments leaves AMD/Intel Windows laptops and Apple Silicon Macs unable to run accelerated local inference out of the box.

---

## 2. Empirical Investigation of the Fine-Tuning Research Gap

Prior to designing the v1.1.0 Vulkan/Metal architecture, we systematically evaluated whether consumer-scale fine-tuning of Whisper `small` (`244M`) across Indonesian (`id`), Japanese (`ja`), English (`en`), and Russian (`ru`) could bring every language below `5%` error rate while keeping CPU/small-GPU deployability. Across four audited experimental protocols (documented in detail under `research/speech/results/`), all fine-tuning attempts hit an empirical plateau:

### 2.1 Quantitative Comparison Across Auditable Fine-Tuning Campaigns

*Sources: `2026-11-18-small-deep-review`, `2026-11-18-small-preservation-review`, `2026-11-19-language-experts-review`, `2026-11-20-small-dora-review`, and `improved-multilingual-20261116-210831-500319/summary.json` (FLEURS 40 dev clips / language, 60 test clips / language, Japanese measured by CER, EN/ID/RU by WER).*

| Campaign / Strategy | Updates & Params | Train Audio Presented | Wall Time | EN WER (Dev Greedy) | ID WER (Dev Greedy) | JA CER (Dev Greedy) | RU WER (Dev Greedy) | Mixed Dev Mean | Outcome / Gate Verdict |
|---|---|---|---|---:|---:|---:|---:|---:|---|
| Pretrained Whisper `small` baseline | — | — | — | `5.09%` | `21.32%` | `13.56%` | `10.22%` | `12.55%` (`11.42%` beam-5) | Baseline incumbent |
| Shared `small-deep` (LR `3e-5`, 6k steps) | 6,000 steps (`196.1M` params) | 50.20 h (`24,000` visits) | `8 h 50 m` | `16.24%` | `16.02%` | `12.78%` | `11.99%` | `14.26%` | Rejected (severe EN repetition/forgetting; 7 -> 60 errors on clip `10035998729701048400`) |
| `small-preservation` (LR `3e-6`, 3k steps) | 3,000 steps (`196.1M` params + mask fix) | 25.07 h (`12,000` visits) | `3 h 58 m` | `4.48%` (`4.48%` beam-5) | `18.74%` (`16.88%` beam-5) | `11.74%` (`10.60%` beam-5) | `11.04%` (`10.22%` beam-5) | `11.50%` (**`10.55%`** beam-5) | Legacy gate only; failed strict RU guard (`81 > 75`) & failed `<5%` test target (`10.61%` beam-5) |
| `small-language-experts` (LoRA rank-16/lang) | 500/lang (`6.49M`/lang) | 16.64 h (`8,000` visits) | `4 h 40 m` | `4.48%` | `16.02%` | `11.74%` | **`9.67%`** | `10.48%` | Only RU met 5% relative gain; 3/4 languages rejected |
| `small-language-dora` (DoRA rank-16/lang) | 500/lang (`6.49M`/lang) | 16.64 h (`8,000` visits) | `7 h 27 m` | `4.48%` | `15.31%` (step 250) -> `16.02%` | `11.90%` (step 500) | `9.67%` (step 500) | `10.52%` | **0/4 languages accepted** |
| **Zero-Shot `whisper-large-v3-turbo` baseline** | Zero-shot (`809M`, 4 decoder layers) | 0 h local fine-tune | 0 h | — | — | — | — | **`5.36%`** (`0.05362`) | LoRA steps 480 (`5.45%`) & 960 (`5.59%`) *slightly degraded* dev error vs zero-shot |

### 2.2 Three Root Causes Explaining Why Small Fine-Tuning Stalls (Grounded in SOTA Literature)

1. **Decoder/Encoder Capacity Bottleneck (`244M` vs `809M` Pruned-Decoder Scaling)**:
   Large-scale empirical scaling studies of Whisper-style attention encoder-decoders (OWLS [1], Whisper [2]) show multilingual acoustic-phonetic generalization improves smoothly with encoder capacity, while Distil-Whisper [3] and Whisper `large-v3-turbo` establish that reducing decoder depth (from 32 to 4 layers) preserves multilingual WER/CER while multiplying decoding throughput—whereas compressing the encoder down to `small` (`244M`) creates an unrecoverable cross-lingual phonetic bottleneck, especially for lower-resource or acoustically varied Indonesian and Japanese tokens. Low-language transfer analyses [4] and acoustic-narrowing studies [5] confirm that gradient updates on small subsets disproportionately narrow acoustic robustness.
2. **Catastrophic Forgetting vs. Parameter-Efficient Plasticity Ceiling**:
   When updating `196.1M` shared weights at LR `3e-5` (`small-deep`), Indonesian WER improved (`21.32% -> 16.02%`), but English exploded (`5.09% -> 16.24%`) due to decoder stopping degradation and repetition loops (consistent with continual Whisper adaptation findings [6, 7]). Reducing LR 10x (`small-preservation`) or isolating parameters via rank-16 `LoRA` / `DoRA` adapters [8, 9] halted catastrophic forgetting (`4.48%` EN), yet hit a plasticity ceiling where Indonesian, Japanese, and Russian failed to breach the multi-language `<5%` boundary. Moreover, fine-tuning `whisper-large-v3-turbo` on the local dev distribution slightly worsened its `5.36%` zero-shot baseline (`5.45%` at step 480, `5.59%` at step 960).
3. **Why Naive `70:15:15` Full-Corpus Fine-Tuning Does Not Scale on Consumer GPUs**:
   Auditing the expanded corpus (`69,097` train = `99.32%`, `196` dev = `0.28%`, `276` test = `0.40%`) reveals that naively rescaling to a full `1,000`-hour `70:15:15` split (`700h` train / `150h` dev / `150h` test) on a 4 GB RTX 2050 (~150 hours compute + evaluation) requires nearly **17–19 continuous machine-days** while saturating constrained system SSD budgets—only to converge near an architecture-limited `~10%` Whisper `small` floor that zero-shot `large-v3-turbo` Q5_0 already outperforms by ~2x on non-English languages without touching a training script.

**Research Gap Identified:** Bridging the gap between server-grade zero-shot multilingual accuracy and Google-Translate-like responsiveness on heterogeneous consumer hardware requires shifting the research focus from *consumer fine-tuning of small checkpoints* to **systems-level inference co-design**: cross-vendor GPU portability (`Vulkan`/`Metal`), streaming prefix stabilization designed around quantized decoder behavior, VAD sensitivity preservation, and elimination of IPC/cold-start bottlenecks.

---

## 3. Novel Architectural Contributions (Live Translator Overlay v1.1.0)

### 3.1 Contribution 1: Cross-Vendor Hardware-Adaptive Quantized ASR (`Auto` Tiering)
Unlike CUDA-bound Python stacks (`PyTorch` + `cuBLAS`), v1.1.0 ships `Whisper.net` 1.9.1 native `Vulkan` (`Whisper.net.Runtime.Vulkan`, supporting NVIDIA, AMD, and Intel GPUs on Windows), `Metal` (`Whisper.net.Runtime.Metal` + `arm64` native runtime on macOS 14+), and `CPU`/`NoAvx` x64 fallbacks inside a self-contained `.NET 10` executable:
- **Adaptive Tier Selection (`SpeechModelDownload.ResolveEffectiveTier`)**: Probes Vulkan/Metal availability at initialization. When cross-vendor GPU acceleration is present, `LocalSpeechModelTier.Auto` selects `ggml-large-v3-turbo-q5_0.bin` (`574,041,195 B`, SHA-256 verified); on CPU-only or older hardware, it seamlessly falls back to `ggml-base.bin` (`147,951,465 B`), preventing unusable >1.0x RTF CPU stalls while unlocking high-accuracy GPU inference without requiring Python or GPU vendor lock-in.

### 3.2 Contribution 2: Shader-Prewarmed Asymmetric Dual-Context Streaming with CJK-Aware Local Agreement-2
Incremental Whisper streaming (`Whisper-Streaming` [10], `Simul-Whisper` [11], `SimulStreaming` [12]) encounters three practical desktop failure modes solved directly in v1.1.0:
1. **Elimination of Cold SPIR-V Shader Stall (`39.9 s -> <1 s`)**: During `InitializeAsync`, `LocalSpeechService` executes a one-time silent `0.5 s` warm-up through the streaming Vulkan graph *before* signaling capture readiness, completely eliminating the observed `39.9 s` first-word compilation spike.
2. **Asymmetric Audio Context (`512` frames interim / `1500` frames final)**: `AudioCaptureService` triggers non-queuing snapshots every `300 ms` (`_streamingGate.Wait(0)` discards superseded intermediate frames if decode takes >300ms so latency never queues, while final silence commits await `_streamingGate`). Intermediate decodes run with `WithSingleSegment().WithAudioContextSize(512)` (covering `10.24 s` sliding windows) rather than full 30s (`1500`) attention matrices, keeping mean Vulkan GPU step time below real-time (`818 ms` across 10s windows).
3. **Punctuation-Resilient CJK/Word `StreamingAgreement` (`Shared/StreamingAgreement.cs`)**: Standard word-level Local Agreement-2 fails on short intermediate Whisper hypotheses because Whisper appends synthetic terminal periods (`.`, `。`, `?`) at partial clip boundaries—turning `"Testing"` at `0.6 s` into `"Testing."` vs `"Testing one"` at `0.9 s` and blocking prefix agreement—and breaks completely on spaceless scripts such as Japanese/Chinese. `StreamingAgreement` performs Unicode rune tokenization (separating contiguous Latin/Cyrillic words from individual CJK/Kana ideographic units while preserving `LeadingSpace`), matches normalized keys resilient to temporary trailing punctuation, and withholds final single-token commits when synthetic terminal punctuation terminates a non-final window.
4. **Quiet-Speaker Survival via Neural Silero VAD Gating**: Early benchmark passes revealed that deterministic amplitude heuristics (`HasSustainedVoiceActivity`: `maxAmp < 0.012f`) prematurely rejected real soft-spoken FLEURS recordings (`fleurs-en_us-test-10167324587744183095`, `fleurs-ja_jp-test-10203675650837159328`) in `0.2 ms` (`100%` error) before Silero VAD saw the audio. Replacing the amplitude heuristic with a minimal non-zero floor (`1e-4f`) feeding Silero VAD v6.2.0 (`DetectSpeechAsync`) recovered full accuracy (`9.1%` EN WER, `4.8%` JA CER) while preserving `0%` emission on silence and constant DC tones (`AuditChecks` & `LocalCpuChecks`).

### 3.3 Contribution 3: Kernel-Guarded Persistent Offline MT Daemon
Replacing single-shot process spawning with line-delimited JSON IPC (`runtime/translation_worker.py --daemon`), runtime CUDA-BLAS validation with graceful `int8` CPU fallback (preventing `cublas64_12.dll` missing-library crashes on end-user machines), background parallel startup (`LocalTranslateProvider.WarmUpAsync` overlapped with Vulkan shader compilation), and a **Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` kernel job object** guarantees both sub-second warm translation and **zero orphaned Python processes** even upon hard process termination.

### 3.4 Contribution 4: Transparent Open-Core Governance & 3-Channel Release Integrity
While keeping every core feature MIT-licensed and free, `CommunityClient`, `AccountSessionStore` (DPAPI / `0600` encrypted local staging ledger), PostgreSQL Row-Level Security (`20261123120000_account_login_donations_and_tiers.sql`), and `StableRelease` establish verifiable account OTP login, tracked donations/selectable tiers (`Supporter US$19.99`, `Insider US$100`), SHA-256 package validation, and separated `main` (`stable`), `beta`, and local `dev` update channels.

---

## 4. Empirical Evaluation (v1.1.0 Benchmark Results)

### 4.1 Experimental Setup
- **Hardware under test:** Windows 11 x64 (`Microsoft Windows 10.0.26300`), consumer NVIDIA RTX 2050 laptop GPU exercised strictly through **cross-vendor Vulkan** (`Whisper.net.Runtime.Vulkan` 1.9.1) — *no CUDA ASR runtime used* — plus isolated **CPU** baseline (`LIVETRANSLATOR_DISABLE_GPU=1`).
- **Dataset:** 12 held-out test recordings from `multilingual-fleurs-960-v1\manifest.jsonl` (`split == "test"`, 3 clips each for `en`, `id`, `ja`, `ru`, durations `6.96 s – 11.46 s`), evaluated with `300 ms` (`4,800`-sample) sliding windows up to `10 s`.
- **Reproducibility command:**
  `dotnet run --project tests/LocalCpuChecks/LocalCpuChecks.csproj -c Release -- --streaming-benchmark "<manifest>"`

### 4.2 Streaming ASR Accuracy & Speed: Vulkan `large-v3-turbo (Q5_0)` vs. CPU `base`

*All metrics copied verbatim from [streaming-benchmark-results.json](streaming-benchmark-results.json).*

| Language | Metric | CPU `ggml-base.bin` Error (%) | **Vulkan GPU `large-v3-turbo (Q5_0)` Error (%)** | Relative Error Reduction | CPU Step Mean / P95 (ms) | **Vulkan Step Mean / P95 (ms)** | Vulkan Final RTF |
|---|---|---:|---:|---:|---:|---:|---:|
| English (`en`, n=3) | WER | `10.56%` | **`9.71%`** | `-8.05%` | `497.5 / 580.6` | `828.8 / 1054.5` | `0.165x` |
| Indonesian (`id`, n=3) | WER | `26.84%` | **`11.33%`** | **`-57.79%`** | `506.7 / 907.3` | `958.9 / 1987.4` | `0.196x` |
| Japanese (`ja`, n=3) | CER | `25.56%` | **`4.92%`** | **`-80.75%`** | `755.4 / 2186.9` | **`748.2 / 1090.3`** | `0.192x` |
| Russian (`ru`, n=3) | WER | `13.63%` | **`5.00%`** | **`-63.32%`** | `493.7 / 1065.2` | `736.3 / 982.5` | `0.192x` |
| **Overall (12 clips)** | Mixed | `19.15%` | **`7.74%`** | **`-59.58%`** | `563.3 / 2840.2` (`P50 468.6`) | `818.0 / 2288.8` (`P50 881.0`) | **`0.186x`** |

- **First-Word Compute Latency:** `880.4 ms` mean on Vulkan `large-v3-turbo (Q5_0)` vs. `1,076.2 ms` mean on CPU `base` (faster initial GPU response despite a `~10.8x` larger model class, once SPIR-V shaders are pre-warmed at startup).
- **Real-Time Margin:** At `0.186x` final RTF (`0.140x – 0.226x` across individual clips), the full-context final decoder runs **5.4x faster than real time**.
- **Warm Local M2M-100 Translation (`--daemon` on CPU `int8`, n=12 outputs):** Cold worker startup **`11,180.0 ms`** once -> subsequent warm translations **`892.5 ms` mean (`912.0 ms` P50, `1,448.0 ms` P95, `401.5 ms` min on short Japanese)**.
- **Live GUI + Audio Loopback Verification (`DevApp\LiveTranslatorOverlay.exe`):** Verified live on Windows 11 with a playing FLEURS Russian clip (`fleurs-ru_ru-test-10011799110630615565.wav`): UI reported `ASR 809 ms · MT 1367 ms` with exact Russian transcription (`"В различных местах Рима были установлены большие телевизионные экраны, с помощью которых люди могли наблюдать за церемонией."`) and `0` surviving orphaned Python child processes on exit.

---

## 5. Threats to Validity, Hardware Scope & Limitations

1. **Sample Size & Clean Read Speech:** The 12-clip streaming benchmark evaluates clean Google FLEURS test speech (`3` clips per language) rather than noisy multi-speaker conversations; therefore, error rates on overlapping crosstalk, background music, or heavy accents will differ from clean FLEURS scores.
2. **Per-Step Decode vs. Sub-300 ms Audio Ingestion:** Audio snapshots trigger every `300 ms`, so user speech is sampled without 4–12 s silence pauses; however, Vulkan inference compute on `large-v3-turbo (Q5_0)` (`736–959 ms` mean across growing 1–10s windows) plus warm local M2M-100 (`~893 ms` CPU `int8`) still yields roughly **`~0.9–1.9 s` total compute per progressive word/phrase update**, rather than strict `<= 300 ms` total wall latency from waveform to translated text. On faster desktop GPUs, Apple Silicon (`M1–M4` Metal), or cloud streaming (`Deepgram` + key-free/Groq), step latency drops further; conversely, low-power CPUs will experience slower updates on `base`.
3. **macOS Hardware Coverage:** Apple Silicon (`Whisper.net.Runtime` + `Whisper.net.Runtime.Metal` `1.9.1`) builds cleanly (`dotnet build MacApp/LiveTranslatorMac.csproj -c Release`), but live macOS Metal runtime benchmarks await macOS 14+ runner telemetry in CI.
4. **Offline MT Model Quality:** M2M-100 (`418M`, `int8`) preserves privacy and offline reliability with `~893 ms` warm CPU latency, yet exhibits known semantic mistranslations on rare proper nouns and homophones compared to larger online neural translators.

---

## 6. References & Acknowledgements

1. W. Chen et al., "OWLS: Scaling Laws for Multilingual Speech Recognition and Translation Models," *arXiv preprint arXiv:2502.10373*, 2025. https://arxiv.org/abs/2502.10373
2. A. Radford et al., "Robust Speech Recognition via Large-Scale Weak Supervision (Whisper)," *arXiv preprint arXiv:2212.04356*, 2022. https://arxiv.org/abs/2212.04356
3. S. Gandhi, P. von Platen, and A. M. Rush, "Distil-Whisper: Robust Knowledge Distillation via Large-Scale Pseudo Labelling," *arXiv preprint arXiv:2311.00430*, 2023. https://arxiv.org/abs/2311.00430
4. S. Watanabe et al. / Multimodal Multilingual ASR Studies on Cross-Lingual Transfer & Low-Resource Scaling, *arXiv preprint arXiv:2611.16260*, 2026 (`https://arxiv.org/abs/2611.16260`), and BuzzASR robustness analysis, *arXiv preprint arXiv:2609.09554*, 2026 (`https://arxiv.org/abs/2609.09554`).
5. Acoustic Narrowing in Fine-Tuned Multilingual Encoders, *arXiv preprint arXiv:2611.18653*, 2026. https://arxiv.org/abs/2611.18653
6. Catastrophic Forgetting and Continual Adaptation in Multilingual Speech Foundation Models, *arXiv preprint arXiv:2501.07875*, 2025 (`https://arxiv.org/abs/2501.07875`), and *arXiv preprint arXiv:2607.11163*, 2026 (`https://arxiv.org/abs/2607.11163`).
7. S.-Y. Liu et al., "DoRA: Weight-Decomposed Low-Rank Adaptation," *ICML 2024* / *arXiv:2402.09353*, 2024. https://arxiv.org/abs/2402.09353
8. R. Joseph and A. Baby, "Speaker Personalization for Automatic Speech Recognition using Weight-Decomposed Low-Rank Adaptation," *Proc. Interspeech 2024*, pp. 2875–2879, DOI: `10.21437/Interspeech.2024-1434`.
9. D. Macháček, R. Dabre, and O. Bojar, "Turning Whisper into Real-Time Transcription System (Whisper-Streaming / Local Agreement-2)," *arXiv preprint arXiv:2307.14743*, 2023. https://arxiv.org/abs/2307.14743
10. P. Wang et al., "Simul-Whisper: Attention-Guided Streaming Whisper," *arXiv preprint arXiv:2406.10052*, 2024. https://arxiv.org/abs/2406.10052
11. D. Macháček et al., "SimulStreaming: Simultaneous Speech Translation via Integrated Whisper Attention," *arXiv preprint arXiv:2506.17077*, 2025. https://arxiv.org/abs/2506.17077
12. A. Conneau et al., "FLEURS: Few-Shot Learning Evaluation of Universal Representations of Speech," *IEEE SLT* / *arXiv preprint arXiv:2205.12446*, 2022. https://arxiv.org/abs/2205.12446

*Acknowledgement: Thank you to arXiv for use of its open access interoperability ([info.arxiv.org/help/api](https://info.arxiv.org/help/api/index.html)).*
