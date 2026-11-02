# macOS audio parity and model readiness

Date: 2 November 2026. Reviewed source: `e1c6d8ef3c37b17fa546dddb2097f14361ff91eb`.

## Decision before implementation

**The custom-trained adapter is not ready for application use.** Its pilot test WER did not improve, Russian validation WER worsened, and unsupported text remained on all three non-speech test controls. Keep the adapter rejected for release. There is no verified 95% recognition or translation result.

Pretrained local recognition models can be used for integration experiments without training a new model from scratch. That does not make the complete offline product ready: local translation quality, streaming latency, selected-device capture, packaging and both Mac architectures still need validation. The Windows local CPU probe was slower than real time on its short Russian sample; do not assume it proves suitable live performance on any Mac.

The pretrained neural VAD is a promising component on the small pilot, not a validated solution for hallucinations during speech or speaker attribution. Retain the original speech results and audit unchanged.

Evidence: [training decision](2026-11-01-ml-training.md), [offline feasibility and timings](2026-11-01-offline-accuracy.md), [measured pilot results](../../research/speech/results/2026-11-01-pilot/report.json).

## Paper hold

The owner will provide manuscript templates later. Preserve the existing [paper package](../../research/speech/paper/README.md), PDF, bibliography, data audit and statistics. Do not reformat or expand the manuscript before those templates arrive. New engineering results should be saved separately and incorporated into the manuscript only in the later requested revision. This is a manuscript hold, not approval to distribute the rejected model.

## Confirmed Mac product requirements

- Audio translation is the primary workflow; manual text translation is secondary.
- Offer **Transcribe** (source captions only) and **Translate** (source plus translated captions). Transcription must not depend on a translation provider or target-language model.
- Capture a user-selected playback source. Preserve the earlier local-only/no-API-key requirement; do not substitute microphone recording or silently fall back to a cloud service.
- Keep selectable source language/Auto and target language, with Russian and English as priority recognition languages. All existing target-language choices remain the intended translation coverage, subject to measured validation.
- Show incremental transcript drafts as speech arrives, then stable captions. Interpret “word by word” as progressive display; isolated word substitution is not a reliable translation strategy. Translation needs sufficient phrase context and may revise drafts.
- Provide caption/overlay presentation, finalized history, copy and export. Commit finalized utterances once, avoiding duplicate history from partial updates.
- Support Apple Silicon and Intel separately. Model speed, native libraries, capture permissions, packaging and device behavior must be checked on each architecture.
- Keep research/model controls out of the normal caption workflow. Provide understandable setup, download progress, missing-model errors and device status.

## Current code and missing pieces

The Mac project is at version 1.5.2 and uses Avalonia. `MacApp/TranslatorWindow.cs` implements manual translation and session history. Its About text and release notes explicitly state that audio, OCR and overlays are absent. The shared project currently links text translators and supporting types, not the Windows audio capture or streaming speech services.

Windows capture uses WASAPI through NAudio in `Core/AudioCaptureService.cs`, which cannot simply become macOS capture. Windows streaming partial transcripts come from `Core/DiarizedSttService.cs`; `MainWindow.xaml.cs` distinguishes interim and final events. Groq's `Core/WhisperSttService.cs` instead decodes chunks, with capture currently configured around 4-12 seconds. Porting the UI alone will not produce continuous offline captions.

Use these existing behaviors as requirements and regression references, not as evidence that offline word-level streaming has already been implemented. There is currently no completed Mac local speech-to-translation pipeline to release.

## Native capture investigation

Apple documents [Core Audio taps for outgoing process audio](https://developer.apple.com/documentation/CoreAudio/capturing-system-audio-with-core-audio-taps) and a [`deviceUID` filter for a specific hardware device](https://developer.apple.com/documentation/coreaudio/catapdescription/deviceuid). This is a relevant candidate for the requested output-device selection. [ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos) is another supported audio-capture route, with content filtering and permission requirements.

Before selecting a bridge, verify API availability against the Mac preview's current macOS 14 minimum and the required permission behavior. Do not raise the OS minimum or equate application/system capture with physical-device parity without documenting the decision. Capture must stop cleanly on device removal, permission loss or source changes, with no microphone fallback. Native testing is necessary; this Windows workspace cannot verify Mac audio permission dialogs or real playback capture.

## Implementation sequence and acceptance

1. Build a Mac playback-capture probe with explicit source selection, permission handling, sample-format conversion, stop/restart and device removal checks. Keep this separate from any accuracy claim.
2. Integrate a pretrained local recognizer behind a bounded streaming queue. Track session/utterance IDs, draft/final updates and cancellation so old audio cannot appear after a source or language change. Benchmark model/acceleration options on both Mac architectures before choosing a default.
3. Deliver transcription-only behavior through the caption UI and history. Verify quiet/short utterances, repeated words, silence/music, long playback, queue growth and dropped/duplicated words.
4. Add local translation using a separately evaluated model. Compare translation from gold transcripts with translation from recognized text to isolate error propagation. Validate names, numbers, negation, each target language and Chinese script choices.
5. Package model installation with versioned checksums, cancellation/resume or safe recovery, disk-space reporting and an offline import path. After setup, verify that recognition and translation work with networking disabled.
6. Test the complete capture-to-caption flow on Apple Silicon and Intel. Record p50/p95 stable-caption delay, throughput, memory, device switches and translation quality. Build/smoke tests alone are insufficient for these checks.
7. Publish a Mac release only with accurately described, verified capabilities. Apple signing/notarization is a separate distribution issue from recognition quality and audio support.

This change records the requested scope and answers readiness before implementation. It does not change application binaries, model weights, version numbers or the manuscript.
