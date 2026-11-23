[CmdletBinding()]
param([string]$ResearchRoot)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) { $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research' }
$root = (Resolve-Path -LiteralPath $ResearchRoot).Path
$storage = Join-Path $env:LOCALAPPDATA 'LiveTranslatorOverlay'

$translationPython = @(
    (Join-Path $storage 'mt-venv\Scripts\python.exe'),
    (Join-Path $root 'mt-venv-2026-11-03\Scripts\python.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

$translationModel = @(
    (Join-Path $storage 'm2m-ct2-int8-e205afe'),
    (Join-Path $root 'm2m-ct2-int8-e205afe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $translationPython) { throw "Missing local translation python under $storage\mt-venv or $root" }
if (-not $translationModel) { throw "Missing m2m-ct2-int8-e205afe model folder under $storage or $root" }

$config = [ordered]@{
    translationPython = $translationPython
    translationModel = $translationModel
}

foreach ($optional in @(
    @{ Key = 'python'; Path = Join-Path $root 'training-cuda-venv\Scripts\python.exe' },
    @{ Key = 'vadModel'; Path = Join-Path $root 'ggml-silero-v6.2.0.bin' },
    @{ Key = 'englishModel'; Path = Join-Path $storage 'accuracy-round-models\distil-large-v3' },
    @{ Key = 'russianCache'; Path = Join-Path $storage 'asr-specialist' }
)) {
    if (Test-Path -LiteralPath $optional.Path) {
        $config[$optional.Key] = $optional.Path
    }
}

New-Item -ItemType Directory -Force -Path $storage | Out-Null
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $storage 'local-speech.json') -Encoding UTF8
Write-Host 'Local translation configured. For word-by-word GPU speech, choose Local multilingual — Live word-by-word (Auto GPU/CPU) in the app.'
