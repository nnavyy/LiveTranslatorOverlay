# Dataset admission and prospective collection protocol

Status: proposed protocol, 2 November 2026. The only completed dataset in this work is the 39-clip pilot. No new participant recruitment, human annotation campaign or Kaggle dataset validation is claimed.

## Existing pilot admission record

| Check | Evidence | Decision / unresolved issue |
| --- | --- | --- |
| Original provider | Google FLEURS, pinned revision `70bb2e84b976b7e960aa89f1c648e09c59f894dd` | Use original archive/TSV URLs in `../results/2026-11-01-pilot/dataset-index.json` |
| Research description | Conneau et al., DOI `10.1109/SLT54892.2023.10023141` | Publication year 2023; the conference is named SLT 2022 |
| Permission metadata | Revision's YAML declares `cc-by-4.0`; body says CC-BY | Preserve attribution and identify derived subsets; model/data licenses are separate |
| Selection | First eligible 1-20 s members, eight/three/four per language per partition | Convenience sample; not representative or randomly sampled |
| Integrity | 39 audio hashes and reference hashes checked against manifest | All match; these checks do not establish semantic label correctness |
| Format | Every clip decoded as finite mono 16 kHz | Pass |
| Duplicate/group leakage | 39 distinct hashes and sentence/control groups | No observed cross-split hash or group leakage; perceptual duplicates not exhaustively checked |
| Speaker disjointness | Publisher states train speakers differ from dev/test | Local speaker IDs unavailable; independent verification and dev/test disjointness unresolved |
| Acoustic risk | Russian train clip ending `10063904975963482359`: 14 full-scale samples in 12.06 s | Retain historical run; review in future data screening; do not silently rewrite results |
| Reference quality | Original normalized transcript field and hashes | No independent bilingual reannotation performed |
| Scope | 30 read-speech clips + nine synthetic controls, 357.14 s | No real music, overlapping voices, mixed-language speech or human translation references |

The machine-readable audit is `dataset-audit.json`. Recreate it using `analyze.py --audio-root ...`. This file stores measurements, not an invented “verified” quality badge.

## Public-data admission, including Kaggle mirrors

1. Record original institution/creator, original release URL, DOI if available, exact version/revision, download time, source license and terms. A platform listing or a popularity score is not verification.
2. For a mirror, trace the files and labels to the original release. Record uploader changes. Compare manifests/checksums or a documented sample of original files. If provenance or permissions are unclear, quarantine the candidate rather than train on it.
3. Inventory languages, domains, durations, sample rates, channels, codec, speaker/session identifiers and annotation method. Separate original human labels, pseudo-labels and synthetic speech. Never use model-generated text as unquestioned test truth.
4. Validate decoding, finite samples, empty files, duration outliers, clipping/full-scale samples, long silence and severe transcript/audio mismatch. Keep a machine-readable rejection reason and original file hash. Do not alter a published experiment's manifest retroactively.
5. Detect exact file/PCM duplicates, normalized text duplicates and approximate acoustic duplicates. Build a parent graph across versions, augmentations, mixtures, translations and mirrors. Split connected speaker/session/content groups before transformations. Investigate suspicious near matches manually.
6. Have qualified annotators review every locked-test item for the actual spoken language and transcript. Use independent review and adjudication for ambiguous speech. Maintain annotation version and uncertainty. A published corpus can still contain errors or domain mismatch.
7. Freeze test data, reference translations and normalization before candidate selection. Keep an untouched external test corpus to detect benchmark overfitting. Pretraining overlap may be unknowable for models with undisclosed training sources; disclose it rather than asserting absence.

