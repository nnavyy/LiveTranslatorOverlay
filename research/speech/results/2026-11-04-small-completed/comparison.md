# Whisper Small comparison

Selected update 4340. Passed validation criteria against pretrained Small: True.
A failed selection means the adapter is diagnostic. Keep pretrained Small as a separate candidate.

| Split | Model | Russian WER | Everyday RU | Audiobook RU | English WER | Blank speech | False captions | Decode seconds/audio second |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| dev | Previous trained Tiny | 50.24% | 50.57% | 50.06% | 8.86% | 0 | 90/90 | 0.23 |
| dev | Pretrained Small | 25.58% | 27.33% | 24.62% | 4.35% | 0 | 90/90 | 0.72 |
| dev | Adapted Small | 23.28% | 23.41% | 23.21% | 3.77% | 0 | 0/90 | 0.94 |
| test | Previous trained Tiny | 49.72% | 47.98% | 50.72% | 8.96% | 0 | 90/90 | 0.15 |
| test | Pretrained Small | 25.60% | 28.67% | 23.83% | 3.74% | 0 | 90/90 | 0.96 |
| test | Adapted Small | 23.93% | 25.14% | 23.24% | 3.67% | 0 | 0/90 | 0.64 |

Lower WER is better. These are raw recognizer scores. The historical Tiny run suppressed initial EOS, while both Small runs allow an empty caption. The Small baseline and adapter use identical decoding.
These test recordings were used in earlier reports. Confirm any deployment decision on fresh recordings representative of live playback.
