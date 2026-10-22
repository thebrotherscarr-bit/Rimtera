"""Generates seamless block textures for Rimtera with ComfyUI (beauty pass).

Reads tools/comfy/tile_textures.tsv (from `dotnet run -- --tile-sprites`): tile id, texture sprite name, display name.
Each batch of 16 is one 1024x1024 sheet: a 4x4 grid of square, borderless, tileable pixel-art textures in the
Neo-Terra style. Each cell is cropped inside its edges, made to wrap seamlessly (the half-offset copy is blended
over the seams), box-downscaled to 32x32, palette-reduced, and written over the tile's texture. The texture it
replaces is kept in art/replaced/ (oldest original only).

    python tools/comfy/gen_textures.py [--exclude word,word] [--only word,word] [--limit N] [--force]

Resumable: textures already regenerated (listed in art/generated/textures/done.txt) are skipped unless --force.
The ComfyUI server is restarted per sheet (see run_all.py).
"""
import argparse
import re
import shutil
import sys
import time
from pathlib import Path

from PIL import Image, ImageEnhance, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parent))
from comfy_client import Comfy  # noqa: E402
from run_all import restart_server  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "Assets" / "Sprites"
OUT = 32

HINTS = [
    (r"ore", "ore veins: bright mineral flecks embedded in dark stone"),
    (r"grass|moss|vine mat", "ground with a living top layer of short growth"),
    (r"sand|dune|silt", "loose granular sand with small pebbles"),
    (r"ice|snow|frost|slush|glacial", "frozen ice and packed snow with cracks and blue highlights"),
    (r"brick|plating|panel|plate|hull", "built sci-fi panels or bricks with clean seams"),
    (r"crystal|prism|geode|gem", "faceted glowing crystal clusters"),
    (r"scrap|junk|rust", "compacted rusted scrap metal and debris"),
    (r"spore|fungal|mycel|mushroom", "spongy teal and cyan fungal mat dotted with softly glowing spores"),
    (r"radiat|rift|void|caustic|uranium", "irradiated ground glowing sickly green"),
    (r"overgrow|root", "soil choked with roots and vines"),
    (r"mud|clay|loam", "wet dark mud"),
    (r"rock|stone|basalt|slate|granite|marble|alabaster|shale", "solid layered rock"),
    (r"regolith|soil|dirt", "packed earthy soil with small stones"),
]


def hint_for(name: str) -> str:
    lowered = name.lower()
    return next((hint for pattern, hint in HINTS if re.search(pattern, lowered)), "natural terrain material")


def prompt_for(rows):
    lines = [f"{index + 1}. {display}: {hint_for(display + ' ' + tile)}." for index, (tile, _, display) in enumerate(rows)]
    return ("A sheet of 16 square terrain textures for a 2D side-scrolling pixel art game, arranged in a strict 4 by 4 grid "
            "of equal square cells. Every cell is completely filled edge to edge with one seamless tileable texture seen "
            "straight on, no borders, no outlines, no frames, no gaps, no objects, no text. 16-bit pixel art with chunky "
            "readable pixels, cohesive sci-fi 'Neo-Terra' palette (deep blues, metallic greys, earthy browns, with glowing "
            "accents only where noted), soft top-left lighting, subtle detail so it repeats without obvious patterns. "
            "Textures, left to right, top to bottom: " + " ".join(lines))


