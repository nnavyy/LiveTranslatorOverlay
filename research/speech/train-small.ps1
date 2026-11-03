# Windows PowerShell 5.1+. Uses the existing local research environment.
[CmdletBinding()]
param([string]$ResearchRoot = (Join-Path $PSScriptRoot '..\..\..\offline-research'))
$ErrorActionPreference = 'Stop'
$r = (Resolve-Path -LiteralPath $ResearchRoot).Path
$py = Join-Path $r 'training-venv\Scripts\python.exe'
$data = Join-Path $r 'russian-focus-20261103\manifest.jsonl'
$model = Join-Path $r 'whisper-small-973afd2'
$trainer = Join-Path $PSScriptRoot 'train_expanded.py'
$run = Join-Path $r ('my-small-training-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if ((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -ne 'ab859fe7348051378d10416fe5ac704eb9b5d0729b3aed709c0e56d567cdeb13') {
    throw 'Expected the verified 5,600-clip Russian-focused manifest.'
}
if (!(Test-Path -LiteralPath "$model/provenance.json")) {
    if (Test-Path -LiteralPath $model) { throw "Incomplete model at $model. Rename the incomplete directory and retry." }
    $stage = "$model-staging-$([guid]::NewGuid().ToString('N'))"
    & $py "$PSScriptRoot/download_model.py" $stage --size small
    if ($LASTEXITCODE) { throw "Model setup failed. Partial files are in $stage." }
    Rename-Item -LiteralPath $stage -NewName (Split-Path -Leaf $model)
}
Write-Host "Training a fresh Small adapter. Run directory: $run"
& $py -u $trainer develop $data $model $run --full-epoch --midpoint-checkpoint --learn-empty-captions --phase-limit-seconds 86400
if ($LASTEXITCODE) { throw "Development failed. Inspect $run and the error above." }
Get-Content -Raw "$run/selection.json"
& $py -u $trainer test $data $model $run --phase-limit-seconds 86400
if ($LASTEXITCODE) { throw "Historical test evaluation failed. Development results are in $run." }
& $py "$PSScriptRoot/report_small.py" $r $run
if ($LASTEXITCODE) { throw "Report generation failed. Raw results are in $run." }
$probeProject = Join-Path $PSScriptRoot '..\..\tests\SpeechGateProbe'
dotnet build "$probeProject/SpeechGateProbe.csproj" -c Release --nologo
if ($LASTEXITCODE) { throw "Gate build failed. Raw results are in $run." }
$probe = (Get-ChildItem "$probeProject/bin/Release" -Recurse -Filter SpeechGateProbe.exe | Select-Object -First 1).FullName
$previous = Join-Path $r 'my-russian-training-20261103-181541'
foreach ($split in @('dev','test')) {
    $decisions = Join-Path $run "$split-gate.jsonl"
    & $probe "$r/ggml-silero-v6.2.0.bin" "$(Split-Path $data)/$split.jsonl" | Set-Content -Encoding utf8 $decisions
    if ($LASTEXITCODE) { throw "Speech detection failed on $split" }
    foreach ($label in @('Previous Tiny','Pretrained Small','Adapted Small')) {
        $folder = $run
        if ($label -eq 'Previous Tiny') { $folder = $previous }
        $name = 'baseline'
        if ($label -ne 'Pretrained Small') {
            $name = (Get-Content -Raw "$folder/selection.json" | ConvertFrom-Json).checkpoint
        }
        if ($split -eq 'test') {
            $folder = Join-Path $folder 'locked-test'
            if ($label -ne 'Pretrained Small') { $name = 'candidate' }
        }
        "$label ($split)" | Out-File "$run/detector-scores.txt" -Encoding utf8 -Append
        & $py "$PSScriptRoot/score_gate.py" "$folder/$name-$split.json" $decisions | Out-File "$run/detector-scores.txt" -Encoding utf8 -Append
        if ($LASTEXITCODE) { throw "Failed to score $label on $split" }
    }
}
Write-Host "Read $run\detector-scores.txt for the paired detector comparison."
Write-Host "Read $run\comparison.md"
