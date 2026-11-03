"""Score a saved Whisper decode with and without the fixed Silero clip gate."""
import argparse
import json
from pathlib import Path

from metrics import summarize
from show_scores import print_scores

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("predictions", type=Path)
parser.add_argument("decisions", type=Path)
args = parser.parse_args()
rows = json.loads(args.predictions.read_text(encoding="utf-8"))
decisions = [json.loads(line) for line in args.decisions.read_text(encoding="utf-8-sig").splitlines()]
by_id = {row["id"]: row for row in decisions}
if len(by_id) != len(decisions) or len(rows) != len(by_id) or {r["id"] for r in rows} != by_id.keys():
    parser.error("Predictions and decisions must cover exactly the same unique recordings")
gated = []
for row in rows:
    decision = by_id[row["id"]]
    if (row["sha256"] != decision["audioSha256"] or row["split"] != decision["split"]
            or type(decision["neuralGate"]) is not bool or decision["threshold"] != 0.5
            or decision["vadSha256"] != "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987"):
        parser.error("Decision has mismatched audio, split or detector settings")
    gated.append(dict(row, prediction=row["prediction"] if decision["neuralGate"] else ""))
print_scores("Without speech detector", summarize(rows))
print_scores("With speech detector", summarize(gated))
