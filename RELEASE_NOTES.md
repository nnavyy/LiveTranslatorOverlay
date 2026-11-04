# Live Translator Overlay v2.0.0

Windows adds local speech recognition and local translation, usable without API keys after model setup. On prepared systems the app selects local engines by default. Choose an explicit English or Russian source language for audio; select an explicit target language for local translation. Auto detection is not supported by the local path. Existing cloud engines remain optional.

Local speech requires the prepared NVIDIA CUDA environment. Translation uses a separate CPU model and releases its RAM after each request; it can be slower than online translation. Models and Python environments are separate from the application ZIP. On the prepared machine setup is already complete. Stop audio before changing the source language.

Download LiveTranslatorOverlay-Windows-x64.zip, extract it and run LiveTranslatorOverlay.exe. Requires .NET 10 Windows Desktop Runtime. Existing API keys/settings are preserved locally and are not included in the download. The updater uses this ZIP.

Future application source is private. Public downloads and historical MIT-licensed source remain available under their existing permissions. Purchasing is unconfigured; this release adds no expiry or mandatory payment. This update does not collect audio to train models.

The new local features are Windows-only. Live conversation and translation quality still need representative testing; historical research accuracy is not a live-product guarantee.

License / Support shows the planned US$13 one-time lifetime offer and a Not now button. Buying and paid activation remain disabled until checkout is configured; usage is not blocked.

