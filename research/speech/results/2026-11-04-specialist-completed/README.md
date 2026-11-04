# Completed specialist benchmark: Russian 5.70%, English 3.67%

**The new Russian system is not still at 23%.** On the same historical test, Russian WER fell from **23.93% with the previous Small model to 5.70% with GigaAM**. The 23% row is the comparison baseline. This run meets the requested Russian <=7% milestone, but English <=3% and equal-language mean <=3% remain unmet. These are offline research results; the desktop app has not been switched to GigaAM.

Run: `specialist-benchmark-20261104-125809`. No weights were trained. It compared the pretrained GigaAM v2 RNNT Russian recognizer with the accepted Small adapter, retaining Small for English and using the same fixed Silero gate for every system.

## What 23.93% means

WER = `(substitutions + deletions + insertions) / number of reference words`. A substitution replaces a wrong word, a deletion restores a missing word and an insertion removes an extra predicted word. The previous Small model made **1,536 word-edit errors across 6,418 Russian reference words**: `1536 / 6418 * 100 = 23.93%`. This is roughly 24 word errors per 100 reference words, not 24% of sentences completely wrong. It is also not simply a probability that each word is wrong; insertion errors can make WER exceed 100%.

| Same historical test recordings | Previous Small baseline | New Russian specialist + English greedy |
|---|---:|---:|
| Russian WER | 23.93% (1,536/6,418) | **5.70% (366/6,418)** |
| English WER | 3.67% (102/2,780) | 3.67% (102/2,780) |
| Equal-language mean WER | 13.80% | **4.69%** |
| Blank speech | 0/750 | 0/750 |
| Synthetic controls emitting captions | 0/90 | 0/90 |

The specialist removed **1,170 Russian errors**, a **76.17% relative reduction** and an 18.23-percentage-point WER reduction. English is unchanged because this completed test used exactly the same accepted Small model and greedy decoding for English. The mean is `(RU WER + EN WER)/2`, not a pooled word-error calculation.

| Russian test source | Previous Small | New specialist |
|---|---:|---:|
| Everyday speech (SOVA) | 25.14% (590/2,347) | 6.52% (153/2,347) |
| Narrated speech (RuLS) | 23.24% (946/4,071) | 5.23% (213/4,071) |

The paired parent-sentence cluster bootstrap interval for Russian candidate-minus-baseline WER was approximately **-19.85 to -16.87 percentage points** (10,000 repeats, seed 20261103). This supports a substantial difference on these recordings. Convenience sampling, unknown overlap with external pretraining data and incomplete speaker independence still limit generalization; the previously examined historical test is not a fresh deployment test.

## Validation, the speech gate and the selector defect

| Same validation recordings, fixed gate | Russian WER | English WER | Mean WER | Blank speech | False captions |
|---|---:|---:|---:|---:|---:|
| Previous Small | 23.31% | 3.77% | 13.54% | 3 | 0/90 |
| Specialist + English greedy | 6.60% | 3.77% | 5.19% | 3 | 0/90 |
| Specialist + English beam 5 | 6.60% | **3.58%** | **5.09%** | 3 | 0/90 |

Raw validation Russian WER was 23.28% for Small and 6.51% for GigaAM. The fixed detector rejected three SOVA speech clips for both systems, increasing WER to 23.31% and 6.60%. These rejected clips remain in the score. Raw recognizers already emitted no captions on the 90 synthetic controls, and raw/gated test scores were identical. This is evidence to inspect those validation gate rejections, not permission to tune the detector on the historical test or conclude that real-world detection is unnecessary.

**Audit finding:** both English decoding options passed validation guardrails. The specialist selector inherited a Russian-only ranking rule from the earlier continuation helper. Since Russian scores were identical, it incorrectly preferred beam 1 even though beam 5 had the lower mean WER. That differed from the stated selection protocol. This was an implementation error, not a reason to claim English could not improve.

A regression test reproduced the failure (`1 != 5`). The specialist selector now ranks eligible candidates by equal-language mean WER, using beam 1 only for an actual mean tie. It records policy `eligible-lowest-macro-mean-v2`. All 23 research tests pass, and applying the corrected rule to the saved development scores selects **beam 5**. The continuation selector is unchanged.

The completed run's original selection, predictions and test results were preserved. The **5.70% Russian / 3.67% English / 4.69% mean** result is valid for the system actually tested. Beam 5's validation score of 3.58% is not a test score; its English test performance remains unmeasured. The original test cannot be described as the result of the corrected selection policy. Future reports lead with the selected system's result and label the old Small row as a comparison baseline.

## Expected versus observed

The registered expectation was that a Russian specialist might substantially improve Russian, while English might remain near the existing score unless beam search helped. Russian exceeded the requested <=7% milestone on both validation and historical test. English did not reach <=3%, including the measured beam-5 validation result. Mean test WER is 4.69%, so the final <=3% mean goal was not met.

