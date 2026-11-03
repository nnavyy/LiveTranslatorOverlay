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

The training command prints the input and output paths, split sizes and progress. Training steps show loss. After decoding a development or test split, the terminal shows WER percentages, errors and reference words by language, blank speech, and false captions on controls. To print percentages from an already completed run, use the same PowerShell session:

```powershell
& $py research/speech/show_scores.py $run
```

Python reads each WAV into memory on the CPU. No audio player, microphone or overlay window needs to open. To inspect an actual clip or compare the first five saved test predictions with their reference transcripts:

```powershell
$clip = Get-Content -Encoding UTF8 $data -TotalCount 1 | ConvertFrom-Json
Join-Path (Split-Path $data) $clip.path
$predictions = Get-Content -Raw -Encoding UTF8 "$run\locked-test\candidate-test.json" | ConvertFrom-Json
$predictions | Select-Object -First 5 language, path, reference, prediction | Format-List
```

## Test your saved adapter on one recording

Run this in PowerShell on the existing Windows machine to transcribe the local 11-second JFK sample with your trained adapter. The command loads the selected checkpoint, prints its transcript and exits. Supplying the words you expect to hear also prints the clip's WER. The reference is used only after decoding.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
$run = "$r\my-training-20261103-132129"
$probe = '.\research\speech\transcribe_clip.py'
& $py $probe $run "$r\jfk.wav" --language en --reference 'And so my fellow Americans ask not what your country can do for you ask what you can do for your country'
```

Replace the WAV path with your own 16 kHz mono clip of up to 20 seconds. Choose `--language ru` for Russian. Omit `--reference` to see the transcript alone. To convert the first 20 seconds of a phone recording, run `ffmpeg -n -i "C:\path\recording.m4a" -t 20 -ac 1 -ar 16000 -c:a pcm_s16le "$r\my-clip.wav"` and pass the output WAV to the probe. Score against what was spoken in those 20 seconds, including any hesitations you want counted. You can try this on new clips even after a run's locked test has finished.

## Try 1,000 or 2,000 training recordings

These counts refer to speech in the training split. With 500 English and 500 Russian clips, the trainer gets 1,000 speech recordings plus 24 controls. Development still has 40 speech clips plus 12 controls, and test has 80 plus 12. At the previous run's average speech duration, 1,000 clips give about 2.8 hours of training speech and 2,000 give about 5.6 hours. Setup streams public FLEURS recordings over the internet. Training then reads the downloaded files locally. Each training or test phase currently has a one-hour time limit.

On this Windows machine, use a new PowerShell session or paste the entire block. Set `$perLanguage` to 500 or 1000 before starting. The example starts from the completed Round 3 adapter and excludes the earlier experiments' sentence groups and audio hashes.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
$trainer = '.\research\speech\train_expanded.py'
$model = "$r\whisper-tiny-169d4a4"
$parent = "$r\continuation-run-01"
$perLanguage = 500
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$folder = "$r\fleurs-$perLanguage-per-language-$stamp"
$run = "$r\training-$perLanguage-per-language-$stamp"
$indices = '.\research\speech\results'
& $py research/speech/prepare_expanded.py $folder "$indices/2026-11-01-pilot/dataset-index.json" --exclude-index "$indices/2026-11-03-expanded/dataset-index.json" --exclude-index "$indices/2026-11-03-continuation/dataset-index.json" --control-series 2 --train-per-language $perLanguage
if ($LASTEXITCODE -ne 0) { throw 'Dataset preparation failed. Inspect the error above.' }
$data = "$folder\manifest.jsonl"
& $py $trainer develop $data $model $run --parent-run $parent
if ($LASTEXITCODE -ne 0) { throw 'Development failed. Inspect the error above.' }
& $py research/speech/show_scores.py $run
```

To compare sizes, start both candidates from the same parent, check their validation clip IDs and hashes match, compare validation WER by language, and examine false captions as well. These runs use one update per training example, so doubling the data also increases training compute. Choose the method on validation before running the test command above once for the final candidate. More FLEURS read speech leaves conversational accuracy an open question.

### How research datasets compare

For a **20-hour training experiment**, one possible design is 10 hours of labeled English and 10 of Russian, plus separate validation and test sessions. Summing the sample counts in our pinned FLEURS training metadata yields 7.49 hours of English and 8.05 hours of Russian before exclusions. The 1–20 second duration filter alone reduces this to about 7.15 and 7.56 hours. Additional licensed or permissioned conversational audio would be needed. The trainer enforces a one-hour limit per phase and saves adapter weights at configured checkpoints, so measure throughput on a smaller subset before committing to this size.

On the FLEURS benchmark, Table 13 gives English/Russian WER of 12.4%/31.1% for Whisper Tiny, 6.1%/11.4% for Small and 4.2%/5.6% for Large V2. These are separate from our convenience subsets, normalization rules and C# runtime. A fair comparison would run the models on identical recordings with identical scoring and report caption latency on the intended machine. [Whisper paper, Table 13](https://cdn.openai.com/papers/whisper.pdf#page=24)

One dataset can contain thousands of recordings. Research papers often describe the hours per language and the task. FLEURS contains about 12 hours per language across 102 languages. LibriSpeech contains about 1,000 hours of read English. The original Whisper model was pretrained on 680,000 hours of multilingual, multitask supervision. Here we adapt an already pretrained model, so the relevant test is how much additional labeled audio improves held-out performance. [FLEURS paper](https://arxiv.org/abs/2205.12446) · [LibriSpeech corpus](https://www.openslr.org/12/) · [Whisper paper](https://arxiv.org/abs/2212.04356)

### Could users contribute training examples?

A proposed workflow is an optional **Contribute this correction** action. A contributor would preview a short shareable clip and correct the source transcript. For translation, a bilingual reviewer would also supply or verify the target text. The first pair teaches the recognizer what was said. The second teaches or evaluates how to convey its meaning. A generated caption by itself can repeat the model's own mistakes.

Ordinary use would leave training collection off. Playback can contain other people's voices or third-party media, so accept only clips the contributor has permission to share for training. Group recordings by speaker and session before splitting. Keep a separate sample of ordinary usage for evaluation because voluntarily reported mistakes overrepresent failure cases. This describes a product design proposal. The current research commands read public and synthetic clips. Wispr Flow separately documents its user setting for sharing dictation audio, transcripts and edits. [Wispr Flow data controls](https://wisprflow.ai/data-controls)

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
