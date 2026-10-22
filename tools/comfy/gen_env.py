"""Generates Rimtera's environment art with ComfyUI: flora, sky bodies and clouds, weather particles, and
parallax background panoramas per biome — all in the shared Neo-Terra style.

    python tools/comfy/gen_env.py [--only sky,fx,backgrounds,flora] [--force]

- sky:          sky_cloud_1..6, sky_sun, sky_moon (sprites; the renderer shades the moon to its phase)
- fx:           weather and ambient particles (fx_*)
- backgrounds:  bg_<theme>_far / bg_<theme>_near for every surface theme; drawn as parallax layers whose bottom
                edge sits on the surface line (UI/GraphicalSandbox.Sky.cs)
- flora:        every decoration listed by `dotnet run -- --flora-sprites` (tools/comfy/flora.tsv), described
                from the ground it grows on

Resumable: sheets and panoramas already in art/generated/ are skipped unless --force. Uses gen_sheet.py for
sprite sheets (art it replaces goes to art/replaced/). The server is restarted per job (see run_all.py).
"""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from comfy_client import Comfy  # noqa: E402
from run_all import restart_server  # noqa: E402
from sprite_post import remove_background  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "Assets" / "Sprites"
BATCHES = ROOT / "tools" / "comfy" / "batches_env"
GENERATED = ROOT / "art" / "generated"

SKY = [
    ["sky_cloud_1", "a large soft cumulus cloud, white with pale blue shading"],
    ["sky_cloud_2", "a long flat stratus cloud bank"],
    ["sky_cloud_3", "a small puffy cloud"],
    ["sky_cloud_4", "a wispy streaked cirrus cloud"],
    ["sky_cloud_5", "a tall billowing cloud with a flat base"],
    ["sky_cloud_6", "a pair of small drifting cloud puffs"],
    ["sky_sun", "a bright alien sun, a glowing white-gold disc with a soft corona"],
    ["sky_moon", "a large pale moon, full, with craters and a faint teal tint, round disc"],
]
FX = [
    ["fx_raindrop", "a single falling raindrop streak, pale blue"],
    ["fx_snowflake", "a single small snowflake crystal, white"],
    ["fx_ash", "a single drifting flake of grey ash"],
    ["fx_rad_mote", "a single glowing green radioactive mote"],
    ["fx_spore", "a single glowing teal spore particle"],
    ["fx_star", "a single small twinkling star sparkle"],
    ["fx_splash", "a small water splash"],
    ["fx_lightning", "a jagged vertical lightning bolt, white-violet"],
    ["fx_ember", "a single glowing green ember spark"],
    ["fx_leaf", "a single falling green leaf"],
]
THEMES = {
    "grassland": "rolling green hills with distant alien trees and colony antenna towers",
    "frozen": "snowy jagged mountains and ice spires under a cold sky",
    "junk": "scrap-metal dunes, half-buried wrecked starships and rusted towers",
    "canopy": "a dense towering alien jungle canopy with giant trees and hanging vines",
    "radiated": "a toxic wasteland with glowing green crystal spires and ruined reactors",
    "overgrowth": "a twisted magenta-purple overgrown forest of tangled roots and spore stalks",
    "ocean": "a calm ocean horizon with distant sea stacks and a far island",
    "fungal": "giant glowing teal mushrooms and spore clouds",
    "crystal": "prismatic crystal mountains glinting violet and blue",
    "core": "a deep irradiated cavern with glowing green uranium falls and ruined reactor pylons",
}
GROUND_THEMES = [
    (r"ice|snow|frost|slush", "frozen"), (r"dune|scrap|junk|salt", "junk desert"), (r"magma|ember|core|ash|uranium", "irradiated core"),
    (r"void|radiat|rift|acid|caustic", "toxic irradiated"), (r"overgrow|mite|root", "overgrown"), (r"spore|glowshroom|fungal|mycel", "glowing fungal"),
    (r"prism|pearl|crystal", "crystal"), (r"mud|canopy|vine|hive", "jungle canopy"), (r"shell|coral|sand", "coastal"),
    (r"precursor|alabaster|basalt|brick", "ancient ruin"),
]


def run_sheet(batch: dict, force: bool):
    if not force and (GENERATED / f"{batch['name']}.png").exists():
        print(f"skip {batch['name']} (done)", flush=True)
        return
    BATCHES.mkdir(parents=True, exist_ok=True)
    path = BATCHES / f"{batch['name']}.json"
    path.write_text(json.dumps(batch, indent=1), encoding="utf-8")
    restart_server()
    result = subprocess.run([sys.executable, str(ROOT / "tools" / "comfy" / "gen_sheet.py"), str(path)], capture_output=True, text=True)
    print(((result.stdout.strip().splitlines() or [""])[-1]) if result.returncode == 0 else result.stderr.strip()[-300:], flush=True)


def flora_batches():
    rows = [line.split("\t") for line in (ROOT / "tools" / "comfy" / "flora.tsv").read_text(encoding="utf-8").splitlines() if line]
    items = []
    for sprite, ground in rows:
        theme = next((name for pattern, name in GROUND_THEMES if re.search(pattern, ground.lower())), "temperate alien")
        label = re.sub(r"^(flora|deco)_", "", sprite).replace("_", " ")
        items.append([sprite, f"{label}: a {theme} plant or ground debris that grows on {ground}, standing upright on the ground"])
    return [{"name": f"flora_{i // 16 + 1}", "size": 40, "items": items[i:i + 16]} for i in range(0, len(items), 16)]


def run_backgrounds(force: bool):
    for theme, scene in THEMES.items():
        for layer in ("far", "near"):
            name = f"bg_{theme}_{layer}"
            raw = GENERATED / "env" / f"{name}.png"
            if not force and raw.exists():
                print(f"skip {name} (done)", flush=True)
                continue
            raw.parent.mkdir(parents=True, exist_ok=True)
            depth = ("distant, low-contrast, hazy silhouettes far away on the horizon" if layer == "far"
                     else "closer, darker, more detailed silhouettes in the middle distance")
            prompt = (f"A wide side-scrolling game parallax background layer, 16-bit pixel art: {scene}. {depth}. "
                      "The landscape fills the bottom half and its silhouette edge is clear; above it the sky is a flat solid "
                      "magenta (#FF00FF) everywhere with no clouds, no sun, no gradient. The left and right edges match so it "
                      "can repeat horizontally. Cohesive sci-fi Neo-Terra palette, no text, no characters.")
            restart_server()
            try:
                png = Comfy().generate(prompt, width=1024, height=512, steps=20)
            except Exception as error:
                print(f"{name}: FAILED {str(error)[:200]}", flush=True)
                continue
            raw.write_bytes(png)
            image = remove_background(Image.open(raw).convert("RGB"), tolerance=70)
            small = image.resize((512, 256), Image.BOX)
            alpha = small.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
            rgb = small.convert("RGB").quantize(colors=32, method=Image.Quantize.MEDIANCUT).convert("RGBA")
            rgb.putalpha(alpha)
            rgb.save(SPRITES / f"{name}.png")
            print(f"{name}: written", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default="sky,fx,backgrounds,flora")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    jobs = set(args.only.split(","))
    if "sky" in jobs:
        run_sheet({"name": "env_sky", "size": 96, "items": SKY}, args.force)
    if "fx" in jobs:
        run_sheet({"name": "env_fx", "size": 20, "items": FX}, args.force)
    if "backgrounds" in jobs:
        run_backgrounds(args.force)
    if "flora" in jobs:
        for batch in flora_batches():
            run_sheet(batch, args.force)


if __name__ == "__main__":
    main()