At Russian 5.70% and English 3.00%, mean WER would still be approximately **4.35%**. English improvement alone to its milestone is insufficient: further Russian improvement is also needed to reach the final goal. Russian 3% plus English 3% would satisfy it. These are arithmetic targets, not forecasts or promises of a fixed number of training runs.

## Runtime and memory

Total benchmark time was **3,299.33 seconds (54 min 59 sec)**, excluding dependency installation and model download. Development Small decoding took 23 min 5 sec; English beam-5 validation 5 min 24 sec; Russian specialist validation 7 min 15 sec; test Small decoding 12 min 24 sec; and specialist test decoding 5 min 23 sec. Other loading, verification and reporting overhead accounts for the remainder. Different clip lengths and system conditions affect stage rates, so these are not controlled per-model speed ratios.

Peak tracked GPU allocation was **1,582 MiB**, with **1,794 MiB reserved**. Sampled process resident RAM reached **1,538.5 MiB**, private committed memory reached **6,688.3 MiB**, and available system RAM fell to **42.5 MiB**. Resident RAM, committed memory and VRAM are distinct measures. There was memory pressure; the counters do not isolate time lost to disk paging. The completed run was within the provisional 30–90-minute planning range.

## Does deleting cache affect learning?

Learned parameters live in model/checkpoint files. Temporary caches do not accumulate accuracy, and clearing a download cache will not reduce WER. However, a folder called a cache can contain the only local copy of required pretrained weights.

| Local folder or artifact | What deletion would mean |
|---|---|
| pip download/wheel cache, commonly `%LOCALAPPDATA%\pip\Cache` | Does not change installed model weights or trained adapters. A later installation may need to download/build packages again. |
| `%LOCALAPPDATA%\LiveTranslatorOverlay\asr-specialist\v2_rnnt.ckpt` | This is the Russian pretrained model itself. Deleting it prevents direct inference until restored. The provided PowerShell benchmark runs setup first and can download the same checksum-verified model again. Restoring identical bytes does not reset or improve its learned recognition. |
| `offline-research\whisper-small-973afd2` | Required pretrained Whisper weights, tokenizer and provenance. Deleting it breaks the English/base-model path until restored; the specialist wrapper does not recreate this directory. |
| `offline-research\my-small-training-20261103-213758\adapter-4340` | Your accepted learned LoRA adapter. Preserve or back it up. It is not a disposable download cache and cannot be restored by simply downloading pretrained Whisper. |
| `offline-research\russian-focus-20261103` | Dataset and manifest used to reproduce the measurements/training. Deletion does not change surviving weights, but prevents this benchmark/training workflow from running. |
| Specialist benchmark predictions/reports | Audit evidence, not newly trained weights. Deleting them loses the ability to verify these results cheaply. Other training-run directories also contain adapters, so do not treat all run folders as logs. |
| PyTorch's temporary GPU allocator cache | Runtime memory reuse, not learned parameters. Releasing it does not retrain the model and is separate from deleting files on disk. |

No cache, checkpoint or dataset was deleted during this audit. Restoration must use the same verified model files for comparable results. Do not delete inputs from a running experiment. If storage cleanup is needed, identify the exact directory first rather than assuming everything named cache is disposable.

## What is needed next

1. Keep this specialist result as a successful Russian research baseline. There is no need to repeat the failed Small-training command to recover an improvement that has already been measured.
2. Evaluate the corrected validation-selected English beam-5 decoder in a separately labeled follow-up. The Russian predictions can be reused after checking their identities/hashes; another Russian training pass is not required to assess this decoder correction. Any repeated historical test remains a diagnostic; use fresh confirmation data for new generalization claims.
3. Inspect the remaining Russian and English errors, including names, numbers, reference conventions, audio quality and the three gated-out validation clips. Those causes have not been established by listening in this audit. Add accurately transcribed, representative data only where the error analysis supports it, preserving group-disjoint evaluation. Licensed public held-out recordings can be used if personal recording is not possible.
4. Reaching 3% mean still needs improvement beyond the achieved Russian milestone. Choose a focused English or Russian model/data experiment from the residual errors, protect the other language and report goals separately.
5. App integration is separate work: GigaAM has a different runtime/model format, and source language was supplied in this benchmark. Confirm live latency, switching behavior, nonspeech handling and fresh-audio accuracy before deployment.

## Reproduction and verification

[Exact audit data](summary.json) records scores, source breakdowns, goals, timing, memory, uncertainty, the historical/corrected decoder choices and input hashes. [Audit script](audit.py) recomputes raw and gated metrics, validates prediction metadata against the manifest, checks model/gate hashes and confirms the original files were not changed. No training or decoding was performed for this audit.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
& '..\offline-research\training-venv\Scripts\python.exe' .\research\speech\results\2026-11-04-specialist-completed\audit.py '..\offline-research'
```
