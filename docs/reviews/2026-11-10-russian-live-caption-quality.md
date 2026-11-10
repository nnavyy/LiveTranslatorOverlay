# Russian → English live-caption quality report

**Measurement date:** 10 November 2026 (Asia/Jakarta)

**Scope:** Windows 1.0.0 private candidate, source baseline `007d6c1`. The user reported slow, frequently incorrect Russian → English captions with **Local multilingual — larger model, no key**. The exact source recording, capture route, and original/translated captions were not available. The tests below isolate possible causes without using the user's audio or sending corpus transcripts to a service.

## Findings

- **The larger model improved recognition in this sample, at a substantial speed cost.** It reduced Russian word error rate from **33.8% to 20.5%** on the same 12 clips, but its median processing time rose from **1.51 s to 4.93 s**. This is approximately **3.3× slower**, before live buffering, queuing, and translation.
- **Voice detection can discard real quiet speech before Silero runs.** At −24 dB relative to the test clips' original level, Silero alone detected speech in all 12 clips, but the app's earlier energy gate would discard 8 of them.
- **Google was faster than larger-model recognition in this run, but either translation route can make independent errors.** Direct Google preserved meaning in 7 of 8 typed examples, with a median of **0.60 s**. In a simulated Google rate limit, the real MyMemory fallback changed a Russian prohibition into **“Open the door.”** It returned a successful response that the app accepted as a translation.

## 1. Russian speech recognition and latency

The Windows setting called **larger model** loads `ggml-small.bin` (488 MB), a local multilingual Whisper Small model. **Balanced** loads `ggml-base.bin` (148 MB). Both use CPU inference in the app. Groq Whisper Large-v3 is a different, cloud speech option. [App model mapping](../../Core/SpeechModelDownload.cs) · [Local CPU pipeline](../../Core/LocalSpeechService.cs) · [Groq speech model](../../Core/WhisperSttService.cs).

