# Application Source Is Now Private

Future application development is private. Public GitHub has downloads and reports; large models and recordings stay local. Earlier MIT-licensed source retains its existing permissions; see [LICENSE](LICENSE) and [APP_LICENSE.md](APP_LICENSE.md).

The release package was verified to contain no API keys or local configuration.

---

# Live Translator Overlay

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/License-Proprietary-blue?style=for-the-badge" alt="Proprietary License" />
  <img src="https://img.shields.io/badge/Status-v2.1.1-brightgreen?style=for-the-badge" alt="v2.1.1" />
</p>

<p align="center">
  <strong>A high-performance, real-time subtitle and OCR translation overlay for Windows.</strong><br>
  Translate live game audio, video streams, voice meetings, and on-screen text directly on top of your display with zero click-through disruption.
</p>

---

## v2.1.1 — CPU speech, no API key

Local multilingual speech runs on the CPU. An NVIDIA/RTX card, Python installation, API key and developer workspace are no longer needed for the default mode. The Windows x64 download bundles .NET and the C++ runtime, including a fallback for older CPUs.

1. Extract the Windows ZIP and launch `LiveTranslatorOverlay.exe`.
2. Choose the spoken language, or **Auto-detect**, and your target language.
3. Leave **Local multilingual — balanced, no key** and **Google Translate** selected, then start audio.
4. On first use, the app downloads and verifies the 148 MB speech model plus a small speech detector. Progress is shown; Stop cancels. Later speech sessions can run offline with the cached models.

The optional larger speech model downloads 488 MB and may improve recognition at the cost of speed. Google text translation needs internet and has service limits; no key is needed. Cloud engines with user keys remain optional. Advanced offline text translation still requires separately prepared models.

**Compatibility and accuracy:** Windows 10/11 x64, model storage and enough memory are still necessary. Caption speed depends on the CPU. Language choices cover the multilingual Whisper model's supported languages, not every language in the world. Accuracy varies with language, names, accents and noise; earlier English/Russian research scores do not apply to these CPU models. Live checks found a wrong-negation response from the online fallback when Google was rate-limited. Translation output is not guaranteed accurate.

---

## Downloads

| Platform | Download | Status |
| --- | --- | --- |
| Windows 10/11 — x64 | [Windows release (v2.1.1)](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v2.1.1) — choose `LiveTranslatorOverlay-Windows-x64.zip` | v2.1.1: CPU speech, key-free online translation, OCR and history |
| Mac — Apple Silicon (M-series) | [Mac preview](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v1.5.2-mac-preview) — choose `LiveTranslatorOverlay-Mac-preview-osx-arm64.zip` | Developer preview: manual text translation and session history |
| Mac — Intel | [Mac preview](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v1.5.2-mac-preview) — choose `LiveTranslatorOverlay-Mac-preview-osx-x64.zip` | Developer preview: manual text translation and session history |

Mac requires macOS 14 or newer. The preview bundles are ad-hoc signed and not notarized; read the [Mac preview notes](MacApp/RELEASE_NOTES.md).

---

## Licensing and Commercial Terms

- **Proprietary Development**: Starting with version 2.0.0, the application is no longer open source. Future application source code is maintained privately. Public GitHub maintains releases, downloads, and research reports.
- **Lifetime License**: License / Support shows the planned **US$13 one-time lifetime license** and a **Not now** option.
- **Unrestricted Usage**: Purchasing remains disabled until checkout is configured; usage is completely unrestricted and no trial expiry or mandatory payment is imposed.
- **Prior Source**: Permissions already granted for earlier MIT-licensed source code prior to v2.0.0 remain in effect. See [LICENSE](LICENSE) and [APP_LICENSE.md](APP_LICENSE.md).

---

## Features

- **Local Multilingual Speech**
  - Local CPU speech with automatic model download and auto-detection; no GPU or API key. Optional offline text translation uses separately prepared models.
- **Key-Free Text Translation by Default**
  - Uses Google Translate without a key; internet availability and service limits affect response time.
  - No account or API key required; optional cloud engines remain available.
- **Cloud Real-Time Speech-to-Text (STT)**
  - **Deepgram Streaming**: Sub-second latency WebSocket streaming with live word-by-word interim preview and multi-speaker diarization with distinct color identification.
  - **Groq Whisper (Large-v3)**: Cloud batch transcription powered by Whisper Large-v3.
