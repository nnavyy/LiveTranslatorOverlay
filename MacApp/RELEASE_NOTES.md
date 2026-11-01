# Mac developer preview — v1.5.1

## Mac downloads — developer preview

Requires **macOS 14 or newer**. Choose the ZIP matching your Mac's chip, then extract it to find `LiveTranslator.app`:

| Your Mac | Download |
| --- | --- |
| Apple Silicon — M-series chip | [LiveTranslatorOverlay-Mac-preview-osx-arm64.zip](https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/v1.5.1-mac-preview/LiveTranslatorOverlay-Mac-preview-osx-arm64.zip) |
| Intel processor | [LiveTranslatorOverlay-Mac-preview-osx-x64.zip](https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/v1.5.1-mac-preview/LiveTranslatorOverlay-Mac-preview-osx-x64.zip) |

Check **Apple menu > About This Mac**: a **Chip** entry naming an M-series chip means Apple Silicon; an **Intel Processor** entry means Intel.

**This is a limited developer preview, not the full Windows overlay.** It currently supports manual text translation and session history. Audio translation, OCR, and overlay captions are not available yet. These downloads are not notarized; see the signing notes below before downloading.

**Using Windows?** Get the [stable Windows x64 release](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v1.5.1). GitHub's **Source code** archives are for developers, not app installers.

This starts the macOS port for both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). It is a developer preview, separate from the stable Windows updater.

Included: manual text translation through Google or Groq, the 34-language catalog, session-only history with a 500-entry cap, clear/export, and an issue-report link. Groq keys stay in memory for the session. The interface is currently English.

Not included: system/microphone audio capture, screen OCR, overlay/caption mode, protection from other apps' screen captures, the Windows feedback mailbox flow, or an automatic Mac updater. Those features require further native macOS implementation and testing.

The `.app` bundles are self-contained and ad-hoc signed. They are **not Developer ID signed or notarized**; macOS Gatekeeper may block downloaded bundles. Trusted public distribution requires Apple signing credentials and notarization. For development, build from source with the .NET 10 SDK:

```sh
dotnet run --project MacApp/LiveTranslatorMac.csproj -c Release
```

Baseline: macOS 14 or newer. CI packages and smoke-tests each native architecture; this does not replace real-device permission, usability, Retina/multi-monitor, or provider-quality testing.
