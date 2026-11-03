# English and Russian speech training history

## Reading the scores

Each training round has its own held-out recordings. Compare models **within one round and split**. English and Russian word error rate (WER) count substituted, deleted, and inserted words divided by reference words for that language. Lower is better. The control column counts how often the model produced text on synthetic silence, tones, or noise. Development scores guide checkpoint selection. Test scores measure the frozen choice.

## Round 1 · 1 November · 20 updates

We froze pretrained Whisper Tiny and trained 49,152 adapter parameters on its decoder attention layers (LoRA rank 4, alpha 8). Each optimizer update used one recording at a learning rate of 0.0001. Training used 16 read-speech clips and three synthetic controls with empty captions. The evaluation supplied the source language and used greedy decoding.

| Split and model | English WER | Russian WER | Controls emitting text |
|---|---:|---:|---:|
| Development · pretrained Tiny | 18.84% | 30.91% | 3/3 |
| Development · step 20 | 18.84% | 32.73% | 3/3 |
| Test · pretrained Tiny | 22.73% | 19.35% | 3/3 |
| Test · step 20 | 22.73% | 19.35% | 3/3 |

The two models made the same number of test errors in each language (20 per 88 English reference words and 12 per 62 Russian words). With only four test recordings per language and no observed gain, the pilot adapter stayed experimental. [Pilot configuration, losses and scores](../../research/speech/results/2026-11-01-pilot/report.json)

## Round 2 · 3 November · 320 updates

This experiment started again from pretrained Tiny. It doubled the adapter rank to 8 (98,304 trainable parameters), increased the training set to 160 speech recordings and 24 controls, and exposed the model to quieter speech and speech with added Gaussian noise. It retained the 0.0001 learning rate and compared checkpoints at 160 and 320 updates. Both checkpoints had to improve mean English/Russian WER while keeping each language within one percentage point of baseline and avoiding additional blank speech or control emissions.

| Split and model | English WER | Russian WER | Controls emitting text |
|---|---:|---:|---:|
| Development · pretrained Tiny | 9.82% | 36.36% | 12/12 |
| Development · step 160 | 14.21% | 40.76% | 10/12 |
| Development · step 320 | 12.40% | 41.35% | 3/12 |
| Test · pretrained Tiny | 11.96% | 36.74% | 12/12 |
| Test · step 320 | 12.54% | 38.61% | 4/12 |

Neither checkpoint passed. Step 320 had the better mean development WER of the two and went to diagnostic testing. It produced text on eight fewer test controls than pretrained Tiny, but added five English word errors and fifteen Russian errors on the same 80 speech clips. [Development record](../../research/speech/results/2026-11-03-expanded/training-report.json) · [Test scores](../../research/speech/results/2026-11-03-expanded/summary.json)

### What produced the earlier 6.04% and 13.57% scores

On the same Round 2 test recordings, the C# probe ran pretrained Whisper Small Q5_1 with an explicit language and greedy decoding. English errors fell from Tiny's 103/861 to 52/861 (6.04% WER), and Russian errors fell from 295/803 to 109/803 (13.57%). The benchmark changed model size, quantization, and runtime together, so it cannot isolate their individual contributions.

Small alone emitted text on all 12 synthetic controls. Replaying Silero's whole-clip speech decisions suppressed all 12 and preserved both WERs. Median complete-clip inference took 4.92 seconds for English and 5.60 seconds for Russian on the Windows CPU. Progressive caption latency still requires a live test. [Runtime and gate comparison](../../research/speech/results/2026-11-03-expanded/summary.json)

An independent offline translation check fed the same pretrained M2M100 translator either reference transcripts or Small's recognized text. English-to-Russian chrF2 fell from 49.17 to 46.95 across 39 pairs, and Russian-to-English fell from 55.92 to 51.86 across 40. Higher chrF2 indicates closer character-sequence agreement with the reference. One English clip lacked a paired Russian reference. All 68 route smoke checks returned text. Translation quality was scored only for these two directions. [Translation measurements](../../research/speech/results/2026-11-03-expanded/translation-summary.json)

## Round 3 · 3 November · 184 additional updates

We loaded Round 2's step-320 weights and trained once through 160 fresh speech recordings (27.1 minutes) and 24 fresh synthetic controls. We halved the learning rate to 0.00005 and initialized a fresh optimizer because the parent run saved adapter weights only. The resulting checkpoint has 504 training updates in its lineage. We kept one candidate and required it to meet the Round 2 development criteria against both the parent and pretrained Tiny.

| Development model | English WER | Russian WER | Mean WER | Controls emitting text |
|---|---:|---:|---:|---:|
| Pretrained Tiny | 14.07% | 28.98% | 21.52% | 12/12 |
| Parent · step 320 | 15.82% | 27.55% | 21.69% | 4/12 |
| Continued · 184 more updates | 14.51% | 28.27% | 21.39% | 4/12 |

The continuation passed development. Against its parent, it made six fewer English errors (72 to 66) and three more Russian errors (116 to 119). Mean WER improved by 0.30 percentage points and the Russian increase stayed within the one-point limit. All three models produced text on every development speech clip. The model and dataset hashes were frozen before the test command. [Run configuration and reproduction](../../research/speech/results/2026-11-03-continuation/README.md)

| Locked test model | English WER | Russian WER | Mean WER | Controls emitting text |
|---|---:|---:|---:|---:|
| Pretrained Tiny | 13.85% (118/852) | 36.43% (255/700) | 25.14% | 12/12 |
| Parent · step 320 | 15.73% (134/852) | 38.43% (269/700) | 27.08% | 3/12 |
| Continued · 184 more updates | 15.14% (129/852) | 36.86% (258/700) | 26.00% | 2/12 |

The final score is the unweighted mean of the two language WERs. Compared with the parent, the continuation removed five net English errors and eleven net Russian errors. Of the 40 English clips, six improved, four worsened, and 30 had the same error count. Of the 40 Russian clips, eight improved, three worsened, and 29 had the same count. None of the models returned a blank transcript on a test speech clip. [Per-clip counts](../../research/speech/results/2026-11-03-continuation/clip-scores.csv)

The estimated English change was −0.59 percentage points (95% paired interval −2.69 to +1.56), so the sample leaves the direction uncertain. The Russian change was −1.57 points (−3.11 to −0.27). Intervals resample whole sentence groups 10,000 times, pairing the two predictions on each recording. The test used read speech from one convenience sample and synthetic nonspeech controls. A single run cannot separate the effect of the extra recordings from the learning rate or additional updates. [Exact scores, intervals and checkpoint hashes](../../research/speech/results/2026-11-03-continuation/summary.json)

**Decision.** Retain the continued checkpoint for research. It outperformed its parent on this test and suppressed ten more false captions than pretrained Tiny, but pretrained Tiny still transcribed both languages with fewer word errors. The earlier Small-plus-Silero benchmark remains the strongest measured prototype result on the Round 2 recordings. Small was evaluated on a different split from Round 3, and live conversational performance remains unmeasured.

## Round 4 · 3 November · 1,024 additional updates

Starting from Round 3, one pass over 1,000 fresh speech recordings and 24 controls reduced held-out English word errors from 142 to 135 and Russian errors from 307 to 306. Both paired WER intervals included zero. False captions increased from 2/12 to 10/12 on the same test controls. The checkpoint failed development selection and stayed experimental. The run also uncovered a source TSV parsing bug. After correcting it, we checked transcript and duration alignment for every speech clip accepted across all four rounds. [Round 4 scores and reproduction](../../research/speech/results/2026-11-03-round4/README.md)
