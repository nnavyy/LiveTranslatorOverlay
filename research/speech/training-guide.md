# Run the speech experiment yourself

## What the three splits do

| Round 3 split | Speech recordings | Synthetic controls | Use |
|---|---:|---:|---|
| Training | 160 | 24 | Update the model using audio and its reference transcript. |
| Validation (development or dev) | 40 | 12 | Compare checkpoints and choose settings. |
| Test | 80 | 12 | Score the frozen choice once on held-out recordings. |

Each split has equal numbers of English and Russian speech recordings. Keep a recording, its sentence, and altered copies in one split. For live conversation, also separate speakers and recording sessions. A known test set is fine for reproducing an experiment. When changing the method in response to a test result, reserve fresh audio for the next final claim.

## Reproduce Round 3 on the existing Windows machine

Open PowerShell. The installed environment, local data, pretrained Tiny model and Round 2 adapter are already available under the project folder. This command starts from Round 2 and writes to a new folder. It trains the small set of adjustable weights called an adapter for one pass through the training set.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
$trainer = '.\research\speech\train_expanded.py'
$data = "$r\continuation-data-02\manifest.jsonl"
$model = "$r\whisper-tiny-169d4a4"
$parent = "$r\expanded-run-2026-11-03"
$run = Join-Path $r ('my-training-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $py $trainer develop $data $model $run --parent-run $parent
if ($LASTEXITCODE -ne 0) { throw 'Development failed. Inspect the error above.' }
Get-Content "$run\selection.json"
```

`developmentEligible` records whether this checkpoint passed the experiment's selection rule. It can be true at much higher than 3% WER. After reviewing it, the same PowerShell session can run the original test protocol.

```powershell
& $py $trainer test $data $model $run --parent-run $parent
if ($LASTEXITCODE -ne 0) { throw 'Test failed. Inspect the error above.' }
Get-Content "$run\locked-test\candidate-test-summary.json"
```

Read `speech.en.wer` and `speech.ru.wer` as fractions. A value of 0.15 means 15%. The summary also lists word errors, reference words, blank speech transcripts, and nonspeech clips that produced words. The [setup and reproduction notes](results/2026-11-03-continuation/README.md) cover preparing the inputs again.

## Interpreting an approximately 97% target

Word error rate (WER) is 100 times the sum of substituted, deleted and inserted words divided by reference words. If 100 reference words yield two substitutions and one deletion, WER is 3%. A caption can have a low WER and still reverse the meaning by dropping “not.” Translation needs its own meaning assessment.

Define the product target before the experiment. For example, require WER at or below 3% **in each language** on previously unseen recordings representative of the app. If an approximate target is acceptable, define the tolerance in advance. A 3.201667% WER gives 96.798333% on the scale of 100 minus WER. Report counts and a confidence interval alongside a rounded percentage. On 700 words, one additional word error moves the score by about 0.14 percentage points.

The [2 November project manuscript](paper/manuscript.md) reports pilot test WER of 22.73% English and 19.35% Russian. Both the pretrained model and pilot adapter had those scores. In the later experiments, the same pretrained Tiny baseline scored 38/387 (9.82%) on Round 2 English validation and 64/455 (14.07%) on Round 3 English validation. Different recordings explain why those numbers are not a before/after training comparison. Within Round 3, the parent and continued adapter scored 15.82% and 14.51% on the same English validation audio. [Full training history](../../docs/reviews/2026-11-03-expanded-ml.md)

## Designing a stronger experiment

1. **Establish a baseline on the intended audio.** The best existing local prototype result used pretrained Whisper Small with Silero speech detection. It reached 6.04% English and 13.57% Russian WER on Round 2 read speech. Compare Small and a stronger candidate on the same development clips and hardware, recording both WER and caption delay.
2. **Label the failures before choosing a method.** Review substitutions, missing words, names, numbers, noise, overlap, and cut-off words. A recognizer change, a segmentation fix and a language-specific adapter address different errors. Silero helped with false captions on nonspeech. Its whole-clip gate preserved the speech WER in that experiment.
3. **Compare training and validation on fixed audio.** High error on both can indicate underfitting, bad labels, or a scoring mismatch. Falling training error with rising validation error across checkpoints suggests overfitting. Retain the best validation checkpoint and test it on new speakers and sessions. Track each language separately to catch gains in one at the expense of the other. The existing runs record training loss and validation WER. A firm diagnosis needs comparable training and validation WER curves.
4. **Confirm the claim once.** Freeze model, decoding, and scoring rules before testing. Report each language and audio condition, paired uncertainty for changes, missed speech, false captions, caption delay and a separate bilingual translation review. Reaching 3% on clean speech would establish that result only for the tested conditions.

For perspective, Table 2 of the [Whisper paper](https://cdn.openai.com/papers/whisper.pdf) reports 2.7% WER for Large V2 on LibriSpeech Clean, 4.4% on FLEURS English, and 17.6% on CallHome. [Singh and Warstadt](https://aclanthology.org/2026.scil-main.37/) report nearly a 30% average relative WER reduction from language-specific fine-tuning of Whisper large-v3 across 81 FLEURS languages. A 30% relative reduction starting at 10% WER yields 7% WER. These are reasons to benchmark an approach on our data before adopting it.
