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

## If macOS says “LiveTranslator” Not Opened

**Known limitation: these preview downloads are not notarized by Apple.** The message **“Apple could not verify ‘LiveTranslator’ is free of malware”** is expected for this distribution. It does not prove Apple has checked the file or that the app is malware-free.

If you downloaded the app from this official release and choose to trust it:

1. Extract the ZIP and move `LiveTranslator.app` to **Applications**.
2. Try opening it. On the blocked-app dialog, choose **Done**.
3. Open **System Settings > Privacy & Security**. Under **Security**, find LiveTranslator and choose **Open Anyway**.
4. Authenticate on your Mac and confirm opening if prompted.

The exception is available for about an hour after an opening attempt; try opening the app again if the button is missing. This allows that app to open; it does not notarize it. See the [Mac installation guide](https://github.com/nnavyy/LiveTranslatorOverlay/blob/main/MacApp/INSTALL.md) and [Apple's instructions](https://support.apple.com/en-us/102445).

Removing this warning for everyone requires Developer ID signing and Apple notarization. No Apple Developer membership or signing credentials are configured yet, and no new binary claiming to resolve it has been published.

This starts the macOS port for both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). It is a developer preview, separate from the stable Windows updater.

Included: manual text translation through Google or Groq, the 34-language catalog, session-only history with a 500-entry cap, clear/export, and an issue-report link. Groq keys stay in memory for the session. The interface is currently English.

Not included: system/microphone audio capture, screen OCR, overlay/caption mode, protection from other apps' screen captures, the Windows feedback mailbox flow, or an automatic Mac updater. Those features require further native macOS implementation and testing.

The `.app` bundles are self-contained and ad-hoc signed. They are **not Developer ID signed or notarized**; macOS Gatekeeper may block downloaded bundles. Trusted public distribution requires Apple signing credentials and notarization. For development, build from source with the .NET 10 SDK:

```sh
dotnet run --project MacApp/LiveTranslatorMac.csproj -c Release
```

Baseline: macOS 14 or newer. CI packages and smoke-tests each native architecture; this does not replace real-device permission, usability, Retina/multi-monitor, or provider-quality testing.