- **Anti-Hallucination and Voice Activity Detection (VAD)**
  - Audio energy gating prevents ambient noise or silence from generating spurious transcriptions.
  - Multi-language filtering actively blocks common subtitle artifacts (*"Subtitles by..."*, *"Spasiba"*, *"To be continued..."*, etc.).
- **Live Screen OCR Translation**
  - Draw any bounding rectangle to translate foreign in-game dialogues, manga, or application interfaces in real time using native Windows Hardware-Accelerated OCR.
- **Non-Intrusive Desktop Overlay**
  - **Always-on-top transparent canvas**: Captions render cleanly above full-screen games, media players, and desktop applications.
  - **Freely repositionable**: Move the subtitle banner and settings dialog anywhere on your monitors via drag-and-drop.
  - **Screen Capture Exclusion (Privacy Mode)**: Employs Windows Graphics Capture Exclusion (`SetWindowDisplayAffinity`) to keep captions and all application windows completely hidden from OBS Studio, Discord screen sharing, Zoom, and screen recordings.
  - **Translation History**: Integrated history log to review past spoken and translated statements with conversation summaries.
- **Background Updates with Release Notes**
  - The app checks for new releases on GitHub and downloads updates silently in the background.
  - Choose **Restart to apply** when convenient.
- **Multi-Language User Interface & Target Translation**
  - Interface languages: **English**, **Indonesian (Bahasa Indonesia)**, **Russian (Русский)**, and **Spanish (Español)**.
  - Version 1.4.0+ adds Malay, Vietnamese, Thai, Portuguese, Italian, Dutch, Polish, Hindi, and Turkish translation choices.
- **In-App Bug Reporting & Resolution Tracker**
  - Report bugs, feature suggestions, and feedback directly inside the application.
  - **My Reports History**: Track previously submitted reports and monitor resolution progress (*Pending*, *In Progress*, *Fixed*) per device.

---

## Quick Start

1. Download the latest release from the [Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v2.1.1) page (`LiveTranslatorOverlay-Windows-x64.zip`).
2. Extract the ZIP archive to your desired location.
3. Launch `LiveTranslatorOverlay.exe`.
4. Start audio and wait for the automatic first-use speech model download. No separate .NET installer is needed.

---

## Configuration and API Setup (Optional)

The application operates without charge using local models or **Google Translate**. If you prefer to utilize specialized cloud AI models, you may optionally supply your own API keys.

| Service | Category | Pricing | Intended Use Case |
|---|---|---|---|
| **Local Engines** | Speech & Translation | Free / Local GPU & CPU | Offline transcription and translation without keys |
| **Google Translate** | Translation | No key required; service limits apply | Fast online text translation |
| **Deepgram** | Speech-to-Text | Check provider plan and limits | Cloud streaming transcription and speaker separation |
| **Groq AI** | Translation & Whisper STT | Check provider plan and limits | LLM translations and Whisper Large-v3 |

Keys can be configured directly inside the interface:
1. Open the **Settings** menu on the overlay.
2. Enter your Deepgram key under **Deepgram API Key** ([console.deepgram.com](https://console.deepgram.com/)).
3. Enter your Groq key under **API Key (Groq/DeepL)** ([console.groq.com](https://console.groq.com/)) if utilizing Groq LLM or Groq Whisper.
4. Preferences persist in `%LOCALAPPDATA%\LiveTranslatorOverlay\appsettings.json`. The v2.1.2 preview encrypts saved keys with Windows account protection. Earlier releases stored keys as plaintext.

---

## Privacy and Data Handling

- **Local-First Audio & OCR**: Audio loopback buffers and desktop screen captures are processed strictly in volatile system memory (RAM) and immediately discarded. No recordings are ever written to disk or sent to developer servers.
- **Zero Background Telemetry**: The application does not collect background metrics, analytics, or user behavior.
- **Voluntary In-App Feedback**: When submitting feedback, an anonymous local Device ID and basic diagnostic metadata (OS build and app version) are included to correlate reports.
- For complete terms and data security policies, consult the full [Privacy Policy and Terms of Service (PRIVACY.md)](PRIVACY.md).

---

## License

Starting with version 2.0.0, the application is distributed under proprietary terms (see [APP_LICENSE.md](APP_LICENSE.md)). License / Support displays the planned US$13 one-time lifetime license; checkout is currently disabled and usage is unrestricted.

Earlier public releases and historical source code prior to v2.0.0 remain under the terms of the [MIT License](LICENSE).
