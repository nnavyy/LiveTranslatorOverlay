# Preserving Speech While Reducing False Captions in Local Multilingual Translation

An audited English-Russian pilot and a prospective evaluation protocol

**Research manuscript for review | 2 November 2026 | Anonymized draft**

This manuscript reports completed component experiments and a separately identified plan for a larger study. It is not peer reviewed. Author names and affiliations are omitted rather than inferred. No production accuracy improvement, completed translation experiment, or 95% accuracy result is claimed.

## Abstract

Local speech translation must reject unsupported text without suppressing genuine speech, preserve speaker attribution, and deliver translations with acceptable delay. These requirements are not captured by a single accuracy percentage. We examine them through a focused review of 44 DOI-bearing publications and an auditable, resource-limited pilot for a desktop translation overlay. The pilot contains 30 English and Russian FLEURS recordings and nine generated non-speech controls, totaling 357.14 seconds. A multilingual Whisper Tiny baseline is compared with a rank-4 decoder LoRA adapter trained for 20 optimizer steps. English and Russian test word error rates remain 22.73% and 19.35%, respectively; Russian validation error increases from 30.91% to 32.73%. Both recognizers emit words on all three test non-speech controls. A separate fixed-threshold neural voice detector accepts eight of eight test speech clips and zero of three controls, compared with six of eight and two of three for the application's energy gate. Wide uncertainty intervals and convenience sampling preclude general performance claims. A dataset integrity audit confirms hashes and no observed sentence-group leakage, but flags one recording with full-scale samples and leaves speaker independence and human transcript verification unresolved. We propose a larger paired speech/non-speech challenge with mixed-language, overlap, translation and latency evaluation. The contribution is a reproducible negative result and a concrete system-level evaluation protocol, not a newly validated recognition algorithm.

**Keywords:** automatic speech recognition; Whisper hallucination; local speech translation; voice activity detection; speaker diarization; reproducibility.

<!-- pagebreak -->

## 1. Problem and research questions

The motivating application captures audio from a user-selected playback source and displays source and translated captions. Its owner reported a Ukrainian sign-off translated as “Thank you for watching!” while different words were being spoken. The source language was mixed or uncertain. Because the original recording was not retained, this instance cannot be replayed, labeled or scored. It is a reported failure, not a measured test case. Silence rejection alone would not address a wrong transcript produced during real speech.

Speech recognition, translation and speaker attribution are separate inference problems. An invented sentence can be fluently translated; a correct transcript can be assigned to the wrong speaker; aggressive filtering can appear to reduce hallucinations by hiding real utterances. Careless Whisper documents fabricated phrases and disparities associated with non-vocal durations [1]. Non-speech induction experiments [2] and selective decoder adaptation [3] provide distinct mitigation routes. Neither establishes that a specific application's mixed playback is reliable.

We ask three questions. **RQ1:** does a small, local mixed speech/non-speech adaptation improve recognition and reduce unsupported emissions without blanking speech? **RQ2:** does a pretrained neural detector offer a useful component-level alternative to the current energy heuristic? **RQ3, prospective:** can a single evaluation protocol expose trade-offs among false captions, speech retention, speaker assignment, translation meaning and live-caption delay? The completed pilot addresses RQ1 and a narrow form of RQ2. RQ3 requires new annotated data and has not been experimentally answered.

The intended user experience remains simple: select an audio output source, source language or Auto, target language and a local quality mode. Downloading model assets is distinct from inference; after assets are available, the research training and evaluation operate locally. The application is built principally in C#. Python is used for model adaptation and analysis, while the existing C# probe measures the production gate. This separation permits component experiments while retaining the application's actual gate as the engineering baseline.

### 1.1 Operational definitions

We use **unsupported non-speech emission** for any non-empty normalized transcript on a control known to contain no speech. This narrow measure differs from a **speech hallucination**, a fluent output that lacks support in an utterance containing real speech. The latter needs audio-grounded annotation; ordinary word error rate (WER) does not by itself separate plausible substitutions from wholly invented content. **Diarization** assigns anonymous speaker turns; **separation** attempts to recover concurrent voices. A “Speaker 1” label does not identify a person or determine gender. Uncertain or overlapping segments must remain explicitly uncertain.

## 2. Evidence review and research gap

### 2.1 Search scope and eligibility

