"""Beauty and size pass: redraws every character and item sprite in one cohesive style at one pixel density, then
re-animates the characters from their new art. Resumable; start it and leave it.

    python tools/comfy/beauty_pass.py [--only actors,anim,items] [--names a,b] [--limit N] [--steps 20] [--force]

Lanes, in order:
1. actors: every character's base sprite (enemies, critters, bosses, town NPCs, crew, pets; dotnet run --
   --entity-sprites), 4 per 1024x1024 sheet. The old sprites, laid out in the grid, are reference image 1 (so each
   keeps its design); the style anchors (tools/comfy/style_anchors.txt) are image 2 (so all share one look).
2. anim: 8 frames per character, drawn from its new base sprite placed in all 8 cells of a 4x2 template, so the
   model only changes poses: frames stay on-model, on-grid and at one scale. Frames are mapped to the base sprite's
   colours and centred on the body (beauty_core.frames_from_sheet).
3. items: weapon, tool, armor, accessory, ammo and consumable icons (dotnet run -- --item-sprites), 16 per sheet.

Size: everything is generated large and box-downscaled to one density, 40 art pixels per tile (the game's largest
zoom), so sprites are only ever scaled down in game. A character fills its drawn box (collision size times draw
scale): its height is box height x 40 px. Item icons are 96 px (weapons and tools, held at body size) or 64 px.

The art each sprite replaces is kept in art/replaced/beauty/ (the first time only, so it is always the original).
Raw sheets and before/after review images are in art/generated/beauty/. Finished sprites are listed in
art/generated/beauty/done_<lane>.txt and skipped next time; --force redoes them. Characters with hand-made frames
are left alone. The ComfyUI server restarts before every sheet (see run_all.py); RIMTERA_VRAM_FREE_GB and
RIMTERA_PRIORITY (set by overnight.py) choose a focused or restricted run.
"""
import argparse
import io
import shutil
import sys
import time
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from beauty_core import DENSITY, animation_template, centre_on_body, cut_sheet, finish, frames_from_sheet, reference_sheet, style_sheet  # noqa: E402
from comfy_client import Comfy  # noqa: E402
from gen_anim import MOTION, STATES  # noqa: E402
from run_all import restart_server  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools" / "comfy"
SPRITES = ROOT / "Assets" / "Sprites"
WORK = ROOT / "art" / "generated" / "beauty"
REPLACED = ROOT / "art" / "replaced" / "beauty"
PRIORITY = {"enemy": 0, "boss": 1, "npc": 2, "crew": 3, "critter": 4, "pet": 5}
PREFIXES = ("creature_", "critter_", "boss_", "npc_", "pet_", "event_", "minion_", "cmd_", "item_", "weapon_",
            "armor_", "tool_", "ammo_")
STYLE = ("crisp 16-bit pixel art in image 2's cohesive sci-fi Neo-Terra style: one consistent pixel size, clean dark "
         "outlines, light from the top left, three or four shades per colour, readable chunky shapes, no glow halos, "
         "no blur, no gradients")
BACKGROUND = "Flat solid magenta background (#FF00FF) everywhere, no grid lines, no text, no labels, no shadows."
ITEM_LANES = ("weapon", "tool", "cutter", "demolisher", "bugnet", "armor", "accessory", "ammo", "consumable", "potion",
              "food", "material", "utility", "vanity")


def label(sprite: str) -> str:
    name = next((sprite[len(prefix):] for prefix in PREFIXES if sprite.startswith(prefix)), sprite)
    return name.replace("_", " ")


def load(name: str):
    path = SPRITES / f"{name}.png"
    return Image.open(path).convert("RGBA") if path.exists() else None


def done_list(lane: str) -> set:
    path = WORK / f"done_{lane}.txt"
    return set(path.read_text(encoding="utf-8").split()) if path.exists() else set()


def mark_done(lane: str, names):
    with (WORK / f"done_{lane}.txt").open("a", encoding="utf-8") as handle:
        handle.writelines(f"{name}\n" for name in names)


def install(name: str, sprite: Image.Image):
    """Writes Assets/Sprites/<name>.png, keeping the original in art/replaced/beauty/ the first time."""
    target = SPRITES / f"{name}.png"
    backup = REPLACED / f"{name}.png"
    if target.exists() and not backup.exists():
        REPLACED.mkdir(parents=True, exist_ok=True)
        shutil.copy2(target, backup)
    sprite.save(target)


