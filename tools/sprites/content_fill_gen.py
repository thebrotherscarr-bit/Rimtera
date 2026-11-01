"""Generates Rimtera's content-fill creature tables (C#), enum blocks and kitbash recipes from one data table.

Usage: python tools/sprites/content_fill_gen.py <project_dir> <out_dir>
Writes: <project>/Systems/Content/ContentFillData.cs, <out>/enemy_enum.txt, <out>/item_enum.txt, <out>/kitbash_block.py
All names and numbers are original Rimtera data.
"""
import re
import sys
from pathlib import Path

PROJECT = Path(sys.argv[1])
OUT = Path(sys.argv[2])
SPRITES = PROJECT / "Assets" / "Sprites"
PREFIXES = ["", "weapon_", "tech_", "cmd_", "armor_", "ammo_", "item_", "tool_", "station_", "furn_", "struct_", "npc_",
            "critter_", "event_", "creature_", "boss_", "minion_", "sentry_", "deco_", "flora_"]

GOLD = '("hue", 0.12, 1.3), ("val", 1.15), ("glow", (255, 215, 90))'
U = 0.29

# ---- Critters: (Kind, Name, w, h, fly, swim, roster, day, night, bait, goldOf, base, steps) ----
CRITTERS = [
    # Grassland surface (day / night) and pond water.
    ("RegolithVole", "Regolith vole", .6, .45, 0, 0, "grass", .05, .02, 10, None, "terra_rat", '("tint", (150, 120, 80), 0.35)'),
    ("SunbeamMoth", "Sunbeam moth", .45, .4, 1, 0, "grass", .04, 0, 18, None, "spore_moth", '("hue", 0.13, 1.2), ("val", 1.15)'),
    ("ThistleFinch", "Thistle finch", .5, .4, 1, 0, "grass", .04, 0, 12, None, "sky_flier", '("hue", 0.2), ("scale", 0.8)'),
    ("MeadowCricket", "Meadow cricket", .45, .35, 0, 0, "grass", .05, .03, 16, None, "pebble_beetle", '("hue", 0.3, 1.2)'),
    ("PuffballTortoise", "Puffball tortoise", .7, .5, 0, 0, "grass", .02, .01, 8, None, "cap_turtle", '("scale", 0.5), ("hue", 0.1)'),
    ("DuskGlider", "Dusk glider", .6, .45, 1, 0, "grass", 0, .04, 12, None, "sky_hawk", '("val", 0.55), ("hue", 0.7)'),
    ("LampWorm", "Lamp worm", .5, .25, 0, 0, "grass", 0, .05, 22, None, "worm", '("hue", 0.15), ("glow", (255, 230, 120))'),
    ("MoonhopToad", "Moonhop toad", .5, .45, 0, 0, "grass", 0, .04, 20, None, "frog", '("hue", 0.72), ("val", 0.9)'),
    ("StarMoth", "Star moth", .45, .45, 1, 0, "grass", 0, .04, 24, None, "spore_moth", '("hue", 0.6), ("glow", (150, 190, 255))'),
    ("PondMinnow", "Pond minnow", .45, .3, 1, 1, "grass", .03, .02, 12, None, "fish_silver", '("scale", 0.7), ("hue", 0.4)'),
    ("BubbleTadpole", "Bubble tadpole", .4, .25, 1, 1, "grass", .02, .02, 10, None, "frog", '("scale", 0.5), ("val", 0.7)'),
    # Underground (soil layer) and caverns.
    ("BurrowMole", "Burrow mole", .6, .45, 0, 0, "under", .06, .06, 10, None, "terra_rat", '("val", 0.55)'),
    ("RootGrub", "Root grub", .5, .35, 0, 0, "under", .06, .06, 14, None, "glow_grub", '("tint", (140, 100, 60), 0.45)'),
    ("GravelSnail", "Gravel snail", .5, .4, 0, 0, "under", .05, .05, 16, None, "snail", '("sat", 0.2), ("val", 0.85)'),
    ("GemTick", "Gem tick", .45, .35, 0, 0, "cave", .05, .05, 20, None, "truffle_mite", '("hue", 0.8, 1.3), ("glow", (210, 150, 255))'),
    ("EchoFlitter", "Echo flitter", .5, .4, 1, 0, "cave", .05, .05, 12, None, "lantern_bat", '("scale", 0.6), ("sat", 0.4)'),
    ("BlindCavefish", "Blind cavefish", .5, .3, 1, 1, "cave", .03, .03, 15, None, "fish_silver", '("sat", 0.1), ("val", 1.2)'),
    ("CaveSalamander", "Cave salamander", .7, .3, 0, 0, "cave", .04, .04, 14, None, "ember_newt", '("hue", 0.95, 0.5), ("val", 1.2)'),
    ("GlowSlug", "Glow slug", .55, .3, 0, 0, "cave", .04, .04, 26, None, "glow_snail", '("hue", 0.42), ("glow", (120, 255, 200))'),
    # Frozen wasteland.
    ("FrostVole", "Frost vole", .6, .45, 0, 0, "frozen", .05, .03, 10, None, "terra_rat", '("tint", (230, 240, 255), 0.55)'),
    ("IcicleBeetle", "Icicle beetle", .45, .35, 0, 0, "frozen", .04, .04, 18, None, "pebble_beetle", '("hue", 0.55), ("val", 1.25)'),
    ("SnowOwlet", "Snow owlet", .5, .5, 1, 0, "frozen", .02, .04, 12, None, "owl", '("tint", (240, 245, 255), 0.6)'),
    ("RimeFish", "Rime fish", .5, .3, 1, 1, "frozen", .02, .02, 14, None, "fish_glow", '("hue", 0.55), ("val", 1.2)'),
    # Junk wasteland.
    ("RustScarab", "Rust scarab", .5, .4, 0, 0, "junk", .05, .03, 18, None, "scavenger_beetle", '("hue", 0.06, 1.2)'),
    ("WireMouse", "Wire mouse", .5, .4, 0, 0, "junk", .04, .04, 10, None, "scrap_rat", '("val", 1.2), ("hue", 0.55, 0.5)'),
    ("SprocketFly", "Sprocket fly", .4, .35, 1, 0, "junk", .04, .02, 12, None, "dragonfly", '("hue", 0.08), ("sat", 0.6)'),
    # Fuel sea shore and water.
    ("TideFlea", "Tide flea", .4, .3, 0, 0, "coast", .04, .03, 16, None, "flea_hopper", '("scale", 0.6), ("hue", 0.1)'),
    ("HermitBot", "Hermit crab-bot", .55, .45, 0, 0, "coast", .04, .03, 14, None, "crab_small", '("hue", 0.55), ("val", 1.1)'),
    ("FuelSeahorse", "Fuel seahorse", .4, .55, 1, 1, "coast", .03, .03, 22, None, "fish_glow", '("hue", 0.11, 1.3), ("rot", 80)'),
    ("ReefStar", "Reef star", .5, .3, 1, 1, "coast", .03, .03, 18, None, "glow_jelly", '("hue", 0.98), ("scale", 0.7)'),
    # Verdant canopy.
    ("CanopyGecko", "Canopy gecko", .7, .3, 0, 0, "canopy", .05, .03, 14, None, "dune_lizard", '("hue", 0.33, 1.2)'),
    ("LeafbackBeetle", "Leafback beetle", .45, .35, 0, 0, "canopy", .04, .04, 20, None, "ladybug_bot", '("hue", 0.3)'),
    ("OrchidMoth", "Orchid moth", .45, .45, 1, 0, "canopy", .03, .04, 22, None, "prism_butterfly", '("hue", 0.88)'),
    ("MireSkipper", "Mire skipper", .5, .3, 1, 1, "canopy", .03, .03, 16, None, "fish_silver", '("tint", (110, 85, 55), 0.5)'),
    # Fungal hollows.
    ("PuffcapHopper", "Puffcap hopper", .5, .45, 0, 0, "fungal", .04, .04, 22, None, "cave_frog", '("hue", 0.52), ("glow", (90, 200, 255))'),
    ("MyceliumWorm", "Mycelium worm", .5, .25, 0, 0, "fungal", .04, .04, 24, None, "worm", '("hue", 0.5), ("val", 1.2)'),
    ("SporeFirefly", "Spore firefly", .4, .4, 1, 0, "fungal", .03, .03, 26, None, "glow_firefly", '("hue", 0.5), ("glow", (90, 220, 255))'),
    # Crystal spires.
    ("ShardMoth", "Shard moth", .45, .45, 1, 0, "crystal", .04, .04, 30, None, "prism_butterfly", '("hue", 0.55), ("val", 1.2)'),
    ("LumenFish", "Lumen fish", .5, .3, 1, 1, "crystal", .03, .03, 30, None, "fish_glow", '("hue", 0.85), ("glow", (255, 170, 240))'),
    # Blight (radiated and overgrowth) and the Irradiated Core.
    ("MutantRoach", "Mutant roach", .5, .35, 0, 0, "rad", .05, .05, 22, None, "scavenger_beetle", f'("hue", {U}), ("glow", (120, 255, 80))'),
    ("GlowToad", "Glow toad", .5, .45, 0, 0, "rad", .03, .04, 20, None, "frog", f'("hue", {U}, 1.3), ("glow", (120, 255, 80))'),
    ("Rootworm", "Rootworm", .55, .25, 0, 0, "over", .05, .05, 24, None, "worm", '("hue", 0.22), ("val", 0.7)'),
    ("IsotopeBeetle", "Isotope beetle", .5, .4, 0, 0, "core", .05, .05, 28, None, "pebble_beetle", f'("hue", {U}, 1.4), ("glow", (120, 255, 80))'),
    ("FissionMoth", "Fission moth", .45, .45, 1, 0, "core", .04, .04, 26, None, "spore_moth", f'("hue", {U}, 1.3), ("glow", (120, 255, 80))'),
]
# Gold variants: (Kind, Name, base kind) -- each spawns at GOLD_SHARE of its base critter's weight.
GOLDS = [
    ("GildedVole", "Gilded vole", "RegolithVole"), ("GildedCricket", "Gilded cricket", "MeadowCricket"),
    ("GildedFinch", "Gilded finch", "ThistleFinch"), ("GildedMinnow", "Gilded minnow", "PondMinnow"),
    ("GildedGecko", "Gilded gecko", "CanopyGecko"), ("GildedScarab", "Gilded scarab", "RustScarab"),
    ("GildedHopBunny", "Gilded hop bunny", "HopBunny"), ("GildedSquirrelBot", "Gilded squirrel bot", "SquirrelBot"),
    ("GildedFrog", "Gilded frog", "Frog"),
]
GOLD_SHARE = 0.07
# Existing critters that gain ambient spawn slots: (Kind, roster, day, night, base sprite for gold recolours, w, h, fly, swim)
EXISTING = {
    "HopBunny": ("grass", .05, .02, "hop_bunny", .6, .55, 0, 0), "SquirrelBot": ("grass", .04, 0, "squirrel_bot", .6, .55, 0, 0),
    "Frog": ("grass", .03, .04, "frog", .5, .4, 0, 0), "ButterflyBlue": ("grass", .04, 0, None, 0, 0, 0, 0),
    "LadybugBot": ("grass", .03, 0, None, 0, 0, 0, 0), "GrassSnake": ("grass", .02, .01, None, 0, 0, 0, 0),
    "Snail": ("grass", .02, .03, None, 0, 0, 0, 0), "Duck": ("grass", .02, 0, None, 0, 0, 0, 0),
    "Owl": ("grass", 0, .03, None, 0, 0, 0, 0), "GlowFirefly": ("grass", 0, .05, None, 0, 0, 0, 0),
    "Dragonfly": ("grass", .02, 0, None, 0, 0, 0, 0), "ButterflyGold": ("grass", .003, 0, None, 0, 0, 0, 0),
    "GoldenCritter": ("grass", .002, .002, None, 0, 0, 0, 0),
    "Worm": ("under", .05, .05, None, 0, 0, 0, 0), "TerraRat": ("under", .04, .04, None, 0, 0, 0, 0),
    "GlowGrub": ("cave", .04, .04, None, 0, 0, 0, 0), "PebbleBeetle": ("cave", .04, .04, None, 0, 0, 0, 0),
    "SnowHare": ("frozen", .04, .03, None, 0, 0, 0, 0), "PenguinDrone": ("frozen", .03, .02, None, 0, 0, 0, 0),
    "DuneLizard": ("junk", .04, .02, None, 0, 0, 0, 0), "ScrapRat": ("junk", .03, .04, None, 0, 0, 0, 0),
    "SandSkitterer": ("junk", .03, .02, None, 0, 0, 0, 0), "ScavengerBeetle": ("junk", .03, .03, None, 0, 0, 0, 0),
    "SkyFlier": ("grass", .02, 0, None, 0, 0, 0, 0),
}