The narrative review was assembled on 2 November 2026 through free publisher archives, author-hosted research records, DOI content negotiation and the official Elsevier source list. Search concepts included Whisper hallucination, multilingual adaptation, code switching, VAD, overlapping speech, diarization, speech segmentation, simultaneous translation and evaluation. Interspeech 2022-2025 indexes were screened for these concepts, with targeted checks at ACM, IEEE, ACL and Nature. This is a purposive review, not an exhaustive systematic review or meta-analysis; no complete database-search denominator or PRISMA flow is claimed.

Every one of the 44 counted references has a verified DOI metadata response and a publication year within 2022-2026. The selected publications happen to be from 2022-2025. Twenty-one concern Whisper and ASR, three speech detection, eight diarization/overlap, and twelve translation, datasets or evaluation. Thirty-nine are from Interspeech, creating a venue concentration that limits claims of comprehensive coverage. Recent preprints were not substituted for indexed proceedings solely to meet the count. Foundational software/model cards are listed as material provenance rather than counted as eligible research papers.

Scopus eligibility was checked at the **source/proceedings level**, using the September 2026 source workbook and its June 2026 conference sheet. Interspeech coverage includes 2022-2025; the additional matched sources are ICASSP, FAccT 2024, SLT 2022 proceedings published in 2023, WMT 2022, and Nature. Exact workbook rows and its SHA-256 are recorded in `indexing-evidence.json`. Individual article EIDs were not queried. Thus the evidence supports publication in covered sources, not a claim that every individual Scopus article record was directly inspected. No SINTA ranking is needed for these selections under the requested Scopus-or-SINTA rule. Indexing is not a guarantee that a paper's conclusions are correct.

### 2.2 Recognition and suppression

WhisperX uses speech segmentation and forced alignment for long-form timestamps [4]; CrisperWhisper investigates verbatim transcription and timestamp refinement [5]. These motivate boundary and retention checks, rather than treating a fluent caption as sufficient. Contextual biasing can improve rare vocabulary [6], while audio-event tagging [7] distinguishes background information from transcript text. Robustness to ordinary noise does not establish robustness to other perturbations or language-detection errors [8]. These findings motivate separate acoustic, language and vocabulary challenges.

Dialect evaluation [9] and child-speech adaptation [10] show why a single aggregate benchmark is insufficient. Open Whisper-style models [11] offer alternative reproducibility baselines. Language-specific LoRA [12], rehearsal-free adaptation [13] and new-language tuning [14] address different versions of multilingual interference and forgetting. The present adapter is a simple shared decoder adaptation; it does not reproduce those systems' training regimes. Work on Kazakh [15] and Norwegian [16] further motivates language-specific data and decoding evaluation, but its reported gains cannot be transferred numerically to Russian, English or this application.

Streaming Whisper research identifies truncated words at chunk boundaries as a specific source of errors [17]. Spontaneous speech-error analysis [18], prompted verbatim transcription [19] and stuttering evaluation [20] motivate retaining genuine mistakes and repetitions. A text blacklist that removes every recurring sign-off or repetition could erase correctly recognized speech. Code-switch adaptation [21] supplies a useful comparison, but results on its language pairs do not validate mixed Russian, Ukrainian and English playback.

### 2.3 Detection, speakers and translation

On-device personal VAD [22] addresses target-speaker and resource constraints; multilingual VAD [23] and boundary-sensitive overlap detection [24] motivate learned gating. Personal speaker enrollment differs from following previously unknown voices in playback audio. Thresholds and minimum-duration settings must therefore be calibrated in the intended domain, with quiet and short utterances retained.

The pyannote pipeline separates segmentation, embeddings and clustering [25]. Powerset classification explicitly represents overlapping speaker combinations [26]; conversation-like simulation models realistic pause and overlap distributions [27]. Joint recognition/diarization through a separator [28] or sequential output [29] addresses a stronger problem than detecting two active voices. SDBench emphasizes dataset-dependent errors and efficiency [30], and Streaming Sortformer addresses persistent online speaker tracking [31]. MSDWild offers a real-overlap dataset [32], although an audio-only application cannot claim the benefits of its visual channel.

FLEURS supplies multilingual speech and parallel content for evaluation [33]. Translation segmentation studies [34,35] show why sentence boundaries must be assessed downstream: the segment setting that lowers ASR WER need not maximize translation quality. Multilingual sharing [36], direct streaming translation [37] and joint named-entity prediction [38] suggest alternatives to a fixed ASR-then-translation cascade. AlignAtt [39], incremental blockwise search [40] and feedback from unstable predictions [41] motivate measuring translation quality against delay and caption revision, not just throughput. GigaST distinguishes pseudo-translated training data from human-translated test material [42]. SeamlessM4T is an important multilingual research comparator [43], but its noncommercial released assets cannot automatically be selected for distribution. COMET-22 supplies complementary translation quality estimation [44]; its output is not a percentage of correctly translated captions.

