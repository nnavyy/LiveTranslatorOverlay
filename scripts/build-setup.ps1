param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$SetupFile,
    [string]$Compiler = 'makensis.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$setupPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SetupFile)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($setupPath)) | Out-Null
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'LiveTranslatorOverlay.csproj')
$version = $project.Project.PropertyGroup.Version
$termsRevision = (Get-Content -LiteralPath (Join-Path $projectRoot 'TERMS_REVISION.txt') -Raw).Trim()
if ($termsRevision -notmatch '^[a-z0-9-]+$') { throw 'Invalid terms revision' }
foreach ($file in @('LiveTranslatorOverlay.exe', 'coreclr.dll', 'PRIVACY.md', 'APP_LICENSE.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $file) -PathType Leaf)) {
        throw "Missing publish file: $file"
    }
}
function ConvertTo-PlainNotice([string]$Markdown) {
    $lines = foreach ($line in ($Markdown -split "\r?\n")) {
        if ($line -match '^\s*[-*_]{3,}\s*$') { '' }
        elseif ($line -match '^#\s+(.*)$') { $Matches[1].ToUpperInvariant() }
        elseif ($line -match '^#{2,}\s+(.*)$') { $Matches[1] }
        elseif ($line -match '^[-*+]\s+(.*)$') { '  ' + [char]0x2022 + ' ' + $Matches[1] }
        else { $line }
    }
    $text = ($lines -join "`r`n") -replace '\[([^\]]+)\]\(([^)\s]+)\)', '$1 ($2)'
    $text = $text -replace '(?<=^|[\s(\["''])_([^_\s](?:[^_]*[^_\s])?)_(?=$|[\s.,;:!?)\]''"])', '$1'
    ($text -replace '\*\*|__|\*|`', '').TrimEnd()
}
$termsPath = Join-Path ([IO.Path]::GetTempPath()) ("LiveTranslator-terms-" + [guid]::NewGuid().ToString('N') + '.txt')
try {
    $license = ConvertTo-PlainNotice (Get-Content -LiteralPath (Join-Path $publishPath 'APP_LICENSE.md') -Raw)
    $privacy = ConvertTo-PlainNotice (Get-Content -LiteralPath (Join-Path $publishPath 'PRIVACY.md') -Raw)
    $setupNotice = 'Setup downloads missing local speech files (about 149 MB for the default model and detector). You can skip the download and prepare speech later.'
    [IO.File]::WriteAllText($termsPath, (@($setupNotice, $license, $privacy) -join "`r`n`r`n"), [Text.UTF8Encoding]::new($true))
    & $Compiler "/DAPP_VERSION=$version" "/DPUBLISH_DIR=$publishPath" "/DSETUP_FILE=$setupPath" "/DSETUP_TERMS=$termsPath" "/DTERMS_REVISION=$termsRevision" (Join-Path $PSScriptRoot 'windows-setup.nsi')
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $termsPath) { Remove-Item -LiteralPath $termsPath }
}
