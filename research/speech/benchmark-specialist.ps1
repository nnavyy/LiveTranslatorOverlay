# Windows PowerShell 5.1+. Pretrained-model benchmark, not another training epoch.
[CmdletBinding()]
param([string]$ResearchRoot, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) {
    $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research'
}
$r = (Resolve-Path -LiteralPath $ResearchRoot).Path
$py = Join-Path $r 'training-cuda-venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $py)) { throw 'Run setup-training-cuda.ps1 first.' }
if (!(Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'FFmpeg must be available on PATH for the upstream GigaAM audio loader.' }
& $py -m pip install torchaudio==2.10.0 --index-url https://download.pytorch.org/whl/cu126
if ($LASTEXITCODE) { throw 'CUDA torchaudio installation failed.' }
& $py -m pip install 'gigaam @ git+https://github.com/salute-developers/GigaAM.git@7447938d791c4f3e643386ee22c33777004293a5' -c "$PSScriptRoot/requirements-cuda.txt"
if ($LASTEXITCODE) { throw 'Pinned specialist dependencies failed to install.' }
$cache = Join-Path $env:LOCALAPPDATA 'LiveTranslatorOverlay\asr-specialist'
& $py "$PSScriptRoot/setup-specialist.py" $cache
if ($LASTEXITCODE) { throw 'Specialist model download/checksum verification failed.' }
$prefix = 'specialist-benchmark-'
if ($Smoke) { $prefix = 'specialist-smoke-' }
$run = Join-Path $r ($prefix + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Write-Host "Run: $run"
Write-Host 'Targets: Russian <=7%, English <=3%; final equal-language mean <=3%. These are targets, not promised outcomes.'
Write-Host 'No training. Validation selects the English decoder; qualifying systems then receive historical test evaluation.'
$benchmarkArguments = @("$PSScriptRoot/benchmark_specialist.py", $r, $cache, $run)
if ($Smoke) { $benchmarkArguments += '--smoke' }
& $py -u @benchmarkArguments
if ($LASTEXITCODE) { throw "Benchmark failed. Inspect the error and any partial output in $run." }
Write-Host "Read $run\report.md. No model has been deployed."