### 2.4 Bounded contribution and proposed novelty

The literature already contains VAD, selective adaptation, LoRA, overlap-aware diarization and latency-aware translation. Combining these established components does not by itself demonstrate algorithmic novelty. In the reviewed evidence, evaluations address different tasks, datasets, language sets and hardware. A remaining **project-specific gap** is an auditable, playback-domain comparison that jointly measures unsupported captions and preservation of genuinely spoken trigger phrases, followed by speaker-attributed translation and local latency under the same split policy. We do not claim that no prior work has studied any part of this combination.

The proposed original contribution is a paired acoustic challenge and joint acceptance protocol for mixed-language playback: match real spoken sign-offs against non-speech segments that can induce the same words; preserve the parent speaker/session across all transformations; measure whether selective suppression improves translated meaning without dropping real utterances. This is a testable contribution hypothesis, not a proven “first” or a demonstrated new state of the art. The completed contribution is narrower: reproducible local training, a rejected adapter, a dataset audit, and uncertainty-aware gate comparisons. A larger study and an expanded independent literature review are prerequisites for a stronger novelty claim.

<!-- pagebreak -->

## 3. Dataset construction and verification

The pilot uses the pinned official `google/fleurs` revision `70bb2e84b976b7e960aa89f1c648e09c59f894dd`, whose dataset card declares CC-BY-4.0. Fifteen recordings each from English and Russian were selected deterministically from the first eligible archive entries with duration between one and twenty seconds. This is a convenience sample. There was no random population sampling or power-based sample-size choice. Original split membership and sentence-group identifiers were retained.

| Partition | Speech clips | Generated controls | Speech duration | Control duration |
| --- | --- | --- | --- | --- |
| Training | 16 | 3 | 175.82 s | 9.00 s |
| Validation | 6 | 3 | 63.78 s | 12.00 s |
| Test | 8 | 3 | 81.54 s | 15.00 s |
| Total | 30 | 9 | 321.14 s | 36.00 s |

Each partition contains silence, a sine tone and Gaussian noise. Durations are three, four and five seconds for train, validation and test; tone frequencies are 310, 447 and 584 Hz. Noise generation uses split-specific deterministic seeds. These controls test a limited non-speech behavior; nine synthetic clips are not nine independent real-world acoustic domains. Music, gaming audio, real overlapping speech, mixed-language segments and spoken sign-off controls were absent.

The audit reopens all 39 WAVs and checks content hashes, reference hashes, finite samples, 16 kHz mono format, sentence groups and cross-split hash duplication. All expected hashes match; all 39 audio hashes and groups are unique; no observed group/hash crosses partitions. One Russian training clip, `fleurs-ru_ru-train-10063904975963482359`, contains 14 samples at full scale, peak 1.0. This is a clipping-risk flag, not proof that its transcript is wrong. It remains in the historical run; it was not silently removed after results were known.

The publisher states that training speakers differ from validation/test speakers. The selected manifest does not expose speaker IDs, so this audit cannot independently verify speaker disjointness, including between validation and test. Hash checks also do not detect all near-duplicate or acoustically related material. Human reannotation of the pilot transcripts has not been performed. Consequently, “verified dataset” here means traceable origin and technical integrity with explicit unresolved quality checks, not certified label accuracy.

Kaggle is a distribution platform, not a validation criterion. A mirror should be admitted only after its original source, version, permission terms, labeling method, sample inventory and duplicate relationships are established. No unverified Kaggle mirror was used. The companion dataset protocol specifies both public-data admission and prospective consented collection. No private user playback was captured, uploaded or used for training in this pilot.

## 4. Completed experimental methods

### 4.1 Recognition adaptation

The baseline is multilingual `openai/whisper-tiny`, revision `169d4a4341b33bc18d8881c4b69c2e104e1cc0af`. The safetensors file contains 151,061,672 bytes with SHA-256 `7ebd0e69e78190ffe1438491fa05cc1f5c1aa3a4c4db3bc1723adbb551ea2395`. The base model is frozen; rank-4 LoRA with alpha 8 and zero dropout adapts query/value projections in decoder self- and cross-attention. There are 49,152 trainable parameters. This is not a head-wise reproduction of Calm-Whisper [3] or the full language-specific LoRA-Whisper method [12].

