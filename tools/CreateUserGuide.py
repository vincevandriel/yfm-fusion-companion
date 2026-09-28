from __future__ import annotations

from pathlib import Path
from textwrap import wrap

from reportlab.lib.colors import Color, HexColor, white
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas
from reportlab.lib.utils import ImageReader


ROOT = Path(__file__).resolve().parents[1]
SCREENSHOTS = ROOT / "docs" / "screenshots"
OUTPUT = ROOT / "output" / "pdf" / "YFM-Fusion-Companion-User-Guide.pdf"
PAGE_W, PAGE_H = landscape(A4)

NAVY = HexColor("#07141d")
PANEL = HexColor("#102636")
PANEL_2 = HexColor("#1c3b50")
CYAN = HexColor("#45c8ef")
GOLD = HexColor("#f7c64b")
MUTED = HexColor("#b8c8d4")
GREEN = HexColor("#70d8b2")
RED = HexColor("#ff6b74")


def footer(pdf: canvas.Canvas, page_number: int) -> None:
    pdf.setStrokeColor(PANEL_2)
    pdf.line(34, 24, PAGE_W - 34, 24)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(36, 12, "YFM Fusion Companion - public Windows x64 guide")
    pdf.drawRightString(PAGE_W - 36, 12, f"Page {page_number}")


def page_base(pdf: canvas.Canvas, title: str, page_number: int, subtitle: str = "") -> None:
    pdf.setFillColor(NAVY)
    pdf.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    pdf.setFillColor(CYAN)
    pdf.setFont("Helvetica-Bold", 22)
    pdf.drawString(36, PAGE_H - 40, title)
    if subtitle:
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 9.5)
        pdf.drawString(37, PAGE_H - 57, subtitle)
    footer(pdf, page_number)


def wrapped_lines(text: str, max_width: float, font: str, size: float) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        candidate = f"{current} {word}".strip()
        if current and stringWidth(candidate, font, size) > max_width:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    return lines


def draw_text_block(
    pdf: canvas.Canvas,
    x: float,
    y: float,
    width: float,
    text: str,
    size: float = 10,
    color: Color = white,
    leading: float | None = None,
    bold: bool = False,
) -> float:
    font = "Helvetica-Bold" if bold else "Helvetica"
    leading = leading or size * 1.35
    pdf.setFont(font, size)
    pdf.setFillColor(color)
    for paragraph in text.split("\n"):
        if not paragraph:
            y -= leading * 0.55
            continue
        for line in wrapped_lines(paragraph, width, font, size):
            pdf.drawString(x, y, line)
            y -= leading
    return y


def draw_bullets(
    pdf: canvas.Canvas,
    x: float,
    y: float,
    width: float,
    items: list[str],
    size: float = 9.5,
    gap: float = 5,
) -> float:
    for item in items:
        pdf.setFillColor(CYAN)
        pdf.circle(x + 3, y + 2, 2.2, fill=1, stroke=0)
        y = draw_text_block(pdf, x + 13, y, width - 13, item, size=size, leading=size * 1.3)
        y -= gap
    return y


def draw_panel(pdf: canvas.Canvas, x: float, y: float, width: float, height: float) -> None:
    pdf.setFillColor(PANEL)
    pdf.roundRect(x, y, width, height, 7, fill=1, stroke=0)


def fit_image(path: Path, x: float, y: float, max_width: float, max_height: float) -> tuple[float, float, float, float]:
    image = ImageReader(str(path))
    iw, ih = image.getSize()
    scale = min(max_width / iw, max_height / ih)
    width, height = iw * scale, ih * scale
    return x + (max_width - width) / 2, y + (max_height - height) / 2, width, height


def screenshot_page(
    pdf: canvas.Canvas,
    page_number: int,
    title: str,
    subtitle: str,
    image_name: str,
    labels: list[str],
    points: list[tuple[float, float]],
) -> None:
    page_base(pdf, title, page_number, subtitle)
    image_path = SCREENSHOTS / image_name
    ix, iy, iw, ih = fit_image(image_path, 42, 112, PAGE_W - 84, 400)
    pdf.setStrokeColor(PANEL_2)
    pdf.setLineWidth(1.2)
    pdf.rect(ix - 2, iy - 2, iw + 4, ih + 4, fill=0, stroke=1)
    pdf.drawImage(str(image_path), ix, iy, iw, ih, preserveAspectRatio=True, mask="auto")

    for number, (nx, ny) in enumerate(points, 1):
        px = ix + nx * iw
        py = iy + (1 - ny) * ih
        pdf.setFillColor(GOLD)
        pdf.setStrokeColor(NAVY)
        pdf.setLineWidth(1.2)
        pdf.circle(px, py, 9, fill=1, stroke=1)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 8.5)
        pdf.drawCentredString(px, py - 3, str(number))

    columns = 2
    col_width = (PAGE_W - 92) / columns
    for index, label in enumerate(labels):
        col = index % columns
        row = index // columns
        lx = 46 + col * col_width
        ly = 91 - row * 25
        pdf.setFillColor(GOLD)
        pdf.circle(lx + 6, ly + 2, 7, fill=1, stroke=0)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 7.5)
        pdf.drawCentredString(lx + 6, ly - 0.5, str(index + 1))
        draw_text_block(pdf, lx + 18, ly + 6, col_width - 24, label, size=7.6, leading=9.1)
    pdf.showPage()


