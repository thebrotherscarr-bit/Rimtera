"""Generates animation frames for every actor with ComfyUI (Qwen-Image 2.1 edit, the base sprite as reference).

For each base sprite listed in tools/comfy/anim_entities.tsv (made by `dotnet run -- --entity-sprites`), the sprite
is upscaled onto flat magenta and passed as the reference image; the prompt asks for a 4x2 sheet of the same
character: idle (2), move (4: walk, flap, hop or drift by its motion type) and attack (2). The cells are cut,
backgrounds removed, and every frame is scaled by ONE factor (so the character doesn't change size between frames)
to the base sprite's height, centred with feet on the bottom row. Frames are written next to the base sprite as
<base>_idle_1..2, <base>_move_1..4, <base>_attack_1..2; the renderer (SpriteCatalog.AnimatedFrame) picks them up.

    python tools/comfy/gen_anim.py [--only enemy,boss] [--limit N] [--steps 20] [--force]

Resumable: actors that already have <base>_move_1 are skipped unless --force. The ComfyUI server is restarted per
sheet (see run_all.py for why). Hand-made frame sets (marked "hand") are never replaced.
"""
import argparse
import sys
import time
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from comfy_client import Comfy  # noqa: E402
from run_all import restart_server  # noqa: E402
from sprite_post import remove_background  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "Assets" / "Sprites"
PREFIXES = ["", "creature_", "critter_", "npc_", "boss_", "minion_", "event_", "cmd_", "pet_"]
PRIORITY = {"enemy": 0, "npc": 1, "crew": 2, "boss": 3, "critter": 4, "pet": 5}
MOTION = {
    "walk": "walking cycle with legs alternating",
    "fly": "flying cycle with wings or thrusters flapping up and down",
    "hop": "hopping cycle: crouch, leap, airborne, land",
    "swim": "swimming cycle",
    "float": "hovering drift bobbing up and down",
}
STATES = [("idle", 2), ("move", 4), ("attack", 2)]


def resolve(name: str):
    for prefix in PREFIXES:
        path = SPRITES / f"{prefix}{name}.png"
        if path.exists():
            return path
    return None


