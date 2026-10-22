"""Cut the Neo-Terra concept sheets into transparent sprite PNGs.

Usage: python -I tools/sprites/extract_sprites.py [project_dir]

Each entry is (sheet, name, box). The panel background is removed by flood
filling from the crop border over pixels close to the border colour, so the
dark outlines of each sprite stop the fill. A preview sheet is written to
Assets/Sprites/_preview.png for visual checks.
"""
import sys
from collections import deque
from pathlib import Path

from PIL import Image

SHEET_CORE = "sheet_00a_core.jpg"
SHEET_ASSETS = "sheet_00b_assets.jpg"
SHEET_BOSSES = "sheet_00c_bosses.jpg"
SHEET_NPCS = "sheet_00d_npcs.jpg"
SHEET_BIOMES = "sheet_00e_biome_overview.jpg"
SHEET_FROZEN = "sheet_00f_frozen.jpg"
SHEET_RADIATED = "sheet_00g_radiated.jpg"
SHEET_MOLTEN = "sheet_00h_molten.jpg"
SHEET_JUNK = "sheet_00i_junk.jpg"
BIOME_SHEETS = {SHEET_BIOMES, SHEET_FROZEN, SHEET_RADIATED, SHEET_MOLTEN, SHEET_JUNK}

# (sheet, name, (left, top, right, bottom), keep_background)
SPRITES = [
    # Colonist walk/turn frames
    (SHEET_CORE, "colonist_front", (258, 100, 326, 208), False),
    (SHEET_CORE, "colonist_side_a", (366, 100, 430, 208), False),
    (SHEET_CORE, "colonist_side_b", (480, 96, 546, 204), False),
    (SHEET_CORE, "colonist_back", (594, 96, 664, 204), False),
    # Structures and technology
    (SHEET_CORE, "solar_panel", (752, 106, 846, 198), False),
    (SHEET_CORE, "oxygen_unit", (900, 116, 966, 198), False),
    (SHEET_CORE, "communal_pod", (1024, 92, 1172, 202), False),
    (SHEET_CORE, "nano_fabricator", (410, 262, 506, 354), False),
    (SHEET_CORE, "nano_banana", (896, 270, 984, 350), False),
    (SHEET_CORE, "colony_wall", (782, 422, 978, 516), False),
    (SHEET_CORE, "spore_small", (342, 606, 402, 668), False),
    (SHEET_CORE, "spore_tall", (404, 580, 476, 668), False),
    (SHEET_CORE, "spore_crystal", (486, 600, 544, 670), False),
    # Weapons
    (SHEET_ASSETS, "laser_rifle", (84, 82, 236, 144), False),
    (SHEET_ASSETS, "plasma_pistol", (330, 78, 430, 144), False),
    (SHEET_ASSETS, "rocket_launcher", (516, 74, 676, 154), False),
    (SHEET_ASSETS, "mini_turret", (106, 222, 210, 304), False),
    (SHEET_ASSETS, "sniper_rifle", (252, 222, 466, 290), False),
    (SHEET_ASSETS, "grenade_launcher", (496, 216, 704, 300), False),
    # Tiles (kept opaque; they are full squares)
    (SHEET_ASSETS, "tile_grate", (748, 66, 846, 160), True),
    (SHEET_ASSETS, "tile_plate", (868, 66, 966, 160), True),
    (SHEET_ASSETS, "tile_hazard", (988, 68, 1084, 160), True),
    (SHEET_ASSETS, "tile_vent", (1112, 72, 1206, 164), True),
    (SHEET_ASSETS, "tile_vent_door", (1234, 74, 1330, 166), True),
    (SHEET_ASSETS, "wall_panel", (746, 216, 848, 316), True),
    (SHEET_ASSETS, "wall_window", (866, 216, 968, 316), True),
    (SHEET_ASSETS, "wall_cracked", (986, 216, 1090, 316), True),
    (SHEET_ASSETS, "door_open", (1116, 216, 1218, 318), True),
    (SHEET_ASSETS, "door_closed", (1238, 216, 1336, 318), True),
    # Materials
    (SHEET_ASSETS, "metal_alloy", (70, 406, 184, 496), False),
    (SHEET_ASSETS, "ore_red", (328, 406, 414, 492), False),
    (SHEET_ASSETS, "ore_blue", (422, 400, 508, 492), False),
    (SHEET_ASSETS, "ore_green", (510, 406, 598, 488), False),
    (SHEET_ASSETS, "energy_crystals", (626, 390, 738, 488), False),
    # Creatures
    (SHEET_ASSETS, "swarm_bug", (800, 420, 910, 508), False),
    (SHEET_ASSETS, "security_droid", (980, 378, 1092, 508), False),
    (SHEET_ASSETS, "overseer_mech", (1150, 356, 1344, 518), False),
    (SHEET_ASSETS, "luminous_spores", (84, 566, 194, 672), False),
    (SHEET_ASSETS, "crystalline_flora", (240, 564, 324, 672), False),
    (SHEET_ASSETS, "vinelash", (394, 564, 516, 682), False),
    (SHEET_ASSETS, "hydroponics_tray", (556, 566, 742, 674), False),
    (SHEET_ASSETS, "rock_mite", (780, 586, 890, 686), False),
    (SHEET_ASSETS, "sky_ray", (930, 580, 1050, 672), False),
    (SHEET_ASSETS, "spore_rat", (1076, 606, 1182, 684), False),
    (SHEET_ASSETS, "astro_beast", (1206, 586, 1340, 684), False),
    # Bosses
    (SHEET_BOSSES, "alpha_skitter", (80, 176, 196, 270), False),
    (SHEET_BOSSES, "damaged_mining_drone", (226, 160, 360, 280), False),
    (SHEET_BOSSES, "plant_overgrowth", (120, 370, 306, 520), False),
    (SHEET_BOSSES, "green_mech", (76, 556, 190, 686), False),
    (SHEET_BOSSES, "scout_mech", (230, 550, 344, 684), False),
    (SHEET_BOSSES, "astro_god", (378, 156, 756, 672), False),
    (SHEET_BOSSES, "cyber_swarm_leader", (748, 150, 924, 304), False),
    (SHEET_BOSSES, "renegade_overseer", (920, 160, 1084, 304), False),
    (SHEET_BOSSES, "solar_flare_entity", (1090, 156, 1196, 280), False),
    (SHEET_BOSSES, "void_stalker", (1218, 156, 1336, 280), False),
    (SHEET_BOSSES, "mutated_flora", (760, 366, 930, 524), False),
    (SHEET_BOSSES, "abomination", (940, 390, 1064, 528), False),
    (SHEET_BOSSES, "asteroid_miner_mech", (1154, 366, 1324, 520), False),
    (SHEET_BOSSES, "warship_core", (780, 570, 1066, 684), False),
    (SHEET_BOSSES, "energy_cutlass", (1138, 556, 1206, 640), False),
    (SHEET_BOSSES, "cyber_pirate_captain", (1136, 550, 1324, 682), False),
    # NPCs
    (SHEET_NPCS, "chief_engineer", (70, 160, 180, 296), False),
    (SHEET_NPCS, "head_researcher", (200, 164, 300, 298), False),
    (SHEET_NPCS, "security_commander", (320, 160, 420, 298), False),
    (SHEET_NPCS, "astro_botanist", (436, 158, 552, 298), False),
    (SHEET_NPCS, "trade_consul", (566, 158, 672, 296), False),
    (SHEET_NPCS, "station_ai", (690, 156, 804, 296), False),
    # Wardrobe
    (SHEET_NPCS, "heavy_astro_suit", (866, 136, 960, 272), False),
    (SHEET_NPCS, "stealth_armor", (996, 156, 1084, 260), False),
    (SHEET_NPCS, "fungal_coat", (1116, 156, 1204, 250), False),
    (SHEET_NPCS, "banana_uniform", (1240, 156, 1344, 260), False),
    (SHEET_NPCS, "void_helmet", (866, 340, 960, 424), False),
    (SHEET_NPCS, "scout_boots", (990, 370, 1094, 430), False),
    (SHEET_NPCS, "mining_pack", (1106, 346, 1220, 450), False),
    (SHEET_NPCS, "swarm_gloves", (1236, 350, 1340, 420), False),
    (SHEET_NPCS, "face_base", (320, 410, 420, 520), False),
]

