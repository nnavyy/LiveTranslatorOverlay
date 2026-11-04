# Next English and Russian accuracy experiment

The current historical result is **English 3.67%, Russian 5.70%, equal-language mean 4.69%**, with zero captions on 90 nonspeech controls. The roughly 23.93% Russian row is the previous Small comparison model. It is not the selected specialist's score.

English stayed unchanged because the reported specialist system retained the same Small adapter and greedy decoder for English. Repeating that command does not update its weights. The previous selection defect also selected greedy despite a better English development result with beam 5; that defect is corrected. Beam 5's development score is 3.58% versus greedy's 3.77%, but its full historical test result is not yet measured.

## Run this command

From Windows PowerShell 5.1, on the prepared machine:

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\speech\benchmark-accuracy-round.ps1
```

This compares pretrained models and decoders; **it does not train weights or install a model into the app**. A new training run is not the best first step while stronger ready-made recognizers and the English decoder remain untested. The CUDA environment and original offline research folder are required. There are no new dependency installations.

New models are stored at `%LOCALAPPDATA%\LiveTranslatorOverlay\accuracy-round-models`. Each run creates `%LOCALAPPDATA%\LiveTranslatorOverlay\accuracy-round-results\accuracy-round-<timestamp>`, on C: on this machine. Read `report.md`; retain `report.json`, `configuration.json`, `selection.json`, `progress.jsonl`, `wall-clock.json` and the prediction files. The script prints the exact path. It preserves earlier results and uses a four-hour evaluation time limit. A failed run records `failure.json` where possible; incomplete results are not a completed benchmark.

Expected runtime is provisionally **20–60 minutes excluding downloads**, not a measured full-run promise. The first short GPU check took about a minute including setup verification; a few clips cannot predict a complete run reliably. The previous full specialist run took 55 minutes. This script reuses audited baseline predictions and runs only the development-selected changes on historical test, reducing redundant work. Stage progress every ten clips includes measured seconds per clip, ETA, process RAM, free RAM and GPU allocation. `wall-clock.json` includes setup time. New models run sequentially. Close unused memory-heavy applications: low host RAM can cause paging even when GPU memory fits.

To check execution only, add `-Smoke`. That decodes a handful of recordings through every new model/decoder path, including test paths; its scores are **not accuracy measurements** and it does not publish target passes.

## What changed, and why it may help

English candidates are current Small greedy, the accepted Small adapter with beam 5, and **Distil-Whisper large-v3** with greedy and beam 5. Russian candidates are current **GigaAM v2 RNNT** and **v3 RNNT**. Each language is selected separately on development data. A Russian gain cannot hide an English regression. The current candidate wins exact ties; a new candidate must not increase word error rate, blank speech or false captions. At least one language must improve before a full run evaluates the frozen selection on historical test.

The [Distil-Whisper paper](https://arxiv.org/abs/2311.00430) studies knowledge distillation using quality-filtered pseudo-labels. We use an already distilled model, avoiding the cost of reproducing its training. The [large-v3 model card](https://huggingface.co/distil-whisper/distil-large-v3) documents its English specialization and inference settings. This is evidence for trying the method, **not proof it will beat our adapted Small on these clips**. Models/revisions and hashes are pinned; English uses float16 SDPA, one recording at a time.

The [pinned GigaAM evaluation](https://github.com/salute-developers/GigaAM/blob/7447938d791c4f3e643386ee22c33777004293a5/evaluation.md) reports Russian LibriSpeech WER of 4.4% for v3 RNNT versus 5.2% for v2, and natural-speech WER of 6.9% versus 10.3%. V3 does not win every dataset: Golos Crowd is 2.4% versus 2.3%. These are upstream results under their protocol, not predictions for ours. Retain v2 if v3 fails our development checks.

## Expectations registered before your full run

Expected direction: at least one new recognizer or decoder may reduce errors. **No improvement is guaranteed, and there is no defensible fixed number of repeated runs that reaches 2%.** Identical benchmark runs do not learn anything. These numbers are acceptance targets, not forecasts:

| Measure | Current historical test | Next milestone | Stretch target |
|---|---:|---:|---:|
| English WER | 3.67% (102/2780) | <=2.67% | <=2% |
| Russian WER | 5.70% (366/6418) | <=4.70% | <=2% |
| Equal-language mean | 4.69% | 3.685% at the two milestone limits | <=2% |
| Nonspeech false captions | 0/90 | 0/90 | 0/90 |
| Blank speech on test | 0 | 0 | 0 |

The final **3% mean** goal is separate. English 2.67% and Russian 4.70% average 3.685%, not 3%. For example, English 2% and Russian 4% average 3%. Both at 2% average 2%.

On the existing historical test, the next language milestones require at most **74 English errors** and **301 Russian errors**: at least 28 and 65 fewer errors respectively. That is about a 27% relative reduction in English errors and 18% in Russian, so one percentage point is substantial at these low starting scores. WER counts substitutions, deletions and insertions divided by reference words; it is not the percentage of sentences that are completely wrong.

## How to act on the report

- If development does not improve, keep the current system. Inspect incorrect words and source conditions before choosing another model or collecting targeted data.
- If development improves but historical test regresses, retain the current system and investigate. Do not change thresholds to make that test pass.
- If English alone improves, retain the Russian v2 candidate; if Russian alone improves, retain the current English candidate. The report records independent choices.
- If both improve but targets are unmet, audit remaining errors by corpus and speaker. Correct bad labels in a separately versioned dataset, collect missing accents/noise/domain vocabulary, and plan adaptation using training data only. Do not repeatedly train on validation or test failures.
- If targets pass, validate on fresh speaker/session-disjoint recordings and the actual app runtime before integration. These Python recognizers are not automatically deployed into the desktop app.

The original 70/15/15 manifest, normalization and Silero threshold remain fixed. Raw and gated predictions are saved separately, and rejected speech is still counted as errors. Baseline predictions are reused only after recorded hashes and reference/audio metadata match. Their old timing is not mixed into new inference timing. Neither source language detection nor translation quality is measured.

**Pretraining overlap limits the English claim:** our English clips come from LibriSpeech `train.clean.100`; Distil's model card lists 960 hours of LibriSpeech in training. There is known corpus overlap, although exact retained recordings have not been matched. A lower English score here can demonstrate compatibility on this historical set, but cannot establish unseen-data improvement. The repeatedly used Russian test also needs fresh confirmation. A fresh, representative evaluation set is more valuable than chasing a 2% score on familiar clips.

## Storage cleanup performed

On 4 November 2026, the Python package manager removed **464 disposable pip cache files (3076.2 MB reported)** from `C:\Users\akung\AppData\Local\pip\Cache`. This affects future package download speed, not trained weights. Original adapters, checkpoints, model assets, audio, manifests, environments, gate decisions and experiment reports were preserved. The completed specialist audit was rerun successfully after cleanup.

C: free space increased from about 20.06 GB to 23.11 GB immediately after cleanup. The new model downloads subsequently use roughly 2 GB there. B: remains tight at about 1.18 GB free; no B: dataset/model folders were deleted. New experiment models/results use C: without resizing partitions or moving protected inputs. Disk space and available RAM are different resources.

Do not delete `offline-research`, accepted adapters/checkpoints, either model folder, manifests/audio or completed reports as generic cache. This command does not automatically clean or relocate them when a drive fills; it checks space and stops safely. The preparation/verification report is [here](results/2026-11-04-accuracy-round-prepared/README.md).
