# v2.1.1 verification — 2026-11-09

Scope: Windows CPU speech without NVIDIA, API keys, Python or prepared developer configuration. Online text translation remains dependent on external services.

## Verified locally

- RegressionChecks: 12/12 passed.
- AuditChecks: 9/9 passed.
- SpeakerChecks: 13/13 passed.
- LocalCpuChecks: fresh defaults, keyless settings migration, preserving configured cloud choices, explicit translation outage, canceling initial setup, invalid checksum rejection/partial-file cleanup, language selector coverage, native CPU initialization, silence/non-speech rejection and invalid PCM checks.
- WPF construction with isolated empty settings selected local speech and Google translation, had no API key, and disabled unconfigured advanced translation.
- The base and small speech models downloaded and passed their fixed SHA-256 checks. Silero v6.2.0 downloaded and passed its fixed checksum.
- Both the standard CPU runtime and explicitly forced NoAvx runtime loaded and transcribed real English/Russian recordings. This was a forced runtime test on the development PC, not testing on every older processor.
- Self-contained Windows publish verified bundled coreclr, C++ CRT, both CPU speech DLLs and empty release API keys. Build retains the pre-existing NU1510 System.Drawing.Common warning.

## Recognition smoke results

The first three under-12-second speech recordings per language in the existing russian-focus-20261103/test.jsonl were used, without selecting examples based on their results. English used auto-detection; Russian used an explicit source. Word errors include names and spelling differences. These six samples are not a representative accuracy benchmark.

| Language / fixture suffix | Base WER | Small WER |
| --- | ---: | ---: |
| en / 0028bf8a863b377860dcbe70 | 0.0% | 0.0% |
| en / 00416985069edcd854994a10 | 35.0% | 40.0% |
| en / 0045c96f99a32ebb72e0e900 | 10.0% | 10.0% |
| ru / 0001f1022cff8f86f0b2b115 | 16.7% | 16.7% |
| ru / 00639c84e7254245c922ae06 | 58.3% | 8.3% |
| ru / 007af85a4358907cda0fa092 | 30.0% | 20.0% |

On the development machine, base decoding took roughly 1.4–4.3 seconds per clip, small 4.7–10.9 seconds, and forced NoAvx base 8.3–16.9 seconds. These are smoke timings, not hardware guarantees. Slower machines can lag and the existing bounded audio queue can drop clips when processing falls behind. The base model remains the default for accessibility and speed; the larger model is optional.

## Known text-translation limitation

Live AccuracySmoke: **5/6 passed, one failed** after correcting an overly permissive negation assertion. Google returned an automated-query rate limit on this connection; the existing MyMemory fallback returned “I do not disagree” for “Saya tidak setuju,” reversing its meaning. This upstream quality problem is not fixed by changing the speech backend. No claim of universal or guaranteed translation accuracy is made. Successful text responses can still be wrong; exhausted/unavailable services now produce an explicit error instead of a false successful translation containing the source text.

No all-language, noisy-conversation, new-device or Windows-installation-matrix benchmark was run. Earlier specialist English/Russian GPU research scores do not apply to the new CPU models.
