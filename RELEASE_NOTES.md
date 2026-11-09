# Live Translator Overlay v2.1.1

Windows users can start local multilingual speech without an NVIDIA/RTX GPU, API key, Python installation or developer workspace. New installs use local CPU speech and key-free online text translation. Existing cloud selections with keys remain optional.

- Automatic, checksum-verified model downloads: 148 MB balanced model or optional 488 MB larger model, plus a small neural speech detector. Progress and cancellation are supported; incomplete files are not loaded.
- Speech auto-detection and language choices covering the multilingual Whisper base/small models. Select the spoken language for better results.
- The Windows x64 ZIP bundles .NET, the C++ runtime and a fallback speech runtime for older CPUs. Extract and launch the EXE.
- API key fields are grouped under optional cloud settings. Old settings without keys fall back to available engines.
- Translation service failures now show an error instead of passing the original text off as a successful translation.

Internet is needed for the initial model download and Google text translation. Cached speech models run offline. Advanced offline text translation still uses separately prepared models. Windows 10/11 x64, model storage and available memory are required; slower computers may lag behind speech.

Accuracy is not guaranteed for every language, accent or recording. The larger model was slower and helped some Russian smoke samples; neither model inherits the earlier English/Russian GPU research scores. During live text checks, Google rate-limited the test connection and MyMemory returned a wrong negation for one Indonesian phrase. This remains a known upstream translation-quality limitation.

See docs/cpu-release-verification.md for verification results and limits. Source remains private. No API keys, user settings, recordings or research datasets are included in the download. No new payment or activation requirement is introduced.
