"""Import the pinned 722-card artwork source; requires Pillow only at import time."""
import hashlib
import io
import json
import re
import sqlite3
import urllib.request
import zipfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
REVISION = "c50e070636fed6a0b2a0d6a9f2f88f79f07d58c2"
REPOSITORY = "https://github.com/hzrqftr/yugioh-forbiddenmemories"
archive = ROOT / "artifacts" / ("card-artwork-source-" + REVISION + ".zip")
archive.parent.mkdir(parents=True, exist_ok=True)
if not archive.exists():
    with urllib.request.urlopen("https://codeload.github.com/hzrqftr/yugioh-forbiddenmemories/zip/" + REVISION, timeout=120) as response:
        archive.write_bytes(response.read())
folder = ROOT / "assets" / "card-artwork"
folder.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(archive) as source:
    prefix = "yugioh-forbiddenmemories-" + REVISION + "/"
    rows = json.loads(source.read(prefix + "data/card_images.json"))
    assert len(rows) == 722 and {int(row["number"]) for row in rows} == set(range(1, 723))
    with sqlite3.connect((ROOT / "artifacts" / "yfm.db").as_uri() + "?mode=ro", uri=True) as database:
        names = dict(database.execute("select card_id,card_name from cards"))
    normalize = lambda name: re.sub("[^a-z0-9]", "", name.lower().replace("α", "a"))
    assert all(normalize(row["name"]) == normalize(names[int(row["number"])]) for row in rows), "Card-number/name mismatch"
    (folder / "card_images.json").write_bytes(source.read(prefix + "data/card_images.json"))
    (folder / "UPSTREAM-NOTICE.md").write_bytes(source.read(prefix + "NOTICE.md"))
    converted = []
    for row in rows:
        assert row["localPath"] == "images/" + row["number"] + ".webp"
        original = source.read(prefix + row["localPath"])
        image = Image.open(io.BytesIO(original))
        image.load()
        target = folder / (row["number"] + ".png")
        image.save(target, format="PNG")
        converted.append({"CardId": int(row["number"]), "Name": row["name"], "Path": target.name,
                          "Width": image.width, "Height": image.height,
                          "UpstreamSHA256": hashlib.sha256(original).hexdigest(),
                          "SHA256": hashlib.sha256(target.read_bytes()).hexdigest()})
    manifest = {"Repository": REPOSITORY, "Revision": REVISION,
                "ArchiveSHA256": hashlib.sha256(archive.read_bytes()).hexdigest(),
                "Conversion": "Lossless WebP decode to PNG for built-in WPF support; no crop or resize",
                "ImportedCards": 722, "Cards": converted}
    (folder / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("Imported and matched all 722 card images to the companion catalog.")
