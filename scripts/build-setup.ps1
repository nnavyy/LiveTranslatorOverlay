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
$license = Join-Path $publishPath 'APP_LICENSE.md'
foreach ($file in @('LiveTranslatorOverlay.exe', 'coreclr.dll', 'PRIVACY.md', 'APP_LICENSE.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $file) -PathType Leaf)) {
        throw "Missing publish file: $file"
    }
}
& $Compiler "/DAPP_VERSION=$version" "/DPUBLISH_DIR=$publishPath" "/DSETUP_FILE=$setupPath" "/DAPP_LICENSE=$license" (Join-Path $PSScriptRoot 'windows-setup.nsi')
if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE" }
