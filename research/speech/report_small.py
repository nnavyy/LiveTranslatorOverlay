"""Build a paired, historical comparison from saved predictions on the same audio."""
import argparse
import json
from pathlib import Path

from metrics import summarize

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("root", type=Path)
parser.add_argument("run", type=Path)
args = parser.parse_args()
data = args.root / "russian-focus-20261103"
old = args.root / "my-russian-training-20261103-181541"
def read(path):
    return json.loads(path.read_text(encoding="utf-8"))
new_selection = read(args.run / "selection.json")
old_selection = read(old / "selection.json")
if new_selection["manifestSha256"] != old_selection["manifestSha256"]:
    raise ValueError("Historical and new runs use different manifests")
lines = ["# Whisper Small comparison", "",
         f"Selected update {new_selection['checkpoint']}. Passed validation criteria against pretrained Small: {new_selection['developmentEligible']}.",
         "A failed selection means the adapter is diagnostic. Keep pretrained Small as a separate candidate.",
         "", "| Split | Model | Russian WER | Everyday RU | Audiobook RU | English WER | Blank speech | False captions | Decode seconds/audio second |",
         "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
for split in ("dev","test"):
    expected = {r["id"]:(r["sha256"],r["reference"],r["corpus"]) for r in
                (json.loads(s) for s in (data/f"{split}.jsonl").read_text(encoding="utf-8").splitlines())}
    for label, run, name in [("Previous trained Tiny",old,old_selection["checkpoint"]),
                             ("Pretrained Small",args.run,"baseline"),("Adapted Small",args.run,new_selection["checkpoint"])]:
        if split == "test":
            run = run / "locked-test"
            if name != "baseline":
                name = "candidate"
        rows = read(run / f"{name}-{split}.json")
        if len(rows)!=len(expected) or {r["id"]:(r["sha256"],r["reference"],r["corpus"]) for r in rows}!=expected:
            raise ValueError("Predictions do not match the source recordings")
        score = summarize(rows)
        ru, en = score["speech"]["ru"], score["speech"]["en"]
        everyday, books = [summarize([r for r in rows if r["corpus"]==corpus])["speech"]["ru"]["wer"]
                           for corpus in ("sova","ruls")]
        controls = score["nonSpeech"]
        rtf = sum(r["inferenceSeconds"] for r in rows) / sum(r["seconds"] for r in rows)
        lines.append(f"| {split} | {label} | {ru['wer']:.2%} | {everyday:.2%} | {books:.2%} | {en['wer']:.2%} | "
                     f"{ru['falseRejections']+en['falseRejections']} | {controls['emittedClips']}/{controls['clips']} | {rtf:.2f} |")
lines += ["","Lower WER is better. These are raw recognizer scores. The historical Tiny run suppressed initial EOS, while both Small runs allow an empty caption. The Small baseline and adapter use identical decoding.",
          "These test recordings were used in earlier reports. Confirm any deployment decision on fresh recordings representative of live playback."]
report = "\n".join(lines)+"\n"
(args.run/"comparison.md").write_text(report,encoding="utf-8")
print(report)
