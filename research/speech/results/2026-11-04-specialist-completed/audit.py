"""Audit saved specialist results without training, decoding, or changing the frozen run."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from metrics import summarize, validate_splits, words, distance
from specialist_protocol import apply_gate, choose, goals
from expanded_utils import paired_interval


def main(root):
    def read(path):
        return json.loads(path.read_text(encoding='utf-8-sig'))
    def sha(path):
        with path.open('rb') as stream:
            return hashlib.file_digest(stream,'sha256').hexdigest()
    run=root/'specialist-benchmark-20261104-125809'
    result=read(run/'report.json')
    selection=read(run/'selection.json')
    assert result['selection']==selection and not result['smoke']
    source_hashes={name:sha(run/name) for name in ('report.json','selection.json','configuration.json','progress.jsonl')}
    for path,expected in selection['modelHashes'].items():
        assert sha(Path(path))==expected, path
    manifest=root/'russian-focus-20261103/manifest.jsonl'
    rows=[json.loads(line) for line in manifest.read_text(encoding='utf-8').splitlines()]
    validate_splits(rows)
    verified={}
    for split,systems in result['scores'].items():
        expected={r['id']:r for r in rows if r['split']==split}
        gate_rows=[json.loads(line) for line in (root/f'my-small-training-20261103-213758/{split}-gate.jsonl').read_text(encoding='utf-8-sig').splitlines()]
        assert len(gate_rows)==len({r['id'] for r in gate_rows})
        gate={r['id']:r for r in gate_rows}
        verified[split]={}
        for name,score in systems.items():
            raw_path=run/split/f'{name}-raw.json'
            gated_path=run/split/f'{name}-gated.json'
            raw,gated=read(raw_path),read(gated_path)
            for predictions in (raw,gated):
                assert len(predictions)==len(expected)==len({r['id'] for r in predictions})
                assert {r['id']:{k:v for k,v in r.items() if k not in ('prediction','inferenceSeconds')} for r in predictions}==expected
            assert apply_gate(raw,gate)==gated
            assert summarize(raw)==result['rawScores'][split][name]
            assert summarize(gated)==score
            for corpus,saved in result['corpusScores'][split][name].items():
                assert summarize([r for r in gated if r['kind']=='speech' and r['corpus']==corpus])==saved
            verified[split][name]=gated
            source_hashes[f'{split}/{name}-raw.json']=sha(raw_path)
            source_hashes[f'{split}/{name}-gated.json']=sha(gated_path)
    dev=result['scores']['dev']
    corrected=choose(dev['accepted-small'],{str(b):dev[f'hybrid-beam{b}'] for b in (1,5)})
    assert corrected['englishBeams']==5 and selection['englishBeams']==1
    assert corrected['eligibility']==selection['eligibility']
    test=result['scores']['test']
    assert choose(test['accepted-small'],{'1':test['hybrid-beam1']})['developmentEligible']
    assert goals(test['hybrid-beam1'])==result['targetChecks']['test']
    baseline={r['id']:r for r in verified['test']['accepted-small']}
    uncertainty={}
    for language in ('ru','en'):
        pairs=[]
        for row in verified['test']['hybrid-beam1']:
            if row['kind']!='speech' or row['language']!=language:
                continue
            ref=words(row['reference'])
            pairs.append(dict(group=row['group'],referenceWords=len(ref),
                baselineErrors=distance(ref,words(baseline[row['id']]['prediction'])),
                candidateErrors=distance(ref,words(row['prediction']))))
        uncertainty[language]=paired_interval(pairs)
    summary=dict(run=run.name,scores=result['scores'],rawScores=result['rawScores'],corpusScores=result['corpusScores'],
        targetChecks=result['targetChecks'],resources=result['resources'],elapsedSeconds=result['elapsedSeconds'],
        completedStages=result['completedStages'],uncertainty=uncertainty,
        originalEnglishBeams=selection['englishBeams'],correctedValidationChoice=corrected,
        originalTestStillValidFor='GigaAM Russian + Small English greedy with fixed Silero',
        correctedEnglishTestMeasured=False,trainingPerformed=False,productionApproved=False,
        sourceHashes=source_hashes)
    (Path(__file__).parent/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    assert all(sha(run/name)==source_hashes[name] for name in ('report.json','selection.json','configuration.json','progress.jsonl'))
    print(json.dumps(dict(test=test,uncertainty=uncertainty,correctedChoice=corrected,elapsedSeconds=result['elapsedSeconds']),indent=2))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('research_root',type=Path)
    main(parser.parse_args().research_root)