# Face variation grid: two rows of eight on the NPC sheet.
FACE_COLUMNS = [74, 152, 230, 309, 388, 467, 546, 625]
FACE_ROWS = [(556, 632), (640, 714)]
for row_index, (top, bottom) in enumerate(FACE_ROWS):
    for col_index, left in enumerate(FACE_COLUMNS):
        SPRITES.append((SHEET_NPCS, f"face_{row_index * 8 + col_index:02d}", (left + 7, top + 4, left + 69, bottom - 4), False))

SPRITES += [
    # Frozen Wasteland
    (SHEET_FROZEN, "tile_icy_soil", (108, 166, 234, 296), True),
    (SHEET_FROZEN, "tile_glacial_ice", (300, 166, 430, 296), True),
    (SHEET_FROZEN, "snow_rocks_a", (494, 176, 660, 300), False),
    (SHEET_FROZEN, "snow_rocks_b", (678, 162, 842, 300), False),
    (SHEET_FROZEN, "wall_ice", (918, 180, 1072, 292), True),
    (SHEET_FROZEN, "hab_frozen", (1158, 160, 1306, 304), False),
    (SHEET_FROZEN, "glacial_ore", (112, 408, 218, 522), False),
    (SHEET_FROZEN, "frost_crystal", (308, 424, 426, 536), False),
    (SHEET_FROZEN, "tundra_lichen_a", (504, 438, 592, 524), False),
    (SHEET_FROZEN, "tundra_lichen_b", (610, 438, 708, 526), False),
    (SHEET_FROZEN, "snow_wolf", (84, 562, 266, 684), False),
    (SHEET_FROZEN, "saxifrage_plant", (338, 588, 428, 668), False),
    (SHEET_FROZEN, "cryopterous", (762, 562, 918, 684), False),
    (SHEET_FROZEN, "glacial_golem", (954, 428, 1190, 684), False),
    (SHEET_FROZEN, "snow_mite", (1212, 592, 1328, 680), False),
    # Radiated Deeps
    (SHEET_RADIATED, "tile_radiated_soil", (104, 164, 236, 296), True),
    (SHEET_RADIATED, "tile_caustic_crystal", (298, 164, 430, 296), True),
    (SHEET_RADIATED, "hab_corrupted_a", (474, 158, 658, 304), False),
    (SHEET_RADIATED, "hab_corrupted_b", (696, 158, 844, 304), False),
    (SHEET_RADIATED, "wall_lead", (918, 184, 1072, 292), True),
    (SHEET_RADIATED, "caustic_rock", (1150, 176, 1306, 302), False),
    (SHEET_RADIATED, "radiant_fungus", (88, 434, 256, 664), False),
    (SHEET_RADIATED, "mutant_vinelash", (282, 436, 462, 672), False),
    (SHEET_RADIATED, "uranium_ore", (496, 436, 604, 528), False),
    (SHEET_RADIATED, "caustic_shard", (620, 420, 726, 528), False),
    (SHEET_RADIATED, "radiant_flora", (596, 592, 688, 668), False),
    (SHEET_RADIATED, "rad_skitter", (764, 432, 924, 554), False),
    (SHEET_RADIATED, "mutant_rat", (966, 446, 1156, 554), False),
    (SHEET_RADIATED, "rad_leech", (1168, 466, 1356, 684), False),
    (SHEET_RADIATED, "geiger_bug", (786, 590, 902, 684), False),
    (SHEET_RADIATED, "glow_stalker", (976, 596, 1116, 684), False),
    # Molten Chasm
    (SHEET_MOLTEN, "tile_molten_lava", (104, 164, 236, 298), True),
    (SHEET_MOLTEN, "tile_obsidian", (298, 164, 430, 298), True),
    (SHEET_MOLTEN, "lava_rock", (492, 176, 660, 300), False),
    (SHEET_MOLTEN, "pyrocite_rock", (690, 162, 844, 300), False),
    (SHEET_MOLTEN, "wall_blast", (918, 184, 1072, 292), True),
    (SHEET_MOLTEN, "hab_lava", (1160, 160, 1306, 304), False),
    (SHEET_MOLTEN, "magma_lichen", (100, 432, 230, 522), False),
    (SHEET_MOLTEN, "molten_reeds", (308, 422, 426, 530), False),
    (SHEET_MOLTEN, "pyrocite_ore", (498, 430, 604, 528), False),
    (SHEET_MOLTEN, "obsidian_shard", (618, 428, 726, 530), False),
    (SHEET_MOLTEN, "pyro_skitter", (762, 432, 926, 554), False),
    (SHEET_MOLTEN, "magma_turtle", (966, 426, 1162, 564), False),
    (SHEET_MOLTEN, "basalt_golem", (1166, 472, 1364, 680), False),
    (SHEET_MOLTEN, "magma_brazier", (108, 562, 236, 674), False),
    (SHEET_MOLTEN, "charred_timber", (322, 588, 432, 666), False),
    (SHEET_MOLTEN, "timber", (564, 586, 698, 684), False),
    (SHEET_MOLTEN, "lava_leech", (778, 596, 908, 676), False),
    (SHEET_MOLTEN, "ember_rat", (992, 600, 1134, 680), False),
    # Junk Wasteland
    (SHEET_JUNK, "tile_scrap_heap", (100, 160, 240, 302), False),
    (SHEET_JUNK, "broken_girder", (290, 156, 438, 304), False),
    (SHEET_JUNK, "salvaged_electronics", (496, 156, 656, 304), False),
    (SHEET_JUNK, "hab_salvaged", (696, 156, 844, 304), False),
    (SHEET_JUNK, "wall_scrap", (912, 178, 1078, 296), True),
    (SHEET_JUNK, "junk_rock", (1148, 176, 1308, 300), False),
    (SHEET_JUNK, "scrap_iron_ore", (72, 474, 260, 644), False),
    (SHEET_JUNK, "oxidized_shard", (292, 454, 448, 648), False),
    (SHEET_JUNK, "oxidized_ore", (496, 432, 604, 528), False),
    (SHEET_JUNK, "mutant_grass", (628, 430, 718, 526), False),
    (SHEET_JUNK, "mutant_flora", (596, 606, 688, 664), False),
    (SHEET_JUNK, "junk_skitter", (762, 428, 926, 556), False),
    (SHEET_JUNK, "scavenger_crab", (966, 426, 1170, 564), False),
    (SHEET_JUNK, "wasteland_beast", (1166, 466, 1354, 680), False),
    (SHEET_JUNK, "salvage_drone", (776, 588, 922, 684), False),
    (SHEET_JUNK, "scrap_mite", (1008, 608, 1116, 684), False),
    # Biome overview extras
    (SHEET_BIOMES, "tile_grassland", (592, 604, 678, 684), True),
    (SHEET_BIOMES, "tile_mutant_flora", (88, 444, 176, 528), True),
    (SHEET_BIOMES, "tile_lava_rock", (766, 162, 852, 246), True),
    (SHEET_BIOMES, "snow_bank", (196, 166, 286, 252), False),
    (SHEET_BIOMES, "mossy_stone", (718, 602, 820, 688), False),
    (SHEET_BIOMES, "fern_plant", (842, 594, 934, 684), False),
    (SHEET_BIOMES, "astro_timber", (970, 602, 1066, 684), False),
    (SHEET_BIOMES, "neo_terra_deer", (1094, 572, 1200, 684), False),
    (SHEET_BIOMES, "sky_hawk", (1218, 572, 1352, 680), False),
    (SHEET_BIOMES, "snow_wolf_small", (640, 162, 738, 250), False),
    (SHEET_BIOMES, "lava_skitter", (1126, 168, 1216, 248), False),
    (SHEET_BIOMES, "magma_turtle_small", (1236, 160, 1336, 250), False),
]


