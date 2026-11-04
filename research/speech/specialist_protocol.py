"""Fixed goals and selection for the language-specific recognizer experiment."""
from expanded_utils import choose_refinement

GIGAAM_COMMIT = "7447938d791c4f3e643386ee22c33777004293a5"
MODEL = "v2_rnnt"
MODEL_MD5 = "547460139acfebd842323f59ed54ab54"
MANIFEST_SHA256 = "ab859fe7348051378d10416fe5ac704eb9b5d0729b3aed709c0e56d567cdeb13"
VAD_SHA256 = "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987"


def goals(score):
    ru, en = (score["speech"][lang]["wer"] for lang in ("ru", "en"))
    return dict(russianAtMost7=ru <= .07, englishAtMost3=en <= .03,
                macroMeanWer=(ru+en)/2, macroMeanAtMost3=(ru+en)/2 <= .03)


def choose(baseline, candidates):
    result = choose_refinement(baseline, candidates)
    pool = [name for name, eligible in result['eligibility'].items() if eligible] or list(candidates)
    selected = min(pool, key=lambda name: (
        sum(candidates[name]['speech'][lang]['wer'] for lang in ('ru','en'))/2, int(name)))
    result.pop('checkpoint')
    result['developmentEligible'] = result['eligibility'][selected]
    result['englishBeams'] = int(selected)
    result['selectionPolicy'] = 'eligible-lowest-macro-mean-v2'
    return result


def apply_gate(rows, decisions):
    if len(decisions) != len(rows) or set(decisions) != {r["id"] for r in rows}:
        raise ValueError("Gate and predictions must cover identical unique recordings")
    result = []
    for row in rows:
        gate = decisions[row["id"]]
        if (gate["audioSha256"] != row["sha256"] or gate["split"] != row["split"]
                or type(gate["neuralGate"]) is not bool or gate["threshold"] != .5
                or gate["vadSha256"] != VAD_SHA256):
            raise ValueError("Gate provenance/settings mismatch")
        result.append(dict(row, prediction=row["prediction"] if gate["neuralGate"] else ""))
    return result
