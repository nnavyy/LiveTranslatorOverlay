# Completed GPU continuation: no qualifying accuracy gain

Run: `my-small-refinement-20261104-081814`. **Keep the previous Small adapter.** The continuation finished successfully as an experiment but failed its accuracy acceptance rule. Russian validation WER increased slightly; English decreased slightly. There is no new test score because the workflow correctly skipped test decoding after failed validation. The earlier historical test results of 23.93% Russian and 3.67% English remain results of the accepted parent, not of this new adapter.

## Same recordings, same runtime

| Validation measure | Parent | Continued | Change |
|---|---:|---:|---:|
| Russian WER | 23.2834% | 23.3915% | +0.1080 percentage points; worse |
| Russian word errors / reference words | 1,509 / 6,481 | 1,516 / 6,481 | 7 more errors |
| English WER | 3.7693% | 3.6727% | -0.0966 percentage points; better |
| English word errors / reference words | 117 / 3,104 | 114 / 3,104 | 3 fewer errors |
| Blank speech clips | 0 / 750 | 0 / 750 | Unchanged |
| Synthetic controls emitting captions | 0 / 90 | 0 / 90 | Unchanged |

Both models were decoded on identical 840 validation clips using CUDA float32, SDPA attention, explicit language and greedy decoding. The accepted parent's validation scores match its earlier CPU report at the aggregate level. Comparing this candidate's 23.39% validation WER to the parent's 23.93% historical **test** WER is invalid: the recordings and word totals differ. The new English validation 3.67% also happens to round to the same percentage as the old English test score; those are different measurements.

By Russian source, everyday speech (SOVA) changed from 537/2,294 errors (23.41%) to 543/2,294 (23.67%); narrated speech (RuLS) changed from 972/4,187 (23.21%) to 973/4,187 (23.24%). Neither source improved overall.

## Why the scores moved so little

Of 150 English validation clips, 140 retained the same word-error count, six improved and four worsened. Only ten normalized transcripts changed. Of 600 Russian clips, 524 retained their error count, 41 improved and 35 worsened; 125 normalized transcripts changed. More Russian clips improved than worsened, but the number of word errors added on worsened clips exceeded the number removed elsewhere. WER counts word edits, not the number of clips that improved.

This run reused the same training recordings, a frozen Whisper Small backbone and decoder-attention rank-8 LoRA. It used one extra pass, fresh AdamW, a learning rate of 0.00001, and seed 20261104. The acoustic encoder is not directly trained by these decoder-only adapters. English already had relatively few remaining errors, so a three-error change moves its WER by only about 0.10 points. The low learning rate and limited adaptation are plausible reasons for small changes; this experiment does not isolate a causal explanation or establish that no different training method could help.

Paired parent-sentence cluster bootstrap intervals (10,000 repeats, seed 20261103) for candidate-minus-parent WER change were:

| Language | Observed change | Approximate 95% interval, percentage points |
|---|---:|---:|
| Russian | +0.108 | -0.405 to +0.697 |
| English | -0.097 | -0.344 to +0.123 |

Both intervals include zero. These convenience-sampled validation clips do not establish a population-wide improvement or regression. Sentence clusters account for related examples but do not guarantee speaker/session independence. The strict predeclared rule still rejects the candidate because Russian did not improve. The <=22% goal and <=20% stretch target were not demonstrated; no candidate test result was collected.

## Time and memory

| Stage | Measured elapsed |
|---|---:|
| Parent validation | 12 min 23 sec |
| 4,340 training updates | 23 min 44 sec |
| Candidate validation | 13 min 31 sec |
| Other workflow overhead | 1 min 42 sec |
| Total, excluding setup and final report writing | **51 min 20 sec** |

Training averaged **0.328 seconds/update**. The full workflow was shorter than the provisional 1–3-hour estimate partly because failed validation skipped both test passes. Do not infer an exact GPU speedup by dividing the previous nine-hour full workflow by this total: the runs performed different numbers of decoding passes.

