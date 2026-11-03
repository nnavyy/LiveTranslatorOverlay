"""Transcribe a short local clip with the selected adapter from a research run."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from peft import PeftModel
import soundfile as sf
import torch
from transformers import WhisperForConditionalGeneration, WhisperProcessor

from metrics import distance, words


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("audio", type=Path, help="16 kHz mono WAV, up to 20 seconds")
    parser.add_argument("--language", required=True, choices=("en", "ru"))
    parser.add_argument("--model", type=Path, help="Override the local base-model path saved by training")
    parser.add_argument("--reference", help="Expected spoken words, for single-clip WER")
    args = parser.parse_args()
    ref = words(args.reference) if args.reference is not None else None
    if ref == []:
        parser.error("WER requires at least one reference word")
    with sf.SoundFile(args.audio) as clip:
        if clip.samplerate != 16000 or clip.channels != 1 or not 0 < len(clip) <= 320000:
            parser.error("Use a mono 16 kHz audio clip of at most 20 seconds")
        audio = clip.read(dtype="float32")
    if not np.isfinite(audio).all():
        parser.error("Audio contains nonfinite samples")
    selection = json.loads((args.run / "selection.json").read_text(encoding="utf-8"))
    adapter = args.run / f"adapter-{int(selection['checkpoint'])}"
    if hashlib.sha256((adapter / "adapter_model.safetensors").read_bytes()).hexdigest() != selection["adapterSha256"]:
        parser.error("Selected adapter checksum mismatch")
    config = json.loads((adapter / "adapter_config.json").read_text(encoding="utf-8"))
    base_path = args.model or Path(config["base_model_name_or_path"])
    if not base_path.is_dir():
        parser.error("Local base model missing. Supply its directory with --model")
    torch.set_num_threads(4)
    processor = WhisperProcessor.from_pretrained(str(base_path), local_files_only=True)
    base = WhisperForConditionalGeneration.from_pretrained(str(base_path), local_files_only=True,
        use_safetensors=True, attn_implementation="eager")
    base.generation_config.forced_decoder_ids = None
    model = PeftModel.from_pretrained(base, str(adapter), local_files_only=True).eval()
    inputs = processor.feature_extractor(audio, sampling_rate=16000, return_tensors="pt", return_attention_mask=True)
    with torch.inference_mode():
        tokens = model.generate(**inputs, language=args.language, task="transcribe",
            max_new_tokens=128, do_sample=False, num_beams=1, use_cache=True)
    prediction = processor.batch_decode(tokens, skip_special_tokens=True)[0].strip()
    print(f"Transcript: {prediction}")
    if ref is not None:
        errors = distance(ref, words(prediction))
        print(f"Clip WER: {errors/len(ref):.2%} ({errors} edits / {len(ref)} reference words)")


if __name__ == "__main__":
    main()
