"""Build the frozen 70/15/15 Russian-focused ASR dataset. No model or trainer is loaded."""
import argparse
from collections import Counter, defaultdict
import hashlib
import io
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import wave

import numpy as np
import pyarrow.parquet as pq
import soundfile as sf

from metrics import validate_splits, words

PACKAGE = Path(__file__).parent / "results/2026-11-03-russian-data"
PRIOR = {
    "ml-pilot-2026-11-01": "1ea6e26a3c449a5f9e24d9b2a66e474e3a7d3a75e23b4e4ea986f6dd2faec2f2",
    "expanded-2026-11-03-v2": "b5fe8369b5a18cea6c0df81a9356c1c84705279ea08ec3daa9b9029630abd119",
    "continuation-data-02": "721168d2c79213881ffa8ca006aecec42efe563ad15691a46179052a687a7e1b",
    "round4-data-500": "b0a8b09b95abc3495dba0e555ee91795b6959ba010084599f1de5c3a65918d7e",
}
RATIOS = {"train": 70, "dev": 15, "test": 15}
TOTALS = {"ruls": 2000, "sova": 2000, "librispeech": 1000, "controls": 600}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def pcm16(audio):
    return np.floor(np.clip(audio * 32768, -32768, 32767)).astype("<i2")


def wav_bytes(samples):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as out:
        out.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        out.writeframes(samples.tobytes())
    return buf.getvalue()