Peak tracked PyTorch GPU allocation was **1,263.5 MiB**, with up to **1,406 MiB reserved**. Sampled process resident RAM peaked at **839.6 MiB**, while private committed memory reached **4,000.8 MiB**. These are different quantities: low displayed resident RAM does not mean the process needs only that much total memory. System available physical RAM fell as low as **64.2 MiB** during sampled progress. There was memory pressure, but these snapshots and page-fault counters do not prove how much time was lost to disk paging. GPU counters exclude other applications and some driver allocations; brief host-memory peaks between progress entries may be missed.

## Do not repeat the default command hoping it accumulates progress

`train-small-refine.ps1` defaults to the original accepted run `my-small-training-20261103-213758`. The trainer derives its seed as parent seed plus one. Repeating the unchanged command therefore uses the same parent, optimizer initialization, seed, data, learning rate and schedule in a new output folder. On the same runtime, expect essentially a replay, not a third epoch. Exact bitwise reproducibility across environments is not promised.

The script can accept another parent explicitly, but it rejects a parent that failed development. Do not edit the failed selection to force this adapter into the next run. Changing the seed alone is not evidence that the method improves recognition. This result supports stopping this exact recipe and keeping the accepted parent.

## Recommended next experiment

1. **Benchmark a Russian specialist before training again.** Compare a pinned GigaAM ASR checkpoint with the accepted Small parent on the same Russian validation clips and nonspeech controls. Measure WER, missed speech, latency, RAM and VRAM under identical scoring. Retain the English recognizer separately. This is a proposed experiment, not an implemented model replacement or a promised WER.
2. **Inspect a stratified sample of remaining errors and listen to the audio.** Include both Russian sources and high- and low-error clips. Distinguish recognition errors from reference errors, names/numbers, clipped audio, background speech and orthographic conventions. The current audit has not listened to the clips and does not assign those causes from text alone. Keep the present normalization fixed for model comparisons; report any alternate normalization as a separate diagnostic.
3. **Add accurately transcribed, diverse audio matching the intended app input.** Target observed failure categories rather than merely duplicating the current examples. Licensed public data is an option if personal recording is not feasible. Check duplicate audio/transcripts and speaker/session overlap; retain 70/15/15 assignments at an appropriate group level. Preserve the old benchmark for regression and reserve fresh sessions for final confirmation. Public benchmark scores may be affected by overlap with a pretrained model's training data.
4. **Choose the next training change from that evidence.** A stronger baseline, better-matched data, or adaptation of acoustic layers is a different hypothesis from another identical decoder-LoRA pass. Encoder/full-model training may require more memory. Set one experiment and acceptance rule in advance, then evaluate; do not infer a required run count from these two checkpoints.

## Is roughly 3% WER possible?

Yes, on some defined benchmarks and conditions; it is not a universal floor or a guarantee for live mixed Russian audio. Table 1 of Kutsakov et al. reports GigaAM RNNT at **2.7% on Russian MCV-19**, **3.9% on Golos Farfield** and **5.5% on Russian LibriSpeech**. Those published results use the authors' data/protocol and are not predictions for this project. [GigaAM, Interspeech 2025](https://www.isca-archive.org/interspeech_2025/kutsakov25_interspeech.pdf) · [Official model repository](https://github.com/salute-developers/GigaAM)

The retained parent's historical Russian test has 1,536 errors in 6,418 reference words. Below 3% permits at most 192 errors: at least 1,344 fewer, or 87.5% of the remaining errors removed. No defensible number of repeated runs follows from the current evidence. English is closer numerically, but this run's three-error validation gain does not establish how to get its held-out WER below 3%. A goal for any model must specify the audio, transcription task and scoring protocol.

## Audit and reproducibility

[Machine-readable audit](summary.json) includes exact scores, source breakdowns, configuration, selection, prediction changes, uncertainty, timing, memory and source hashes. The audit verified manifest and adapter hashes, split consistency, prediction-to-manifest identity, recomputed scores and the rejection rule. It confirmed that no `locked-test` directory was created. No training or decoding was performed for this report.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
& '..\offline-research\training-venv\Scripts\python.exe' .\research\speech\results\2026-11-04-refinement-completed\audit.py '..\offline-research'
```

The run's local `comparison.md` and `comparison.json` were regenerated with explicit error-count changes and a validation-only notice. Future reports include these distinctions automatically. Model weights and selection were not changed.
