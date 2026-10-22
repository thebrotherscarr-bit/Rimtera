"""Kitbash the art for sheets not generated yet by recolouring and combining existing sprites.

Usage: python tools/sprites/kitbash.py [project_dir]

Recipes are lists of steps applied to a base sprite:
  ("hue", h)                 shift saturated pixels to hue h (0-1)
  ("tint", (r,g,b), amount)  blend toward a colour (works on greys too)
  ("sat", factor) / ("val", factor)
  ("flip",)                  mirror horizontally
  ("scale", factor)          nearest-neighbour resize
  ("rot", degrees)
  ("over", sprite, (fx, fy), scale, [steps])   layer another sprite at a fractional offset
  ("glow", (r,g,b))          soft coloured outline
  ("tile",)                  crop to a centred square (for block textures)
Existing files are never overwritten, so real art always wins. Run after grid_sheets.py.
"""
import colorsys
import sys
from pathlib import Path

from PIL import Image, ImageFilter

SPRITES = Path()


def load(name):
    return Image.open(SPRITES / f"{name}.png").convert("RGBA")


def hue(image, h, sat=1.0):
    image = image.copy()
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.15:
                hh = h
                s = min(1.0, s * sat)
            nr, ng, nb = colorsys.hsv_to_rgb(hh, s, v)
            px[x, y] = (int(nr * 255), int(ng * 255), int(nb * 255), a)
    return image


def adjust(image, sat=1.0, val=1.0):
    image = image.copy()
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            nr, ng, nb = colorsys.hsv_to_rgb(h, min(1, s * sat), min(1, v * val))
            px[x, y] = (int(nr * 255), int(ng * 255), int(nb * 255), a)
    return image


def tint(image, rgb, amount):
    overlay = Image.new("RGBA", image.size, tuple(rgb) + (255,))
    mixed = Image.blend(image, overlay, amount)
    mixed.putalpha(image.getchannel("A"))
    return mixed


