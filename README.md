# Live Translator Overlay

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="MIT License" />
  <img src="https://img.shields.io/badge/Status-Free%20%26%20Open%20Source-brightgreen?style=for-the-badge" alt="Free" />
</p>

<p align="center">
  <strong>A high-performance, real-time subtitle and OCR translation overlay for Windows.</strong><br>
  Translate live game audio, video streams, voice meetings, and on-screen text directly on top of your display with zero click-through disruption.
</p>

---

## Features

- **100% Free and Unlimited by Default (No API Key Required)**
  - Operates out-of-the-box using Google Translate's high-speed endpoint (~100–200ms latency).
  - No account creation, no credit card, and no token quotas.
- **Real-Time Speech-to-Text (STT)**
  - **Deepgram Streaming (Recommended)**: Sub-second latency WebSocket streaming with live word-by-word interim preview and multi-speaker diarization with distinct color identification.
  - **Groq Whisper (Large-v3)**: Cloud batch transcription powered by Whisper Large-v3.
- **Anti-Hallucination and Voice Activity Detection (VAD)**
  - Audio energy gating prevents ambient noise or silence from generating spurious transcriptions.
  - Multi-language filtering actively blocks common subtitle artifacts (such as *"Subtitles by..."*, *"Spasiba"*, *"To be continued..."*, etc.).
- **Live Screen OCR Translation**
  - Draw any bounding rectangle to translate foreign in-game dialogues, manga, or application interfaces in real time using native Windows Hardware-Accelerated OCR.
- **Non-Intrusive Desktop Overlay**
  - **Always-on-top transparent canvas**: Captions render cleanly above full-screen games, media players, and desktop applications.
  - **Freely repositionable**: Move the subtitle banner and settings dialog anywhere on your monitors via drag-and-drop.
  - **OBS / Capture Exclusion**: Employs Windows Graphics Capture Exclusion (`SetWindowDisplayAffinity`) to keep the overlay hidden from stream recordings or screen shares.
  - **Translation History**: Integrated history log to review past spoken and translated statements.

---

## Quick Start

### Option 1: Pre-Built Binary (Recommended)
1. Navigate to the [Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases) page.
2. Download the latest `LiveTranslatorOverlay-Windows-x64.zip`.
3. Extract the ZIP archive to your desired location.
4. Launch `LiveTranslatorOverlay.exe`.
5. *(Optional)* If prompted, install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Option 2: Build From Source
Ensure that the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) is installed on your machine.

```bash
# Clone the repository
git clone https://github.com/nnavyy/LiveTranslatorOverlay.git
cd LiveTranslatorOverlay

# Restore and compile release build
dotnet build -c Release

# Execute application
dotnet run -c Release
```

---

## Configuration and API Setup (Bring Your Own Key)

The application operates without charge using **Google Translate**. If you prefer to utilize specialized cloud AI models, you may optionally supply your own API keys.

| Service | Category | Pricing | Intended Use Case |
|---|---|---|---|
| **Google Translate** | Translation | **Free** (No Key Required) | Daily translation, streaming, and gaming without rate limits |
| **Deepgram** | Speech-to-Text | **Free Tier** ($200 credit / ~12,000 mins) | Low-latency streaming transcription and speaker separation |
| **Groq AI** | Translation & Whisper STT | **Free Tier** (Rate-limited tokens) | Context-aware LLM translations and Whisper Large-v3 |

### Supplying API Keys (Optional)
Keys can be configured directly inside the interface:
1. Open the **Settings** menu on the overlay.
2. Enter your Deepgram key under **Deepgram API Key** ([console.deepgram.com](https://console.deepgram.com/)).
3. Enter your Groq key under **API Key (Groq/DeepL)** ([console.groq.com](https://console.groq.com/)) if utilizing Groq LLM or Groq Whisper.
4. Preferences automatically persist to your local `appsettings.json` file.

> [!NOTE]
> All credentials and configurations remain strictly stored on your local machine within `appsettings.json` and are never transmitted to external third-party servers.

---

## Usage Guide

1. **Configure Languages**:
   - Set the source language (e.g., `Russian`, `Japanese`, `English`) or select `Auto Detect`.
   - Set the target output language (e.g., `English`, `Indonesian`).
2. **Select Audio Source**:
   - Choose `Default System Playback` to capture audio from your system output (games, media players, web browsers).
   - Alternatively, choose a specific microphone input or individual application process.
3. **Initiate Translation**:
   - Click **Start Audio Translate** to begin live audio subtitle generation.
   - For on-screen text, click **Select Screen Area**, outline the target region, and click **Start Live OCR**.
4. **Customize Display**:
   - Reposition the subtitle box by clicking and dragging it to your preferred position.
   - Adjust caption font sizing (`Small`, `Medium`, `Large`, `Extra Large`) in the Settings pane.

---

## Architecture and Technologies

- **Runtime**: Windows Presentation Foundation (WPF) on .NET 10.0 Windows
- **Audio Processing**: [NAudio](https://github.com/naudio/NAudio) (WASAPI loopback capture and endpoint enumeration)
- **Speech-to-Text Engines**:
  - Deepgram WebSocket Nova-2 API (Real-time streaming with interim tokens and diarization)
  - Groq Cloud Whisper Large-v3 API
- **Translation Providers**:
  - Google Translate GTX Client Endpoint
  - Groq LLM API (Llama 3.3, GPT-OSS, Mixtral)
- **Optical Character Recognition**: `Windows.Media.Ocr` (Hardware-accelerated native Windows OCR)
- **User Interface**: Dark glassmorphic design with native WPF hardware acceleration and non-blocking mouse hit-testing.

---

## Contributing

Contributions, issues, and feature proposals are welcome:
1. Fork the repository.
2. Create your feature branch (`git checkout -b feature/NewFeature`).
3. Commit your changes (`git commit -m 'Add NewFeature'`).
4. Push to your branch (`git push origin feature/NewFeature`).
5. Open a Pull Request.

---

## Privacy and Terms of Service

This application processes audio and screen captures strictly in-memory and does not host any telemetry or tracking servers. For complete details regarding data handling, third-party APIs, and user responsibilities, please review [PRIVACY.md](PRIVACY.md).

---

## License

This project is licensed under the terms of the **MIT License**. See the `LICENSE` file for details.
