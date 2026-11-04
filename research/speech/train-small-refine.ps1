# One continuation of the selected Small adapter. Windows PowerShell 5.1+.
[CmdletBinding()]
param([string]$ResearchRoot, [string]$ParentRun)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) {
    $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research'
}
$r = (Resolve-Path -LiteralPath $ResearchRoot).Path
if ([string]::IsNullOrWhiteSpace($ParentRun)) {
    $ParentRun = Join-Path $r 'my-small-training-20261103-213758'
}
$parent = (Resolve-Path -LiteralPath $ParentRun).Path
$choice = Get-Content -Raw -LiteralPath "$parent/selection.json" | ConvertFrom-Json
$config = Get-Content -Raw -LiteralPath "$parent/configuration.json" | ConvertFrom-Json
if (!$choice.developmentEligible -or $config.baseModel -ne 'openai/whisper-small') {
    throw 'Choose a Small parent that passed development criteria.'
}
$py = Join-Path $r 'training-cuda-venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $py)) { throw 'Run setup-training-cuda.ps1 first.' }
& $py -c "import torch; assert torch.cuda.is_available(), 'CUDA unavailable'; print('Using',torch.cuda.get_device_name(0))"
if ($LASTEXITCODE) { throw 'GPU check failed. Run setup-training-cuda.ps1.' }
$data = Join-Path $r 'russian-focus-20261103\manifest.jsonl'
if ((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -ne 'ab859fe7348051378d10416fe5ac704eb9b5d0729b3aed709c0e56d567cdeb13') {
    throw 'Expected the verified 70/15/15 Russian-focused manifest.'
}
$model = Join-Path $r 'whisper-small-973afd2'
$run = Join-Path $r ('my-small-refinement-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$timer = [Diagnostics.Stopwatch]::StartNew()
Write-Host "Parent: $parent. New output: $run"
Write-Host 'One additional epoch, lower learning rate, unchanged splits. Full dev/test scores decide whether this helped.'
Write-Host 'ETA is per stage. GPU and RAM usage are saved in progress.jsonl. No automatic deployment.'
& $py -u "$PSScriptRoot/train_expanded.py" develop $data $model $run --parent-run $parent --refine-selected --device cuda --phase-limit-seconds 86400
if ($LASTEXITCODE) { throw "Refinement failed. Read the error and $run. Original parent is preserved." }
$selected = Get-Content -Raw -LiteralPath "$run/selection.json" | ConvertFrom-Json
if ($selected.developmentEligible) {
    & $py -u "$PSScriptRoot/train_expanded.py" test $data $model $run --parent-run $parent --refine-selected --device cuda --phase-limit-seconds 86400
    if ($LASTEXITCODE) { throw "Historical test failed. Development output is in $run." }
} else {
    Write-Host 'No qualifying development gain. Keep the parent. Skipping historical test decoding.'
}
$timer.Stop()
@{ elapsedSeconds = $timer.Elapsed.TotalSeconds; includesSetup = $false } | ConvertTo-Json | Set-Content -LiteralPath "$run/workflow-timing.json" -Encoding utf8
& $py "$PSScriptRoot/report_refinement.py" $run
if ($LASTEXITCODE) { throw "Report failed. Raw scores remain in $run." }
Write-Host "Elapsed: $($timer.Elapsed). Report: $run\comparison.md"
