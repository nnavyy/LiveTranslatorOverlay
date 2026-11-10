#!/usr/bin/env bash
set -euo pipefail
rid="${1:?Pass osx-arm64 or osx-x64}"
output="${2:?Pass output directory}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'Unsupported runtime' >&2; exit 1 ;; esac
app="$output/LiveTranslator.app"
mkdir -p "$app/Contents/MacOS"
dotnet publish MacApp/LiveTranslatorMac.csproj -c Release -r "$rid" --self-contained true -o "$app/Contents/MacOS"
arch="${rid#osx-}"
[[ "$arch" != x64 ]] || arch=x86_64
xcrun swiftc -swift-version 5 -O -emit-library -target "$arch-apple-macosx14.0" \
    MacApp/NativeAudio.swift -o "$app/Contents/MacOS/libLiveTranslatorAudio.dylib"
version=$(python3 -c 'import xml.etree.ElementTree as e; print(e.parse("MacApp/LiveTranslatorMac.csproj").findtext("PropertyGroup/Version"))')
cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Live Translator Preview</string>
<key>CFBundleDisplayName</key><string>Live Translator Preview</string>
<key>CFBundleIdentifier</key><string>com.nnavyy.livetranslator.preview</string>
<key>CFBundleExecutable</key><string>LiveTranslatorMac</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSMicrophoneUsageDescription</key><string>Translate speech from your microphone on this Mac.</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
EOF
plutil -lint "$app/Contents/Info.plist"
while IFS= read -r -d '' binary; do
    # The app host seals its bundle: sign managed assemblies/resources before it.
    if [[ "$binary" != "$app/Contents/MacOS/LiveTranslatorMac" ]]; then
        codesign --force --sign - "$binary"
    fi
done < <(find "$app/Contents/MacOS" -type f -print0)
codesign --force --sign - "$app/Contents/MacOS/LiveTranslatorMac"
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"
"$app/Contents/MacOS/LiveTranslatorMac" --smoke-test
"$app/Contents/MacOS/LiveTranslatorMac" --ui-smoke-test
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/LiveTranslatorOverlay-Mac-preview-$rid.zip"
