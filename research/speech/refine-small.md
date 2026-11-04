# Continue Small once, then measure

The next prepared command is now available in the [Russian specialist benchmark instructions](specialist-benchmark.md).

**Completed-run update:** this continuation failed its Russian accuracy gate: validation WER changed from 23.28% to 23.39%, while English changed from 3.77% to 3.67%. Keep the accepted parent and do not repeat the unchanged command expecting accumulated training. Read the [complete result, runtime and next-experiment recommendation](results/2026-11-04-refinement-completed/README.md). The commands below reproduce the experiment, not a recommendation to rerun it after this result.

The completed model reached 23.93% Russian WER, 3.67% English WER and 0/90 synthetic controls emitting captions on the historical test. Re-running `train-small.ps1` starts a new adapter from pretrained Small. It does not continue that successful adapter.

Use the new continuation workflow below for one controlled additional epoch. It starts from the selected adapter in `my-small-training-20261103-213758`, uses a fresh AdamW optimizer at learning rate 0.00001 (one fifth of the previous rate), and advances the seed so the shuffle and augmentations differ. The data and 70/15/15 assignments remain unchanged. It still presents 3,500 speech recordings and 420 training controls twice, for 4,340 updates. Only training recordings contribute gradients. This is a conservative experiment, not a proven accuracy improvement.

## Run manually

