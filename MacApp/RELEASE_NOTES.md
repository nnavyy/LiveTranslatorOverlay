# Mac developer preview — v1.5.0

This starts the macOS port for both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). It is a developer preview, separate from the stable Windows updater.

Included: manual text translation through Google or Groq, the 34-language catalog, session-only history with a 500-entry cap, clear/export, and an issue-report link. Groq keys stay in memory for the session. The interface is currently English.

Not included: system/microphone audio capture, screen OCR, overlay/caption mode, protection from other apps' screen captures, the Windows feedback mailbox flow, or an automatic Mac updater. Those features require further native macOS implementation and testing.

The `.app` bundles are self-contained and ad-hoc signed. They are **not Developer ID signed or notarized**; macOS Gatekeeper may block downloaded bundles. Trusted public distribution requires Apple signing credentials and notarization. For development, build from source with the .NET 10 SDK:

```sh
dotnet run --project MacApp/LiveTranslatorMac.csproj -c Release
```

Baseline: macOS 14 or newer. CI packages and smoke-tests each native architecture; this does not replace real-device permission, usability, Retina/multi-monitor, or provider-quality testing.