# ---- Hostiles: (Kind, Name, roster, hard, day, night, family, w, h, hp, dmg, fly, swim, credits, segments, drops, base, steps,
#      defense) ----
# family: G ground, F flyer, H hopper, L leaping hopper, S skittering hopper, W worm, C caster, D diving flyer, M mimic, T stationary
H = []

# Defense tier table (docs/roadmap/0.1.9.md A1; the same table as CreatureStatTable.TierDefense in Entities/ContentCreatures.cs):
# pre-boss 0-6 (gel 2, walker 6), mid pre-Fracture 8-14, post-Fracture 16-30, all by hit points.
WALKERS = {"GraveHusk"}  # Terraria's zombie: 6 even at low hit points


def tier_defense(kind, hard, hp):
    if hard:
        for limit, value in ((200, 16), (250, 18), (300, 20), (350, 22), (400, 24), (500, 26), (1000, 28)):
            if hp < limit:
                return value
        return 30
    if kind in WALKERS:
        return 6
    for limit, value in ((45, 2), (60, 4), (100, 6), (150, 8), (250, 10), (400, 12)):
        if hp < limit:
            return value
    return 14


def h(kind, name, roster, hard, day, night, fam, w, hh, hp, dmg, credits, drops, base, steps, segs=0, swim=0, defense=None):
    if hard:
        # Hard versions sit in the existing hard band. These numbers are final: since 0.1.9 (D-18) the Fracture no
        # longer rescales creatures authored for it.
        hp, dmg = int(round(hp * 0.55 / 5.0) * 5), int(round(dmg * 0.72))
    fly = fam in ("F", "D", "C") and fam != "C" or swim == 1
    if defense is None:
        defense = tier_defense(kind, hard, hp)
    H.append((kind, name, roster, hard, day, night, fam, w, hh, hp, dmg, fly, swim, credits, segs, drops, base, steps, defense))

