# Russian-focused speech dataset · 3 November 2026

This dataset prepares the next English/Russian speech-recognition experiment after Round 4. It contains 4,000 Russian recordings, 1,000 English recordings and 600 nonspeech controls. No training or prediction is part of the preparation procedure.

| Source | Training | Validation | Locked test | Total |
|---|---:|---:|---:|---:|
| Russian · RuDevices everyday speech | 1,400 | 300 | 300 | 2,000 |
| Russian · RuLS audiobook narration | 1,400 | 300 | 300 | 2,000 |
| English · LibriSpeech clean audiobook narration | 700 | 150 | 150 | 1,000 |
| Synthetic silence, tones and noise | 420 | 90 | 90 | 600 |
| **All clips** | **3,920** | **840** | **840** | **5,600** |

Every source follows **70/15/15 by recording count**. Russian makes up 80% of the speech examples in every split. Giving validation and test the same size leaves 600 Russian recordings in each, including 300 from everyday speech. This supports separate estimates by recording condition. In Round 4, the checkpoint emitted words on ten of twelve test controls. Each new evaluation split has 30 silence, 30 tone and 30 noise clips, evenly divided between English and Russian decoder prompts. The controls are generated examples and do not measure performance on real music or ambient noise.

## Sources and credits

- **SOVA RuDevices** supplies everyday Russian speech recorded on devices with manual transcripts. Credit Virtual Assistant, LLC, Egor Zubarev, Timofey Moskalets and the SOVA.ai team. The source and the bond005 packaging identify CC BY 4.0. We selected a subset and rewrote its audio as mono 16 kHz PCM WAV. [SOVA project](https://github.com/sovaai/sova-dataset) · [RuDevices distribution](https://huggingface.co/datasets/bond005/sova_rudevices/tree/d9197eacfb0afff29d90a2d4e7d0d98a5dfb54bc) · [License](https://creativecommons.org/licenses/by/4.0/)
- **Russian LibriSpeech (RuLS)** supplies LibriVox narration aligned to Russian book text. Credit NVIDIA Corporation (2020), the LibriVox readers and the dataset packager Ivan Stupakov. OpenSLR and the distribution's README describe the original corpus as public domain in the USA. The distribution also carries a CC BY 4.0 metadata tag. We retain both notices and use the supplied normalized Russian text. [OpenSLR 96](https://www.openslr.org/96/) · [Pinned distribution and credits](https://huggingface.co/datasets/istupakov/russian_librispeech/tree/a519c986bb3342cc8136d3d14e5ad8a4f1e1a2bd)
- **English LibriSpeech** supplies clean read speech from the train-clean-100, development-clean and test-clean partitions. Credit Vassil Panayotov, Guoguo Chen, Daniel Povey, Sanjeev Khudanpur and the LibriVox readers. The corpus is CC BY 4.0. We selected recordings, converted lossless audio to PCM WAV and lowercased the supplied English transcripts. [OpenSLR 12](https://www.openslr.org/12/) · [Pinned distribution](https://huggingface.co/datasets/openslr/librispeech_asr/tree/71cacbfb7e2354c4226d01e70d77d5fca3d04ba1) · [License](https://creativecommons.org/licenses/by/4.0/)
- The project generated the three nonspeech conditions with empty transcripts and a fixed seed (2026110305). They are provided under [CC0](https://creativecommons.org/publicdomain/zero/1.0/).

## What the split protects

The builder retains the published training, development and test assignments. It selects from twelve pinned source shards. Within each corpus and split, it cycles through known readers in deterministic hashed recording order. The sample is quota-based. It cannot establish population-level speaker or accent coverage.

Each selected recording decodes to finite mono audio at 16 kHz lasting 1–20 seconds. Every output WAV has a file checksum and a second checksum over its PCM samples. No selected recording repeats by decoded PCM or by normalized speech transcript, either within this package or against the four earlier project manifests. Known reader IDs and audiobook chapter groups stay in one split. RuDevices publishes no speaker or session IDs, so speaker independence cannot be established for that portion. The Russian audiobook development portion contains one narrator. Report the two Russian domains separately in any future evaluation.

## Local handoff

The prepared directory on this machine is `B:\Projects\live translator\offline-research\russian-focus-20261103`. It contains 16-bit WAVs under `audio/` and four UTF-8 JSONL files with relative paths. The combined `manifest.jsonl` is the input format expected by the existing trainer. The files `train.jsonl`, `dev.jsonl` and `test.jsonl` are exact partitions for tools that load each split separately. Keep the test file reserved until a future model and decoding configuration are frozen. These examples supervise source-language transcription. They do not supply bilingual translation references.

The [source inventory](sources.json) records the revision, URL, size and SHA-256 of every upstream shard. The [audit](audit.json) records durations and known reader counts by split and source, plus checksums for the manifests. Speech, transcripts and model weights stay outside Git.

## Rebuild the data only

From the repository root, the following PowerShell commands reproduce the dataset on this machine using the existing source cache and previous manifests. Use a new destination. The builder verifies every source checksum, checks prior recordings, and publishes the output only after the checks pass.

```powershell
$r = (Resolve-Path '..\offline-research').Path
$cache = "$r\librispeech-source-20261103"
$py = "$cache\reader-env\Scripts\python.exe"
& $py research/speech/prepare_russian_data.py $cache $r "$r\russian-focus-rebuild"
if ($LASTEXITCODE) { throw 'Dataset preparation failed' }
Get-FileHash "$r\russian-focus-rebuild\manifest.jsonl" -Algorithm SHA256
```

For a new cache, download each URL in `sources.json` to its listed filename. The twelve shards total 4.74 GB. The preparation environment uses Python 3.12, PyArrow 20.0.0, SoundFile 0.13.1 and NumPy 2.2.6. The four earlier dataset directories must contain the manifests and WAVs pinned by the builder. They are needed to check decoded-audio and transcript overlap.

## Verification on the prepared files

An independent pass reopened all 5,600 WAVs, checked their PCM and file hashes, verified the three partition manifests against the combined manifest, and repeated the prior-data and split-overlap checks. The pinned Whisper Tiny tokenizer accepted every reference. The longest targets, including prompt and end tokens, were 109 tokens in Russian and 79 in English. The feature extractor produced finite 80 × 3,000 inputs for a sample from each of the 15 source, language and split combinations. No model weights were loaded or updated.

The package totals 9.55 hours including controls. Russian speech accounts for 5.59 hours across all splits and 3.81 hours in training. The complete training, validation and test partitions contain 6.93, 1.32 and 1.30 hours respectively. The [frozen selection](selection.csv) lists each output recording, split, originating shard and row where applicable, and WAV checksum. The existing CPU trainer limits each phase to one hour. Its runtime on this larger dataset has not been measured.
