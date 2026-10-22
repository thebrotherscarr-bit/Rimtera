"""Kitbash art for the arsenal pass (docs/ARSENAL_SPRITES.txt).

Each listed item gets a base sprite chosen by its section (weapon style, tool, ammo family, armor line and
piece, accessory kind, wings, potion, food), with several alternatives per section picked by a hash of the
name, recoloured toward the material colour named in the list while keeping the base's shading.
Never overwrites existing art. Run after kitbash.py:

    python tools/sprites/arsenal.py <repo root>
"""
import re
import sys
import zlib
from pathlib import Path

from PIL import Image

SPRITES: Path

# Material colour words from the list -> RGB. Unknown words fall back to a hash colour.
COLOURS = {
    "rust": (160, 110, 85), "grey": (150, 150, 155), "scrap": (130, 120, 110), "iron": (130, 125, 120),
    "deep blue": (60, 100, 220), "blue": (70, 120, 230), "ice": (130, 220, 245), "cyan": (110, 220, 240),
    "molten": (245, 120, 50), "lava": (240, 100, 40), "orange": (245, 140, 60), "toxic": (120, 230, 70),
    "jungle": (60, 170, 70), "vine": (80, 170, 60), "leaf": (90, 190, 80), "green": (80, 190, 90),
    "violet": (150, 90, 220), "void": (110, 60, 190), "black": (60, 50, 70), "pearl": (235, 230, 245),
    "prism": (230, 190, 250), "prismatic": (220, 170, 255), "starfield": (245, 210, 110), "gold": (240, 200, 90),
    "solar": (255, 220, 90), "yellow": (240, 220, 80), "spore": (90, 150, 220), "charred": (150, 50, 50),
    "crimson": (190, 50, 60), "honey": (235, 170, 60), "amber": (230, 160, 60), "coral": (245, 130, 140),
    "pink": (240, 130, 190), "plasma": (240, 110, 220), "chrome": (200, 210, 225), "alien": (170, 230, 210),
    "storm": (90, 140, 210), "sea": (60, 170, 180), "teal": (60, 180, 170), "precursor": (190, 150, 90),
    "bronze": (190, 140, 80), "pirate": (190, 60, 50), "officer": (200, 60, 60), "red": (220, 70, 60),
    "fire": (240, 90, 40), "ember": (230, 80, 50), "hazard": (240, 170, 40), "mining": (230, 200, 70),
    "eclipse": (80, 60, 90), "circuit": (60, 190, 170), "electric": (90, 170, 255), "olive": (130, 140, 70),
    "khaki": (190, 170, 120), "brass": (200, 160, 80), "timber": (150, 100, 60), "brown": (140, 95, 60),
    "silk": (240, 240, 235), "white": (240, 240, 245), "lunar": (200, 205, 220), "silver": (200, 205, 215),
    "gunmetal": (110, 115, 125), "navy": (60, 70, 110), "basalt": (100, 100, 105), "abyss": (40, 70, 150),
    "mutant": (200, 60, 170), "magenta": (210, 60, 180), "spectral": (170, 140, 240), "signal": (230, 60, 60),
    "crystal": (140, 200, 255), "glow": (240, 250, 220), "feather": (245, 245, 240), "roast": (160, 100, 60),
    "earthy": (130, 100, 70), "melee": (220, 90, 70), "ranged": (90, 200, 120), "tech": (110, 170, 255),
    "command": (220, 180, 80), "chitin": (140, 100, 70), "frost": (225, 240, 250), "rusted": (180, 110, 70),
}

BASES = {
    "weapon/Broadsword": ["weapon_lumenite_greatsword", "weapon_astral_blade", "weapon_chainsaw_sword"],
    "weapon/Shortsword": ["weapon_astral_blade"],
    "weapon/Spear": ["weapon_shock_spear"],
    "weapon/Yoyo": ["weapon_yoyo_drone"],
    "weapon/Boomerang": ["weapon_return_boomerang", "weapon_throwing_disc"],
    "weapon/Flail": ["weapon_chain_flail", "weapon_mushroom_mace"],
    "weapon/Whip": ["weapon_tether_whip", "cmd_signal_whip_chain"],
    "weapon/Bow": ["weapon_lumenite_longbow", "weapon_glacial_crossbow"],
    "weapon/Gun": ["plasma_pistol", "laser_rifle", "weapon_scrap_pistol", "sniper_rifle"],
    "weapon/Shotgun": ["sniper_rifle", "laser_rifle"],
    "weapon/Launcher": ["rocket_launcher", "grenade_launcher"],
    "weapon/Thrown": ["weapon_throwing_disc", "weapon_dart_blower"],
    "weapon/Staff": ["tech_prism_lens_staff", "tech_coral_wand", "tech_crystal_shard_wand"],
    "weapon/Tome": ["tech_datapad_flare", "tech_datapad_chain_lightning", "tech_datapad_gravity_well"],
    "weapon/Beam": ["tech_rad_beam_rod", "tech_spark_emitter"],
    "weapon/SummonStaff": ["cmd_hornet_drone_remote", "cmd_ember_drone_remote", "cmd_frost_drone_remote", "cmd_repair_drone_remote"],
    "weapon/SentryStaff": ["cmd_turret_autogun", "cmd_turret_flame", "cmd_turret_mortar", "cmd_turret_tesla"],
    "tool/pick": ["tool_ferrite_drill", "item_power_pickaxe", "tool_cobalt_drill"],
    "tool/hatchet": ["weapon_chainsaw_sword"],
    "tool/breaker": ["item_power_pickaxe"],
    "ammo/Arrow": ["ammo_toxic_darts"],
    "ammo/Bullet": ["ammo_shotgun_shells"],
    "ammo/Rocket": ["ammo_mini_rocket"],
    "ammo/Dart": ["ammo_toxic_darts"],
    "ammo/Fuel": ["ammo_fuel_canister"],
    "ammo/Cell": ["ammo_fuel_canister"],
    "wings": ["sky_ray"],  # item_glider_wings on the sheet is mis-cut (a spider)
    "potion": ["item_med_stim", "item_speed_stim", "item_armor_stim", "item_charge_stim"],
    "food": ["item_ration_bar", "item_mushroom_stew", "nano_banana", "item_nano_banana_split"],
}

