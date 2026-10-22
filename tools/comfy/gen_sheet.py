"""Generates Rimtera sprites in batches of up to 16 with ComfyUI (Qwen-Image 2.1), one 4x4 sheet per batch.

A batch file is JSON: {"name": "...", "size": 48, "items": [["sprite_name", "short visual description"], ...]}.
Each sheet is prompted as a 4x4 grid of separate objects on flat magenta in the shared Neo-Terra style; the raw
sheet is kept in art/generated/, each grid cell becomes a sprite (background removed, trimmed, downscaled,
palette-reduced) in Assets/Sprites/<sprite_name>.png. Art it replaces is moved to art/replaced/ first.

    python tools/comfy/gen_sheet.py tools/comfy/batches/accessories_1.json [--steps 20] [--keep-existing]

The ComfyUI server must be running (tools/comfy/start_comfy.ps1).
"""
import argparse
import json
import shutil
import sys
import time
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from comfy_client import Comfy  # noqa: E402
from sprite_post import to_sprite  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
STYLE = ("16-bit pixel art game sprites in a cohesive sci-fi 'Neo-Terra' style: chunky readable silhouettes, "
         "dark outlines, limited palette with metallic greys, deep blues and glowing cyan/amber accents, "
         "soft top-left lighting, crisp pixels, no anti-aliased blur")


def sheet_prompt(items):
    lines = [f"{index + 1}. {description}" for index, (_, description) in enumerate(items)]
    return (f"A sprite sheet of {len(items)} separate game item icons arranged in a strict 4 by 4 grid, "
            f"one object centered in each equal square cell, generous empty space between objects, every object "
            f"fully inside its own cell, nothing touching or overlapping, no grid lines, no text, no labels, "
            f"no shadows on the ground. Flat solid magenta background (#FF00FF) everywhere. {STYLE}. "
            f"Every object is a different, distinct design that matches its own name and description: vary the "
            f"silhouette, materials and details between cells; never repeat the same shape in another colour. "
            f"Objects, left to right, top to bottom: " + " ".join(lines))


def cut(sheet: Image.Image, count: int, size: int):
    cell_w, cell_h = sheet.width // 4, sheet.height // 4
    for index in range(count):
        col, row = index % 4, index // 4
        cell = sheet.crop((col * cell_w, row * cell_h, (col + 1) * cell_w, (row + 1) * cell_h))
        yield to_sprite(cell, size)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("batch")
    parser.add_argument("--steps", type=int, default=20)
    parser.add_argument("--seed", type=int)
    parser.add_argument("--keep-existing", action="store_true", help="skip sprites that already have art")
    args = parser.parse_args()
    batch = json.loads(Path(args.batch).read_text(encoding="utf-8"))
    items = batch["items"][:16]
    sprites = ROOT / "Assets" / "Sprites"
    if args.keep_existing:
        items = [item for item in items if not (sprites / f"{item[0]}.png").exists()]
    if not items:
        print("nothing to generate")
        return
    started = time.time()
    png = Comfy().generate(sheet_prompt(items), width=1024, height=1024, steps=args.steps, seed=args.seed)
    generated = ROOT / "art" / "generated"
    generated.mkdir(parents=True, exist_ok=True)
    sheet_path = generated / f"{batch['name']}.png"
    sheet_path.write_bytes(png)
    sheet = Image.open(sheet_path).convert("RGB")
    replaced = ROOT / "art" / "replaced"
    replaced.mkdir(parents=True, exist_ok=True)
    written = []
    for (name, _), sprite in zip(items, cut(sheet, len(items), batch.get("size", 48))):
        if sprite.getbbox() is None or min(sprite.size) < 4:
            print(f"  empty cell for {name}; kept existing art")
            continue
        target = sprites / f"{name}.png"
        # Keep the oldest original in art/replaced: a second regeneration must not overwrite it.
        if target.exists() and not (replaced / f"{name}.png").exists():
            shutil.move(target, replaced / f"{name}.png")
        elif target.exists():
            target.unlink()
        sprite.save(target)
        written.append(name)
    print(f"{batch['name']}: {len(written)}/{len(items)} sprites in {time.time() - started:.0f}s -> {sheet_path}")


if __name__ == "__main__":
    main()
