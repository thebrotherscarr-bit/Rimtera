"""Overnight art run: everything still to generate, in order, resumable. Start it and leave it.

    python tools/comfy/overnight.py [--only beauty,textures,env] [--restricted] [--pause SECONDS] [--limit N]

A night has one focus, art or audio, never both. A focused night (the default) gets the machine: the ComfyUI server
runs at normal priority with 1.5 GB of VRAM left free. --restricted leaves 4 GB free at below-normal priority so the
PC stays usable. --pause rests the GPU that long after every image (RIMTERA_COMFY_PAUSE).

1. Beauty and size pass (beauty_pass.py): every character redrawn in one style at one pixel density, then
   re-animated from its new art, then every item icon. Refreshes anim_entities.tsv and item_sprites.tsv first.
2. Block textures not yet regenerated (gen_textures.py; refreshes tile_textures.tsv first).
3. Environment art: sky bodies and clouds, weather particles, parallax backgrounds, flora (gen_env.py).

--only runs some of these; --limit caps each beauty lane (sheets or characters) for a short run. Each step skips
work already done, so stopping and re-running continues where it left off. Afterwards rebuild or publish the game so
the new art is copied to the output folder.
"""
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools" / "comfy"


def export(flag: str, target: str):
    """Refreshes a list file from the game (dotnet run -- <flag>)."""
    result = subprocess.run(["dotnet", "run", "--project", str(ROOT), "--", flag], capture_output=True, text=True, cwd=ROOT)
    if result.returncode == 0 and result.stdout.strip():
        (TOOLS / target).write_text(result.stdout, encoding="utf-8")
    else:
        print(f"could not refresh {target}; using the existing list", flush=True)


def step(name: str, script: str, *args: str):
    print(f"=== {name} ===", flush=True)
    subprocess.run([sys.executable, str(TOOLS / script), *args], cwd=ROOT)


def main():
    argv = sys.argv[1:]
    restricted = "--restricted" in argv
    os.environ["RIMTERA_VRAM_FREE_GB"] = "4" if restricted else "1.5"
    os.environ["RIMTERA_PRIORITY"] = "below" if restricted else "normal"
    if "--pause" in argv[:-1]:
        os.environ["RIMTERA_COMFY_PAUSE"] = argv[argv.index("--pause") + 1]
    limit = ["--limit", argv[argv.index("--limit") + 1]] if "--limit" in argv[:-1] else []
    only = argv[argv.index("--only") + 1].split(",") if "--only" in argv[:-1] else ["beauty", "textures", "env"]
    if "beauty" in only:
        export("--entity-sprites", "anim_entities.tsv")
        export("--item-sprites", "item_sprites.tsv")
        step("Beauty and size pass", "beauty_pass.py", *limit)
    if "textures" in only:
        export("--tile-sprites", "tile_textures.tsv")
        step("Block textures", "gen_textures.py")
    if "env" in only:
        export("--flora-sprites", "flora.tsv")
        step("Environment art", "gen_env.py")
    print("Overnight run finished. Rebuild or publish to pick up the new art.", flush=True)


if __name__ == "__main__":
    main()
