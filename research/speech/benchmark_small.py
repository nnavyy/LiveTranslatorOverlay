"""Paired development pilot before investing in a full Whisper Small training run."""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path
import time

import soundfile as sf
import torch
from peft import PeftModel
from transformers import WhisperForConditionalGeneration, WhisperProcessor

from empty_caption import allow_empty_caption
from metrics import summarize

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("research_root", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = args.research_root.resolve()
dataset = root / "russian-focus-20261103"
rows = sorted((json.loads(line) for line in (dataset/"dev.jsonl").read_text(encoding="utf-8").splitlines()),
              key=lambda row: row["id"])
limits = dict(sova=12, ruls=12, librispeech=6, silence=3, tone=3, noise=3)
counts = Counter()
subset = []
for row in rows:
    group = row.get("condition", row["corpus"])
    if row["split"] != "dev":
        raise ValueError("Expected only development clips")
    if counts[group] < limits[group]:
        subset.append(row)
        counts[group] += 1
if counts != limits:
    raise ValueError("Missing pilot strata")
torch.set_num_threads(4)
scores = {}
for name, folder in [("trainedTiny", "whisper-tiny-169d4a4"), ("pretrainedSmall", "whisper-small-973afd2")]:
    processor = WhisperProcessor.from_pretrained(root/folder, local_files_only=True)
    model = WhisperForConditionalGeneration.from_pretrained(root/folder, local_files_only=True,
        use_safetensors=True, attn_implementation="eager")
    model.generation_config.forced_decoder_ids = None
    allow_empty_caption(model)
    if name == "trainedTiny":
        run = root / "my-russian-training-20261103-181541"
        selected = json.loads((run/"selection.json").read_text())
        adapter = run / ("adapter-" + selected["checkpoint"])
        if hashlib.sha256((adapter/"adapter_model.safetensors").read_bytes()).hexdigest() != selected["adapterSha256"]:
            raise ValueError("Tiny adapter checksum mismatch")
        model = PeftModel.from_pretrained(model, str(adapter), local_files_only=True)
    model.eval()
    predictions = []
    start = time.monotonic()
    for row in subset:
        audio_path = dataset / row["path"]
        if hashlib.sha256(audio_path.read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError("Pilot audio checksum mismatch")
        audio, rate = sf.read(audio_path, dtype="float32")
        features = processor.feature_extractor(audio, sampling_rate=rate, return_tensors="pt", return_attention_mask=True)
        with torch.inference_mode():
            tokens = model.generate(**features, language=row["language"], task="transcribe", max_new_tokens=128,
                                    do_sample=False, num_beams=1, use_cache=True)
        predictions.append(dict(row, prediction=processor.batch_decode(tokens, skip_special_tokens=True)[0].strip()))
    scores[name] = dict(all=summarize(predictions), seconds=time.monotonic()-start,
        russianBySource={source:summarize([row for row in predictions if row["corpus"]==source])["speech"]["ru"]
                         for source in ("ruls","sova")})
    print(name, scores[name], flush=True)
    del model
args.output.write_text(json.dumps(dict(ids=[r["id"] for r in subset], scores=scores),indent=2)+"\n",encoding="utf-8")
