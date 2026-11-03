# Round 4 · 1,000 new training recordings

The new checkpoint made eight fewer word errors than its Round 3 parent across 80 held-out speech recordings, but produced captions on eight more nonspeech controls. It failed development selection and remains a research artifact. The training speech totaled 84.2 minutes of English and 89.8 minutes of Russian.

| Split | Model | English WER (errors/words) | Russian WER (errors/words) | False captions |
|---|---|---:|---:|---:|
| Development | Pretrained Tiny | 12.19% (49/402) | 33.57% (94/280) | 12/12 |
| Development | Round 3 parent | 12.69% (51/402) | 39.64% (111/280) | 3/12 |
| Development | Round 4 | 12.94% (52/402) | 35.71% (100/280) | 10/12 |
| Locked test | Pretrained Tiny | 16.54% (135/816) | 39.63% (298/752) | 12/12 |
| Locked test | Round 3 parent | 17.40% (142/816) | 40.82% (307/752) | 2/12 |
| Locked test | Round 4 | 16.54% (135/816) | 40.69% (306/752) | 10/12 |

Round 4 had lower mean development WER than its parent (24.32% versus 26.16%), but higher mean WER than pretrained Tiny (22.88%). On the test controls, the parent emitted words on two noise clips. Round 4 emitted words on all four silence clips, all four noise clips and two of the four tones. Every model returned words on every development and test speech recording.

Against its parent, the test WER change was −0.86 percentage points in English (95% paired interval −2.29 to +0.58) and −0.13 points in Russian (−2.49 to +2.51). Both intervals include zero. These intervals use 10,000 paired resamples of sentence groups. Live conversation and translated captions were outside this evaluation. The [per-clip counts](clip-scores.csv) and [aggregate scores, intervals and checkpoint hashes](summary.json) preserve the comparisons.

## Experiment design

Start from the Round 3 English/Russian Whisper Tiny decoder adapter. Use one shuffled pass over 500 English and 500 Russian FLEURS training recordings and 24 synthetic nonspeech controls. Keep Round 3's learning rate (0.00005), optimizer reset, augmentation, explicit source language and greedy decoder. The only new checkpoint is step 1,024.

Use 20 development and 40 test speech clips per language plus 12 controls per split. Use the pinned FLEURS revision and its original splits. Reserve the first eligible archive entries after excluding the three previous dataset indices. Check audio hashes, sentence groups and normalized references for overlap before training. FLEURS supplies read speech. The controls contain silence, tones and generated noise.

Compare against the unchanged Round 3 parent and pretrained Tiny on identical clips. To pass development, the checkpoint must reduce the unweighted mean of English and Russian word error rates against **each comparator**. Neither language may regress by more than one percentage point. Additional blank speech transcripts in either language or additional false captions on controls also disqualify it. Freeze the selection, then evaluate the three models once on test. Report the test scores even if the development decision is negative.

## Dataset verification

Some FLEURS transcripts start with a literal quotation mark. The previous TSV parser joined several lines starting at English training recording `12278477339576674587.wav` and assigned it another recording's transcript and 167,040-sample length. The WAV has 131,520 samples. Round 4 treats quotation marks as transcript text. Every accepted speech recording in this round (1,120) and the three previous rounds (590) matched its source transcript and WAV sample count when checked against the pinned TSVs.

The [dataset index](dataset-index.json) identifies all 1,168 Round 4 files by recording, sentence group, split and SHA-256 for exclusion from future rounds. We found no recording, sentence group, audio hash or normalized speech reference shared with earlier experiments and no normalized reference shared across Round 4 splits. The source has no local speaker identifiers for verifying independence by speaker. A bounded byte-range download recovered from a network timeout while preserving source bytes and archive order.

## Reproduction

From the repository root, use the pinned local Python environment and the same Round 3 parent as in the [training guide](../../training-guide.md). Choose empty output paths for a new run.

```powershell
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
$i = 'research/speech/results'
$data = "$r\round4-data-500"
$run = "$r\round4-run-500"
$model = "$r\whisper-tiny-169d4a4"
$parent = "$r\continuation-run-01"
& $py research/speech/prepare_expanded.py $data "$i/2026-11-01-pilot/dataset-index.json" --exclude-index "$i/2026-11-03-expanded/dataset-index.json" --exclude-index "$i/2026-11-03-continuation/dataset-index.json" --control-series 2 --train-per-language 500
if ($LASTEXITCODE) { throw 'Data preparation failed' }
& $py research/speech/train_expanded.py develop "$data/manifest.jsonl" $model $run --parent-run $parent
if ($LASTEXITCODE) { throw 'Development failed' }
Get-Content "$run/selection.json"
& $py research/speech/train_expanded.py test "$data/manifest.jsonl" $model $run --parent-run $parent
if ($LASTEXITCODE) { throw 'Locked evaluation failed' }
```