Training uses AdamW, learning rate 0.0001, zero weight decay, batch size one, gradient norm clipping at one, seed 20261101 and 20 optimizer steps. Shuffled training examples include genuine speech and empty-label controls. Language/task prompt tokens are masked in the loss; transcript and end-of-text tokens are learned. Twenty steps are approximately one pass over 19 training examples, plus one additional update. No converged or optimal training regime is claimed. Per-step losses, example IDs, model revision and environment are retained.

Baseline and candidate inference use greedy decoding, explicit English/Russian language, transcription task and at most 128 new tokens, without an upstream VAD. This does not evaluate automatic language detection, mixed-language decoding, translation, quantization or the full application's cloud path. The candidate was fixed before comparison and rejected after evaluation. Baseline test decoding occurs before training in the script; it does not select hyperparameters. The pilot test is now known and must not serve as a fresh blind test for later development.

Execution used an Intel Core i5-13420H, approximately 7.7 GiB physical memory, Windows 11 build 26300 and four CPU threads; no GPU was used. Recorded packages are Torch 2.15.0+cpu, Transformers 4.57.3, PEFT 0.17.1, NumPy 2.2.6 and SoundFile 0.13.1. Baseline evaluation, training and candidate evaluation took 50.64 seconds after data, packages and models were installed. This excludes setup and is not production caption latency. The saved adapter hash is `fad85e76e51fb3e9a8f9e3817ccca7b976e8b6eed73db80dba51be944aa71aba`.

### 4.2 Separate speech-gate comparison

The C# probe calls the application's actual energy-based `HasSustainedVoiceActivity` function and compares it with pretrained Silero 6.2.0, run through Whisper.net 1.9.1. The neural threshold is fixed at 0.5, state is reset for every file and inference uses two CPU threads. Its model SHA-256 is `2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987`. No VAD weights or thresholds were trained. Acceptance means that the gate finds speech in the clip. It does not imply a correct transcript, and rejection of a control is not a measured end-to-end caption improvement.

### 4.3 Measures and uncertainty

WER is `(substitutions + deletions + insertions) / reference words`, aggregated by word counts within each language. Text normalization is Unicode NFKC, lowercase, punctuation-to-space and whitespace tokenization. Number words and digits are not equated. WER may exceed 100%; subtracting it from 100 does not establish a general “accuracy” probability.

For each three- or four-clip language partition, all possible n-out-of-n clip bootstrap resamples are enumerated: 27 for validation and 256 for test. The 2.5th and 97.5th percentiles use linear interpolation. Baseline/candidate differences use the same paired resamples. These descriptive intervals do not account for unknown speaker dependence, convenience selection or unseen domains. Binary gate acceptance and unsupported-emission rates use two-sided 95% Wilson score intervals without continuity correction. Exact two-sided McNemar tests summarize paired gate discordance. These exploratory tests do not establish population superiority or correct for multiple comparisons.

<!-- pagebreak -->

## 5. Results

### 5.1 Recognition: the adapter is rejected

| Partition / language | Reference words | Baseline errors | Candidate errors | Baseline WER | Candidate WER |
| --- | --- | --- | --- | --- | --- |
| Validation / English | 69 | 13 | 13 | 18.84% | 18.84% |
| Validation / Russian | 55 | 17 | 18 | 30.91% | 32.73% |
| Test / English | 88 | 20 | 20 | 22.73% | 22.73% |
| Test / Russian | 62 | 12 | 12 | 19.35% | 19.35% |

Test bootstrap intervals are 11.54-36.11% for English and 12.50-26.03% for Russian, identical for baseline and candidate. All observed per-clip test error counts are unchanged, producing a degenerate zero-to-zero bootstrap interval for the paired difference. This reflects the observed finite sample; it is not an equivalence test or evidence that adaptation never matters. Russian validation WER increases by 1.82 percentage points, with an exploratory paired bootstrap difference interval of 0.00-7.31 points.

Both models emit text on three of three non-speech test clips, and neither blanks any of eight speech test clips. The non-speech emission proportion is 100%, with a Wilson interval of 43.85-100%. These controls are intentionally small and synthetic; this is not the real-world hallucination prevalence of Whisper or the application. The recorded non-speech test prediction is “you” in each case. The pilot provides no basis to distribute the adapter as an improvement.

### 5.2 Detection: a promising component with substantial uncertainty