# Grassland surface.
h("ThistleBoar", "Thistle boar", "grass", 0, .07, .03, "G", 1.3, .9, 55, 12, 3, "Berry:2", "astro_beast", '("hue", 0.1), ("scale", 0.8)')
h("MeadowGel", "Meadow gel", "grass", 0, .08, .03, "H", .9, .65, 28, 8, 1, "BioGel:2", "gel_hopper", '("hue", 0.33)')
h("HornetDrone", "Hornet drone", "grass", 0, .06, 0, "F", .7, .6, 22, 8, 2, "EnergyCrystal:1", "salvage_drone", '("hue", 0.13, 1.3)')
h("BoreBeetle", "Bore beetle", "grass", 0, .05, .03, "S", .8, .5, 30, 9, 2, "CopperOre:2", "flea_hopper", '("scale", 1.4), ("tint", (120, 80, 40), 0.4)')
h("DuskProwler", "Dusk prowler", "grass", 0, 0, .07, "G", 1.2, 1, 60, 14, 4, "SilkThread:2", "snow_wolf", '("val", 0.5), ("hue", 0.75)')
h("UmbralGel", "Umbral gel", "grass", 0, 0, .07, "H", .9, .65, 34, 10, 2, "BioGel:2", "gel_hopper", '("hue", 0.72), ("val", 0.45)')
h("GloomKite", "Gloom kite", "grass", 0, 0, .05, "D", 1, .6, 40, 12, 3, "SilkThread:1", "dive_kite", '("val", 0.5)')
h("GraveHusk", "Grave husk", "grass", 0, 0, .08, "G", .9, 1.7, 50, 13, 3, "ClothRoll:1", "event_husk", '("hue", 0.55)')
h("DriftingEye", "Drifting eye", "grass", 0, 0, .06, "F", .8, .8, 30, 10, 2, "EnergyCrystal:1", "event_eye_drone", '("hue", 0.8)')
h("ThornBehemoth", "Thorn behemoth", "grass", 1, .12, .1, "G", 1.8, 1.6, 420, 40, 18, "ViridiumOre:3", "wasteland_beast", '("hue", 0.3)')
h("StormHarrier", "Storm harrier", "grass", 1, .12, .06, "D", 1.4, .6, 260, 34, 12, "PowerCell:1", "storm_ray", '("hue", 0.6), ("glow", (140, 200, 255))')
h("NightRevenant", "Night revenant", "grass", 1, 0, .16, "G", 1, 1.8, 380, 42, 16, "VoidEssence:1", "event_corrupted_survivor", '("val", 0.6)')
h("ShockGel", "Shock gel", "grass", 1, .1, .1, "L", 1.2, 1, 300, 36, 12, "BioGel:5", "boulder_hopper", '("hue", 0.55), ("glow", (120, 220, 255))')
h("SkyLancer", "Sky lancer", "grass", 1, .1, .04, "F", 1.2, 1, 240, 32, 12, "PowerCell:1", "terraformer_drone", '("hue", 0.1)')
# Underground soil layer.
h("TunnelRat", "Tunnel rat", "under", 0, .08, .08, "G", .8, .55, 32, 10, 2, "CopperOre:1", "mutant_rat", '("tint", (120, 90, 60), 0.4)')
h("BurrowSpider", "Burrow spider", "under", 0, .07, .07, "G", 1, .6, 40, 12, 3, "SilkThread:2", "silk_spider_mite", '("hue", 0.08)')
h("ClayGolem", "Clay golem", "under", 0, .05, .05, "G", 1.2, 1.6, 90, 15, 5, "ClayBlock:6", "security_droid", '("tint", (170, 110, 80), 0.5)')
h("GrubWorm", "Grub worm", "under", 0, .05, .05, "W", .8, .8, 70, 11, 4, "CopperOre:3", "dune_borer", '("hue", 0.08)', segs=5)
h("MudGel", "Mud gel", "under", 0, .07, .07, "H", .9, .65, 36, 10, 2, "BioGel:2,MudBlock:2", "gel_hopper", '("tint", (110, 80, 50), 0.6)')
h("DrillMite", "Drill mite", "under", 0, .06, .06, "S", .7, .5, 26, 9, 2, "CobaltOre:1", "rock_mite", '("hue", 0.55)')
h("BedrockCrusher", "Bedrock crusher", "cave", 1, .14, .14, "G", 1.6, 2, 520, 44, 18, "CobaltOre:6", "basalt_golem", '("val", 0.7), ("hue", 0.6, 0.5)')
h("DelveWyrm", "Delve wyrm", "cave", 1, .1, .1, "W", 1.1, 1.1, 700, 42, 22, "CobaltOre:8,EnergyCrystal:2", "tunnel_wyrm", '("hue", 0.08)', segs=10)
h("ShadeCaster", "Shade caster", "cave", 1, .12, .12, "C", .9, 1.7, 340, 38, 14, "VoidEssence:1", "rift_caster", '("val", 0.5), ("hue", 0.7)')
# Caverns.
h("GemCrawler", "Gem crawler", "cave", 0, .07, .07, "G", 1.1, .8, 55, 13, 4, "AmethystShard:1", "geode_crab", '("hue", 0.78)')
h("CaveLurker", "Cave lurker", "cave", 0, .06, .06, "F", .8, .6, 48, 13, 3, "SilkThread:2", "lantern_bat", '("val", 0.6)')
h("SlateGel", "Slate gel", "cave", 0, .07, .07, "H", .9, .65, 50, 13, 2, "BioGel:2,RockBlock:3", "gel_hopper", '("sat", 0.2), ("val", 0.7)')
h("EchoCaster", "Echo caster", "cave", 0, .04, .04, "C", .9, 1.7, 70, 14, 6, "SapphireShard:1", "rift_caster", '("hue", 0.55)')
h("CrystalBorer", "Crystal borer", "cave", 0, .04, .04, "W", .9, .9, 110, 14, 6, "EnergyCrystal:2", "tunnel_wyrm", '("hue", 0.5)', segs=6)
h("HollowBat", "Hollow bat", "cave", 0, .06, .06, "F", .8, .6, 34, 12, 2, "CobaltOre:1", "halo_bat", '("val", 0.6), ("sat", 0.3)')
h("UrnMimic", "Urn mimic", "cave", 1, .05, .05, "M", 1, 1, 380, 34, 40, "TopazShard:3,Credits:60", "cache_mimic", '("hue", 0.1), ("scale", 0.8)')
h("GeodeTitan", "Geode titan", "cave", 1, .12, .12, "G", 1.7, 2.1, 620, 46, 22, "DiamondShard:2,RubyShard:2", "pearl_golem", '("hue", 0.95)')
h("VoidMantis", "Void mantis", "cave", 1, .1, .1, "L", 1.2, 1.2, 460, 48, 18, "VoidEssence:2", "jungle_stalker", '("hue", 0.75), ("val", 0.7)')
h("RunicCaster", "Runic caster", "cave", 1, .1, .1, "C", .9, 1.6, 380, 42, 16, "VoidEssence:1,EmeraldShard:2", "mycelid_shaman", '("hue", 0.45)')
h("ArmoredDelver", "Armored delver", "cave", 1, .1, .1, "G", 1.5, 2, 520, 44, 18, "CobaltOre:10", "event_corroded_mech", '("hue", 0.55)')
h("StrongboxMimic", "Strongbox mimic", "cave", 1, .03, .03, "M", 1, 1, 700, 52, 80, "Credits:300,DiamondShard:3", "cache_mimic", '("hue", 0.14, 1.3), ("glow", (255, 215, 90))')
# Frozen wasteland.
h("RimeGel", "Rime gel", "frozen", 0, .08, .06, "H", .9, .65, 32, 9, 1, "FrostCrystal:1,BioGel:1", "gel_hopper", '("hue", 0.55), ("val", 1.2)')
h("FrostFlitter", "Frost flitter", "frozen", 0, .06, .08, "F", .8, .6, 26, 9, 2, "FrostCrystal:1", "lantern_bat", '("hue", 0.55)')
h("TundraBrute", "Tundra brute", "frozen", 0, .05, .06, "G", 1.6, 1.5, 120, 17, 6, "GlacialOre:3", "wasteland_beast", '("hue", 0.55), ("val", 1.2)')
h("DriftLeaper", "Drift leaper", "frozen", 0, .06, .05, "L", 1.2, 1, 70, 14, 4, "SnowBlock:4", "boulder_hopper", '("tint", (235, 245, 255), 0.6)')
h("CryoCaster", "Cryo caster", "frozen", 0, .04, .05, "C", .9, 1.7, 80, 15, 7, "FrostCrystal:3", "rift_caster", '("hue", 0.53)')
h("PermafrostBorer", "Permafrost borer", "frozen", 0, .04, .04, "W", .8, .8, 120, 14, 6, "GlacialOre:4", "dune_borer", '("hue", 0.55)', segs=6)
h("GlacierTitan", "Glacier titan", "frozen", 1, .14, .14, "G", 1.6, 2, 640, 48, 22, "GlacialOre:8,FrostCrystal:4", "glacial_golem", '("val", 0.8), ("glow", (150, 220, 255))')
h("BlizzardWraith", "Blizzard wraith", "frozen", 1, .1, .14, "F", 1, 1.5, 380, 40, 14, "FrostCrystal:4", "crystal_wraith", '("hue", 0.55)')
h("FrozenMimic", "Frozen mimic", "frozen", 1, .04, .04, "M", 1, 1, 650, 50, 60, "FrostCrystal:6,Credits:150", "cache_mimic", '("tint", (200, 230, 255), 0.55)')
h("RimeDiver", "Rime diver", "frozen", 1, .1, .1, "D", 1, .6, 320, 38, 12, "FrostCrystal:2", "dive_kite", '("hue", 0.55)')
# Junk wasteland.
h("RustGel", "Rust gel", "junk", 0, .08, .06, "H", .9, .65, 34, 10, 1, "OxidizedShard:1,BioGel:1", "gel_hopper", '("hue", 0.06)')
h("ScrapHound", "Scrap hound", "junk", 0, .07, .08, "G", 1.3, .9, 60, 14, 4, "ScrapIronOre:2", "event_rust_hound", '("hue", 0.08)')
h("BoltDrone", "Bolt drone", "junk", 0, .06, .04, "F", .8, .7, 36, 11, 3, "DroneSpareParts:1", "salvage_drone", '("hue", 0.15)')
h("DunePhantom", "Dune phantom", "junk", 0, 0, .07, "F", 1, 1.6, 50, 13, 4, "OxidizedShard:2", "event_dust_devil", '("hue", 0.1)')
h("JunkTinker", "Junk tinker", "junk", 0, .04, .04, "C", .9, 1.7, 70, 14, 6, "CircuitBoard:1", "rift_caster", '("tint", (200, 120, 50), 0.45)')
h("SlagBorer", "Slag borer", "junk", 0, .04, .04, "W", .9, .9, 140, 15, 7, "ScrapIronOre:4", "dune_borer", '("tint", (150, 80, 40), 0.5)', segs=7)
h("JunkPileMimic", "Junk-pile mimic", "junk", 1, .05, .05, "M", 1, 1, 600, 48, 50, "ScrapCrate:1,CircuitBoard:3", "cache_mimic", '("tint", (150, 90, 50), 0.5)')
h("ScrapColossus", "Scrap colossus", "junk", 1, .12, .12, "G", 1.6, 2, 700, 50, 24, "ScrapIronOre:10,CircuitBoard:3", "event_scrap_elemental", '("val", 0.8)')
h("RustReaper", "Rust reaper", "junk", 1, .1, .12, "F", .9, 1.4, 420, 44, 16, "OxidizedShard:6", "event_rust_wraith", '("hue", 0.07)')
h("DuneDevourer", "Dune devourer", "junk", 1, .1, .1, "W", 1.1, 1.1, 900, 46, 26, "ScrapIronOre:8,PowerCell:2", "dune_borer", '("scale", 1.4), ("hue", 0.1)', segs=12)
# Fuel sea (shore and swimmers).
h("FuelGel", "Fuel gel", "coast", 0, .06, .04, "H", .9, .65, 34, 10, 1, "BioGel:2", "gel_hopper", '("hue", 0.11)')
h("PincerCrab", "Pincer crab", "coast", 0, .06, .05, "G", 1.1, .7, 60, 14, 4, "CoralShard:2", "scavenger_crab", '("hue", 0.96)')
h("GullDiver", "Gull diver", "coast", 0, .05, 0, "D", .8, .5, 40, 12, 3, "CoralShard:1", "sea_bird", '("val", 0.8), ("hue", 0.1, 0.4)')
h("FuelEel", "Fuel eel", "coast", 0, .06, .07, "F", 1.2, .5, 70, 15, 4, "TideFish:1", "tide_leech", '("hue", 0.1)', swim=1)
h("BarbRay", "Barb ray", "coast", 0, .05, .05, "F", 1.3, .5, 60, 14, 4, "BioGel:2", "storm_ray", '("hue", 0.1)', swim=1)
h("InkSquid", "Ink squid", "coast", 0, .04, .06, "F", .8, .8, 55, 13, 4, "BioGel:3", "glow_jelly", '("hue", 0.8), ("val", 0.7)', swim=1)
h("DeepMaw", "Deep maw", "coast", 1, .14, .14, "F", 2, .9, 700, 52, 24, "TideFish:3,CoralShard:6", "acid_shark", '("val", 0.6)', swim=1)
h("ReefHorror", "Reef horror", "coast", 1, .1, .1, "G", 1.3, 1.7, 620, 48, 22, "CoralReefBlock:8", "coral_sentinel", '("val", 0.7)')
h("TideCaller", "Tide caller", "coast", 1, .1, .1, "C", .9, 1.7, 400, 40, 16, "CoralShard:4", "drowned_diver", '("hue", 0.5)')
# Verdant canopy and bloom hives.
h("VineGel", "Vine gel", "canopy", 0, .07, .05, "H", .9, .65, 36, 11, 1, "JungleRootBlock:2", "gel_hopper", '("hue", 0.3, 1.2)')
h("HornBeetle", "Horn beetle", "canopy", 0, .05, .05, "G", 1.1, .8, 80, 15, 4, "ViridiumOre:1", "cap_turtle", '("hue", 0.3)')
h("CanopyBat", "Canopy bat", "canopy", 0, .04, .08, "F", .8, .6, 38, 11, 2, "Berry:1", "lantern_bat", '("hue", 0.3)')
h("SpearFrog", "Spear frog", "canopy", 0, .05, .05, "L", .8, .7, 60, 14, 3, "BioGel:2", "leaf_frog", '("scale", 1.6), ("hue", 0.95)')
h("RootLasher", "Root lasher", "canopy", 0, .04, .04, "W", .9, .9, 120, 14, 6, "JungleRootBlock:4", "dune_borer", '("hue", 0.3)', segs=6)
h("DartLizard", "Dart lizard", "canopy", 0, .05, .04, "S", .9, .4, 46, 12, 3, "MudBlock:2", "dune_lizard", '("hue", 0.3), ("scale", 1.3)')
h("ThornChanter", "Thorn chanter", "canopy", 0, .03, .04, "C", .9, 1.6, 80, 15, 7, "BioGel:3", "mycelid_shaman", '("hue", 0.3)')
h("MossHornet", "Moss hornet", "canopy", 1, .12, .1, "F", 1, .9, 360, 42, 14, "NectarBlock:2,ViridiumOre:2", "hive_guard", '("hue", 0.3)')
h("CanopyWyrm", "Canopy wyrm", "canopy", 1, .08, .08, "W", 1.1, 1.1, 900, 46, 26, "ViridiumOre:6", "tunnel_wyrm", '("hue", 0.3)', segs=12)
h("StranglerBloom", "Strangler bloom", "canopy", 1, .08, .08, "T", 1.1, 1.5, 500, 46, 18, "BioGel:6", "snapjaw_plant", '("hue", 0.9)')
h("SilverbackBrute", "Silverback brute", "canopy", 1, .1, .1, "G", 1.5, 1.7, 600, 48, 20, "Berry:6,ClothRoll:2", "canopy_ape", '("val", 0.6), ("sat", 0.4)')
h("CombGel", "Comb gel", "hive", 0, .2, .2, "H", .9, .65, 40, 12, 2, "HiveCombBlock:2", "gel_hopper", '("hue", 0.12, 1.3)')
h("BroodLarva", "Brood larva", "hive", 0, .15, .15, "G", 1, .6, 50, 11, 2, "NectarBlock:1", "glow_grub", '("scale", 2), ("hue", 0.12)')
h("HiveWarden", "Hive warden", "hive", 1, .3, .3, "D", 1.1, 1, 420, 44, 16, "NectarBlock:3,HiveCombBlock:4", "hive_guard", '("val", 0.7)')
# Fungal hollows.
h("SporeGel", "Spore gel", "fungal", 0, .08, .08, "H", .9, .65, 36, 11, 1, "GlowSpore:2", "gel_hopper", '("hue", 0.5), ("glow", (90, 200, 255))')
h("ToadstoolLeaper", "Toadstool leaper", "fungal", 0, .06, .06, "L", 1, .8, 70, 14, 4, "MushroomCapBlock:3", "cap_turtle", '("hue", 0.55)')
h("MyceliumBorer", "Mycelium borer", "fungal", 0, .04, .04, "W", .9, .9, 110, 13, 6, "SporeBlockBlock:4", "dune_borer", '("hue", 0.5)', segs=6)
h("GlowcapMite", "Glowcap mite", "fungal", 0, .06, .06, "S", .6, .45, 30, 10, 2, "GlowSpore:1", "truffle_mite", '("hue", 0.5), ("scale", 1.4)')
h("CapSentinel", "Cap sentinel", "fungal", 0, .04, .04, "T", .9, 1.2, 90, 14, 5, "MushroomCapBlock:4", "spitting_orchid", '("hue", 0.5)')
h("MycelidHulk", "Mycelid hulk", "fungal", 1, .12, .12, "G", 1.6, 2, 640, 48, 22, "SporeBlockBlock:8,GlowSpore:6", "spore_golem", '("val", 0.7)')
h("SporePhantom", "Spore phantom", "fungal", 1, .1, .1, "F", 1, 1.5, 380, 42, 14, "GlowSpore:6", "crystal_wraith", '("hue", 0.5)')
h("PuffBomber", "Puff bomber", "fungal", 1, .1, .1, "D", .9, .9, 340, 40, 12, "GlowSpore:4", "puff_drifter", '("val", 0.8), ("hue", 0.45)')
# Crystal spires.
h("PrismGel", "Prism gel", "crystal", 0, .08, .08, "H", .9, .65, 120, 24, 6, "PrismShard:1", "gel_hopper", '("hue", 0.85), ("glow", (255, 170, 240))')
h("ShardLeaper", "Shard leaper", "crystal", 0, .06, .06, "L", 1.2, 1, 160, 28, 8, "PrismShard:2", "boulder_hopper", '("hue", 0.85)')
h("LumenCaster", "Lumen caster", "crystal", 0, .04, .06, "C", .9, 1.7, 200, 30, 10, "PrismShard:2", "rift_caster", '("hue", 0.85), ("val", 1.3)')
h("PearlWyrm", "Pearl wyrm", "crystal", 0, .04, .04, "W", 1, 1, 320, 30, 12, "PearlStoneBlock:6", "tunnel_wyrm", '("hue", 0.85), ("val", 1.2)', segs=8)
h("GlassKite", "Glass kite", "crystal", 0, .06, .04, "D", 1, .6, 150, 26, 8, "PrismShard:1", "dive_kite", '("hue", 0.85)')
h("PrismColossus", "Prism colossus", "crystal", 1, .1, .1, "G", 1.7, 2.1, 800, 54, 26, "LumeniteOre:6,PrismShard:6", "pearl_golem", '("hue", 0.8), ("glow", (255, 170, 240))')
h("HaloSeraph", "Halo seraph", "crystal", 1, .1, .1, "F", 1.2, 1, 500, 50, 20, "PrismShard:6", "light_wisp_bird", '("scale", 2), ("hue", 0.12)')
h("ChromaSerpent", "Chroma serpent", "crystal", 1, .08, .08, "W", 1.1, 1.1, 1000, 50, 30, "LumeniteOre:8", "tunnel_wyrm", '("hue", 0.9), ("glow", (255, 150, 230))', segs=14)
# Blight: radiated deeps.
h("IsotopeGel", "Isotope gel", "rad", 0, .08, .08, "H", .9, .65, 40, 12, 1, "CausticShard:1", "gel_hopper", f'("hue", {U}), ("glow", (120, 255, 80))')
h("FalloutHound", "Fallout hound", "rad", 0, .07, .07, "G", 1.3, .9, 70, 15, 4, "CausticShard:2", "event_rust_hound", f'("hue", {U})')
h("RadMoth", "Rad moth", "rad", 0, .06, .06, "F", .8, .7, 34, 11, 2, "UraniumOre:1", "geiger_bug", f'("hue", {U}), ("scale", 1.3)')
h("FalloutSeer", "Fallout seer", "rad", 0, .04, .04, "C", .9, 1.7, 80, 16, 7, "CausticShard:3", "rift_caster", f'("hue", {U}), ("glow", (120, 255, 80))')
h("SludgeWorm", "Sludge worm", "rad", 0, .04, .04, "W", .9, .9, 130, 15, 6, "UraniumOre:3", "dune_borer", f'("hue", {U})', segs=7)
h("RadBehemoth", "Rad behemoth", "rad", 1, .12, .12, "G", 1.6, 1.6, 720, 52, 24, "UraniumOre:8", "event_abomination", f'("hue", {U})')
h("IsotopeWraith", "Isotope wraith", "rad", 1, .1, .1, "F", .9, 1.4, 420, 46, 16, "CausticShard:6", "event_rust_wraith", f'("hue", {U}), ("glow", (120, 255, 80))')
h("LeadBoxMimic", "Lead-box mimic", "rad", 1, .04, .04, "M", 1, 1, 700, 52, 60, "UraniumOre:6,Credits:150", "cache_mimic", f'("hue", {U}), ("val", 0.7)')
# Blight: overgrowth.
h("RootGel", "Root gel", "over", 0, .08, .08, "H", .9, .65, 38, 11, 1, "BioGel:2", "gel_hopper", '("hue", 0.22), ("val", 0.7)')
h("BlightCrawler", "Blight crawler", "over", 0, .07, .07, "S", 1, .6, 40, 12, 2, "ViridiumOre:1", "fungal_crawler", '("hue", 0.22)')
h("ThornBat", "Thorn bat", "over", 0, .05, .08, "F", .8, .6, 36, 11, 2, "BioGel:1", "halo_bat", '("hue", 0.22), ("val", 0.7)')
h("RootWyrm", "Root wyrm", "over", 0, .04, .04, "W", 1, 1, 160, 16, 7, "ViridiumOre:2", "tunnel_wyrm", '("hue", 0.22), ("val", 0.7)', segs=8)
h("BlightHulk", "Blight hulk", "over", 1, .12, .12, "G", 1.6, 2, 700, 50, 24, "ViridiumOre:6", "spore_golem", '("hue", 0.22), ("val", 0.6)')
h("RotChanter", "Rot chanter", "over", 1, .1, .1, "C", .9, 1.6, 420, 44, 16, "BioGel:6", "mycelid_shaman", '("hue", 0.22), ("val", 0.6)')
h("VineReaper", "Vine reaper", "over", 1, .1, .1, "D", 1, .6, 400, 44, 16, "JungleRootBlock:6", "dive_kite", '("hue", 0.22), ("val", 0.6)')
# Irradiated core.
h("SlagGel", "Slag gel", "core", 0, .08, .08, "H", .9, .65, 60, 16, 3, "ObsidianShard:1", "gel_hopper", f'("hue", {U}), ("val", 0.6), ("glow", (120, 255, 80))')
h("FissionHound", "Fission hound", "core", 0, .06, .06, "G", 1.3, .9, 110, 20, 6, "PyrociteOre:2", "event_rust_hound", f'("hue", {U}), ("glow", (120, 255, 80))')
h("CoreBorer", "Core borer", "core", 0, .04, .04, "W", 1, 1, 220, 22, 10, "PyrociteOre:4", "tunnel_wyrm", f'("hue", {U}), ("glow", (120, 255, 80))', segs=8)
h("IsotopeImp", "Isotope imp", "core", 0, .05, .05, "C", .9, 1.6, 120, 20, 8, "UraniumOre:2", "rift_caster", f'("hue", {U}), ("glow", (120, 255, 80))')
h("ReactorBat", "Reactor bat", "core", 0, .06, .06, "F", .8, .6, 70, 18, 4, "ObsidianShard:2", "lantern_bat", f'("hue", {U}), ("glow", (120, 255, 80))')
h("MeltdownGolem", "Meltdown golem", "core", 1, .12, .12, "G", 1.6, 2.2, 900, 56, 28, "PyrociteOre:8,UraniumOre:4", "core_husk", '("scale", 1.3)')
h("CriticalWisp", "Critical wisp", "core", 1, .1, .1, "D", .9, .9, 480, 50, 18, "UraniumOre:4", "fission_wisp", '("val", 1.3)')
# Mite warrens, precursor vault and dungeon.
h("WebLurker", "Web lurker", "mite", 0, .2, .2, "G", 1.1, .7, 70, 15, 4, "SilkThread:4", "silk_spider_mite", '("val", 0.6)')
h("BroodTyrant", "Brood tyrant", "mite", 1, .2, .2, "G", 1.8, 1.2, 680, 50, 24, "SilkThread:12", "mite_broodmother", '("hue", 0.75)')
h("GlyphCaster", "Glyph caster", "vault", 0, .15, .15, "C", .9, 1.7, 220, 32, 12, "PrecursorGlyphBlock:3", "rift_caster", '("hue", 0.45), ("val", 1.2)')
h("VaultLancer", "Vault lancer", "vault", 1, .2, .2, "G", 1.2, 2, 800, 56, 26, "AncientPowerCell:2", "vault_sentinel", '("hue", 0.1)')
h("CryptHoplite", "Crypt hoplite", "dungeon", 0, .2, .2, "G", 1, 1.8, 140, 24, 8, "AlabasterBlock:4", "alabaster_hoplite", '("val", 0.6)')
h("VoidScribe", "Void scribe", "dungeon", 0, .14, .16, "C", .9, 1.7, 160, 26, 10, "VoidEssence:1", "rift_caster", '("hue", 0.76)')
h("CryptFlitter", "Crypt flitter", "dungeon", 0, .16, .2, "F", .8, .6, 80, 20, 5, "VoidEssence:1", "halo_bat", '("hue", 0.76), ("val", 0.5)')

