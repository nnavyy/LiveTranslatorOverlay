[CmdletBinding()]
param([string]$ResearchRoot)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) { $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research' }
$root = (Resolve-Path -LiteralPath $ResearchRoot).Path
$storage = Join-Path $env:LOCALAPPDATA 'LiveTranslatorOverlay'
$config = @{
    python = Join-Path $root 'training-cuda-venv\Scripts\python.exe'
    englishModel = Join-Path $storage 'accuracy-round-models\distil-large-v3'
    russianCache = Join-Path $storage 'asr-specialist'
    vadModel = Join-Path $root 'ggml-silero-v6.2.0.bin'
    translationPython = Join-Path $root 'mt-venv-2026-11-03\Scripts\python.exe'
    translationModel = Join-Path $root 'm2m-ct2-int8-e205afe'
}
foreach ($path in $config.Values) { if (!(Test-Path -LiteralPath $path)) { throw "Missing prepared input: $path" } }
New-Item -ItemType Directory -Force -Path $storage | Out-Null
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $storage 'local-speech.json') -Encoding UTF8
Write-Host 'Local speech configured. In app v2.0.0+, select Local English / Russian (GPU), choose English or Russian source, then Start Audio. Translation uses your selected translation provider.'
