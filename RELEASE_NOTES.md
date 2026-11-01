# Live Translator Overlay v1.5.0

## Fixed

- Preserve meaningful arrows and the instructions around them when cleaning translated text.
- Invalidate old translations and context when the Groq model or key changes.
- Reject truncated streams and bound stream reads before caching a translation as complete.
- Decode integer audio according to its actual sample format, including 24-bit and 32-bit PCM.
- Await Whisper consumers so its two-slot limit covers asynchronous processing; session tokens guard late Whisper results.
- Release localization subscriptions when history/feedback windows close.
- Close summary loading windows on failure, cancel summary requests on close, and apply summary capture exclusion.

## Feedback and history

- Track delivery separately from resolution: Sending, Sent, Failed, Composer opened, or Unknown for older records.
- Use full report IDs, atomic writes, and synchronized changes. Refresh reloads records after the network request so new reports are preserved.
- Store settings per user with atomic writes and migration of existing Windows configuration. Release packages contain no user keys.
- Keep the latest 500 history entries. Export uses a save dialog with an overwrite prompt.

## Mac developer preview

Shared translation, localization, settings, and feedback code now compiles for plain .NET 10. A separate Avalonia preview adds manual text translation, session history, clear/export, and an issue link for Apple Silicon and Intel.

Mac downloads are a separate prerelease so older Windows updaters cannot select a Mac ZIP. The preview is English-only and does not include audio, screen OCR, overlay captions, capture protection, or a Mac updater yet. Bundles are ad-hoc signed, not Developer ID signed or notarized: they are development artifacts, not a production Mac release.

## Verification and remaining work

12 existing regression checks and 9 new audit checks cover offline behaviour with synthetic audio and mocked HTTP. Six small live text checks preserve their tested meanings, numbers, and negations. This is not a general accuracy score or a speech-recognition benchmark.

Real-provider speech evaluation, native Mac capture/OCR, full updater rollback/signing, encrypted credentials, capture/DPI hardware tests, and clearer provider-fallback status remain open. The November 1 review documents these limits. Two existing NU1510 warnings concern the Windows System.Drawing.Common reference.
