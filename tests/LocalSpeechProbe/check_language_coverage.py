"""Compare the app catalog with downloaded upstream metadata; no model inference."""

import argparse
import hashlib
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("m2m100_model_card", type=Path)
parser.add_argument("argos_package_index", type=Path)
args = parser.parse_args()

repo = Path(__file__).resolve().parents[2]
catalog = "\n".join(
    (repo / "Core/Localization" / filename).read_text(encoding="utf-8")
    for filename in ("LanguageCatalog.cs", "AdditionalLanguages.cs")
)
targets = sorted(set(re.findall(r'\["([a-z]{2}(?:-TW)?)"\]', catalog)))
card = args.m2m100_model_card.read_text(encoding="utf-8")
language_section = card.split("## Languages covered", 1)[1].split("## ", 1)[0]
supported = set(re.findall(r"\(([a-z]{2})\)", language_section))
if not targets or not {"ru", "en"}.issubset(supported):
    raise ValueError("Missing catalog or source languages; check the input metadata format.")

edges: dict[str, set[str]] = {}
for package in json.loads(args.argos_package_index.read_text(encoding="utf-8")):
    edges.setdefault(package["from_code"], set()).add(package["to_code"])

missing_argos = {}
for source in ("ru", "en"):
    reachable, queue = {source}, [source]
    while queue:
        for target in edges.get(queue.pop(), set()):
            if target not in reachable:
                reachable.add(target)
                queue.append(target)
    missing_argos[source] = [target for target in targets if target not in reachable]

missing_mapped = [
    target for target in targets
    if ("zh" if target == "zh-TW" else target) not in supported
]
print(json.dumps({
    "targetCount": len(targets),
    "targets": targets,
    "sourceLanguages": ["ru", "en"],
    "m2m100MissingNativeCodes": [t for t in targets if t not in supported],
    "m2m100MissingAfterScriptMapping": missing_mapped,
    "requiredScriptConversion": {"zh-TW": "zh model output plus local Traditional Chinese conversion"},
    "argosMissingEvenWithPivots": missing_argos,
    "metadataSha256": {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (args.m2m100_model_card, args.argos_package_index)
    },
    "qualityTested": False,
}, ensure_ascii=False, indent=2))
raise SystemExit(1 if missing_mapped else 0)