def cover(pdf: canvas.Canvas) -> None:
    pdf.setFillColor(NAVY)
    pdf.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    pdf.setFillColor(PANEL)
    pdf.roundRect(52, 70, PAGE_W - 104, PAGE_H - 140, 16, fill=1, stroke=0)
    pdf.setFillColor(CYAN)
    pdf.setFont("Helvetica-Bold", 36)
    pdf.drawCentredString(PAGE_W / 2, PAGE_H - 180, "YFM FUSION COMPANION")
    pdf.setFillColor(white)
    pdf.setFont("Helvetica-Bold", 21)
    pdf.drawCentredString(PAGE_W / 2, PAGE_H - 220, "Illustrated User Guide")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 12)
    pdf.drawCentredString(PAGE_W / 2, PAGE_H - 253, "Yu-Gi-Oh! Forbidden Memories - Windows x64 companion")
    pdf.setStrokeColor(CYAN)
    pdf.setLineWidth(2)
    pdf.line(PAGE_W / 2 - 165, PAGE_H - 278, PAGE_W / 2 + 165, PAGE_H - 278)
    pdf.setFillColor(GREEN)
    pdf.setFont("Helvetica-Bold", 13)
    pdf.drawCentredString(PAGE_W / 2, 190, "Manual fusion tools work without an emulator")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 10)
    pdf.drawCentredString(PAGE_W / 2, 163, "Optional read-only live and save integration for NTSC-U RetroArch/SwanStation")
    pdf.drawCentredString(PAGE_W / 2, 105, "Independent fan-made utility - offline artwork with separate attribution")
    pdf.showPage()


def installation_page(pdf: canvas.Canvas) -> None:
    page_base(pdf, "Install and start", 2, "Use the release ZIP - GitHub's source archive is for developers")
    draw_panel(pdf, 38, 275, 365, 245)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(58, 492, "Four steps")
    draw_bullets(pdf, 58, 461, 325, [
        "Download a Windows x64 ZIP from the latest release on the Releases page.",
        "Extract the entire ZIP. Never run the program from inside the ZIP preview.",
        "Keep Resources, Documentation and Licenses beside the executable. Resources contains the database, artwork and research.",
        "Start YFM Fusion Companion.exe. The bottom line should report 722 cards and 25,146 resolved fusion pairs.",
    ], size=10)
    draw_panel(pdf, 420, 275, 383, 245)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(440, 492, "What is included")
    draw_bullets(pdf, 440, 461, 343, [
        "The portable program is self-contained. Ordinary users do not install .NET.",
        "RetroArch/SwanStation is optional. It is only needed for Live Duel and automatic save discovery.",
        "Windows 10 or 11 x64 is required. The release is not code-signed, so SmartScreen may appear.",
        "Documentation includes the illustrated manual, searchable design guide and a source-code index. See CONTRIBUTING.md online to build from source.",
    ], size=10)
    draw_panel(pdf, 38, 68, 765, 180)
    pdf.setFillColor(CYAN)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(58, 218, "Optional RetroArch setup")
    draw_bullets(pdf, 58, 188, 725, [
        "RetroArch: Settings > Network > Network Commands = ON; Network Command Port = 55355.",
        "Leave Network RetroPad and stdin Commands OFF. Restart RetroArch once, then launch the NTSC-U game with SwanStation.",
        "The companion connects to 127.0.0.1 and sends read-only status and memory-read requests. Keep unsolicited inbound UDP 55355 blocked in Windows Firewall.",
    ], size=9.5)
    pdf.showPage()