| Test outcome | Current energy gate | Neural gate | 95% Wilson intervals |
| --- | --- | --- | --- |
| Speech accepted | 6/8 (75%) | 8/8 (100%) | 40.93-92.85%; 67.56-100% |
| Non-speech accepted | 2/3 (66.67%) | 0/3 (0%) | 20.77-93.85%; 0-56.15% |

Two speech clips pass only the neural gate; two controls pass only the energy gate. Exact paired McNemar p-values are 0.50 for each comparison. These are encouraging observations for a larger experiment, not statistically established superiority. In particular, zero errors in three controls remains compatible with a high underlying error rate. The binary sampling assumptions are themselves an approximation for this convenience sample.

<!-- figure:gate-intervals -->

Figure 1. Test-split acceptance estimates and descriptive 95% Wilson intervals. Higher speech acceptance is desirable; lower non-speech acceptance is desirable. The opposite objectives must be checked together. Neither panel measures translation quality.

All 39 neural VAD evaluations took 1.31 seconds in the probe, excluding model loading. This component total cannot be compared directly with the 50.64-second training/evaluation run or interpreted as live delay. The app's downstream confidence checks were not part of this raw gate experiment.

### 5.3 Audit findings and missing outcomes

The 39-clip integrity audit passes the documented format, hash and group checks and flags the single full-scale recording described above. No human transcript agreement statistic, diarization error rate, overlap-detection F1, translation score, bilingual meaning rating, speaker-attributed translation score, Mac timing or production stability score was measured. Those outcomes are intentionally absent rather than represented as zero. The screenshot cannot be counted as a labeled audio sample.

## 6. Prospective study: testing the entire caption pipeline

### 6.1 Data and annotation

The next dataset should combine versioned public corpora with consented playback-domain recordings. It must include Russian, English and mixed/uncertain language; clean and noisy speech; short replies; pauses; music-only and game-effect negatives; real spoken sign-offs; disfluencies; sequential speakers and overlapping voices. Every augmented or mixed item retains its parent recording, speaker, session and sentence identifiers. Connected parent groups are allocated to train, development and locked test sets before augmentation, preventing a clean original and its noisy or translated derivative from entering different partitions.

Two independent annotators should transcribe what was actually spoken and mark language spans, speech boundaries, overlap and anonymous speaker turns. A third reviewer adjudicates disagreements. Bilingual reviewers create and assess translation references for the direction being tested. Report pre-adjudication normalized transcript disagreement, boundary/overlap agreement and speaker-label consistency; do not infer correctness from annotators agreeing on a model-generated label. Names, numbers, negation, omissions and invented content receive explicit error tags. The companion protocol gives a record schema, exclusions and collection gates. No such new recordings or annotations have yet been produced.

### 6.2 Factorized comparisons

Keep the recognizer fixed while comparing no gate, the energy gate and neural VAD. Then keep the selected development-set gate fixed while testing chunk overlap/boundary rules, forced versus automatic language, and confidence/context reset. Compare the untouched pretrained model with a mixed speech/negative LoRA candidate and a stronger speech-preserving negative curriculum. A head-selective method is a separate ablation, with heads identified for the actual architecture rather than copied from large-v3. Evaluate at least five declared seeds for selected training candidates; use development results for selection and open a new locked test only once.

Next compare ASR-plus-local text translation against a direct speech-translation research baseline under the same language and hardware limits. Gold-transcript-to-translation evaluation isolates translation errors; ASR-transcript-to-translation measures the added recognition burden. For overlap, compare diarization alone with an explicit separation or joint-recognition branch. Measure permutation-aware speaker-attributed transcription, overlap-included diarization error, anonymous speaker-ID switches and errors in the translated speaker attribution. Mixed or low-confidence audio should support an abstention label instead of a forced assignment.

### 6.3 Candidate acceptance and analysis plan

Evaluate every currently exposed target-language choice separately. There are 34 target choices in the application; support labels alone do not demonstrate reliable translation, and Simplified/Traditional Chinese rendering requires its own check. Use corpus-level lexical metrics and an appropriate learned translation metric where validated for the language pair, plus blinded bilingual meaning review. Report severe meaning errors per caption and per minute, alongside coverage. Do not average unsupported target languages into a claim of universal accuracy.

The aspirational recognition threshold is WER at or below 5% on a defined representative test set, reported by language and condition. A candidate also needs acceptable non-speech emission and genuine-speech miss rates, preserved spoken sign-offs, no meaningful language regression, and tolerable stable-caption delay. Practical non-inferiority margins and their primary endpoint must be declared before testing, after development-set variability and user requirements are known. These are proposed research acceptance criteria, not current app specifications or achieved performance.

