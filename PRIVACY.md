# Privacy Policy

Live Translator Overlay, revision ml-2. Last updated: November 2026.

This policy explains what the app does with your audio, text, files and network connections. The application source code is open source under the MIT License, so you can check these statements in the code.

## 1. Summary

- The app is local first. Audio capture, screen capture and OCR happen on your device.
- The app has no telemetry, no advertising and no analytics SDK.
- Nothing is sent to the developer unless you choose to send feedback.
- Audio, captions and screenshots are not recorded to disk. They are not uploaded to the developer.
- Audio or text leaves your device only for the speech or translation provider you select. Default text translation sends recognized text to Google.
- Accounts are optional. This version does not create accounts and does not upload your usage.

## 2. Local speech and model downloads

- The default speech engine processes audio on your device, on the CPU.
- Windows setup reuses valid local speech files and can download missing files from Hugging Face over HTTPS.
- If you skip that download, use the portable app, or use macOS, missing files download when you start local speech.
- Hugging Face receives normal connection metadata such as your IP address. The app sends no audio with the download.
- Models are cached in the per-user application data folder.
- On Windows, you can select a separately prepared offline translation model to keep text processing on the device.

## 3. Audio capture

- When Audio Translation is active, the app reads system audio or microphone input into temporary memory buffers.
- Windows uses WASAPI. macOS uses ScreenCaptureKit for system audio and AVAudioEngine for microphone input. The app asks macOS for the permissions the selected source needs and uses only audio samples.
- Audio is never written to your disk.
- Buffers are released after processing. Recent samples may overlap between segments so that words at the boundary are not cut.
- The app stores the total duration of your audio translation on this device so you can see your personal total. The file contains no audio, captions or identity information.

## 4. Screen capture and OCR

- When you start Screen OCR, the app captures only the screen region you select.
- Text recognition runs on your device with the native Windows OCR API (Windows.Media.Ocr).
- The app does not record video and does not watch the full screen. The image is processed in memory and discarded after the text is read.

## 5. Translation and speech providers

The default and optional providers receive data as follows.

- Google Translate (default): recognized text is sent over HTTPS to Google translation endpoints. No audio or images are sent.
- MyMemory (fallback): if the default text translation fails, text may be sent over HTTPS to api.mymemory.translated.net.
- Deepgram (optional): if you add a Deepgram API key, audio is streamed over an encrypted WebSocket to Deepgram. Deepgram privacy policy: https://deepgram.com/privacy
- Groq (optional): if you choose Groq speech, audio chunks are sent to Groq. If you choose Groq translation, recognized text and recent context may be sent to Groq. Summarize in History sends the history text to Groq when Groq translation is selected. Groq privacy policy: https://groq.com/privacy-policy

Each provider handles data under its own terms. If you do not want to send text or audio to a provider, do not select it.

## 6. API keys and local files

- The Windows app masks Groq and Deepgram keys on screen and encrypts saved keys with Windows Data Protection tied to your Windows account.
- The app decrypts keys when it loads settings so it can sign in to the provider over HTTPS or WSS. Keys are never sent to the developer.
- If you move to another Windows account or PC, you may need to enter your keys again.
- The active settings file is %LOCALAPPDATA%\LiveTranslatorOverlay\appsettings.json. The app upgrades older plaintext keys there. Copies from older installations and backups may still contain plaintext.
- The Mac developer preview keeps its Groq key only in memory for the session.
- Session history holds up to 500 entries. It is saved only when you use an explicit export or save action.
- Version 1.4.0 and later do not write raw Deepgram or Groq transcripts to debug files. Older versions may have created deepgram_debug.txt or groq_debug.txt in their working folder. The app does not delete those files automatically.

## 7. Updates

- The Windows app checks a stable update feed on GitHub at start and about every 15 minutes, and downloads updates from GitHub Releases.
- The app can also read a public report status file on GitHub to show the state of feedback you sent.
- GitHub receives normal connection metadata such as your IP address. The app adds no personal information to these requests.

## 8. Feedback and bug reports

The app has an optional feedback dialog for bug reports and suggestions. Nothing is sent until you click Send Report.

If you send a report, it contains:

- The report type, subject and description.
- Your contact email, only if you enter one. If you leave it empty, the report has no contact address.
- A random device ID, your Windows user name and your machine name. They link your reports together and let the app show whether a report is pending, in progress or fixed.
- The app version, Windows build, 32-bit or 64-bit mode, .NET version and time.

Delivery:

- Reports are sent over HTTPS to the developer mailbox through the FormSubmit service, or through your default email program if you choose that route.
- Reports you sent are also kept on your device in %LOCALAPPDATA%\LiveTranslatorOverlay\feedback_history.json.

## 9. Community, accounts and support

- Your personal audio total stays on your device and is shown to you privately.
- The Community window can read online rankings only when a community server is configured in a local community.json file. Without that file, the app makes no community requests.
- The request sends the board name, your search text and the page position. Rankings list only people who have opted in to be listed.
- This version does not upload your usage, does not create accounts and does not accept payments.
- The developer-assisted purchase request is a preview for a staging server only. It signs in an existing test account with a one-time email code and records the account ID, the chosen offer and the time. It takes no payment.
- If online accounts, rankings or payments become available in a later version, this policy will be updated and the app will ask you to accept the new revision.

## 10. Screen capture exclusion

On Windows, the app can use the Windows display affinity setting WDA_EXCLUDEFROMCAPTURE. When it is on, the overlay and the app windows are hidden from screen recorders and screen sharing such as OBS Studio, Discord, Zoom and screenshot tools. This keeps your translations private during broadcasts and meetings.

## 11. Your choices and your data

- You can stop any feature at any time. Stopping Audio Translation or Screen OCR stops capture.
- You can delete local data by removing the application data folder. On Windows it is %LOCALAPPDATA%\LiveTranslatorOverlay. On macOS it is ~/Library/Application Support/LiveTranslatorOverlay.
- You can choose not to use any cloud provider.
- Because the developer does not hold your audio, captions or screenshots, there is nothing to delete on the developer side. For a feedback report you sent, contact the developer through the repository and mention your device ID.

## 12. Changes to this policy

When this policy changes in a way that matters, the revision changes and the app asks you to accept it again.

## 13. Contact

For privacy questions, open an issue on the official GitHub repository: https://github.com/nnavyy/LiveTranslatorOverlay

To report a security problem, use private vulnerability reporting in the Security tab of that repository instead of a public issue.
