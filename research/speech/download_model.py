"""Explicit one-time setup: pinned upstream safetensors and tokenizer files only."""
import argparse
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
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=False)
with urllib.request.urlopen(f"https://huggingface.co/api/models/{REPO}/revision/{REVISION}?blobs=true", timeout=60) as response:
    metadata = json.load(response)
hashes = {}
for entry in metadata["siblings"]:
    name = entry["rfilename"]
    if name not in FILES:
        continue
    path = args.output / name
    digest = hashlib.sha256()
    with urllib.request.urlopen(f"https://huggingface.co/{REPO}/resolve/{REVISION}/{name}", timeout=60) as response, path.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
            digest.update(chunk)
    if path.stat().st_size != entry["size"] or (entry.get("lfs") and digest.hexdigest() != entry["lfs"]["sha256"]):
        raise ValueError(f"Upstream size/checksum mismatch: {name}")
    hashes[name] = digest.hexdigest()
    print(f"Verified {name}", flush=True)
if hashes.keys() != FILES:
    raise ValueError("Missing model files")
(args.output / "provenance.json").write_text(json.dumps(dict(repo=REPO, revision=REVISION, files=hashes), indent=2), encoding="utf-8")