FAMILY = {"G": "Default", "F": "Default", "T": "Default", "H": "Hopper", "L": "LeapingHopper", "S": "SkitteringHopper",
          "W": "Worm", "C": "Caster", "D": "DivingFlyer", "M": "Mimic"}


def snake(name):
    return re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name).lower()


def resolve(base):
    for prefix in PREFIXES:
        if (SPRITES / f"{prefix}{base}.png").exists():
            return prefix + base
    raise SystemExit(f"missing base sprite {base}")


def enum_names(path, enum):
    text = (PROJECT / path).read_text(encoding="utf-8")
    body = re.search(r"public enum " + enum + r"\s*:\s*\w+\s*\{(.*?)\n\}", text, re.S).group(1)
    body = re.sub(r"//.*", "", body)
    return {m.group(1) for m in re.finditer(r"\b([A-Z][A-Za-z0-9]*)\b\s*(?:=\s*\d+\s*)?(?:,|$)", body)}


items = enum_names("Systems/ItemSystem.cs", "ItemId")
kinds = enum_names("Entities/Actors.cs", "EnemyKind")
crit_by = {c[0]: c for c in CRITTERS}
new_kinds = [c[0] for c in CRITTERS] + [g[0] for g in GOLDS] + [x[0] for x in H]
kinds -= set(new_kinds)  # already-inserted blocks are ours (re-runs)
items -= set(new_kinds)
assert len(new_kinds) == len(set(new_kinds)), "duplicate kind"
for k in new_kinds:
    assert k not in kinds, f"EnemyKind clash {k}"