I ran the production `LocalSpeechService` with explicit Russian (`ru`) on an Intel Core i5-13420H. The deterministic sample used six held-out Russian LibriSpeech clips and six SOVA RuDevices clips from the local research manifest. Each model received the same mono 16 kHz audio at its original level, with one unscored warm-up. Clip lengths were **4.08–9.99 s**. Word error rate (WER) is the number of word insertions, deletions, and substitutions divided by the 151 reference words. Times include the speech gates and completed batch transcription, and exclude capture, waiting for a chunk, and translation. [Russian LibriSpeech source](https://www.openslr.org/96/) · [SOVA dataset source](https://github.com/sovaai/sova-dataset).

| Windows local option | Word edits / reference words | WER | Median processing | Observed range | Processing faster than audio duration |
| --- | ---: | ---: | ---: | ---: | ---: |
| Balanced / Whisper Base | 51 / 151 | 33.8% | 1.51 s | 1.39–1.97 s | 12 / 12 |
| Larger / Whisper Small | 31 / 151 | 20.5% | 4.93 s | 4.74–5.47 s | 8 / 12 |

Small reduced the errors on both sources in aggregate, from 26 to 11 on Russian LibriSpeech and 25 to 20 on SOVA. Individual results varied. One SOVA clip had three word edits with Small and two with Base. Neither option returned a blank transcript on these original-level clips. The measured sample is small and does not represent every accent, a live conversation, or game audio mixed with music.

**Why the English captions can take longer than the table.** Windows normally begins looking for a break after collecting about four seconds of audio. It looks for a roughly 800 ms quiet tail or for audio input to stop, and forces a chunk around 12 seconds if it keeps receiving sound. There is a separate short-chunk path when input stops. Recognition and translation then share a serial semaphore, so a slow translation can also hold up the next chunk. The capture queue has two slots and can drop a chunk when it stays full. These are code paths that can cause lag or missing lines. I did not measure how often they happened in the user's session. [Capture logic](../../Core/AudioCaptureService.cs) · [Caption processing](../../MainWindow.xaml.cs).

## 2. Voice detection and missing words

The Windows capture stage can reject low-energy chunks. The local speech service also runs a sustained-energy check **before** the Silero neural detector. I replayed the same 12 clips through those two detectors independently after applying fixed attenuation, without changing the waveform or adding noise. The last column counts clips Silero detected that the earlier local-service energy check would reject. This measures a specific cause of missing speech. It does not measure accuracy on a noisy microphone or show that removing the energy check improves overall caption quality. [Energy check](../../Core/WhisperSttService.cs) · [Silero VAD](https://github.com/snakers4/silero-vad).

| Gain relative to original clip | Energy check detected | Silero detected | Silero-positive clips blocked before Silero |
| ---: | ---: | ---: | ---: |
| 0 dB | 12 / 12 | 12 / 12 | 0 |
| −18 dB | 9 / 12 | 12 / 12 | 3 |
| −24 dB | 4 / 12 | 12 / 12 | 8 |

The result supports testing Silero earlier in the capture pipeline. Evaluate missed short or quiet speech alongside false captions on silence, music, and noise before changing the default. If a line never appears in the original Russian captions, changing the English translator cannot recover the missing audio.

## 3. Google Translate and MyMemory fallback

The app's **Google Translate** engine uses a key-free `translate.googleapis.com/translate_a/single` request. It calls MyMemory if Google fails or returns no usable text. The two calls are sequential, each with a six-second HTTP timeout. An immediate 429 and a request that times out have different total latency. The app does not use the documented Google Cloud Translation API for this mode, so Google Cloud quotas and glossary support do not describe this route. Both online routes receive text. [App provider code](../../Core/Translation/GoogleFreeTranslateProvider.cs) · [Google Cloud API overview](https://docs.cloud.google.com/translate/docs/api-overview) · [MyMemory API](https://mymemory.translated.net/doc/spec.php).

I sent eight short, authored Russian sentences straight into the app's translation provider, with explicit `ru` → `en`. I compared the returned meaning with an English reference. In the direct run Google returned HTTP 200 for all eight and no fallback was used. In the fallback run the test injected a Google 429 locally, then let the app call the real MyMemory endpoint. This is evidence about fallback output, not evidence that Google rate-limited this run.

| Tested route | Meaning preserved | Median request-to-result | Observed range |
| --- | ---: | ---: | ---: |
| Direct Google | 7 / 8 | 0.60 s | 0.05–1.43 s |
| MyMemory after injected 429 | 6 / 8 | 0.70 s | 0.51–1.48 s |

| Russian input | Intended English | Direct Google output | MyMemory fallback output |
| --- | --- | --- | --- |
| Не открывайте дверь. | Do not open the door. | Don't open the door. | **Open the door** |
| У меня руки не доходят до этого. | I haven't gotten around to it. | **I can't get my hands around it.** | **My hands don't get to it.** |

Both providers preserved the meaning of the other six examples, covering disagreement, prices, times, names, and the two meanings of `лук` (onion and bow). These are eight selected examples, not accuracy percentages for either service. A previous live check found a separate reversal in Indonesian → English after an actual Google rate limit. [Earlier release verification](../cpu-release-verification.md).

## What to try and what to fix

1. **For the current preview:** stop audio, set source to **Russian**, target to **English**, and try **Balanced** with Google on the same audio. Expect faster transcription and compare the original Russian captions because Balanced made more recognition errors on this sample. If the Russian line is already wrong or missing, investigate capture, detection, and transcription. If it is correct but English is wrong, investigate translation and the active provider.
2. **Before release:** show the actual translation provider, remove the “No Quota” wording, and make the fallback explicit or user-controlled. Preserve the original Russian text if the translation route fails. Add the Russian negation, number, and idiom cases to route acceptance checks. Compare an optional LLM translator on the same reference text, with meaning preservation and latency measured together. No Groq request or paid comparison was run here.
3. **Improve live responsiveness:** measure capture wait, voice detection, recognition, translation, and end-to-caption delay separately on timed Russian playback. Track dropped and duplicated chunks. Compare the current behavior with earlier neural speech detection and a shorter speech-boundary wait. Try the larger model only where the quality gain is worth its latency on the target hardware. Include the user's audio conditions in the next benchmark once a reproducible example is available.

## Reproduction

Run from the private candidate repository root. The Russian manifest used here has SHA-256 `ab859fe7348051378d10416fe5ac704eb9b5d0729b3aed709c0e56d567cdeb13`. The app verifies the cached Base, Small, and Silero artifacts. The speech probe emits per-clip timing and edit counts without transcripts. The live translation probes send only the eight synthetic text fixtures in the source file.

```powershell
dotnet run --project tests/LocalCpuChecks/LocalCpuChecks.csproj -c Release -- --ru-caption-report 'B:\Projects\live translator\offline-research\russian-focus-20261103\manifest.jsonl'
dotnet run --project tests/AccuracySmoke/AccuracySmoke.csproj -c Release -- --ru-report
dotnet run --project tests/AccuracySmoke/AccuracySmoke.csproj -c Release -- --ru-report --force-fallback
```

The probes and report do not change runtime model selection, voice detection, translation, or published releases.
