# Fully local captions: feasibility and accuracy review

Date: November 1, 2026. Reviewed application: v1.5.1, baseline commit `df03d73`.

## Decision

A mode without API keys is feasible using two local engines: Whisper for speech recognition and a multilingual model for text translation. The most practical first translation candidate is M2M100 418M with integer quantization. It covers the underlying languages in all 34 current target choices; Traditional Chinese additionally needs local script conversion. This is a candidate to evaluate, not a verified accuracy upgrade.

The speech prototype runs on this Windows computer without an API key. Its CPU results are too slow on the short Russian sample to recommend this configuration for continuous live captions. Translation inference, GPU acceleration, real device capture in the prototype, and Mac execution remain untested. No production offline feature or application update is included in this research change.

## Confirmed requirements

- Recognition and translation both work locally after the model files are installed. No subscription, API key or cloud fallback.
- Capture playback from a selectable audio output device. Do not request or silently switch to microphone input.
- Keep the source-language selector, with Russian and English as the accuracy priorities.
- Keep all 34 existing target-language choices, including both Chinese scripts.
- Plan for Windows, Apple Silicon Macs and Intel Macs. The current Mac preview still has no audio capture.
- Keep the current name and branding until the owner approves replacements.

## What runs today

The production audio modes use Deepgram or Groq. Groq sends audio to `whisper-large-v3`. The default Google text translator needs no key but still uses the internet; the public LibreTranslate endpoint is also online. Neither path meets the local-only requirement. Whisper's speech-to-English task alone cannot translate captions into arbitrary target languages.

The existing capture code can select playback devices. Its batch path uses 4–12 second chunks, an 800 ms silence window and 500 ms overlap, with an idle flush for short playback. Groq also has request pacing. A local engine must have its own scheduling: adding local inference behind the existing cloud pacing would preserve avoidable delay. Shorter chunks also reduce recognition context, so reducing every timeout is not an accuracy fix.

Relevant code: [capture](../../Core/AudioCaptureService.cs), [Groq speech](../../Core/WhisperSttService.cs), [Google translation](../../Core/Translation/GoogleFreeTranslateProvider.cs), [language catalog](../../Core/Localization/LanguageCatalog.cs), [additional languages](../../Core/Localization/AdditionalLanguages.cs).

## Methods compared

| Method | Fit for this request | Decision |
| --- | --- | --- |
| Local Whisper through whisper.cpp / Whisper.net | Runs speech recognition locally; native backends exist for Windows and both Mac architectures | Best initial cross-platform integration candidate. Benchmark larger models and acceleration before choosing a default. |
| Microsoft Windows AI Speech Recognition | On-device recognition, but currently experimental and Windows-specific | Track it; do not make the production cross-platform app depend on it. |
| M2M100 418M + CTranslate2 | Local multilingual text translation; MIT model license; int8 inference supported | First translation baseline to test against all current targets. CTranslate2 needs a native bridge or packaged worker for this C# app. |
| Argos Translate | Offline engine with installable language-pair packages | Insufficient alone: the checked package graph has eight missing target choices from both Russian and English. |
| MADLAD-400 3B | Broad multilingual model under Apache 2.0 | Candidate for a higher-resource quality comparison; no memory or latency measurement here. |
| NLLB-200 distilled 600M | Broad multilingual translation, with a noncommercial model license | Do not silently bundle as the unrestricted default for this MIT application. |

