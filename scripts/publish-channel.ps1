[CmdletBinding()]
param(
    [ValidateSet('dev', 'beta', 'stable')]
    [string]$Channel = 'dev',
    [string]$PublishDirectory = 'publish'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$csproj = Get-Content (Join-Path $projectRoot 'LiveTranslatorOverlay.csproj')
$version = [string]$csproj.Project.PropertyGroup.Version

& (Join-Path $PSScriptRoot 'package-windows.ps1') -OutputDirectory $PublishDirectory
$publishFull = [IO.Path]::GetFullPath((Join-Path $projectRoot $PublishDirectory))

$zipPath = Join-Path $projectRoot 'LiveTranslatorOverlay-Windows-x64.zip'
Compress-Archive -Path (Join-Path $publishFull '*') -DestinationPath $zipPath -Force
$zipInfo = Get-Item -LiteralPath $zipPath
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash

if ($Channel -eq 'dev') {
    $storage = Join-Path $env:LOCALAPPDATA 'LiveTranslatorOverlay'
    $devApp = Join-Path $storage 'DevApp'
    $devFeed = Join-Path $storage 'DevFeed'
    New-Item -ItemType Directory -Force -Path $devApp, $devFeed | Out-Null

    $devZip = Join-Path $devFeed 'LiveTranslatorOverlay-Windows-x64.zip'
    Copy-Item -LiteralPath $zipPath -Destination $devZip -Force
    $null = robocopy $publishFull $devApp /E /IS /IT /XF appsettings.json /R:2 /W:1
    if ($LASTEXITCODE -ge 8) { throw "Failed to sync DevApp (robocopy exit $LASTEXITCODE)" }

    $devManifest = [ordered]@{
        line = 'ml'
        channel = 'dev'
        version = $version
        packages = [ordered]@{
            'win-x64' = [ordered]@{
                url = ([Uri]$devZip).AbsoluteUri
                size = [int64]$zipInfo.Length
                sha256 = $zipHash
            }
        }
    }
    $devManifestPath = Join-Path $devFeed 'ml-dev.json'
    $devManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $devManifestPath -Encoding UTF8

    $channelCfg = [ordered]@{
        channel = 'dev'
        manifestUrl = ([Uri]$devManifestPath).AbsoluteUri
    }
    $channelCfg | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $storage 'channel.json') -Encoding UTF8

    $workspaceParent = Split-Path $projectRoot -Parent
    $shortcutPath = Join-Path $workspaceParent 'Live Translator Overlay (Dev).lnk'
    $devExePath = Join-Path $devApp 'LiveTranslatorOverlay.exe'
    $wsh = New-Object -ComObject WScript.Shell
    $sc = $wsh.CreateShortcut($shortcutPath)
    $sc.TargetPath = $devExePath
    $sc.WorkingDirectory = $devApp
    $sc.Arguments = '--developer'
    $sc.Description = "Launch Live Translator Overlay v$version (Developer mode)"
    $sc.Save()

    Write-Output "DevApp synced to $devApp, DevFeed written to $devManifestPath, and shortcut updated at $shortcutPath"
    return
}

if ($Channel -eq 'beta') {
    $betaDir = Join-Path $projectRoot 'publish-beta'
    New-Item -ItemType Directory -Force -Path $betaDir | Out-Null
    $betaArchive = Join-Path $betaDir 'LiveTranslatorOverlay-Windows-x64.zip'
    Copy-Item -LiteralPath $zipPath -Destination $betaArchive -Force
    Write-Output "Built private Beta archive: $betaArchive | size=$($zipInfo.Length) | sha256=$zipHash"
    Write-Output "Next: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\owner-grants.ps1 -Action publish-beta -Version $version -Runtime win-x64 -ZipPath `"$betaArchive`""
    return
}

$updatesDir = Join-Path $projectRoot 'updates'
New-Item -ItemType Directory -Force -Path $updatesDir | Out-Null
$tag = "v$version-ml"
$manifestFile = 'ml-stable.json'

$manifest = [ordered]@{
    line = 'ml'
    channel = $Channel
    version = $version
    packages = [ordered]@{
        'win-x64' = [ordered]@{
            url = "https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/$tag/LiveTranslatorOverlay-Windows-x64.zip"
            size = [int64]$zipInfo.Length
            sha256 = $zipHash
        }
    }
}
$manifestPath = Join-Path $updatesDir $manifestFile
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Updated $manifestPath for v$version ($Channel) | size=$($zipInfo.Length) | sha256=$zipHash"
