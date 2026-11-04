"""Selection and uncertainty rules for the expanded local speech experiment."""
import random


def assert_disjoint(rows, previous):
    for key in ("id", "group", "sha256"):
        if {r[key] for r in rows} & {r[key] for r in previous}:
            raise ValueError(f"New experiment overlaps previous pilot by {key}")


def choose_candidate(baseline, candidates, parent=None):
    def macro(score):
        return sum(score["speech"][lang]["wer"] for lang in ("en", "ru")) / 2

    eligible = {}
    for name, score in candidates.items():
        eligible[name] = (macro(score) < macro(baseline)
            and all(score["speech"][l]["wer"] <= baseline["speech"][l]["wer"] + .01
                    and score["speech"][l]["falseRejections"] <= baseline["speech"][l]["falseRejections"]
                    for l in ("en", "ru"))
            and score["nonSpeech"]["emittedClips"] <= baseline["nonSpeech"]["emittedClips"])
    if parent is not None:
        against_parent = choose_candidate(parent, candidates)["eligibility"]
        eligible = {name: ok and against_parent[name] for name, ok in eligible.items()}
    pool = [name for name in candidates if eligible[name]] or list(candidates)
    selected = min(pool, key=lambda name: (macro(candidates[name]), int(name)))
    return dict(checkpoint=selected, developmentEligible=eligible[selected],
                eligibility=eligible, selectionData="dev-only", productionApproved=False)


def paired_interval(pairs, repeats=10000, seed=20261103):
    """Cluster bootstrap by parent sentence, retaining paired outputs."""
    grouped = {}
    for row in pairs:
        group = grouped.setdefault(row["group"], [0, 0])
        group[0] += row["candidateErrors"] - row["baselineErrors"]
        group[1] += row["referenceWords"]
    groups = list(grouped.values())
    if not groups or any(g[1] <= 0 for g in groups):
        raise ValueError("Need nonempty reference groups")
    rng = random.Random(seed)
    values = []
    for _ in range(repeats):
        sample = rng.choices(groups, k=len(groups))
        values.append(sum(g[0] for g in sample) / sum(g[1] for g in sample))
    values.sort()
    return dict(difference=sum(g[0] for g in groups) / sum(g[1] for g in groups),
                lower=values[int(.025 * (repeats - 1))], upper=values[int(.975 * (repeats - 1))],
                independentGroups=len(groups), repeats=repeats, seed=seed,
                method="paired parent-sentence cluster percentile bootstrap, 95%")


def choose_refinement(parent, candidates):
    """Keep a continuation only if Russian improves without other regressions."""
    result = choose_candidate(parent, candidates)
    for name, score in candidates.items():
        result["eligibility"][name] = (result["eligibility"][name]
            and score["speech"]["ru"]["wer"] < parent["speech"]["ru"]["wer"]
            and score["speech"]["en"]["wer"] <= parent["speech"]["en"]["wer"])
    pool = [name for name, ok in result["eligibility"].items() if ok] or list(candidates)
    result["checkpoint"] = min(pool, key=lambda name: (candidates[name]["speech"]["ru"]["wer"], int(name)))
    result["developmentEligible"] = result["eligibility"][result["checkpoint"]]
    return result
