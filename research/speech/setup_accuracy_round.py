"""Download only pinned inference assets into C: storage; no package cache duplication."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
import time
from accuracy_round_protocol import DISTIL_REPO, DISTIL_REVISION, RUSSIAN_MODEL, RUSSIAN_MD5

FILES={'added_tokens.json','config.json','generation_config.json','merges.txt','model.safetensors',
       'normalizer.json','preprocessor_config.json','special_tokens_map.json','tokenizer.json','tokenizer_config.json','vocab.json'}


def digest(path,algorithm='sha256'):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,algorithm).hexdigest()


def download(url,path):
    part=path.with_suffix(path.suffix+'.part')
    for attempt in range(4):
        offset=part.stat().st_size if part.exists() else 0
        request=urllib.request.Request(url,headers={'Range':f'bytes={offset}-'} if offset else {})
        try:
            with urllib.request.urlopen(request,timeout=60) as source:
                resume=offset and source.status==206
                if resume and not source.headers.get('Content-Range','').startswith(f'bytes {offset}-'):
                    raise ValueError('Unexpected download range')
                count=offset if resume else 0
                print(f'Downloading {path.name} from {count/1024**2:.0f} MiB',flush=True)
                with part.open('ab' if resume else 'wb') as output:
                    while chunk:=source.read(1024**2):
                        output.write(chunk)
                        count+=len(chunk)
                        if count%(128*1024**2)==0:
                            print(f'{path.name}: {count/1024**2:.0f} MiB downloaded',flush=True)
            return part
        except (TimeoutError, OSError) as error:
            if attempt==3:
                raise
            print(f'Download interrupted ({error}); resuming attempt {attempt+2}/4',flush=True)
            time.sleep(2)


def setup(cache):
    folder=cache/'distil-large-v3'
    folder.mkdir(parents=True,exist_ok=True)
    provenance=folder/'provenance.json'
    if provenance.exists():
        saved=json.loads(provenance.read_text())
        if saved['repo']!=DISTIL_REPO or saved['revision']!=DISTIL_REVISION or set(saved['files'])!=FILES:
            raise ValueError('Unexpected English model provenance')
        for name,checksum in saved['files'].items():
            if digest(folder/name)!=checksum:
                raise ValueError(f'English model changed: {name}')
    else:
        with urllib.request.urlopen(f'https://huggingface.co/api/models/{DISTIL_REPO}/revision/{DISTIL_REVISION}?blobs=true',timeout=60) as response:
            metadata=json.load(response)
        hashes={}
        for item in metadata['siblings']:
            name=item['rfilename']
            if name not in FILES:
                continue
            path=folder/name
            part=download(f'https://huggingface.co/{DISTIL_REPO}/resolve/{DISTIL_REVISION}/{name}',path)
            checksum=digest(part)
            if part.stat().st_size!=item['size'] or (item.get('lfs') and checksum!=item['lfs']['sha256']):
                raise ValueError(f'English upstream checksum/size mismatch: {name}')
            part.replace(path)
            hashes[name]=checksum
        if set(hashes)!=FILES:
            raise ValueError('Incomplete English model files')
        provenance.write_text(json.dumps(dict(repo=DISTIL_REPO,revision=DISTIL_REVISION,files=hashes),indent=2),encoding='utf-8')
    russian=cache/f'{RUSSIAN_MODEL}.ckpt'
    if not russian.exists():
        part=download(f'https://cdn.chatwm.opensmodel.sberdevices.ru/GigaAM/{RUSSIAN_MODEL}.ckpt',russian)
        if digest(part,'md5')!=RUSSIAN_MD5:
            raise ValueError('Russian upstream checksum mismatch')
        part.replace(russian)
    if digest(russian,'md5')!=RUSSIAN_MD5:
        raise ValueError('Cached Russian checkpoint differs from upstream')
    (cache/'v3-provenance.json').write_text(json.dumps(dict(model=RUSSIAN_MODEL,md5=RUSSIAN_MD5,
        sha256=digest(russian)),indent=2),encoding='utf-8')
    print(f'New models verified in {cache}',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cache',type=Path)
    setup(parser.parse_args().cache)
