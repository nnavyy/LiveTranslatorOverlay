"""Local CTranslate2 translation worker supporting one-shot and persistent --daemon modes."""
from collections import OrderedDict
import hashlib
import json
import os
from pathlib import Path
import sys
import ctranslate2
import sentencepiece as spm
from opencc import OpenCC

_CUDA_DLL_HANDLES = []
_MAX_CACHE_ENTRIES = 256

HASHES = {
    "config.json": "8f6496adfc930cbfecbe8281112197705c488fab47d34b4829b06d7f478909af",
    "model.bin": "137949450f9cf691f3c92084f14123e82d05ecb23ac3cd688308ce1a95214572",
    "sentencepiece.bpe.model": "d8f7c76ed2a5e0822be39f0a4f95a55eb19c78f4593ce609e2edbc2aea4d380a",
    "shared_vocabulary.json": "7eb5d0ff184c6095c7c10f9911c0aea492250abd12854f9c3d787c64b1c6397e",
}


def _register_cuda_dll_dirs(config: dict, config_dir: Path) -> None:
    if os.name != "nt":
        return

    venv_roots = []
    for exe in (config.get("python"), sys.executable):
        if isinstance(exe, str) and exe.strip():
            venv_roots.append(Path(exe).resolve().parent.parent)

    candidates = []
    for root in venv_roots:
        candidates.extend([
            root / "Lib" / "site-packages" / "torch" / "lib",
            root / "Lib" / "site-packages" / "nvidia" / "cublas" / "bin",
            root / "Lib" / "site-packages" / "nvidia" / "cuda_runtime" / "bin",
        ])
    candidates.append(config_dir / "cuda-libs")

    seen = set()
    existing_path = os.environ.get("PATH", "")
    path_parts = [p for p in existing_path.split(os.pathsep) if p]
    prepend = []

    for directory in candidates:
        resolved = str(directory)
        key = resolved.lower()
        if key in seen or not directory.is_dir():
            continue
        seen.add(key)
        prepend.append(resolved)
        if hasattr(os, "add_dll_directory"):
            try:
                _CUDA_DLL_HANDLES.append(os.add_dll_directory(resolved))
            except OSError:
                pass

    if prepend:
        os.environ["PATH"] = os.pathsep.join(prepend + path_parts)


def load_resources(config_path: Path):
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    _register_cuda_dll_dirs(config, config_path.parent)
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
        intra = min(max((os.cpu_count() or 4) - 2, 4), 8)
        model = ctranslate2.Translator(str(folder), device="cpu", compute_type="int8", inter_threads=1, intra_threads=intra)
    return model, tokenizer, vocabulary


def translate_request(request: dict, model, tokenizer, vocabulary, converters: dict, cache: OrderedDict | None = None) -> str:
    text = request.get("text", "")
    source = request.get("source", "")
    target = request.get("target", "")
    fast = bool(request.get("fast", False))
    if not isinstance(text, str) or len(text) > 4000:
        raise ValueError("Local translation accepts at most 4000 characters")
    native_source = "zh" if source == "zh-TW" else source
    native_target = "zh" if target == "zh-TW" else target
    if f"__{native_source}__" not in vocabulary or f"__{native_target}__" not in vocabulary:
        raise ValueError("Choose explicit source and target languages supported by the local model; Auto is unsupported")

    cache_key = (source, target, fast, text)
    if cache is not None:
        cached = cache.get(cache_key)
        if cached is None and fast:
            cached = cache.get((source, target, False, text))
        if cached is not None:
            return cached

    tokens = [f"__{native_source}__", *tokenizer.encode(text, out_type=str), "</s>"]
    if len(tokens) > 512:
        raise ValueError("Text too long for local translation; split into shorter passages")
    prefix = f"__{native_target}__"
    beam_size = 1 if fast else 2
    result = model.translate_batch([tokens], target_prefix=[[prefix]], beam_size=beam_size, max_decoding_length=256)[0].hypotheses[0]
    if not result or result[0] != prefix or len(result) >= 256:
        raise ValueError("Local translation did not complete; use a shorter passage")
    output = tokenizer.decode(result[1:]).strip()
    if target == "zh-TW":
        converter = converters.get("s2twp")
        if converter is None:
            converter = converters["s2twp"] = OpenCC("s2twp")
        output = converter.convert(output)
    if cache is not None:
        cache[cache_key] = output
        cache.move_to_end(cache_key)
        if len(cache) > _MAX_CACHE_ENTRIES:
            cache.popitem(last=False)
    return output


def main() -> int:
    daemon = "--daemon" in sys.argv[2:]
    config_path = Path(sys.argv[1])
    model, tokenizer, vocabulary = load_resources(config_path)
    converters: dict = {}
    cache: OrderedDict = OrderedDict()

    if not daemon:
        request = json.load(sys.stdin)
        output = translate_request(request, model, tokenizer, vocabulary, converters, cache)
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
            output = translate_request(request, model, tokenizer, vocabulary, converters, cache)
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
