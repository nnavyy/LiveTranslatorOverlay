# Speech hallucinations, local training and overlapping speakers

Experiment date: November 1, 2026. Review completed November 2, 2026. Production baseline: v1.5.1 / commit `4542a3f`.

## Decision and current status

Use pretrained local machine-learning models, evaluate each stage separately, and fine-tune only when a controlled comparison demonstrates an improvement. Training a recognizer from scratch is not necessary to remove the dependency on an inference provider. Whisper already is a trained neural model; local execution changes where it runs, not whether it can hallucinate.

This pass reviewed primary research, implemented a reproducible local training/evaluation workflow, trained a small LoRA adapter, and compared a pretrained neural voice detector with the actual application's energy gate. **The trained adapter is rejected for release:** test scores did not improve, and Russian validation WER worsened. The neural gate performed better on this small pilot, but it is not yet integrated or broadly validated.

There is no 95% accuracy result, production model replacement, overlapping-speaker implementation, or new application release in this change. The complete [commands, data provenance and experiment records](../../research/speech/README.md) are versioned in Git.

## The reported caption

The screenshot shows Ukrainian “Дякую за перегляд!” with the corresponding English translation “Thank you for watching!” The owner reports that someone was speaking different words, and the source language was mixed or uncertain. This makes it a reported speech-recognition error, not a case that silence filtering alone can fix. The screenshot contains no original audio, so its exact decoding failure cannot be replayed or scored.

The saved settings inspected during this pass selected Ukrainian speech and English captions, using Groq Whisper. A fixed language can be wrong for mixed content; language selection needs its own test. For mixed or uncertain material, the existing Auto source option is preferable to forcing Ukrainian, although short-segment automatic detection can also be wrong. Settings were not changed automatically.

Code inspection found that the current “voice activity” check measures amplitude and frame energy, not learned speech features. It cannot reliably distinguish speech from tones/noise. The current text filter deliberately permits natural phrases: banning “Thank you for watching” would also remove a real spoken sign-off. Its Ukrainian spelling already occurs in the old phrase list; adding the same phrase again would not solve the acoustic problem. The “Speaker unclear / mixed” label is separate from the wrong transcript.

## Research reviewed

