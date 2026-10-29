"""Indexes the original art sheets (art/sheets) for the beauty pass: every labelled picture, its label and where it is.

    tools\\comfy\\run_sheet_index.cmd        one click: starts ComfyUI, indexes, then stops ComfyUI
    python tools/comfy/index_sheets.py [--redo] [--restricted] [--only sheet_23_critters,...]

For each sheet, the Qwen3-VL model ComfyUI already has (name_check.NameCheck) lists every labelled sprite with a box
around its picture. Each picture is cut out (its label text left off) to art/generated/sheet_index/<sheet>/<label>.png
and listed in tools/comfy/sheet_index.tsv (sheet, label, file, box). A review image per sheet,
art/generated/sheet_index/<sheet>_review.png, shows every cut-out with its label, to check at a glance.

About a minute per sheet. Resumable: sheets already in the index are skipped unless --redo. A focused run (the
default) leaves 1.5 GB of VRAM free; --restricted leaves 4 GB free at below-normal priority.
"""
import argparse
import csv
import json
import os
import re
import sys
import time
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from name_check import NameCheck  # noqa: E402
from run_all import restart_server, stop_server  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SHEETS = ROOT / "art" / "sheets"
OUT = ROOT / "art" / "generated" / "sheet_index"
INDEX = Path(__file__).resolve().parent / "sheet_index.tsv"
QUESTION = ("This is a sheet of labelled pixel art game sprites. Find every sprite picture that has a text label. For "
            "each one output a JSON object on its own line: {\"label\": \"<the label text>\", \"bbox_2d\": [x1, y1, x2, y2]} "
            "where the box covers only the picture, not its label text. Output only the JSON lines.")


def safe(label: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", label.lower()).strip("_") or "unnamed"


def boxes(answer: str, width: int, height: int):
    """(label, box in pixels) for each JSON line the model wrote. Qwen3-VL gives boxes on a 0-1000 scale; a box
    with any number above 1000 is taken as pixels."""
    found = []
    for chunk in re.findall(r"\{[^{}]*\}", answer):
        try:
            item = json.loads(chunk)
            label = str(item["label"]).strip()
            x1, y1, x2, y2 = (float(v) for v in item["bbox_2d"])
        except (ValueError, KeyError, TypeError):
            continue
        if max(x1, y1, x2, y2) <= 1000:
            x1, x2, y1, y2 = x1 * width / 1000, x2 * width / 1000, y1 * height / 1000, y2 * height / 1000
        box = (max(0, int(min(x1, x2))), max(0, int(min(y1, y2))), min(width, int(max(x1, x2))), min(height, int(max(y1, y2))))
        if label and box[2] - box[0] >= 8 and box[3] - box[1] >= 8:
            found.append((label, box))
    return found


def review(sheet: str, cuts):
    """Every cut-out with its label underneath, on one image."""
    tiles = []
    for label, path in cuts:
        image = Image.open(path).convert("RGB")
        image.thumbnail((180, 180))
        tile = Image.new("RGB", (200, 220), (40, 44, 52))
        tile.paste(image, ((200 - image.width) // 2, 5 + (180 - image.height) // 2))
        ImageDraw.Draw(tile).text((6, 196), label[:30], fill=(235, 235, 235))
        tiles.append(tile)
    if not tiles:
        return
    columns = min(8, len(tiles))
    page = Image.new("RGB", (columns * 200, ((len(tiles) + columns - 1) // columns) * 220), (24, 26, 32))
    for index, tile in enumerate(tiles):
        page.paste(tile, ((index % columns) * 200, (index // columns) * 220))
    page.save(OUT / f"{sheet}_review.png")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--redo", action="store_true")
    parser.add_argument("--restricted", action="store_true")
    parser.add_argument("--only", default="")
    args = parser.parse_args()
    os.environ["RIMTERA_VRAM_FREE_GB"] = "4" if args.restricted else "1.5"
    os.environ["RIMTERA_PRIORITY"] = "below" if args.restricted else "normal"

    rows = list(csv.reader(INDEX.open(encoding="utf-8"), delimiter="\t"))[1:] if INDEX.exists() else []
    done = {row[0] for row in rows}
    wanted = {name for name in args.only.split(",") if name}
    sheets = sorted(path for path in SHEETS.iterdir() if path.suffix.lower() in (".jpg", ".jpeg", ".png")
                    and (not wanted or path.stem in wanted) and (args.redo or path.stem not in done))
    print(f"{len(sheets)} sheets to index ({len(done)} already done)", flush=True)
    if not sheets:
        return
    OUT.mkdir(parents=True, exist_ok=True)
    try:
        restart_server()
        check = NameCheck()
        for number, sheet in enumerate(sheets, start=1):
            started = time.time()
            image = Image.open(sheet).convert("RGB")
            try:
                answer = check._text(QUESTION, check.upload(sheet), length=2500)
            except Exception as error:  # one bad sheet should not end the run; it is retried next time
                print(f"[{number}/{len(sheets)}] {sheet.stem}: FAILED {str(error)[:200]}", flush=True)
                restart_server()
                check = NameCheck()
                continue
            folder = OUT / sheet.stem
            folder.mkdir(exist_ok=True)
            cuts, seen = [], {}
            rows = [row for row in rows if row[0] != sheet.stem]
            for label, box in boxes(answer, image.width, image.height):
                name = safe(label)
                seen[name] = seen.get(name, 0) + 1
                if seen[name] > 1:
                    name = f"{name}_{seen[name]}"
                path = folder / f"{name}.png"
                image.crop(box).save(path)
                cuts.append((label, path))
                rows.append([sheet.stem, label, str(path.relative_to(ROOT)).replace("\\", "/"), ",".join(map(str, box))])
            with INDEX.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.writer(handle, delimiter="\t", lineterminator="\n")
                writer.writerow(["sheet", "label", "file", "box"])
                writer.writerows(rows)
            review(sheet.stem, cuts)
            print(f"[{number}/{len(sheets)}] {sheet.stem}: {len(cuts)} pictures ({time.time() - started:.0f}s)", flush=True)
    finally:
        stop_server()
    print(f"Done. Index: tools/comfy/sheet_index.tsv. Check the cut-outs in art/generated/sheet_index/*_review.png.", flush=True)


if __name__ == "__main__":
    main()
