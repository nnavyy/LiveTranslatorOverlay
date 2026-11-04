"""Local JSON-lines speech worker. Audio and recognition stay on this machine."""
import contextlib
import gc
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import sys
import tempfile

os.environ['HF_HUB_OFFLINE']='1'
os.environ['TRANSFORMERS_OFFLINE']='1'
os.environ.setdefault('CUBLAS_WORKSPACE_CONFIG', ':4096:8')
protocol=sys.stdout
sys.stdout=sys.stderr
import numpy as np
import soundfile as sf
import torch
from transformers import WhisperProcessor, WhisperForConditionalGeneration


def checksum(path,algorithm='sha256'):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream,algorithm).hexdigest()


def run(config,language):
    if language not in ('en','ru'):
        raise ValueError('Choose English or Russian as source language; Auto is unsupported')
    if not torch.cuda.is_available():
        raise RuntimeError('Local speech requires the prepared NVIDIA CUDA environment')
    torch.set_num_threads(4)
    torch.manual_seed(20261104)
    torch.use_deterministic_algorithms(True)
    if language=='en':
        folder=Path(config['englishModel'])
        provenance=json.loads((folder/'provenance.json').read_text())
        if provenance['revision']!='8031d2e6ce6631b7fc45629dddfc00271116d981':
            raise ValueError('Unexpected English revision')
        for name,digest in provenance['files'].items():
            if not (folder/name).resolve().is_relative_to(folder.resolve()) or checksum(folder/name)!=digest:
                raise ValueError('English model checksum mismatch')
        processor=WhisperProcessor.from_pretrained(folder,local_files_only=True)
        model=WhisperForConditionalGeneration.from_pretrained(folder,local_files_only=True,
            torch_dtype=torch.float16,attn_implementation='sdpa',use_safetensors=True,low_cpu_mem_usage=True).to('cuda').eval()
        model.generation_config.forced_decoder_ids=None
        suppressed=model.generation_config.begin_suppress_tokens
        if suppressed:
            model.generation_config.begin_suppress_tokens=[t for t in suppressed if t!=model.config.eos_token_id]
        def transcribe(audio):
            features=processor.feature_extractor(audio,sampling_rate=16000,return_tensors='pt',return_attention_mask=True).to('cuda')
            features['input_features']=features['input_features'].to(torch.float16)
            with torch.inference_mode():
                tokens=model.generate(**features,language='en',task='transcribe',do_sample=False,num_beams=5,max_new_tokens=128,use_cache=True)
            return processor.batch_decode(tokens,skip_special_tokens=True)[0].strip()
    else:
        direct=json.loads(importlib.metadata.distribution('gigaam').read_text('direct_url.json'))
        if direct.get('vcs_info',{}).get('commit_id')!='7447938d791c4f3e643386ee22c33777004293a5':
            raise ValueError('Unexpected GigaAM revision')
        cache=Path(config['russianCache'])
        if checksum(cache/'v2_rnnt.ckpt','md5')!='547460139acfebd842323f59ed54ab54':
            raise ValueError('Russian model checksum mismatch')
        import gigaam
        model=gigaam.load_model('v2_rnnt',device='cuda',download_root=str(cache),fp16_encoder=True,use_flash=False)
        def transcribe(audio):
            with tempfile.TemporaryDirectory(prefix='lto-speech-') as temporary:
                path=Path(temporary)/'clip.wav'
                sf.write(path,audio,16000,subtype='FLOAT')
                return model.transcribe(str(path)).text.strip()
    print(json.dumps(dict(ready=True,language=language)),file=protocol,flush=True)
    for line in sys.stdin:
        try:
            request=json.loads(line)
            audio=np.asarray(request['samples'],dtype=np.float32)
            if audio.ndim!=1 or not 0<len(audio)<=320000 or not np.isfinite(audio).all():
                raise ValueError('Expected finite mono 16 kHz audio, at most 20 seconds')
            text=transcribe(audio)
            response=dict(text=text)
        except Exception as error:
            response=dict(error=str(error))
        print(json.dumps(response,ensure_ascii=False),file=protocol,flush=True)


if __name__=='__main__':
    try:
        run(json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig')),sys.argv[2])
    except Exception as error:
        print(json.dumps(dict(error=str(error))),file=protocol,flush=True)
        raise
