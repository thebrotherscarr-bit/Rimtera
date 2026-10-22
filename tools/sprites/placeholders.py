"""Procedural placeholder sprites for sheets not generated yet (docs/ART_PROMPTS.md 11, 12, 13, 23).

Usage: python tools/sprites/placeholders.py [project_dir]

Placeholders are hue-shifted copies of existing generated sprites, so they share the art style.
They are written with a "ph_" marker in the preview only; file names follow the normal naming
convention, so real art dropped in later simply replaces them (re-run grid_sheets.py after this).
Existing files are never overwritten.
"""
import colorsys
import sys
from pathlib import Path

from PIL import Image


def hue_shift(image, target_hue, saturation=1.0, value=1.0):
    """Recolour saturated pixels to target_hue (0-1), keeping shading; greys stay grey."""
    image = image.convert("RGBA")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.18:
                h = target_hue
                s = min(1.0, s * saturation)
            v = min(1.0, v * value)
            nr, ng, nb = colorsys.hsv_to_rgb(h, s, v)
            pixels[x, y] = (int(nr * 255), int(ng * 255), int(nb * 255), a)
    return image


def tint(image, rgb, amount):
    """Blend toward a colour, even for grey metal (bars, ingots)."""
    image = image.convert("RGBA")
    overlay = Image.new("RGBA", image.size, rgb + (255,))
    mixed = Image.blend(image, overlay, amount)
    mixed.putalpha(image.getchannel("A"))
    return mixed


