# Offline regression checks

Run from the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet build LiveTranslatorOverlay.csproj -c Release --nologo
dotnet run --project tests/RegressionChecks/RegressionChecks.csproj -c Release --nologo
```

The checks execute the current application assembly using synthetic samples and mocked HTTP. They cover number changes in normal and streamed translation, exact cache behavior, everyday short phrases, silence/repetition, unchanged translation, fallback errors, short playback, language mappings, Spanish key/format coverage, release-notice version tracking, and explicit report-resolution mapping. They do not capture real audio or submit feedback. Quota persistence is redirected to a unique temporary directory.

The four initial correctness checks failed on version 1.3.3 before the fixes. Exit 0 now means the asserted behavior passes, unlike the historical defect-reproduction probes under docs/reviews.
