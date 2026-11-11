# Live Translator Overlay 1.0.0 development candidate

This private candidate prepares the machine-learning release line. Public releases through 2.1.2 belong to the legacy alpha series. The candidate uses a separate stable update feed and validates downloaded packages before staging. Publish the feed and legacy compatibility ZIP only after both platforms pass the joint release checklist.

- Audio translation shows the running speech model. Stop audio to unlock speech model, source language and input device settings, then start the new session.
- Only the selected cloud providers show key controls. Verifying Groq credentials keeps the selected translation engine. Groq speech and text usage are shown separately. Deepgram usage is available in the provider dashboard.
- Saved Groq and Deepgram keys use Windows account encryption. The app upgrades the active plaintext settings file when it reads it. Earlier builds cannot read the encrypted keys.
- Filters the reported Whisper subtitle credit. Speech recognition and translation can still make mistakes.
- The Windows installer and both applications use the same versioned terms and privacy documents. The Windows installer offers to prepare missing speech models. Valid models are reused, and first audio use can prepare missing files later.
- Windows shows the installed version in Settings. Developer launches have a visible mode badge and keep the flag when restarting after an update. The updater explains when the public 1.x feed has not launched.
- The Community rankings preview adds a cartoon podium for the top three public profiles on each configured board. The podium shows audio translation time or verified contributions, and the full standings remain searchable.
- Community uses a dark theme and briefly animates the podium when rankings load. Select a nickname to open a profile popup with illustrated badges and the player's verified contribution total when they share it publicly.
- Windows Screen OCR closes from the result panel, the Stop OCR button, or Escape. The result panel stays within the screen edges.

The candidate includes a Mac audio capture bridge and floating captions. Both apps display a private audio usage total and the planned Supporter, Insider, and donation offers. Community has all-time, monthly, and supporter boards for a configured staging backend. Public accounts, payments, private messaging, and the Insider library are pending. Native device and permission testing are release gates.

Default speech runs locally. Default text translation uses Google or its online fallback. Advanced offline text translation still requires a separately configured translation model.
