"""Bounded local LoRA experiment. A pilot adapter is never installed in the app."""
import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import random
import time

# Setup/download is a separate step. No network fallback during training or evaluation.
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
import numpy as np
import soundfile as sf
import torch
from peft import LoraConfig, get_peft_model
from transformers import WhisperForConditionalGeneration, WhisperProcessor
from metrics import summarize, validate_splits

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("manifest", type=Path)
parser.add_argument("model", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--steps", type=int, default=20)
args = parser.parse_args()
if not 1 <= args.steps <= 100:
    parser.error("Pilot steps must be between 1 and 100")
provenance = json.loads((args.model / "provenance.json").read_text(encoding="utf-8"))
if provenance["repo"] != "openai/whisper-tiny" or provenance["revision"] != "169d4a4341b33bc18d8881c4b69c2e104e1cc0af":
    raise ValueError("Use the evaluated model revision from download_model.py")
for filename, digest in provenance["files"].items():
    path = (args.model / filename).resolve()
    if not path.is_relative_to(args.model.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError("Model checksum mismatch")
rows = [json.loads(line) for line in args.manifest.read_text(encoding="utf-8").splitlines() if line.strip()]
validate_splits(rows)
for row in rows:
    path = (args.manifest.parent / row["path"]).resolve()
    if not path.is_relative_to(args.manifest.parent.resolve()):
        raise ValueError("Audio path escapes the dataset directory")
    if hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
        raise ValueError(f"Audio checksum mismatch: {row['id']}")
    if row["language"] not in {"en", "ru"} or row["kind"] not in {"speech", "non_speech"}:
        raise ValueError("This pilot supports en/ru speech and non-speech only")
    if not row.get("license") or not row.get("source"):
        raise ValueError("Missing source/license")
args.output.mkdir(parents=True, exist_ok=False)
seed = 20261101
random.seed(seed)
np.random.seed(seed)
torch.manual_seed(seed)
torch.set_num_threads(4)
torch.use_deterministic_algorithms(True)
started = time.monotonic()
processor = WhisperProcessor.from_pretrained(str(args.model), local_files_only=True)
base = WhisperForConditionalGeneration.from_pretrained(str(args.model), local_files_only=True,
    use_safetensors=True, attn_implementation="eager")
base.config.use_cache = False
base.generation_config.forced_decoder_ids = None
features = {}
for row in rows:
    audio, rate = sf.read(args.manifest.parent / row["path"], dtype="float32")
    if rate != 16000 or audio.ndim != 1 or not 0 < len(audio) <= 20 * rate or not np.isfinite(audio).all():
        raise ValueError(f"Expected finite 16kHz mono audio <=20s: {row['id']}")
    features[row["id"]] = processor.feature_extractor(audio, sampling_rate=rate, return_tensors="pt", return_attention_mask=True)

def evaluate(model, split, phase):
    model.eval()
    predictions = []
    for row in (r for r in rows if r["split"] == split):
        if time.monotonic() - started > 1800:
            raise TimeoutError("Thirty-minute pilot time limit")
        before = time.monotonic()
        with torch.inference_mode():
            tokens = model.generate(**features[row["id"]], language=row["language"], task="transcribe",
                max_new_tokens=128, do_sample=False, num_beams=1, use_cache=True)
        prediction = processor.batch_decode(tokens, skip_special_tokens=True)[0].strip()
        result = dict(row, prediction=prediction, inferenceSeconds=time.monotonic() - before)
        predictions.append(result)
        print(f"{phase}/{split}: {row['id']}", flush=True)
    (args.output / f"{phase}-{split}.json").write_text(json.dumps(predictions, ensure_ascii=False, indent=2), encoding="utf-8")
    return summarize(predictions)

baseline = {split: evaluate(base, split, "baseline") for split in ("dev", "test")}
# Generic PEFT wrapper preserves Whisper's input_features interface. Decoder-only
# adaptation keeps this CPU plumbing experiment small; it is not the paper's recipe.
targets = [name for name, module in base.named_modules()
           if name.startswith("model.decoder.") and name.endswith(("q_proj", "v_proj"))]
model = get_peft_model(base, LoraConfig(r=4, lora_alpha=8, lora_dropout=0.0,
    target_modules=targets, bias="none"))
trainable = sum(p.numel() for p in model.parameters() if p.requires_grad)
optimizer = torch.optim.AdamW((p for p in model.parameters() if p.requires_grad), lr=1e-4, weight_decay=0.0)
training = [r for r in rows if r["split"] == "train"]
if not training:
    raise ValueError("No training data")
order = list(training)
random.shuffle(order)
losses = []
model.train()
for step in range(args.steps):
    if time.monotonic() - started > 1800:
        raise TimeoutError("Thirty-minute pilot time limit")
    if step and step % len(order) == 0:
        random.shuffle(order)
    row = order[step % len(order)]
    processor.tokenizer.set_prefix_tokens(language=row["language"], task="transcribe", predict_timestamps=False)
    tokens = processor.tokenizer(row["reference"], return_tensors="pt").input_ids
    # Learn the actual transcript and EOS, including empty negative labels.
    # Never teach the language/task prompt as if it were spoken text.
    labels = tokens[:, 1:].clone()
    labels[:, :len(processor.tokenizer.prefix_tokens) - 1] = -100
    optimizer.zero_grad(set_to_none=True)
    result = model(input_features=features[row["id"]].input_features,
        decoder_input_ids=tokens[:, :-1], labels=labels, use_cache=False)
    if not torch.isfinite(result.loss):
        raise ValueError("Non-finite training loss")
    result.loss.backward()
    torch.nn.utils.clip_grad_norm_((p for p in model.parameters() if p.requires_grad), 1.0)
    optimizer.step()
    losses.append(dict(step=step + 1, id=row["id"], loss=result.loss.item()))
    print(f"Training {step+1}/{args.steps}: loss={result.loss.item():.4f}", flush=True)
model.save_pretrained(args.output / "adapter", safe_serialization=True)
candidate = {split: evaluate(model, split, "candidate") for split in ("dev", "test")}
report = dict(status="pilot-only-not-approved-for-release", seed=seed, steps=args.steps,
    baseModel=provenance["repo"], baseRevision=provenance["revision"],
    manifestSha256=hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
    device="cpu", threads=4, loraRank=4, loraAlpha=8, loraModules=targets,
    trainableParameters=trainable, learningRate=1e-4, batchSize=1,
    decoder="greedy, explicit language, max 128 new tokens", baseline=baseline, candidate=candidate,
    losses=losses, elapsedSeconds=time.monotonic()-started,
    versions={p: importlib.metadata.version(p) for p in ("torch", "transformers", "peft", "numpy", "soundfile")},
    limitations=["Tiny non-random subset; no 95% accuracy claim", "No model selection based on test results",
        "No overlap, music, or spoken closing-phrase challenge recordings in this pilot",
        "No local translation or diarization training; no production model update"])
(args.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(dict(baseline=baseline, candidate=candidate, elapsedSeconds=report["elapsedSeconds"])), flush=True)
