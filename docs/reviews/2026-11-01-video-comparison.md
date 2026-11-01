# Comparison with Microsoft's Live Captions demo

Reviewed November 1, 2026, against Live Translator Overlay v1.5.1.

The app has a useful position beyond basic live captions: translation into more destination languages, screen OCR, a movable overlay, provider choice, and session history. Microsoft's advantage is the simpler setup and on-device speech pipeline. Neither this short video nor our existing synthetic tests establishes an accuracy or latency winner.

## Evidence and scope

Reference: [Live Captions with Translation! — Microsoft ExpertZone North America](https://www.youtube.com/watch?v=ZW-koeljiBE), a 54-second demonstration. The video was opened and its key scenes inspected in the browser. YouTube reported no transcript available; this review uses visible UI and embedded captions rather than a complete narration transcript. The timestamps below identify sampled scenes, not exact edit boundaries.

### Beat [00:00–00:05]: First-run setup

- **Visual:** Windows Live Captions requests consent for on-device processing and language-file setup.
- **Conceptual key:** Explain processing and prerequisites before starting.
- **Actionable artifact:** A future first-run flow could show audio source, source language, target language, provider requirements, and one Start action. Advanced settings can remain available separately.

### Beat [00:25–00:35]: Mandarin video with English captions

- **Visual:** A Mandarin conversation plays in a browser with English captions in a separate band above it. The creator's embedded captions describe support for 44 input languages.
- **Conceptual key:** System audio becomes translated captions without editing or uploading a media file manually.
- **Actionable artifact:** Demonstrate our corresponding audio-to-overlay flow using a fixed test clip, then show a destination such as Indonesian. Count input and output language support separately.
- **Note:** This is a demonstration, not a controlled test of translation quality, speaker separation, or delay.

### Beat [00:41]: Recap

- **Visual:** The creator returns to describing Live Captions over the same video example.
- **Conceptual key:** The short keeps one primary workflow in view.
- **Actionable artifact:** A promotional video for this app should show the result first, followed by the shortest setup and one distinctive feature such as OCR.

## Current product comparison

Microsoft's [current support documentation](https://support.microsoft.com/en-us/accessibility/windows/use-live-captions-to-better-understand-audio) distinguishes ordinary captions from translation: translation requires a Copilot+ PC running Windows 11 24H2 or newer. It documents over 40 source languages into English and 27 into Simplified Chinese. Audio and captions are processed on-device, captioning can work offline after setup, and generated captions are not stored.

| Area | Microsoft's demonstrated feature | Our current app |
| --- | --- | --- |
| Hardware | Translation has the Copilot+ requirement above | Windows x64 build does not require a Copilot+ NPU |
| Destination languages | English or Simplified Chinese, per current documentation | 34 catalog choices, including Indonesian; actual speech support depends on provider |
| Processing | On-device | Groq/Deepgram receive audio for speech recognition; translation text goes to the selected cloud service |
| Setup | OS-integrated setup and language files | Audio needs provider credentials; provider/model choices add setup work |
| Speaker identity | No separate speaker identities demonstrated in the clip; not evaluated | Deepgram diarization; Groq uses conservative acoustic estimates, not reliable person recognition |
| History | Generated captions are not stored, per documentation | Session history capped at 500 entries, explicit export, and optional cloud summaries |
| OCR | Screen OCR translation is not demonstrated | Selected-region Windows OCR followed by text translation |
| Mac | The demonstrated Windows feature is not a Mac app | Separate Apple Silicon/Intel preview: manual text translation and session history only |

Our capability statements are based on the current source and [speaker-label notes](../speaker-labels.md), [privacy policy](../../PRIVACY.md), and [Mac preview notes](../../MacApp/RELEASE_NOTES.md). They are not claims that every language, game, audio device, or provider combination has been tested.

## Recommended priorities after review

1. **Simpler setup:** keep the common audio flow short, validate the chosen provider key, and expose advanced controls when needed. Show Listening, Transcribing, Translating, and provider/network errors clearly.
2. **Measure quality before marketing it:** use the same consented clips on both products, including clean dialogue, names, numbers, accents, noise, and overlapping voices. Measure speech errors, meaning preservation, time to first caption, finalized-caption delay, and unwanted caption rewrites. Use human reference translations. No accuracy percentage is justified yet.
3. **Improve turn handling:** prioritize stable speaker labels and correct pairing of speaker, source text, and translation. For Groq users, a dedicated diarization path needs investigation; pitch grouping alone cannot reliably separate similar or overlapping voices.
4. **Make cloud use visible:** explain which provider receives audio or text and its key/network requirements. An optional offline engine would address a meaningful gap, but requires a separate performance and packaging assessment.
5. **Finish the Mac essentials:** native audio capture, permission flow, captions/overlay, then OCR and distribution signing. Continue labeling it a limited preview until those workflows are implemented and tested.

Suggested positioning: **Live multilingual captions and screen translation for games, videos, and conversations.** Show concrete features in the promotional video; avoid claims of better accuracy, zero delay, complete capture invisibility, or full Mac parity.

No app runtime, name, logo, version, or binary was changed for this comparison. The accompanying publication changes clarify the existing release titles, download links, architecture labels, README, and future release titles only. Branding remains pending user approval.
