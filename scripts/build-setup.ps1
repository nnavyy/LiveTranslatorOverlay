param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$SetupFile,
    [string]$Compiler = 'makensis.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$setupPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SetupFile)
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'LiveTranslatorOverlay.csproj')
$version = $project.Project.PropertyGroup.Version
foreach ($file in @('LiveTranslatorOverlay.exe', 'coreclr.dll', 'PRIVACY.md', 'APP_LICENSE.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $file) -PathType Leaf)) {
        throw "Missing publish file: $file"
    }
}
$termsPath = Join-Path ([IO.Path]::GetTempPath()) ("LiveTranslator-terms-" + [guid]::NewGuid().ToString('N') + '.txt')
try {
    $license = Get-Content -LiteralPath (Join-Path $publishPath 'APP_LICENSE.md') -Raw
    $privacy = Get-Content -LiteralPath (Join-Path $publishPath 'PRIVACY.md') -Raw
    $setupNotice = 'Setup downloads missing local speech files (about 149 MB for the default model and detector). You can skip the download and prepare speech later.'
    [IO.File]::WriteAllText($termsPath, (@($setupNotice, $license, $privacy) -join "`r`n`r`n"), [Text.UTF8Encoding]::new($true))
    & $Compiler "/DAPP_VERSION=$version" "/DPUBLISH_DIR=$publishPath" "/DSETUP_FILE=$setupPath" "/DSETUP_TERMS=$termsPath" (Join-Path $PSScriptRoot 'windows-setup.nsi')
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $termsPath) { Remove-Item -LiteralPath $termsPath }
}