def make_seamless(tile: Image.Image) -> Image.Image:
    """Blends a half-offset copy over the seams so the texture wraps on all four sides."""
    w, h = tile.size
    shifted = Image.new("RGB", (w, h))
    shifted.paste(tile.crop((w // 2, h // 2, w, h)), (0, 0))
    shifted.paste(tile.crop((0, h // 2, w // 2, h)), (w - w // 2, 0))
    shifted.paste(tile.crop((w // 2, 0, w, h // 2)), (0, h - h // 2))
    shifted.paste(tile.crop((0, 0, w // 2, h // 2)), (w - w // 2, h - h // 2))
    # Weight toward the shifted copy near the edges (where the original seams are) and the original in the middle.
    mask = Image.new("L", (w, h))
    pixels = mask.load()
    for y in range(h):
        for x in range(w):
            edge = min(x, w - 1 - x, y, h - 1 - y) / (min(w, h) / 2)
            pixels[x, y] = int(255 * max(0.0, 1.0 - edge * 2.2))
    return Image.composite(shifted, tile, mask)


def cut(sheet: Image.Image, count: int):
    cell_w, cell_h = sheet.width // 4, sheet.height // 4
    inset = int(cell_w * 0.08)
    for index in range(count):
        col, row = index % 4, index // 4
        cell = sheet.crop((col * cell_w + inset, row * cell_h + inset, (col + 1) * cell_w - inset, (row + 1) * cell_h - inset))
        side = min(cell.size)
        cell = cell.crop((0, 0, side, side)).resize((128, 128), Image.BOX)
        cell = make_seamless(cell)
        small = cell.resize((OUT, OUT), Image.BOX)
        # Downscaling flattens contrast: lift it back so textures read at tile size.
        # (A plain contrast lift: autocontrast stretches each channel separately and shifts hues.)
        small = ImageEnhance.Color(ImageEnhance.Contrast(small).enhance(1.3)).enhance(1.1)
        yield small.quantize(colors=24, method=Image.Quantize.MEDIANCUT).convert("RGBA")


def is_ore(tile: str, display: str) -> bool:
    """Whole-word match ("spore" is not an ore): tile ids end in Ore, display names say "ore"."""
    return tile.endswith("Ore") or re.search(r"ore", display.lower()) is not None


def compose_ores(rows, replaced: Path):
    """
    Ores are not generated (downscaling averages their flecks away). Each ore texture becomes the regenerated rock
    texture with the bright, saturated mineral pixels of its original texture laid on top, so ores match their rock.
    """
    rock = SPRITES / "terrain_rock.png"
    if not rock.exists():
        return 0
    base = Image.open(rock).convert("RGBA").resize((OUT, OUT), Image.NEAREST)
    count = 0
    for tile, sprite, display in rows:
        if not is_ore(tile, display):
            continue
        original = replaced / f"{sprite}.png"
        source = original if original.exists() else SPRITES / f"{sprite}.png"
        if not source.exists():
            continue
        if not original.exists():
            shutil.copy(source, original)
        flecks = Image.open(source).convert("RGB").resize((OUT, OUT), Image.NEAREST)
        out = base.copy()
        pixels, fleck = out.load(), flecks.load()
        for y in range(OUT):
            for x in range(OUT):
                r, g, b = fleck[x, y]
                high, low = max(r, g, b), min(r, g, b)
                if high > 90 and (high - low) / max(1, high) > 0.35:
                    pixels[x, y] = (r, g, b, 255)
        out.save(SPRITES / f"{sprite}.png")
        count += 1
    return count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exclude", default="")
    parser.add_argument("--only", default="")
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--steps", type=int, default=20)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--reprocess", help="re-cut an existing sheet file for the first 16 done textures (no generation)")
    args = parser.parse_args()
    rows = [line.split("\t") for line in (ROOT / "tools" / "comfy" / "tile_textures.tsv").read_text(encoding="utf-8").splitlines() if line]
    exclude = [word.lower() for word in args.exclude.split(",") if word]
    only = [word.lower() for word in args.only.split(",") if word]
    work = ROOT / "art" / "generated" / "textures"
    work.mkdir(parents=True, exist_ok=True)
    done_file = work / "done.txt"
    done = set(done_file.read_text(encoding="utf-8").split()) if done_file.exists() else set()
    all_rows = rows
    rows = [row for row in rows
            if not is_ore(row[0], row[2])
            and (args.force or row[1] not in done)
            and not any(word in (row[0] + " " + row[2]).lower() for word in exclude)
            and (not only or any(word in (row[0] + " " + row[2]).lower() for word in only))]
    batches = [rows[i:i + 16] for i in range(0, len(rows), 16)]
    if args.limit:
        batches = batches[:args.limit]
    print(f"{len(rows)} textures in {len(batches)} sheets", flush=True)
    replaced = ROOT / "art" / "replaced"
    replaced.mkdir(parents=True, exist_ok=True)
    started = time.time()
    for index, batch in enumerate(batches, start=1):
        png = None
        for attempt in range(2):  # one retry: a just-restarted server can briefly report out of memory
            restart_server()
            try:
                png = Comfy().generate(prompt_for(batch), width=1024, height=1024, steps=args.steps)
                break
            except Exception as error:
                print(f"[{index}/{len(batches)}] attempt {attempt + 1} failed: {str(error)[:160]}", flush=True)
                time.sleep(10)
        if png is None:
            continue
        sheet_path = work / f"textures_{int(time.time())}.png"
        sheet_path.write_bytes(png)
        for (tile, sprite, _), texture in zip(batch, cut(Image.open(sheet_path).convert("RGB"), len(batch))):
            target = SPRITES / f"{sprite}.png"
            if target.exists() and not (replaced / f"{sprite}.png").exists():
                shutil.copy(target, replaced / f"{sprite}.png")
            texture.save(target)
            done.add(sprite)
        done_file.write_text("\n".join(sorted(done)), encoding="utf-8")
        compose_ores(all_rows, replaced)
        elapsed = time.time() - started
        print(f"[{index}/{len(batches)}] {len(batch)} textures (elapsed {elapsed / 60:.0f} min, "
              f"~{elapsed / index * (len(batches) - index) / 60:.0f} min left) -> {sheet_path.name}", flush=True)


if __name__ == "__main__":
    main()
