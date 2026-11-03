# Testing a stronger recognizer for Russian

The last completed Tiny adapter made 3,191 word edits on 6,418 Russian reference words (49.72% WER). Pretrained Tiny made 3,241 edits. Fine-tuning recovered only 50 net errors relative to pretrained Tiny on that test. The larger six-point gain in the previous report was against the weaker Round 3 starting adapter. Silero removed false captions on controls but left the Russian word-error count unchanged on that test.

## Research behind the experiment

Radford et al. compared multiple Whisper sizes on identical multilingual transcription benchmarks. Their FLEURS results in Table 13 motivate replacing the small-capacity Tiny backbone. Published benchmark WER is specific to its recordings and scoring. We therefore compare on the project's RuDevices and Russian LibriSpeech recordings before training. [Whisper, ICML 2023](https://proceedings.mlr.press/v202/radford23a.html)

Hu et al. describe LoRA, which freezes pretrained weights and learns low-rank updates. We retain the project's decoder-attention LoRA procedure and initialize a fresh adapter for the dimensions of Whisper Small. Testing plain Small separately measures whether our limited training data adds anything to the stronger backbone. LoRA's original language-model experiments do not predict a particular Russian ASR gain. [LoRA, ICLR 2022](https://arxiv.org/abs/2106.09685)

Kutsakov et al. studied Russian-specific self-supervised acoustic pretraining with GigaAM and reported improved Russian ASR on their benchmarks. That is a separate model family and an alternative for Russian-only recognition. This experiment uses Whisper Small because the existing recognizer, English evaluation, adapter training and local Whisper probe already support the Whisper architecture. [GigaAM, Interspeech 2025](https://www.isca-archive.org/interspeech_2025/kutsakov25_interspeech.html)

## Run on the existing Windows machine

Open Windows PowerShell and paste the whole block. The child PowerShell process uses a process-local execution policy for the repository script. Run it once and keep the window open. CPU training with Small can take hours. The script prints the new run directory before fitting.

```powershell
Set-Location 'B:\Projects\live translator\LiveTranslatorOverlay'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\speech\train-small.ps1
```

The script verifies the prepared manifest, uses the pinned Small safetensors, trains one shuffled pass over 3,500 speech examples plus 420 controls presented twice, and evaluates steps 2,170 and 4,340 on validation. It uses the unchanged 70/15/15 split, rank 8, learning rate 0.00005 and the immediate-empty-caption option. After freezing the selection, it runs the existing historical test. The script writes the three-way comparison to `comparison.md` in the run folder, including Russian WER by speech source, English WER, blank speech and control emissions. Raw scores exclude external speech detection. The historical Tiny row uses its saved decoder settings. Both Small rows use identical decoding.

Read the pretrained Small and adapted Small rows together. The adapter must beat its base on mean validation WER, avoid a regression greater than one percentage point in either language, and avoid extra missed speech or control emissions. If neither checkpoint qualifies, the reported adapter is diagnostic. For deployment, separately confirm the chosen system on fresh recorded sessions, real nonspeech and the intended live runtime. The PyTorch adapter is an offline research artifact. Merging, conversion, and checking accuracy and latency through the desktop app are separate steps.

## Paired development pilot

Before starting the full training workflow, we ran the trained Tiny adapter and pretrained Small with the same explicit language, immediate-EOS setting, greedy decoder and scoring. We selected the first IDs in sorted order within each stratum before decoding. Neither recognizer used an external speech detector in this comparison.

| Development subset | Trained Tiny | Pretrained Small |
|---|---:|---:|
| Russian, 24 clips | 46.67% (126/270) | 27.78% (75/270) |
| Everyday Russian, 12 clips | 52.44% (43/82) | 26.83% (22/82) |
| Narrated Russian, 12 clips | 44.15% (83/188) | 28.19% (53/188) |
| English, 6 clips | 14.71% (15/102) | 8.82% (9/102) |
| Controls emitting words | 0/9 | 9/9 |

Small removed 51 Russian word errors on this sample. Neither model missed a speech clip. Nine synthetic controls are too few to characterize real background rejection, and the 24 Russian clips do not establish a full-test WER. Detection and training must still earn their place on the full benchmark. The script also writes paired raw and Silero-assisted results to `detector-scores.txt`. Report any speech rejected by the detector. The pilot ran alongside a training smoke check, so its wall times are unsuitable for a single-model latency comparison. [Recording IDs and exact scores](paired-pilot.json)

A separate miniature run completed six optimizer updates, saved two Small adapters, froze the selection and completed the test phase. This verified the fresh-adapter training path. No full Small adapter has been trained in this report.