For orientation only, under independent representative Bernoulli sampling, 299 controls with zero events give a one-sided exact 95% upper bound below 1%, because the upper bound is `1 - 0.05^(1/n)`. This does not justify using 299 correlated synthetic copies, does not cover multiple strata simultaneously, and does not determine the sample size for WER, translation or paired improvements. Plan those sample sizes from a larger development sample, with speaker/session clustering and the intended effect size. Primary comparisons should use paired cluster bootstrap intervals; declare multiplicity handling before examining the locked test.

Measure the delay from audible speech ending to a stable caption on the actual selected device, with p50/p95, cold-start time, revision counts, dropped samples, CPU/GPU use and peak memory. Repeat on Windows, Apple Silicon and Intel macOS. Capture-device selection, resampling, buffering, reconnects and source changes require replayable tests. A local model still needs robust capture and an explicit “no speech” or “uncertain” state. Users should see readable captions and status, while model hashes, thresholds and run identifiers stay in the research record.

<!-- pagebreak -->

## 7. Limitations, reproducibility and research integrity

This pilot has only 30 read-speech examples, nine elementary negatives, one training seed and a single short adaptation budget. It does not show that LoRA or negative-example training is ineffective generally. Conversely, it does not show that a neural gate solves hallucinations during speech. The language is forced during evaluation, so the original mixed-language failure remains untested. WER depends on the specified normalization and does not describe translation meaning. Clip-level uncertainty underestimates unobserved domain variability, and the unknown speaker relationships limit independence assumptions.

The experiment date is 1 November 2026; the audit and manuscript analysis are dated 2 November 2026. Training, data-preparation, per-clip metrics, losses, environment and gate outputs are versioned under `research/speech`. The present package adds the statistical reanalysis, technical dataset audit, DOI bibliography, indexing evidence and prospective protocol. Raw public audio, downloaded models and the rejected adapter are retained outside Git; reconstruction commands and hashes are provided. Rebuilding statistics from committed metrics does not require those large files. Rechecking audio integrity requires the reconstructed dataset.

This is a method-inspired engineering pilot, not a full replication of a published paper. Published results are described as literature evidence, while our own measurements appear in Section 5. The pilot subset, trained adapter and manuscript have not received independent peer review. Publication eligibility depends on the target venue, human author responsibility and additional empirical validation; DOI counts and source indexing do not establish scientific quality on their own.

No new participant data were collected. Future collection requires informed permission, appropriate institutional review where applicable, and separate choices about research use and public redistribution. Audio feedback should be opt-in and minimized; user correction/history data must not silently become training data. Speaker IDs are session-local labels. Perceived voice characteristics cannot reliably establish gender and should not be used as identity ground truth.

AI assistance was used for literature discovery, drafting, code and analysis preparation. Human authors must check claims, source interpretations and authorship disclosures before submission and remain accountable for the work. No financial support, institutional affiliation, ethics approval number or author contribution has been invented. This draft has not been submitted to a journal or conference.

## 8. Conclusion

The local adaptation failed to improve the measured test recognition results and is rejected for release. A pretrained neural gate performed better than the current energy heuristic on the observed test clips, but the sample is too small and artificial to establish deployment quality. The defensible next step is a larger, independently annotated evaluation that jointly tests unsupported emissions, preservation of genuine speech, mixed-language recognition, overlapping speakers, translated meaning and local latency. The supplied audit and protocol make those claims testable without turning an unmet 95% aspiration into a reported result.

## Materials and non-counted provenance

The following are software/data provenance links, not additional papers counted toward the 44-reference requirement: [FLEURS revision and dataset card](https://huggingface.co/datasets/google/fleurs/tree/70bb2e84b976b7e960aa89f1c648e09c59f894dd); [Whisper Tiny revision](https://huggingface.co/openai/whisper-tiny/tree/169d4a4341b33bc18d8881c4b69c2e104e1cc0af); [Silero GGML model revision](https://huggingface.co/ggml-org/whisper-vad/tree/9ffd54a1e1ee413ddf265af9913beaf518d1639b); [Elsevier source-list page](https://www-prod.elsevier.com/products/scopus/content); [project research artifacts](https://github.com/nnavyy/LiveTranslatorOverlay/tree/main/research/speech).

<!-- references -->