def turn_adviser_page(pdf: canvas.Canvas) -> None:
    page_base(pdf, "Turn Adviser", 4, "Enter only cards that actually exist in the current hand and player field")
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(45, 507, "Legal selection order")
    boxes = [
        ("FIELD M3", "Optional one field target"),
        ("HAND 2", "First selected hand card"),
        ("RESULT A", "First interaction result"),
        ("HAND 5", "Next selected hand card"),
        ("FINAL", "Ranked final result"),
    ]
    x = 45
    for index, (heading, detail) in enumerate(boxes):
        width = 132
        draw_panel(pdf, x, 390, width, 82)
        pdf.setFillColor(CYAN if index < 4 else GREEN)
        pdf.setFont("Helvetica-Bold", 12)
        pdf.drawCentredString(x + width / 2, 438, heading)
        draw_text_block(pdf, x + 10, 416, width - 20, detail, size=7.5, color=MUTED, leading=9)
        if index < len(boxes) - 1:
            pdf.setFillColor(GOLD)
            pdf.setFont("Helvetica-Bold", 18)
            pdf.drawCentredString(x + width + 15, 425, "+")
        x += 153
    draw_panel(pdf, 45, 82, 360, 275)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(65, 327, "Inputs and controls")
    draw_bullets(pdf, 65, 297, 320, [
        "Hand: up to five ordered cards.",
        "Monster Field and Spell/Trap Field: up to five optional positions each.",
        "Analyze Turn ranks legal routes by final ATK, then DEF.",
        "Tab accepts the top autocomplete result and moves to the next input.",
        "Empty slots are ignored; duplicate copies remain distinct by slot.",
    ], size=9.5)
    draw_panel(pdf, 425, 82, 372, 275)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(445, 327, "Rules the route obeys")
    draw_bullets(pdf, 445, 297, 332, [
        "The field card, when used, interacts with the first selected hand card.",
        "Only one occupied field position may participate.",
        "Intermediate results continue through later hand cards in the displayed order.",
        "Non-fusions that merely discard a material are omitted.",
        "Compatible equips are terminal; their bonus cannot survive a later monster fusion.",
        "Documented glitches can be included or excluded explicitly.",
    ], size=9.2)
    pdf.showPage()


def safety_page(pdf: canvas.Canvas) -> None:
    page_base(pdf, "Compatibility, privacy, and troubleshooting", 14)
    draw_panel(pdf, 38, 290, 370, 230)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(58, 490, "Compatibility boundary")
    draw_bullets(pdf, 58, 460, 330, [
        "Manual adviser, analyzer, and optimizer work from the bundled offline catalog.",
        "Live decoding and automatic discovery are validated for NTSC-U / SLUS-01411 in RetroArch/SwanStation.",
        "Other regions, revisions, emulators, cores, and modified games are not claimed compatible; use manual tools.",
        "RetroAchievements Hardcore Mode works because the companion never uses save states.",
    ], size=9.3)
    draw_panel(pdf, 428, 290, 375, 230)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(448, 490, "Read-only and private by design")
    draw_bullets(pdf, 448, 460, 335, [
        "No game input, memory writes, save edits, config edits, telemetry, updater, ads, or online API.",
        "Local settings store window bounds, compact/topmost choices, and the last selected save path.",
        "Diagnostics are created only on request and exclude gameplay/card values.",
        "No ROM, BIOS, emulator or memory-card save is distributed. Bundled artwork has separate attribution.",
    ], size=9.3)
    draw_panel(pdf, 38, 68, 765, 195)
    pdf.setFillColor(CYAN)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(58, 233, "When something is unavailable")
    draw_bullets(pdf, 58, 203, 725, [
        "ERROR or DISCONNECTED: confirm Network Commands, port 55355, firewall scope, SwanStation, and that RetroArch was restarted.",
        "NOT IN DUEL: connectivity is healthy; enter a duel. Stale duel structures are intentionally hidden in story mode.",
        "Save looks old: save in-game, close content so the emulator flushes the file, then Refresh in the analyzer or optimizer.",
        "Wrong region/core: Live Duel may be rejected safely, but every manual feature remains available.",
        "For support, use Export Diagnostics; review the text yourself before sharing it.",
    ], size=9.2)
    pdf.showPage()


