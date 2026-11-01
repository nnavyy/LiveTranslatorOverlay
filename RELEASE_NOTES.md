# Live Translator Overlay v1.5.1

## Popup scrolling

The What's new window scrolls in smaller steps up and down. Accelerated wheel events are bounded, so they no longer jump through the release notes. Keyboard navigation, copying text, and scrollbar dragging remain native.

## Speaker labels

- Groq's local voice estimates use neutral Speaker 1 / Speaker 2 labels instead of guessing gender.
- Small pitch changes are less likely to create another label; harmonic pitch errors are reduced.
- Accepted transcriptions update the voice groups; identical words from a later turn are not suppressed for 20 seconds.
- Silent, constant, mixed, or ambiguous audio is left unassigned instead of borrowing a known person's label.
- Cancelled audio sessions cannot register new speaker profiles. Labels/colors remain session-local and explicitly marked as estimates.
- Deepgram results without speaker IDs stay unknown. Interim text cannot overwrite another finalized turn while it is being translated; speaker, original, and translation stay paired.
- Callbacks from old connections are ignored, and socket cleanup is bounded and safe to repeat.

For word-level speaker separation, select Deepgram streaming and configure its API key. Whisper's local pitch groups cannot reliably identify people with similar voices or separate overlapping speech. This update does not claim a measured real-conversation accuracy percentage. See [speaker-label details](https://github.com/nnavyy/LiveTranslatorOverlay/blob/main/docs/speaker-labels.md).

## Verification

Existing regression/audit suites plus new synthetic-speaker, mocked-diarization, and actual WPF wheel-event tests cover the targeted changes. Native Windows and both Mac preview builds run in CI. The Mac preview still supports manual text translation/history only; it does not yet have audio or OCR, and remains ad-hoc signed rather than notarized.
