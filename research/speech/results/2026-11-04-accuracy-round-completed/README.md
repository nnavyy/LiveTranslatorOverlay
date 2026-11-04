# Completed accuracy round and Windows integration — 4 November 2026

**English improved to 2.12% WER; Russian remains 5.70%; equal-language mean is 3.91%.** This run compared pretrained recognizers/decoding; no weights were trained. Source run: `accuracy-round-20261104-153959`. All candidate scores were independently recomputed from saved predictions, and frozen input hashes checked again.

| Historical test | Previous specialist system | Selected system |
|---|---:|---:|
| English WER | 3.67% (102/2780) | 2.12% (59/2780) |
| Russian WER | 5.70% (366/6418) | 5.70% (366/6418) |
| Equal-language mean | 4.69% | 3.91% |
| Nonspeech captions | 0/90 | 0/90 |
| Blank speech | 0 | 0 |

English made 43 fewer errors (42.2% relative reduction) and passed <=2.67% on historical test. Russian <=4.70%, mean <=3% and both <=2% remain unmet. At this English score, Russian needs <=3.88% for a 3% equal-language mean.

Development selected English Distil-Whisper large-v3 beam 5 (3.32% versus current 3.77%). GigaAM v3 Russian was rejected: 7.53% versus v2's 6.60%, with the same three gate-rejected speech clips. V2 was retained, reusing audited historical predictions. Test did not change selection. The original 70/15/15 manifest and Silero gate were preserved.

Evaluation took 23.7 minutes; setup-inclusive wall time was 24.5 minutes, within the provisional 20–60 minute estimate. Peak PyTorch GPU allocation was 2052 MiB; sampled private commit peaked at 6797 MiB; minimum available host RAM was 106 MiB. Paging can slow this system. Measurements exclude some native allocations and between-sample peaks.

## Integration

The Windows app gains a selectable local GPU speech engine with the selected English beam-5 model and Russian v2 model. C# applies the benchmark's pinned Silero gate at threshold 0.5 before recognition. Source must be explicit English or Russian. Stop/restart audio before changing source language. Recognition uses prepared local CUDA Python/model assets, separate from the app ZIP. Run `enable-local-speech.ps1` once; on this machine configuration is already written. Other machines need compatible assets and environment.

The actual C# service reproduced one English and one Russian benchmark transcript exactly, rejected silence in both sessions, rejected Auto language and tolerated repeated disposal. Existing checks passed 12/12 regression, 9/9 audit and 13/13 speaker/scroll checks. These are integration fixtures, not a full live-capture WER study. Capture chunking, resampling, overlap, downstream filtering and speaker estimation differ from offline scoring. Live accuracy is not guaranteed to match this table.

English comes from LibriSpeech train.clean.100, a corpus listed in Distil's training inventory. Exact retained recording overlap is not audited. This repeatedly inspected historical set cannot establish unseen-speech accuracy. Fresh speaker/session-disjoint conversations remain necessary.

Future app source is private; public downloads/reports and historical MIT source remain available. Existing API keys/settings are not included in release artifacts. Purchasing remains unconfigured. Large models, audio, environments and caches stay local. See [summary.json](summary.json) for recomputed scores, target checks, resources and report hashes.

## v2.0.0 offline translation

The release connects the prepared M2M100 int8 CPU translation model, with explicit source and target languages. Unsupported languages and oversized text fail explicitly, without online fallback. This does not collect recordings or update weights. Configured systems default to local speech and local translation; cloud engines remain optional. ASR WER does not measure translation quality. The CPU model loads per request and releases RAM afterward, trading startup latency for lower idle memory. Models and environments remain separate from the app ZIP.