| Primary source | Relevant evidence | Application to this project |
| --- | --- | --- |
| [Careless Whisper, FAccT 2024](https://arxiv.org/abs/2402.08021) | Documents invented phrases and an association with non-vocal durations in the studied recordings | Include pauses and missed-speech checks. Its reported rates are not this app's rates. |
| [Investigation of Whisper ASR Hallucinations Induced by Non-Speech Audio, 2025](https://arxiv.org/html/2501.11378v1) | Studies recurring hallucinated phrases, added silence/noise, decoding choices and text filtering | Compare VAD, context/decoding and phrase safeguards independently. Natural phrase matches need acoustic evidence. |
| [Calm-Whisper, Interspeech 2025](https://arxiv.org/html/2505.12969v1) | Fine-tunes selected decoder heads with non-speech audio and empty labels, evaluating both hallucinations and English WER | A candidate training method, not a drop-in rule. Head indices depend on the tested architecture; English results do not establish Russian performance. |
| [How to Reduce Whisper Hallucination, September 2026 preprint](https://arxiv.org/html/2609.32560v1) | Compares negative-only training against mixtures containing genuinely spoken hallucination phrases; also reports multilingual degradation after fine-tuning | Train with both speech and non-speech. Measure legitimate phrase recovery and multilingual regression. This recent preprint has not been independently replicated here. |
| [LoRA, Hu et al.](https://arxiv.org/abs/2106.09685) | Describes low-rank adaptation with frozen pretrained weights | Useful for smaller training experiments. Lower training cost does not guarantee better recognition. |
| [FLEURS, Conneau et al.](https://arxiv.org/abs/2205.12446) | Provides multilingual speech and references for reproducible evaluation | Use publisher splits and keep train/test data separate. Read speech is insufficient to represent games, conversational overlap or code switching. |
| [pyannote.audio 2.1, Interspeech 2023](https://www.isca-archive.org/interspeech_2023/bredin23_interspeech.html) | Describes segmentation, neural speaker embeddings and clustering, with an adaptation recipe | Evaluate speaker assignment as a distinct model task with annotated speaker turns. |
| [SepFormer, ICASSP 2021](https://arxiv.org/abs/2010.13154) | Separates simultaneous voices using a trained separation network | Detecting overlap and recovering two concurrent utterances require different capabilities. Benchmark the extra delay and separation errors. |

The training decision follows these papers' experimental reasoning, not their headline scores. In particular, a reduction in invented words can come from suppressing real speech. The September 2026 study explicitly reports worse FLEURS results after fine-tuning despite improvements elsewhere; training loss alone would miss that failure.

## What was actually trained and tested

The pilot used 30 public FLEURS recordings, 15 per language, plus nine generated silence/tone/noise controls. Splits were 19 training clips, nine validation clips and 11 test clips. Original dataset splits and sentence groups were preserved. The subset was chosen deterministically from archive order, not randomly, and is too small for an accuracy claim.

Training ran locally on the CPU using multilingual `whisper-tiny`, rank-4 LoRA on decoder query/value projections, 49,152 trainable parameters, 20 optimizer steps and a fixed seed. This small model made a bounded engineering experiment possible; it is not a substitute for Groq's larger model or the intended production quality model. Prompt tokens were excluded from the loss while transcript/end-of-text targets were learned. The full run, including baseline/candidate evaluation, took about 51 seconds after setup.

| Measure | Pretrained baseline | After pilot fine-tuning |
| --- | --- | --- |
| English validation WER, 69 reference words | 18.84% | 18.84% |
| Russian validation WER, 55 reference words | 30.91% | 32.73% |
| English test WER, 88 reference words | 22.73% | 22.73% |
| Russian test WER, 62 reference words | 19.35% | 19.35% |
| Test non-speech clips producing words | 3/3 | 3/3 |
| Test speech clips completely suppressed | 0/8 | 0/8 |

These are raw local recognizer results under the documented greedy decoding setup. They are not measurements of the full application's cloud path or its downstream confidence filters. No model-selection decision used the test set; the one candidate was evaluated and rejected. Further development needs a new locked test set because this pilot's results are now known.

The separate gate experiment ran the **actual production energy-check function**, then pretrained Silero v6.2.0 at a fixed 0.5 threshold on the same files. No VAD weights were trained or thresholds tuned.

| Test-split gate result | Current energy check | Pretrained neural VAD |
| --- | --- | --- |
| Speech clips accepted | 6/8 | 8/8 |
| Non-speech clips accepted | 2/3 | 0/3 |

Across all 39 clips, VAD processing took approximately 1.31 seconds, excluding setup/model loading. This is component timing, not live-caption latency. The current gate's acceptance of a non-speech clip does not prove downstream Whisper will hallucinate on it. Conversely, VAD passing a speech clip cannot prove its transcript is correct. The three test negatives are synthetic, not real music or game audio.

**Result:** prioritize a validated neural speech gate plus stronger recognition evaluation. The tiny training run provides a reproducible failure record, not a model to distribute. Full machine-readable settings, per-step losses, per-clip metrics, hashes and gate decisions are in the [experiment folder](../../research/speech/results/2026-11-01-pilot).

## What “95%+” should mean

Use separate acceptance criteria instead of one combined percentage:

- **Recognition:** a proposed target of WER at or below 5% on a defined, representative held-out set, reported separately for Russian, English and mixed speech. Report confidence intervals and errors in names, numbers and negation. WER is an edit-rate metric, not a probability that each caption is correct, and it may exceed 100%.
- **Hallucinations:** words emitted on verified non-speech, measured both per clip and per hour. A proposed target is at most 1%, with uncertainty reported. Zero errors in three controls is nowhere near enough evidence for that claim.
- **Speech preservation:** missed or blanked real utterances, especially quiet speech, short replies and genuinely spoken sign-offs. A filter that hides everything must fail acceptance.
- **Translation:** bilingual meaning review and reference-based metrics for every one of the 34 target choices. Recognition WER does not establish translation accuracy. Track important factual errors separately from stylistic differences.
- **Speaker attribution:** diarization error rate with overlap included, speaker-confusion rate, overlap detection and speaker-attributed word errors. Do not hide difficult overlapping regions by excluding them from scoring.
- **Usability:** sustained caption delay, dropped chunks, RAM/VRAM, source switching and responsiveness on supported Windows and Mac hardware.

These are proposed engineering targets, not achieved scores or universal guarantees. A claim must name the dataset, conditions, model version, decoding settings and measured uncertainty.

## Data and training program

1. **Build a representative corpus.** Keep clean Russian/English references, real background noise/music, quiet and clipped speech, language switching, multi-speaker overlap and paired spoken sign-offs. Use publisher-licensed audio or explicit consent. Never collect users' playback or feedback attachments automatically. Synthetic speech/noise can augment training but must not be the sole acceptance evidence.
2. **Prevent leakage.** Split by original recording/session/speaker and shared sentence, not by randomly cutting one recording into train/test chunks. Audit near-duplicates and translated versions. Use train for weight updates, validation for settings/checkpoint selection and a locked test set for final assessment. Keep raw benchmark text out of publicly indexed result logs.
3. **Run controlled comparisons.** Baseline; baseline plus neural VAD; context/chunking/decoding changes; balanced speech/non-speech LoRA; architecture-specific fine-tuning if its implementation is verified. Include real positive examples of phrases the model tends to invent. Change one method at a time before testing combinations. Preserve failed runs, seeds, manifests, loss curves and regression results.
4. **Separate translation training.** Speech fine-tuning will not train all target-language translators. Use licensed parallel training corpora if domain adaptation is needed. [FLORES+](https://huggingface.co/datasets/openlanguagedata/flores_plus) is an evaluation resource, not training material; respect its access conditions and CC-BY-SA license. The [existing offline feasibility review](2026-11-01-offline-accuracy.md) retains all 34 targets as a requirement.
5. **Evaluate real non-speech.** The generated controls are only a smoke test. [MUSAN](https://www.openslr.org/17/) is a documented CC-BY-4.0 candidate for music/noise evaluation and augmentation. Keep corpus subsets and licenses recorded, and verify that selected negatives really contain no intelligible speech.
6. **Approve the exported runtime model.** Verify adapter merging/export, then remeasure after quantization. A Python training result does not automatically carry over to whisper.cpp. Test Windows and both Mac architectures, cold start and offline operation. Publish model licenses, checksums, compatibility and rollback information.

## Two people speaking at once

The required sequence is overlap-aware speaker segmentation, speaker embeddings/clustering, optional speech separation, then recognition and translation aligned to each speaker. Diarization says who spoke when; separation attempts to recover each voice waveform. A single mixed waveform can still lose words, even when two speakers are detected.

Use stable session labels **Speaker 1** and **Speaker 2** and show simultaneous captions only when the model supports both tracks. When attribution is uncertain, retain an explicit mixed/unknown state instead of inventing a confident assignment. Allow the user to name speakers. Voice pitch alone does not reliably determine whether someone is a man or woman; gender labels should be user supplied rather than presented as acoustic facts.

[Pyannote's local community pipeline](https://huggingface.co/pyannote/speaker-diarization-community-1) is a candidate to evaluate, with its model access and redistribution terms handled during developer setup. This pass did not download its gated weights, train diarization, or demonstrate overlapping-voice separation. A production choice must meet the requirement that ordinary users need no provider account/key. Do not substitute a cloud diarization API and call it local.

## Intended user experience

Training remains a developer activity. The user chooses playback source, source language or Auto, and target language. A simple local-mode setup installs verified model packs with clear size/progress, then runs without an inference key. No training controls, learning rates, dataset paths or Python installation appear in normal use.

Finalized captions enter history once, with source/translation and conservative speaker labels. Feedback can include an explicitly reviewed diagnostic export; recording is opt-in. Update notices should explain verified improvements and known limits in plain language, with the technical model/data records linked for interested users. Never advertise “95% accurate” from this pilot.

## Verification and remaining work

The pilot executed locally, saved a real trained adapter and rejected it on measured results. Six metric/leakage unit tests passed. The Windows gate probe built and processed all 39 records. Recomputing aggregate scores from saved predictions matched the report. The production build succeeded with the existing `NU1510` warning about `System.Drawing.Common`. Application regressions passed: RegressionChecks 12/12, AuditChecks 9/9 and SpeakerChecks 13/13, for 34 application checks plus six research checks.

The original screenshot's error is not reproduced from audio and is not claimed fixed. Larger representative training/evaluation, the real spoken-sign-off challenge, mixed-language recordings, overlap-aware diarization/separation, all-target translation evaluation and Windows/Mac integration remain required before a production accuracy release.
