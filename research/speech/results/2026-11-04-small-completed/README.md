# Completed Small run · 4 November 2026

The run selected update 4,340 and passed the frozen development acceptance criteria against pretrained Small. The selected adapter's hash and all five development/test score summaries were independently checked against the saved predictions. [Exact scores and configuration](summary.json) · [Three-model comparison](comparison.md)

| Historical test | Previous trained Tiny | Pretrained Small | Adapted Small |
|---|---:|---:|---:|
| Russian WER | 49.72% | 25.60% | 23.93% |
| English WER | 8.96% | 3.74% | 3.67% |
| False captions | 90/90 | 90/90 | 0/90 |
| Blank speech clips | 0 | 0 | 0 |

Changing the backbone produced most of the speech gain. Fine-tuning Small removed a further 107 Russian errors out of 6,418 reference words and two English errors out of 2,780. The adapter itself emitted no words on the 90 synthetic controls. Applying Silero to the candidate's test output preserved its WER and zero blank-speech count.

## What the runtime shows

The saved development phase took 25,904.03 seconds (7 hours 11 minutes 44 seconds). Testing took 7,591.27 seconds (2 hours 6 minutes 31 seconds). Together these measured phases took **9 hours 18 minutes 15 seconds**. The detector and report stages add more time. The user's observation of around ten hours is consistent with this workload.

Five decoding passes processed 840 clips each, for 4,200 evaluation decodes. Their saved generation times total 19,200.59 seconds, or 5 hours 20 minutes. The remaining 3 hours 58 minutes includes all 4,340 optimizer updates, preprocessing, loading, verification and disk work. Training-only time was not recorded, so subtracting decoding does not isolate it exactly.

At 50 seconds per ten optimizer updates, all 4,340 updates would take about 6 hours 2 minutes. Adding five long evaluation passes can push total runtime beyond ten hours. Progress lines during evaluation count clips, while training progress counts optimizer updates. Reference length, generated length, nonspeech behavior, system load and paging can change speed during a run.

Execution was on the CPU with four PyTorch threads. The machine reports an i5-13420H and about 7.71 GiB of OS-visible RAM. At inspection after completion, available physical RAM was approximately 61 MiB, indicating current memory pressure. The finished run did not record RAM history. That snapshot cannot establish how much memory the trainer consumed overnight or whether disk paging caused its pauses.

## Expectations for the next run

Use **roughly 9–12 hours** as a planning range for an identical full run on this CPU, based on this completed run. This is an estimate, and higher background load or paging can make it longer. The 24-hour phase limit is a timeout rather than a duration estimate. More RAM helps when paging limits progress; it does not remove the compute cost of a larger model.

For accuracy, the existing pass rule was set before this run. It requires lower mean validation WER than the pretrained base, at most one percentage point of regression per language, and no increase in missed speech or control emissions. This run passed. It confirms the earlier small pilot's direction on the historical benchmark. It does not establish the same WER on new speakers or live app playback.

For a proposed next improvement, use the current 23.93% Russian / 3.67% English and 0/90 false captions as the historical reference. A useful prospective goal would be Russian WER at or below 20%, English no worse than the current 3.67%, and no extra missed speech or false captions on identical regression audio, followed by confirmation on fresh sessions. These are proposed goals, not a forecast for another identical training run.

## New timing and memory output

Future training prints active seconds per optimizer update, estimated training time remaining, phase elapsed time, process resident RAM and system available RAM. Evaluation prints a separate estimate for that decoding pass. Training estimates exclude intermediate validation pauses. Each stage's estimate concerns that stage only.

Each phase saves progress.jsonl alongside its predictions. Its timestamped entries include process resident and private memory, available and total system RAM, timing and Windows page-fault counts where available. Page-fault counts include soft faults and cannot alone prove disk paging. The script flags low available RAM below the larger of 512 MiB and 10% of physical RAM. Reports are sampled at the same ten-item progress boundaries, so brief memory peaks between samples may be missed.

The next full run is started with the same PowerShell command from the [Small workflow](../2026-11-03-small-pilot/README.md). The telemetry additions do not change training settings.

On successful completion, the PowerShell workflow also writes `workflow-timing.json` with total elapsed seconds, including model setup, detector checks and report generation. Interrupted runs retain their most recent per-stage progress entries but do not receive a completed workflow timing file.

Verification: a separate miniature Tiny run completed baseline evaluation, six optimizer updates and candidate evaluation, with timing and RAM entries in all three stages. A deterministic timing check confirmed that ten updates in 50 seconds projects 21,650 seconds remaining for 4,340 updates. This verifies instrumentation, not Small accuracy or its next-run duration. The new dependency is `psutil==7.2.2`, already present in this machine's research environment.
