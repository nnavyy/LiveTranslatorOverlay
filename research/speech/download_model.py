"""Explicit one-time setup: pinned upstream safetensors and tokenizer files only."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import time
import hashlib
import json
from pathlib import Path
import urllib.request

REPO = "openai/whisper-tiny"
REVISION = "169d4a4341b33bc18d8881c4b69c2e104e1cc0af"
FILES = {"added_tokens.json", "config.json", "generation_config.json", "merges.txt", "model.safetensors",
         "normalizer.json", "preprocessor_config.json", "special_tokens_map.json", "tokenizer.json",
         "tokenizer_config.json", "vocab.json"}
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
parser.add_argument("--size", choices=("tiny", "small"), default="tiny")
args = parser.parse_args()
if args.size == "small":
    REPO = "openai/whisper-small"
    REVISION = "973afd24965f72e36ca33b3055d56a652f456b4d"
args.output.mkdir(parents=True, exist_ok=False)
with urllib.request.urlopen(f"https://huggingface.co/api/models/{REPO}/revision/{REVISION}?blobs=true", timeout=60) as response:
    metadata = json.load(response)
hashes = {}
for entry in metadata["siblings"]:
    name = entry["rfilename"]
    if name not in FILES:
        continue
    path = args.output / name
    url = f"https://huggingface.co/{REPO}/resolve/{REVISION}/{name}"
    if entry["size"] > 64 * 1024**2:
        size = entry["size"]
        chunk_size = 8 * 1024**2

        def fetch(start):
            end = min(size, start + chunk_size) - 1
            request = urllib.request.Request(url, headers={"Range": f"bytes={start}-{end}"})
            for attempt in range(4):
                try:
                    with urllib.request.urlopen(request, timeout=40) as response:
                        if response.headers.get("Content-Range") != f"bytes {start}-{end}/{size}":
                            raise IOError("Unexpected upstream byte range")
                        chunk = response.read(end-start+1)
                        if len(chunk) != end-start+1:
                            raise IOError("Short upstream byte range")
                        return chunk
                except OSError:
                    if attempt == 3:
                        raise
                    time.sleep(2 ** attempt)

        with path.open("wb") as output, ThreadPoolExecutor(4) as pool:
            for offset in range(0, size, 4 * chunk_size):
                for chunk in pool.map(fetch, range(offset, min(offset + 4 * chunk_size, size), chunk_size)):
                    output.write(chunk)
    else:
        with urllib.request.urlopen(url, timeout=60) as response:
            path.write_bytes(response.read())
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if path.stat().st_size != entry["size"] or (entry.get("lfs") and digest != entry["lfs"]["sha256"]):
        raise ValueError(f"Upstream size/checksum mismatch: {name}")
    hashes[name] = digest
    print(f"Verified {name}", flush=True)
if hashes.keys() != FILES:
    raise ValueError("Missing model files")
(args.output / "provenance.json").write_text(json.dumps(dict(repo=REPO, revision=REVISION, files=hashes), indent=2), encoding="utf-8")
