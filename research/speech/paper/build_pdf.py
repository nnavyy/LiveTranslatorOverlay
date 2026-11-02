"""Build the manuscript PDF and a standalone Markdown copy with its bibliography."""
import argparse
from html import escape
import json
from pathlib import Path
import re

from reportlab import __version__ as reportlab_version
from reportlab.graphics.shapes import Circle, Drawing, Line, Polygon, String
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle

HERE = Path(__file__).resolve().parent
BLUE = colors.HexColor("#234665")
TEAL = colors.HexColor("#217769")
GRAY = colors.HexColor("#52606D")


def inline(text):
    value = escape(text)
    value = re.sub(r"\[([^\]]+)\]\((https?://[^\s)]+)\)", r'<a href="\2" color="#234665">\1</a>', value)
    value = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", value)
    value = re.sub(r"`([^`]+)`", r'<font name="Courier" size="8">\1</font>', value)
    return value


def gate_figure(stats, width):
    drawing = Drawing(width, 220)
    x0, x1 = 155, width - 20
    y_positions = [("Speech accepted (n=8)", "speech", 155), ("Non-speech accepted (n=3)", "nonSpeech", 65)]
    for title, kind, y in y_positions:
        drawing.add(String(0, y + 30, title, fontName="Paper-Bold", fontSize=9.5, fillColor=BLUE))
        for index, (name, label, color) in enumerate([("currentEnergyGate", "Energy gate", BLUE), ("neuralGate", "Neural VAD", TEAL)]):
            row = stats["gates"][kind][name]
            yy = y - index * 26
            value = row["accepted"] / row["clips"]
            lo, hi = row["wilson95"]
            x = x0 + value * (x1 - x0)
            drawing.add(String(0, yy - 3, label, fontName="Paper", fontSize=9, fillColor=GRAY))
            drawing.add(Line(x0 + lo * (x1 - x0), yy, x0 + hi * (x1 - x0), yy, strokeColor=color, strokeWidth=2))
            for endpoint in [lo, hi]:
                px = x0 + endpoint * (x1 - x0)
                drawing.add(Line(px, yy - 4, px, yy + 4, strokeColor=color, strokeWidth=1))
            drawing.add(Circle(x, yy, 3.5, fillColor=color, strokeColor=color) if index == 0 else Polygon([x, yy + 4, x + 4, yy, x, yy - 4, x - 4, yy], fillColor=color, strokeColor=color))
    for tick in [0, .25, .5, .75, 1]:
        x = x0 + tick * (x1 - x0)
        drawing.add(Line(x, 15, x, 19, strokeColor=GRAY))
        drawing.add(String(x, 1, f"{tick:.0%}", fontName="Paper", fontSize=8, textAnchor="middle", fillColor=GRAY))
    drawing.add(Line(x0, 19, x1, 19, strokeColor=GRAY))
    return drawing


