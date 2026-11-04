"""One local CPU translation; no networking or implicit model download."""
import hashlib
import json
from pathlib import Path
import sys
import ctranslate2
import sentencepiece as spm
from opencc import OpenCC

try:
    config=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    folder=Path(config['translationModel'])
    hashes={'config.json':'8f6496adfc930cbfecbe8281112197705c488fab47d34b4829b06d7f478909af',
        'model.bin':'137949450f9cf691f3c92084f14123e82d05ecb23ac3cd688308ce1a95214572',
        'sentencepiece.bpe.model':'d8f7c76ed2a5e0822be39f0a4f95a55eb19c78f4593ce609e2edbc2aea4d380a',
        'shared_vocabulary.json':'7eb5d0ff184c6095c7c10f9911c0aea492250abd12854f9c3d787c64b1c6397e'}
    for name,digest in hashes.items():
        with (folder/name).open('rb') as stream:
            if hashlib.file_digest(stream,'sha256').hexdigest()!=digest:
                raise ValueError('Translation model checksum mismatch')
    request=json.load(sys.stdin)
    text=request['text']; source=request['source']; target=request['target']
    if not isinstance(text,str) or len(text)>4000:
        raise ValueError('Local translation accepts at most 4000 characters')
    native_source='zh' if source=='zh-TW' else source
    native_target='zh' if target=='zh-TW' else target
    vocabulary=set(json.loads((folder/'shared_vocabulary.json').read_text(encoding='utf-8')))
    if f'__{native_source}__' not in vocabulary or f'__{native_target}__' not in vocabulary:
        raise ValueError('Choose explicit source and target languages supported by the local model; Auto is unsupported')
    tokenizer=spm.SentencePieceProcessor(model_file=str(folder/'sentencepiece.bpe.model'))
    tokens=[f'__{native_source}__',*tokenizer.encode(text,out_type=str),'</s>']
    if len(tokens)>512:
        raise ValueError('Text too long for local translation; split into shorter passages')
    model=ctranslate2.Translator(str(folder),device='cpu',compute_type='int8',inter_threads=1,intra_threads=4)
    prefix=f'__{native_target}__'
    result=model.translate_batch([tokens],target_prefix=[[prefix]],beam_size=2,max_decoding_length=256)[0].hypotheses[0]
    if not result or result[0]!=prefix or len(result)>=256:
        raise ValueError('Local translation did not complete; use a shorter passage')
    output=tokenizer.decode(result[1:]).strip()
    if target=='zh-TW':
        output=OpenCC('s2twp').convert(output)
    print(json.dumps(dict(text=output),ensure_ascii=False))
except Exception as error:
    print(json.dumps(dict(error=str(error))))
    sys.exit(1)