for k in [c[0] for c in CRITTERS] + [g[0] for g in GOLDS]:
    assert k not in items, f"ItemId clash {k}"
for x in H:
    for d in x[15].split(","):
        assert d.split(":")[0] in items, f"unknown item {d} in {x[0]}"

# ---- C# data ----
cs = []
w = cs.append
w("// <auto-generated> by tools/sprites/content_fill_gen.py; edit the generator table, not this file.")
w("using Rimtera.Entities;\n\nnamespace Rimtera.Systems;\n")
w("/// <summary>One content-fill critter: size, motion, home roster and spawn weights, bait power, and its gold base (if a gold variant).</summary>")
w("public sealed record FillCritter(EnemyKind Kind, string Name, float Width, float Height, bool Flying, bool Swims, string Roster,")
w("    float DayWeight, float NightWeight, int BaitPower, EnemyKind? GoldOf);\n")
w("/// <summary>One content-fill hostile: roster slot (hard = post-Fracture), behaviour family, body, stats, biome drops and defense.</summary>")
w("public sealed record FillHostile(EnemyKind Kind, string Name, string Roster, bool Hard, float DayWeight, float NightWeight,")
w("    EnemyAiFamily Family, float Width, float Height, float HitPoints, float Damage, bool Flying, bool Swims, bool Stationary,")
w("    int Credits, int Segments, (ItemId Item, int Count)[] Drops, int Defense);\n")
w("/// <summary>Content-fill creature data (docs/TERRARIA_GAPS.md plan item 4). Original Rimtera names and tuning.</summary>")
w("public static class ContentFillData\n{")
w("    /// <summary>Spawn share of a gold variant relative to its base critter.</summary>")
w(f"    public const float GoldShare = {GOLD_SHARE}f;\n")
w("    public static readonly FillCritter[] Critters =\n    [")
rows = []
for (k, n, cw, ch, fly, swim, roster, d, nt, bait, gold, base, steps) in CRITTERS:
    rows.append(f'        new(EnemyKind.{k}, "{n}", {cw}f, {ch}f, {str(bool(fly)).lower()}, {str(bool(swim)).lower()}, "{roster}", {d}f, {nt}f, {bait}, null)')
