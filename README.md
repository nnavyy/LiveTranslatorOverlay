# 🌐 Live Translator Overlay

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="MIT License" />
  <img src="https://img.shields.io/badge/Free%20%26%20Open%20Source-100%25-brightgreen?style=for-the-badge" alt="Free" />
</p>

<p align="center">
  <strong>A lightweight, ultra-fast real-time subtitle and OCR translator overlay for Windows.</strong><br>
  Translate live game audio, YouTube streams, Discord calls, movies, and on-screen text directly onto your screen with zero click-through disruption.
</p>

---

## ✨ Features

- ⚡ **100% Free & Unlimited by Default (No API Key Required)**
  - Powered out-of-the-box by Google Translate's high-speed engine (~100–200ms latency).
  - No account, no credit card, and no tokens to run out of.
- 🎙️ **Live Audio Transcription (Speech-to-Text)**
  - **Deepgram Streaming (Recommended)**: Ultra-low latency (<300ms) WebSocket streaming with live word-by-word interim preview and multi-speaker detection (Speaker Diarization with distinct color tags).
  - **Groq Whisper (Large-v3)**: High-accuracy batch transcription powered by Whisper Large-v3.
- 🛡️ **Anti-Hallucination & Smart VAD Filter**
  - Advanced Voice Activity Detection (VAD) energy gating prevents background noise or silence from triggering false translations.
  - Built-in multi-language hallucination cleaner stops common subtitle artifacts (such as *"Subtitles by..."*, *"Spasiba"*, *"To be continued..."*, etc.) from ever reaching your screen.
- 📷 **Real-Time Screen OCR**
  - Draw any region on your screen to translate foreign game text, manga/comics, or software interfaces on the fly using Windows Native OCR.
- 🪟 **Gamer & Streamer Friendly Overlay**
  - **Always-on-top transparent canvas**: Subtitles float cleanly over full-screen games or video players.
  - **Draggable elements**: Drag the caption bar and settings panel anywhere on your monitor.
  - **OBS / Screen Capture Safe**: Utilizes Windows Graphics Capture Exclusion (`SetWindowDisplayAffinity`) to remain invisible in OBS / Discord streams if desired.
  - **Live History Log**: Built-in notepad history viewer to review recent spoken and translated dialogs.

---

## 🚀 Quick Start

### Option 1: Download Pre-built Binary (Recommended)
1. Go to the [Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases) section.
2. Download the latest `LiveTranslatorOverlay-Release.zip`.
3. Extract the ZIP archive anywhere on your PC.
4. Run `LiveTranslatorOverlay.exe`.
5. *(Optional)* If prompted, install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Option 2: Build From Source
Ensure you have the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed.

```bash
# Clone the repository
git clone https://github.com/nnavyy/LiveTranslatorOverlay.git
cd LiveTranslatorOverlay

# Restore dependencies and build
dotnet build -c Release

# Run the app
dotnet run -c Release
```

---

## ⚙️ Configuration & API Setup (BYOK - Bring Your Own Key)

The application is completely free and functional immediately with **Google Translate**. If you wish to use advanced cloud AI models, you can easily plug in your own API keys.

| Service | Engine | Cost | Recommended For |
|---|---|---|---|
| **Google Translate** | Translation | **100% Free** (No API Key needed) | Everyday use, gaming, streaming without quota worries |
| **Deepgram** | STT (Speech-to-Text) | **Free tier** ($200 credit ≈ 12,000 mins) | Real-time streaming subtitles & speaker detection |
| **Groq AI** | Translation & Whisper STT | **Free tier** (Rate-limited tokens) | Context-aware LLM translations & Whisper Large-v3 |

### Adding API Keys (Optional)
You can configure your keys directly inside the application UI:
1. Click **⚙️ Settings** on the overlay.
2. Under **Deepgram API Key**, paste your key from [console.deepgram.com](https://console.deepgram.com/) *(Free $200 credit upon signup)*.
3. Under **API Key (Groq/DeepL)**, paste your key from [console.groq.com](https://console.groq.com/) if you want to use Groq LLM or Groq Whisper.
4. Your settings will automatically save to your local `appsettings.json` upon exit.

> **🔒 Privacy Note**: Your API keys and configurations are stored strictly on your local machine in `appsettings.json` and are **never** uploaded or shared anywhere.

---

## 🎮 How to Use

1. **Select Source & Target Languages**:
   - Choose your audio/text language (e.g. `Russian`, `Japanese`, `English`) or select `Auto Detect`.
   - Choose your target language (e.g. `English`, `Indonesian`).
2. **Select Audio Device**:
   - Pick `Default System Playback` to capture sound coming out of your speakers/headphones (games, browser, video player).
   - Or pick a specific microphone or running application process.
3. **Start Live Translation**:
   - Click **🔊 Start Audio Translate** to begin live captioning.
   - For on-screen text, click **✂️ Select Screen Area**, draw a rectangle over the target area, then click **▶ Start Live OCR**.
4. **Customize Subtitles**:
   - Drag the subtitle box by left-clicking and dragging it to your preferred location on screen.
   - Adjust font sizes (`Small`, `Medium`, `Large`, `Extra Large`) in the Settings menu.

---

## 🛠️ Tech Stack & Architecture

- **Framework**: WPF (.NET 10.0 Windows)
- **Audio Capture**: [NAudio](https://github.com/naudio/NAudio) (WASAPI loopback capture & endpoint enumeration)
- **STT Engines**:
  - Deepgram WebSocket Nova-2 API (Real-time interim results & diarization)
  - Groq Whisper Large-v3 Cloud API
- **Translation Providers**:
  - Google Translate Free GTX Engine
  - Groq LLM (Llama 3.3, GPT-OSS, Mixtral)
- **Screen OCR**: `Windows.Media.Ocr` (Native Windows Hardware-Accelerated OCR)
- **UI Design**: Modern glassmorphic dark theme with WPF canvas drag-and-drop & drop shadow effects.

---

## 🤝 Contributing

Contributions, bug reports, and feature suggestions are always welcome!
1. Fork the Project.
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`).
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the Branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request.

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for more information.
