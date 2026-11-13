# Third-party notices and license inventory

Live Translator Overlay source code in this repository is licensed under the [MIT License](LICENSE). Third-party libraries, runtimes, speech/translation models, and research datasets retain their own licenses and notices below.

## 1. Bundled and .NET dependencies

All NuGet dependencies resolved by `LiveTranslatorOverlay.csproj`, `Shared/LiveTranslatorOverlay.Shared.csproj`, `MacApp/LiveTranslatorMac.csproj`, `AudioDiag/AudioDiag.csproj`, and `tests/` use permissive MIT-compatible licenses:

- **Whisper.net, Whisper.net.Runtime, Whisper.net.Runtime.NoAvx (1.9.1)** — MIT License (`licenses/Whisper.net.txt`, Copyright (c) 2024 sandrohanea), wrapping `whisper.cpp` and `ggml` (MIT License).
- **NAudio (3.0.1)** (`NAudio.Core`, `NAudio.Wasapi`, `NAudio.Asio`, `NAudio.Midi`, `NAudio.WinMM`, `NAudio.WinForms`) — MIT License (Copyright Mark Heath).
- **System.Drawing.Common (10.0.11)** and **System.Security.Cryptography.ProtectedData (10.0.14)** — MIT License (Copyright Microsoft Corporation).
- **Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent (12.1.4)** (`MacApp`) and transitive **SkiaSharp (3.119.4)**, **HarfBuzzSharp (8.3.1.3)**, **MicroCom.Runtime (0.11.6)**, **Tmds.DBus.Protocol (0.94.1)** — MIT License.
- **Avalonia.Angle.Windows.Natives (2.1.27548.20260419)** (transitive package in `MacApp`) — BSD-3-Clause License (Copyright 2018 The ANGLE Project Authors).
- **Microsoft .NET 10 Runtime & Visual C++ Redistributable CRT DLLs** — Self-contained Windows builds (`scripts/package-windows.ps1`) bundle the .NET runtime (MIT) and Microsoft Visual C++ CRT DLLs for application-local execution. Upstream Microsoft redistribution terms apply to the Visual C++ CRT binaries.
- **Platform OS APIs (linked dynamically at runtime)** — Windows WASAPI, `Windows.Media.Ocr`, `SetWindowDisplayAffinity`, and macOS `ScreenCaptureKit` / `AVAudioEngine` / `AppKit` (`MacApp/NativeAudio.swift`).

## 2. Runtime-downloaded models

These models are not committed to Git or bundled into release archives. The application downloads them on first use over HTTPS and verifies fixed SHA-256 checksums before loading:

- **Whisper `ggml-base.bin` & `ggml-small.bin`** (OpenAI Whisper weights converted by `ggerganov/whisper.cpp`) — MIT License (`licenses/Whisper-models.txt`, Copyright (c) 2022 OpenAI).
- **Silero VAD `ggml-silero-v6.2.0.bin`** (`ggml-org/whisper-vad` / `snakers4/silero-vad`) — MIT License (`licenses/Silero-VAD.txt`, Copyright (c) 2020-present Silero Team).

## 3. Optional locally prepared models and Python workers

Optional scripts under `runtime/` and `research/speech/` are not bundled into the Windows or macOS application packages:

- **Distil-Whisper `distil-large-v3`** — MIT License (`https://huggingface.co/distil-whisper/distil-large-v3`).
- **GigaAM `v2_rnnt` / `v3_rnnt`** — MIT License (`https://github.com/salute-developers/GigaAM`).
- **M2M100 418M (`facebook/m2m100_418M` & `auralmira/m2m100-418m-ct2-int8`)** — MIT License.
- **OpenAI `whisper-tiny`** (research pilot) — MIT License.
- **Optional Python worker & research packages** (`runtime/speech_worker.py`, `runtime/translation_worker.py`, `research/speech/requirements*.txt`): `ctranslate2` (MIT), `transformers` / `peft` / `sentencepiece` / `opencc` (Apache-2.0), `torch` / `numpy` / `psutil` / `soundfile` (BSD-3-Clause; `soundfile` dynamically wraps LGPL `libsndfile` in optional local Python environments). No FFmpeg executable is bundled or required.

## 4. Research evaluation datasets & offline test tools

Audio recordings from evaluation datasets stay outside Git (`research/speech/` tracks only manifests, scripts, and metrics):

- **Google FLEURS**, **SOVA RuDevices**, and **English LibriSpeech (OpenSLR 12)** — CC-BY-4.0 (attribution preserved in `research/speech/`).
- **Russian LibriSpeech / RuLS (OpenSLR 96)** — Public domain in the USA; mirror metadata tagged CC-BY-4.0.
- **Synthetic nonspeech controls** — CC0-1.0.
- **Offline database check (`tests/CommunityDatabase/verify.mjs`)** — Optionally uses `@electric-sql/pglite` (Apache-2.0) installed locally in a scratch folder.

Note: `docs/reviews/2026-11-01-offline-accuracy.md` evaluates `NLLB-200 distilled 600M` (CC-BY-NC-4.0) solely as a documented comparison note that was explicitly rejected for bundling; no NLLB weights or code are downloaded or used.

Retain upstream license texts when redistributing model or runtime assets (`licenses/`).
