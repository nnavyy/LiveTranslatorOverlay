# Mac release notes — v1.0.0

## What's included on macOS

- **Local CPU audio translation & floating captions**: Capture **System audio** (`ScreenCaptureKit`) or **Microphone** (`AVAudioEngine`) on macOS 14+ and transcribe locally on CPU with Whisper (`Balanced` 148 MB or `Larger model` 488 MB) and Silero VAD. Audio stays on your Mac; the selected translation provider receives only recognized text.
- **Manual text translation workspace**: Side-by-side original and translated text across the 100-language catalog using Google Translate (with MyMemory fallback) or an optional session-only Groq API key.
- **Session history**: Up to 500 entries with clear/export and preserved scroll position when new translations arrive while scrolling back.
- **Community & voluntary Support**: Private on-device audio usage counter, Community rankings view (active when `community.json` is configured), and Support development tab. Live Translator Overlay is free and open source under the [MIT License](../LICENSE); purchases are currently closed.

**Not yet included on macOS:** Screen OCR and an automatic in-app updater (available on Windows).

## Mac downloads (`macOS 14+`)

Choose the archive matching your Mac's architecture from [GitHub Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases):

| Your Mac | Archive |
| --- | --- |
| Apple Silicon (M-series chip) | `LiveTranslatorOverlay-Mac-preview-osx-arm64.zip` |
| Intel processor | `LiveTranslatorOverlay-Mac-preview-osx-x64.zip` |

Check **Apple menu > About This Mac**: a **Chip** entry naming an M-series chip means Apple Silicon; an **Intel Processor** entry means Intel.

## Opening ad-hoc signed downloads

**Known limitation: prebuilt Mac downloads are ad-hoc signed, not Developer ID signed or notarized by Apple.** macOS Gatekeeper may display **“Apple could not verify ‘LiveTranslator’ is free of malware”**.

If you downloaded the archive from the official GitHub Releases page and choose to trust it:

1. Extract the ZIP and move `LiveTranslator.app` to **Applications**.
2. Try opening it, then click **Done** on the blocked-app dialog.
3. Open **System Settings > Privacy & Security**, find LiveTranslator under **Security**, and click **Open Anyway**.
4. Authenticate and confirm opening.

See [MacApp/INSTALL.md](INSTALL.md) and [Apple's support guide](https://support.apple.com/en-us/102445) for full details.

## Building from source

Requires **.NET 10 SDK** and Xcode Command Line Tools (`swiftc`) on macOS 14+:

```sh
dotnet run --project MacApp/LiveTranslatorMac.csproj -c Release
# Or build and smoke-test a self-contained .app bundle:
bash MacApp/package-macos.sh osx-arm64 ./out
```

CI packages and smoke-tests `osx-arm64` and `osx-x64` bundles, which does not replace real-device macOS permission, Retina/multi-monitor, or audio-device testing.