Sources: [whisper.cpp](https://github.com/ggml-org/whisper.cpp), [Whisper.net](https://github.com/sandrohanea/whisper.net), [Microsoft API status](https://learn.microsoft.com/en-us/windows/ai/apis/speech-recognition), [M2M100 model card](https://huggingface.co/facebook/m2m100_418M), [CTranslate2 model support](https://opennmt.net/CTranslate2/guides/transformers.html) and [quantization](https://opennmt.net/CTranslate2/quantization.html), [Argos](https://github.com/argosopentech/argos-translate), [MADLAD](https://huggingface.co/google/madlad400-3b-mt), [NLLB license and intended use](https://huggingface.co/facebook/nllb-200-distilled-600M).

Microsoft's current API documentation explicitly describes experimental recognition, including an optional CPU model path. It does not establish a supported, cross-platform API exposing the complete Copilot+ Live Captions translation experience. The product's lack of a visible key is not evidence that any app can reuse its complete engine.

## All current target languages

The coverage checker reads the actual catalog and found 34 choices:

`ar, az, bg, bs, de, el, en, es, fr, hi, hr, id, it, ja, ka, ko, mk, mn, ms, nl, pl, pt, ro, ru, sl, sq, sr, th, tr, uk, uz, vi, zh, zh-TW`.

M2M100 lists Russian, English and the 33 distinct underlying language codes. It does not list `zh-TW` separately. Use its Chinese output with an explicit local conversion to the chosen script, such as [OpenCC](https://github.com/BYVoid/OpenCC). Script conversion does not prove correct Taiwanese phrasing or equal translation quality. Test both Chinese choices separately.

The checked [Argos package index](https://github.com/argosopentech/argospm-index/blob/ff90de60728f7c1338ff6b75974e4c89b2442d22/index.json) has no route, even allowing pivots, from either priority source to `bs, hr, ka, mk, mn, sr, uz, zh-TW`. Adding Chinese script conversion would resolve only the last entry. Coverage is a metadata result; no translation-quality score has been measured.

## Local speech measurements

Hardware: Intel Core i5-13420H, 7.7 GiB physical RAM, Windows 11 build 26300. An RTX 2050 with 4 GiB VRAM is present but was not used. The probe uses Whisper.net 1.9.1, CPU with four threads, quantized multilingual `small-q5_1`, explicit language, temperature zero and no prior-text context. Each file ran twice in one process.

| Sample | Audio length | Inference, pass 1 / 2 | First segment, pass 1 / 2 | Real-time factor, pass 1 / 2 | Peak process memory |
| --- | --- | --- | --- | --- | --- |
| English, upstream JFK sample | 11.00 s | 6.13 / 6.36 s | 6.10 / 6.33 s | 0.56 / 0.58 | 534 MiB |
| Russian, sherpa test sample | 3.55 s | 6.61 / 7.62 s | 6.58 / 7.59 s | 1.86 / 2.15 | 514 MiB |

Model loading took 1.11 seconds for the English process and 0.50 seconds for Russian. The model file is 190,085,487 bytes, about 181 MiB. Peak process memory excludes a translation model and the production application.

A real-time factor above 1 means processing took longer than the clip. These batch results exclude capture buffering, translation and UI rendering. Two clips cannot establish general accuracy or sustained throughput. The Russian sample has no verified reference transcript in this evaluation, so it was not scored for word errors. No user audio was recorded. No comparison against Groq or Copilot+ was run.

The smaller local model is not a justified accuracy upgrade over the existing cloud `large-v3`. Next, compare accelerated `large-v3` / `large-v3-turbo` and smaller models on identical Russian and English recordings. Keep model quality and runtime speed as separate measurements. Larger models may exceed the practical memory budget when recognition, translation and a game run together.

The [probe and reproduction instructions](../../tests/LocalSpeechProbe/README.md) and [raw results](2026-11-01-offline-probe-results.json) are committed. Public audio sources: [JFK WAV](https://github.com/ggml-org/whisper.cpp/blob/master/samples/jfk.wav) and [sherpa language-identification samples](https://k2-fsa.github.io/sherpa/onnx/spoken-language-identification/pretrained_models.html). Exact hashes are in the results file. Downloaded weights and recordings are outside the repository.

## Integration work needed before release

1. **Prove the complete pipeline.** Benchmark local translation, then recognition and translation together. Use CTranslate2's quantized model support; measure conversion/package size and runtime memory instead of estimating from the model name. Evaluate all target directions from both priority sources. Choose a stronger translation model if the baseline loses meaning.
2. **Handle playback sources explicitly.** Reuse Windows render-device loopback and resampling. List only playback sources in this mode; when a device disappears, stop with a clear message instead of falling back to a microphone. For Mac, implement and test system/app audio capture with ScreenCaptureKit and the required permission flow. A selectable physical-output-device experience needs separate validation on Mac; do not promise Windows device parity from inference compatibility alone.
3. **Preserve speech and meaning.** Evaluate speech VAD, sentence boundaries, overlap deduplication and short prior-context windows. Keep source language fixed when selected. Test names, numbers, negation, slang, quiet speech, music, interruptions and mixed Russian/English. Avoid broad filters that remove valid short phrases. A [local VAD](https://github.com/snakers4/silero-vad) can be evaluated, but voice activity detection is not speaker identity.
4. **Keep captions and history consistent.** Show unstable recognition as a draft, then commit finalized source/translation pairs once. Preserve actual repetitions and changed numbers. Cancel stale results when the language or device changes. An unavailable model must show an actionable error, never an untranslated sentence presented as successful translation.
5. **Make local mode self-contained.** Install versioned model packs with checksums, sizes, licenses, progress, cancellation and recovery from interrupted downloads. Allow installing copied model files for offline machines. No automatic audio/text requests to Google, Groq or public LibreTranslate. Feedback submission and update downloads remain separate, explicit online actions; history must still work without a connection.
6. **Package and validate each platform.** Test Windows dependencies, Apple Silicon and Intel packages separately. The evaluated Whisper.net runtime documents Windows 11 / Server 2022 as prerequisites, while the app currently supports Windows 10. Resolve that mismatch or clearly scope the optional mode before release. Mac audio support and Developer ID notarization remain separate unfinished work.
7. **Publish only verified behavior.** Release notes and the in-app What's New notice should distinguish offline availability, measured performance, supported targets and any platform limits. Announce accuracy improvement only with a repeatable comparison. Speaker labels need their own diarization evaluation; local Whisper alone does not solve who-is-speaking accuracy.

Apple reference for the capture work: [ScreenCaptureKit sample and permission flow](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos).

## Acceptance checks

- With network access blocked after setup: Russian and English playback produce captions in every current target choice; missing models produce an error with no cloud request.
- Switching devices, disconnecting headphones, stopping capture and changing source/target languages cannot display stale output or capture a microphone.
- A licensed, reference-transcribed set of speech clips measures word errors separately from translation meaning. Native-language review covers names, numbers and negation. Use the same clips and settings for any comparison with the current cloud mode.
- A sustained playback run measures caption delay, queue growth, lost/duplicated words, RAM/VRAM and UI responsiveness. Faster-than-real-time speech inference alone is insufficient.
- Silence/music tests check invented captions. Multi-speaker recordings check attribution separately. History contains each finalized caption once.
- Windows and both Mac architectures pass their own device-capture, model-load and offline checks. Compilation alone is not a platform pass.

## Verification in this research change

The local speech probe built with zero warnings/errors and produced both English and Russian text in four CPU runs. Invalid arguments returned exit code 2. The coverage script successfully checked the actual 34-entry catalog against pinned upstream metadata. Translation inference and quality, GPU execution, live capture in the probe, and Mac execution were not tested.

The production Release build succeeded with no errors and the existing `NU1510` warning about `System.Drawing.Common`. Existing checks passed: RegressionChecks 12/12, AuditChecks 9/9, SpeakerChecks 13/13 (34 total). These tests protect current behavior; they are not evidence of local translation quality. This report does not change the application's version or claim that the requested offline mode is already available.
