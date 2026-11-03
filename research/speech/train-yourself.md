# Train the Russian and English recognizer on this Windows machine

**Compare checkpoints on the same audio.** Round 4 and Round 5 both started from Round 3. The Round 3 Russian WER was 40.82% on 40 FLEURS test recordings and 55.72% on the later 600-recording Russian test. Round 5 reached 49.72% on that second test, six percentage points better than its parent. English worsened by 0.50 points, and nonspeech emissions increased from 21/90 to 90/90. Comparing 40.69% and 49.72% directly mixes changes in recordings and model. A head-to-head comparison of Rounds 4 and 5 needs both checkpoints decoded on identical clips.

**1. Start training.** Open PowerShell on the machine with the prepared data. Paste this block as one command sequence. It uses the verified 70/15/15 dataset and starts from the Round 3 adapter. Keep the PowerShell window open. Each new run gets its own directory.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
$r = (Resolve-Path '..\offline-research').Path
$py = "$r\training-venv\Scripts\python.exe"
$data = "$r\russian-focus-20261103"
$model = "$r\whisper-tiny-169d4a4"
$parent = "$r\continuation-run-01"
$run = Join-Path $r ('my-russian-training-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $py research/speech/train_expanded.py develop "$data/manifest.jsonl" $model $run --parent-run $parent --midpoint-checkpoint --phase-limit-seconds 14400
if ($LASTEXITCODE) { throw 'Training failed. Inspect the last error.' }
$choice = Get-Content -Raw "$run/selection.json" | ConvertFrom-Json
$choice
```

The trainer evaluates pretrained Tiny and the parent, makes one update for each of the 3,920 training examples, and now evaluates both steps 1,960 and 3,920. Of the candidates passing the existing criteria against **both** comparators, it selects the lowest mean validation WER. If neither qualifies, it records a diagnostic choice with `developmentEligible = false`. Adding a halfway checkpoint lets you retain an earlier improvement if further training loses it. Its effect on the full dataset still needs measurement. The previous final-only development run took 89 minutes on this CPU. Allow additional time for the extra validation pass.

**2. Measure false captions on validation.** Run this in the same PowerShell window. It builds the local Silero probe and measures how the selected checkpoint behaves when transcription is suppressed on clips with no detected speech. Include missed speech in WER.

```powershell
dotnet build tests/SpeechGateProbe -c Release
if ($LASTEXITCODE) { throw 'Probe build failed' }
$probe = (Get-ChildItem tests/SpeechGateProbe/bin/Release -Recurse -Filter SpeechGateProbe.exe | Select-Object -First 1).FullName
$vad = "$r/ggml-silero-v6.2.0.bin"
& $probe $vad "$data/dev.jsonl" | Set-Content -Encoding utf8 "$run/dev-gate.jsonl"
if ($LASTEXITCODE) { throw 'Gate evaluation failed' }
foreach ($name in @('baseline', 'parent', $choice.checkpoint)) {
    Write-Output $name
    & $py research/speech/score_gate.py "$run/$name-dev.json" "$run/dev-gate.jsonl"
    if ($LASTEXITCODE) { throw 'Score verification failed' }
}
```

On the saved Round 5 checkpoint, this fixed detector reduced validation control emissions from 90/90 to 0/90. Three of 600 Russian speech clips became blank. Russian WER moved from 3,256/6,481 to 3,257/6,481 (50.24% to 50.25%). English stayed at 275/3,104 (8.86%). This is a runtime change measured on known validation clips with synthetic silence, tones and noise. The adapter's original acceptance decision and weights are independent of this diagnostic.

**3. Try a recording.** Supply a mono 16 kHz WAV of up to 20 seconds and the source language. The command loads the selected adapter for detected speech. The detector uses only the audio.

```powershell
& $py research/speech/transcribe_clip.py $run "$r/jfk.wav" --language en --speech-gate $probe $vad
```

**4. Freeze your choice before testing.** For reproduction on the previously reported test split, use the separate command below once per run. It verifies the manifest and checkpoint hashes and writes to `locked-test`. For a new accuracy claim after changing the method, reserve fresh test recordings and include real backgrounds and quiet speech. Report English WER, Russian WER, missed speech and control emissions side by side for each model.

```powershell
& $py research/speech/train_expanded.py test "$data/manifest.jsonl" $model $run --parent-run $parent --phase-limit-seconds 14400
if ($LASTEXITCODE) { throw 'Locked test failed' }
& $py research/speech/show_scores.py $run
```

The trainer reports raw recognizer scores. To reproduce a gated comparison on that same historical test, repeat step 2 with `test.jsonl`, `test-gate.jsonl` and the three saved prediction files `locked-test/baseline-test.json`, `locked-test/parent-test.json` and `locked-test/candidate-test.json`. A gated result applies to the speech detector and recognizer together. Live overlay performance requires an end-to-end evaluation.
