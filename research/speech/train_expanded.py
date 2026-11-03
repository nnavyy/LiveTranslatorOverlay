"""Two-phase local LoRA experiment: development selection, then locked test evaluation."""
import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import random
import time

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
import numpy as np
import soundfile as sf
import torch
from peft import LoraConfig, PeftModel, get_peft_model
from transformers import WhisperForConditionalGeneration, WhisperProcessor
from expanded_utils import choose_candidate
from metrics import summarize, validate_splits
from show_scores import print_scores


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("develop", "test"))
    parser.add_argument("manifest", type=Path)
    parser.add_argument("model", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--parent-run", type=Path, help="Warm-start the selected adapter with a fresh optimizer on a new manifest")
    args = parser.parse_args()
    print(f"{args.phase} | Local CPU, 4 threads\nAudio manifest: {args.manifest.resolve()}\n"
          f"Base model: {args.model.resolve()}\nRun folder: {args.output.resolve()}", flush=True)
    seed = 20261103
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.set_num_threads(4)
    torch.use_deterministic_algorithms(True)
    started = time.monotonic()
    provenance = json.loads((args.model / "provenance.json").read_text(encoding="utf-8"))
    if provenance["repo"] != "openai/whisper-tiny" or provenance["revision"] != "169d4a4341b33bc18d8881c4b69c2e104e1cc0af":
        raise ValueError("Unexpected model revision")
    for filename, digest in provenance["files"].items():
        path = (args.model / filename).resolve()
        if not path.is_relative_to(args.model.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError("Model checksum mismatch")
    manifest_hash = hashlib.sha256(args.manifest.read_bytes()).hexdigest()
    parent_adapter = None
    parent_hash = None
    if args.parent_run:
        parent = json.loads((args.parent_run / "selection.json").read_text(encoding="utf-8"))
        parent_config = json.loads((args.parent_run / "configuration.json").read_text(encoding="utf-8"))
        if parent_config["baseRevision"] != provenance["revision"] or parent["manifestSha256"] == manifest_hash:
            raise ValueError("Warm start requires the same base revision and a new manifest")
        parent_adapter = args.parent_run / f"adapter-{int(parent['checkpoint'])}"
        parent_hash = hashlib.sha256((parent_adapter / "adapter_model.safetensors").read_bytes()).hexdigest()
        if parent_hash != parent["adapterSha256"]:
            raise ValueError("Parent adapter checksum mismatch")
    rows = [json.loads(line) for line in args.manifest.read_text(encoding="utf-8").splitlines()]
    validate_splits(rows)
    for split in ("train", "dev", "test"):
        subset = [r for r in rows if r["split"] == split]
        speech = [r for r in subset if r["kind"] == "speech"]
        print(f"  {split}: {len(speech)} speech clips, {len(subset)-len(speech)} controls", flush=True)
    for row in rows:
        path = (args.manifest.parent / row["path"]).resolve()
        if not path.is_relative_to(args.manifest.parent.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError("Audio path or checksum mismatch")
        if row["language"] not in {"en", "ru"} or not row.get("source") or not row.get("license"):
            raise ValueError("Invalid language/provenance")
    if args.phase == "develop":
        args.output.mkdir(parents=True, exist_ok=False)
        run_dir = args.output
    else:
        selection_path = args.output / "selection.json"
        selection = json.loads(selection_path.read_text(encoding="utf-8"))
        if selection.get("parentAdapterSha256") != parent_hash:
            raise ValueError("Parent adapter differs from development run")
        if selection["manifestSha256"] != manifest_hash:
            raise ValueError("Dataset changed after development selection")
        adapter = args.output / f"adapter-{selection['checkpoint']}"
        if hashlib.sha256((adapter / "adapter_model.safetensors").read_bytes()).hexdigest() != selection["adapterSha256"]:
            raise ValueError("Selected weights changed")
        run_dir = args.output / "locked-test"
        run_dir.mkdir(exist_ok=False)
        (run_dir / "selection.json").write_bytes(selection_path.read_bytes())

    def save(name, data):
        (run_dir / name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    processor = WhisperProcessor.from_pretrained(str(args.model), local_files_only=True)
    def load_base():
        base = WhisperForConditionalGeneration.from_pretrained(str(args.model), local_files_only=True,
            use_safetensors=True, attn_implementation="eager")
        base.generation_config.forced_decoder_ids = None
        return base

    base = load_base()

    def features(row, augmentation="clean", rng=None):
        audio, rate = sf.read(args.manifest.parent / row["path"], dtype="float32")
        if rate != 16000 or audio.ndim != 1 or not 0 < len(audio) <= 20*rate or not np.isfinite(audio).all():
            raise ValueError("Expected finite mono 16kHz <=20-second audio")
        if augmentation == "quiet":
            audio *= .02
        elif augmentation == "noise20dB":
            audio += rng.normal(0, np.sqrt(np.mean(audio.astype("float64")**2))/10, len(audio)).astype("float32")
        return processor.feature_extractor(audio, sampling_rate=rate, return_tensors="pt", return_attention_mask=True)

    def evaluate(model, split, name):
        model.eval()
        predictions = []
        evaluation_rows = [r for r in rows if r["split"] == split]
        for i, row in enumerate(evaluation_rows, start=1):
            if time.monotonic() - started > 3600:
                raise TimeoutError("One-hour experiment phase limit")
            inputs = features(row)
            before = time.monotonic()
            with torch.inference_mode():
                tokens = model.generate(**inputs, language=row["language"], task="transcribe",
                    max_new_tokens=128, do_sample=False, num_beams=1, use_cache=True)
            predictions.append(dict(row, prediction=processor.batch_decode(tokens, skip_special_tokens=True)[0].strip(),
                inferenceSeconds=time.monotonic()-before))
            if i % 10 == 0 or i == len(evaluation_rows):
                print(f"{name}/{split}: {i}/{len(evaluation_rows)} clips ({i/len(evaluation_rows):.0%})", flush=True)
        save(f"{name}-{split}.json", predictions)
        score = summarize(predictions)
        save(f"{name}-{split}-summary.json", score)
        print(json.dumps(dict(name=name, split=split, score=score)), flush=True)
        print_scores(f"{name}/{split}", score)
        return score

    if args.phase == "test":
        baseline = evaluate(base, "test", "baseline")
        if parent_adapter:
            parent_model = PeftModel.from_pretrained(base, str(parent_adapter), local_files_only=True)
            evaluate(parent_model, "test", "parent")
            del parent_model, base
            base = load_base()
        model = PeftModel.from_pretrained(base, str(adapter), local_files_only=True)
        candidate = evaluate(model, "test", "candidate")
        save("report.json", dict(baseline=baseline, candidate=candidate, selection=selection,
            elapsedSeconds=time.monotonic()-started, productionApproved=False))
        return

    configuration = dict(seed=seed, manifestSha256=manifest_hash, baseModel=provenance["repo"],
        baseRevision=provenance["revision"], device="cpu", threads=4, steps=320, checkpoints=[160, 320],
        loraRank=8, loraAlpha=16, loraDropout=0, learningRate=1e-4, batchSize=1,
        weightDecay=0, gradientClip=1, trainingAugmentation="speech: 50% unchanged, 25% gain 0.02, 25% Gaussian 20 dB SNR",
        decoder="greedy, explicit language, transcribe, 128 max new tokens, no prior transcript context",
        selection="Lowest macro en/ru development WER among eligible checkpoints, earliest tie; otherwise diagnostic minimum",
        eligibility="Strict macro WER improvement, no language >1 percentage point worse, no extra blank speech or negative emissions",
        testUse="None during training/selection; independent test command after selection.json freeze",
        versions={p: importlib.metadata.version(p) for p in ("torch", "transformers", "peft", "numpy", "soundfile")},
        productionApproved=False)
    if parent_adapter:
        epoch_steps = sum(row["split"] == "train" for row in rows)
        configuration.update(parentAdapterSha256=parent_hash, parentCheckpoint=parent["checkpoint"],
            optimizerState="fresh AdamW", learningRate=5e-5, steps=epoch_steps, checkpoints=[epoch_steps],
            eligibility="Must pass original guardrails against both pretrained baseline and parent")
    save("configuration.json", configuration)
    baseline = evaluate(base, "dev", "baseline")
    targets = [name for name, _ in base.named_modules()
               if name.startswith("model.decoder.") and name.endswith(("q_proj", "v_proj"))]
    if parent_adapter:
        model = PeftModel.from_pretrained(base, str(parent_adapter), local_files_only=True, is_trainable=True)
        parent_dev = evaluate(model, "dev", "parent")
    else:
        model = get_peft_model(base, LoraConfig(r=8, lora_alpha=16, lora_dropout=0, target_modules=targets, bias="none"))
    parameters = [p for p in model.parameters() if p.requires_grad]
    optimizer = torch.optim.AdamW(parameters, lr=configuration["learningRate"], weight_decay=0)
    training = [r for r in rows if r["split"] == "train"]
    random.shuffle(training)
    augment_rng = np.random.default_rng(seed)
    losses, candidates = [], {}
    for step in range(1, configuration["steps"] + 1):
        if time.monotonic()-started > 3600:
            raise TimeoutError("One-hour experiment phase limit")
        if step > 1 and (step - 1) % len(training) == 0:
            random.shuffle(training)
        row = training[(step - 1) % len(training)]
        draw = augment_rng.random()
        augmentation = "clean" if row["kind"] != "speech" or draw < .5 else ("quiet" if draw < .75 else "noise20dB")
        inputs = features(row, augmentation, augment_rng)
        processor.tokenizer.set_prefix_tokens(language=row["language"], task="transcribe", predict_timestamps=False)
        tokens = processor.tokenizer(row["reference"], return_tensors="pt").input_ids
        labels = tokens[:, 1:].clone()
        labels[:, :len(processor.tokenizer.prefix_tokens)-1] = -100
        model.train()
        optimizer.zero_grad(set_to_none=True)
        result = model(input_features=inputs.input_features, decoder_input_ids=tokens[:, :-1], labels=labels, use_cache=False)
        if not torch.isfinite(result.loss):
            raise ValueError("Nonfinite loss")
        result.loss.backward()
        norm = torch.nn.utils.clip_grad_norm_(parameters, 1.0, error_if_nonfinite=True)
        optimizer.step()
        losses.append(dict(step=step, id=row["id"], augmentation=augmentation,
                           loss=result.loss.item(), gradientNorm=norm.item()))
        if step % 10 == 0 or step == configuration["steps"]:
            save("losses.json", losses)
            print(f"Training {step}/{configuration['steps']} ({step/configuration['steps']:.0%}): "
                  f"loss={result.loss.item():.4f}", flush=True)
        if step in configuration["checkpoints"]:
            model.save_pretrained(args.output / f"adapter-{step}", safe_serialization=True)
            candidates[str(step)] = evaluate(model, "dev", str(step))
    selection = choose_candidate(baseline, candidates)
    if parent_adapter:
        parent_eligible = choose_candidate(parent_dev, candidates)["developmentEligible"]
        selection.update(parentAdapterSha256=parent_hash, eligibleAgainstParent=parent_eligible,
            eligibleAgainstBase=selection["developmentEligible"])
        selection["developmentEligible"] &= parent_eligible
        selection["eligibility"][selection["checkpoint"]] = selection["developmentEligible"]
    selection.update(manifestSha256=manifest_hash, adapterSha256=hashlib.sha256(
        (args.output / f"adapter-{selection['checkpoint']}" / "adapter_model.safetensors").read_bytes()).hexdigest())
    save("selection.json", selection)
    print(f"Selected adapter: {(args.output / ('adapter-' + selection['checkpoint'])).resolve()}\n"
          f"Passed development criteria: {selection['developmentEligible']}", flush=True)
    save("report.json", dict(configuration=configuration, baseline=baseline, candidates=candidates,
        selection=selection, trainableParameters=sum(p.numel() for p in parameters), loraModules=targets,
        elapsedSeconds=time.monotonic()-started, productionApproved=False))


if __name__ == "__main__":
    main()