No Kaggle mirror has passed these checks in this project. FLEURS already provides a traceable primary source. Current Mozilla Common Voice Russian releases are potential additional data, but access terms, download permission, release inventory and a full audit must be checked before admission. The current official release listing is [Mozilla Data Collective](https://mozilladatacollective.com/datasets/cmu5x45pn00dao107j4o9w2yv); it is a candidate, not data used in the reported experiment.

## Proposed original dataset

Scope is selected playback-device audio. The app must not silently switch to microphone capture. Recording participants in a research collection is a separate consented activity, not passive collection from all app users. Begin with legally reusable recordings or volunteers who understand research use, model training, retention and any redistribution. Do not scrape private voice chats. Consent to participate, consent to redistribute audio, and consent to distribute derived models should be documented separately; allow withdrawal until the declared freeze point.

Construct strata for English, Russian and explicitly annotated mixed language. Ukrainian segments must be labeled when present rather than silently reclassified as Russian. Within language strata include: quiet speech, noisy speech, accents, short replies, names/numbers/negation, real repetitions, pauses, genuine spoken sign-offs, music-only controls, game effects, sequential voices and overlap. Voluntary demographic information may support bias audits, but do not guess sensitive demographics from a voice. Use anonymous speaker IDs; voice pitch is not a reliable gender label.

The distinguishing challenge pairs real spoken trigger phrases with acoustically negative clips on which a baseline emits the same or similar text. Counterfactual pairing is established from audio-grounded labels, not by treating every instance of a phrase as hallucinated. Include matched non-trigger utterances and quiet speech, so suppressing everything cannot pass. Select mining candidates on training/development data only; lock the final test before evaluating candidate filters.

Synthetic overlaps retain both source utterances, exact offsets, gain ratios, transforms and source identities. Assign the connected parent group to a split before mixing. Include genuine conversational overlap in the held-out set because arbitrary synthetic mixing cannot reproduce turn-taking. Music and effects need their own reuse permissions and provenance. Do not infer “independent” observations from many augmentations of one track.

### Annotation and adjudication

Two independent annotators produce verbatim transcripts and language spans without seeing model predictions. They mark speech boundaries, pauses, overlapping intervals and anonymous speaker turns. A third reviewer resolves disagreements; retain both original annotations. Difficult spans receive explicit unintelligible/uncertain labels with a declared scoring policy. Do not silently rewrite disfluencies into intended fluent sentences.

For translation evaluation, bilingual reviewers create target references from verified source meaning, blind to model identity. Mark names, amounts, units, negation, omitted facts, invented claims and speaker-attribution errors separately from stylistic differences. Evaluate actual recognized-source output and gold-source output to separate ASR propagation from translation failures. Reference translations in every evaluated target language need their own review; translating references automatically and scoring against the same model would be circular.

Report pre-adjudication agreement: normalized transcript edit disagreement, speech/overlap interval intersection-over-union or F1 under a declared boundary tolerance, and speaker-label agreement after optimal permutation. For bilingual severity labels, report weighted agreement and the raw confusion matrix. These are proposed measures; no annotator-agreement values exist for this pilot.

### Minimum record schema

| Field | Meaning |
| --- | --- |
| `record_id`, `dataset_version`, `original_sha256`, `pcm_sha256` | Stable identity and byte/decoded-content provenance |
| `source_url`, `source_revision`, `license`, `attribution`, `permission_record` | Source and permitted uses; private consent record is not committed publicly |
| `parent_ids`, `speaker_ids`, `session_id`, `content_group`, `split` | Leakage prevention across all transforms and related content |
| `capture_device_class`, `sample_rate`, `channels`, `duration_s`, `transform_parameters` | Acquisition and reproducibility |
| `language_spans`, `speech_intervals`, `overlap_intervals`, `speaker_turns` | Time-aligned ground truth; annotate uncertainty |
| `verbatim_transcript`, `annotator_versions`, `adjudication_status` | Original independent labels and resolved reference |
| `translations`, `translation_review`, `critical_error_tags` | Language-specific meaning references and review status |
| `quality_flags`, `admission_decision`, `rejection_reason` | Transparent exclusions and acoustic risks |

Do not publish personal names, emails, exact device identifiers or raw consent forms in the open manifest. A public record may link to a restricted audit ID. Document retention, deletion and redistribution policy before collecting participant audio.

## Sample size, splits and release gates

Use a separate development collection to estimate variance and plan sample size for the primary endpoint. Split by speaker/session/content-connected group, not by waveform file. Declare target margins, language strata, cluster bootstrap unit, number of training seeds and multiple-comparison policy before the locked test. The 299-zero-event calculation in the manuscript is a single-binomial illustration, not a complete study power analysis.

Admit data for training only when provenance, permission and technical checks pass. Admit it as gold test data only when independent annotation and split checks also pass. Publish a dataset card, split manifest, annotation guide, exclusions and checksums alongside any redistributable release. Until those stages are complete, status is **planned**, never **collected** or **validated**.