def review(path: Path, pairs, scale: int = 3):
    """Before (left) and after (right) for each sprite, scaled up so the pixels can be judged."""
    rows = [(old, new) for old, new in pairs if new is not None]
    if not rows:
        return
    def big(image):
        if image is None:
            return Image.new("RGBA", (1, 1), (0, 0, 0, 0))
        k = max(1, min(scale, 400 // max(image.size)))
        return image.resize((image.width * k, image.height * k), Image.NEAREST)
    rows = [(big(old), big(new)) for old, new in rows]
    width = max(old.width for old, _ in rows) + max(new.width for _, new in rows) + 30
    sheet = Image.new("RGBA", (width, sum(max(o.height, n.height) + 10 for o, n in rows)), (40, 44, 52, 255))
    y = 0
    split = max(old.width for old, _ in rows) + 20
    for old, new in rows:
        sheet.alpha_composite(old, (5, y))
        sheet.alpha_composite(new, (split, y))
        y += max(old.height, new.height) + 10
    ImageDraw.Draw(sheet).line((split - 10, 0, split - 10, sheet.height), fill=(90, 96, 110, 255))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def generate(prompt: str, references, width: int, height: int, steps: int):
    """One sheet on a freshly started server; one retry, since a just-restarted server can briefly run out of memory."""
    for attempt in range(2):
        restart_server()
        try:
            png = Comfy().generate(prompt, references=references, width=width, height=height, steps=steps, ref_resolution=768)
            return Image.open(io.BytesIO(png)).convert("RGB")
        except Exception as error:
            print(f"  generation failed ({'retrying' if attempt == 0 else 'skipped'}): {str(error)[:200]}", flush=True)
    return None


def style_reference() -> Path:
    names = [line.strip() for line in (TOOLS / "style_anchors.txt").read_text(encoding="utf-8").splitlines()
             if line.strip() and not line.startswith("#")]
    path = WORK / "style.png"
    style_sheet([sprite for sprite in (load(name) for name in names) if sprite is not None]).save(path)
    return path


def actor_rows(args):
    rows = [line.split("\t") for line in (TOOLS / "anim_entities.tsv").read_text(encoding="utf-8").splitlines() if line]
    rows = [row for row in rows if len(row) >= 7 and row[3] != "hand" and (SPRITES / f"{row[1]}.png").exists()]
    if args.names:
        rows = [row for row in rows if row[1] in args.names]
    rows.sort(key=lambda row: (PRIORITY.get(row[0], 9), row[1]))
    return rows


def run_actors(args, style: Path):
    done = set() if args.force else done_list("actors")
    rows = [row for row in actor_rows(args) if row[1] not in done]
    sheets = [rows[i:i + 4] for i in range(0, len(rows), 4)]
    if args.limit:
        sheets = sheets[:args.limit]
    print(f"actors: {len(rows)} characters, {len(sheets)} sheets", flush=True)
    for index, group in enumerate(sheets, start=1):
        started = time.time()
        olds = [load(row[1]) for row in group]
        reference = WORK / "actors" / f"ref_{group[0][1]}.png"
        reference.parent.mkdir(parents=True, exist_ok=True)
        reference_sheet(olds, 2, 2, 1024, 1024).save(reference)
        cells = " ".join(f"{n + 1}. a {label(row[1])} ({row[0]})." for n, row in enumerate(group))
        prompt = (f"Image 1 is a sheet of {len(group)} game characters in a 2 by 2 grid; image 2 shows the art style. "
                  f"Redraw image 1 as a polished sprite sheet: the same characters in the same cells, each keeping its own "
                  f"design, colours, silhouette, proportions and pose, side view facing right, drawn as {STYLE}. Each "
                  f"character centred and fully inside its own cell, nothing touching or overlapping. {BACKGROUND} "
                  f"Characters, left to right, top to bottom: {cells}")
        sheet = generate(prompt, [reference, style], 1024, 1024, args.steps)
        if sheet is None:
            continue
        sheet.save(WORK / "actors" / f"sheet_{group[0][1]}.png")
        cut = cut_sheet(sheet, 2, 2)
        pairs = []
        for row, old, cell in zip(group, olds, cut):
            if cell is None:
                print(f"  {row[1]}: empty cell, kept the old sprite", flush=True)
                continue
            box_w, box_h = float(row[5]), float(row[6])
            sprite = finish(cell, round(box_w * 1.6 * DENSITY), round(box_h * DENSITY), colours=32)
            install(row[1], sprite)
            mark_done("actors", [row[1]])
            pairs.append((old, sprite))
        review(WORK / "review" / f"actors_{group[0][1]}.png", pairs)
        print(f"[{index}/{len(sheets)}] {', '.join(row[1] for row in group)} ({time.time() - started:.0f}s)", flush=True)


def run_anim(args):
    redrawn = done_list("actors")
    done = set() if args.force else done_list("anim")
    rows = [row for row in actor_rows(args) if row[1] in redrawn and row[1] not in done]
    if args.limit:
        rows = rows[:args.limit]
    print(f"anim: {len(rows)} characters", flush=True)
    names = [f"{state}_{n}" for state, count in STATES for n in range(1, count + 1)]
    for index, row in enumerate(rows, start=1):
        started = time.time()
        category, name, motion, attack = row[0], row[1], row[2], row[4]
        base = load(name)
        template = WORK / "anim" / f"{name}_template.png"
        template.parent.mkdir(parents=True, exist_ok=True)
        animation_template(base).save(template)
        if category in ("npc", "crew"):
            action = "working or gesturing with its hands"
        elif category in ("pet", "critter") or attack == "none":
            action = "reacting (a startled jump or flinch)"
        elif attack == "ranged":
            action = "firing its ranged attack (aiming, then a projectile or energy burst leaving it)"
        else:
            action = "a melee attack (wind-up, then lunging, biting or striking), with no projectiles, beams or muzzle flash"
        prompt = ("Image 1 shows the same character 8 times in a 4 by 2 grid. Redraw it as a pixel art animation sprite "
                  "sheet of that character: keep its exact design, colours, proportions, size, pixel size and outline in "
                  "every cell and change only its pose. Row 1: cells 1-2 idle (subtle breathing, the two slightly "
                  f"different), cells 3-4 the first half of a {MOTION.get(motion, MOTION['walk'])}. Row 2: cells 5-6 the "
                  f"second half of that cycle, cells 7-8 {action}. Side view facing right, each pose centred in its own "
                  "cell with its feet on the same ground line as in image 1, nothing crossing into another cell. "
                  + BACKGROUND)
        sheet = generate(prompt, [template], 1024, 512, args.steps)
        if sheet is None:
            continue
        sheet.save(WORK / "anim" / f"{name}_sheet.png")
        frames = frames_from_sheet(sheet, base)
        if any(frame is None for frame in frames):
            # A partial set would mix old and new frames; the character is retried on the next run.
            print(f"[{index}/{len(rows)}] {name}: {sum(f is not None for f in frames)}/8 frames came out; kept the old ones", flush=True)
            continue
        frames = centre_on_body(frames)
        for frame_name, frame in zip(names, frames):
            install(f"{name}_{frame_name}", frame)
        mark_done("anim", [name])
        review(WORK / "review" / f"anim_{name}.png", [(base, frame) for frame in frames])
        print(f"[{index}/{len(rows)}] {name}: 8 frames ({time.time() - started:.0f}s)", flush=True)


def run_items(args, style: Path):
    actors = {row[1] for row in actor_rows(argparse.Namespace(names=None))}
    done = set() if args.force else done_list("items")
    rows = [line.split("\t") for line in (TOOLS / "item_sprites.tsv").read_text(encoding="utf-8").splitlines() if line]
    rows = [row for row in rows if len(row) >= 3 and row[2].split(":")[0] in ITEM_LANES and row[0] not in actors and row[0] not in done]
    if args.names:
        rows = [row for row in rows if row[0] in args.names]
    rows.sort(key=lambda row: (row[2], row[0]))
    sheets = [rows[i:i + 16] for i in range(0, len(rows), 16)]
    if args.limit:
        sheets = sheets[:args.limit]
    print(f"items: {len(rows)} icons, {len(sheets)} sheets", flush=True)
    for index, group in enumerate(sheets, start=1):
        started = time.time()
        olds = [load(row[0]) for row in group]
        reference = WORK / "items" / f"ref_{group[0][0]}.png"
        reference.parent.mkdir(parents=True, exist_ok=True)
        reference_sheet(olds, 4, 4, 1024, 1024).save(reference)
        cells = " ".join(f"{n + 1}. {row[1]} ({row[2]})." for n, row in enumerate(group))
        prompt = (f"Image 1 is a sheet of {len(group)} game item icons in a 4 by 4 grid; image 2 shows the art style. "
                  f"Redraw image 1 as a polished icon sheet: the same objects in the same cells, each keeping its own "
                  f"design, colours and silhouette (draw an empty cell's object from its name), as {STYLE}. Every object "
                  f"centred and fully inside its own cell with space around it, nothing touching or overlapping. "
                  f"{BACKGROUND} Objects, left to right, top to bottom: {cells}")
        sheet = generate(prompt, [reference, style], 1024, 1024, args.steps)
        if sheet is None:
            continue
        sheet.save(WORK / "items" / f"sheet_{group[0][0]}.png")
        pairs = []
        for row, old, cell in zip(group, olds, cut_sheet(sheet, 4, 4)):
            if cell is None:
                continue
            size = 96 if row[2].split(":")[0] in ("weapon", "tool", "cutter", "demolisher", "bugnet") else 64
            sprite = finish(cell, size, size, colours=24)
            install(row[0], sprite)
            mark_done("items", [row[0]])
            pairs.append((old, sprite))
        review(WORK / "review" / f"items_{group[0][0]}.png", pairs)
        print(f"[{index}/{len(sheets)}] {group[0][0]} .. {group[-1][0]} ({time.time() - started:.0f}s)", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default="actors,anim,items")
    parser.add_argument("--names", type=lambda text: set(text.split(",")))
    parser.add_argument("--limit", type=int, default=0, help="sheets (actors, items) or characters (anim) per lane")
    parser.add_argument("--steps", type=int, default=20)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True)
    lanes = args.only.split(",")
    style = style_reference() if {"actors", "items"} & set(lanes) else None
    if "actors" in lanes:
        run_actors(args, style)
    if "anim" in lanes:
        run_anim(args)
    if "items" in lanes:
        run_items(args, style)
    print("Beauty pass finished. Review: art/generated/beauty/review/. Rebuild or publish to pick up the new art.", flush=True)


if __name__ == "__main__":
    main()
