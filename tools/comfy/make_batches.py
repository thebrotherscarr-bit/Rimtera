"""Builds gen_sheet batch files (16 items each) from docs/ARSENAL_SPRITES.txt.

    python tools/comfy/make_batches.py [section-prefix ...]     e.g.  wings accessory weapon/Bow

Descriptions combine the item's display name, its material colour and a hint for its kind, so each sheet cell
is prompted as a recognisable object of the right type.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

HINTS = {
    "weapon/Broadsword": "a sci-fi broadsword, blade pointing up-right diagonally",
    "weapon/Shortsword": "a short sci-fi dagger, blade pointing up-right diagonally",
    "weapon/Spear": "a long sci-fi spear, pointing up-right diagonally",
    "weapon/Yoyo": "a round mechanical yo-yo disc with a short string",
    "weapon/Boomerang": "a curved throwing boomerang blade",
    "weapon/Flail": "a spiked flail ball on a short chain with handle",
    "weapon/Whip": "a coiled energy whip with handle",
    "weapon/Bow": "a sci-fi compound bow, vertical",
    "weapon/Gun": "a sci-fi blaster gun, side view facing right",
    "weapon/Shotgun": "a sci-fi shotgun, side view facing right",
    "weapon/Launcher": "a shoulder rocket launcher, side view facing right",
    "weapon/Thrown": "a throwing weapon (knife, star or grenade)",
    "weapon/Staff": "a tech staff with a glowing crystal head, diagonal",
    "weapon/Tome": "a glowing datapad tablet used as a spellbook",
    "weapon/Beam": "a beam emitter rod with a glowing tip, diagonal",
    "weapon/SummonStaff": "a drone control remote with antenna",
    "weapon/SentryStaff": "a folded turret deployment kit",
    "tool/pick": "a mining drill or pickaxe, diagonal",
    "tool/hatchet": "a cutting tool hatchet or saw, diagonal",
    "tool/breaker": "a heavy demolition hammer, diagonal",
    "ammo/Arrow": "a bundle of three bolts/arrows",
    "ammo/Bullet": "a small stack of bullets",
    "ammo/Rocket": "a small rocket shell",
    "ammo/Dart": "a bundle of darts",
    "ammo/Fuel": "a fuel canister",
    "ammo/Cell": "an energy cell battery",
    "accessory": "a small wearable gadget or charm icon",
    "wings": "a pair of mechanical or organic wings spread open, seen from behind",
    "potion": "a small glass vial or stim injector with coloured liquid",
    "food": "a food item or meal",
}


def rows():
    section = ""
    for line in (ROOT / "docs" / "ARSENAL_SPRITES.txt").read_text(encoding="utf-8").splitlines():
        header = re.match(r"== (.+?) \(\d+\) ==", line)
        if header:
            section = header.group(1)
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) >= 5 and parts[0].startswith("T"):
            yield section, parts[2], parts[3], parts[4]


def main():
    wanted = sys.argv[1:] or [""]
    out = ROOT / "tools" / "comfy" / "batches"
    out.mkdir(parents=True, exist_ok=True)
    by_section = {}
    for section, sprite, display, colour in rows():
        if not any(section.startswith(prefix) for prefix in wanted):
            continue
        hint = HINTS.get(section) or ("a sci-fi armor piece: " + section.split("/", 1)[-1] if section.startswith("armor/") else "a game item")
        colour_text = "" if colour in ("themed",) else f", {colour} colours"
        by_section.setdefault(section, []).append([sprite, f"{display}: {hint}{colour_text}."])
    written = 0
    for section, items in by_section.items():
        slug = re.sub(r"[^a-z0-9]+", "_", section.lower()).strip("_")
        for start in range(0, len(items), 16):
            name = f"{slug}_{start // 16 + 1}"
            size = 64 if section.startswith(("weapon/Spear", "weapon/Broadsword", "weapon/Bow", "weapon/Launcher", "wings")) else 48
            (out / f"{name}.json").write_text(json.dumps({"name": name, "size": size, "items": items[start:start + 16]}, indent=1), encoding="utf-8")
            written += 1
    print(f"wrote {written} batch files to {out}")


if __name__ == "__main__":
    main()
