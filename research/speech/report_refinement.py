"""Compare a continuation with its parent on identical audio and runtime settings."""
import argparse
import json
from pathlib import Path

from expanded_utils import choose_refinement
from metrics import summarize


def report(run):
    def read(path):
        return json.loads(path.read_text(encoding="utf-8-sig"))

    selection = read(run / "selection.json")
    config = read(run / "configuration.json")
    if not config.get("refineSelected"):
        raise ValueError("Expected a refinement run")
    lines = ["# Small continuation report", "",
        f"Development pass: **{selection['developmentEligible']}**. Parent adapter SHA256: `{selection['parentAdapterSha256']}`.",
        f"Learning rate {config['learningRate']}; {config['steps']} updates; seed {config['seed']}; device {config['device']}; float32.",
        "", "| Split | Model | Russian WER | English WER | Blank speech | False captions |",
        "|---|---|---:|---:|---:|---:|"]
    summary = dict(configuration=config, selection=selection, scores={}, productionApproved=False)
    changes = []
    for split, folder, name in (("dev", run, selection["checkpoint"]), ("test", run/"locked-test", "candidate")):
        if not folder.exists():
            continue
        parent_rows = read(folder / f"parent-{split}.json")
        candidate_rows = read(folder / f"{name}-{split}.json")
        def identity(rows):
            return {r["id"]: {k:v for k,v in r.items() if k not in ("prediction", "inferenceSeconds")} for r in rows}
        if len(parent_rows) != len(candidate_rows) or identity(parent_rows) != identity(candidate_rows):
            raise ValueError("Parent and candidate predictions are not paired")
        scores = [summarize(rows) for rows in (parent_rows, candidate_rows)]
        summary["scores"][split] = dict(parent=scores[0], candidate=scores[1])
        for lang in ("ru", "en"):
            before, after = (score["speech"][lang] for score in scores)
            changes.append(f"{split} {lang.upper()}: {before['wordErrors']} -> {after['wordErrors']} word errors "
                           f"({after['wordErrors']-before['wordErrors']:+d}); "
                           f"WER change {100*(after['wer']-before['wer']):+.3f} percentage points. Lower is better.")
        for label, score in zip(("Parent", "Continued"), scores):
            ru, en, controls = score["speech"]["ru"], score["speech"]["en"], score["nonSpeech"]
            lines.append(f"| {split} | {label} | {ru['wer']:.2%} | {en['wer']:.2%} | "
                         f"{ru['falseRejections']+en['falseRejections']} | {controls['emittedClips']}/{controls['clips']} |")
        if split == "test":
            summary["historicalTestGuardrailsPassed"] = choose_refinement(scores[0], {"1": scores[1]})["developmentEligible"]
            lines += ["", f"Historical test guardrails passed: **{summary['historicalTestGuardrailsPassed']}**.",
                      "This diagnostic does not change the development-selected checkpoint."]
    lines += ["", *[f"- {change}" for change in changes]]
    if "test" not in summary["scores"]:
        lines += ["", "**Validation results only: no completed test evaluation is included.** "
                  "Compare dev with dev; the earlier 23.93% Russian test WER is a different set of recordings."]
    timing_path = run / "workflow-timing.json"
    if timing_path.exists():
        summary["timing"] = read(timing_path)
        lines += ["", f"Workflow elapsed: {summary['timing']['elapsedSeconds']/3600:.2f} hours (excludes environment setup and final report writing)."]
    lines += ["", "Keep the parent if development or historical test guardrails fail. Do not deploy automatically.",
        "Goal: historical Russian WER <=22%, stretch <=20%; English no worse than the parent; no additional blank speech or false captions.",
        "These are goals, not predicted results. The historical test has been used in previous reports; new generalization claims need fresh audio.",
        "GPU PyTorch/attention settings differ from the original CPU report. Both rows above were re-decoded with identical current settings.",
        "Timing, RAM and GPU allocations are in progress.jsonl in each phase. GPU speed does not itself improve accuracy."]
    text = "\n".join(lines) + "\n"
    (run / "comparison.md").write_text(text, encoding="utf-8")
    (run / "comparison.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(text)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    report(parser.parse_args().run)