for (k, n, of) in GOLDS:
    if of in crit_by:
        _, _, cw, ch, fly, swim, roster, d, nt, bait, _, _, _ = crit_by[of]
    else:
        roster, d, nt, _, cw, ch, fly, swim = EXISTING[of]
    rows.append(f'        new(EnemyKind.{k}, "{n}", {cw}f, {ch}f, {str(bool(fly)).lower()}, {str(bool(swim)).lower()}, "{roster}", '
                f'{round(d * GOLD_SHARE, 5)}f, {round(nt * GOLD_SHARE, 5)}f, 48, EnemyKind.{of})')
w(",\n".join(rows))
w("    ];\n")
w("    /// <summary>Existing critters that gain ambient spawn slots in the content-fill rosters: (kind, roster, day, night).</summary>")
w("    public static readonly (EnemyKind Kind, string Roster, float Day, float Night)[] ExistingCritterSlots =\n    [")
w(",\n".join(f'        (EnemyKind.{k}, "{v[0]}", {v[1]}f, {v[2]}f)' for k, v in EXISTING.items()))
w("    ];\n")
w("    public static readonly FillHostile[] Hostiles =\n    [")
rows = []
for (k, n, roster, hard, d, nt, fam, hw, hh, hp, dmg, fly, swim, cr, segs, drops, base, steps, defense) in H:
    dr = ", ".join(f"(ItemId.{p.split(':')[0]}, {p.split(':')[1]})" for p in drops.split(","))
    rows.append(f'        new(EnemyKind.{k}, "{n}", "{roster}", {str(bool(hard)).lower()}, {d}f, {nt}f, EnemyAiFamily.{FAMILY[fam]}, {hw}f, {hh}f, '
                f'{hp}f, {dmg}f, {str(bool(fly)).lower()}, {str(bool(swim)).lower()}, {str(fam == "T").lower()}, {cr}, {segs}, [{dr}], {defense})')