# Dark sprites on tinted panels: only strip pixels very close to the panel colour.
STRICT_BACKGROUND = {"caustic_rock", "lava_rock", "junk_rock", "obsidian_shard", "tile_scrap_heap", "pyrocite_rock"}

# Natural terrain is not on the sheets, so it is generated in the sheets' palette:
# (name, base, dark, light, top_colour or None). Textures are 16x16 pixel art.
TERRAIN = [
    ("terrain_regolith", (112, 78, 54), (78, 52, 38), (140, 102, 70), None),
    ("terrain_moss", (112, 78, 54), (78, 52, 38), (140, 102, 70), (62, 170, 120)),
    ("terrain_rock", (92, 96, 104), (62, 66, 74), (124, 130, 138), None),
    ("terrain_sand", (196, 170, 112), (160, 134, 84), (222, 198, 140), None),
    ("terrain_dune", (206, 150, 84), (168, 112, 60), (232, 182, 112), None),
    ("terrain_bedrock", (34, 36, 46), (20, 22, 30), (52, 56, 70), None),
    ("terrain_ice", (196, 222, 240), (150, 184, 214), (236, 246, 255), None),
    ("terrain_vine", (54, 70, 50), (34, 46, 32), (80, 104, 66), (70, 150, 60)),
    ("terrain_fungal", (58, 52, 84), (38, 34, 58), (84, 76, 116), (64, 214, 196)),
    ("terrain_ember", (120, 58, 40), (82, 36, 28), (232, 132, 52), None),
    ("terrain_void", (60, 44, 92), (38, 26, 62), (126, 84, 186), None),
    ("terrain_rift", (122, 46, 58), (84, 28, 40), (170, 72, 84), None),
    ("terrain_prism", (86, 92, 128), (60, 64, 96), (150, 220, 236), (178, 236, 255)),
]


