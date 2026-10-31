# Live Translator Overlay v1.4.0

## Added

- Spanish interface for settings, feedback, history, and release notes. Translation is an initial draft awaiting native-speaker review.
- Nine source/target language choices: Malay, Vietnamese, Thai, Portuguese, Italian, Dutch, Polish, Hindi, and Turkish. Speech support depends on the selected provider; OCR requires an installed Windows language pack.
- A “What’s new” window on first launch of each installed version, with notes in English, Indonesian, Russian, or Spanish. Reopen it from App Updates at any time.

## Fixed

- Changed numbers and similar sentences receive fresh translations instead of reusing an older caption.
- Ordinary short replies and brief Chinese/Japanese/Korean text survive filtering; silence and repetitive noise are still checked.
- Short voiced playback is emitted after a pause without repeatedly emitting the overlap buffer.
- Successful unchanged Google translations no longer invoke fallback unnecessarily; MyMemory application errors are rejected.
- Only explicit report-status mappings change report resolution. A version bump alone never marks reports Fixed.
- Static OCR text is retranslated when target language, engine, or script style changes.
- Feedback submissions include the current app version and require an explicit successful service response.

## Improved

- Restarting to install is explicit. Editing settings, reading history, and quitting no longer trigger automatic update restarts.
- The updater selects the expected Windows ZIP, checks for the executable, avoids terminating unrelated copy processes, and retains staging after a failed copy.
- Raw transcripts are no longer written to debug files during normal operation. Existing logs remain on disk until users remove them.
- Deepgram Auto explicitly requests Nova-2 multilingual English/Spanish mode; the UI advises restarting audio after changing source language.
- Saved UI language uses a stable language tag when restored.
- Build and regression checks run on main pushes, pull requests, and release tags.

## Known limitations

- Audio transcription requires a Deepgram or Groq key; the key-free path translates text.
- Other Deepgram source languages must be selected explicitly; Auto is not universal language detection.
- This release does not claim to resolve every finding in the app review. Async audio queue/session isolation, encrypted credential storage, full updater rollback/signing, and hardware-specific capture/DPI checks remain follow-up work.
- Real-provider audio quality, mixed-monitor behavior, and third-party screen capture require further device testing. The regression suite uses synthetic audio and mocked HTTP.
- Two existing NU1510 build warnings concern the System.Drawing.Common package reference.
