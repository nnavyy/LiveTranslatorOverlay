# Open the Mac preview

The current Mac ZIPs (`LiveTranslator.app`) are **ad-hoc signed, not Apple Developer ID signed or notarized**. macOS Gatekeeper can therefore show **“LiveTranslator” Not Opened — Apple could not verify ... is free of malware**. This is a known distribution limitation, not a confirmed app crash. A successful build or local signature check does not prove Apple has checked the download for malware.

## Download and open

1. Download the `osx-arm64` (Apple Silicon M-series) or `osx-x64` (Intel) Mac archive from [GitHub Releases](https://github.com/nnavyy/LiveTranslatorOverlay/releases). macOS 14 or newer is required.
2. Extract the ZIP and move `LiveTranslator.app` to **Applications**.
3. Open the app. If the verification dialog appears, choose **Done** to keep the app.
4. If you trust the official download (or built it yourself from source), open **Apple menu > System Settings > Privacy & Security**. In the **Security** section, find the message about LiveTranslator being blocked and choose **Open Anyway**.
5. Authenticate on your Mac and confirm opening the app if prompted.

Apple's per-app exception is available for about an hour after an opening attempt. If it is missing, try opening the app again, then return to **Privacy & Security**. Menu wording can vary by macOS version. See [Apple's opening instructions](https://support.apple.com/en-us/102445) and [per-app exception details](https://support.apple.com/en-gb/guide/mac-help/mh40617/mac).

This is an exception for the app you choose to trust; it does not mean the download has passed Apple's notarization checks. If macOS instead reports detected malware, or you obtained the file from another source, stop and report that separate issue.

## What this preview includes

- Manual text translation via Google Translate (with fallback) or an optional session-only Groq API key across the 100-language catalog.
- Local CPU speech translation using Whisper (`Balanced` or `Larger model`) for **System audio** (`ScreenCaptureKit`) and **Microphone** (`AVAudioEngine`) with floating live captions.
- Session history (up to 500 entries, scroll-preserved while browsing) with clear and export.
- Private on-device audio translation timer, plus Community rankings (when a `community.json` endpoint is configured) and voluntary Support development overview.

Not included yet on macOS: Screen OCR and an automatic in-app updater.

## Removing the Gatekeeper warning for public distribution

Distributing without Gatekeeper warnings requires an Apple Developer Program membership, a **Developer ID Application** signing identity, and notarization credentials. The release pipeline must sign the app and its native components with hardened runtime options, submit the package to Apple's notary service, staple the accepted ticket, and verify the final artifact through Gatekeeper. See [Apple's notarization requirements](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution) and [.NET macOS publishing guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

Those credentials are not configured yet, so prebuilt Mac downloads cannot be described as Apple-verified. Store signing certificates and passwords only in protected GitHub Actions secrets when configured—never commit certificate private keys or passwords to the repository.
