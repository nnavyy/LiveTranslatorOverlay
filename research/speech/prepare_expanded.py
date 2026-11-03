"""Freeze an expanded FLEURS subset excluding every old pilot sentence/audio."""
import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import tarfile
import urllib.request

import numpy as np
import soundfile as sf
from expanded_utils import assert_disjoint
from metrics import validate_splits

REVISION = "70bb2e84b976b7e960aa89f1c648e09c59f894dd"
BASE = f"https://huggingface.co/datasets/google/fleurs/resolve/{REVISION}/data"


def labels_by_filename(label_bytes):
    # FLEURS uses literal quotation marks in transcripts, including at line starts.
    rows = csv.reader(io.StringIO(label_bytes.decode("utf-8")), delimiter="\t", quoting=csv.QUOTE_NONE)
    return {row[1]: row for row in rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("previous_index", type=Path)
    parser.add_argument("--exclude-index", type=Path, action="append", default=[],
                        help="Additional earlier dataset index to exclude (repeatable)")
    parser.add_argument("--control-series", type=int, choices=(0, 1, 2), default=0)
    parser.add_argument("--train-per-language", type=int, default=80,
                        help="Number of speech recordings for each of English and Russian (default 80)")
    args = parser.parse_args()
    if args.train_per_language < 1:
        parser.error("--train-per-language must be positive")
    old = json.loads(args.previous_index.read_text(encoding="utf-8"))
    for index in args.exclude_index:
        old.extend(json.loads(index.read_text(encoding="utf-8")))
    excluded_groups = {r["group"] for r in old}
    excluded_hashes = {r["sha256"] for r in old}
    args.output.mkdir(parents=True, exist_ok=False)
    records, rejected = [], []
    group_splits, hashes = {}, set()
    for language, locale in (("en", "en_us"), ("ru", "ru_ru")):
        for split, count in (("train", args.train_per_language), ("dev", 20), ("test", 40)):
            with urllib.request.urlopen(f"{BASE}/{locale}/{split}.tsv", timeout=60) as response:
                label_bytes = response.read()
            (args.output / f"{locale}-{split}.tsv").write_bytes(label_bytes)
            labels = labels_by_filename(label_bytes)
            url = f"{BASE}/{locale}/audio/{split}.tar.gz"
            accepted = 0
            with urllib.request.urlopen(url, timeout=60) as response, tarfile.open(fileobj=response, mode="r|gz") as archive:
                for member in archive:
                    name = Path(member.name).name
                    if not member.isfile() or name not in labels:
                        continue
                    row = labels[name]
                    group = f"fleurs-sentence-{row[0]}"
                    if (group in excluded_groups or group_splits.get(group, split) != split
                            or not 16000 <= int(row[5]) <= 20 * 16000):
                        continue
                    if member.size > 2_000_000:
                        raise ValueError("Unexpectedly large audio member")
                    data = archive.extractfile(member).read()
                    digest = hashlib.sha256(data).hexdigest()
                    if digest in excluded_hashes or digest in hashes:
                        continue
                    audio, rate = sf.read(io.BytesIO(data), dtype="float32")
                    if rate != 16000 or audio.ndim != 1 or len(audio) != int(row[5]) or not np.isfinite(audio).all():
                        raise ValueError(f"Invalid audio format: {name}")
                    # Freeze this exclusion before fitting; no post-hoc test filtering.
                    if np.max(np.abs(audio)) >= 1 or not row[3].strip():
                        rejected.append(dict(member=member.name, locale=locale, split=split, reason="full-scale samples or empty reference"))
                        continue
                    identifier = f"fleurs-{locale}-{split}-{name[:-4]}"
                    filename = identifier + ".wav"
                    (args.output / filename).write_bytes(data)
                    records.append(dict(id=identifier, path=filename, split=split, language=language,
                        kind="speech", reference=row[3], group=group, source=url, sourceMember=member.name,
                        sourceSha256=digest, sha256=digest, license="CC-BY-4.0", sourceRevision=REVISION,
                        seconds=len(audio)/rate, peak=float(np.max(np.abs(audio))), condition="clean"))
                    hashes.add(digest)
                    group_splits[group] = split
                    accepted += 1
                    if accepted % 100 == 0:
                        print(f"Preparing {locale}/{split}: {accepted}/{count}", flush=True)
                    if accepted == count:
                        break
            if accepted != count:
                raise RuntimeError(f"Insufficient eligible clips: {locale}/{split}")
            print(f"Prepared {locale}/{split}: {accepted}", flush=True)

    for split_index, (split, count) in enumerate((("train", 24), ("dev", 12), ("test", 12))):
        for i in range(count):
            rng = np.random.default_rng(20261103 + args.control_series * 1000 + split_index * 100 + i)
            seconds = 2.13 + split_index + i * .17 + args.control_series * .005
            n = int(seconds * 16000)
            kind = ("silence", "tone", "noise")[i % 3]
            if kind == "silence":
                audio = np.zeros(n, dtype="float32")
            elif kind == "tone":
                audio = .08 * np.sin(2*np.pi*(260 + split_index * 173 + i * 29)*np.arange(n)/16000)
            else:
                audio = rng.normal(0, .015 + .002*i, n)
            identifier = f"control-{split}-{i:02}-{kind}"
            if args.control_series:
                identifier = f"s{args.control_series}-{identifier}"
            path = args.output / (identifier + ".wav")
            sf.write(path, audio, 16000, subtype="PCM_16")
            records.append(dict(id=identifier, path=path.name, split=split,
                language="en" if (i//3) % 2 == 0 else "ru", kind="non_speech", reference="",
                group=identifier, source="locally-generated", license="CC0-1.0",
                sha256=hashlib.sha256(path.read_bytes()).hexdigest(), seconds=n/16000, condition=kind))

    validate_splits(records)
    assert_disjoint(records, old)
    manifest = args.output / "manifest.jsonl"
    manifest.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in records), encoding="utf-8")
    # Paired, derived challenges are only for VAD; they are not independent recordings.
    challenges = []
    for row in records:
        if row["split"] not in {"dev", "test"} or row["kind"] != "speech":
            continue
        audio, rate = sf.read(args.output / row["path"], dtype="float32")
        for condition in ("quiet", "noise20dB"):
            seed = int(row["sha256"][:8], 16)
            altered = audio * .02 if condition == "quiet" else audio + np.random.default_rng(seed).normal(
                0, np.sqrt(np.mean(audio.astype("float64")**2))/10, len(audio))
            path = args.output / f"{row['id']}-{condition}.wav"
            sf.write(path, altered, rate, subtype="FLOAT")
            challenges.append(dict(row, id=path.stem, path=path.name, condition=condition,
                parentId=row["id"], sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    (args.output / "gate-manifest.jsonl").write_text("".join(json.dumps(r, ensure_ascii=False)+"\n"
        for r in records + challenges), encoding="utf-8")
    (args.output / "audit.json").write_text(json.dumps(dict(
        status="technical-audit-only-not-human-reannotation", records=len(records),
        priorPilotDisjoint=True, priorIndexSha256=hashlib.sha256(args.previous_index.read_bytes()).hexdigest(),
        additionalIndexSha256=[hashlib.sha256(index.read_bytes()).hexdigest() for index in args.exclude_index],
        trainingSpeechPerLanguage=args.train_per_language,
        manifestSha256=hashlib.sha256(manifest.read_bytes()).hexdigest(), rejected=rejected,
        selection="First eligible archive entries, original splits, not a random sample",
        limitations=["No independent transcript verification", "No local speaker IDs",
                     "Controls are synthetic, no real music/noise or overlapping speech"]), indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
