# Live Translator Overlay v2.1.2

- Audio translation shows the running speech model. Stop audio to unlock speech model, source language and input device settings, then start the new session.
- Only the selected cloud providers show key controls. Verifying Groq credentials keeps the selected translation engine. Groq speech and text usage are shown separately. Deepgram usage is available in the provider dashboard.
- Filters the reported Whisper subtitle credit. Speech recognition and translation can still make mistakes.
- The Windows installer presents the license, terms and privacy policy on first install, then downloads missing speech files. Valid models are reused. Regular app launches skip setup, and the portable app downloads missing models when you start local speech.

Default speech runs locally. Default text translation uses Google or its online fallback. Advanced offline text translation still requires a separately configured translation model.
