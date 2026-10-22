"""Overnight art run: everything still to generate, in order, resumable. Start it and leave it.

    python tools/comfy/overnight.py [--pause SECONDS]

--pause rests the GPU that long after every image so other programs (video, games) stay smooth; it is
passed to the generator scripts as RIMTERA_COMFY_PAUSE.

1. Block textures not yet regenerated (gen_textures.py; refreshes tools/comfy/tile_textures.tsv first so newly
   themed tiles such as the irradiated core are included).
2. Environment art: sky bodies and clouds, weather particles, parallax backgrounds, flora (gen_env.py).
3. Character animations for every actor without frames (gen_anim.py; refreshes anim_entities.tsv first).

Each step skips work already done, so stopping and re-running continues where it left off. Afterwards rebuild or
publish the game so the new art is copied to the output folder.
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
    if "--pause" in sys.argv[1:-1]:
        os.environ["RIMTERA_COMFY_PAUSE"] = sys.argv[sys.argv.index("--pause") + 1]
    export("--tile-sprites", "tile_textures.tsv")
    export("--entity-sprites", "anim_entities.tsv")
    export("--flora-sprites", "flora.tsv")
    step("Block textures", "gen_textures.py")
    step("Environment art", "gen_env.py")
    step("Animations", "gen_anim.py")
    print("Overnight run finished. Rebuild or publish to pick up the new art.", flush=True)


if __name__ == "__main__":
    main()
