# Speaker labels and the v1.5.1 update

The “What's new” window now scrolls 12 Windows DIPs per wheel notch, in either direction. Larger/accelerated events are capped at 24 DIPs. Keyboard navigation, selection/copy, and dragging the scrollbar remain native.

## Groq Whisper

This app's Whisper path has no word-level speaker IDs. Its optional local labels are approximate voice groups, not verified people or persistent identities. Pitch alone cannot reliably distinguish similar voices, and one audio chunk can contain several speakers.

The update removes the male/female split, chooses a nearer compatible group instead of the first match, avoids harmonic pitch halving, checks pitch consistency, rejects constant/unvoiced input, and leaves mixed/ambiguous chunks unassigned. Cancelled sessions cannot register profiles. Speaker numbers and colors are session-local, and the caption/history explicitly say “estimate.” No real-conversation accuracy percentage is claimed.

Only accepted transcriptions update local voice groups. Identical words in later turns are not suppressed by the old 20-second text-only deduplication rule, since another person may legitimately say the same thing.

These changes reduce specific false assignments demonstrated by synthetic fixtures; they do not turn Whisper into a trained diarization model.

## Word-level speaker separation

Select **Deepgram** in STT Mode, configure its API key, select the source language, and restart audio. Its diarizer assigns speaker IDs to transcript words. The app pins the supported streaming `diarize_model=v1` option. Streaming responses do not provide the batch API's `speaker_confidence`; the app does not invent a confidence percentage. [Deepgram diarization documentation](https://developers.deepgram.com/docs/diarization).

Missing IDs now stay unknown instead of becoming Speaker 1. Interim words use a neutral “Speaking…” label and cannot overwrite a finalized turn during translation. A finalized turn's speaker, original, and translated text update together while holding the translation slot. Old connection callbacks are ignored, and connection cleanup is idempotent and bounded.

Provider diarization can still be wrong, especially with overlap or short utterances. Validate with representative recordings before relying on the labels. Speaker numbers describe voice turns within a connection; they are not people's names.

## Verification

The original tests reproduced the gender-boundary split (150 Hz vs 160 Hz), silent audio inheriting a known label, and missing diarization being treated as Speaker 1. The revised suite covers those cases, distinct/returning pitch groups, mixed chunks, cancellation, constant input, ordered finalized word groups, repeated cleanup, and actual WPF up/down/accelerated wheel events.

Run `dotnet run --project tests/SpeakerChecks/SpeakerChecks.csproj -c Release` on Windows. Fixtures use generated samples and mocked transcript messages; no audio capture, credentials, or provider calls are needed.
