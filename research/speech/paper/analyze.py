"""Recompute manuscript statistics; optionally audit the original local WAV files."""
import argparse
import hashlib
import itertools
import json
import math
from pathlib import Path
from statistics import NormalDist

HERE = Path(__file__).resolve().parent
RESULTS = HERE.parent / "results" / "2026-11-01-pilot"


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def wilson(successes, n):
    """Two-sided 95% Wilson score interval, without continuity correction."""
    if not 0 <= successes <= n or n <= 0:
        raise ValueError("Invalid binomial counts")
    z = NormalDist().inv_cdf(0.975)
    proportion = successes / n
    denominator = 1 + z * z / n
    center = (proportion + z * z / (2 * n)) / denominator
    half = z * math.sqrt(proportion * (1 - proportion) / n + z * z / (4 * n * n)) / denominator
    return [max(0, center - half), min(1, center + half)]


def quantile(values, probability):
    ordered = sorted(values)
    index = (len(ordered) - 1) * probability
    lo, hi = math.floor(index), math.ceil(index)
    return ordered[lo] + (index - lo) * (ordered[hi] - ordered[lo])


def bootstrap_wer(baseline, candidate, language):
    """Enumerate every n-out-of-n clip resample (pilot has only 3 or 4 clips)."""
    b = {r["id"]: r for r in baseline if r["kind"] == "speech" and r["language"] == language}
    c = {r["id"]: r for r in candidate if r["kind"] == "speech" and r["language"] == language}
    if not b or b.keys() != c.keys() or len(b) > 6:
        raise ValueError("Expected a small, identically paired pilot sample")
    pairs = []
    for key in b:
        x, y = b[key]["metrics"]["speech"][language], c[key]["metrics"]["speech"][language]
        if x["referenceWords"] != y["referenceWords"] or b[key]["referenceSha256"] != c[key]["referenceSha256"]:
            raise ValueError("Mismatched reference")
        pairs.append((x["wordErrors"], y["wordErrors"], x["referenceWords"]))
    distributions = [[], [], []]
    for sample in itertools.product(pairs, repeat=len(pairs)):
        denominator = sum(row[2] for row in sample)
        baseline_wer = sum(row[0] for row in sample) / denominator
        candidate_wer = sum(row[1] for row in sample) / denominator
        for dist, value in zip(distributions, [baseline_wer, candidate_wer, candidate_wer - baseline_wer]):
            dist.append(value)
    words = sum(row[2] for row in pairs)
    return {
        "clips": len(pairs), "referenceWords": words,
        "baselineErrors": sum(row[0] for row in pairs),
        "candidateErrors": sum(row[1] for row in pairs),
        "baselineWer": sum(row[0] for row in pairs) / words,
        "candidateWer": sum(row[1] for row in pairs) / words,
        "baselineInterval": [quantile(distributions[0], p) for p in [0.025, 0.975]],
        "candidateInterval": [quantile(distributions[1], p) for p in [0.025, 0.975]],
        "pairedDifferenceInterval": [quantile(distributions[2], p) for p in [0.025, 0.975]],
        "resamples": len(distributions[0]),
    }


def mcnemar_exact(b, c):
    """Two-sided conditional exact McNemar test of discordant paired outcomes."""
    n = b + c
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(min(b, c) + 1)) / 2**n) if n else 1.0