def build() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(OUTPUT), pagesize=(PAGE_W, PAGE_H), pageCompression=1, invariant=1)
    pdf.setTitle("YFM Fusion Companion - Illustrated User Guide")
    pdf.setAuthor("Vincent van Driel")
    pdf.setSubject("Public user manual for YFM Fusion Companion")

    cover(pdf)
    installation_page(pdf)
    screenshot_page(pdf, 3, "Interface map", "The full window keeps source, display, troubleshooting, and workspace controls visible", "live-duel.png", [
        "Workspace tabs select the job.", "Source badge and Compact Live are global.",
        "Read-only connection and summary.", "Hand and player/opponent field tables.",
        "Advice, deck, and collection subtabs.", "Inspector opens only when requested.",
    ], [(0.25, 0.125), (0.60, 0.055), (0.50, 0.22), (0.50, 0.47), (0.18, 0.815), (0.93, 0.22)])
    turn_adviser_page(pdf)
    screenshot_page(pdf, 5, "Deck Analyzer", "Every physical five-card hand is examined for a complete 40-card deck", "deck-analyzer.png", [
        "Enter up to 40 cards.", "Load the validated saved deck in one click.",
        "Run, cancel, clear, and glitch controls.", "Exact probability and expected-ATK metrics.",
        "Per-result hand chance and representative route.",
    ], [(0.18, 0.25), (0.255, 0.68), (0.08, 0.68), (0.60, 0.815), (0.50, 0.895)])
    screenshot_page(pdf, 6, "Live Duel", "Validated state refreshes automatically every second", "live-duel.png", [
        "Connection and read-only status.", "Life Points, terrain, and update health.",
        "Current ordered five-card hand.", "Player and opponent active field positions.",
        "Best legal live routes and both guardian-star chains; F#? preserves unknown opponent state.", "Open the selected-card inspector.",
    ], [(0.47, 0.22), (0.62, 0.30), (0.13, 0.48), (0.66, 0.48), (0.48, 0.895), (0.93, 0.22)])
    screenshot_page(pdf, 7, "Card Inspector", "Select a live card, then inspect basic and advanced catalog data", "live-duel-inspector.png", [
        "Select a card in any live table.", "Inspector slides over the right edge.",
        "Basic type, ATK, and DEF appear first.", "Advanced Data expands catalog relationships.",
    ], [(0.20, 0.47), (0.86, 0.27), (0.86, 0.45), (0.86, 0.69)])
    screenshot_page(pdf, 8, "Recommended campaign decks", "Six researched reference builds; opening-hand availability is not a duel win rate", "recommended-decks.png", [
        "Distinct icons select each reference build.", "Required copies already owned appear out of 40 beneath each icon.",
        "Every missing card names its best Free Duel opponent, required rank group, and conditional drop chance.", "Check all opening hands; cancel whenever needed.",
        "Adapt a strategy to your collection; the optimizer compares other starts too.", "Reference metrics can include missing cards; read sources and limits.",
    ], [(0.11, 0.16), (0.17, 0.257), (0.48, 0.59), (0.125, 0.735), (0.166, 0.938), (0.425, 0.79)])
    screenshot_page(pdf, 9, "Free Duel database", "Browse every Free Duel opponent and search cards or duelists after three characters", "free-duel-gallery.png", [
        "Search suggestions combine matching cards and duelists.", "All 39 Free Duel opponents appear with their portraits and names.",
        "Select a portrait without leaving the deck library.", "Recommended Decks and Free Duel Database share one searchable screen.",
    ], [(0.45, 0.05), (0.50, 0.46), (0.83, 0.31), (0.50, 0.18)])
    screenshot_page(pdf, 10, "Duelist reward tables", "Opponent details open over the same window and preserve your place", "free-duel-duelist-popup.png", [
        "The enlarged portrait and duelist name identify the selected opponent.", "S/A POW, S/A TEC, and B/C/D tables remain separate.",
        "Every row shows card number, name, weight, denominator, and exact conditional chance.", "Select a card row to open its complete card and farming details.",
    ], [(0.18, 0.16), (0.49, 0.29), (0.50, 0.61), (0.53, 0.83)])
    screenshot_page(pdf, 11, "Card and farming details", "Card art, game data, password-shop data, and all drop sources appear together", "free-duel-card-popup.png", [
        "The enlarged offline card image accompanies its in-game description.", "Type, attribute, level, ATK, DEF, and both guardian stars are shown.",
        "Password and starchip cost appear when the card is available in the shop.", "Drop sources are ordered from highest to lowest conditional chance and link back to the duelist.",
    ], [(0.23, 0.25), (0.55, 0.23), (0.25, 0.49), (0.69, 0.65)])
    screenshot_page(pdf, 12, "Suggested deck and ordering", "Save import is integrated into Deck Analyzer and Owned-Card Optimizer", "suggested-order-Alphabetical.png", [
        "Enter or load owned quantities; card artwork works offline.", "Alphabetical is the default card order.",
        "Choose card number, ATK or DEF; the choice is remembered.", "ATK and DEF sort highest first; card numbers lowest first.",
        "Campaign summary keeps uncertainty visible.", "Fields, equips and removal are scored as coherent support; setups may need separate turns.",
    ], [(0.11, 0.61), (0.41, 0.42), (0.52, 0.42), (0.73, 0.47), (0.48, 0.24), (0.82, 0.60)])
    screenshot_page(pdf, 13, "Compact Live", "A narrow right-side companion that leaves the central duel area visible", "compact.png", [
        "Result name and effective ATK.", "Hand routes use plain slot numbers.",
        "F(1)-F(5) are monster positions; F(6)-F(10) are spell/trap positions.", "20px stars sit beneath Result; warm/red glows clarify choices and known enemy relations.",
        "PIN stays above the game; FULL restores the normal window.",
    ], [(0.34, 0.10), (0.87, 0.10), (0.85, 0.15), (0.24, 0.23), (0.85, 0.02)])
    safety_page(pdf)
    pdf.save()


if __name__ == "__main__":
    build()
