"""Recompute this completed run's audit without training or decoding audio."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from expanded_utils import choose_refinement, paired_interval
from metrics import distance, summarize, validate_splits, words


def main(root):
    def read(path):
        return json.loads(path.read_text(encoding="utf-8-sig"))

    def sha(path):
        with path.open("rb") as stream:
            return hashlib.file_digest(stream, "sha256").hexdigest()

    run = root / "my-small-refinement-20261104-081814"
    parent = root / "my-small-training-20261103-213758"
    manifest = root / "russian-focus-20261103/manifest.jsonl"
    config, selection = read(run/"configuration.json"), read(run/"selection.json")
    assert sha(manifest) == selection["manifestSha256"] == config["manifestSha256"]
    parent_choice = read(parent/"selection.json")
    assert sha(parent/f"adapter-{parent_choice['checkpoint']}/adapter_model.safetensors") == selection["parentAdapterSha256"]
    assert sha(run/f"adapter-{selection['checkpoint']}/adapter_model.safetensors") == selection["adapterSha256"]
    manifest_rows = [json.loads(s) for s in manifest.read_text(encoding="utf-8").splitlines()]
    validate_splits(manifest_rows)
    expected = {r["id"]: r for r in manifest_rows if r["split"] == "dev"}
    before = read(run/"parent-dev.json")
    after = read(run/f"{selection['checkpoint']}-dev.json")
    for rows in (before, after):
        assert len(rows) == len(expected) == len({r['id'] for r in rows})
        assert {r["id"]: {k:v for k,v in r.items() if k not in ("prediction", "inferenceSeconds")} for r in rows} == expected
    scores = [summarize(rows) for rows in (before, after)]
    assert scores[0] == read(run/"parent-dev-summary.json")
    assert scores[1] == read(run/f"{selection['checkpoint']}-dev-summary.json")
    assert choose_refinement(scores[0], {selection['checkpoint']: scores[1]})["developmentEligible"] == selection["developmentEligible"]
    lookup = {r["id"]:r for r in before}
    changes = []
    for row in after:
        if row["kind"] != "speech":
            continue
        ref = words(row["reference"])
        old, new = words(lookup[row["id"]]["prediction"]), words(row["prediction"])
        changes.append(dict(id=row['id'], group=row['group'], language=row['language'], corpus=row['corpus'],
            baselineErrors=distance(ref,old), candidateErrors=distance(ref,new), referenceWords=len(ref),
            predictionChanged=old != new))
    language_changes = {}
    for language in ("ru", "en"):
        pairs = [r for r in changes if r['language']==language]
        language_changes[language] = dict(
            improvedClips=sum(r['candidateErrors']<r['baselineErrors'] for r in pairs),
            worsenedClips=sum(r['candidateErrors']>r['baselineErrors'] for r in pairs),
            tiedClips=sum(r['candidateErrors']==r['baselineErrors'] for r in pairs),
            changedTranscripts=sum(r['predictionChanged'] for r in pairs),
            uncertainty=paired_interval(pairs))
    corpora = {}
    for corpus in sorted({r['corpus'] for r in changes}):
        corpora[corpus] = {label: summarize([r for r in rows if r['kind']=='speech' and r['corpus']==corpus])
                           for label,rows in zip(('parent','candidate'),(before,after))}
    progress = [json.loads(s) for s in (run/'progress.jsonl').read_text().splitlines()]
    stages = [r for r in progress if r['completed']==r['total']]
    summary = dict(run=run.name, configuration=config, selection=selection,
        manifestSplitCounts=dict(Counter(r['split'] for r in manifest_rows)),
        scores=dict(parent=scores[0],candidate=scores[1]), languageChanges=language_changes, corpora=corpora,
        workflowTiming=read(run/'workflow-timing.json'), stageTimings=stages,
        sampledMemory=dict(maxProcessRssMiB=max(r['processRssMiB'] for r in progress),
            maxProcessPrivateMiB=max(r['processPrivateMiB'] for r in progress),
            minAvailableRamMiB=min(r['availableRamMiB'] for r in progress),
            maxGpuAllocatedMiB=max(r['gpuPeakAllocatedMiB'] for r in progress),
            maxGpuReservedMiB=max(r['gpuReservedMiB'] for r in progress)),
        testRunPresent=(run/'locked-test').exists(),
        sourceHashes={name:sha(run/name) for name in ('configuration.json','selection.json','parent-dev.json',
            f"{selection['checkpoint']}-dev.json",'progress.jsonl','workflow-timing.json')})
    (Path(__file__).parent/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    print(json.dumps({k:summary[k] for k in ('scores','languageChanges','sampledMemory','workflowTiming','testRunPresent')},indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('research_root',type=Path)
    main(parser.parse_args().research_root)