# name -> (source sprite, operation, args)
PLACEHOLDERS = {
    # Sheet 13: ores, bars, gems, materials
    "item_lumenite_ore": ("ore_blue", "tint", ((250, 240, 190), 0.55)),
    "item_voidsteel_ore": ("ore_blue", "tint", ((70, 30, 110), 0.55)),
    "item_astral_ore": ("ore_blue", "tint", ((40, 60, 200), 0.45)),
    "item_meteorite_ore": ("ore_red", "tint", ((90, 20, 20), 0.45)),
    "item_ferrite_bar": ("item_metal_ingots", "tint", ((160, 110, 80), 0.30)),
    "item_cobalt_bar": ("item_metal_ingots", "tint", ((50, 90, 210), 0.45)),
    "item_glacial_bar": ("item_metal_ingots", "tint", ((170, 225, 255), 0.45)),
    "item_pyrocite_bar": ("item_metal_ingots", "tint", ((230, 90, 30), 0.45)),
    "item_uranium_rod": ("item_metal_ingots", "tint", ((120, 230, 60), 0.50)),
    "item_viridium_bar": ("item_metal_ingots", "tint", ((40, 180, 90), 0.50)),
    "item_lumenite_bar": ("item_metal_ingots", "tint", ((255, 240, 170), 0.50)),
    "item_voidsteel_bar": ("item_metal_ingots", "tint", ((70, 30, 110), 0.55)),
    "item_astral_bar": ("item_metal_ingots", "tint", ((50, 60, 210), 0.55)),
    "item_ruby_shard": ("energy_crystals", "hue", (0.98,)),
    "item_sapphire_shard": ("energy_crystals", "hue", (0.62,)),
    "item_emerald_shard": ("energy_crystals", "hue", (0.36,)),
    "item_amethyst_shard": ("energy_crystals", "hue", (0.78,)),
    "item_topaz_shard": ("energy_crystals", "hue", (0.13,)),
    "item_diamond_shard": ("energy_crystals", "tint", ((240, 245, 255), 0.6)),
    "item_credits": ("tech_charge_cell_small", "tint", ((230, 190, 70), 0.55)),
    "item_power_cell": ("cmd_drone_battery", "hue", (0.55,)),
    "item_bio_gel": ("ammo_cryo_rounds", "hue", (0.33,)),
    # Sheet 12: consumables and summons
    "item_med_stim": ("tech_charge_cell_small", "hue", (0.0,)),
    "item_charge_stim": ("tech_charge_cell_small", "hue", (0.6,)),
    "item_speed_stim": ("tech_charge_cell_small", "hue", (0.15,)),
    "item_armor_stim": ("tech_charge_cell_small", "tint", ((150, 150, 160), 0.6)),
    "item_regen_patch": ("tech_overcharge_capacitor", "hue", (0.0,)),
    "item_spelunker_spray": ("ammo_fuel_canister", "hue", (0.13,)),
    "item_ration_bar": ("item_metal_ingots", "tint", ((170, 140, 90), 0.6)),
    "item_summon_swarm_pheromone": ("ammo_toxic_darts", "hue", (0.25,)),
    "item_summon_faulty_mining_chip": ("cmd_drone_spare_parts", "hue", (0.08,)),
    "item_summon_rotting_seed": ("cmd_spore_familiar", "hue", (0.28,)),
    "item_summon_scout_beacon": ("cmd_scout_drone_remote", "hue", (0.0,)),
    "item_summon_reactor_key": ("cmd_mech_companion_key", "hue", (0.33,)),
    "item_summon_signal_core": ("tech_focus_lens_crystal", "hue", (0.55,)),
    "item_summon_pirate_transmitter": ("cmd_swarm_controller", "hue", (0.0,)),
    "item_summon_astral_sigil": ("cmd_astral_guardian_core", "hue", (0.7,)),
    # Sheet 11: accessories
    "item_jet_boots": ("armor_cobalt_trooper_legs", "hue", (0.08,)),
    "item_energy_shield_emitter": ("tech_focus_lens_crystal", "hue", (0.5,)),
    "item_night_vision_goggles": ("cmd_command_visor", "hue", (0.33,)),
    "item_magnet_ring": ("tech_void_orb_focus", "hue", (0.0,)),
    "item_melee_module": ("tech_charge_cell_small", "hue", (0.0,)),
    "item_ballistics_module": ("tech_charge_cell_small", "hue", (0.33,)),
    "item_tech_module": ("tech_charge_cell_small", "hue", (0.6,)),
    "item_command_module": ("tech_charge_cell_small", "hue", (0.08,)),
    # Sheet 23: critters (recoloured variants of the generated critters)
    "critter_snow_hare": ("critter_terra_rat", "tint", ((235, 240, 250), 0.55)),
    "critter_scrap_rat": ("critter_terra_rat", "tint", ((120, 90, 60), 0.35)),
    "critter_ember_newt": ("critter_sand_skitterer", "hue", (0.03,)),
    "critter_glow_firefly": ("critter_sky_flier", "hue", (0.15,)),
    "critter_pebble_beetle": ("critter_scavenger_beetle", "tint", ((130, 130, 140), 0.5)),
    "critter_golden_critter": ("critter_terra_rat", "tint", ((240, 200, 60), 0.6)),
}


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    sprites = root / "Assets" / "Sprites"
    written, skipped = [], []
    for name, (source, operation, args) in PLACEHOLDERS.items():
        target = sprites / f"{name}.png"
        if target.exists():
            continue  # real art (or an earlier placeholder) already present
        path = sprites / f"{source}.png"
        if not path.exists():
            skipped.append(name)
            continue
        image = Image.open(path)
        image = hue_shift(image, *args) if operation == "hue" else tint(image, *args)
        image.save(target)
        written.append((name, image))

    if written:
        cell = 96
        columns = 12
        rows = (len(written) + columns - 1) // columns
        preview = Image.new("RGBA", (columns * cell, rows * cell), (255, 0, 255, 255))
        for i, (_, image) in enumerate(written):
            thumb = image.copy()
            thumb.thumbnail((cell - 6, cell - 6))
            preview.alpha_composite(thumb, ((i % columns) * cell + (cell - thumb.width) // 2, (i // columns) * cell + (cell - thumb.height) // 2))
        preview.save(sprites.parent / "_placeholder_preview.png")
    print(f"Wrote {len(written)} placeholders; missing sources: {skipped}")


if __name__ == "__main__":
    main()
