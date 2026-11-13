# Live Translator Overlay 1.0.0

Open-source (MIT) release candidate for the 1.x machine-learning release line. Earlier GitHub releases through v2.1.2 belong to the legacy alpha series. This line uses the `ml-stable` update feed and validates downloaded update packages before staging.

- **Local CPU speech on Windows and macOS**: Runs multilingual Whisper (`base` 148 MB default, `small` 488 MB optional) + Silero VAD on the CPU with SHA-256 checksum verification, plus a `NoAvx` CPU fallback on Windows. Stop audio to unlock speech model, source language, and input device settings before starting a new session.
- **Key-free translation and optional cloud providers**: Uses Google Translate (with MyMemory fallback) by default without a key, or optional user-supplied keys for Groq AI and Deepgram streaming. Only the selected cloud providers show key controls, and Groq speech/text usage are displayed separately.
- **Windows DPAPI key encryption**: Saved Groq and Deepgram keys are encrypted using Windows Data Protection tied to the current Windows user account. Earlier plaintext settings are upgraded automatically on load.
- **Windows installer and versioned terms**: Both Windows and macOS share the versioned `ml-2` terms and privacy notice (`APP_LICENSE.md`, `PRIVACY.md`, `TERMS_REVISION.txt`), rendered without raw Markdown syntax. The Windows installer offers to prepare missing local speech files and reuses valid cached models.
- **Community rankings & voluntary Support development**: Both apps show a private on-device audio translation total, optional online rankings (when configured via `community.json`), top-three podium, searchable standings, profile popups with badges, and voluntary Support development offers (Supporter US$19.99, Insider US$100, Donate). All features are free under MIT; purchases and donations are currently closed.
- **Windows Screen OCR & History**: Screen OCR closes cleanly from the result panel, Stop OCR button, or Escape while staying within screen edges. Session history (Windows and Mac) keeps scroll position when new entries arrive while scrolled up.
- **Developer mode & updater**: Windows shows the installed version in Settings, keeps `--developer` mode badges across update restarts, and reports clearly when no public 1.x release is published yet.

Default speech recognition runs locally. Recognition and translation remain approximate and can make mistakes. Advanced offline text translation requires separately prepared models (`runtime/translation_worker.py`).