def reference_image(base: Path, out: Path):
    """Base sprite scaled up with nearest-neighbour onto a flat magenta 512x512 canvas."""
    sprite = Image.open(base).convert("RGBA")
    scale = max(1, min(384 // max(1, sprite.width), 384 // max(1, sprite.height)))
    big = sprite.resize((sprite.width * scale, sprite.height * scale), Image.NEAREST)
    canvas = Image.new("RGBA", (512, 512), (255, 0, 255, 255))
    canvas.alpha_composite(big, ((512 - big.width) // 2, (512 - big.height) // 2))
    canvas.convert("RGB").save(out)


def prompt_for(category: str, motion: str, attack: str = "melee") -> str:
    if category in ("npc", "crew"):
        action = "working or gesturing"
    elif category in ("pet", "critter") or attack == "none":
        action = "reacting (startled jump or flinch)"
    elif attack == "ranged":
        action = "firing its ranged attack (aiming, then a projectile or energy burst leaving it)"
    else:
        action = "a melee attack (wind-up, then lunging, biting or striking), with no projectiles, beams or muzzle flash"
    return ("Using the reference image's character exactly (same design, colours, proportions and pixel-art style), "
            "draw a pixel art animation sprite sheet of that same character as 8 frames in a strict 4 columns by 2 rows "
            "grid of equal cells, side view facing right, the whole character inside each cell at the same size and the "
            "same ground line. Row 1: frames 1-2 idle (subtle breathing), frames 3-4 the first half of a "
            f"{MOTION.get(motion, MOTION['walk'])}. Row 2: frames 5-6 the second half of that cycle, frames 7-8 {action}. "
            "Flat solid magenta background (#FF00FF), no grid lines, no text, no shadows, crisp 16-bit pixel art.")


def cut_frames(sheet: Image.Image, target_height: int):
    """Cuts a 4x2 sheet into 8 frames, all scaled by one factor to the target height, feet on the bottom row."""
    cell_w, cell_h = sheet.width // 4, sheet.height // 2
    cells = []
    for row in range(2):
        for col in range(4):
            cell = remove_background(sheet.crop((col * cell_w, row * cell_h, (col + 1) * cell_w, (row + 1) * cell_h)))
            box = cell.getbbox()
            cells.append(cell.crop(box) if box else None)
    heights = sorted(cell.height for cell in cells if cell is not None)
    if not heights:
        return []
    scale = target_height / heights[len(heights) // 2]
    width = max(max(1, round(cell.width * scale)) for cell in cells if cell is not None)
    height = max(max(1, round(cell.height * scale)) for cell in cells if cell is not None)
    frames = []
    for cell in cells:
        if cell is None:
            frames.append(None)
            continue
        small = cell.resize((max(1, round(cell.width * scale)), max(1, round(cell.height * scale))), Image.BOX)
        alpha = small.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
        rgb = small.convert("RGB").quantize(colors=24, method=Image.Quantize.MEDIANCUT).convert("RGBA")
        rgb.putalpha(alpha)
        frame = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        frame.alpha_composite(rgb, ((width - rgb.width) // 2, height - rgb.height))
        frames.append(frame)
    return frames


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default="")
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--steps", type=int, default=20)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    wanted = {part for part in args.only.split(",") if part}
    rows = [line.split("\t") for line in (ROOT / "tools" / "comfy" / "anim_entities.tsv").read_text(encoding="utf-8").splitlines() if line]
    rows = [row for row in rows if (not wanted or row[0] in wanted) and row[3] != "hand"]
    rows.sort(key=lambda row: (PRIORITY.get(row[0], 9), row[1]))
    todo = []
    for row in rows:
        category, name, motion = row[0], row[1], row[2]
        attack = row[4] if len(row) > 4 else "melee"
        base = resolve(name)
        if base is None:
            print(f"skip {name}: no base sprite", flush=True)
            continue
        if not args.force and (SPRITES / f"{base.stem}_move_1.png").exists():
            continue
        todo.append((category, base, motion, attack))
    if args.limit:
        todo = todo[:args.limit]
    print(f"{len(todo)} actors to animate", flush=True)
    work = ROOT / "art" / "generated" / "anim"
    work.mkdir(parents=True, exist_ok=True)
    started = time.time()
    for index, (category, base, motion, attack) in enumerate(todo, start=1):
        restart_server()
        reference = work / f"{base.stem}_ref.png"
        reference_image(base, reference)
        try:
            png = Comfy().generate(prompt_for(category, motion, attack), references=[reference], width=1024, height=512, steps=args.steps)
        except Exception as error:  # keep going; a failed actor is retried on the next run
            print(f"[{index}/{len(todo)}] {base.stem}: FAILED {str(error)[:200]}", flush=True)
            continue
        sheet_path = work / f"{base.stem}_sheet.png"
        sheet_path.write_bytes(png)
        target = Image.open(base).convert("RGBA")
        frames = cut_frames(Image.open(sheet_path).convert("RGB"), (target.getbbox() and target.crop(target.getbbox()).height) or target.height)
        written = 0
        names = [f"{state}_{n}" for state, count in STATES for n in range(1, count + 1)]
        for frame_name, frame in zip(names, frames):
            if frame is not None and frame.getbbox():
                frame.save(SPRITES / f"{base.stem}_{frame_name}.png")
                written += 1
        elapsed = time.time() - started
        print(f"[{index}/{len(todo)}] {base.stem}: {written}/8 frames (elapsed {elapsed / 60:.0f} min, "
              f"~{elapsed / index * (len(todo) - index) / 60:.0f} min left)", flush=True)


if __name__ == "__main__":
    main()
