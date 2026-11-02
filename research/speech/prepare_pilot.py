"""Download a bounded FLEURS subset and generate deterministic non-speech controls."""
import argparse
import csv
import hashlib
import io
import json
import math
from pathlib import Path
import random
import struct
import tarfile
import urllib.request
import wave

REVISION = "70bb2e84b976b7e960aa89f1c648e09c59f894dd"
BASE = f"https://huggingface.co/datasets/google/fleurs/resolve/{REVISION}/data"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=False)
records = []
for language, locale in (("en", "en_us"), ("ru", "ru_ru")):
    for split, count in (("train", 8), ("dev", 3), ("test", 4)):
        with urllib.request.urlopen(f"{BASE}/{locale}/{split}.tsv", timeout=60) as response:
            rows = list(csv.reader(io.StringIO(response.read().decode("utf-8")), delimiter="\t"))
        labels = {row[1]: row for row in rows}
        url = f"{BASE}/{locale}/audio/{split}.tar.gz"
        accepted = 0
        # Stream only the beginning of each archive. Never extract archive paths.
        with urllib.request.urlopen(url, timeout=60) as response, tarfile.open(fileobj=response, mode="r|gz") as archive:
            for member in archive:
                name = Path(member.name).name
                if not member.isfile() or name not in labels:
                    continue
                row = labels[name]
                if not 16000 <= int(row[5]) <= 20 * 16000:
                    continue
                data = archive.extractfile(member).read()
                identifier = f"fleurs-{locale}-{split}-{name[:-4]}"
                filename = identifier + ".wav"
                (args.output / filename).write_bytes(data)
                records.append(dict(id=identifier, path=filename, split=split, language=language,
                    kind="speech", reference=row[3], group=f"fleurs-sentence-{row[0]}",
                    source=url, sourceMember=member.name, sourceSha256=hashlib.sha256(data).hexdigest(),
                    sha256=hashlib.sha256((args.output / filename).read_bytes()).hexdigest(),
                    license="CC-BY-4.0", sourceRevision=REVISION))
                accepted += 1
                if accepted == count:
                    break
        if accepted != count:
            raise RuntimeError(f"Only found {accepted}/{count} samples for {locale}/{split}")
        print(f"Prepared {locale}/{split}: {accepted}", flush=True)

for index, split in enumerate(("train", "dev", "test")):
    rng = random.Random(20261101 + index)
    n = (3 + index) * 16000
    controls = {"silence": [0.0] * n,
                "tone": [0.1 * math.sin(2 * math.pi * (310 + index * 137) * i / 16000) for i in range(n)],
                "noise": [rng.gauss(0, 0.015) for _ in range(n)]}
    for kind, audio in controls.items():
        identifier = f"generated-{split}-{kind}"
        filename = identifier + ".wav"
        with wave.open(str(args.output / filename), "wb") as wav:
            wav.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
            wav.writeframes(b"".join(struct.pack("<h", round(max(-1, min(1, x)) * 32767)) for x in audio))
        records.append(dict(id=identifier, path=filename, split=split, language="en", kind="non_speech",
            reference="", group=identifier, source="Deterministic signal generator in prepare_pilot.py",
            sha256=hashlib.sha256((args.output / filename).read_bytes()).hexdigest(), license="CC0-1.0"))

# A source sentence may have several recordings or translated versions: keep it in one split.
groups = {}
for record in records:
    previous = groups.setdefault(record["group"], record["split"])
    if previous != record["split"]:
        raise ValueError(f"Cross-split sentence leakage: {record['group']}")
manifest = args.output / "manifest.jsonl"
manifest.write_text("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in records), encoding="utf-8")
print(f"Wrote {len(records)} records to {manifest}")
