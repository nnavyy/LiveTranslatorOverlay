# Whisper Tiny adapter continuation, 3 November 2026

This run starts from checkpoint 320 of the previous English/Russian decoder LoRA experiment. That checkpoint failed its development criteria. The question is whether one pass over additional read speech improves it enough to clear the same criteria against both the parent and the original pretrained Tiny model.

Read the [training history](../../../../docs/reviews/2026-11-03-expanded-ml.md) for the pilot, both later rounds, runtime comparison, and translation scores. The [result summary](summary.json) records exact counts, settings, hashes, and paired test intervals. The [per-clip scores](clip-scores.csv) let you recompute WER by dividing summed speech edits by summed reference words within a language and split. For controls, count rows where the model's blank flag is zero. The [dataset index](dataset-index.json) records the source and checksum of each recording.

## Design

The new FLEURS subset uses the original train, development, and test assignments. Each language contributes 80 training, 20 development, and 40 test recordings. A second series of synthetic silence, tone, and noise clips contributes 24/12/12 controls. Before fitting, we checked for overlap with the pilot and expanded datasets by recording ID, audio hash, sentence group, and normalized transcript. We also checked that normalized transcripts did not cross splits in the new dataset.

Train for exactly 184 additional updates, shuffling all training examples once. Continue the saved adapter weights with a fresh AdamW optimizer at 0.00005. The previous run saved weights only. Keep the decoder prompt, augmentation, rank, batch size, clipping, and seed from the parent experiment. Evaluate the pretrained model and the parent on development speech and controls before fitting.

The sole new checkpoint must beat both comparators on mean English/Russian development WER, with neither language regressing by more than one percentage point against either comparator. Additional blank speech clips or additional nonspeech emissions also disqualify it. Freeze this decision before running the three models on the fresh test split. A failed development decision stands after diagnostic testing.

## Reproduction

Use the research Python environment with the versions in `research/speech/requirements.txt`. From the repository root, set `$r` to a directory outside Git containing the pinned Whisper Tiny model and the expanded parent run. To recreate the parent, run the default `prepare_expanded.py` with the pilot index, followed by the default `train_expanded.py develop` without a parent argument. For the continuation, use fresh output directories.

```powershell
$pilot = 'research/speech/results/2026-11-01-pilot/dataset-index.json'
$expanded = 'research/speech/results/2026-11-03-expanded/dataset-index.json'
python -c "import json,sys; from pathlib import Path; a,b,out=map(Path,sys.argv[1:]); out.write_text(json.dumps(json.loads(a.read_text(encoding='utf8'))+json.loads(b.read_text(encoding='utf8'))),encoding='utf8')" $pilot $expanded "$r/exclusions.json"
python research/speech/prepare_expanded.py "$r/data" "$r/exclusions.json" --control-series 1
python research/speech/train_expanded.py develop "$r/data/manifest.jsonl" "$r/whisper-tiny" "$r/run" --parent-run "$r/expanded-run"
```

After successful development, inspect `selection.json` and run the locked evaluation once.

```powershell
python research/speech/train_expanded.py test "$r/data/manifest.jsonl" "$r/whisper-tiny" "$r/run" --parent-run "$r/expanded-run"
```
