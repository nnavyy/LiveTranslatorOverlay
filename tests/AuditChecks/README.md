# November 1 audit regressions

Run `dotnet run --project tests/AuditChecks/AuditChecks.csproj -c Release` on Windows with the .NET 10 SDK.

The first six checks failed against v1.4.0 before the fixes. They exercise cleanup, model cache invalidation, PCM decoding, truncated streams, native history-window listener cleanup, and awaited queue capacity. Further checks exercise isolated feedback concurrency/delivery, per-user settings, and bounded history. No email, real audio, API keys, or real HTTP calls are used. Temporary settings, reports, and quota files are kept outside user application data.

The optional [AccuracySmoke](../AccuracySmoke/Program.cs) project sends six synthetic phrases to the key-free text translation path. It is intentionally excluded from deterministic CI: provider/network availability is external, and these simple meaning checks are not a general quality benchmark.
