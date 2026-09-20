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
    pdf.drawString(36, 12, "YFM Fusion Companion v1.0.0 - public Windows x64 guide")
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
        y = draw_text_block(pdf, x + 13, y + 6, width - 13, item, size=size, leading=size * 1.3)
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
    pdf.drawCentredString(PAGE_W / 2, PAGE_H - 253, "Yu-Gi-Oh! Forbidden Memories - Windows x64 - Version 1.0.0")
    pdf.setStrokeColor(CYAN)
    pdf.setLineWidth(2)
    pdf.line(PAGE_W / 2 - 165, PAGE_H - 278, PAGE_W / 2 + 165, PAGE_H - 278)
    pdf.setFillColor(GREEN)
    pdf.setFont("Helvetica-Bold", 13)
    pdf.drawCentredString(PAGE_W / 2, 190, "Manual fusion tools work without an emulator")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 10)
    pdf.drawCentredString(PAGE_W / 2, 163, "Optional read-only live and save integration for NTSC-U RetroArch/SwanStation")
    pdf.drawCentredString(PAGE_W / 2, 105, "Independent fan-made utility - no ROM, BIOS, save, or card artwork included")
    pdf.showPage()


def installation_page(pdf: canvas.Canvas) -> None:
    page_base(pdf, "Install and start", 2, "Use the release ZIP - GitHub's source archive is for developers")
    draw_panel(pdf, 38, 275, 365, 245)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(58, 492, "Four steps")
    draw_bullets(pdf, 58, 461, 325, [
        "Download yfm_companion_windows_x64_v1.0.0.zip from the Releases page.",
        "Extract the entire ZIP. Never run the program from inside the ZIP preview.",
        "Run install_dependencies.cmd first. It checks Windows x64 and verifies that the executable and database stayed together.",
        "Start YFM Fusion Companion.exe. The bottom line should report 722 cards and 25,146 resolved fusion pairs.",
    ], size=10)
    draw_panel(pdf, 420, 275, 383, 245)
    pdf.setFillColor(GOLD)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(440, 492, "What the dependency check means")
    draw_bullets(pdf, 440, 461, 343, [
        "The portable program is self-contained. Ordinary users do not install .NET.",
        "RetroArch/SwanStation is optional. It is only needed for Live Duel and automatic save discovery.",
        "Windows 10 or 11 x64 is required. The release is not code-signed, so SmartScreen may appear.",
        "Developers can run install_dependencies.ps1 -InstallBuildTools to check or install the .NET 9 SDK.",
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
    page_base(pdf, "Compatibility, privacy, and troubleshooting", 11)
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
        "No ROM, BIOS, emulator, memory-card save, or copyrighted card artwork is distributed.",
    ], size=9.3)
    draw_panel(pdf, 38, 68, 765, 195)
    pdf.setFillColor(CYAN)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(58, 233, "When something is unavailable")
    draw_bullets(pdf, 58, 203, 725, [
        "ERROR or DISCONNECTED: confirm Network Commands, port 55355, firewall scope, SwanStation, and that RetroArch was restarted.",
        "NOT IN DUEL: connectivity is healthy; enter a duel. Stale duel structures are intentionally hidden in story mode.",
        "Save looks old: save in-game, close content so the emulator flushes the file, then Refresh Saved Snapshot.",
        "Wrong region/core: Live Duel may be rejected safely, but every manual feature remains available.",
        "For support, use Export Diagnostics; review the text yourself before sharing it.",
    ], size=9.2)
    pdf.showPage()


def build() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(OUTPUT), pagesize=(PAGE_W, PAGE_H), pageCompression=1)
    pdf.setTitle("YFM Fusion Companion - Illustrated User Guide")
    pdf.setAuthor("Vincent van Driel")
    pdf.setSubject("Public user manual for YFM Fusion Companion v1.0.0")

    cover(pdf)
    installation_page(pdf)
    screenshot_page(pdf, 3, "Interface map", "The full window keeps source, display, troubleshooting, and workspace controls visible", "live-duel.png", [
        "Workspace tabs select the job.", "Source badge and Compact Live are global.",
        "Read-only connection and summary.", "Hand and player/opponent field tables.",
        "Advice, deck, and collection subtabs.", "Inspector opens only when requested.",
    ], [(0.25, 0.125), (0.73, 0.055), (0.50, 0.22), (0.50, 0.47), (0.18, 0.64), (0.93, 0.22)])
    turn_adviser_page(pdf)
    screenshot_page(pdf, 5, "Deck Analyzer", "Every physical five-card hand is examined for a complete 40-card deck", "deck-analyzer.png", [
        "Enter up to 40 cards.", "Load the validated saved deck in one click.",
        "Run, cancel, clear, and glitch controls.", "Exact probability and expected-ATK metrics.",
        "Per-result hand chance and representative route.",
    ], [(0.18, 0.25), (0.28, 0.49), (0.17, 0.49), (0.62, 0.58), (0.52, 0.76)])
    screenshot_page(pdf, 6, "Live Duel", "Validated state refreshes automatically every second", "live-duel.png", [
        "Connection and read-only status.", "Life Points, terrain, and update health.",
        "Current ordered five-card hand.", "Player and opponent active field positions.",
        "Best legal live routes.", "Open the selected-card inspector.",
    ], [(0.47, 0.22), (0.62, 0.30), (0.13, 0.48), (0.66, 0.48), (0.48, 0.77), (0.93, 0.22)])
    screenshot_page(pdf, 7, "Card Inspector", "Select a live card, then inspect basic and advanced catalog data", "live-duel-inspector.png", [
        "Select a card in any live table.", "Inspector slides over the right edge.",
        "Basic type, ATK, and DEF appear first.", "Advanced Data expands catalog relationships.",
    ], [(0.20, 0.47), (0.86, 0.27), (0.86, 0.45), (0.86, 0.69)])
    screenshot_page(pdf, 8, "Save Snapshot", "Imports supported memory-card images without modifying them", "save-snapshot.png", [
        "Refresh discovery or choose an SRM/MCR file.", "Source, timestamp, and validation details.",
        "Saved 40-card constructed deck.", "Chest, deck, total owned, and Library flag.",
        "Copy snapshot data into the two analysis tabs.",
    ], [(0.19, 0.22), (0.24, 0.38), (0.18, 0.66), (0.70, 0.66), (0.58, 0.22)])
    screenshot_page(pdf, 9, "Owned-card Optimizer", "Builds a legal 40-card deck for the selected strategy and matchup", "owned-card-optimizer.png", [
        "Choose profile, focus types, field, and opponent types.", "Search and enter owned quantities.",
        "Build, cancel, clear, or set filtered cards to three.", "Exact finalist metrics.",
        "Deck, outcomes, limits, and exclusions explain the result.",
    ], [(0.48, 0.21), (0.20, 0.56), (0.22, 0.34), (0.63, 0.43), (0.70, 0.56)])
    screenshot_page(pdf, 10, "Compact Live", "A narrow right-side companion that leaves the central duel area visible", "compact.png", [
        "Result name and effective ATK.", "Hand routes use plain slot numbers.",
        "F(1)-F(5) are monster positions; F(6)-F(10) are spell/trap positions.", "PIN stays above the game; FULL restores the normal window.",
    ], [(0.34, 0.10), (0.76, 0.10), (0.80, 0.15), (0.85, 0.02)])
    safety_page(pdf)
    pdf.save()


if __name__ == "__main__":
    build()