def glow(image, rgb):
    pad = max(4, image.width // 12)
    canvas = Image.new("RGBA", (image.width + pad * 2, image.height + pad * 2), (0, 0, 0, 0))
    alpha = Image.new("L", canvas.size, 0)
    alpha.paste(image.getchannel("A"), (pad, pad))
    halo = Image.new("RGBA", canvas.size, tuple(rgb) + (0,))
    halo.putalpha(alpha.filter(ImageFilter.GaussianBlur(pad / 2)).point(lambda v: int(v * 0.7)))
    canvas.alpha_composite(halo)
    canvas.alpha_composite(image, (pad, pad))
    return canvas


def apply(image, steps):
    for step in steps:
        op = step[0]
        if op == "hue":
            image = hue(image, step[1], step[2] if len(step) > 2 else 1.0)
        elif op == "tint":
            image = tint(image, step[1], step[2])
        elif op == "sat":
            image = adjust(image, sat=step[1])
        elif op == "val":
            image = adjust(image, val=step[1])
        elif op == "flip":
            image = image.transpose(Image.FLIP_LEFT_RIGHT)
        elif op == "scale":
            image = image.resize((max(1, int(image.width * step[1])), max(1, int(image.height * step[1]))), Image.NEAREST)
        elif op == "rot":
            image = image.rotate(step[1], resample=Image.NEAREST, expand=True)
        elif op == "glow":
            image = glow(image, step[1])
        elif op == "tile":
            side = min(image.size)
            left, top = (image.width - side) // 2, (image.height - side) // 2
            image = image.crop((left, top, left + side, top + side))
        elif op == "over":
            _, name, (fx, fy), factor, sub = step
            layer = apply(load(name), sub)
            layer = layer.resize((max(1, int(layer.width * factor)), max(1, int(layer.height * factor))), Image.NEAREST)
            canvas = image
            x = int(fx * image.width - layer.width / 2)
            y = int(fy * image.height - layer.height / 2)
            # Grow the canvas if the layer overhangs.
            left, top = min(0, x), min(0, y)
            right = max(image.width, x + layer.width)
            bottom = max(image.height, y + layer.height)
            if left < 0 or top < 0 or right > image.width or bottom > image.height:
                canvas = Image.new("RGBA", (right - left, bottom - top), (0, 0, 0, 0))
                canvas.alpha_composite(image, (-left, -top))
            canvas.alpha_composite(layer, (x - left, y - top))
            image = canvas
        else:
            raise ValueError(op)
    bbox = image.getbbox()
    return image.crop(bbox) if bbox else image


ACID = 0.27
CORAL = 0.96
FUNGAL = 0.5
CRYSTAL = 0.78
CANOPY = 0.33
GOLD = 0.12

RECIPES = {
    # ---- Sheet 15: Acid Coast ----
    "tile_acid_water": ("tile_acid_sand", [("tint", (60, 200, 40), 0.35), ("val", 0.9)]),
    "tile_shell_beach": ("tile_shell_sand", []),
    "flora_tube_coral": ("radiant_fungus", [("hue", CORAL)]),
    "flora_kelp_strand": ("mutant_vinelash", [("hue", 0.25), ("val", 0.85)]),
    "flora_salt_spire": ("frost_crystal", [("tint", (240, 240, 235), 0.5)]),
    "flora_shell_pile": ("snow_rocks_a", [("tint", (235, 215, 190), 0.55)]),
    "flora_tide_pool": ("snow_bank", [("tint", (70, 200, 80), 0.45)]),
    "flora_driftwood": ("charred_timber", [("tint", (190, 170, 140), 0.45)]),
    "critter_shell_crab": ("scavenger_crab", [("scale", 0.5), ("tint", (240, 210, 180), 0.35)]),
    "critter_glow_jelly": ("critter_sky_flier", [("hue", ACID), ("glow", (120, 255, 90))]),
    "critter_beach_skitter": ("critter_sand_skitterer", [("tint", (230, 210, 170), 0.3)]),
    "critter_sea_bird": ("sky_hawk", [("tint", (235, 240, 245), 0.5)]),
    "creature_acid_shark": ("magma_turtle", [("hue", ACID), ("flip",)]),
    "creature_tide_leech": ("rad_leech", [("hue", 0.45)]),
    "creature_reef_crawler": ("creature_acid_skitterer", [("hue", CORAL)]),
    "creature_brine_golem": ("glacial_golem", [("tint", (90, 200, 120), 0.35)]),
    "creature_spitting_barnacle": ("spore_rat", [("tint", (200, 190, 170), 0.5)]),
    "creature_storm_ray": ("sky_ray", [("hue", 0.6), ("glow", (120, 180, 255))]),
    "creature_drowned_diver": ("event_corrupted_survivor", [("over", "void_helmet", (0.5, 0.18), 0.55, [("hue", ACID)])]),
    "creature_coral_sentinel": ("security_droid", [("hue", CORAL)]),
    "struct_coastal_hab_pod": ("struct_colony_pod", [("tint", (120, 210, 160), 0.25)]),
    "struct_beacon_tower": ("struct_communications_array", [("glow", (140, 255, 120))]),
    # ---- Sheet 16: Fungal Hollows ----
    "tile_mycelium_stone": ("terrain_rock", [("tint", (90, 80, 130), 0.35)]),
    "tile_spore_block": ("terrain_fungal", []),
    "tile_mushroom_cap": ("tile_moss_violet", [("hue", 0.03), ("val", 1.1)]),
    "flora_giant_glowshroom": ("radiant_fungus", [("hue", FUNGAL), ("scale", 1.4), ("glow", (90, 220, 230))]),
    "flora_shelf_fungus": ("magma_lichen", [("hue", 0.08), ("tint", (200, 170, 130), 0.3)]),
    "flora_spore_puffball": ("spore_small", [("hue", 0.83)]),
    "flora_glow_vine": ("mutant_vinelash", [("hue", FUNGAL), ("glow", (90, 220, 230))]),
    "flora_mycelium_tendrils": ("tundra_lichen_b", [("tint", (210, 200, 230), 0.5)]),
    "flora_biolum_moss": ("luminous_spores", [("hue", 0.52)]),
    "critter_glow_snail": ("critter_glow_grub", [("hue", FUNGAL), ("scale", 0.7)]),
    "critter_spore_moth": ("critter_sky_flier", [("hue", 0.83)]),
    "critter_cave_frog": ("ember_rat", [("hue", 0.3), ("scale", 0.6)]),
    "critter_truffle_mite": ("scrap_mite", [("tint", (120, 80, 60), 0.4)]),
    "creature_spore_zombie": ("event_corrupted_survivor", [("over", "spore_small", (0.45, 0.12), 0.45, [("hue", FUNGAL)])]),
    "creature_fungal_crawler": ("rad_skitter", [("hue", FUNGAL)]),
    "creature_puff_drifter": ("spore_tall", [("hue", 0.83), ("glow", (220, 140, 255))]),
    "creature_mycelid_shaman": ("event_commander", [("hue", FUNGAL)]),
    "creature_shroom_stalker": ("glow_stalker", [("hue", FUNGAL)]),
    "creature_cap_turtle": ("magma_turtle", [("over", "radiant_fungus", (0.45, 0.25), 0.5, [("hue", 0.02)])]),
    "creature_lantern_bat": ("geiger_bug", [("hue", 0.13), ("glow", (255, 210, 110))]),
    "creature_spore_golem": ("basalt_golem", [("hue", FUNGAL), ("tint", (60, 70, 110), 0.25)]),
    "boss_spore_monarch": ("mutated_flora", [("hue", FUNGAL), ("scale", 1.3), ("glow", (100, 230, 240))]),
    "struct_fungal_hab_ruin": ("hab_corrupted_b", [("hue", FUNGAL)]),
    # ---- Sheet 17: Crystal Spires ----
    "tile_crystal_spire": ("tile_crystal_block", [("tile",)]),
    "tile_pearl_sand": ("terrain_sand", [("tint", (235, 220, 240), 0.5)]),
    "flora_crystal_tree": ("astro_timber", [("over", "frost_crystal", (0.5, 0.15), 1.0, [("hue", CRYSTAL)])]),
    "flora_prism_flower": ("crystalline_flora", [("hue", 0.88)]),
    "flora_light_reed": ("molten_reeds", [("tint", (240, 240, 255), 0.55), ("glow", (220, 230, 255))]),
    "flora_singing_crystal": ("energy_crystals", [("hue", CRYSTAL), ("glow", (210, 170, 255))]),
    "flora_halo_bush": ("fern_plant", [("tint", (250, 235, 190), 0.55)]),
    "flora_pearl_shell_plant": ("saxifrage_plant", [("tint", (240, 220, 245), 0.5)]),
    "critter_prism_butterfly": ("critter_glow_firefly", [("hue", 0.88)]),
    "critter_light_wisp_bird": ("sky_hawk", [("tint", (255, 250, 230), 0.6), ("glow", (255, 240, 200))]),
    "critter_crystal_bunny": ("critter_snow_hare", [("hue", CRYSTAL)]),
    "critter_halo_snail": ("critter_glow_grub", [("tint", (250, 235, 190), 0.5)]),
    "creature_prism_knight": ("event_brute", [("tint", (235, 230, 255), 0.55)]),
    "creature_shard_unicorn": ("neo_terra_deer", [("tint", (225, 215, 255), 0.5), ("over", "frost_crystal", (0.82, 0.08), 0.35, [("hue", CRYSTAL)])]),
    "creature_light_mimic": ("struct_storage_container", [("tint", (240, 235, 255), 0.5), ("glow", (230, 200, 255))]),
    "creature_crystal_wraith": ("void_stalker", [("hue", CRYSTAL), ("tint", (230, 220, 255), 0.3)]),
    "creature_refractor_turret": ("sentry_tesla", [("hue", CRYSTAL)]),
    "creature_halo_bat": ("geiger_bug", [("tint", (255, 245, 220), 0.6)]),
    "creature_pearl_golem": ("glacial_golem", [("tint", (240, 225, 245), 0.5)]),
    "creature_spire_dragonfly": ("critter_sky_flier", [("hue", CRYSTAL), ("scale", 1.4)]),
    "boss_prism_empress": ("solar_flare_entity", [("hue", CRYSTAL), ("scale", 1.3), ("glow", (230, 190, 255))]),
    "struct_crystal_altar": ("caustic_rock", [("hue", CRYSTAL), ("glow", (210, 170, 255))]),
    # ---- Sheet 18: Verdant Canopy / Bloom Hives ----
    "tile_canopy_grass": ("tile_grassland", [("sat", 1.3), ("val", 0.85)]),
    "flora_canopy_tree": ("astro_timber", [("over", "fern_plant", (0.5, 0.05), 1.6, [("sat", 1.3)])]),
    "flora_giant_leaf": ("fern_plant", [("scale", 1.6), ("sat", 1.2)]),
    "flora_pitcher_plant": ("radiant_flora", [("hue", 0.95)]),
    "flora_thorn_bush": ("mutant_vinelash", [("hue", 0.22), ("val", 0.75)]),
    "flora_bloom_flower": ("crystalline_flora", [("hue", 0.92)]),
    "flora_hanging_vines": ("mutant_vinelash", [("hue", CANOPY)]),
    "critter_canopy_parrot": ("sky_hawk", [("hue", 0.0), ("sat", 1.6)]),
    "critter_leaf_frog": ("critter_ember_newt", [("hue", CANOPY)]),
    "critter_pollen_bee": ("critter_glow_firefly", [("hue", GOLD), ("scale", 0.8)]),
    "critter_vine_snake": ("lava_leech", [("hue", CANOPY)]),
    "creature_pollen_drone_swarm": ("minion_rad_swarm", [("hue", GOLD)]),
    "creature_thorn_lurker": ("glow_stalker", [("hue", CANOPY), ("val", 0.8)]),
    "creature_canopy_ape": ("snow_wolf", [("hue", 0.08), ("tint", (90, 120, 60), 0.2)]),
    "creature_snapjaw_plant": ("plant_overgrowth", [("scale", 0.6)]),
    "creature_hive_guard": ("event_grunt", [("hue", GOLD)]),
    "creature_mud_crawler": ("rock_mite", [("tint", (110, 80, 50), 0.45)]),
    "creature_spitting_orchid": ("radiant_flora", [("hue", 0.85), ("scale", 1.3)]),
    "creature_jungle_stalker": ("void_stalker", [("hue", CANOPY)]),
    "boss_bloom_matriarch": ("struct_bloom_hive", [("over", "alpha_skitter", (0.5, 0.75), 0.9, [("hue", GOLD)]), ("scale", 1.4)]),
    "struct_precursor_vault_door": ("wallart_bulkhead_door", [("tint", (220, 180, 70), 0.4), ("glow", (120, 255, 170))]),
    # ---- Sheet 19: Precursor Vault & mini-biomes ----
    "tile_precursor_brick": ("tile_ferrite_brick", [("tint", (200, 170, 80), 0.45)]),
    "tile_precursor_glyph": ("tile_circuit_panel", [("hue", 0.4), ("tint", (190, 160, 70), 0.3)]),
    "tile_geode_cyan": ("tile_crystal_block", [("hue", 0.5), ("tile",)]),
    "tile_geode_magenta": ("tile_crystal_block", [("hue", 0.88), ("tile",)]),
    "deco_precursor_statue": ("event_commander", [("tint", (200, 170, 80), 0.75)]),
    "deco_glyph_tablet": ("furn_display_panel", [("tint", (190, 160, 70), 0.55)]),
    "deco_energy_pylon": ("furn_floor_lamp", [("hue", 0.4), ("glow", (120, 255, 170))]),
    "deco_geode_cluster": ("energy_crystals", [("hue", 0.88)]),
    "deco_mite_silk_cocoon": ("spore_tall", [("tint", (230, 225, 220), 0.65)]),
    "creature_vault_sentinel": ("security_droid", [("tint", (200, 170, 80), 0.5)]),
    "creature_glyph_drone": ("minion_scout_drone", [("tint", (200, 170, 80), 0.4), ("glow", (120, 255, 170))]),
    "creature_ancient_guardian": ("green_mech", [("tint", (200, 170, 80), 0.5)]),
    "creature_geode_crab": ("scavenger_crab", [("over", "energy_crystals", (0.5, 0.15), 0.5, [("hue", 0.88)])]),
    "creature_alabaster_hoplite": ("event_brute", [("tint", (240, 238, 232), 0.65)]),
    "creature_mite_broodmother": ("alpha_skitter", [("tint", (200, 195, 185), 0.45)]),
    "creature_silk_spider_mite": ("scrap_mite", [("tint", (220, 215, 205), 0.5)]),
    "boss_vault_colossus": ("asteroid_miner_mech", [("tint", (200, 170, 80), 0.5), ("glow", (120, 255, 170))]),
    "item_vault_key": ("cmd_mech_companion_key", [("tint", (220, 180, 70), 0.55)]),
    "item_ancient_power_cell": ("cmd_drone_battery", [("tint", (200, 170, 80), 0.4)]),
    "item_precursor_relic": ("item_summon_signal_core", [("tint", (220, 180, 70), 0.5)]),
    # ---- Sheet 20/21: more event enemies ----
    "event_husk": ("event_corrupted_survivor", [("tint", (150, 70, 50), 0.35)]),
    "event_rust_wraith": ("void_stalker", [("hue", 0.03)]),
    "event_rust_hound": ("snow_wolf", [("tint", (160, 70, 40), 0.45)]),
    "event_eye_drone": ("minion_ember_drone", [("scale", 1.2)]),
    "event_corroded_mech": ("scout_mech", [("tint", (150, 80, 40), 0.4)]),
    "boss_moon_eater": ("overseer_mech", [("hue", 0.0), ("scale", 1.3), ("glow", (255, 60, 40))]),
    "event_swarm_queen": ("alpha_skitter", [("hue", 0.25)]),
    "event_dust_devil": ("event_energy_vortex", [("tint", (200, 160, 100), 0.6)]),
    "event_sand_ray": ("sky_ray", [("tint", (210, 180, 120), 0.5)]),
    "event_scrap_elemental": ("basalt_golem", [("tint", (140, 110, 90), 0.4)]),
    "event_dust_raider": ("event_marksman", [("tint", (190, 150, 100), 0.4)]),
    "event_acid_slime": ("swarm_bug", [("hue", ACID), ("scale", 1.3)]),
    "event_acid_bird": ("sky_hawk", [("hue", ACID)]),
    "event_toxic_cloud": ("event_energy_vortex", [("hue", ACID)]),
    "event_aurora_wisp": ("minion_lumenite_sprite", [("hue", 0.45), ("glow", (120, 255, 220))]),
    "event_festival_balloon_drone": ("minion_scout_drone", [("hue", 0.9), ("sat", 1.4)]),
    "event_meteor_head": ("tech_meteor_caller", [("scale", 1.3)]),
    "event_meteor_crawler": ("magma_turtle_small", [("tint", (80, 30, 30), 0.4)]),
    "event_eclipse_shade": ("glow_stalker", [("tint", (30, 20, 40), 0.55)]),
    "event_eclipse_reaper": ("event_commander", [("tint", (30, 20, 40), 0.5), ("glow", (180, 60, 255))]),
    "event_eclipse_moth_mech": ("cryopterous", [("tint", (40, 30, 60), 0.4), ("scale", 1.5)]),
    "boss_eclipse_lord": ("renegade_overseer", [("tint", (30, 20, 50), 0.5), ("glow", (200, 80, 255))]),
    "event_scavenger_grunt": ("event_grunt", [("tint", (150, 120, 80), 0.35)]),
    "event_scavenger_brute": ("event_brute", [("tint", (150, 120, 80), 0.35)]),
    "event_scavenger_sniper": ("event_marksman", [("tint", (150, 120, 80), 0.3)]),
    "event_scavenger_rigger": ("npc_base_engineer", [("tint", (90, 60, 40), 0.35)]),
    "boss_scavenger_warlord": ("cyber_pirate_captain", [("tint", (150, 110, 60), 0.35), ("scale", 1.2)]),
    "event_pirate_deckhand": ("event_grunt", [("hue", 0.0)]),
    "event_pirate_gunner": ("event_marksman", [("hue", 0.0)]),
    "event_pirate_parrot_drone": ("critter_sky_flier", [("hue", 0.0), ("sat", 1.5)]),
    "event_pirate_cannon": ("sentry_mortar", [("tint", (120, 40, 40), 0.35)]),
    "boss_pirate_dreadnought": ("warship_core", [("tint", (140, 40, 40), 0.35)]),
    "event_protocol_grunt": ("security_droid", [("hue", 0.6)]),
    "event_protocol_shieldbearer": ("event_brute", [("hue", 0.6)]),
    "event_protocol_wyvern": ("cryopterous", [("hue", 0.6)]),
    "event_protocol_ogre": ("asteroid_miner_mech", [("hue", 0.6)]),
    "event_protocol_mage": ("event_commander", [("hue", 0.6)]),
    "boss_protocol_dragon": ("cryopterous", [("hue", 0.62), ("scale", 2.0), ("glow", (90, 160, 255))]),
    "event_precursor_probe": ("creature_terraformer_drone", [("tint", (200, 170, 80), 0.3)]),
    "event_probe_gunner": ("event_drone", [("tint", (200, 170, 80), 0.3)]),
    "event_probe_tesla_walker": ("sentry_tesla", [("tint", (200, 170, 80), 0.35)]),
    "event_probe_brain_drone": ("event_void_siphon", [("hue", 0.4)]),
    "boss_mothership_core": ("warship_core", [("tint", (200, 170, 80), 0.35), ("glow", (120, 255, 170))]),
    # ---- Sheet 22: more town NPCs and bound survivors ----
    "npc_demolitionist": ("npc_colony_worker", [("hue", 0.03)]),
    "npc_tailor": ("npc_store_clerk", [("hue", 0.85)]),
    "npc_cartographer": ("npc_xeno_biologist", [("hue", 0.1)]),
    "npc_mechanic": ("npc_base_engineer", [("hue", 0.58)]),
    "npc_pilot": ("security_commander", [("hue", 0.6)]),
    "npc_cook": ("npc_store_clerk", [("tint", (240, 240, 240), 0.35)]),
    "npc_arborist": ("astro_botanist", []),
    "npc_archivist": ("head_researcher", [("hue", 0.75)]),
    "npc_bound_survivor": ("colonist_front", [("over", "tool_rope_coil", (0.5, 0.55), 0.55, [("hue", 0.52), ("glow", (90, 230, 255))])]),
    # ---- Sheet 23: remaining critters ----
    "critter_hop_bunny": ("critter_snow_hare", [("tint", (170, 130, 90), 0.4)]),
    "critter_grass_snake": ("lava_leech", [("hue", 0.3)]),
    "critter_penguin_drone": ("minion_scout_drone", [("tint", (30, 30, 40), 0.35)]),
    "critter_dune_lizard": ("critter_ember_newt", [("hue", 0.1)]),
    "critter_worm": ("lava_leech", [("tint", (200, 130, 120), 0.5), ("scale", 0.6)]),
    "critter_butterfly_blue": ("critter_glow_firefly", [("hue", 0.6), ("scale", 0.8)]),
    "critter_butterfly_gold": ("critter_glow_firefly", [("hue", GOLD), ("scale", 0.8)]),
    "critter_dragonfly": ("critter_sky_flier", [("hue", 0.4)]),
    "critter_squirrel_bot": ("critter_terra_rat", [("tint", (160, 160, 170), 0.45)]),
    "critter_owl": ("sky_hawk", [("tint", (120, 90, 60), 0.4)]),
    "critter_duck": ("sky_hawk", [("hue", 0.33), ("scale", 0.8)]),
    "critter_fish_silver": ("lava_leech", [("tint", (200, 205, 215), 0.6)]),
    "critter_fish_glow": ("lava_leech", [("hue", 0.5), ("glow", (90, 230, 255))]),
    "critter_crab_small": ("scavenger_crab", [("scale", 0.45)]),
    "critter_snail": ("critter_glow_grub", [("tint", (170, 140, 100), 0.5), ("scale", 0.7)]),
    "critter_ladybug_bot": ("critter_scavenger_beetle", [("hue", 0.0), ("scale", 0.7)]),
    "critter_frog": ("critter_ember_newt", [("hue", 0.33)]),
    # ---- Sheet 11/12: remaining accessories and consumables ----
    "item_rocket_pack": ("mining_pack", [("hue", 0.03)]),
    "item_glider_wings": ("cryopterous", [("tint", (180, 190, 200), 0.5), ("scale", 0.6)]),
    "item_grapple_hook": ("tool_rope_coil", [("over", "tool_cobalt_cutter", (0.8, 0.2), 0.45, [])]),
    "item_double_jump_canister": ("ammo_fuel_canister", [("hue", 0.55)]),
    "item_climbing_claws": ("swarm_gloves", [("hue", 0.08)]),
    "item_life_monitor": ("item_magnet_ring", [("hue", 0.0)]),
    "item_ore_scanner": ("item_drone_controller", [("hue", 0.13)]),
    "item_depth_meter": ("tech_focus_lens_crystal", [("hue", 0.1)]),
    "item_compass_chip": ("item_tech_module", [("hue", 0.15)]),
    "item_regen_implant": ("item_regen_patch", [("hue", 0.33)]),
    "item_rebreather_mask": ("void_helmet", [("hue", 0.5)]),
    "item_lava_wader_boots": ("scout_boots", [("hue", 0.04)]),
    "item_frost_grip_gloves": ("swarm_gloves", [("hue", 0.55)]),
    "item_lucky_charm": ("item_topaz_shard", [("glow", (255, 220, 120))]),
    "item_thorn_plating": ("armor_pyrocite_bulwark_chest", [("hue", 0.3)]),
    "item_astral_halo": ("cmd_astral_guardian_core", [("glow", (160, 180, 255))]),
    "item_night_owl_drops": ("tech_charge_cell_small", [("hue", 0.75)]),
    "item_hunter_lens": ("tech_focus_lens_crystal", [("hue", 0.02)]),
    "item_featherfall_pill": ("item_med_stim", [("tint", (240, 240, 250), 0.6)]),
    "item_water_breath_capsule": ("item_med_stim", [("hue", 0.55)]),
    "item_snow_wolf_steak": ("item_ration_bar", [("hue", 0.02)]),
    "item_mushroom_stew": ("station_cooking_station", [("scale", 0.6), ("hue", 0.08)]),
    "item_nectar_tea": ("tool_bucket_water", [("hue", GOLD), ("scale", 0.6)]),
    "item_coral_sushi": ("item_ration_bar", [("hue", CORAL)]),
    "item_nano_banana_split": ("nano_banana", [("over", "item_bio_gel", (0.5, 0.3), 0.5, [("hue", 0.95)])]),
    "item_cloth_roll": ("tool_rope_coil", [("tint", (220, 210, 190), 0.6)]),
    "item_circuit_board": ("tile_circuit_panel", [("scale", 0.5)]),
    # ---- Sheet 14: remaining tools ----
    "tool_viridium_drill": ("tool_cobalt_drill", [("hue", 0.38)]),
    "tool_lumenite_drill": ("tool_cobalt_drill", [("tint", (250, 240, 190), 0.55)]),
    "tool_voidsteel_drill": ("tool_cobalt_drill", [("hue", 0.78), ("val", 0.7)]),
    "tool_astral_drill": ("tool_cobalt_drill", [("hue", 0.65), ("glow", (120, 150, 255))]),
    "tool_viridium_cutter": ("tool_cobalt_cutter", [("hue", 0.38)]),
    "tool_lumenite_cutter": ("tool_cobalt_cutter", [("tint", (250, 240, 190), 0.55)]),
    "tool_ferrite_demolisher": ("weapon_pyrocite_maul", [("tint", (150, 120, 100), 0.6)]),
    "tool_cobalt_demolisher": ("weapon_pyrocite_maul", [("hue", 0.62)]),
    "tool_viridium_demolisher": ("weapon_pyrocite_maul", [("hue", 0.38)]),
    "tool_lumenite_demolisher": ("weapon_pyrocite_maul", [("tint", (250, 240, 190), 0.55)]),
    "tool_glow_sticks": ("ammo_toxic_darts", [("hue", 0.45), ("glow", (120, 255, 200))]),
    "tool_teleport_beacon": ("station_teleporter_pad", [("scale", 0.6)]),
}


# ---- Generated recipes for content without sheet art (armor tiers, tools, fishing, summons) ----
TIER_TINT = {"glacial": (170, 225, 255), "viridium": (40, 180, 90), "lumenite": (250, 240, 190),
             "voidsteel": (70, 30, 110), "astral": (50, 60, 210), "cobalt": (50, 90, 210), "ferrite": (150, 110, 80)}
CLASS_BASE = {"warden": "armor_pyrocite_bulwark", "marksman": "armor_viridium_stalker",
              "sage": "armor_uranium_hazmat", "handler": "armor_cobalt_trooper"}
for _tier in ("glacial", "viridium", "lumenite", "voidsteel", "astral"):
    for _cls, _base in CLASS_BASE.items():
        for _piece in ("helmet", "chest", "legs"):
            RECIPES.setdefault(f"armor_{_tier}_{_cls}_{_piece}", (f"{_base}_{_piece}", [("tint", TIER_TINT[_tier], 0.5)]))
for _tier in ("glacial", "voidsteel", "astral"):
    RECIPES.setdefault(f"tool_{_tier}_cutter", ("tool_cobalt_cutter", [("tint", TIER_TINT[_tier], 0.5)]))
    RECIPES.setdefault(f"tool_{_tier}_demolisher", ("tool_cobalt_demolisher", [("tint", TIER_TINT[_tier], 0.5)]))
for _tier in ("ferrite", "cobalt", "glacial", "viridium", "lumenite", "voidsteel", "astral"):
    RECIPES.setdefault(f"tool_{_tier}_fishing_rod", ("tool_fishing_rod", [("tint", TIER_TINT[_tier], 0.45)]))

_EXTRA = {
    "tool_acid_fishing_rod": ("tool_fishing_rod", [("hue", ACID)]),
    "tool_magma_fishing_rod": ("tool_fishing_rod", [("hue", 0.05), ("glow", (255, 120, 40))]),
    "item_fishing_line": ("tool_rope_coil", [("tint", (235, 235, 240), 0.6)]),
    "item_fishing_lure": ("tech_focus_lens_crystal", [("hue", GOLD)]),
    "item_acid_wader": ("item_lava_wader_boots", [("hue", ACID)]),
    "item_lava_lure": ("tech_focus_lens_crystal", [("hue", 0.04), ("glow", (255, 120, 40))]),
    "item_catch_scanner": ("item_ore_scanner", [("hue", 0.55)]),
    "item_abyss_bait": ("critter_worm", [("hue", 0.78), ("glow", (170, 90, 255))]),
    "item_frost_cod": ("critter_fish_silver", [("tint", (200, 230, 255), 0.45)]),
    "item_ember_koi": ("critter_fish_glow", [("hue", 0.05)]),
    "item_acid_eel": ("critter_grass_snake", [("hue", ACID)]),
    "item_reef_snapper": ("critter_fish_silver", [("hue", CORAL), ("sat", 2.0)]),
    "item_nectar_minnow": ("critter_fish_glow", [("hue", GOLD), ("scale", 0.75)]),
    "item_blight_carp": ("critter_fish_silver", [("tint", (130, 60, 160), 0.45)]),
    "item_prism_trout": ("critter_fish_glow", [("hue", 0.85)]),
    "item_spore_guppy": ("critter_fish_glow", [("hue", FUNGAL), ("scale", 0.7)]),
    "item_mud_skipper": ("critter_dune_lizard", [("tint", (110, 80, 50), 0.4)]),
    "item_cave_blindfish": ("critter_fish_silver", [("tint", (240, 235, 235), 0.6)]),
    "item_cooked_fish": ("critter_fish_silver", [("tint", (190, 120, 60), 0.55)]),
    "item_cooked_eel": ("critter_grass_snake", [("tint", (180, 110, 60), 0.55)]),
    "item_grilled_koi": ("critter_fish_glow", [("tint", (200, 110, 50), 0.55)]),
    "item_fish_fillet": ("item_ration_bar", [("tint", (240, 160, 150), 0.5)]),
    "item_rusted_can": ("item_power_cell", [("tint", (150, 80, 40), 0.6)]),
    "item_tangled_wire": ("tool_wire_tool", [("tint", (130, 130, 130), 0.5)]),
    "item_soggy_datapad": ("tech_datapad_gravity_well", [("tint", (90, 110, 90), 0.5)]),
    "item_spiny_rustback": ("critter_fish_silver", [("tint", (170, 80, 40), 0.5)]),
    "item_lantern_eel": ("critter_grass_snake", [("hue", 0.13), ("glow", (255, 220, 120))]),
    "item_glass_pike": ("critter_fish_silver", [("tint", (220, 240, 255), 0.6)]),
    "item_twin_tail_snapper": ("critter_fish_glow", [("hue", 0.95)]),
    "item_mottled_grouper": ("critter_fish_silver", [("tint", (120, 100, 70), 0.5), ("scale", 1.3)]),
    "item_hexscale": ("critter_fish_glow", [("hue", 0.75)]),
    "item_bubble_ray": ("creature_storm_ray", [("hue", 0.5), ("scale", 0.6)]),
    "item_crowned_tetra": ("critter_fish_glow", [("hue", GOLD), ("glow", (255, 220, 120))]),
    "item_void_angler": ("critter_fish_glow", [("hue", 0.78), ("val", 0.6)]),
    "item_starfall_minnow": ("critter_fish_glow", [("tint", (240, 240, 255), 0.5), ("glow", (230, 230, 255))]),
    "item_scrap_crate": ("furn_storage_crate", [("tint", (150, 110, 70), 0.45)]),
    "item_alloy_crate": ("furn_storage_crate", [("tint", (170, 175, 190), 0.35)]),
    "item_astral_crate": ("furn_storage_crate", [("tint", (50, 60, 210), 0.45)]),
    "item_coast_crate": ("furn_storage_crate", [("tint", (90, 220, 60), 0.4)]),
    "item_frozen_crate": ("furn_storage_crate", [("tint", (190, 230, 255), 0.45)]),
    "item_junk_crate": ("furn_storage_crate", [("tint", (140, 80, 40), 0.45)]),
    "item_fungal_crate": ("furn_storage_crate", [("tint", (90, 220, 230), 0.4)]),
    "item_canopy_crate": ("furn_storage_crate", [("tint", (60, 150, 60), 0.45)]),
    "item_crystal_crate": ("furn_storage_crate", [("tint", (225, 200, 255), 0.45)]),
    "item_blight_crate": ("furn_storage_crate", [("tint", (130, 60, 160), 0.45)]),
    "item_molten_crate": ("furn_storage_crate", [("tint", (230, 90, 30), 0.45)]),
    "item_meteorite_bar": ("item_ferrite_bar", [("tint", (90, 30, 30), 0.5)]),
    "item_overseer_circuit": ("item_circuit_board", [("hue", 0.0)]),
    "item_heart_fragment": ("item_ruby_shard", [("glow", (255, 90, 120))]),
    "item_silk_thread": ("item_cloth_roll", [("tint", (245, 245, 250), 0.6)]),
    "item_coral_shard": ("item_ruby_shard", [("tint", (250, 150, 140), 0.5)]),
    "item_glow_spore": ("luminous_spores", [("scale", 0.6)]),
    "item_prism_shard": ("item_diamond_shard", [("hue", 0.85)]),
    "item_void_essence": ("tech_void_orb_focus", [("scale", 0.7)]),
    "item_tide_fish": ("critter_fish_silver", [("hue", 0.5)]),
    "item_empty_bucket": ("tool_bucket_water", [("tint", (150, 150, 160), 0.6)]),
    "weapon_astral_repeater": ("weapon_lumenite_longbow", [("hue", 0.65)]),
    "item_swarm_resonator": ("item_summon_swarm_pheromone", [("glow", (160, 255, 120))]),
    "item_renegade_credentials": ("cmd_mech_companion_key", [("hue", 0.0)]),
    "item_mutagen_bloom": ("flora_bloom_flower", [("hue", 0.3)]),
    "item_tainted_tissue": ("item_bio_gel", [("hue", 0.92)]),
    "item_warship_transponder": ("cmd_swarm_controller", [("hue", 0.6)]),
    "item_sunspot_lens": ("tech_focus_lens_crystal", [("hue", 0.1), ("glow", (255, 210, 90))]),
    "item_void_lure": ("tech_void_orb_focus", [("glow", (170, 90, 255))]),
    "item_claim_marker": ("furn_sign_post", [("tint", (220, 180, 70), 0.3)]),
    "item_bounty_notice": ("furn_display_panel", [("tint", (220, 200, 150), 0.6)]),
    "item_spore_crown": ("flora_spore_puffball", [("glow", (90, 220, 230))]),
    "item_royal_nectar": ("tool_bucket_water", [("hue", GOLD)]),
    "item_scrap_beacon": ("struct_beacon_tower", [("tint", (150, 90, 50), 0.45)]),
    "item_festival_flare": ("ammo_fuel_canister", [("hue", 0.9), ("glow", (255, 140, 220))]),
    "item_prism_catalyst": ("item_diamond_shard", [("hue", 0.88), ("glow", (240, 170, 255))]),
    "item_vault_core": ("item_ancient_power_cell", [("glow", (120, 255, 170))]),
    "item_moon_eater_lure": ("item_summon_scout_beacon", [("hue", 0.0), ("glow", (255, 60, 40))]),
    "item_eclipse_orb": ("tech_void_orb_focus", [("tint", (30, 20, 50), 0.4), ("glow", (200, 80, 255))]),
    "item_warlord_banner": ("item_invasion_banner", []),
    "item_dreadnought_flag": ("item_invasion_banner", [("hue", 0.0)]),
    "item_protocol_key": ("item_vault_key", [("hue", 0.6)]),
    "item_mothership_beacon": ("item_summon_signal_core", [("hue", 0.8)]),
    "creature_bloodfin_ambusher": ("creature_acid_shark", [("hue", 0.0)]),
    "creature_abyss_leviathan": ("creature_acid_shark", [("hue", 0.75), ("scale", 1.5), ("glow", (170, 90, 255))]),
    "event_vengeful_wisp": ("event_aurora_wisp", [("hue", 0.0)]),
    "terrain_lumenite_ore": ("terrain_rock", [("over", "item_lumenite_ore", (0.5, 0.5), 0.12, [])]),
    "terrain_voidsteel_ore": ("terrain_rock", [("over", "item_voidsteel_ore", (0.5, 0.5), 0.12, [])]),
    "terrain_astral_ore": ("terrain_rock", [("over", "item_astral_ore", (0.5, 0.5), 0.12, [])]),
    "terrain_meteorite_ore": ("terrain_rock", [("over", "item_meteorite_ore", (0.5, 0.5), 0.12, [])]),
}
for _name, _recipe in _EXTRA.items():
    RECIPES.setdefault(_name, _recipe)

# Batch A/B content: props, blocks, pets, mount, enemies and new items.
_BATCH = {
    "deco_pot_surface": ("furn_plant_pot", [("tint", (180, 120, 80), 0.45)]),
    "deco_pot_frozen": ("furn_plant_pot", [("tint", (170, 210, 240), 0.55)]),
    "deco_pot_junk": ("furn_plant_pot", [("tint", (140, 130, 120), 0.6)]),
    "deco_pot_cavern": ("furn_plant_pot", [("tint", (120, 95, 70), 0.55)]),
    "deco_pot_molten": ("furn_plant_pot", [("tint", (200, 70, 30), 0.5), ("glow", (255, 120, 40))]),
    "deco_pot_dungeon": ("furn_plant_pot", [("tint", (90, 110, 150), 0.55)]),
    "deco_pot_canopy": ("furn_plant_pot", [("tint", (90, 170, 80), 0.5)]),
    "furn_dungeon_chest": ("furn_storage_crate", [("tint", (80, 100, 160), 0.45), ("glow", (120, 170, 255))]),
    "furn_cabin_chest": ("furn_storage_crate", [("tint", (170, 120, 70), 0.4)]),
    "deco_tombstone": ("furn_sign_post", [("tint", (150, 150, 160), 0.75)]),
    "station_cargo_pod_station": ("item_alloy_crate", [("glow", (120, 220, 255))]),
    "furn_kill_banner": ("furn_colony_banner", [("hue", 0.0)]),
    "deco_weapon_shrine": ("snow_rocks_a", [("tint", (120, 130, 150), 0.5), ("over", "weapon_chainsaw_sword", (0.5, 0.0), 0.6, [("rot", -90), ("glow", (170, 210, 255))])]),
    "furn_standing_torch": ("furn_wall_torch", []),
    "tile_dart_trap": ("tile_basalt_brick", [("tint", (120, 90, 60), 0.35)]),
    "tile_explosive_trap": ("tile_basalt_brick", [("tint", (200, 60, 40), 0.4)]),
    "tile_dungeon_brick_precursor": ("tile_cobalt_brick", [("tint", (60, 80, 120), 0.45), ("val", 0.8)]),
    "tile_giant_trunk": ("tile_astro_timber_planks", [("tint", (110, 80, 50), 0.5)]),
    "deco_herb_sunpetal": ("flora_bloom_flower", [("hue", 0.13)]),
    "deco_herb_moonglow": ("flora_bloom_flower", [("hue", 0.6), ("glow", (120, 170, 255))]),
    "deco_herb_rainreed": ("molten_reeds", [("hue", 0.5)]),
    "deco_herb_frostbell": ("flora_prism_flower", [("hue", 0.55)]),
    "deco_herb_emberroot": ("flora_bloom_flower", [("hue", 0.02), ("glow", (255, 120, 40))]),
    "deco_herb_sporecap": ("spore_small", [("hue", 0.83)]),
    "deco_sapling": ("fern_plant", [("scale", 0.6)]),
    "item_sunpetal_herb": ("flora_bloom_flower", [("hue", 0.13)]),
    "item_sunpetal_seeds": ("spore_small", [("tint", (230, 200, 90), 0.6), ("scale", 0.5)]),
    "item_wire_spool": ("tool_rope_coil", [("hue", 0.0)]),
    "item_gate_and": ("item_compass_chip", [("hue", 0.3)]),
    "item_bomb": ("ammo_fuel_canister", [("tint", (60, 60, 60), 0.5)]),
    "item_charge_crystal": ("energy_crystals", [("hue", 0.6), ("glow", (120, 180, 255))]),
    "item_cargo_pod": ("item_alloy_crate", [("scale", 0.7), ("glow", (120, 220, 255))]),
    "item_pet_drone_chip": ("item_compass_chip", [("hue", 0.55)]),
    "item_pet_crab_shell": ("critter_shell_crab", [("scale", 0.7)]),
    "item_light_pet_wisp": ("event_aurora_wisp", [("scale", 0.5)]),
    "item_light_pet_lantern": ("furn_glow_lamp_amber", [("scale", 0.6)]),
    "item_hover_bike_key": ("item_vault_key", [("hue", 0.55)]),
    "item_recall_capsule": ("item_ancient_power_cell", [("hue", 0.1)]),
    "item_phase_rod": ("tech_focus_lens_crystal", [("hue", 0.8), ("glow", (220, 140, 255))]),
    "item_memorial_marker": ("furn_sign_post", [("tint", (150, 150, 160), 0.75)]),
    "pet_pocket_drone": ("damaged_mining_drone", [("scale", 0.5)]),
    "pet_pebble_crab": ("critter_crab_small", [("tint", (160, 150, 140), 0.4)]),
    "pet_lumen_wisp": ("critter_light_wisp_bird", [("glow", (140, 230, 255))]),
    "pet_lantern_bot": ("security_droid", [("scale", 0.45), ("glow", (255, 200, 110))]),
    "mount_hover_bike": ("event_scavenger_bike_rider", []),
    "creature_dungeon_guardian": ("creature_ancient_guardian", [("tint", (200, 200, 220), 0.35), ("glow", (120, 200, 255))]),
    "creature_tunnel_wyrm": ("critter_worm", [("scale", 2.0), ("tint", (120, 90, 70), 0.4)]),
    "creature_dune_borer": ("critter_worm", [("scale", 2.0), ("tint", (220, 190, 120), 0.5)]),
    "creature_rift_caster": ("event_protocol_mage", [("hue", 0.78)]),
    "creature_dive_kite": ("sky_ray", [("hue", 0.08)]),
    "creature_cache_mimic": ("furn_storage_crate", [("tint", (200, 60, 50), 0.3)]),
    "creature_gel_hopper": ("critter_glow_grub", [("scale", 1.5), ("hue", 0.55)]),
    "creature_boulder_hopper": ("snow_rocks_a", [("tint", (130, 120, 110), 0.7)]),
    # The sheet's glider wings cell holds a spider; this is the real glider pack art.
    "item_glider_pack": ("sky_ray", [("tint", (200, 220, 240), 0.45)]),
    "creature_flea_hopper": ("scrap_mite", [("hue", 0.0), ("scale", 0.7)]),
}
for _name, _recipe in _BATCH.items():
    RECIPES.setdefault(_name, _recipe)

# World generation completion: blocks, cave formations, piles, statues, plants, chests, stations, walls and the phase liquid.
def _statue(creature, scale):
    return ("deco_precursor_statue", [("tint", (140, 140, 145), 0.75),
                                      ("over", creature, (0.5, 0.18), scale, [("tint", (150, 150, 155), 0.85)])])


_WORLDGEN = {
    "tile_scrap_sandstone": ("tile_scrap_heap", [("val", 0.8), ("tint", (150, 105, 60), 0.3)]),
    "tile_slush": ("tile_icy_soil", [("tint", (150, 160, 175), 0.45)]),
    "tile_gem_amethyst": ("terrain_rock", [("over", "item_amethyst_shard", (0.5, 0.5), 0.12, [])]),
    "tile_gem_topaz": ("terrain_rock", [("over", "item_topaz_shard", (0.5, 0.5), 0.12, [])]),
    "tile_gem_sapphire": ("terrain_rock", [("over", "item_sapphire_shard", (0.5, 0.5), 0.12, [])]),
    "tile_gem_emerald": ("terrain_rock", [("over", "item_emerald_shard", (0.5, 0.5), 0.12, [])]),
    "tile_gem_ruby": ("terrain_rock", [("over", "item_ruby_shard", (0.5, 0.5), 0.12, [])]),
    "tile_gem_diamond": ("terrain_rock", [("over", "item_diamond_shard", (0.5, 0.5), 0.12, [])]),
    "tile_mud_brick": ("tile_basalt_brick", [("tint", (110, 80, 55), 0.55)]),
    "tile_boulder": ("terrain_rock", [("val", 0.75), ("tint", (120, 110, 100), 0.25)]),
    "deco_stalactite_stone": ("snow_rocks_a", [("tint", (120, 115, 110), 0.65), ("rot", 180)]),
    "deco_stalagmite_stone": ("snow_rocks_a", [("tint", (120, 115, 110), 0.65)]),
    "deco_stalactite_ice": ("snow_rocks_b", [("tint", (170, 210, 240), 0.55), ("rot", 180)]),
    "deco_stalagmite_ice": ("snow_rocks_b", [("tint", (170, 210, 240), 0.55)]),
    "deco_stalactite_sand": ("snow_rocks_a", [("tint", (200, 160, 100), 0.6), ("rot", 180)]),
    "deco_stalagmite_sand": ("snow_rocks_a", [("tint", (200, 160, 100), 0.6)]),
    "deco_stalactite_ash": ("lava_rock", [("tint", (70, 60, 60), 0.5), ("rot", 180)]),
    "deco_stalagmite_ash": ("lava_rock", [("tint", (70, 60, 60), 0.5)]),
    "deco_stalactite_hive": ("snow_rocks_b", [("tint", (230, 170, 50), 0.6), ("rot", 180)]),
    "deco_stalagmite_hive": ("snow_rocks_b", [("tint", (230, 170, 50), 0.6)]),
    "deco_rubble_pile": ("junk_rock", [("tint", (120, 115, 110), 0.5)]),
    "deco_loot_pile": ("junk_rock", [("tint", (210, 180, 70), 0.35)]),
    "deco_shell_pile": ("item_coral_shard", [("tint", (240, 230, 210), 0.5)]),
    "deco_burrower_nest": ("junk_rock", [("tint", (190, 150, 90), 0.5), ("flip",)]),
    "deco_statue_hopper": _statue("critter_glow_grub", 0.6),
    "deco_statue_crab": _statue("critter_crab_small", 0.6),
    "deco_statue_bat": _statue("creature_lantern_bat", 0.45),
    "deco_fallen_log": ("tile_giant_trunk", [("rot", 90), ("tint", (90, 65, 40), 0.3)]),
    "deco_lilypad": ("fern_plant", [("hue", 0.33), ("scale", 0.6)]),
    "deco_reed": ("flora_light_reed", [("hue", 0.25)]),
    "deco_seaweed": ("flora_kelp_strand", []),
    "deco_coral_fan": ("flora_tube_coral", [("hue", 0.95)]),
    "deco_grave_marker": ("snow_rocks_b", [("tint", (120, 122, 118), 0.75), ("scale", 0.6)]),
    "deco_spike_strip": ("item_spike_lattice", [("tint", (160, 160, 170), 0.3)]),
    "station_slag_forge": ("station_power_forge_mk2", [("tint", (90, 50, 40), 0.45), ("glow", (255, 120, 40))]),
    "furn_sunken_chest": ("furn_cabin_chest", [("tint", (60, 120, 110), 0.45)]),
    "furn_surface_crate": ("furn_storage_crate", [("tint", (160, 130, 80), 0.35)]),
    "furn_shrine_chest": ("furn_cabin_chest", [("tint", (80, 120, 60), 0.4), ("glow", (160, 230, 120))]),
    "furn_sky_chest": ("furn_storage_crate", [("tint", (200, 220, 250), 0.45), ("glow", (200, 230, 255))]),
    "furn_lander_chest": ("furn_storage_crate", [("tint", (150, 150, 160), 0.5)]),
    "furn_slag_chest": ("furn_storage_crate", [("tint", (70, 40, 35), 0.5), ("glow", (255, 110, 40))]),
    "furn_campfire": ("magma_brazier", [("scale", 0.8)]),
    "deco_brood_pod": ("struct_bloom_hive", [("scale", 0.45), ("tint", (240, 200, 120), 0.3)]),
    "deco_solar_lily": ("flora_bloom_flower", [("hue", 0.12), ("glow", (255, 220, 120))]),
    "deco_dye_blossom_indigo": ("flora_prism_flower", [("hue", 0.68)]),
    "deco_dye_blossom_crimson": ("flora_prism_flower", [("hue", 0.98)]),
    "deco_dye_blossom_gold": ("flora_prism_flower", [("hue", 0.14)]),
    "deco_long_moss": ("flora_hanging_vines", [("hue", 0.38), ("glow", (120, 255, 160))]),
    "deco_crystal_shrub": ("crystalline_flora", [("hue", 0.78)]),
    "deco_wild_gourd": ("nano_banana", [("hue", 0.08), ("scale", 0.8)]),
    "deco_wild_mushroom": ("radiant_fungus", [("hue", 0.08), ("scale", 0.5)]),
    "struct_vault_altar": ("struct_crystal_altar", [("hue", 0.45), ("glow", (120, 255, 200))]),
    "wall_grass_roots": ("wall_regolith", [("tint", (70, 120, 50), 0.3)]),
    "wall_cave_layered": ("wall_rock", [("val", 0.85), ("tint", (120, 110, 100), 0.2)]),
    "wall_cave_rough": ("wall_rock", [("val", 0.7)]),
    "wall_mossy_rock": ("wall_rock", [("tint", (60, 140, 90), 0.35)]),
    "tile_phase_liquid": ("tile_acid_water", [("hue", 0.82)]),
    "dye_petals": ("flora_bloom_flower", [("hue", 0.75), ("scale", 0.6)]),
}
for _name, _recipe in _WORLDGEN.items():
    RECIPES.setdefault(_name, _recipe)


# Neo-Terra theme pass: the Irradiated Core (liquid uranium, irradiated rock, cracked crust, vitrified glass, reactor
# ruins, core flora and creatures). Toxic-green palette; hue-shifted from the old molten art, never overwriting it.
URANIUM = 0.29
_GLOW = (120, 255, 80)
_THEME = {
    "tile_liquid_uranium": ("tile_molten_lava", [("hue", URANIUM, 1.15), ("val", 1.1)]),
    "tile_irradiated_rock": ("tile_obsidian", [("hue", URANIUM, 0.7), ("tint", (40, 70, 40), 0.25), ("val", 1.15)]),
    "tile_core_crust": ("tile_ash", [("tint", (80, 120, 55), 0.5), ("val", 0.85)]),
    "tile_vitrified_glass": ("tile_obsidian", [("hue", 0.42, 1.3), ("val", 1.35)]),
    "wall_irradiated_core": ("wall_obsidian", [("hue", URANIUM, 0.6), ("tint", (30, 55, 30), 0.3)]),
    "flora_uranium_crystal": ("frost_crystal", [("hue", URANIUM, 1.4), ("glow", _GLOW)]),
    "flora_rad_lichen": ("magma_lichen", [("hue", URANIUM)]),
    "flora_glow_reeds": ("molten_reeds", [("hue", URANIUM), ("glow", _GLOW)]),
    "flora_irradiated_boulder": ("lava_rock", [("hue", URANIUM, 0.8)]),
    "deco_core_vent": ("magma_brazier", [("hue", URANIUM), ("glow", _GLOW)]),
    "deco_stalactite_core": ("deco_stalactite_ash", [("tint", (70, 110, 60), 0.4)]),
    "deco_stalagmite_core": ("deco_stalagmite_ash", [("tint", (70, 110, 60), 0.4)]),
    "deco_pot_isotope": ("deco_pot_molten", [("hue", URANIUM), ("glow", _GLOW)]),
    "station_reactor_forge": ("station_power_forge_mk2", [("hue", URANIUM, 1.2), ("tint", (40, 70, 40), 0.2), ("glow", _GLOW)]),
    "furn_reactor_chest": ("furn_storage_crate", [("tint", (200, 190, 40), 0.35), ("glow", _GLOW)]),
    "core_husk": ("basalt_golem", [("hue", URANIUM, 1.2), ("glow", _GLOW)]),
    "fission_wisp": ("event_vengeful_wisp", [("hue", URANIUM, 1.3), ("glow", _GLOW)]),
    "item_vitrified_shard": ("obsidian_shard", [("hue", 0.42, 1.3), ("val", 1.3)]),
    # The fuel seas at the world edges (amber hydrocarbon), and the char slag uranium burns it into.
    "tile_liquid_fuel": ("tile_acid_water", [("hue", 0.11, 1.1), ("val", 1.05)]),
    "tile_char_slag": ("tile_ash", [("val", 0.55), ("tint", (90, 50, 20), 0.3)]),
    "tool_bucket_fuel": ("tool_bucket_water", [("hue", 0.11, 1.2)]),
}
for _name, _recipe in _THEME.items():
    RECIPES.setdefault(_name, _recipe)

# Hardmode progression (docs/HARDMODE_DIFF.md): the core gate boss, the ritual boss, the four obelisks, the core breaker,
# the igniter and hymnal summons, the three essences and the canopy bulb.
_HARDMODE = {
    "boss_fission_tyrant": ("boss_vault_colossus", [("hue", URANIUM, 1.3), ("tint", (40, 70, 30), 0.15), ("glow", _GLOW)]),
    "boss_void_cantor": ("boss_eclipse_lord", [("hue", 0.76, 1.2), ("val", 0.8), ("glow", (150, 70, 255))]),
    "creature_ember_obelisk": ("deco_energy_pylon", [("hue", 0.06, 1.4), ("glow", (255, 140, 40))]),
    "creature_rift_obelisk": ("deco_energy_pylon", [("hue", 0.78, 1.3), ("val", 0.85), ("glow", (170, 80, 255))]),
    "creature_gale_obelisk": ("deco_energy_pylon", [("hue", 0.53, 1.3), ("glow", (90, 220, 255))]),
    "creature_shard_obelisk": ("deco_energy_pylon", [("hue", 0.88, 1.2), ("val", 1.15), ("glow", (255, 150, 230))]),
    "tool_core_breaker": ("tool_seismic_hammer", [("hue", URANIUM, 1.3), ("glow", _GLOW)]),
    "item_core_igniter": ("item_summon_astral_sigil", [("hue", URANIUM, 1.3), ("glow", _GLOW)]),
    "item_void_hymnal": ("item_eclipse_sigil", [("hue", 0.76, 1.2), ("val", 0.85)]),
    "item_prism_essence": ("item_void_essence", [("hue", 0.88, 1.2), ("val", 1.2), ("glow", (255, 160, 235))]),
    "item_blight_essence": ("item_void_essence", [("hue", 0.24, 1.1), ("val", 0.8), ("glow", (150, 200, 60))]),
    "item_flight_essence": ("item_void_essence", [("hue", 0.53, 1.2), ("val", 1.25), ("glow", (120, 230, 255))]),
    "deco_canopy_bulb": ("deco_brood_pod", [("hue", 0.92, 1.3), ("val", 1.1), ("glow", (255, 120, 200))]),
}
for _name, _recipe in _HARDMODE.items():
    RECIPES.setdefault(_name, _recipe)

# Travel pass (docs/TERRARIA_GAPS.md plan item 1): pylons, glide-walks, mounts and their items (placeholder recolours).
_PYLON = "deco_energy_pylon"
_TRAVEL = {
    "deco_pylon_colony": (_PYLON, [("hue", 0.33, 1.2), ("glow", (120, 255, 140))]),
    "deco_pylon_frost": (_PYLON, [("hue", 0.55, 1.1), ("val", 1.15), ("glow", (170, 230, 255))]),
    "deco_pylon_scrap": (_PYLON, [("hue", 0.08, 1.2), ("glow", (255, 170, 80))]),
    "deco_pylon_canopy": (_PYLON, [("hue", 0.25, 1.3), ("glow", (180, 255, 90))]),
    "deco_pylon_cavern": (_PYLON, [("hue", 0.72, 1.1), ("val", 0.9), ("glow", (170, 120, 255))]),
    "deco_pylon_coast": (_PYLON, [("hue", 0.47, 1.2), ("glow", (90, 255, 220))]),
    "mount_dune_strider": ("snow_wolf", [("tint", (210, 140, 60), 0.45)]),
    "mount_rotor_skiff": ("event_drone", [("hue", 0.58, 1.1), ("scale", 1.2)]),
    "mount_tide_ray": ("creature_storm_ray", [("hue", 0.5, 1.2), ("val", 1.1)]),
    "mount_burrower": ("critter_pebble_beetle", [("hue", 0.07, 1.1), ("val", 0.85), ("scale", 2)]),
    "mount_spring_hopper": ("critter_frog", [("hue", 0.16, 1.3), ("scale", 2)]),
    "buff_dune_strider": ("snow_wolf", [("tint", (210, 140, 60), 0.45)]),
    "buff_rotor_skiff": ("event_drone", [("hue", 0.58, 1.1)]),
    "buff_tide_ray": ("creature_storm_ray", [("hue", 0.5, 1.2)]),
    "buff_burrower": ("critter_pebble_beetle", [("hue", 0.07, 1.1)]),
    "buff_spring_hopper": ("critter_frog", [("hue", 0.16, 1.3)]),
    "item_glide_walk": ("wallart_glass_rail", [("sat", 0.2), ("val", 0.85)]),
    "item_express_walk": ("wallart_glass_rail", [("hue", 0.11, 1.4), ("glow", (255, 190, 60))]),
    "item_colony_pylon": (_PYLON, [("hue", 0.33, 1.2)]),
    "item_frost_pylon": (_PYLON, [("hue", 0.55, 1.1), ("val", 1.15)]),
    "item_scrap_pylon": (_PYLON, [("hue", 0.08, 1.2)]),
    "item_canopy_pylon": (_PYLON, [("hue", 0.25, 1.3)]),
    "item_cavern_pylon": (_PYLON, [("hue", 0.72, 1.1), ("val", 0.9)]),
    "item_coast_pylon": (_PYLON, [("hue", 0.47, 1.2)]),
    "item_strider_whistle": ("item_hover_bike_key", [("hue", 0.09, 1.3)]),
    "item_rotor_skiff_key": ("item_hover_bike_key", [("hue", 0.58, 1.2)]),
    "item_tide_ray_charm": ("item_protocol_key", [("hue", 0.5, 1.2), ("glow", (90, 230, 255))]),
    "item_burrower_key": ("item_hover_bike_key", [("hue", 0.07, 1.0), ("val", 0.8)]),
    "item_spring_hopper_key": ("item_hover_bike_key", [("hue", 0.16, 1.4)]),
}
for _name, _recipe in _TRAVEL.items():
    RECIPES.setdefault(_name, _recipe)

# Player and combat systems (docs/TERRARIA_GAPS.md plan item 2): sifter, dye vat, dyes, luck and aggro gear, the
# boss salvage cache, trophy and relic (the renderer tints these per boss).
_PLAYER_SYSTEMS = {
    "station_sifter": ("station_water_recycler", [("tint", (170, 140, 100), 0.4)]),
    "station_dye_vat": ("deco_pot_isotope", [("hue", 0.85, 1.2), ("scale", 1.3)]),
    "deco_boss_trophy": ("furn_kill_banner", [("tint", (230, 190, 80), 0.45), ("glow", (255, 220, 120))]),
    "deco_boss_relic": ("deco_precursor_statue", [("hue", 0.12, 1.3), ("glow", (255, 200, 90))]),
    "item_salvage_cache": ("item_alloy_crate", [("hue", 0.8, 1.2), ("glow", (220, 150, 255))]),
    "item_fortune_tonic": ("item_tech_tonic", [("hue", 0.33, 1.2)]),
    "item_clover_circuit": ("item_lucky_charm", [("hue", 0.36, 1.3), ("glow", (120, 255, 140))]),
    "item_beacon_harness": ("item_gunners_harness", [("hue", 0.02, 1.3), ("glow", (255, 120, 80))]),
    "item_shroud_cloak": ("item_ghost_cloak", [("val", 0.6), ("tint", (40, 40, 70), 0.4)]),
    "item_crimson_dye": ("item_med_stim", [("hue", 0.97, 1.3)]),
    "item_azure_dye": ("item_med_stim", [("hue", 0.6, 1.3)]),
    "item_verdant_dye": ("item_med_stim", [("hue", 0.33, 1.3)]),
    "item_amber_dye": ("item_med_stim", [("hue", 0.11, 1.3)]),
    "item_violet_dye": ("item_med_stim", [("hue", 0.77, 1.3)]),
    "item_teal_dye": ("item_med_stim", [("hue", 0.5, 1.3)]),
    "item_shadow_dye": ("item_med_stim", [("val", 0.45), ("tint", (40, 40, 60), 0.4)]),
    "item_prismatic_dye": ("item_med_stim", [("hue", 0.88, 1.2), ("glow", (255, 160, 235))]),
    "item_dye_solvent": ("item_med_stim", [("tint", (235, 240, 245), 0.6)]),
}
for _name, _recipe in _PLAYER_SYSTEMS.items():
    RECIPES.setdefault(_name, _recipe)

# World & leisure (docs/TERRARIA_GAPS.md plan item 3): wave-moon summons and drops, kites, pet permits and town pets,
# golf gear, paint buckets and tools, glow coating, audio decks (items and placed tiles).
_PAINTS = {"signal_red": 0.0, "hazard_orange": 0.07, "solar_yellow": 0.15, "moss_lime": 0.25, "canopy_green": 0.36,
           "coolant_teal": 0.48, "ion_cyan": 0.53, "sky_blue": 0.6, "deep_blue": 0.66, "void_violet": 0.76, "neon_pink": 0.9}
_DECK_HUES = {"grassland": 0.3, "underground": 0.08, "frozen": 0.55, "junk": 0.1, "molten": 0.03, "radiated": 0.27,
              "overgrowth": 0.4, "fungal": 0.5, "crystal": 0.82, "canopy": 0.35, "coast": 0.48, "boss": 0.98}
_LEISURE = {
    "item_hollow_lantern": ("cmd_spore_lantern", [("hue", 0.07, 1.3), ("glow", (255, 150, 40))]),
    "item_rime_beacon": ("item_scrap_beacon", [("hue", 0.55, 1.2), ("val", 1.15), ("glow", (140, 220, 255))]),
    "item_cryo_orders": ("item_invasion_banner", [("hue", 0.55, 1.1), ("val", 1.1)]),
    "item_hollow_ember": ("item_ruby_shard", [("hue", 0.07, 1.3), ("glow", (255, 140, 40))]),
    "item_rime_shard": ("item_sapphire_shard", [("hue", 0.52, 0.8), ("val", 1.25)]),
    "item_delta_kite": ("creature_dive_kite", [("hue", 0.0, 1.3)]),
    "item_box_kite": ("creature_dive_kite", [("hue", 0.13, 1.3), ("val", 1.1)]),
    "item_ribbon_kite": ("creature_dive_kite", [("hue", 0.8, 1.2)]),
    "item_purrbot_permit": ("item_pet_drone_chip", [("hue", 0.08, 1.2)]),
    "item_rover_permit": ("item_pet_drone_chip", [("hue", 0.6, 1.2)]),
    "item_hopling_permit": ("item_pet_drone_chip", [("hue", 0.92, 1.2)]),
    "pet_purrbot": ("critter_scrap_rat", [("hue", 0.08, 1.3), ("val", 1.1)]),
    "pet_rover": ("critter_squirrel_bot", [("hue", 0.1, 0.9), ("val", 0.95)]),
    "pet_hopling": ("critter_hop_bunny", [("hue", 0.92, 0.8), ("val", 1.15)]),
    "item_golf_ball": ("item_eclipse_orb", [("sat", 0.0), ("val", 1.6)]),
    "tool_golf_putter": ("tool_ferrite_fishing_rod", [("sat", 0.2), ("val", 1.1)]),
    "tool_golf_iron": ("tool_cobalt_fishing_rod", [("sat", 0.3), ("val", 1.15)]),
    "tool_golf_driver": ("tool_astral_fishing_rod", [("hue", 0.12, 1.1)]),
    "tile_golf_tee": ("deco_stalagmite_ice", [("sat", 0.0), ("val", 1.3), ("scale", 0.6)]),
    "item_golf_tee": ("deco_stalagmite_ice", [("sat", 0.0), ("val", 1.3)]),
    "tile_golf_cup": ("tile_vent", [("tint", (30, 90, 40), 0.55)]),
    "item_golf_cup": ("tile_vent", [("tint", (30, 90, 40), 0.55), ("over", "deco_stalagmite_ice", (0.35, 0.0), 0.5, [("hue", 0.0, 1.5)])]),
    "tool_paintbrush": ("tool_paint_sprayer", [("hue", 0.08, 1.2)]),
    "tool_paint_roller": ("tool_paint_sprayer", [("hue", 0.6, 1.2)]),
    "tool_paint_scraper": ("tool_paint_sprayer", [("sat", 0.2), ("val", 0.8)]),
    "item_paint_shadow_black": ("item_empty_bucket", [("tint", (25, 25, 35), 0.7)]),
    "item_glow_coating": ("item_empty_bucket", [("tint", (200, 255, 150), 0.5), ("glow", (180, 255, 120))]),
    "item_audio_deck": ("item_mothership_chip", [("sat", 0.2), ("val", 0.9)]),
}
for _name, _hue in _PAINTS.items():
    _LEISURE["item_paint_" + _name] = ("item_empty_bucket", [("hue", _hue, 1.6), ("tint", tuple(int(c * 255) for c in colorsys.hsv_to_rgb(_hue, 0.8, 0.95)), 0.45)])
for _theme, _hue in _DECK_HUES.items():
    _LEISURE["item_deck_" + _theme] = ("item_mothership_chip", [("hue", _hue, 1.3)])
    _LEISURE["tile_deck_" + _theme] = ("furn_control_console", [("hue", _hue, 1.2)])
for _name, _recipe in _LEISURE.items():
    RECIPES.setdefault(_name, _recipe)

# Content fill (docs/TERRARIA_GAPS.md plan item 4): critters, gold variants and biome hostiles, generated from one table
# (recolours and kitbashes of existing creature art; placeholders until generated art lands).
_CONTENT_FILL = {
    "critter_regolith_vole": ("critter_terra_rat", [("tint", (150, 120, 80), 0.35)]),
    "critter_sunbeam_moth": ("critter_spore_moth", [("hue", 0.13, 1.2), ("val", 1.15)]),
    "critter_thistle_finch": ("critter_sky_flier", [("hue", 0.2), ("scale", 0.8)]),
    "critter_meadow_cricket": ("critter_pebble_beetle", [("hue", 0.3, 1.2)]),
    "critter_puffball_tortoise": ("creature_cap_turtle", [("scale", 0.5), ("hue", 0.1)]),
    "critter_dusk_glider": ("sky_hawk", [("val", 0.55), ("hue", 0.7)]),
    "critter_lamp_worm": ("critter_worm", [("hue", 0.15), ("glow", (255, 230, 120))]),
    "critter_moonhop_toad": ("critter_frog", [("hue", 0.72), ("val", 0.9)]),
    "critter_star_moth": ("critter_spore_moth", [("hue", 0.6), ("glow", (150, 190, 255))]),
    "critter_pond_minnow": ("critter_fish_silver", [("scale", 0.7), ("hue", 0.4)]),
    "critter_bubble_tadpole": ("critter_frog", [("scale", 0.5), ("val", 0.7)]),
    "critter_burrow_mole": ("critter_terra_rat", [("val", 0.55)]),
    "critter_root_grub": ("critter_glow_grub", [("tint", (140, 100, 60), 0.45)]),
    "critter_gravel_snail": ("critter_snail", [("sat", 0.2), ("val", 0.85)]),
    "critter_gem_tick": ("critter_truffle_mite", [("hue", 0.8, 1.3), ("glow", (210, 150, 255))]),
    "critter_echo_flitter": ("creature_lantern_bat", [("scale", 0.6), ("sat", 0.4)]),
    "critter_blind_cavefish": ("critter_fish_silver", [("sat", 0.1), ("val", 1.2)]),
    "critter_cave_salamander": ("critter_ember_newt", [("hue", 0.95, 0.5), ("val", 1.2)]),
    "critter_glow_slug": ("critter_glow_snail", [("hue", 0.42), ("glow", (120, 255, 200))]),
    "critter_frost_vole": ("critter_terra_rat", [("tint", (230, 240, 255), 0.55)]),
    "critter_icicle_beetle": ("critter_pebble_beetle", [("hue", 0.55), ("val", 1.25)]),
    "critter_snow_owlet": ("critter_owl", [("tint", (240, 245, 255), 0.6)]),
    "critter_rime_fish": ("critter_fish_glow", [("hue", 0.55), ("val", 1.2)]),
    "critter_rust_scarab": ("critter_scavenger_beetle", [("hue", 0.06, 1.2)]),
    "critter_wire_mouse": ("critter_scrap_rat", [("val", 1.2), ("hue", 0.55, 0.5)]),
    "critter_sprocket_fly": ("critter_dragonfly", [("hue", 0.08), ("sat", 0.6)]),
    "critter_tide_flea": ("creature_flea_hopper", [("scale", 0.6), ("hue", 0.1)]),
    "critter_hermit_bot": ("critter_crab_small", [("hue", 0.55), ("val", 1.1)]),
    "critter_fuel_seahorse": ("critter_fish_glow", [("hue", 0.11, 1.3), ("rot", 80)]),
    "critter_reef_star": ("critter_glow_jelly", [("hue", 0.98), ("scale", 0.7)]),
    "critter_canopy_gecko": ("critter_dune_lizard", [("hue", 0.33, 1.2)]),
    "critter_leafback_beetle": ("critter_ladybug_bot", [("hue", 0.3)]),
    "critter_orchid_moth": ("critter_prism_butterfly", [("hue", 0.88)]),
    "critter_mire_skipper": ("critter_fish_silver", [("tint", (110, 85, 55), 0.5)]),
    "critter_puffcap_hopper": ("critter_cave_frog", [("hue", 0.52), ("glow", (90, 200, 255))]),
    "critter_mycelium_worm": ("critter_worm", [("hue", 0.5), ("val", 1.2)]),
    "critter_spore_firefly": ("critter_glow_firefly", [("hue", 0.5), ("glow", (90, 220, 255))]),
    "critter_shard_moth": ("critter_prism_butterfly", [("hue", 0.55), ("val", 1.2)]),
    "critter_lumen_fish": ("critter_fish_glow", [("hue", 0.85), ("glow", (255, 170, 240))]),
    "critter_mutant_roach": ("critter_scavenger_beetle", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "critter_glow_toad": ("critter_frog", [("hue", 0.29, 1.3), ("glow", (120, 255, 80))]),
    "critter_rootworm": ("critter_worm", [("hue", 0.22), ("val", 0.7)]),
    "critter_isotope_beetle": ("critter_pebble_beetle", [("hue", 0.29, 1.4), ("glow", (120, 255, 80))]),
    "critter_fission_moth": ("critter_spore_moth", [("hue", 0.29, 1.3), ("glow", (120, 255, 80))]),
    "critter_gilded_vole": ("critter_terra_rat", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_cricket": ("critter_pebble_beetle", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_finch": ("critter_sky_flier", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_minnow": ("critter_fish_silver", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_gecko": ("critter_dune_lizard", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_scarab": ("critter_scavenger_beetle", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_hop_bunny": ("critter_hop_bunny", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_squirrel_bot": ("critter_squirrel_bot", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "critter_gilded_frog": ("critter_frog", [("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))]),
    "creature_thistle_boar": ("astro_beast", [("hue", 0.1), ("scale", 0.8)]),
    "creature_meadow_gel": ("creature_gel_hopper", [("hue", 0.33)]),
    "creature_hornet_drone": ("salvage_drone", [("hue", 0.13, 1.3)]),
    "creature_bore_beetle": ("creature_flea_hopper", [("scale", 1.4), ("tint", (120, 80, 40), 0.4)]),
    "creature_dusk_prowler": ("snow_wolf", [("val", 0.5), ("hue", 0.75)]),
    "creature_umbral_gel": ("creature_gel_hopper", [("hue", 0.72), ("val", 0.45)]),
    "creature_gloom_kite": ("creature_dive_kite", [("val", 0.5)]),
    "creature_grave_husk": ("event_husk", [("hue", 0.55)]),
    "creature_drifting_eye": ("event_eye_drone", [("hue", 0.8)]),
    "creature_thorn_behemoth": ("wasteland_beast", [("hue", 0.3)]),
    "creature_storm_harrier": ("creature_storm_ray", [("hue", 0.6), ("glow", (140, 200, 255))]),
    "creature_night_revenant": ("event_corrupted_survivor", [("val", 0.6)]),
    "creature_shock_gel": ("creature_boulder_hopper", [("hue", 0.55), ("glow", (120, 220, 255))]),
    "creature_sky_lancer": ("creature_terraformer_drone", [("hue", 0.1)]),
    "creature_tunnel_rat": ("mutant_rat", [("tint", (120, 90, 60), 0.4)]),
    "creature_burrow_spider": ("creature_silk_spider_mite", [("hue", 0.08)]),
    "creature_clay_golem": ("security_droid", [("tint", (170, 110, 80), 0.5)]),
    "creature_grub_worm": ("creature_dune_borer", [("hue", 0.08)]),
    "creature_mud_gel": ("creature_gel_hopper", [("tint", (110, 80, 50), 0.6)]),
    "creature_drill_mite": ("rock_mite", [("hue", 0.55)]),
    "creature_bedrock_crusher": ("basalt_golem", [("val", 0.7), ("hue", 0.6, 0.5)]),
    "creature_delve_wyrm": ("creature_tunnel_wyrm", [("hue", 0.08)]),
    "creature_shade_caster": ("creature_rift_caster", [("val", 0.5), ("hue", 0.7)]),
    "creature_gem_crawler": ("creature_geode_crab", [("hue", 0.78)]),
    "creature_cave_lurker": ("creature_lantern_bat", [("val", 0.6)]),
    "creature_slate_gel": ("creature_gel_hopper", [("sat", 0.2), ("val", 0.7)]),
    "creature_echo_caster": ("creature_rift_caster", [("hue", 0.55)]),
    "creature_crystal_borer": ("creature_tunnel_wyrm", [("hue", 0.5)]),
    "creature_hollow_bat": ("creature_halo_bat", [("val", 0.6), ("sat", 0.3)]),
    "creature_urn_mimic": ("creature_cache_mimic", [("hue", 0.1), ("scale", 0.8)]),
    "creature_geode_titan": ("creature_pearl_golem", [("hue", 0.95)]),
    "creature_void_mantis": ("creature_jungle_stalker", [("hue", 0.75), ("val", 0.7)]),
    "creature_runic_caster": ("creature_mycelid_shaman", [("hue", 0.45)]),
    "creature_armored_delver": ("event_corroded_mech", [("hue", 0.55)]),
    "creature_strongbox_mimic": ("creature_cache_mimic", [("hue", 0.14, 1.3), ("glow", (255, 215, 90))]),
    "creature_rime_gel": ("creature_gel_hopper", [("hue", 0.55), ("val", 1.2)]),
    "creature_frost_flitter": ("creature_lantern_bat", [("hue", 0.55)]),
    "creature_tundra_brute": ("wasteland_beast", [("hue", 0.55), ("val", 1.2)]),
    "creature_drift_leaper": ("creature_boulder_hopper", [("tint", (235, 245, 255), 0.6)]),
    "creature_cryo_caster": ("creature_rift_caster", [("hue", 0.53)]),
    "creature_permafrost_borer": ("creature_dune_borer", [("hue", 0.55)]),
    "creature_glacier_titan": ("glacial_golem", [("val", 0.8), ("glow", (150, 220, 255))]),
    "creature_blizzard_wraith": ("creature_crystal_wraith", [("hue", 0.55)]),
    "creature_frozen_mimic": ("creature_cache_mimic", [("tint", (200, 230, 255), 0.55)]),
    "creature_rime_diver": ("creature_dive_kite", [("hue", 0.55)]),
    "creature_rust_gel": ("creature_gel_hopper", [("hue", 0.06)]),
    "creature_scrap_hound": ("event_rust_hound", [("hue", 0.08)]),
    "creature_bolt_drone": ("salvage_drone", [("hue", 0.15)]),
    "creature_dune_phantom": ("event_dust_devil", [("hue", 0.1)]),
    "creature_junk_tinker": ("creature_rift_caster", [("tint", (200, 120, 50), 0.45)]),
    "creature_slag_borer": ("creature_dune_borer", [("tint", (150, 80, 40), 0.5)]),
    "creature_junk_pile_mimic": ("creature_cache_mimic", [("tint", (150, 90, 50), 0.5)]),
    "creature_scrap_colossus": ("event_scrap_elemental", [("val", 0.8)]),
    "creature_rust_reaper": ("event_rust_wraith", [("hue", 0.07)]),
    "creature_dune_devourer": ("creature_dune_borer", [("scale", 1.4), ("hue", 0.1)]),
    "creature_fuel_gel": ("creature_gel_hopper", [("hue", 0.11)]),
    "creature_pincer_crab": ("scavenger_crab", [("hue", 0.96)]),
    "creature_gull_diver": ("critter_sea_bird", [("val", 0.8), ("hue", 0.1, 0.4)]),
    "creature_fuel_eel": ("creature_tide_leech", [("hue", 0.1)]),
    "creature_barb_ray": ("creature_storm_ray", [("hue", 0.1)]),
    "creature_ink_squid": ("critter_glow_jelly", [("hue", 0.8), ("val", 0.7)]),
    "creature_deep_maw": ("creature_acid_shark", [("val", 0.6)]),
    "creature_reef_horror": ("creature_coral_sentinel", [("val", 0.7)]),
    "creature_tide_caller": ("creature_drowned_diver", [("hue", 0.5)]),
    "creature_vine_gel": ("creature_gel_hopper", [("hue", 0.3, 1.2)]),
    "creature_horn_beetle": ("creature_cap_turtle", [("hue", 0.3)]),
    "creature_canopy_bat": ("creature_lantern_bat", [("hue", 0.3)]),
    "creature_spear_frog": ("critter_leaf_frog", [("scale", 1.6), ("hue", 0.95)]),
    "creature_root_lasher": ("creature_dune_borer", [("hue", 0.3)]),
    "creature_dart_lizard": ("critter_dune_lizard", [("hue", 0.3), ("scale", 1.3)]),
    "creature_thorn_chanter": ("creature_mycelid_shaman", [("hue", 0.3)]),
    "creature_moss_hornet": ("creature_hive_guard", [("hue", 0.3)]),
    "creature_canopy_wyrm": ("creature_tunnel_wyrm", [("hue", 0.3)]),
    "creature_strangler_bloom": ("creature_snapjaw_plant", [("hue", 0.9)]),
    "creature_silverback_brute": ("creature_canopy_ape", [("val", 0.6), ("sat", 0.4)]),
    "creature_comb_gel": ("creature_gel_hopper", [("hue", 0.12, 1.3)]),
    "creature_brood_larva": ("critter_glow_grub", [("scale", 2), ("hue", 0.12)]),
    "creature_hive_warden": ("creature_hive_guard", [("val", 0.7)]),
    "creature_spore_gel": ("creature_gel_hopper", [("hue", 0.5), ("glow", (90, 200, 255))]),
    "creature_toadstool_leaper": ("creature_cap_turtle", [("hue", 0.55)]),
    "creature_mycelium_borer": ("creature_dune_borer", [("hue", 0.5)]),
    "creature_glowcap_mite": ("critter_truffle_mite", [("hue", 0.5), ("scale", 1.4)]),
    "creature_cap_sentinel": ("creature_spitting_orchid", [("hue", 0.5)]),
    "creature_mycelid_hulk": ("creature_spore_golem", [("val", 0.7)]),
    "creature_spore_phantom": ("creature_crystal_wraith", [("hue", 0.5)]),
    "creature_puff_bomber": ("creature_puff_drifter", [("val", 0.8), ("hue", 0.45)]),
    "creature_prism_gel": ("creature_gel_hopper", [("hue", 0.85), ("glow", (255, 170, 240))]),
    "creature_shard_leaper": ("creature_boulder_hopper", [("hue", 0.85)]),
    "creature_lumen_caster": ("creature_rift_caster", [("hue", 0.85), ("val", 1.3)]),
    "creature_pearl_wyrm": ("creature_tunnel_wyrm", [("hue", 0.85), ("val", 1.2)]),
    "creature_glass_kite": ("creature_dive_kite", [("hue", 0.85)]),
    "creature_prism_colossus": ("creature_pearl_golem", [("hue", 0.8), ("glow", (255, 170, 240))]),
    "creature_halo_seraph": ("critter_light_wisp_bird", [("scale", 2), ("hue", 0.12)]),
    "creature_chroma_serpent": ("creature_tunnel_wyrm", [("hue", 0.9), ("glow", (255, 150, 230))]),
    "creature_isotope_gel": ("creature_gel_hopper", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_fallout_hound": ("event_rust_hound", [("hue", 0.29)]),
    "creature_rad_moth": ("geiger_bug", [("hue", 0.29), ("scale", 1.3)]),
    "creature_fallout_seer": ("creature_rift_caster", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_sludge_worm": ("creature_dune_borer", [("hue", 0.29)]),
    "creature_rad_behemoth": ("event_abomination", [("hue", 0.29)]),
    "creature_isotope_wraith": ("event_rust_wraith", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_lead_box_mimic": ("creature_cache_mimic", [("hue", 0.29), ("val", 0.7)]),
    "creature_root_gel": ("creature_gel_hopper", [("hue", 0.22), ("val", 0.7)]),
    "creature_blight_crawler": ("creature_fungal_crawler", [("hue", 0.22)]),
    "creature_thorn_bat": ("creature_halo_bat", [("hue", 0.22), ("val", 0.7)]),
    "creature_root_wyrm": ("creature_tunnel_wyrm", [("hue", 0.22), ("val", 0.7)]),
    "creature_blight_hulk": ("creature_spore_golem", [("hue", 0.22), ("val", 0.6)]),
    "creature_rot_chanter": ("creature_mycelid_shaman", [("hue", 0.22), ("val", 0.6)]),
    "creature_vine_reaper": ("creature_dive_kite", [("hue", 0.22), ("val", 0.6)]),
    "creature_slag_gel": ("creature_gel_hopper", [("hue", 0.29), ("val", 0.6), ("glow", (120, 255, 80))]),
    "creature_fission_hound": ("event_rust_hound", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_core_borer": ("creature_tunnel_wyrm", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_isotope_imp": ("creature_rift_caster", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_reactor_bat": ("creature_lantern_bat", [("hue", 0.29), ("glow", (120, 255, 80))]),
    "creature_meltdown_golem": ("core_husk", [("scale", 1.3)]),
    "creature_critical_wisp": ("fission_wisp", [("val", 1.3)]),
    "creature_web_lurker": ("creature_silk_spider_mite", [("val", 0.6)]),
    "creature_brood_tyrant": ("creature_mite_broodmother", [("hue", 0.75)]),
    "creature_glyph_caster": ("creature_rift_caster", [("hue", 0.45), ("val", 1.2)]),
    "creature_vault_lancer": ("creature_vault_sentinel", [("hue", 0.1)]),
    "creature_crypt_hoplite": ("creature_alabaster_hoplite", [("val", 0.6)]),
    "creature_void_scribe": ("creature_rift_caster", [("hue", 0.76)]),
    "creature_crypt_flitter": ("creature_halo_bat", [("hue", 0.76), ("val", 0.5)]),
    # Town residents: the travelling merchant and the four town gels.
    "npc_travelling_merchant": ("npc_store_clerk", [("hue", 0.08, 1.2), ("tint", (120, 80, 40), 0.2)]),
    "npc_gel_azure": ("creature_gel_hopper", [("hue", 0.58, 1.3), ("val", 1.15), ("scale", 1.3)]),
    "npc_gel_amber": ("creature_gel_hopper", [("hue", 0.1, 1.3), ("val", 1.15), ("scale", 1.3)]),
    "npc_gel_rose": ("creature_gel_hopper", [("hue", 0.92, 1.2), ("val", 1.2), ("scale", 1.3)]),
    "npc_gel_verdant": ("creature_gel_hopper", [("hue", 0.36, 1.2), ("val", 1.05), ("scale", 1.3)]),
    # Biome timbers (felled biome trees).
    "item_vinewood": ("astro_timber", [("hue", 0.3, 0.6), ("val", 0.8)]),
    "item_palmwood": ("astro_timber", [("tint", (200, 170, 100), 0.4)]),
    "item_stalkwood": ("astro_timber", [("tint", (200, 205, 220), 0.6)]),
    "item_prismwood": ("astro_timber", [("tint", (230, 160, 230), 0.5), ("glow", (255, 170, 240))]),
    "item_blightwood": ("astro_timber", [("tint", (80, 90, 40), 0.55)]),
    "item_gemwood": ("astro_timber", [("tint", (110, 110, 125), 0.6)]),
    # Wild-plant materials and the plants themselves.
    "item_plant_fiber": ("fern_plant", [("hue", 0.25), ("scale", 0.6)]),
    "item_frost_lichen": ("flora_rad_lichen", [("hue", 0.55), ("val", 1.2)]),
    "item_scrap_thistle": ("mutant_vinelash", [("hue", 0.08), ("sat", 0.6), ("scale", 0.6)]),
    "item_sea_fennel": ("flora_kelp_strand", [("hue", 0.2), ("scale", 0.6)]),
    "item_canopy_pod": ("nano_banana", [("hue", 0.33), ("scale", 0.7)]),
    "item_glowcap_cluster": ("deco_wild_mushroom", [("hue", 0.55), ("glow", (90, 200, 255))]),
    "item_prism_bloom": ("flora_prism_flower", [("hue", 0.88), ("scale", 0.7)]),
    "item_blight_bulb": ("radiant_fungus", [("hue", 0.22), ("val", 0.7), ("scale", 0.6)]),
    "deco_wild_fiber_tuft": ("fern_plant", [("hue", 0.25)]),
    "deco_wild_frost_lichen": ("flora_rad_lichen", [("hue", 0.55), ("val", 1.25)]),
    "deco_wild_scrap_thistle": ("mutant_vinelash", [("hue", 0.08), ("sat", 0.6)]),
    "deco_wild_sea_fennel": ("flora_kelp_strand", [("hue", 0.2)]),
    "deco_wild_canopy_pod": ("flora_bloom_flower", [("hue", 0.33), ("val", 0.9)]),
    "deco_wild_glowcap": ("deco_wild_mushroom", [("hue", 0.55), ("glow", (90, 200, 255))]),
    "deco_wild_prism_bloom": ("flora_prism_flower", [("hue", 0.88), ("glow", (255, 170, 240))]),
    "deco_wild_blight_bulb": ("radiant_fungus", [("hue", 0.22), ("val", 0.7)]),
}
for _name, _recipe in _CONTENT_FILL.items():
    RECIPES.setdefault(_name, _recipe)
# Progression pass: the Fallout Hive-mind, the new town NPCs, the lifebloom plant and one-use upgrades.
_PROGRESSION = {
    "creature_fallout_hivemind": ("cyber_swarm_leader", [("hue", ACID), ("glow", (140, 255, 90))]),
    "npc_field_zoologist": ("npc_xeno_biologist", [("tint", (110, 170, 90), 0.3)]),
    "npc_pigment_trader": ("npc_tailor", [("tint", (200, 110, 220), 0.25)]),
    "npc_party_planner": ("npc_store_clerk", [("hue", 0.1), ("tint", (255, 200, 90), 0.25)]),
    "deco_lifebloom_pod": ("flora_bloom_flower", [("hue", 0.93), ("glow", (255, 140, 190))]),
    "item_lifebloom_pod": ("flora_bloom_flower", [("hue", 0.93), ("scale", 0.6), ("glow", (255, 140, 190))]),
    "item_auxiliary_socket": ("item_compass_chip", [("hue", GOLD), ("glow", (255, 210, 110))]),
    "item_nanite_marrow": ("item_bio_gel", [("tint", (150, 170, 200), 0.6)]),
    "item_coolant_pearl": ("item_diamond_shard", [("hue", 0.55), ("glow", (120, 220, 255))]),
    "item_fortune_lattice": ("item_lucky_charm", [("hue", GOLD), ("glow", (255, 230, 120))]),
    "item_bait_gland": ("item_bio_gel", [("tint", (240, 130, 160), 0.6)]),
    "item_servo_grease": ("ammo_fuel_canister", [("tint", (60, 55, 50), 0.55)]),
    "item_fabricator_manual": ("tech_datapad_flare", [("tint", (200, 160, 80), 0.5)]),
    "item_lumen_accord": ("furn_glow_lamp_cyan", [("scale", 0.7), ("hue", 0.15)]),
    "item_salvaged_thruster_fins": ("sky_ray", [("tint", (170, 110, 80), 0.6), ("scale", 0.8)]),
    "item_scrap_tether": ("tool_rope_coil", [("tint", (140, 130, 120), 0.6)]),
    "item_twin_lash": ("tool_rope_coil", [("hue", 0.33), ("glow", (120, 230, 120))]),
    "item_phase_tether": ("tool_rope_coil", [("hue", 0.8), ("glow", (200, 120, 255))]),
    "item_fission_slag": ("item_glowing_ore", [("tint", (120, 200, 60), 0.5)]),
    "item_fallout_hive_cell": ("cmd_drone_battery", [("hue", ACID), ("glow", (140, 255, 90))]),
    "item_companion_permit": ("furn_display_panel", [("tint", (230, 210, 160), 0.6), ("scale", 0.6)]),
}
for _name, _recipe in _PROGRESSION.items():
    RECIPES.setdefault(_name, _recipe)


def main():
    global SPRITES
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    SPRITES = root / "Assets" / "Sprites"
    written, failed = [], []
    for name, (base, steps) in RECIPES.items():
        target = SPRITES / f"{name}.png"
        if target.exists():
            continue
        try:
            image = apply(load(base), steps)
        except FileNotFoundError as error:
            failed.append(f"{name}: {error.filename}")
            continue
        image.save(target)
        written.append((name, image))

    if written:
        cell, columns = 96, 14
        rows = (len(written) + columns - 1) // columns
        preview = Image.new("RGBA", (columns * cell, rows * cell), (255, 0, 255, 255))
        for i, (_, image) in enumerate(written):
            thumb = image.copy()
            thumb.thumbnail((cell - 6, cell - 6))
            preview.alpha_composite(thumb, ((i % columns) * cell + (cell - thumb.width) // 2, (i // columns) * cell + (cell - thumb.height) // 2))
        preview.save(SPRITES.parent / "_kitbash_preview.png")
    print(f"Kitbashed {len(written)} sprites; failed: {failed}")


if __name__ == "__main__":
    main()