w(",\n".join(rows))
w("    ];\n}")
(PROJECT / "Systems" / "Content" / "ContentFillData.cs").write_text("\n".join(cs) + "\n", encoding="utf-8")

# ---- enum blocks ----
enum_lines = ["    // ---- Content fill (append-only; explicit band 1000-1299; data in Systems/Content/ContentFillData.cs) ----"]
crit_kinds = [c[0] for c in CRITTERS] + [g[0] for g in GOLDS]
value = 1000
line = []
for k in crit_kinds:
    line.append(f"{k} = {value}")
    value += 1
enum_lines.append("    // Critters and gold variants.")
for i in range(0, len(line), 6):
    enum_lines.append("    " + ", ".join(line[i:i + 6]) + ",")
value = 1100
line = []
for x in H:
    line.append(f"{x[0]} = {value}")
    value += 1
enum_lines.append("    // Biome hostiles and their hardmode versions.")
for i in range(0, len(line), 5):
    enum_lines.append("    " + ", ".join(line[i:i + 5]) + ",")
enum_lines[-1] = enum_lines[-1].rstrip(",")
(OUT / "enemy_enum.txt").write_text("\n".join(enum_lines) + "\n", encoding="utf-8")

item_lines = ["    // ---- Content fill (append-only; explicit band 8600-8999; definitions in Systems/Content/ContentFillItems.cs) ----",
              "    // Catch items: same names as their critters (the bug net parses the creature name)."]