def reference_text(record):
    authors = "; ".join(record["authors"])
    return f'[{record["number"]}] {authors}. ({record["year"]}). {record["title"]}. {record["venue"]}' + (f', {record["volume"]}' if record["volume"] else "") + (f', pp. {record["pages"]}' if record["pages"] else "") + f'. DOI: [{record["doi"]}]({record["url"]}).'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=HERE / "output" / "pdf" / "local-speech-translation-pilot.pdf")
    parser.add_argument("--font-dir", type=Path, default=Path("C:/Windows/Fonts"))
    args = parser.parse_args()
    font_files = [args.font_dir / name for name in ["arial.ttf", "arialbd.ttf", "ariali.ttf"]]
    if not all(path.exists() for path in font_files):
        raise FileNotFoundError("Pass --font-dir containing arial.ttf, arialbd.ttf, ariali.ttf (not redistributed).")
    for name, path in zip(["Paper", "Paper-Bold", "Paper-Italic"], font_files):
        pdfmetrics.registerFont(TTFont(name, str(path)))
    pdfmetrics.registerFontFamily("Paper", normal="Paper", bold="Paper-Bold", italic="Paper-Italic", boldItalic="Paper-Bold")
    styles = getSampleStyleSheet()
    body = ParagraphStyle("PaperBody", fontName="Paper", fontSize=10.2, leading=14.2, spaceAfter=8, textColor=colors.HexColor("#17232E"), splitLongWords=True)
    styles.add(body)
    styles.add(ParagraphStyle("PaperTitle", parent=body, fontName="Paper-Bold", fontSize=23, leading=28, spaceAfter=14, textColor=BLUE))
    styles.add(ParagraphStyle("PaperH2", parent=body, fontName="Paper-Bold", fontSize=14, leading=18, spaceBefore=14, spaceAfter=9, keepWithNext=True, textColor=BLUE))
    styles.add(ParagraphStyle("PaperH3", parent=body, fontName="Paper-Bold", fontSize=11.2, leading=15, spaceBefore=10, spaceAfter=7, keepWithNext=True, textColor=BLUE))
    styles.add(ParagraphStyle("PaperCell", parent=body, fontSize=8.3, leading=11, spaceAfter=0))
    styles.add(ParagraphStyle("PaperReference", parent=body, fontSize=8.7, leading=12, spaceAfter=7))
    styles.add(ParagraphStyle("PaperCaption", parent=body, fontSize=9, leading=12, textColor=GRAY))
    references = json.loads((HERE / "references.json").read_text(encoding="utf-8"))
    stats = json.loads((HERE / "statistics.json").read_text(encoding="utf-8"))
    source = (HERE / "manuscript.md").read_text(encoding="utf-8")
    bibliography = "\n\n".join(reference_text(r) for r in references)
    source = source.replace("<!-- references -->", "<!-- pagebreak -->\n\n## References\n\n" + bibliography)
    # Avoid nonbreaking/dash glyph differences across PDF readers.
    source = source.translate(str.maketrans({"\u2011": "-", "\u2013": "-", "\u2014": "-"}))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    markdown_output = HERE / "manuscript-with-references.md"
    # Preserve the generated diagram in the standalone Markdown edition too.
    from reportlab.graphics import renderSVG
    figure_path = HERE / "output" / "gate-intervals.svg"
    renderSVG.drawToFile(gate_figure(stats, A4[0] - 96), str(figure_path))
    markdown_output.write_text(source.replace("<!-- figure:gate-intervals -->", "![Gate acceptance estimates and Wilson intervals](output/gate-intervals.svg)"), encoding="utf-8")
    width = A4[0] - 96
    story = []
    lines = source.splitlines()
    index = 0
    while index < len(lines):
        line = lines[index].strip()
        if not line:
            index += 1
            continue
        if line == "<!-- pagebreak -->":
            story.append(PageBreak())
        elif line == "<!-- figure:gate-intervals -->":
            story.append(gate_figure(stats, width))
            story.append(Spacer(1, 6))
        elif line.startswith("# "):
            story.append(Paragraph(inline(line[2:]), styles["PaperTitle"]))
        elif line.startswith("## "):
            story.append(Paragraph(inline(line[3:]), styles["PaperH2"]))
        elif line.startswith("### "):
            story.append(Paragraph(inline(line[4:]), styles["PaperH3"]))
        elif line.startswith("|"):
            rows = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                cells = [cell.strip() for cell in lines[index].strip().strip("|").split("|")]
                if not all(re.fullmatch(r":?-+:?", cell.replace(" ", "")) for cell in cells):
                    rows.append([Paragraph(inline(cell), styles["PaperCell"]) for cell in cells])
                index += 1
            n = len(rows[0])
            widths = [width * .24] + [width * .76 / (n - 1)] * (n - 1)
            table = Table(rows, colWidths=widths, repeatRows=1, hAlign="LEFT")
            table.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#E6EEF3")), ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F5F7F8")]), ("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 7), ("RIGHTPADDING", (0, 0), (-1, -1), 7), ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7), ("LINEBELOW", (0, 0), (-1, 0), .7, BLUE)]))
            story.extend([table, Spacer(1, 10)])
            continue
        else:
            paragraph = [line]
            while index + 1 < len(lines) and lines[index + 1].strip() and not lines[index + 1].startswith(("#", "|", "<!--")):
                index += 1
                paragraph.append(lines[index].strip())
            style = "PaperReference" if re.match(r"^\[\d+\]", line) else "PaperCaption" if line.startswith("Figure ") else "PaperBody"
            story.append(Paragraph(inline(" ".join(paragraph)), styles[style]))
        index += 1

    def footer(canvas, doc):
        canvas.saveState()
        if doc.page > 1:
            canvas.setFillColor(GRAY)
            canvas.setFont("Paper", 8)
            canvas.drawString(48, A4[1] - 28, "LOCAL SPEECH TRANSLATION  /  AUDITED PILOT")
        canvas.setStrokeColor(colors.HexColor("#CBD5DC"))
        canvas.line(48, 38, A4[0] - 48, 38)
        canvas.setFont("Paper", 8)
        canvas.setFillColor(GRAY)
        canvas.drawString(48, 25, "Research draft | 2 November 2026 | No production accuracy claim")
        canvas.drawRightString(A4[0] - 48, 25, str(doc.page))
        canvas.restoreState()

    doc = SimpleDocTemplate(str(args.output), pagesize=A4, rightMargin=48, leftMargin=48, topMargin=48, bottomMargin=52, title="Preserving Speech While Reducing False Captions in Local Multilingual Translation", author="Anonymized research draft", subject="Exploratory pilot, dataset audit and prospective protocol")
    doc.build(story, onFirstPage=footer, onLaterPages=footer)
    print(f"Built {args.output}; {len(references)} references; ReportLab {reportlab_version}.")


if __name__ == "__main__":
    main()
