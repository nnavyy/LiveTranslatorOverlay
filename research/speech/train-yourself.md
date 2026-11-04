# Train the Russian and English recognizer on this Windows machine

For the next Russian accuracy experiment, use the [Whisper Small PowerShell workflow](results/2026-11-03-small-pilot/README.md). The instructions below reproduce the Tiny experiments.

The [completed Small run report](results/2026-11-04-small-completed/README.md) records accuracy, runtime, RAM limitations and proposed next targets. Future runs print stage timing and memory readings. Update the existing research environment with `& '..\offline-research\training-venv\Scripts\python.exe' -m pip install -r research/speech/requirements.txt` from the repository directory before using the updated scripts.

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
& $py research/speech/train_expanded.py develop "$data/manifest.jsonl" $model $run --parent-run $parent --midpoint-checkpoint --learn-empty-captions --phase-limit-seconds 14400
if ($LASTEXITCODE) { throw 'Training failed. Inspect the last error.' }
$choice = Get-Content -Raw "$run/selection.json" | ConvertFrom-Json
$choice
```

This run keeps all 3,500 speech examples and presents each of the 420 training controls twice, for 4,340 updates. It evaluates steps 2,170 and 4,340. The decoder can now end a transcript immediately when it hears no words. The same decoding setting applies to the base model, parent and candidate, and is saved for Step 4 and single-clip use. Keep each run in its own folder so the historical scores remain available.

Selection requires lower mean validation WER than both comparison models, at most one percentage point of regression per language, and no additional blank speech clips or control emissions. If neither checkpoint qualifies, the trainer saves the lower-WER diagnostic candidate and records `developmentEligible = false`. The earlier final-only development run took 89 minutes. Allow additional time for the extra training updates and validation pass. Full-run accuracy for this new option remains to be measured.

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

**3. Optional. Try a recording.** You can skip directly to Step 4. Supply a mono 16 kHz WAV of up to 20 seconds and the source language. The command loads the selected adapter for detected speech. The detector uses only the audio.

```powershell
& $py research/speech/transcribe_clip.py $run "$r/jfk.wav" --language en --speech-gate $probe $vad
```

**4. Freeze your choice before testing.** For reproduction on the previously reported test split, use the separate command below once per run. It verifies the manifest and checkpoint hashes and writes to `locked-test`. For a new accuracy claim after changing the method, reserve fresh test recordings and include real backgrounds and quiet speech. Report English WER, Russian WER, missed speech and control emissions side by side for each model.

```powershell
& $py research/speech/train_expanded.py test "$data/manifest.jsonl" $model $run --parent-run $parent --phase-limit-seconds 14400
if ($LASTEXITCODE) { throw 'Locked test failed' }
& $py research/speech/show_scores.py $run
```

To see the detector results on the same test clips, run this after Step 4 in the same session. It uses the saved predictions and completes the comparison without repeating training or decoding.

```powershell
& $probe $vad "$data/test.jsonl" | Set-Content -Encoding utf8 "$run/test-gate.jsonl"
if ($LASTEXITCODE) { throw 'Test gate evaluation failed' }
foreach ($name in @('baseline', 'parent', 'candidate')) {
    Write-Output $name
    & $py research/speech/score_gate.py "$run/locked-test/$name-test.json" "$run/test-gate.jsonl"
    if ($LASTEXITCODE) { throw 'Test score verification failed' }
}
```

Compare your chosen checkpoint with the most recent run on identical audio and with the same gate setting. Check both Russian and English WER, false captions on controls and blank speech clips. The historical test is now a regression benchmark. Reserve new recordings for independent confirmation before adopting a change.
