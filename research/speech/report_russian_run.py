"""Recompute and export the Russian-focused training results without transcript text."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
from metrics import summarize, distance, words, validate_splits
from expanded_utils import choose_candidate, paired_interval

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("root", type=Path)
parser.add_argument("result", type=Path)
args = parser.parse_args()
root, result = args.root, args.result
result.mkdir(exist_ok=True)
run = root / "russian-focus-run-20261103"
data = root / "russian-focus-20261103"
def read(path):
    return json.loads(path.read_text(encoding="utf-8"))
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
rows = [json.loads(s) for s in (data / "manifest.jsonl").read_text(encoding="utf-8").splitlines()]
validate_splits(rows)
for row in rows:
    assert digest(data / row["path"]) == row["sha256"]
sel = read(run / "selection.json")
cfg = read(run / "configuration.json")
assert sel == read(run / "locked-test/selection.json")
assert cfg["steps"] == 3920 and sel["checkpoint"] == "3920"
assert sel["manifestSha256"] == digest(data / "manifest.jsonl") == cfg["manifestSha256"]
assert sel["adapterSha256"] == digest(run / "adapter-3920/adapter_model.safetensors")
assert sel["parentAdapterSha256"] == read(root / "continuation-run-01/selection.json")["adapterSha256"] != sel["adapterSha256"]
losses = read(run / "losses.json")
assert len(losses) == 3920 and {x["id"] for x in losses} == {x["id"] for x in rows if x["split"] == "train"}
scores, intervals = {}, {}
for split, path, candidate in [("dev",run,"3920"),("test",run/"locked-test","candidate")]:
    scores[split], preds = {}, {}
    expected = {x["id"]:(x["reference"],x["sha256"]) for x in rows if x["split"]==split}
    for name in ["baseline","parent",candidate]:
        pred = read(path / f"{name}-{split}.json")
        assert len(pred)==len(expected) and {x["id"]:(x["reference"],x["sha256"]) for x in pred}==expected
        label = "candidate" if name==candidate else name
        preds[label] = {x["id"]:x for x in pred}
        scores[split][label] = summarize(pred)
        assert scores[split][label] == read(path / f"{name}-{split}-summary.json")
    if split == "dev":
        for label,field in [("baseline","eligibleAgainstBase"),("parent","eligibleAgainstParent")]:
            assert choose_candidate(scores[split][label],{candidate:scores[split]["candidate"]})["developmentEligible"] == sel[field]
        assert sel["developmentEligible"] == (sel["eligibleAgainstParent"] and sel["eligibleAgainstBase"])
    else:
        for label in ["parent","baseline"]:
            intervals[label] = {}
            for lang in ["en","ru"]:
                pairs = []
                for idx,row in preds["candidate"].items():
                    if row["language"]==lang and row["kind"]=="speech":
                        ref = words(row["reference"])
                        pairs.append(dict(group=row.get("speaker") or row["group"], referenceWords=len(ref), candidateErrors=distance(ref,words(row["prediction"])), baselineErrors=distance(ref,words(preds[label][idx]["prediction"]))))
                intervals[label][lang] = paired_interval(pairs)
                intervals[label][lang]["method"] = "paired known-reader clusters, otherwise recording groups; 95% percentile bootstrap"
def save(name, obj):
    (result/name).write_text(json.dumps(obj, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
save("summary.json", dict(configuration=cfg, selection=sel, scores=scores, testCandidateMinusComparator=intervals, trainingSpeechMinutes={l:sum(x["seconds"] for x in rows if x["split"]=="train" and x["kind"]=="speech" and x["language"]==l)/60 for l in ["en","ru"]}))
save("dataset-index.json", [{k:r[k] for k in ("id","group","sha256","split","language","kind")} for r in rows])
print("Verified checkpoint, training coverage, selection and all six score summaries. Exported aggregate results and exclusion index.")
import csv
with (result / "clip-scores.csv").open("w",newline="",encoding="utf-8") as f:
    writer = csv.writer(f,lineterminator="\n")
    models = ("baseline","parent","candidate")
    writer.writerow(["split","id","group","language","kind","referenceWords",*[m+"Errors" for m in models],*[m+"Blank" for m in models]])
    for split,path,step in (("dev",run,"3920"),("test",run/"locked-test","candidate")):
        predictions = [{r["id"]:r for r in read(path / f"{name}-{split}.json")} for name in ("baseline","parent",step)]
        for idx,row in predictions[0].items():
            ref = words(row["reference"])
            outputs = [words(p[idx]["prediction"]) for p in predictions]
            writer.writerow([split,idx,row["group"],row["language"],row["kind"],len(ref),*[distance(ref,p) for p in outputs],*[int(not p) for p in outputs]])

for name in ("configuration.json","selection.json","losses.json","report.json"):
    save("training-"+name, read(run/name))
save("test-report.json",read(run/"locked-test/report.json"))
domains = {}
for split,path,candidate in (("dev",run,"3920"),("test",run/"locked-test","candidate")):
    domains[split] = {}
    for name in ("baseline","parent",candidate):
        pred = read(path / f"{name}-{split}.json")
        domains[split][name] = {corpus:summarize([r for r in pred if r["corpus"]==corpus])
            for corpus in ("sova","ruls","librispeech","controls")}
save("domain-scores.json",domains)
