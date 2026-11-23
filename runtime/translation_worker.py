"""Local CTranslate2 translation worker supporting one-shot and persistent --daemon modes."""
import hashlib
import json
import os
from pathlib import Path
import sys
import ctranslate2
import sentencepiece as spm
from opencc import OpenCC

HASHES = {
    "config.json": "8f6496adfc930cbfecbe8281112197705c488fab47d34b4829b06d7f478909af",
    "model.bin": "137949450f9cf691f3c92084f14123e82d05ecb23ac3cd688308ce1a95214572",
    "sentencepiece.bpe.model": "d8f7c76ed2a5e0822be39f0a4f95a55eb19c78f4593ce609e2edbc2aea4d380a",
    "shared_vocabulary.json": "7eb5d0ff184c6095c7c10f9911c0aea492250abd12854f9c3d787c64b1c6397e",
}


def load_resources(config_path: Path):
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    folder = Path(config["translationModel"])
    for name, digest in HASHES.items():
        with (folder / name).open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                raise ValueError("Translation model checksum mismatch")
    vocabulary = set(json.loads((folder / "shared_vocabulary.json").read_text(encoding="utf-8")))
    tokenizer = spm.SentencePieceProcessor(model_file=str(folder / "sentencepiece.bpe.model"))

    force_cpu = os.environ.get("LIVETRANSLATOR_MT_DEVICE", "").lower() == "cpu"
    model = None
    if not force_cpu:
        try:
            if ctranslate2.get_cuda_device_count() > 0:
                candidate = ctranslate2.Translator(str(folder), device="cuda", compute_type="int8_float16")
                # Force lazy CUDA BLAS initialization (e.g. cublas64_12.dll) so missing toolkit libs fall back cleanly to CPU.
                candidate.translate_batch([["__en__", "hi", "</s>"]], target_prefix=[["__id__"]], beam_size=1, max_decoding_length=8)
                model = candidate
        except Exception:
            model = None
    if model is None:
        model = ctranslate2.Translator(str(folder), device="cpu", compute_type="int8", inter_threads=1, intra_threads=4)
    return model, tokenizer, vocabulary


def translate_request(request: dict, model, tokenizer, vocabulary, converters: dict) -> str:
    text = request.get("text", "")
    source = request.get("source", "")
    target = request.get("target", "")
    if not isinstance(text, str) or len(text) > 4000:
        raise ValueError("Local translation accepts at most 4000 characters")
    native_source = "zh" if source == "zh-TW" else source
    native_target = "zh" if target == "zh-TW" else target
    if f"__{native_source}__" not in vocabulary or f"__{native_target}__" not in vocabulary:
        raise ValueError("Choose explicit source and target languages supported by the local model; Auto is unsupported")
    tokens = [f"__{native_source}__", *tokenizer.encode(text, out_type=str), "</s>"]
    if len(tokens) > 512:
        raise ValueError("Text too long for local translation; split into shorter passages")
    prefix = f"__{native_target}__"
    result = model.translate_batch([tokens], target_prefix=[[prefix]], beam_size=2, max_decoding_length=256)[0].hypotheses[0]
    if not result or result[0] != prefix or len(result) >= 256:
        raise ValueError("Local translation did not complete; use a shorter passage")
    output = tokenizer.decode(result[1:]).strip()
    if target == "zh-TW":
        converter = converters.get("s2twp")
        if converter is None:
            converter = converters["s2twp"] = OpenCC("s2twp")
        output = converter.convert(output)
    return output


def main() -> int:
    daemon = "--daemon" in sys.argv[2:]
    config_path = Path(sys.argv[1])
    model, tokenizer, vocabulary = load_resources(config_path)
    converters: dict = {}

    if not daemon:
        request = json.load(sys.stdin)
        output = translate_request(request, model, tokenizer, vocabulary, converters)
        print(json.dumps({"text": output}, ensure_ascii=False), flush=True)
        return 0

    print(json.dumps({"ready": True}), flush=True)
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            if request.get("command") == "shutdown":
                print(json.dumps({"ok": True}), flush=True)
                return 0
            output = translate_request(request, model, tokenizer, vocabulary, converters)
            print(json.dumps({"text": output}, ensure_ascii=False), flush=True)
        except Exception as exc:
            print(json.dumps({"error": str(exc)}, ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(json.dumps({"error": str(error)}, ensure_ascii=False), flush=True)
        sys.exit(1)
