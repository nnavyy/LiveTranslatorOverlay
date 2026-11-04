"""Independent English/Russian candidates, fixed development selection, historical test."""
import argparse
from collections import defaultdict
import gc
import importlib.metadata
import json
from pathlib import Path
import time

from benchmark_specialist import read, write, digest, torch, sf, WhisperProcessor, WhisperForConditionalGeneration, PeftModel
from accuracy_round_protocol import select_language, targets, RUSSIAN_MODEL, RUSSIAN_MD5, DISTIL_REVISION
from empty_caption import allow_empty_caption
from metrics import summarize, validate_splits
from run_progress import RunProgress
from specialist_protocol import MANIFEST_SHA256, GIGAAM_COMMIT, apply_gate


def render(output, result):
    progress_path=output/'progress.jsonl'
    if progress_path.exists():
        samples=[json.loads(line) for line in progress_path.read_text().splitlines()]
        result['resources']={key:operation(r[key] for r in samples) for key,operation in (
            ('gpuPeakAllocatedMiB',max),('processRssMiB',max),('processPrivateMiB',max),('availableRamMiB',min))}
        result['completedStages']=[r for r in samples if r['completed']==r['total']]
    lines=['# Independent English / Russian accuracy experiment','',
        '**SMOKE CHECK ONLY**' if result['smoke'] else '**Historical benchmark; no weight training or app deployment.**','',
        'Expected direction: English-focused distillation/beam search and Russian v3 may help. No numerical gain is guaranteed.',
        'Milestones: English <=2.67%, Russian <=4.70%; their mean is 3.685%. Final mean <=3%; stretch goal both <=2%.','',
        '| Split | Language | Candidate | WER | Errors / words | Blank speech | False captions |',
        '|---|---|---|---:|---:|---:|---:|']
    for split, languages in result['scores'].items():
        for lang, systems in languages.items():
            for name, score in systems.items():
                s=score['speech'][lang]; n=score['nonSpeech']
                lines.append(f"| {split} | {lang} | {name} | {s['wer']:.2%} | {s['wordErrors']}/{s['referenceWords']} | {s['falseRejections']} | {n['emittedClips']}/{n['clips']} |")
    lines += ['', 'Selection: '+json.dumps(result.get('selection',{})),
        '', 'Target checks (full runs only): '+json.dumps(result.get('targetChecks',{})),
        '', 'Historical test guardrails: '+str(result.get('testGuardrailsPassed','not evaluated')),
        '', 'Next action: '+result['nextAction'],
        '', f"Elapsed: {result['elapsedSeconds']/60:.1f} minutes, excluding download/setup.",
        'Memory summary (MiB; sampled host peaks/minimum free RAM): '+json.dumps(result.get('resources',{})),
        'Current baseline predictions and Small beam-5 development predictions are reused from the hash-audited previous run. Their old inference times are not new timings.',
        'New decoding stage ETA, RAM/private commit/free RAM and GPU memory are in progress.jsonl. Models run sequentially.',
        'The original 70/15/15 split and scoring normalization are unchanged. Source language is supplied. The same frozen Silero gate applies to every candidate; rejected speech remains an error.',
        'Raw and gated predictions/scores are retained. English and Russian are selected separately on development data; ties retain the current system. Test results never change that selection.',
        'This repeatedly used historical test is not a fresh holdout. English originates from LibriSpeech train.clean.100; Distil lists 960 hours of LibriSpeech training. Corpus overlap is known and exact recording overlap has not been audited. This English result cannot establish unseen-data accuracy. Confirm gains on fresh speaker/session-disjoint audio.',
        'A passing research result still needs app runtime, language routing, latency and fresh-audio validation before deployment. Repeating the same command does not train weights.']
    write(output/'report.json',result)
    (output/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(f"Report: {output/'report.md'}",flush=True)


def main(args):
    started=time.monotonic()
    if not torch.cuda.is_available():
        raise RuntimeError('CUDA unavailable; use training-cuda-venv')
    torch.set_num_threads(4)
    torch.manual_seed(20261104)
    torch.use_deterministic_algorithms(True)
    direct=json.loads(importlib.metadata.distribution('gigaam').read_text('direct_url.json'))
    if direct.get('vcs_info',{}).get('commit_id')!=GIGAAM_COMMIT:
        raise ValueError('GigaAM revision differs from pinned source')
    manifest=args.root/'russian-focus-20261103/manifest.jsonl'
    if digest(manifest)!=MANIFEST_SHA256:
        raise ValueError('The fixed dataset changed')
    rows=[json.loads(s) for s in manifest.read_text(encoding='utf-8').splitlines()]
    validate_splits(rows)
    parent=args.root/'my-small-training-20261103-213758'
    accepted=read(parent/'selection.json')
    adapter=parent/f"adapter-{accepted['checkpoint']}"
    source=args.root/'specialist-benchmark-20261104-125809'
    audit=read(Path(__file__).parent/'results/2026-11-04-specialist-completed/summary.json')
    frozen={str(source/name):checksum for name,checksum in audit['sourceHashes'].items()}
    frozen.update(read(source/'configuration.json')['hashes'])
    english_dir=args.cache/'distil-large-v3'
    english_provenance=read(english_dir/'provenance.json')
    if english_provenance['revision']!=DISTIL_REVISION:
        raise ValueError('English model revision changed')
    frozen.update({str(english_dir/name):checksum for name,checksum in english_provenance['files'].items()})
    if digest(args.cache/f'{RUSSIAN_MODEL}.ckpt','md5')!=RUSSIAN_MD5:
        raise ValueError('Russian v3 checksum mismatch')
    frozen[str(args.cache/f'{RUSSIAN_MODEL}.ckpt')]=digest(args.cache/f'{RUSSIAN_MODEL}.ckpt')
    for path,checksum in frozen.items():
        if digest(Path(path))!=checksum:
            raise ValueError(f'Frozen input changed: {path}')
    args.output.mkdir(parents=True,exist_ok=False)
    config=dict(hashes=frozen,trainingPerformed=False,productionApproved=False,smoke=args.smoke,
        selection='Independent languages; no WER/blank/control regression; lowest eligible WER; current first on tie',
        targets=dict(english=.0267,russian=.047,mean=.03,stretchBoth=.02),
        versions={p:importlib.metadata.version(p) for p in ('torch','transformers','peft','gigaam')})
    write(args.output/'configuration.json',config)
    progress=RunProgress(args.output,started,'cuda')
    result=dict(smoke=args.smoke,scores={},rawScores={},targetChecks={},configuration=config)
    selected_rows={}; gates={}
    for split in ('dev','test'):
        subset=[r for r in rows if r['split']==split]
        if args.smoke:
            buckets=defaultdict(list)
            for row in sorted(subset,key=lambda r:r['id']):
                buckets[(row['language'],row['kind'],row['corpus'])].append(row)
            subset=[bucket[0] for bucket in buckets.values()]
        selected_rows[split]=subset
        ids={r['id'] for r in subset}
        decisions=[json.loads(s) for s in (parent/f'{split}-gate.jsonl').read_text(encoding='utf-8-sig').splitlines()]
        if len({r['id'] for r in decisions})!=len(decisions):
            raise ValueError('Duplicate gate decisions')
        gates[split]={r['id']:r for r in decisions if r['id'] in ids}
        apply_gate([dict(r,prediction='') for r in subset],gates[split])
        for row in subset:
            path=(manifest.parent/row['path']).resolve()
            if not path.is_relative_to(manifest.parent.resolve()) or digest(path)!=row['sha256']:
                raise ValueError('Audio changed')
        (args.output/split).mkdir()

    def cached(split,lang,beam=1):
        original=read(source/f'{split}/hybrid-beam{beam}-raw.json')
        by_id={r['id']:r for r in original}
        expected=[r for r in rows if r['split']==split]
        if len(by_id)!=len(original) or set(by_id)!={r['id'] for r in expected}:
            raise ValueError('Cached prediction coverage differs')
        for row in expected:
            if any(by_id[row['id']].get(key)!=value for key,value in row.items()):
                raise ValueError('Cached reference/audio metadata differs')
        return [by_id[r['id']] for r in selected_rows[split] if r['language']==lang]

    def save(split,lang,name,predictions):
        decisions={r['id']:gates[split][r['id']] for r in predictions}
        gated=apply_gate(predictions,decisions)
        write(args.output/split/f'{name}-raw.json',predictions)
        write(args.output/split/f'{name}-gated.json',gated)
        result['scores'].setdefault(split,{}).setdefault(lang,{})[name]=summarize(gated)
        result['rawScores'].setdefault(split,{}).setdefault(lang,{})[name]=summarize(predictions)
        return gated

    def decode(split,lang,names):
        subset=[r for r in selected_rows[split] if r['language']==lang]
        if lang=='ru':
            import gigaam
            model=gigaam.load_model(RUSSIAN_MODEL,device='cuda',download_root=str(args.cache),fp16_encoder=True,use_flash=False)
            def transcribe(row,beam):
                return model.transcribe(str(manifest.parent/row['path'])).text.strip()
        else:
            small=names[0]=='small-beam5'
            directory=args.root/'whisper-small-973afd2' if small else english_dir
            processor=WhisperProcessor.from_pretrained(directory,local_files_only=True)
            model=WhisperForConditionalGeneration.from_pretrained(directory,local_files_only=True,
                torch_dtype=torch.float32 if small else torch.float16,attn_implementation='sdpa',use_safetensors=True,low_cpu_mem_usage=True)
            model.generation_config.forced_decoder_ids=None
            allow_empty_caption(model)
            if small:
                model=PeftModel.from_pretrained(model,str(adapter),local_files_only=True)
            model=model.to('cuda').eval()
            def transcribe(row,beam):
                audio,rate=sf.read(manifest.parent/row['path'],dtype='float32')
                if rate!=16000 or audio.ndim!=1 or not 0<len(audio)<=20*rate:
                    raise ValueError('Expected mono 16 kHz audio <=20 seconds')
                features=processor.feature_extractor(audio,sampling_rate=rate,return_tensors='pt',return_attention_mask=True).to('cuda')
                features['input_features']=features['input_features'].to(model.dtype)
                with torch.inference_mode():
                    tokens=model.generate(**features,language='en',task='transcribe',do_sample=False,num_beams=beam,max_new_tokens=128,use_cache=True)
                return processor.batch_decode(tokens,skip_special_tokens=True)[0].strip()
        outputs={}
        for name in names:
            predictions=[]; stage=time.monotonic()
            for i,row in enumerate(subset,1):
                if time.monotonic()-started>args.limit_seconds:
                    raise TimeoutError('Run time limit reached')
                before=time.monotonic()
                prediction=transcribe(row,5 if name.endswith('beam5') else 1)
                torch.cuda.synchronize()
                predictions.append(dict(row,prediction=prediction,inferenceSeconds=time.monotonic()-before))
                if i%10==0 or i==len(subset):
                    write(args.output/split/f'{name}-partial.json',predictions)
                    progress.report(f'{split}/{name}',i,len(subset),time.monotonic()-stage)
            outputs[name]=save(split,lang,name,predictions)
        del transcribe,model
        if lang=='en':
            del processor
        gc.collect(); torch.cuda.empty_cache()
        return outputs

    dev={'en':{},'ru':{}}
    for lang in dev:
        dev[lang]['current']=save('dev',lang,f'current-{lang}',cached('dev',lang))
    dev['en']['small-beam5']=save('dev','en','small-beam5',cached('dev','en',5))
    dev['en'].update(decode('dev','en',['distil-beam1','distil-beam5']))
    dev['ru'].update(decode('dev','ru',['gigaam-v3']))
    choices={lang:select_language(lang,summarize(dev[lang]['current']),
        {name:summarize(pred) for name,pred in dev[lang].items()}) for lang in dev}
    result['selection']=choices
    write(args.output/'selection.json',dict(languages=choices,selectionData='dev-only',hashes=frozen,smoke=args.smoke))
    combined=sum((dev[lang][choices[lang]['selected']] for lang in dev),[])
    if not args.smoke:
        result['targetChecks']['dev']=targets(summarize(combined))
    result['nextAction']='No development gain: retain current system; audit residual errors and collect targeted new data.'
    result['elapsedSeconds']=time.monotonic()-started
    render(args.output,result)
    if any(c['improved'] for c in choices.values()) or args.smoke:
        for path,checksum in frozen.items():
            if digest(Path(path))!=checksum:
                raise ValueError('Frozen input changed before historical test')
        combined=[]; guardrails=[]
        smoke_outputs={}
        if args.smoke:
            # Exercise every model's test path, without using these scores to select.
            smoke_outputs.update(decode('test','en',['small-beam5']))
            smoke_outputs.update(decode('test','en',['distil-beam1','distil-beam5']))
            smoke_outputs.update(decode('test','ru',['gigaam-v3']))
        for lang in ('en','ru'):
            baseline=save('test',lang,f'current-{lang}',cached('test',lang))
            name=choices[lang]['selected']
            prediction=baseline if name=='current' else (smoke_outputs[name] if args.smoke else decode('test',lang,[name])[name])
            check=select_language(lang,summarize(baseline),{'current':summarize(baseline),'candidate':summarize(prediction)})
            guardrails.append(check['eligibility']['candidate'])
            combined+=prediction
        result['testGuardrailsPassed']=all(guardrails)
        if not args.smoke:
            result['targetChecks']['test']=targets(summarize(combined))
        result['nextAction']=('Confirm on fresh audio and actual app runtime before deployment; audit residual errors if targets are unmet.'
            if all(guardrails) else 'Retain current system; investigate development/test disagreement without tuning on test.')
    if args.smoke:
        result['nextAction']='Execution check only. Run the full command manually; tiny scores are not accuracy evidence.'
    result['elapsedSeconds']=time.monotonic()-started
    render(args.output,result)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('root','cache','output'):
        parser.add_argument(name,type=Path)
    parser.add_argument('--smoke',action='store_true')
    parser.add_argument('--limit-seconds',type=int,default=14400)
    args=parser.parse_args()
    if args.output.exists() or args.limit_seconds<1:
        parser.error('Choose a new output directory and a positive time limit')
    try:
        main(args)
    except (Exception,KeyboardInterrupt) as error:
        if args.output.exists():
            write(args.output/'failure.json',dict(type=type(error).__name__,message=str(error)))
        raise
