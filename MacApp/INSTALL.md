# Open the Mac developer preview

The current Mac ZIPs are **ad-hoc signed, not Apple Developer ID signed or notarized**. macOS can therefore show **“LiveTranslator” Not Opened — Apple could not verify ... is free of malware**. This is a known distribution limitation, not a confirmed app crash. A successful build or local signature check does not prove Apple has checked the download for malware.

## Download and open

1. Download from the [official Mac preview release](https://github.com/nnavyy/LiveTranslatorOverlay/releases/tag/v1.5.1-mac-preview). Use `osx-arm64` for Apple Silicon (M-series) or `osx-x64` for Intel. macOS 14 or newer is required.
2. Extract the ZIP and move `LiveTranslator.app` to Applications.
3. Open the app. If the verification dialog appears, choose **Done** to keep the app.
4. If you trust this official download, open **Apple menu > System Settings > Privacy & Security**. In the **Security** section, find the message about LiveTranslator being blocked and choose **Open Anyway**.
5. Authenticate on your Mac and confirm opening the app if prompted.

Apple's per-app exception is available for about an hour after an opening attempt. If it is missing, try opening the app again, then return to Privacy & Security. Menu wording can vary by macOS version. See [Apple's opening instructions](https://support.apple.com/en-us/102445) and [per-app exception details](https://support.apple.com/en-gb/guide/mac-help/mh40617/mac).

This is an exception for the app you choose to trust; it does not mean the download has passed Apple's notarization checks. If macOS instead reports detected malware, or you obtained the file from another source, stop and report that separate issue.

## What this preview includes

Manual text translation and session history, with clear/export. Audio translation, screen OCR, overlay captions, and a Mac updater are not implemented yet. The Windows release has a different feature set.

## Removing this warning for public distribution

The project needs an Apple Developer Program membership, a **Developer ID Application** signing identity, and notarization credentials. The release pipeline must sign the app and its native components with the required runtime options, submit the package to Apple's notary service, staple the accepted ticket, and verify the final downloadable artifact through Gatekeeper. See [Apple's notarization requirements](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution) and [.NET macOS publishing guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

Those credentials are not configured, so the current release cannot be described as Apple-verified. No replacement binary has been published as a fix for this warning. Configure credentials through private GitHub Actions secrets when available; do not post certificate private keys or passwords in issues or chat.
