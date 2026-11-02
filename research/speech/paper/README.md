# Local speech-translation research manuscript

**Status: completed exploratory pilot report; larger study proposed.** The adapter failed to improve test WER and is rejected for release. The neural speech gate is promising on a tiny synthetic-control pilot, but no production fix, 95% accuracy result, completed translation experiment or validated new algorithm is claimed.

Start with the [13-page PDF](output/pdf/local-speech-translation-pilot.pdf) or [editable manuscript with references](manuscript-with-references.md). The research questions prioritize detection and translation accuracy, mixed language, overlapping speakers, speech retention and local latency. C# remains the application/probe stack; Python performs training and statistical analysis.

## Deliverables

| File | Purpose |
| --- | --- |
| `manuscript.md` | Editable manuscript body; references injected by the PDF builder |
| `manuscript-with-references.md` | Generated readable manuscript including all 44 references |
| `output/pdf/local-speech-translation-pilot.pdf` | Rendered manuscript, statistics, tables and uncertainty figure |
| `references.json`, `references.bib` | Structured verified DOI bibliography and Zotero-compatible BibTeX |
| `reference-audit.csv` | Per-paper category, relevance, review depth, DOI and indexing verification limits |
| `indexing-evidence.json` | Official source-list URL, SHA-256 and exact workbook evidence rows |
| `statistics.json`, `analyze.py` | Recomputed clip-bootstrap intervals, Wilson intervals, paired gate tests and assumptions |
| `dataset-audit.json`, `dataset-protocol.md` | Completed technical integrity audit and separately marked future data/annotation protocol |
| `verification.json`, `verify_package.py` | Bibliographic, metric, citation and PDF package checks |
| `test_analysis.py` | Known-result statistical checks and invalid pairing checks |
| `build_pdf.py`, `requirements-document.txt` | Rebuild the document using ReportLab and local fonts |

The [original research workflow](../README.md) contains model/data preparation and training commands. The [original measurements](../results/2026-11-01-pilot) contain per-clip counts and hashes, losses, gate decisions and the environment. The experiment ran on 1 November 2026; this paper and audit were prepared on 2 November 2026. Source baseline at the start of this manuscript work was commit `31022897f02f879edc5b83c58c773e124e6fbace`. No training was silently rerun to obtain better-looking results.

## Reference requirements and free tools

The bibliography contains 44 unique, DOI-verified papers: 21 Whisper/ASR, three speech detection, eight diarization/overlap, and twelve translation/dataset/evaluation. Publication years are 2022: seven; 2023: fourteen; 2024: thirteen; 2025: ten. Every reference is cited in the manuscript. All are within the requested 2022-present window.

Each DOI returned matching CSL metadata through free DOI content negotiation. ISCA citation metadata was also read from publisher pages. Sources were matched against Elsevier's official September 2026 workbook, including its June 2026 conference sheet. The snapshot SHA-256 is `1e2eea11dd6b02609efaf78fb23efe888b47d55a3d1e20dffcd0ec87c56d892b`. Publisher title/year metadata plus covered source/proceedings records establish **source-level eligibility**. Individual Scopus article EIDs were not queried, and a proceedings EID must never be presented as the article's EID. No SINTA rank, journal quartile or citation count is inferred. The DOI audit is a bibliographic check, not an endorsement of results or an exhaustive retraction search.

The review is purposive and concentrated in Interspeech (39/44). Publisher/author abstracts were screened for all records; selected full-text sections informed the main methods and gap discussion. Per-reference review depth is explicit in the audit. The package does not claim 44 independent full-paper replications, exhaustive search coverage or globally proven novelty.

No paid research service is required. Publisher archives and DOI resolution provide metadata and accessible papers. [Zotero](https://www.zotero.org/about) is free/open source; its installed local API and connector were verified as responding during preparation. `references.bib` is ready for Zotero's File > Import. This task did not import records into or change the user's existing library. Paid Scite/SciSpace plans are not a dependency of this package. Crossref's public API requires no subscription, but its API hostname did not resolve in this session; DOI content negotiation successfully provided the metadata instead.

## Reproduce analysis

Run from the repository root with Python 3.12 or compatible:

```powershell
python research/speech/paper/analyze.py
python -m unittest discover -s research/speech/paper -p test_analysis.py -v
python -m unittest discover -s research/speech -p test_metrics.py -v
```

Statistics require only the Python standard library and the committed pilot metrics. Exact enumeration is intentionally limited to very small samples; this script is not a scalable production bootstrap utility. The reported intervals and p-values are descriptive/exploratory because the data are a small convenience subset with synthetic controls and unknown speaker dependence.

To repeat the **audio** audit, reconstruct the pilot using the existing preparation instructions and run with the training environment (SoundFile and NumPy):

```powershell
python research/speech/paper/analyze.py --audio-root "B:\Projects\live translator\offline-research\ml-pilot-2026-11-01"
```

The path above is the original local dataset location. On another machine, substitute the reconstructed dataset directory containing `manifest.jsonl`. The audit records 39 matching audio/reference hashes, 357.14 seconds, no observed cross-split group/hash leakage and one full-scale-sample flag. It does not certify transcript correctness or speaker independence.

## Rebuild and inspect the PDF

Use an isolated environment with `requirements-document.txt`; the original build used the bundled document runtime with ReportLab 4.4.9. Existing model-training dependencies need not change.

```powershell
python -m pip install -r research/speech/paper/requirements-document.txt
python research/speech/paper/build_pdf.py --font-dir "C:\Windows\Fonts"
python research/speech/paper/verify_package.py
pdftoppm -scale-to 1400 -png research/speech/paper/output/pdf/local-speech-translation-pilot.pdf path/to/qa/page
```

The builder uses locally installed Arial regular/bold/italic files; they are not redistributed. On another OS, pass a directory containing those appropriately licensed fonts or adapt the three font registrations. Visually inspect every rendered page after edits. The original 13 pages were inspected for tables, figure labels, bibliography, page numbers and clipping. Poppler printed missing fallback font warnings for Symbol/ArialUnicode, but the embedded text and rendered pages were checked and displayed correctly. PDF byte hashes can change with build timestamps; statistical JSON is deterministic.

## What remains before submission or deployment

The reported negative result is real and usable as a pilot record. A stronger empirical paper still needs independently reviewed references and labels, a representative and locked test set, multiple training seeds, genuine trigger-phrase controls, mixed-language/overlap data, bilingual translation evaluation and measured Windows/Mac latency. The proposed novelty is an evaluation contribution to test; it is not a proven new model architecture. Human authors must establish authorship, target venue, institutional requirements and final disclosures before submitting. No journal submission or application release is performed by these research-only changes.
