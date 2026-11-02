# Local speech research and training

This is a developer research workspace. It does not install a model in the application or change the app's settings, capture sources, version, or update channel. End users will not need Python, notebooks, datasets or training commands in the intended product.

Read the [research review and decision](../../docs/reviews/2026-11-01-ml-training.md). The first [recorded experiment](results/2026-11-01-pilot/report.json) is **not approved for release**: its small adapter did not improve held-out speech recognition or non-speech hallucinations. The separate neural VAD comparison is promising on a tiny sample, not a validated fix for wrong words during speech.

The [research manuscript and audit package](paper/README.md), dated 2 November 2026, adds a PDF, 44 DOI-verified references with Scopus source-coverage evidence, recomputed statistical intervals, a technical dataset audit and a prospective speech/translation study protocol. It reports the negative pilot honestly and separates future experiments from measured results.

## Reproduce the pilot

Use a separate directory outside the repository for audio, models, the Python environment and run outputs. Internet is needed for setup downloads. Training/evaluation load only local files with Hugging Face offline mode enabled; no inference API is used. Downloads use pinned revisions and training verifies the recorded model/audio hashes.

Example in PowerShell, from the repository root:

```powershell
python -m venv C:\LocalCaptionResearch\venv
C:\LocalCaptionResearch\venv\Scripts\python.exe -m pip install torch==2.15.0 --index-url https://download.pytorch.org/whl/cpu
C:\LocalCaptionResearch\venv\Scripts\python.exe -m pip install -r research/speech/requirements.txt
python research/speech/download_model.py C:\LocalCaptionResearch\whisper-tiny
python research/speech/prepare_pilot.py C:\LocalCaptionResearch\pilot-data
C:\LocalCaptionResearch\venv\Scripts\python.exe research/speech/train_pilot.py C:\LocalCaptionResearch\pilot-data\manifest.jsonl C:\LocalCaptionResearch\whisper-tiny C:\LocalCaptionResearch\run-01 --steps 20
python -m unittest discover -s research/speech -p test_metrics.py
```

Run each setup command to completion before starting the next. Output directories must be new so previous experiments cannot be overwritten. The recorded installed environment is in [environment.txt](results/2026-11-01-pilot/environment.txt); the actual CPU torch runtime reported `2.15.0+cpu`. Package/build differences can change results.

`prepare_pilot.py` streams a bounded subset from each official FLEURS archive; it does not download entire archives or extract arbitrary archive paths. It selects the first eligible 1–20 second recordings: eight train, three dev and four test per language. It adds three generated controls to each split. This is deterministic convenience sampling, not a representative benchmark. If setup fails, use a fresh output directory after resolving the error.

The training script freezes the base model and trains only rank-4 adapters on the decoder's query/value projections. It masks prompt-token loss and includes the end-of-text token, including for empty non-speech targets. Batch size is one, AdamW learning rate is `1e-4`, clipping is `1.0`, seed is `20261101`, and the CPU uses four threads. A 30-minute limit is checked between records/steps. This intentionally small setup validates the workflow; it does not reproduce any paper's full training recipe.

Outputs include raw local predictions, an adapter, per-step losses, per-language WER, missed-speech counts, non-speech emission rates, configuration and package versions. The run is marked pilot-only regardless of score. No tuning or checkpoint selection uses the test split. Once these pilot test results have been examined, use a new locked test set for later claims.

The metric normalization is NFKC, lowercase, punctuation replaced by spaces, then whitespace tokenization. Word edit distance is summed over each language and divided by its total reference words; insertions can make WER exceed 100%. Numbers are not converted between words and digits. Empty-reference controls are scored separately, so invented text on silence cannot disappear behind an undefined WER. This is a documented pilot metric, not a substitute for a benchmark's official normalization.

## Speech gate comparison

The [SpeechGateProbe](../../tests/SpeechGateProbe/Program.cs) calls the actual production `WhisperSttService.HasSustainedVoiceActivity` and compares it with pretrained Silero VAD through Whisper.net. It uses no microphones, speech APIs or translation providers. It handles both FLEURS floating-point WAVs and PCM16 controls with the app's existing NAudio dependency. Each file is checked against the manifest hash, and VAD state resets between files.

Download the [pinned Silero v6.2.0 model](https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin) separately. The probe enforces SHA-256 `2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987`. This is a pretrained model comparison, not a newly trained VAD.

```powershell
dotnet run --project tests/SpeechGateProbe/SpeechGateProbe.csproj -c Release -- C:\LocalCaptionResearch\ggml-silero-v6.2.0.bin C:\LocalCaptionResearch\pilot-data\manifest.jsonl
```

The probe requires the same Windows/native prerequisites as the existing [local speech probe](../../tests/LocalSpeechProbe/README.md). It was tested on Windows 11 only. Native model weights stay outside Git. Its use of a Windows project reference is deliberate: it measures the current application's gate, not a rewritten approximation.

## Data and experiment records

- **Speech:** Google FLEURS, revision `70bb2e84b976b7e960aa89f1c648e09c59f894dd`, English and Russian. [Dataset card and attribution](https://huggingface.co/datasets/google/fleurs), [Conneau et al., 2022](https://arxiv.org/abs/2205.12446), CC-BY-4.0. The original splits are retained; the publisher documents separation of training speakers from dev/test speakers. The checker also rejects cross-split sentence-group and audio-hash leakage.
- **Controls:** locally generated silence, sine tones and Gaussian noise. No recordings of people; generated waveforms are offered under CC0-1.0. Seeds/frequencies/durations are in `prepare_pilot.py`. Different split parameters are useful plumbing checks, not independent real acoustic environments.
- **Base model:** `openai/whisper-tiny`, revision `169d4a4341b33bc18d8881c4b69c2e104e1cc0af`, MIT. Only safetensors and tokenizer/configuration data are downloaded. This tiny model was chosen for a bounded CPU training smoke test, not as the app's quality model.
- **Stored records:** [dataset index](results/2026-11-01-pilot/dataset-index.json), [training report](results/2026-11-01-pilot/report.json), per-clip metric files, [gate decisions](results/2026-11-01-pilot/gate-results.json), environment versions and artifact hashes. The index omits reference text but preserves its hash and upstream archive member for reproduction. Raw transcripts, audio and weights remain local.

There is no user-audio collection or background training. A future application diagnostic recording needs an explicit user action and a reviewable export. Feedback must not silently upload audio, transcripts or speaker embeddings.

## Next experiment protocol

Before running further variants, freeze a larger licensed dataset and a new test split with speaker/session/source separation. Include actual conversational Russian/English, code switching, clipped words, quiet speech, game/music backgrounds, real noise, overlapping speakers, and genuinely spoken closing phrases. Use synthetic examples for augmentation, with real recordings as the acceptance benchmark.

Compare baseline, VAD-only, decoding/context changes, balanced positive/negative LoRA, and any architecture-specific method one at a time. Select settings on validation data, then evaluate a locked candidate once on test data. Preserve failed and diverged runs. Training loss alone cannot approve a model.

Evaluate translation and speaker attribution separately. Translation keeps all 34 target choices as a requirement; this speech pilot trained neither the translation engine nor a diarization/separation model. The review specifies their datasets, metrics and product behavior.
