# Russian-focused training experiment · 3 November 2026

## Frozen protocol

Continue Round 3's selected step-184 decoder LoRA adapter for exactly one shuffled pass through the prepared 3,920-example training split. This uses 2,800 Russian speech clips, 700 English speech clips and 420 nonspeech controls. Validation and test each contain 600 Russian speech clips, 150 English speech clips and 90 controls. The split is 70/15/15 by recording count within every source. See the [dataset report](../2026-11-03-russian-data/README.md) for source licenses, provenance, duration and speaker limitations.

Choose Round 3 as the parent before evaluating this dataset because Round 4 regressed on nonspeech. Keep the existing rank-8, alpha-16 adapter architecture, learning rate 0.00005, fresh AdamW optimizer, batch size one, clipping at one, speech augmentation and seed 20261103. Freeze the base Whisper Tiny model. Save one final candidate at step 3,920. Decode with explicit source language, greedy search and a 128-token limit. Use local CPU execution with four threads. Raise the phase timeout from one to four hours to accommodate the larger validation and training sets.

Compare the candidate with pretrained Tiny and the unchanged Round 3 parent on the same validation recordings. Eligibility requires strict improvement in mean English/Russian WER against both comparators, neither language regressing by over one percentage point, and no additional blank speech or nonspeech emissions. Preserve this selection decision before evaluating the candidate, parent and pretrained model on the locked test split once. Report failed criteria and diagnostic test results as recorded. No hyperparameters are chosen from test results.

## Reproduction

From the repository root with the existing pinned Python environment, sources and Round 3 checkpoint available outside Git, use a fresh run directory. The actual run uses the following paths.

```powershell
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
foreach ($phase in @('develop', 'test')) {
    & $py research/speech/train_expanded.py $phase "$r/russian-focus-20261103/manifest.jsonl" "$r/whisper-tiny-169d4a4" "$r/russian-focus-run-20261103" --parent-run "$r/continuation-run-01" --phase-limit-seconds 14400
    if ($LASTEXITCODE) { throw "$phase failed" }
}
& $py research/speech/report_russian_run.py $r research/speech/results/2026-11-03-russian-training
```

## Results

The run completed all 3,920 updates over each training example exactly once. There are 98,304 trainable adapter parameters. Development, including training and three validation passes, took 88.84 minutes. The locked test phase took 33.87 minutes.

| Split | Model | English WER (errors/words) | Russian WER (errors/words) | Controls emitting words |
|---|---|---:|---:|---:|
| dev | baseline | 8.12% (252/3104) | 53.90% (3493/6481) | 90/90 |
| dev | parent | 9.15% (284/3104) | 55.45% (3594/6481) | 19/90 |
| dev | candidate | 8.86% (275/3104) | 50.24% (3256/6481) | 90/90 |
| test | baseline | 8.60% (239/2780) | 50.50% (3241/6418) | 89/90 |
| test | parent | 8.45% (235/2780) | 55.72% (3576/6418) | 21/90 |
| test | candidate | 8.96% (249/2780) | 49.72% (3191/6418) | 90/90 |

Every model returned words on every speech recording in both splits. The candidate improved mean validation WER against both comparators, and each language stayed within the permitted regression tolerance. It failed eligibility against the parent because control emissions rose from 19 to 90. That frozen rejection stands after diagnostic test evaluation.

## Russian results by source

| Split | Model | Everyday speech WER | Audiobook WER |
|---|---|---:|---:|
| dev | baseline | 60.33% | 50.37% |
| dev | parent | 57.67% | 54.24% |
| dev | 3920 | 50.57% | 50.06% |
| test | baseline | 50.11% | 50.72% |
| test | parent | 58.03% | 54.38% |
| test | candidate | 47.98% | 50.72% |

The candidate removed 385 net Russian test word errors relative to the parent (6.00 percentage points of WER), including a 10.05-point improvement on everyday speech. Against pretrained Tiny it removed 50 net Russian errors (0.78 points), while adding ten English errors.

## Uncertainty

| Candidate minus comparator test WER | English change, 95% interval | Russian change, 95% interval |
|---|---:|---:|
| parent | +0.50 pp (-0.04 to +1.06) | -6.00 pp (-9.97 to -3.73) |
| baseline | +0.36 pp (-1.23 to +1.74) | -0.78 pp (-2.46 to +0.75) |

Intervals use 10,000 paired bootstrap samples. Known audiobook readers are resampled as clusters. Other recordings use their recording groups because RuDevices supplies no speaker IDs. Correlation between RuDevices clips from the same unidentified speaker could narrow these intervals artificially. The Russian test comprises seven audiobook readers and 300 everyday recordings. The English test has 40 known readers. The Russian audiobook validation sample has one reader. The sample uses fixed source shards and quotas. It does not establish accuracy on all Russian speakers or live playback.

## Controls and processing time

| Test model | Silence captions | Tone captions | Noise captions | Median / 95th percentile decode seconds per clip |
|---|---:|---:|---:|---:|
| baseline | 30/30 | 29/30 | 30/30 | 0.781 / 1.468 |
| parent | 0/30 | 6/30 | 15/30 | 0.688 / 1.391 |
| candidate | 30/30 | 30/30 | 30/30 | 0.672 / 1.329 |

Decode timings measure local model generation after feature extraction. They exclude capture, segmentation, preprocessing, translation and overlay display. They do not measure end-to-end caption latency.

## Training diagnostics and decision

Mean loss across the first 100 shuffled updates was 1.0206; across the last 100 it was 0.7099. These use different examples and augmentation draws, so the difference is descriptive. Full losses and gradient norms are retained in [training-losses.json](training-losses.json).

Retain checkpoint 3,920 for research. Its Russian speech improvement against Round 3 came with false captions on all test controls. It does not pass the experiment's acceptance rule. These results evaluate source-language speech recognition. Translation accuracy, speaker attribution, real music rejection and application integration remain unmeasured.

The [summary](summary.json) includes exact scores, intervals, environment versions and parent, candidate and dataset hashes. The [domain scores](domain-scores.json), [per-clip counts](clip-scores.csv), [training report](training-report.json) and [locked test report](test-report.json) preserve the evidence. Audio, transcripts and adapter weights remain in the local run directory. Independent recomputation verified the audio checksums, all six score summaries, the frozen selection, the candidate weight hash and coverage of all training examples. Fifteen speech tests passed before execution.
