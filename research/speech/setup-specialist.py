"""Download the published Russian checkpoint into the specified cache; no training."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request

from specialist_protocol import GIGAAM_COMMIT, MODEL, MODEL_MD5


def setup(cache):
    cache.mkdir(parents=True,exist_ok=True)
    target=cache/f'{MODEL}.ckpt'
    url=f'https://cdn.chatwm.opensmodel.sberdevices.ru/GigaAM/{MODEL}.ckpt'
    if not target.exists():
        partial=target.with_suffix('.part')
        with urllib.request.urlopen(url,timeout=60) as source, partial.open('wb') as output:
            received=0
            while chunk:=source.read(4*1024**2):
                output.write(chunk)
                received+=len(chunk)
                if received % (64*1024**2)==0:
                    print(f'Downloaded {received/1024**2:.0f} MiB',flush=True)
        with partial.open('rb') as stream:
            if hashlib.file_digest(stream,'md5').hexdigest()!=MODEL_MD5:
                raise ValueError('Downloaded checkpoint differs from pinned upstream checksum')
        partial.replace(target)
    with target.open('rb') as stream:
        if hashlib.file_digest(stream,'md5').hexdigest()!=MODEL_MD5:
            raise ValueError('Cached checkpoint checksum mismatch; preserve it for inspection and use a new cache directory')
    with target.open('rb') as stream:
        digest=hashlib.file_digest(stream,'sha256').hexdigest()
    provenance=dict(model=MODEL,url=url,upstreamCommit=GIGAAM_COMMIT,md5=MODEL_MD5,sha256=digest)
    (cache/'provenance.json').write_text(json.dumps(provenance,indent=2),encoding='utf-8')
    print(f'Verified {target}',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cache',type=Path)
    setup(parser.parse_args().cache)
