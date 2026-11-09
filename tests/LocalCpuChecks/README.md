# CPU/key-free checks

Run `dotnet run --project tests/LocalCpuChecks -c Release` for offline checks of fresh defaults, saved-settings migration, cloud-choice preservation, translation failures, download cancellation and checksum-failure cleanup. A tiny loopback HTTP fixture supplies a corrupt model; no external service is used.

Add `-- --ui` to instantiate the WPF settings window with isolated, empty settings and verify the default engine selections and absence of required credentials.

Add `-- --integration` to download/verify the real speech models and exercise native CPU loading, auto-detection, silence/non-speech rejection and language coverage. Add `--small` for the larger model, or `--noavx` to force the bundled older-CPU native runtime in a fresh process. Models are cached in the normal per-user model directory; no keys or prepared local-speech.json are used.

Add `--fixtures <russian-focus-20261103 directory>` to transcribe the first three under-12-second speech fixtures per language from its existing test.jsonl. English uses auto-detection; Russian uses an explicit source. The deliberately broad smoke threshold (WER below 60%) catches broken recognition, not product-level accuracy. Printed WER and timing must accompany any quality interpretation; these six recordings do not establish general accuracy or real-time performance. Fixtures are not redistributed in the application.

`tests/AccuracySmoke` separately tests the live text service. It can fail because of upstream service limits or incorrect translations; it is not a deterministic offline release gate.