Paste into Windows PowerShell from this machine. Setup creates a separate CUDA environment and leaves the CPU environment available. The initial CUDA wheel download is about 2.6 GB; later setup calls reuse the installation. No CUDA toolkit installation is required by this wheel workflow. An installed compatible NVIDIA driver is required.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\speech\setup-training-cuda.ps1
if ($LASTEXITCODE) { throw 'GPU setup failed. Training has not started.' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\speech\train-small-refine.ps1
if ($LASTEXITCODE) { throw 'Training or evaluation failed. Read the error above.' }
```

The script prints the new run folder. Read `comparison.md` and `comparison.json` there when it finishes. Each phase saves raw predictions, scores and `progress.jsonl`; `workflow-timing.json` records elapsed training/evaluation time excluding environment setup and final report writing. The original adapter is preserved. Nothing is deployed into the app.

The workflow re-evaluates the parent on development, trains once, then evaluates the candidate on development. Acceptance requires strictly lower Russian WER, no English WER increase, no extra blank speech and no additional control emissions. If it fails, the script writes a report and skips test decoding. If it passes, parent and candidate are both decoded on the historical test. A test regression is reported as a reason to keep the original model; it is not used to select another checkpoint. Only one checkpoint is evaluated, so there is no halfway decoding pass.

## Expectations and stopping rule

| Measure | Current historical result | Next experiment goal |
|---|---:|---:|
| Russian WER | 23.93% | <=22%; <=20% is a stretch |
| English WER | 3.67% | No worse than parent |
| False captions | 0/90 | 0/90 |
| Blank speech | 0 | 0 |

These are engineering targets, not forecast percentages or confidence intervals. A plateau or regression is possible. Development acceptance compares with the freshly decoded parent rather than substituting historical test percentages. Both rows use the same GPU, precision and attention implementation, because the GPU runtime differs from the original CPU runtime.

Run this experiment once, then review. If it fails validation, keep the existing checkpoint. If the gain is less than one Russian WER percentage point, treat it as diminishing returns and prioritize error inspection, additional accurately transcribed Russian audio from the intended use case, or a stronger Russian model. Do not schedule ten identical retries. Repeated use of this historical test makes it a regression benchmark; it cannot remain an untouched test set. Reserve fresh speaker/session-disjoint recordings before making new generalization claims. Public recordings can be used if personal recording is not possible, provided their source, transcript quality and model-training overlap are checked.

Below 3% is not a predictable number of training runs. The current Russian test has 6,418 reference words and 1,536 edit errors. Less than 3% allows at most 192 errors: at least 1,344 fewer, or 87.5% of the remaining errors removed. WER counts insertions, deletions and substitutions; it is not the percentage of sentences misunderstood. Different microphones, accents, vocabulary, noise and reference conventions can change it substantially.

## Speed and memory

Hardware inspection found an NVIDIA RTX 2050 with 4,096 MiB VRAM. The previous environment was `torch 2.15.0+cpu`, so that run did not use it. The new environment pins the official Windows CUDA 12.6 build of PyTorch 2.10.0 and the existing Transformers/PEFT versions. This is an isolated compatibility choice, not a request to downgrade the CPU environment. [Official PyTorch wheel instructions](https://pytorch.org/get-started/previous-versions/)

The trainer now accepts `--device cuda`, moves model, features and tokens onto the GPU, and uses scaled dot-product attention on that path. It keeps float32, batch size one and the existing decoder LoRA modules. Parent and candidate are re-evaluated on this runtime; GPU execution itself does not promise better WER. CUDA synchronization makes the stage measurements account for completed GPU work. Progress includes allocated, reserved and peak allocated GPU memory, alongside system RAM readings. PyTorch's allocation counters exclude other applications and some driver allocations.

The completed CPU workflow took over nine hours. This workflow has four decoding passes if it qualifies, two if it fails development, and avoids a fresh pretrained-base comparison and midpoint decoding. The GPU speedup must be measured on this machine. Use the first 100 updates and each evaluation pass's progress to estimate completion; training ETA excludes later validation/test passes. Eight GB of system RAM and four GB of VRAM still constrain the workload. If CUDA reports out-of-memory, stop and close unnecessary GPU-heavy applications before retrying; the script deliberately does not silently fall back to a slow CPU run.

The 4 November GPU smoke check completed fresh Small training, continuation, separate test evaluation and report generation. Its continuation used six updates and 12 clips per decoding pass: 0.430 seconds/update and 0.853–1.225 seconds/clip. Peak PyTorch GPU allocation was 1,261 MiB (about 1.23 GiB), with sampled process resident RAM up to 1,394 MiB. This is not total GPU/system memory, and a longer run may use more. A simple extrapolation gives about 1.5 hours; use **1–3 hours** as a provisional planning range excluding installation, then replace it with the full-run stage measurements. This small sample overrepresents controls and is not a representative accuracy or speed benchmark. [Exact smoke configuration, sample IDs and measurements](results/2026-11-04-refinement-check/smoke.json)

The smoke candidate had no development improvement and was correctly marked ineligible. We deliberately exercised the direct test command to verify it still worked; the normal PowerShell workflow skips that test after failed development. These smoke weights are not a replacement for your completed adapter.

Additional verification: 18 research unit tests passed, both new PowerShell scripts passed parsing, the setup script completed an actual CUDA calculation, and the existing CPU path completed a separate miniature Tiny training run. The full 4,340-update refinement has not been run; that is the manual experiment above.

## What the papers support

- Radford et al. demonstrate that model/data scale affects Whisper recognition and that gains diminish as data scales. This supports comparing stronger pretrained models and useful additional data; it does not predict a fixed gain from another epoch on these 3,500 speech clips. [Whisper, ICML 2023](https://proceedings.mlr.press/v202/radford23a.html)
- Hu et al. motivate adapting low-rank weights while freezing the backbone. We retain LoRA for manageable training memory. Their language-model experiments do not establish a Russian WER guarantee for this implementation or the new learning rate. [LoRA, ICLR 2022](https://arxiv.org/abs/2106.09685)
- Kutsakov et al. report Russian-specialized GigaAM RNNT results of 2.7% on Russian MCV-19, 3.9% on Golos Farfield and 5.5% on Russian LibriSpeech (Table 1). Even a strong specialist is not below 3% everywhere. If this continuation plateaus, benchmark a specialist on the same development audio before paying for more training. Its published results cannot be substituted for our test scores, and it would require a separate Russian inference path while retaining English recognition. [GigaAM, Interspeech 2025](https://www.isca-archive.org/interspeech_2025/kutsakov25_interspeech.pdf)
