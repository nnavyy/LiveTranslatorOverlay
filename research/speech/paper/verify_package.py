"""Check citation coverage, source years, computed results and generated PDF links."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import time
import urllib.request

from pypdf import PdfReader
from analyze import statistics

HERE = Path(__file__).resolve().parent


def require(condition, message):
    if not condition:
        raise ValueError(message)


def covered(year, source):
    values = source["values"]
    if source["sheet"] == "All Conf. Proceedings Jun. 2026":
        return year == values[3]
    coverage = str(values[5])
    return any(int(start) <= year <= int(end or start) for start, end in re.findall(r"(\d{4})(?:-(\d{4}))?", coverage))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--refresh-dois", action="store_true", help="Free network check; fetch one DOI at a time. No API key needed.")
    args = parser.parse_args()
    references = json.loads((HERE / "references.json").read_text(encoding="utf-8"))
    evidence = json.loads((HERE / "indexing-evidence.json").read_text(encoding="utf-8"))
    require(len(references) == 44, "Expected 44 counted references")
    require(len({r["doi"].lower() for r in references}) == 44, "Duplicate DOI")
    require(len({r["key"] for r in references}) == 44, "Duplicate citation key")
    for number, row in enumerate(references, 1):
        require(row["number"] == number, "Reference numbering mismatch")
        require(re.fullmatch(r"10\.\d{4,9}/\S+", row["doi"]) is not None, "Invalid DOI syntax")
        require(2022 <= row["year"] <= 2026, "Publication outside date window")
        require(covered(row["year"], evidence["rows"][row["sourceEvidence"]]), "Publication year outside recorded source coverage")
        require(bool(row["authors"]) and bool(row["title"]) and bool(row["venue"]), "Missing citation metadata")
        if args.refresh_dois:
            request = urllib.request.Request(row["url"], headers={"Accept": "application/vnd.citationstyles.csl+json"})
            with urllib.request.urlopen(request, timeout=30) as response:
                actual = json.load(response)
            require(actual["DOI"].lower() == row["doi"].lower(), "DOI resolver returned different record")
            require(actual["issued"]["date-parts"][0][0] == row["year"], "DOI publication year changed")
            time.sleep(.3)
    with (HERE / "reference-audit.csv").open(encoding="utf-8-sig", newline="") as stream:
        audit = list(csv.DictReader(stream))
    require([r["doi"] for r in audit] == [r["doi"] for r in references], "CSV audit out of sync")
    bib = (HERE / "references.bib").read_text(encoding="utf-8")
    require(len(re.findall(r"^@(?:article|inproceedings)\{", bib, re.M)) == 44, "BibTeX count mismatch")
    require(all(r["doi"] in bib for r in references), "BibTeX missing DOI")
    body = (HERE / "manuscript.md").read_text(encoding="utf-8")
    citations = {int(n) for group in re.findall(r"\[([\d,]+)\]", body) for n in group.split(",")}
    require(citations == set(range(1, 45)), "Missing or unexpected in-text citation")
    require(json.loads((HERE / "statistics.json").read_text()) == statistics(), "Statistics no longer match the source measurements")
    pdf_path = HERE / "output" / "pdf" / "local-speech-translation-pilot.pdf"
    pdf = PdfReader(pdf_path)
    links = set()
    page_chars = []
    for page in pdf.pages:
        page_chars.append(len(page.extract_text()))
        for annotation in page.get("/Annots", []):
            action = annotation.get_object().get("/A", {})
            if action.get("/URI"):
                links.add(str(action["/URI"]).lower())
    require(all(count > 300 for count in page_chars), "Unexpected empty or near-empty PDF page")
    require(all(r["url"].lower() in links for r in references), "PDF missing a DOI link")
    pdf_text = "\n".join(page.extract_text() for page in pdf.pages)
    require("<!--" not in pdf_text and "turn51" not in pdf_text, "Internal marker leaked into PDF")
    output = {"verifiedAtUtc": datetime.now(timezone.utc).isoformat(), "referenceCount": 44, "uniqueDois": 44, "allReferencesCited": True, "sourceCoverageChecks": 44, "individualScopusArticleRecordsVerified": False, "doiNetworkRecheckedThisRun": args.refresh_dois, "statisticsRecomputed": True, "pdfPages": len(pdf.pages), "pdfDoiLinks": 44, "pdfSha256": hashlib.sha256(pdf_path.read_bytes()).hexdigest(), "visualReviewAutomated": False, "note": "Unit tests and visual review are separate checks documented in README.md; this verifier does not perform them."}
    (HERE / "verification.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(output, indent=2))


if __name__ == "__main__":
    main()
