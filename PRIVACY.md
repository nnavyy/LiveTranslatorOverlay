# Privacy Policy and Terms of Service

Last Updated: September 2026

This document outlines the privacy practices, data handling, and terms of service for the **Live Translator Overlay** desktop application ("the Software").

---

## 1. Overview and Core Principle

The Software is an open-source, client-side utility designed to provide real-time audio subtitle generation and on-screen text translation. 

**The core architectural principle of the Software is local-first privacy:**
- The Software does not operate any centralized tracking, telemetry, or data-collection servers.
- The developer does not collect, monitor, store, or sell any of your data, audio streams, or screen captures.
- All application logic runs locally on your computer.

---

## 2. Audio Capture and Processing

- **Volatile In-Memory Processing**: When Audio Translation is active, the Software captures system audio output (via Windows WASAPI loopback) or microphone input in short temporary buffers (typically 0.5 to 1.5 seconds) within volatile system memory (RAM).
- **No Disk Recording**: Audio data is never written to your hard drive, SSD, or external storage.
- **Immediate Discard**: Once an audio segment is transcribed or determined to be silent, the buffer is immediately overwritten and purged from memory.

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
3. **Groq (Optional)**: If you supply a Groq API key, text prompts and audio chunks are transmitted via encrypted HTTPS (`https://api.groq.com`) directly to Groq servers in accordance with [Groq's Privacy Policy](https://groq.com/privacy-policy).

**Security of API Keys**:
All API keys entered in the application are saved exclusively on your local machine in the `appsettings.json` file. They are never sent to the Software's developer or any intermediary server.

---

## 5. User Responsibilities and Lawful Use

As a user of the Software, you agree to:
- Comply with all applicable local, national, and international laws regarding audio recording, wiretapping, and privacy consent (including one-party or two-party consent laws where applicable).
- Not use the Software to intercept confidential, classified, or protected private communications without proper authorization.
- Respect copyright and terms of service of third-party video games, streams, and media content.

---

## 6. Disclaimer of Warranty and Limitation of Liability

The Software is distributed under the **MIT License**.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM, OUT OF, OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

## 7. Contact and Inquiries

For security inquiries, vulnerability reports, or questions regarding this document, please open an issue on the official GitHub repository:
https://github.com/nnavyy/LiveTranslatorOverlay