def generate_terrain(name, base, dark, light, top):
    import random
    rng = random.Random(name)
    image = Image.new("RGBA", (16, 16), base + (255,))
    pixels = image.load()
    for _ in range(26):
        x, y = rng.randrange(16), rng.randrange(16)
        pixels[x, y] = dark + (255,)
        if rng.random() < 0.5 and x < 15:
            pixels[x + 1, y] = dark + (255,)
    for _ in range(14):
        x, y = rng.randrange(16), rng.randrange(16)
        pixels[x, y] = light + (255,)
    # Bevel so placed blocks read as separate cells.
    for i in range(16):
        pixels[i, 15] = tuple(max(0, c - 18) for c in dark) + (255,)
        pixels[15, i] = tuple(max(0, c - 18) for c in dark) + (255,)
        pixels[i, 0] = tuple(min(255, (c + l) // 2) for c, l in zip(base, light)) + (255,)
    if top:
        for x in range(16):
            depth = 3 + (1 if rng.random() < 0.5 else 0)
            for y in range(depth):
                shade = top if y < depth - 1 else tuple(c * 3 // 4 for c in top)
                pixels[x, y] = shade + (255,)
    return image


def generate_ore_tile(rock, ore):
    tile = rock.copy()
    chunk = ore.copy()
    chunk.thumbnail((12, 12), Image.LANCZOS)
    tile.alpha_composite(chunk, ((16 - chunk.width) // 2, (16 - chunk.height) // 2))
    return tile


def remove_background(image, tolerance=20, follow_shading=True):
    """Flood fill from the border, clearing pixels near the border colour."""
    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    border = [pixels[x, 0] for x in range(width)] + [pixels[x, height - 1] for x in range(width)]
    border += [pixels[0, y] for y in range(height)] + [pixels[width - 1, y] for y in range(height)]
    border.sort(key=lambda p: p[0] + p[1] + p[2])
    reference = border[len(border) // 2]

    def is_background(p):
        return abs(p[0] - reference[0]) + abs(p[1] - reference[1]) + abs(p[2] - reference[2]) <= tolerance * 2

    seen = bytearray(width * height)
    original = rgba.copy().load()

    def follows_shading(p, previous):
        # Panels have soft gradients and faint grid lines; follow small low-saturation steps.
        if previous is None or not follow_shading:
            return False
        step = abs(p[0] - previous[0]) + abs(p[1] - previous[1]) + abs(p[2] - previous[2])
        return step <= 14 and max(p[:3]) - min(p[:3]) < 22 and sum(p[:3]) < 300

    queue = deque()
    for x in range(width):
        queue.append((x, 0, None))
        queue.append((x, height - 1, None))
    for y in range(height):
        queue.append((0, y, None))
        queue.append((width - 1, y, None))
    while queue:
        x, y, previous = queue.popleft()
        index = y * width + x
        if seen[index]:
            continue
        p = original[x, y]
        if not (is_background(p) or follows_shading(p, previous)):
            continue
        seen[index] = 1
        pixels[x, y] = (0, 0, 0, 0)
        if x > 0:
            queue.append((x - 1, y, p))
        if x < width - 1:
            queue.append((x + 1, y, p))
        if y > 0:
            queue.append((x, y - 1, p))
        if y < height - 1:
            queue.append((x, y + 1, p))

    # Drop tiny islands left by JPEG noise: clear opaque pixels with < 2 opaque neighbours.
    alpha = [pixels[x, y][3] for y in range(height) for x in range(width)]
    for y in range(1, height - 1):
        for x in range(1, width - 1):
            if alpha[y * width + x] == 0:
                continue
            neighbours = sum(1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if alpha[(y + dy) * width + x + dx])
            if neighbours < 2:
                pixels[x, y] = (0, 0, 0, 0)

    strip_thin_lines(pixels, width, height)
    bbox = rgba.getbbox()
    return rgba.crop(bbox) if bbox else rgba


def strip_thin_lines(pixels, width, height, min_length=16, max_thickness=2):
    """Erase leftover panel grid lines: long straight runs only 1-2 px thick."""
    def opaque(x, y):
        return 0 <= x < width and 0 <= y < height and pixels[x, y][3] > 0

    doomed = []
    for y in range(height):
        x = 0
        while x < width:
            start = x
            while x < width and opaque(x, y) and not (opaque(x, y - max_thickness) and opaque(x, y + 1))                     and not (opaque(x, y - 1) and opaque(x, y + max_thickness)):
                x += 1
            if x - start >= min_length:
                doomed.extend((i, y) for i in range(start, x))
            x = max(x, start + 1)
    for x in range(width):
        y = 0
        while y < height:
            start = y
            while y < height and opaque(x, y) and not (opaque(x - max_thickness, y) and opaque(x + 1, y))                     and not (opaque(x - 1, y) and opaque(x + max_thickness, y)):
                y += 1
            if y - start >= min_length:
                doomed.extend((x, i) for i in range(start, y))
            y = max(y, start + 1)
    for x, y in doomed:
        pixels[x, y] = (0, 0, 0, 0)


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    out_dir = root / "Assets" / "Sprites"
    out_dir.mkdir(parents=True, exist_ok=True)
    sheets = {}
    results = []
    for sheet, name, box, keep_background in SPRITES:
        if sheet not in sheets:
            sheets[sheet] = Image.open(root / "art" / "sheets" / sheet).convert("RGB")
        if not keep_background and not name.startswith("face_"):
            box = (box[0] + 3, box[1] + 3, box[2] - 3, box[3] - 3)
        crop = sheets[sheet].crop(box)
        strict = name in STRICT_BACKGROUND
        sprite = crop.convert("RGBA") if keep_background else remove_background(
            crop, tolerance=12 if strict else 20, follow_shading=not strict)
        sprite.save(out_dir / f"{name}.png")
        results.append((name, sprite))

    named = dict(results)
    for name, base, dark, light, top in TERRAIN:
        texture = generate_terrain(name, base, dark, light, top)
        texture.save(out_dir / f"{name}.png")
        named[name] = texture
        results.append((name, texture))
    for ore in ("ore_red", "ore_blue", "ore_green", "glacial_ore", "uranium_ore", "pyrocite_ore", "scrap_iron_ore"):
        texture = generate_ore_tile(named["terrain_rock"], named[ore])
        texture.save(out_dir / f"terrain_{ore}.png")
        results.append((f"terrain_{ore}", texture))
    # Blight ground variants: tinted copies of existing terrain so spread reads at a glance.
    def tinted(source, tint, strength):
        base = source.convert("RGBA")
        overlay = Image.new("RGBA", base.size, tint + (255,))
        mixed = Image.blend(base, overlay, strength)
        mixed.putalpha(base.getchannel("A"))
        return mixed

    blight_tiles = {
        "tile_radiated_sand": tinted(named["terrain_dune"], (90, 200, 60), 0.35),
        "tile_radiated_ice": tinted(named["tile_glacial_ice"], (120, 220, 90), 0.30),
        "tile_overgrowth_soil": named["tile_mutant_flora"],
        "tile_overgrowth_rock": tinted(named["terrain_rock"], (70, 150, 60), 0.35),
        "tile_overgrowth_sand": tinted(named["terrain_dune"], (90, 140, 50), 0.40),
        "tile_overgrowth_ice": tinted(named["tile_glacial_ice"], (80, 160, 90), 0.35),
    }
    blight_tiles["purifier_sprayer"] = tinted(named["plasma_pistol"], (90, 230, 160), 0.45)
    blight_tiles["purifier_gel"] = tinted(named["frost_crystal"], (110, 240, 140), 0.45)
    for name, texture in blight_tiles.items():
        texture.save(out_dir / f"{name}.png")
        results.append((name, texture))

    crystal = generate_ore_tile(named["terrain_rock"], named["energy_crystals"])
    crystal.save(out_dir / "terrain_crystal.png")
    results.append(("terrain_crystal", crystal))

    biome_names = {name for sheet, name, _, _ in SPRITES if sheet in BIOME_SHEETS}
    write_preview([r for r in results if r[0] not in biome_names], out_dir.parent / "_sprite_preview.png")
    write_preview([r for r in results if r[0] in biome_names], out_dir.parent / "_sprite_preview_biomes.png")
    print(f"Wrote {len(results)} sprites to {out_dir}")


def write_preview(results, path):
    """Contact sheet for review."""
    cell = 160
    columns = 10
    rows = (len(results) + columns - 1) // columns
    preview = Image.new("RGBA", (columns * cell, rows * cell), (255, 0, 255, 255))
    for i, (name, sprite) in enumerate(results):
        thumb = sprite.copy()
        thumb.thumbnail((cell - 8, cell - 8))
        x = (i % columns) * cell + (cell - thumb.width) // 2
        y = (i // columns) * cell + (cell - thumb.height) // 2
        preview.alpha_composite(thumb, (x, y))
    preview.save(path)


if __name__ == "__main__":
    main()