value = 8600
line = []
for k in crit_kinds:
    line.append(f"{k} = {value}")
    value += 1
for i in range(0, len(line), 6):
    item_lines.append("    " + ", ".join(line[i:i + 6]) + ",")
(OUT / "item_enum.txt").write_text("\n".join(item_lines) + "\n", encoding="utf-8")

# ---- kitbash recipes ----
kb = ["", "# Content fill (docs/TERRARIA_GAPS.md plan item 4): critters, gold variants and biome hostiles, generated from one table",
      "# (recolours and kitbashes of existing creature art; placeholders until generated art lands).", "_CONTENT_FILL = {"]
for (k, n, cw, ch, fly, swim, roster, d, nt, bait, gold, base, steps) in CRITTERS:
    kb.append(f'    "critter_{snake(k)}": ("{resolve(base)}", [{steps}]),')
for (k, n, of) in GOLDS:
    base = crit_by[of][11] if of in crit_by else EXISTING[of][3]
    kb.append(f'    "critter_{snake(k)}": ("{resolve(base)}", [{GOLD}]),')
for x in H:
    kb.append(f'    "creature_{snake(x[0])}": ("{resolve(x[16])}", [{x[17]}]),')
kb.append("}")
(OUT / "kitbash_block.py").write_text("\n".join(kb) + "\n", encoding="utf-8")
print(f"critters {len(CRITTERS)} + gold {len(GOLDS)}; hostiles {len(H)} (hard {sum(1 for x in H if x[3])})")
