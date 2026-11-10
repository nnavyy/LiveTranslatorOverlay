param([string]$OutputDirectory = 'publish')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
dotnet publish (Join-Path $projectRoot 'LiveTranslatorOverlay.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
# Ship the redistributable CRT beside the executable so no separate installer is needed.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ redistributable files were not found' }
$redistRoot = Join-Path $visualStudio 'VC\Redist\MSVC'
$crt = Get-ChildItem $redistRoot -Directory | Where-Object { $_.Name -match '^\d+\.' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Get-ChildItem (Join-Path $_.FullName 'x64') -Directory -Filter '*.CRT' } |
    Select-Object -First 1
if (!$crt) { throw 'x64 CRT redistributable directory was not found' }
Copy-Item (Join-Path $crt.FullName '*.dll') $output
Copy-Item (Join-Path $projectRoot 'appsettings.example.json') (Join-Path $output 'appsettings.json') -Force
foreach ($required in @('LiveTranslatorOverlay.exe', 'coreclr.dll', 'vcruntime140.dll', 'msvcp140.dll', 'runtimes\win-x64\whisper.dll', 'runtimes\noavx\win-x64\whisper.dll')) {
    if (!(Test-Path (Join-Path $output $required))) { throw "Missing bundled runtime: $required" }
}
$settings = Get-Content (Join-Path $output 'appsettings.json') -Raw | ConvertFrom-Json
if ($settings.GroqApiKey -or $settings.DeepgramApiKey -or $settings.GroqApiKeyProtected -or $settings.DeepgramApiKeyProtected) {
    throw 'Release settings contain an API credential'
}
Write-Output "Verified self-contained Windows package: $output"
