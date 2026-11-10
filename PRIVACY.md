# Privacy Policy and Terms of Service

## Local CPU speech and setup

The default speech engine processes audio on the device. Windows setup reuses valid local speech files and can download missing files from Hugging Face over HTTPS. If you skip that download, use the portable app, or use macOS, missing files download when you start local speech. The model service receives normal connection metadata such as your IP address. The app sends no audio with that download. Models are cached in the per-user application data directory. Default text translation sends recognized text to Google or its MyMemory fallback. On Windows, users with a separately configured offline translation model can select it to keep text processing on the device.

Last Updated: November 2026

This document outlines the privacy practices, data handling, and terms of service for the **Live Translator Overlay** desktop application ("the Software").

---

## 1. Overview and Core Principle

The Software is a client-side utility designed to provide real-time audio subtitle generation and on-screen text translation. Future application source is private; downloads and reports remain public. 

**The core architectural principle of the Software is local-first privacy:**
- The Software does not operate any centralized tracking, telemetry, or data-collection servers.
- The developer does not collect, monitor, store, or sell any of your data, audio streams, or screen captures.
- All application logic runs locally on your computer.

---

## 2. Audio Capture and Processing

- **Volatile In-Memory Processing**: When Audio Translation is active, the Software receives system audio or microphone input into temporary buffers in memory. Windows uses WASAPI. macOS uses ScreenCaptureKit for system audio and AVAudioEngine for microphone input. The app asks macOS for the permissions required by the selected source and consumes only audio samples.
- **No Disk Recording**: Audio data is never written to your hard drive, SSD, or external storage.
- **Temporary buffers**: Audio buffers are released after processing. Recent samples may overlap between segments to preserve words at segment boundaries.

---

## 3. Screen Capture and Optical Character Recognition (OCR)

- **Selected Region Only**: When Screen OCR is initiated, the Software captures only the precise desktop rectangle explicitly designated by the user.
- **Hardware-Accelerated Local OCR**: Image-to-text recognition is processed directly on your device using native Windows APIs (`Windows.Media.Ocr`).
- **No Video Recording**: No continuous video or full-screen surveillance is performed. The bitmap data is processed in-memory and discarded immediately after text extraction.

---

## 4. Third-Party Cloud Services and API Keys

The Software allows users to connect to third-party providers for machine translation and speech recognition:

1. **Google Translate (Default)**: Text queries are transmitted to Google's translation endpoints over encrypted HTTPS connections. No audio or images are transmitted to Google Translate.
2. **Deepgram (Optional)**: If you supply a Deepgram API key for streaming transcription, PCM audio streams are transmitted via encrypted WebSockets (`wss://api.deepgram.com`) directly to Deepgram servers in accordance with [Deepgram's Privacy Policy](https://deepgram.com/privacy).
3. **Groq (Optional)**: If you select Groq speech, audio chunks are sent to Groq. If you select Groq translation, recognized text and recent context may be sent to Groq. Using Summarize in History with Groq translation selected sends the history text to Groq. Requests use HTTPS and are subject to [Groq's Privacy Policy](https://groq.com/privacy-policy).

4. **MyMemory (Fallback)**: If the default text translation service fails, text may be sent over HTTPS to `api.mymemory.translated.net` for fallback translation.

Starting with version 1.4.0, normal operation does not write raw Deepgram or Groq transcripts to debug files. Older versions may have created `deepgram_debug.txt` or `groq_debug.txt` in their working folder. Those existing files are not automatically deleted. Translation history can still be explicitly exported by the user.

**Security of API Keys**:
The Windows app masks Groq and Deepgram API keys on screen and encrypts saved keys using Windows Data Protection tied to your Windows account. The app decrypts keys when loading settings so it can authenticate with the selected provider over HTTPS or WSS. The keys are never sent to the developer. If you move to a different Windows account or PC, you may need to enter your keys again.

The app upgrades older plaintext keys in its active settings file at `%LOCALAPPDATA%\LiveTranslatorOverlay\appsettings.json`. Copies from older installations and backups may still contain plaintext. The Mac developer preview keeps its Groq key only in session memory. Session history holds up to 500 entries and is exported only through an explicit save action.

---

## 5. Voluntary Bug Reporting, Feedback, and Diagnostics

The Software includes an optional in-app feedback dialog allowing users to submit bug reports, feature suggestions, and general feedback directly to the developer.

- **Strictly User-Initiated**: No feedback or telemetry is ever gathered or transmitted in the background without explicit user action (clicking "Send Report").
- **Data Transmitted Upon Submission**:
  - **User Feedback**: Selected report type, subject, and description.
  - **Contact Email (Optional)**: If provided by the user to receive reply updates. If left empty, submissions remain anonymous (`noreply@livetranslator.app`).
  - **Anonymous Device ID**: A randomly generated local identifier (e.g. `ID-XXXX-XXXX-XXXX`), Windows username, and machine name, used solely to correlate reports and allow the user to track resolution progress (*Pending*, *In Progress*, *Fixed*) within the application.
  - **Diagnostic Metadata**: Application version, Windows OS build (32/64-bit), .NET runtime version, and timestamp to assist in reproducing and resolving bugs.
- **Local History Storage**:
  - Reports submitted from your device are stored locally in `%LOCALAPPDATA%\LiveTranslatorOverlay\feedback_history.json`.
  - This allows the user to review past submissions and verify whether reported issues have been marked as resolved in newer releases.
- **Transmission Security**:
  - Feedback is transmitted over TLS/HTTPS directly to the developer's mailbox (`nandazhafran@gmail.com`) via FormSubmit API or via the user's default email client (`mailto:`).
- **No Third-Party Analytics**: The Software does not embed any tracking SDKs, advertising frameworks, or behavior analytics.

---

## 6. Screen Capture Exclusion (Privacy Mode)

The Software incorporates Windows Graphic Capture Exclusion (`SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`):
- When enabled, the overlay and all associated application dialogs are rendered invisible to third-party screen recording software, such as OBS Studio, Discord screen sharing, Zoom, and screen-capture tools.
- This ensures personal translations and desktop subtitles are kept private during live broadcasts and meetings.

---

## 7. User Responsibilities and Lawful Use

As a user of the Software, you agree to:
- Comply with all applicable local, national, and international laws regarding audio recording, wiretapping, and privacy consent (including one-party or two-party consent laws where applicable).
- Not use the Software to intercept confidential, classified, or protected private communications without proper authorization.
- Respect copyright and terms of service of third-party video games, streams, and media content.

---

## 8. Disclaimer of Warranty and Limitation of Liability

Speech recognition and translation are approximate. Captions can omit words, change meaning, add words nobody said or attribute speech to the wrong speaker. Check the original audio or text when accuracy matters.

The Software is distributed under the proprietary terms outlined in [APP_LICENSE.md](APP_LICENSE.md) (with historical open-source code under the MIT License).

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM, OUT OF, OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

## 9. Contact and Inquiries

For security inquiries, vulnerability reports, or questions regarding this document, please open an issue on the official GitHub repository:
https://github.com/nnavyy/LiveTranslatorOverlay