def jsonl(path, rows):
    path.write_bytes("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows).encode())


def previous(root):
    checksums, references = set(), set()
    for folder, expected in PRIOR.items():
        path = root / folder / "manifest.jsonl"
        if digest(path) != expected:
            raise ValueError(f"Unexpected previous manifest: {path}")
        for line in path.read_text(encoding="utf-8").splitlines():
            r = json.loads(line)
            audio = path.parent / r["path"]
            if digest(audio) != r["sha256"]:
                raise ValueError(f"Previous recording checksum mismatch: {audio}")
            samples, rate = sf.read(audio, dtype="float32")
            if rate != 16000 or samples.ndim != 1:
                raise ValueError("Prior recording has unexpected format")
            checksums.add(hashlib.sha256(pcm16(samples)).hexdigest())
            if r["kind"] == "speech":
                references.add(tuple(words(r["reference"])))
    return checksums, references


def speech_row(source, entry, offset):
    corpus = source["corpus"]
    member = entry["audio"]["path"]
    speaker = None
    if corpus == "ruls":
        member = entry["audio_filepath"]
        _, reader, book, filename = member.split("/")
        speaker = "librivox-" + reader
        chapter = f"ruls-{book}-{Path(filename).stem.rsplit('_', 1)[0]}"
        reference = entry["text"]
    elif corpus == "librispeech":
        speaker = f"librivox-{entry['speaker_id']}"
        chapter = f"librispeech-{entry['chapter_id']}"
        reference = entry["text"].lower()
    else:
        # Some RuDevices shards omit the WAV filename. The shard row locates
        # the source and the encoded-audio hash identifies the recording.
        member = member or hashlib.sha256(entry["audio"]["bytes"]).hexdigest()
        chapter = "sova-" + Path(member).stem
        reference = entry["transcription"]
    # Hash the corpus-relative source ID, independent of the upstream split or shard.
    identifier = corpus + "-" + hashlib.sha256(Path(member).name.encode()).hexdigest()[:24]
    samples, rate = sf.read(io.BytesIO(entry["audio"]["bytes"]), dtype="float32")
    if rate != 16000 or samples.ndim != 1 or not 16000 <= len(samples) <= 320000:
        return None
    if not np.isfinite(samples).all() or not np.any(samples) or not words(reference):
        return None
    if corpus == "ruls" and abs(entry["duration"] - len(samples)/16000) > .02:
        return None
    pcm = pcm16(samples)
    if not np.any(pcm):
        return None
    row = dict(id=identifier, path=f"audio/{identifier}.wav", split=source["split"],
               language=source["language"], kind="speech", reference=reference.strip(), corpus=corpus,
               group=chapter, speaker=speaker, source=source["url"], sourceMember=member,
               sourceRow=offset, sourceFile=source["file"], seconds=len(samples)/16000,
               license="Public domain in USA (OpenSLR 96); mirror metadata CC BY 4.0" if corpus=="ruls" else "CC-BY-4.0",
               pcmSha256=hashlib.sha256(pcm).hexdigest())
    return row


def build(cache, prior, out):
    sources = json.loads((PACKAGE / "sources.json").read_text())
    hashes, refs = previous(prior)
    candidates = defaultdict(list)
    for src in sources:
        path = cache / src["file"]
        if path.stat().st_size != src["bytes"] or digest(path) != src["sha256"]:
            raise ValueError(f"Source checksum mismatch: {path}")
        offset = 0
        for batch in pq.ParquetFile(path).iter_batches(batch_size=16):
            for entry in batch.to_pylist():
                row = speech_row(src, entry, offset)
                offset += 1
                if row:
                    candidates[src["corpus"],src["split"]].append(row)
        print(f"Indexed {path.name}", flush=True)

    rows, ids, group_split, reader_split = [], set(), {}, {}
    for split in ("test", "dev", "train"):
        for corpus in ("ruls", "sova", "librispeech"):
            pool = sorted(candidates[corpus,split], key=lambda r:r["id"])
            # Deal one recording per known reader per pass, in hashed recording order.
            turns = Counter()
            order = []
            for r in pool:
                turns[r["speaker"]] += 1
                order.append((turns[r["speaker"]], r["id"], r))
            target = TOTALS[corpus]*RATIOS[split]//100
            accepted = 0
            for _, _, r in sorted(order, key=lambda item:item[:2]):
                ref = tuple(words(r["reference"]))
                if r["pcmSha256"] in hashes or ref in refs or r["id"] in ids:
                    continue
                if group_split.get(r["group"], split) != split or reader_split.get(r["speaker"], split) != split:
                    continue
                ids.add(r["id"])
                refs.add(ref)
                hashes.add(r["pcmSha256"])
                group_split[r["group"]] = split
                if r["speaker"]:
                    reader_split[r["speaker"]] = split
                rows.append(r)
                accepted += 1
                if accepted == target:
                    break
            if accepted != target:
                raise ValueError(f"Only {accepted}/{target} eligible {corpus} {split} clips")

    (out / "audio").mkdir()
    by_file = defaultdict(dict)
    for r in rows:
        by_file[r["sourceFile"]][r["sourceRow"]] = r
    for filename, wanted in by_file.items():
        offset = 0
        for batch in pq.ParquetFile(cache / filename).iter_batches(batch_size=24, columns=["audio"]):
            for entry in batch.to_pylist():
                if offset in wanted:
                    r = wanted[offset]
                    samples, _ = sf.read(io.BytesIO(entry["audio"]["bytes"]), dtype="float32")
                    pcm = pcm16(samples)
                    if hashlib.sha256(pcm).hexdigest() != r["pcmSha256"]:
                        raise ValueError("Audio changed between passes")
                    (out / r["path"]).write_bytes(wav_bytes(pcm))
                offset += 1

    rng = np.random.default_rng(2026110305)
    sequence = 0
    for split, pct in RATIOS.items():
        for i in range(TOTALS["controls"]*pct//100):
            condition = ("silence", "tone", "noise")[i % 3]
            n = int(rng.integers(16000, 144000))
            while True:
                if condition == "silence":
                    signal = np.zeros(n)
                elif condition == "tone":
                    signal = rng.uniform(.025,.12)*np.sin(2*np.pi*rng.uniform(120,1800)*np.arange(n)/16000)
                else:
                    signal = rng.normal(0,rng.uniform(.004,.04),n)
                pcm = pcm16(signal)
                checksum = hashlib.sha256(pcm).hexdigest()
                if checksum not in hashes:
                    break
                n += 1
            hashes.add(checksum)
            identifier = f"ru-prep-control-{sequence:04d}"
            path = f"audio/{identifier}.wav"
            (out / path).write_bytes(wav_bytes(pcm))
            rows.append(dict(id=identifier, path=path, split=split, language=("ru","en")[(i//3)%2],
                             kind="non_speech", reference="", corpus="controls", condition=condition,
                             group=identifier, speaker=None, source="locally-generated", license="CC0-1.0",
                             pcmSha256=checksum, seconds=n/16000))
            sequence += 1

    rows.sort(key=lambda r:r["id"])
    for r in rows:
        r["sha256"] = digest(out / r["path"])
    validate_splits(rows)
    jsonl(out / "manifest.jsonl", rows)
    summary = {}
    for split, pct in RATIOS.items():
        part = [r for r in rows if r["split"]==split]
        jsonl(out / f"{split}.jsonl", part)
        summary[split] = {}
        for corpus, total in TOTALS.items():
            subset = [r for r in part if r["corpus"]==corpus]
            if len(subset) != pct*total//100:
                raise ValueError(f"Incorrect split size: {split}/{corpus}")
            summary[split][corpus] = dict(clips=len(subset), seconds=round(sum(r["seconds"] for r in subset),4),
                                         knownReaders=len({r["speaker"] for r in subset if r["speaker"]}))
    audit = dict(status="prepared", splits=summary, manifestSha256={p.name:digest(p) for p in sorted(out.glob("*.jsonl"))},
                 previousManifests=PRIOR, sourcesSha256=digest(PACKAGE/"sources.json"))
    (out / "audit.json").write_text(json.dumps(audit,indent=2)+"\n",encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("cache","prior","output"):
        parser.add_argument(arg, type=Path)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Choose a new output directory")
    with TemporaryDirectory(dir=args.output.parent) as temporary:
        staging = Path(temporary) / "dataset"
        staging.mkdir()
        build(args.cache, args.prior, staging)
        staging.rename(args.output)
    print(args.output.resolve())
