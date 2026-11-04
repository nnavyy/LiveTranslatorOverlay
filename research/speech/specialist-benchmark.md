# Next experiment: Russian specialist, measured before more training

The completed Small continuation did not improve Russian validation WER. This command tests a different hypothesis: use the Russian-specialized **GigaAM v2 RNNT** for Russian and retain the accepted Small adapter for English. It compares English greedy decoding against beam size 5, chooses on validation, and evaluates a qualifying choice on the historical test. **It does not train weights.** Running a stronger pretrained recognizer first tells us whether more Small training is worthwhile.

## Evidence and what it does not prove

Kutsakov et al. report GigaAM RNNT WER of **3.9% on Golos Farfield, 2.7% on Russian MCV-19 and 5.5% on Russian LibriSpeech**, versus Whisper-large-v3 at 16.6%, 5.5% and 9.5% respectively (Table 1). This is published evidence for testing a Russian specialist. It is not proof of a particular score on our SOVA/RuLS clips, preprocessing, scoring, GPU runtime or live application. We pin the v2 checkpoint associated with that research line instead of silently switching to later model families. [Interspeech 2025 paper](https://www.isca-archive.org/interspeech_2025/kutsakov25_interspeech.pdf) · [Official version overview](https://github.com/salute-developers/GigaAM)

English beam search explores alternative transcript sequences; it does not add training data or update weights. The official Whisper implementation supports beam search, but this alone does not prove an English accuracy gain. We measure it and prefer greedy on ties; a worse English decoder is ineligible. [Official Whisper decoding implementation](https://github.com/openai/whisper/blob/main/whisper/decoding.py)

## Expected outcome and explicit goals

| Measure | Accepted Small historical test | Requested next milestone | Final goal |
|---|---:|---:|---:|
| Russian WER | 23.93% | <=7% | Must also allow mean <=3% |
| English WER | 3.67% | <=3% | <=3% |
| Equal-language mean WER | 13.80% | <=5% if both limits are reached | <=3% |
| False captions | 0/90 | No regression | No regression |
| Blank speech clips | 0 | No regression | No regression |

Mean WER here is `(Russian WER + English WER) / 2`, regardless of the different numbers of words or recordings. Russian 7% plus English 3% gives 5%, not 3%. Russian 3.5% plus English 2.5%, for example, gives 3%. Reports mark each goal separately so a language milestone cannot be mistaken for the final mean target.

**Expected direction:** the Russian specialist may produce a substantial reduction relative to Small; English may remain near the existing score unless beam search helps. The paper supports this hypothesis, but there is no defensible precise forecast for this dataset yet. The numerical percentages above are targets, not guaranteed outcomes or confidence bounds. The full evaluation may miss every target or reject the specialist. The script saves these expectations in `configuration.json` before decoding, then puts observed scores and the next action in its final report. There is no new training result to forecast in this stage.

## Paste into Windows PowerShell

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\speech\benchmark-specialist.ps1
if ($LASTEXITCODE) { throw 'Benchmark failed. Read the error and partial output in the printed run folder.' }
```

The command reuses the prepared CUDA environment. It adds matching CUDA torchaudio and the pinned GigaAM package, preserving the core dependency constraints. FFmpeg must be on PATH; it is present on this machine. It verifies the upstream checkpoint checksum and caches the model on C: under `%LOCALAPPDATA%\LiveTranslatorOverlay\asr-specialist`, keeping the large weights off the nearly full B: drive. Setup and checkpoint download have already been verified on this machine. They are reused on subsequent calls.

Each benchmark creates a new `offline-research\specialist-benchmark-<timestamp>` directory. Read **`report.md`** afterward. The full run has not been executed by the assistant; it is for you to launch. Allow **roughly 30–90 minutes as an initial planning estimate**, excluding first-time installation/download; this is not a measured full-run duration. English beam search can make decoding slower, and memory pressure affects loading. Stage timing/ETA and memory are printed every ten clips. The benchmark has a four-hour timeout rather than a promised four-hour runtime.

For a small functionality check only, append `-Smoke`. Its tiny scores cannot establish accuracy or target attainment. This is unnecessary if you want the full benchmark immediately.

## What is measured

- The 70/15/15 manifest is unchanged. There are no gradient updates; training recordings do not participate in decoder selection. All 840 development clips are evaluated in a normal run.
- The accepted Small parent is re-decoded on the current GPU. The script compares it with GigaAM for Russian plus Small English greedy/beam-5 decoding. Source language is supplied, so this does not test automatic language detection or mixed-language switching.
- Every system is measured before and after the same previously fixed Silero gate (threshold 0.5, verified model/audio identities). A gated-out speech clip remains a deletion error. Raw recognizer false captions remain visible in the report; they are not concealed by the gated table.
- Validation eligibility requires strictly better Russian WER with no English WER, blank-speech or control-emission regression. Among qualifying decoders, select the lowest mean; greedy wins ties. Selection and model/data hashes are frozen before testing.
- If nothing qualifies, test decoding is skipped and the report explains why. Otherwise the chosen system and accepted Small are evaluated on the full historical test; that result cannot change the validation-selected decoder. If it regresses on test, keep the accepted Small system.
- Raw/gated predictions, scores, source-corpus breakdowns, configuration, selection, target checks, stage timings and memory summaries are saved. Host RAM counters and GPU counters measure different things; neither is total system memory. Caught execution errors and keyboard interrupts write `failure.json` when a run directory exists; abrupt process termination may prevent this.

The historical test has been examined repeatedly, and overlap with externally pretrained models' original training data is unknown. Passing it is a regression result, not proof of unseen-session performance. Fresh speaker/session-disjoint recordings and actual app latency/accuracy checks are required before deployment. GigaAM uses a different model/runtime from the current Whisper application; this command does not convert, deploy or enable it in the app.

## Decision after the report

| Observed outcome | Next action |
|---|---|
| Validation fails | Keep Small; inspect representative audio/errors and investigate data or scoring differences. Do not repeat the same training command. |
| Validation improves but historical test regresses | Keep Small; investigate the disagreement and reserve fresh evaluation audio. Do not tune on the old test. |
| Both improve but Russian remains above 7% | Audit residual Russian errors and add correctly transcribed, domain-matched data before considering specialist acoustic fine-tuning. Verify its GPU memory requirements first. |
| Russian passes but English remains above 3% | Retain the Russian research improvement; focus the next experiment on English error categories or a stronger English model rather than more Russian training. |
| Language milestones pass but mean exceeds 3% | The final goal is unmet; focus on the language contributing most remaining error while protecting the other language. |
| All goals pass | Confirm on fresh sessions and through the app runtime before implementation/deployment. |

The report produces a recommended next action from the observed outcome. No fixed number of training repetitions is promised to reach these goals.

## Verification completed before handoff

The PowerShell command completed a small end-to-end CUDA check: five clips each on development and test, covering English speech, both Russian speech sources and two controls. It exercised greedy and beam-5 decoding, GigaAM, fixed gating, validation selection, frozen test evaluation and reporting. This only validates execution; no full-run accuracy target has been demonstrated. The benchmark portion took 50.5 seconds, excluding setup, with 1,556 MiB peak tracked GPU allocation. Sampled private process commitment reached 5,392.5 MiB, so adequate system memory remains relevant even when resident RAM appears lower. [Exact verification record](results/2026-11-04-specialist-smoke/verification.json)

All 21 research unit tests passed. Additional report checks verified outcomes for target success, missed targets, test regression and validation failure. The full manual benchmark is still outstanding.