ACCESSORY_RULES = [
    (r"lamp|visor|goggle|lens|scope|sight", ["cmd_command_visor"]),
    (r"plate|mesh|shield|barrier|bulwark|aegis", ["item_energy_shield_emitter"]),
    (r"servo|boot|tread|stride|skate|runner", ["scout_boots", "item_jet_boots", "item_lava_wader_boots"]),
    (r"ring|band|loop", ["item_magnet_ring"]),
    (r"capacitor|battery|cell|bank|reactor|core", ["cmd_drone_battery"]),
    (r"canister|bottle|flask|tank", ["item_double_jump_canister"]),
    (r"chip|module|processor|uplink|link|relay", ["item_compass_chip"]),
    (r"gauntlet|glove|arm|grip|fist", ["item_power_pickaxe"]),
]
ACCESSORY_DEFAULT = ["item_lucky_charm", "item_compass_chip", "cmd_drone_battery", "item_energy_shield_emitter"]

# Armor art lines by role word: warden (close quarters), marksman (ballistics), sage (tech), handler (command).
ARMOR_ROLES = [
    (r"brawler|vanguard|striker|knight|revenant|sentinel|salvager", "warden"),
    (r"gunner|sharpshooter|gunslinger|deadeye|stalker|corsair|diver", "marksman"),
    (r"tinker|arcanist|cryomancer|mystic|adept|oracle|tidecaller", "sage"),
    (r"wrangler|conductor|swarmlord|marshal|overseer|paragon", "handler"),
]
ARMOR_PIECES = [
    (r"helm|mask|hood|visor|cap|crown|cowl|headgear", "helmet"),
    (r"greave|boot|tread|legging|pants|tasset|leg", "legs"),
]


def stable(name: str) -> int:
    return zlib.crc32(name.encode())


def colour_for(words: str, name: str):
    text = words.lower()
    # Two-word keys first ("deep blue"), then single words in order of appearance.
    for key in sorted(COLOURS, key=len, reverse=True):
        if " " in key and key in text:
            return COLOURS[key]
    for word in re.findall(r"[a-z]+", text):
        if word in COLOURS:
            return COLOURS[word]
    h = stable(name)
    return (80 + h % 160, 80 + (h >> 8) % 160, 80 + (h >> 16) % 160)


def tint(image: Image.Image, rgb, amount=0.6) -> Image.Image:
    image = image.convert("RGBA")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            lum = (r * 0.3 + g * 0.59 + b * 0.11) / 255
            sat = (max(r, g, b) - min(r, g, b)) / 255
            k = amount - sat * 0.3
            pixels[x, y] = tuple(max(0, min(255, int(c * (1 - k) + t * lum * 1.5 * k))) for c, t in zip((r, g, b), rgb)) + (a,)
    return image


def base_for(section: str, name: str):
    if section in BASES:
        options = BASES[section]
        return options[stable(name) % len(options)]
    if section == "accessory":
        for pattern, options in ACCESSORY_RULES:
            if re.search(pattern, name):
                return options[stable(name) % len(options)]
        return ACCESSORY_DEFAULT[stable(name) % len(ACCESSORY_DEFAULT)]
    if section.startswith("armor/"):
        role = next((line for pattern, line in ARMOR_ROLES if re.search(pattern, section)), "warden")
        piece = next((p for pattern, p in ARMOR_PIECES if re.search(pattern, name)), "chest")
        return f"armor_astral_{role}_{piece}"
    return None


def main():
    global SPRITES
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    SPRITES = root / "Assets" / "Sprites"
    section = ""
    written, missing = 0, []
    for line in (root / "docs" / "ARSENAL_SPRITES.txt").read_text(encoding="utf-8").splitlines():
        header = re.match(r"== (.+?) \(\d+\) ==", line)
        if header:
            section = header.group(1)
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) < 5 or not parts[0].startswith("T"):
            continue
        _, name, target, _, colour = parts[:5]
        out = SPRITES / f"{target}.png"
        if out.exists() or (SPRITES / f"{name}.png").exists():
            continue
        base = base_for(section, name)
        if base is None or not (SPRITES / f"{base}.png").exists():
            missing.append(f"{name} ({section} -> {base})")
            continue
        image = Image.open(SPRITES / f"{base}.png")
        image = tint(image, colour_for(colour, name))
        # Alternate facing for variety where it reads fine (not armor, whose pieces must line up).
        if not section.startswith("armor/") and stable(name) % 3 == 0:
            image = image.transpose(Image.FLIP_LEFT_RIGHT)
        image.save(out)
        written += 1
    print(f"Arsenal sprites: wrote {written}; missing bases: {missing[:10]}{' ...' if len(missing) > 10 else ''}")


if __name__ == "__main__":
    main()
