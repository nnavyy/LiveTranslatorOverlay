# Local speech feasibility probe

Research tool only. It is not connected to the shipped application, captures no audio devices, and implements neither translation nor speaker identification.

The only inputs are an existing local Whisper GGML model, a local 16 kHz mono WAV, and the explicit source language (`en` or `ru`). The program does not download models or call a speech API. Restoring NuGet packages and obtaining the model/sample files are separate online setup steps; inference uses local files.

Whisper.net and its CPU runtime are pinned to 1.9.1 because the application uses .NET and needs a native speech engine. Production dependencies are unchanged. This runtime's published prerequisites include Windows 11 / Server 2022, the Visual C++ 2022 runtime, and AVX/AVX2/FMA/F16C on x64. Do not infer Windows 10 compatibility from the main application's existing support. Mac execution has not been tested here.

From the repository root:

```powershell
dotnet build tests/LocalSpeechProbe/LocalSpeechProbe.csproj -c Release --nologo
dotnet run --project tests/LocalSpeechProbe/LocalSpeechProbe.csproj -c Release --no-build -- 'C:\Models\ggml-small-q5_1.bin' 'C:\Samples\speech.wav' ru
```

Use `en` for English. No arguments or invalid paths/languages return exit code 2. Native/model/WAV failures return a nonzero process exit; there is no network fallback. Cancellation is requested after three minutes or Ctrl+C; native inference may take time to observe cancellation.

The tool forces the CPU backend, uses at most four threads, fixes temperature to zero and disables prior-text context. It runs twice in one process, reusing the loaded model. JSON lines include the model hash, audio length, model-load time, first-segment time, inference time, real-time factor, peak process working set and transcript. These are batch measurements, not live-caption delay. Peak working set is cumulative across both passes. Other programs and power/thermal settings can affect timings.

Reproduce the recorded model using the pinned [upstream artifact](https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small-q5_1.bin), then verify SHA-256 before running:

```text
ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb
```

Public samples and measured results are documented in the [offline feasibility report](../../docs/reviews/2026-11-01-offline-accuracy.md). Model weights and recordings are deliberately not committed. The two clips are execution and timing checks, not an accuracy benchmark or comparison with Groq or Copilot+.

For translation coverage, download the [M2M100 model card at the evaluated revision](https://huggingface.co/facebook/m2m100_418M/raw/55c2e61bbf05dfb8d7abccdc3fae6fc8512fd636/README.md) and [Argos package index at the evaluated revision](https://raw.githubusercontent.com/argosopentech/argospm-index/ff90de60728f7c1338ff6b75974e4c89b2442d22/index.json) to local files, then run:

```powershell
python tests/LocalSpeechProbe/check_language_coverage.py 'C:\Models\m2m100-model-card.md' 'C:\Models\argos-package-index.json'
```

This standard-library script reads the application's actual catalog, checks both source languages, traverses the Argos package graph including pivot routes, and emits metadata hashes. `zh-TW` requires an explicit conversion step; the output distinguishes this from native model coverage. Exit code 0 means the mapped M2M100 catalog covers the language codes, not that any translation was executed or judged accurate. The Argos index can change; compare hashes with the recorded results.
