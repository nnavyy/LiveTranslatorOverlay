# Offline review reproduction checks

These probes document six defects in version 1.3.3 at commit `e1362bec63f05bfaca34d1c67deec3470a0bb81e`. They reference the release assembly built from unchanged application source. Reflection seeds private state and injects a mock HTTP client. Audio inputs are synthetic samples.

No real HTTP requests, device capture, update installation, feedback submission, or API keys are used. The audio service may write synthetic chunk metadata to `chunk_debug.txt` in the temporary working folder.

The sources are stored with `.txt` suffixes because this repository's WPF project otherwise compiles nested C# files automatically. To reproduce, run the following from the repository root in PowerShell with the .NET 10 SDK installed:

```powershell
$reviewRepo = (Get-Location).Path
dotnet build LiveTranslatorOverlay.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
$reviewAssembly = Join-Path $reviewRepo 'bin\Release\net10.0-windows10.0.19041.0\LiveTranslatorOverlay.dll'
$reviewRun = Join-Path ([System.IO.Path]::GetTempPath()) ('LiveTranslatorReview-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reviewRun | Out-Null
Copy-Item -LiteralPath (Join-Path $reviewRepo 'docs\reviews\2026-10-31\Program.cs.txt') -Destination (Join-Path $reviewRun 'Program.cs')
Copy-Item -LiteralPath (Join-Path $reviewRepo 'docs\reviews\2026-10-31\ReviewChecks.csproj.txt') -Destination (Join-Path $reviewRun 'ReviewChecks.csproj')
Push-Location -LiteralPath $reviewRun
try {
    dotnet run --project ReviewChecks.csproj -c Release --nologo "-p:AppAssemblyPath=$reviewAssembly"
    if ($LASTEXITCODE -ne 0) { throw 'One or more defect probes did not reproduce' }
} finally {
    Pop-Location
}
```

Expected for the reviewed version: **6/6 defect probes reproduced**, exit 0. This means the known defects were observed; it does not mean the product passed a regression suite. After fixes, replace these probes with assertions of correct behavior.

The queue probe uses bounded delays to demonstrate async concurrency, not hardware or recognition quality. The settings probe only resolves a path; it does not save settings. Original captured output and build/audit results are in [results.txt](results.txt). The complete findings, test limitations, notification design, and balanced language expansion plan are in the [review report](../2026-10-31.md).
