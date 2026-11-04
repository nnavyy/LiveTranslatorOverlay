# Accuracy round preparation and verification — 4 November 2026

**Prepared and smoke-tested; the full benchmark is reserved for the user's manual run. No new full-dataset accuracy claim or weight training is reported.**

The current completed result remains English 3.67%, Russian 5.70%, mean 4.69%. The next command, expected outcomes, scientific sources, overlap limitation and decision rules are in [next-accuracy-round.md](../../next-accuracy-round.md).

## Changes

- Added independent development selection for English and Russian, preserving the current candidate on ties or regressions in WER, blank speech or nonspeech captions.
- Added pinned Distil-Whisper large-v3 English greedy/beam-5 inference and pinned GigaAM v3 RNNT Russian inference. Small beam 5 is also eligible. Existing audited baseline and Small beam-5 development predictions are reused.
- Preserved the original 70/15/15 manifest and fixed Silero gate. Selection is saved before historical test. Test results do not alter selection. Raw/gated predictions and scores are retained separately.
- Added C: model/result storage, streamed resumable downloads with upstream hash verification, per-stage timing/ETA and memory logs, a total wall clock and automatic reports/target checks.
- Recorded language milestones of English <=2.67% and Russian <=4.70%, separate from the <=3% mean and both <=2% stretch goals. These are targets, not promised outcomes.

## Storage action

The resolved pip cache was verified as `C:\Users\akung\AppData\Local\pip\Cache` before invoking `python -m pip cache purge`. Pip reported **464 files removed, 3076.2 MB**. No model/data/checkpoint directory was deleted. Immediate C: free bytes rose from 20,062,863,360 to 23,109,943,296; B: remained approximately 1.18 GB free. New model assets were subsequently downloaded to C:, so that immediate freed-space figure is not the final available space.

All protected historical model/data/report hashes checked by the benchmark passed, and the completed specialist audit was rerun successfully. Deleting disposable pip downloads does not change trained model weights. The new model assets should now be treated as protected inference inputs, not disposable package cache.

## Verification evidence

- 4 accuracy-round unit tests passed: independent English selection, baseline preference on ties, blank/control regression rejection, and target arithmetic.
- 5 existing specialist protocol tests and 6 metrics tests passed (15 total).
- The completed specialist report audit passed after cleanup, including unchanged source artifact hashes and recomputed historical scores.
- The actual Windows PowerShell 5.1 wrapper completed a CUDA smoke run: `accuracy-smoke-20261104-153435` under C: local app data.
- The smoke run executed seven new decoding stages: English Distil greedy/beam 5 on dev and test, Small beam 5 on test, Russian v3 on dev and test. Each split sampled one recording per language/kind/corpus, five recordings total. Scores from these few clips are not full-set accuracy evidence.
- All seven stages completed, and the report, frozen selection, predictions, timing and memory logs were written. Total wall time was about **1.5 minutes** including setup verification. Peak PyTorch GPU allocation was about **2052 MiB**, with sampled free host RAM as low as **475 MiB**. These measurements are not guaranteed full-run peaks or runtime estimates.

The compact machine-readable evidence is [verification.json](verification.json). Complete local smoke artifacts remain under the C: run directory recorded there. The user should run the full command once and inspect the resulting report before choosing a training intervention.

## Limits and next decision

The English source is LibriSpeech `train.clean.100`, and Distil lists 960 hours of LibriSpeech training. Corpus overlap is known; exact recording overlap has not been audited. Even a 2% historical English result would not prove 2% accuracy on unseen live speech. Russian also requires fresh confirmation because this historical test has been repeatedly inspected.

If a new language candidate improves development but fails historical test guardrails, retain the current system. If targets remain unmet, inspect residual errors and collect representative new training/development data instead of repeating identical commands. Any eventual application integration needs native-runtime accuracy, memory, routing and latency verification. No application version or release is changed here.