def statistics():
    report = read(RESULTS / "report.json")
    output = {"analysisDate": "2026-11-02", "wer": {}, "gates": {}, "rawNonSpeech": {}}
    for split in ["dev", "test"]:
        b = read(RESULTS / f"baseline-{split}-metrics.json")
        c = read(RESULTS / f"candidate-{split}-metrics.json")
        output["wer"][split] = {lang: bootstrap_wer(b, c, lang) for lang in ["en", "ru"]}
        for lang, row in output["wer"][split].items():
            for name in ["baseline", "candidate"]:
                expected = report[name][split]["speech"][lang]
                if row[name + "Errors"] != expected["wordErrors"] or row["referenceWords"] != expected["referenceWords"]:
                    raise ValueError("Aggregates disagree with training report")
        output["rawNonSpeech"][split] = {}
        for name, records in [("baseline", b), ("candidate", c)]:
            negative = [r for r in records if r["kind"] != "speech"]
            emitted = sum(r["metrics"]["nonSpeech"]["emittedClips"] for r in negative)
            output["rawNonSpeech"][split][name] = {"emitted": emitted, "clips": len(negative), "wilson95": wilson(emitted, len(negative))}
    gates = read(RESULTS / "gate-results.json")
    for kind in ["speech", "nonSpeech"]:
        records = [r for r in gates if r["split"] == "test" and (r["kind"] == "speech") == (kind == "speech")]
        n = len(records)
        result = {}
        for name in ["currentEnergyGate", "neuralGate"]:
            accepted = sum(r[name] for r in records)
            result[name] = {"accepted": accepted, "clips": n, "wilson95": wilson(accepted, n)}
        b = sum(r["currentEnergyGate"] and not r["neuralGate"] for r in records)
        c = sum(r["neuralGate"] and not r["currentEnergyGate"] for r in records)
        result["paired"] = {"energyOnly": b, "neuralOnly": c, "exactMcnemarP": mcnemar_exact(b, c)}
        output["gates"][kind] = result
    output["samplePlanning"] = {"zeroEventsForOneSided95UpperBelow1Percent": math.ceil(math.log(.05) / math.log(.99)), "assumption": "Independent representative Bernoulli trials; no multiple-stratum adjustment. Not satisfied by this convenience pilot."}
    output["interpretation"] = "Exploratory only: convenience subset, synthetic controls, one seed. Bootstrap at clip level cannot model unseen domains or unknown speaker dependence; a zero-width paired interval is not equivalence evidence."
    return output


def audit_audio(audio_root):
    import soundfile as sf
    import numpy as np
    index = read(RESULTS / "dataset-index.json")
    raw = [json.loads(line) for line in (audio_root / "manifest.jsonl").read_text(encoding="utf-8").splitlines()]
    by_id = {r["id"]: r for r in raw}
    if len(by_id) != len(raw) or by_id.keys() != {r["id"] for r in index}:
        raise ValueError("Manifest IDs do not match")
    rows, groups, hashes = [], {}, {}
    for record in index:
        path = (audio_root / record["path"]).resolve()
        if not path.is_relative_to(audio_root.resolve()):
            raise ValueError("Audio path escapes dataset root")
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        ref_hash = hashlib.sha256(by_id[record["id"]]["reference"].encode()).hexdigest()
        if digest != record["sha256"] or ref_hash != record["referenceSha256"]:
            raise ValueError("Dataset checksum mismatch")
        audio, sample_rate = sf.read(path, dtype="float32", always_2d=True)
        if sample_rate != 16000 or audio.shape[1] != 1 or not np.isfinite(audio).all():
            raise ValueError("Invalid pilot audio")
        for collection, key in [(groups, record["group"]), (hashes, digest)]:
            if key in collection and collection[key] != record["split"]:
                raise ValueError("Cross-split duplicate/group leakage")
            collection[key] = record["split"]
        rows.append({"id": record["id"], "split": record["split"], "kind": record["kind"], "language": record["language"], "seconds": len(audio) / sample_rate, "sampleRate": sample_rate, "channels": audio.shape[1], "peak": float(np.max(np.abs(audio))), "samplesAtOrAboveFullScale": int((np.abs(audio) >= 1).sum()), "audioHashMatches": True, "referenceHashMatches": True})
    return {"checked": len(rows), "durationSeconds": sum(r["seconds"] for r in rows), "uniqueAudioHashes": len(hashes), "uniqueSentenceOrControlGroups": len(groups), "crossSplitHashOrGroupLeaks": 0, "humanTranscriptReannotation": False, "speakerIdentityAudit": "Unavailable: selected manifest has no speaker IDs; publisher reports train speakers differ from dev/test. Dev/test speaker independence not established.", "clips": rows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audio-root", type=Path)
    parser.add_argument("--output", type=Path, default=HERE / "statistics.json")
    args = parser.parse_args()
    args.output.write_text(json.dumps(statistics(), indent=2) + "\n", encoding="utf-8")
    if args.audio_root:
        (args.output.parent / "dataset-audit.json").write_text(json.dumps(audit_audio(args.audio_root), indent=2) + "\n", encoding="utf-8")
    print("Recomputed statistics and verified published aggregate counts.")
