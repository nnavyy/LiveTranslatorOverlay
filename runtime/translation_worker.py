"""Local CTranslate2 translation worker supporting one-shot and persistent --daemon modes."""
from collections import OrderedDict
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import ctranslate2
import sentencepiece as spm
from opencc import OpenCC

_CUDA_DLL_HANDLES = []
_MAX_CACHE_ENTRIES = 256

_SENTENCE_SPLIT_RE = re.compile(
    r"(?<!\bMr\.)(?<!\bMs\.)(?<!\bMrs\.)(?<!\bDr\.)(?<!\bSt\.)(?<!\bJr\.)(?<!\bSr\.)(?<!\bvs\.)(?<!\b[A-Z]\.)(?<=[.!?])\s+|(?<=[。！？])\s*"
)

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


def _effective_len(text: str) -> int:
    total = 0
    for ch in text:
        code = ord(ch)
        if (
            0x3040 <= code <= 0x30FF
            or 0x3400 <= code <= 0x9FFF
            or 0xF900 <= code <= 0xFAFF
            or 0xAC00 <= code <= 0xD7AF
        ):
            total += 3
        else:
            total += 1
    return total


def _join_fragments(left: str, right: str) -> str:
    if not left:
        return right
    if not right:
        return left
    sep = "" if left[-1] in "。！？" else " "
    return f"{left}{sep}{right}"


def _split_sentences(text: str) -> list[str]:
    stripped = text.strip()
    if not stripped:
        return []
    raw_parts = [p.strip() for p in _SENTENCE_SPLIT_RE.split(stripped) if p and p.strip()]
    if len(raw_parts) <= 1:
        return [stripped]

    merged: list[str] = []
    pending = ""
    for part in raw_parts:
        if pending:
            part = _join_fragments(pending, part)
            pending = ""
        if _effective_len(part) < 12:
            if merged:
                merged[-1] = _join_fragments(merged[-1], part)
            else:
                pending = part
        else:
            merged.append(part)

    if pending:
        if merged:
            merged[-1] = _join_fragments(merged[-1], pending)
        else:
            merged.append(pending)

    if len(merged) <= 1:
        return [stripped]
    return merged


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
    if not text.strip():
        return ""

    cache_key = (source, target, fast, text)
    if cache is not None:
        cached = cache.get(cache_key)
        if cached is None and fast:
            cached = cache.get((source, target, False, text))
        if cached is not None:
            return cached

    sentences = _split_sentences(text)
    batch_tokens = [
        [f"__{native_source}__", *tokenizer.encode(sent, out_type=str), "</s>"]
        for sent in sentences
    ]
    if any(len(toks) > 512 for toks in batch_tokens):
        raise ValueError("Text too long for local translation; split into shorter passages")

    prefix = f"__{native_target}__"
    beam_size = 1 if fast else 2
    max_tok_len = max(len(t) for t in batch_tokens)
    max_dec_len = min(256, max(48, int(max_tok_len * 2.5) + 16))
    batch_results = model.translate_batch(
        batch_tokens,
        target_prefix=[[prefix]] * len(batch_tokens),
        beam_size=beam_size,
        repetition_penalty=1.15,
        no_repeat_ngram_size=3,
        max_decoding_length=max_dec_len,
    )

    decoded_sentences = []
    for res in batch_results:
        hyp = res.hypotheses[0] if res.hypotheses else None
        if not hyp or hyp[0] != prefix or len(hyp) >= 256:
            raise ValueError("Local translation did not complete; use a shorter passage")
        decoded = tokenizer.decode(hyp[1:]).strip()
        if decoded:
            decoded_sentences.append(decoded)

    joiner = "" if target in ("ja", "zh", "zh-TW") else " "
    output = joiner.join(decoded_sentences)
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
