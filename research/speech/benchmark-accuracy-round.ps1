# Windows PowerShell 5.1+. New downloads/results use C: through LOCALAPPDATA.
[CmdletBinding()]
param([string]$ResearchRoot, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) {
    $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research'
}
$r = (Resolve-Path -LiteralPath $ResearchRoot).Path
$py = Join-Path $r 'training-cuda-venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $py)) { throw 'The existing training-cuda-venv is required.' }
if (!(Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'FFmpeg must be available on PATH.' }
$storage = Join-Path $env:LOCALAPPDATA 'LiveTranslatorOverlay'
$cache = Join-Path $storage 'accuracy-round-models'
$drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($storage))
if ($drive.AvailableFreeSpace -lt 4GB) { throw 'Keep at least 4 GB free on the model/results drive before starting.' }
$clock = [System.Diagnostics.Stopwatch]::StartNew()
& $py -u "$PSScriptRoot/setup_accuracy_round.py" $cache
if ($LASTEXITCODE) { throw 'Model download/checksum verification failed. Rerun to resume partial downloads.' }
$prefix = 'accuracy-round-'
if ($Smoke) { $prefix = 'accuracy-smoke-' }
$run = Join-Path (Join-Path $storage 'accuracy-round-results') ($prefix + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$arguments = @("$PSScriptRoot/benchmark_accuracy_round.py", $r, $cache, $run)
if ($Smoke) { $arguments += '--smoke' }
Write-Host "Results: $run"
Write-Host 'Targets: English <=2.67%, Russian <=4.70%; final mean <=3%. Gains are not guaranteed.'
Write-Host 'No weight training. Existing models, datasets and previous results are preserved.'
& $py -u @arguments
if ($LASTEXITCODE) { throw "Benchmark failed; inspect partial output and failure.json in $run." }
$clock.Stop()
@{ totalSecondsIncludingSetup = $clock.Elapsed.TotalSeconds; output = $run } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'wall-clock.json') -Encoding UTF8
Write-Host ("Total elapsed including setup: {0:N1} minutes" -f $clock.Elapsed.TotalMinutes)
Write-Host "Read $run\report.md. No model was deployed to the app."
