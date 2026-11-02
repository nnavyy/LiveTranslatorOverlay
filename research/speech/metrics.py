"""Transparent pilot metrics; no accuracy percentage combining unrelated tasks."""
import unicodedata


def words(text):
    normalized = unicodedata.normalize("NFKC", text).lower()
    return "".join(" " if unicodedata.category(c).startswith("P") else c for c in normalized).split()


def distance(reference, prediction):
    previous = list(range(len(prediction) + 1))
    for i, ref in enumerate(reference, 1):
        current = [i]
        for j, pred in enumerate(prediction, 1):
            current.append(min(current[-1] + 1, previous[j] + 1, previous[j - 1] + (ref != pred)))
        previous = current
    return previous[-1]


def validate_splits(rows):
    ids, groups, hashes = set(), {}, {}
    for row in rows:
        if row["id"] in ids or row["split"] not in {"train", "dev", "test"}:
            raise ValueError("Duplicate ID or unknown split")
        ids.add(row["id"])
        for key, seen in (("group", groups), ("sha256", hashes)):
            if not row[key] or seen.setdefault(row[key], row["split"]) != row["split"]:
                raise ValueError(f"Missing {key} or cross-split leakage")


def summarize(rows):
    speech = {}
    count, emitted = 0, 0
    for row in rows:
        ref, pred = words(row["reference"]), words(row["prediction"])
        if row["kind"] == "non_speech":
            if ref:
                raise ValueError("Non-speech must have an empty reference")
            count += 1
            emitted += bool(pred)
        elif row["kind"] == "speech":
            if not ref:
                raise ValueError("Speech needs a nonempty human reference")
            entry = speech.setdefault(row["language"], dict(clips=0, wordErrors=0, referenceWords=0, falseRejections=0))
            entry["clips"] += 1
            entry["wordErrors"] += distance(ref, pred)
            entry["referenceWords"] += len(ref)
            entry["falseRejections"] += not pred
        else:
            raise ValueError("Unsupported audio kind; overlapping speech requires separate scoring")
    for entry in speech.values():
        entry["wer"] = entry["wordErrors"] / entry["referenceWords"]
        entry["falseRejectionRate"] = entry["falseRejections"] / entry["clips"]
    return dict(speech=speech, nonSpeech=dict(clips=count, emittedClips=emitted,
                emissionRate=emitted / count if count else None))
