"""Validation-selected Russian specialist / English Whisper benchmark; no weight training."""
import argparse
from collections import defaultdict
import gc
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import time

os.environ['HF_HUB_OFFLINE']='1'
os.environ['TRANSFORMERS_OFFLINE']='1'
os.environ.setdefault('CUBLAS_WORKSPACE_CONFIG', ':4096:8')
import soundfile as sf
import torch
from peft import PeftModel
from transformers import WhisperForConditionalGeneration, WhisperProcessor

from empty_caption import allow_empty_caption
from metrics import summarize, validate_splits
from run_progress import RunProgress
from specialist_protocol import GIGAAM_COMMIT, MODEL, MODEL_MD5, MANIFEST_SHA256, apply_gate, choose, goals


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path, algorithm='sha256'):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,algorithm).hexdigest()


def write(path,value):
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')


def report(output,result):
    progress=[json.loads(line) for line in (output/'progress.jsonl').read_text().splitlines()]
    result['resources']=dict(
        peakGpuAllocatedMiB=max(r['gpuPeakAllocatedMiB'] for r in progress),
        peakGpuReservedMiB=max(r['gpuReservedMiB'] for r in progress),
        maxSampledProcessRssMiB=max(r['processRssMiB'] for r in progress),
        maxSampledProcessPrivateMiB=max(r['processPrivateMiB'] for r in progress),
        minSampledAvailableRamMiB=min(r['availableRamMiB'] for r in progress))
    result['completedStages']=[dict(stage=r['stage'],items=r['completed'],seconds=r['stageActiveSeconds'],
        secondsPerItem=r['secondsPerItem']) for r in progress if r['completed']==r['total']]
    latest_split='test' if 'test' in result['scores'] else 'dev'
    selected_name=f"hybrid-beam{result['selection']['englishBeams']}"
    latest=result['scores'][latest_split][selected_name]
    display_names={'accepted-small':'Previous Small (comparison baseline)',
                   'hybrid-beam1':'Russian specialist + English greedy',
                   'hybrid-beam5':'Russian specialist + English beam 5'}
    lines=['# Russian specialist / English decoding benchmark','',
        f"**Main result ({latest_split}): Russian {latest['speech']['ru']['wer']:.2%}, "
        f"English {latest['speech']['en']['wer']:.2%}, mean {goals(latest)['macroMeanWer']:.2%}.**",
        'The Previous Small row below is the old comparison model, not the specialist result.', '',
        '**No training was performed.** This compares pretrained recognition and decoding choices.',
        'Mean WER is the equal-weight mean of Russian and English WER, not pooled word errors.',
        '', '## Expectations registered before decoding','',
        'Expected direction: a Russian specialist may reduce Russian errors; English may stay near its existing score unless beam search helps. Neither improvement is guaranteed.',
        'Milestone: Russian <=7%, English <=3%. At those limits, mean WER is 5%. Final goal: mean WER <=3%, in addition to the language limits.',
        'Published results are supporting evidence, not a numerical forecast on this dataset.',
        '', '| Split | System | RU WER (errors/words) | EN WER (errors/words) | Mean WER | Blank speech | False captions |',
        '|---|---|---:|---:|---:|---:|---:|']
    for split,systems in result['scores'].items():
        for name,score in systems.items():
            ru,en=score['speech']['ru'],score['speech']['en']
            controls=score['nonSpeech']
            lines.append(f"| {split} | {display_names[name]} | {ru['wer']:.2%} ({ru['wordErrors']}/{ru['referenceWords']}) | "
                         f"{en['wer']:.2%} ({en['wordErrors']}/{en['referenceWords']}) | {(ru['wer']+en['wer'])/2:.2%} | "
                         f"{ru['falseRejections']+en['falseRejections']} | {controls['emittedClips']}/{controls['clips']} |")
    lines += ['', '## Before the fixed speech detector', '',
              '| Split | System | Raw RU WER | Raw EN WER | Raw false captions |',
              '|---|---|---:|---:|---:|']
    for split,systems in result['rawScores'].items():
        for name,score in systems.items():
            controls=score['nonSpeech']
            lines.append(f"| {split} | {display_names[name]} | {score['speech']['ru']['wer']:.2%} | "
                         f"{score['speech']['en']['wer']:.2%} | {controls['emittedClips']}/{controls['clips']} |")
    choice=result['selection']
    lines += ['',f"Development criteria passed: **{choice['developmentEligible']}**. English decoder: **{choice['englishBeams']} beam(s)**.",
              'Selection, pinned versions and model/data hashes are saved in selection.json and configuration.json.']
    if 'historicalTestGuardrailsPassed' in result:
        lines += [f"Historical test guardrails passed: **{result['historicalTestGuardrailsPassed']}**. This does not change development selection."]
    if 'test' not in result['scores']:
        lines += ['Historical test has not been completed. Do not compare validation numbers with earlier test numbers.']
    for split,decision in result.get('targetChecks',{}).items():
        lines += ['',f"{split} goals — Russian <=7%: **{decision['russianAtMost7']}**; English <=3%: **{decision['englishAtMost3']}**; "
                  f"mean <=3%: **{decision['macroMeanAtMost3']}**."]
    if result['smoke']:
        action='Run the full benchmark manually. This tiny execution check cannot establish any accuracy target.'
    elif not choice['developmentEligible']:
        action='Keep the accepted Small system. Review errors and data coverage; do not repeat the identical training recipe.'
    elif 'test' not in result['scores']:
        action='Development improved. Await the frozen historical test before drawing a conclusion.'
    elif not result['historicalTestGuardrailsPassed']:
        action='Keep the accepted Small system. Investigate the validation/test disagreement without tuning on the test set.'
    elif all(result['targetChecks']['test'][key] for key in ('russianAtMost7','englishAtMost3','macroMeanAtMost3')):
        action='The historical targets passed. Confirm on fresh speaker/session-disjoint audio and the actual app runtime before deployment.'
    else:
        action='The research candidate improved, but at least one target is unmet. Audit the failing language(s), then plan targeted data or a stronger model before another training run.'
    result['recommendedNextAction']=action
    lines += ['', '**Recommended next action:** '+action]
    lines += ['', '## What to do with this result','',
        '- If validation fails, retain the accepted Small system. Inspect errors and data coverage before another training run.',
        '- If validation improves but historical test regresses, retain the accepted system and investigate the disagreement; do not tune against that test.',
        '- If both improve but targets are missed, keep the improvement as a research candidate, audit residual errors and plan targeted new training data or acoustic adaptation.',
        '- If language targets pass but mean WER exceeds 3%, the final goal is still unmet.',
        '- If all targets pass, confirm on fresh speaker/session-disjoint audio and the app runtime before deployment.',
        '', f"Elapsed benchmark time: {result['elapsedSeconds']/60:.1f} minutes; excludes dependency installation/model download.",
        f"Peak PyTorch GPU allocation: {result['resources']['peakGpuAllocatedMiB']:.0f} MiB; "
        f"max sampled process RAM: {result['resources']['maxSampledProcessRssMiB']:.0f} MiB; "
        f"max sampled private commit: {result['resources']['maxSampledProcessPrivateMiB']:.0f} MiB; "
        f"minimum available system RAM: {result['resources']['minSampledAvailableRamMiB']:.0f} MiB.",
        'GPU counters exclude other applications and some driver allocations; host-memory peaks between samples may be missed.',
        'Raw and fixed-Silero-gated predictions/scores are saved separately. The table uses the same gate for every system; rejected speech remains in WER.',
        'Progress logs include stage ETA, process RAM and GPU allocations. Timing includes decoding audio read/preprocessing, but excludes the cached VAD computation.',
        'Source language is supplied, not detected. No automatic language-switching claim is made.',
        'Historical test audio has been used in prior reports; external pretrained-model training overlap is unknown. Fresh validation is required for generalization claims.',
        'The runtime and model format differ from the current desktop app. No model is installed into the app by this script.']
    if result['smoke']:
        lines.insert(2,'**SMOKE CHECK ONLY: selected few clips; these scores are not full-dataset accuracy results.**')
    write(output/'report.json',result)
    (output/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print('\n'.join(lines),flush=True)


def main(args):
    started=time.monotonic()
    if not torch.cuda.is_available():
        raise RuntimeError('CUDA unavailable: use the prepared training-cuda-venv')
    torch.set_num_threads(4)
    torch.manual_seed(20261104)
    torch.use_deterministic_algorithms(True)
    direct=json.loads(importlib.metadata.distribution('gigaam').read_text('direct_url.json'))
    if direct.get('vcs_info',{}).get('commit_id') != GIGAAM_COMMIT:
        raise ValueError('GigaAM package is not the pinned source revision')
    manifest=args.root/'russian-focus-20261103/manifest.jsonl'
    if digest(manifest)!=MANIFEST_SHA256:
        raise ValueError('Expected unchanged 70/15/15 manifest')
    rows=[json.loads(s) for s in manifest.read_text(encoding='utf-8').splitlines()]
    validate_splits(rows)
    parent=args.root/'my-small-training-20261103-213758'
    parent_selection=read(parent/'selection.json')
    if not parent_selection['developmentEligible'] or parent_selection['manifestSha256']!=MANIFEST_SHA256:
        raise ValueError('Expected accepted parent on this manifest')
    adapter=parent/f"adapter-{parent_selection['checkpoint']}"
    if digest(adapter/'adapter_model.safetensors')!=parent_selection['adapterSha256']:
        raise ValueError('Parent adapter changed')
    model_dir=args.root/'whisper-small-973afd2'
    provenance=read(model_dir/'provenance.json')
    if provenance['repo']!='openai/whisper-small' or provenance['revision']!='973afd24965f72e36ca33b3055d56a652f456b4d':
        raise ValueError('Unexpected Whisper base revision')
    for filename,expected in provenance['files'].items():
        path=(model_dir/filename).resolve()
        if not path.is_relative_to(model_dir.resolve()) or digest(path)!=expected:
            raise ValueError('Whisper file/checksum mismatch')
    checkpoint=args.cache/f'{MODEL}.ckpt'
    if digest(checkpoint,'md5')!=MODEL_MD5:
        raise ValueError('Russian checkpoint differs from upstream checksum')
    frozen={str(checkpoint):digest(checkpoint),str(adapter/'adapter_model.safetensors'):parent_selection['adapterSha256'],
            str(manifest):MANIFEST_SHA256}
    frozen.update({str(model_dir/name):value for name,value in provenance['files'].items()})
    frozen.update({str(parent/f'{split}-gate.jsonl'):digest(parent/f'{split}-gate.jsonl') for split in ('dev','test')})
    args.output.mkdir(parents=True,exist_ok=False)
    config=dict(gigaamModel=MODEL,gigaamCommit=GIGAAM_COMMIT,hashes=frozen,
        whisperBase=provenance,englishBeamsToCompare=[1,5],knownSourceLanguage=True,
        whisperPrecision='float32 SDPA',gigaamPrecision='upstream fp16 encoder/autocast, flash_attn disabled',
        expectedDirection='Russian reduction is a hypothesis; English may remain near current WER. No promised percentage.',
        targetThresholds=dict(russianWer=.07,englishWer=.03,macroMeanWer=.03),
        selectionRule='Strict RU improvement, no EN/control/blank regression; lowest eligible mean; prefer beam 1 on tie',
        versions={p:importlib.metadata.version(p) for p in ('torch','torchaudio','transformers','peft','gigaam','soundfile')},
        smoke=args.smoke,trainingPerformed=False,productionApproved=False)
    write(args.output/'configuration.json',config)
    progress=RunProgress(args.output,started,'cuda')
    result=dict(configuration=config,scores={},rawScores={},corpusScores={},selection={},targetChecks={},smoke=args.smoke,elapsedSeconds=0)

    def evaluate(split, beams):
        selected=[r for r in rows if r['split']==split]
        if args.smoke:
            buckets=defaultdict(list)
            for row in sorted(selected,key=lambda r:r['id']):
                buckets[(row['language'],row['kind'],row['corpus'])].append(row)
            selected=[r for bucket in buckets.values() for r in bucket[:1]]
        decisions_list=[json.loads(s) for s in (parent/f'{split}-gate.jsonl').read_text(encoding='utf-8-sig').splitlines()]
        if len({r['id'] for r in decisions_list})!=len(decisions_list):
            raise ValueError('Duplicate gate recording')
        decisions={r['id']:r for r in decisions_list if r['id'] in {s['id'] for s in selected}}
        apply_gate([dict(r,prediction='') for r in selected],decisions)
        for row in selected:
            path=(manifest.parent/row['path']).resolve()
            if not path.is_relative_to(manifest.parent.resolve()) or digest(path)!=row['sha256']:
                raise ValueError('Audio changed')
        folder=args.output/split
        folder.mkdir(exist_ok=False)

        def decode(name,subset,transcribe):
            predictions=[]
            stage_started=time.monotonic()
            for i,row in enumerate(subset,1):
                if time.monotonic()-started>args.limit_seconds:
                    raise TimeoutError('Benchmark time limit reached')
                torch.cuda.synchronize()
                before=time.monotonic()
                prediction=transcribe(row)
                torch.cuda.synchronize()
                predictions.append(dict(row,prediction=prediction,inferenceSeconds=time.monotonic()-before))
                if i%10==0 or i==len(subset):
                    write(folder/f'{name}-raw.json',predictions)
                    progress.report(f'{split}/{name}',i,len(subset),time.monotonic()-stage_started)
            return predictions

        processor=WhisperProcessor.from_pretrained(model_dir,local_files_only=True)
        base=WhisperForConditionalGeneration.from_pretrained(model_dir,local_files_only=True,
            use_safetensors=True,attn_implementation='sdpa')
        base.generation_config.forced_decoder_ids=None
        allow_empty_caption(base)
        whisper=PeftModel.from_pretrained(base,str(adapter),local_files_only=True).to('cuda').eval()

        def transcribe_whisper(row,beam):
            audio,rate=sf.read(manifest.parent/row['path'],dtype='float32')
            if rate!=16000 or audio.ndim!=1 or not 0<len(audio)<=20*rate:
                raise ValueError('Expected mono 16 kHz <=20-second audio')
            features=processor.feature_extractor(audio,sampling_rate=rate,return_tensors='pt',return_attention_mask=True).to('cuda')
            with torch.inference_mode():
                tokens=whisper.generate(**features,language=row['language'],task='transcribe',do_sample=False,
                    num_beams=beam,max_new_tokens=128,use_cache=True)
            return processor.batch_decode(tokens,skip_special_tokens=True)[0].strip()

        baseline=decode('small-greedy',selected,lambda row:transcribe_whisper(row,1))
        english={1:[r for r in baseline if r['language']=='en']}
        if 5 in beams:
            english[5]=decode('english-beam5',[r for r in selected if r['language']=='en'],lambda row:transcribe_whisper(row,5))
        del whisper,base,processor
        gc.collect()
        torch.cuda.empty_cache()
        import gigaam
        russian=gigaam.load_model(MODEL,device='cuda',download_root=str(args.cache),fp16_encoder=True,use_flash=False)
        ru=decode('gigaam',[r for r in selected if r['language']=='ru'],
            lambda row:russian.transcribe(str(manifest.parent/row['path'])).text.strip())
        del russian
        gc.collect()
        torch.cuda.empty_cache()
        systems={'accepted-small':baseline}
        for beam in beams:
            systems[f'hybrid-beam{beam}']=ru+english[beam]
        raw_scores,gated_scores={},{}
        result['corpusScores'][split]={}
        for name,predictions in systems.items():
            gated=apply_gate(predictions,decisions)
            raw_scores[name]=summarize(predictions)
            gated_scores[name]=summarize(gated)
            result['corpusScores'][split][name]={corpus:summarize([r for r in gated if r['kind']=='speech' and r['corpus']==corpus])
                for corpus in sorted({r['corpus'] for r in gated if r['kind']=='speech'})}
            write(folder/f'{name}-gated.json',gated)
            write(folder/f'{name}-raw.json',predictions)
        result['rawScores'][split]=raw_scores
        result['scores'][split]=gated_scores
        result['elapsedSeconds']=time.monotonic()-started
        return gated_scores

    dev=evaluate('dev',[1,5])
    selection=choose(dev['accepted-small'],{str(b):dev[f'hybrid-beam{b}'] for b in (1,5)})
    selection.update(modelHashes=frozen,selectionData='dev-only',smoke=args.smoke)
    write(args.output/'selection.json',selection)
    result['selection']=selection
    if not args.smoke:
        result['targetChecks']['dev']=goals(dev[f"hybrid-beam{selection['englishBeams']}"])
    report(args.output,result)
    if selection['developmentEligible']:
        for path,expected in frozen.items():
            if digest(Path(path))!=expected:
                raise ValueError('Frozen model or data changed before test')
        beam=selection['englishBeams']
        test=evaluate('test',[beam])
        if not args.smoke:
            result['targetChecks']['test']=goals(test[f'hybrid-beam{beam}'])
        result['historicalTestGuardrailsPassed']=choose(test['accepted-small'],{str(beam):test[f'hybrid-beam{beam}']})['developmentEligible']
        # This does not alter development selection.
        report(args.output,result)
    print(f'Read {args.output / "report.md"}',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root',type=Path)
    parser.add_argument('cache',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--smoke',action='store_true')
    parser.add_argument('--limit-seconds',type=int,default=14400)
    args=parser.parse_args()
    if args.output.exists():
        parser.error('Output already exists; choose a new directory to preserve previous results')
    if args.limit_seconds<1:
        parser.error('Time limit must be positive')
    try:
        main(args)
    except (Exception, KeyboardInterrupt) as error:
        if args.output.exists():
            write(args.output/'failure.json',dict(errorType=type(error).__name__,message=str(error)))
        raise
