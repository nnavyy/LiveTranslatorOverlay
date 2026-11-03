"""Display saved speech evaluation summaries without running inference."""
import argparse
import json
from pathlib import Path


def print_scores(label, score):
    print(label)
    for language, row in score["speech"].items():
        print(f"  {language.upper()} WER {row['wer']:.2%} "
              f"({row['wordErrors']}/{row['referenceWords']} words), "
              f"{row['clips']} speech clips, {row['falseRejections']} blank")
    if set(score["speech"]) == {"en", "ru"}:
        mean = sum(row["wer"] for row in score["speech"].values()) / 2
        print(f"  Mean English/Russian WER {mean:.2%}")
    controls = score["nonSpeech"]
    if controls["clips"]:
        print(f"  False captions on controls {controls['emissionRate']:.2%} "
              f"({controls['emittedClips']}/{controls['clips']})")
    else:
        print("  No nonspeech controls evaluated")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    args = parser.parse_args()
    files = sorted(args.run.glob("*-dev-summary.json")) + sorted(
        (args.run / "locked-test").glob("*-test-summary.json"))
    if not files:
        parser.error("No completed evaluation summaries found in this run directory")
    for path in files:
        print_scores(path.stem.removesuffix("-summary"), json.loads(path.read_text(encoding="utf-8")))
