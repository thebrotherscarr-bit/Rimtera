# What Rimtera is missing from Terraria

Sources:
- **Terraria:** REA ID tables (`docs/terraria-evidence/gaps-*.json`, `items-*.json`): NPCID 766, ItemID ~5,400, TileID 755, WallID 368, ProjectileID 1,137, BuffID 402, MountID 67, PaintID 33, GameModeID 4.
- **Rimtera:** the game's own catalogues (`--item-census`, `--entity-sprites`, enums).

Earlier passes already cover the base mechanics (`docs/MECHANICS_PARITY.md`), world generation (`docs/WORLDGEN_DIFF.md`), items (`docs/ITEMS_DIFF.md`) and hardmode (`docs/HARDMODE_DIFF.md`).

The gap pass (four parallel work streams) closed every system gap below. The suite has since grown to 550 self-test checks. All new content uses placeholder art (kitbashed recolours or reused sprites); generated art comes with the next asset run.

## Content counts (after the gap pass)
| Category | Terraria | Rimtera | Gap |
|---|---|---|---|
| Bosses and event mini-bosses | ~36 | 33 bosses plus 9 event mini-bosses; every boss has a summon item or trigger ([CREATURE_COVERAGE.md](CREATURE_COVERAGE.md)) | ✅ |
| Hostile creatures | ~450 distinct (766 NPC ids including segments and variants) | 328: 255 plus 73 from the creature coverage pass (sky, underground junk and frost, grave field, late dungeon, and own units for every event) | Terraria still has more segment and variant ids; every biome, variant and event has its own roster |
| Critters | ~100 (with gold variants) | 102: 11 gold, 13 water, all catchable as bait | ✅ |
| Town NPCs and pets | ~40 | 20 NPCs plus Greenskeeper, Travelling Merchant, 4 town gels, town pets | ✅ |
| Items | ~5,400 (about 4,000 furniture, blocks, banners, vanity) | ~1,600 | combat and gear at parity; furniture, decor and vanity volume is not |
| Tiles | 755 | ~400 | furniture and decor sets |
| Walls | 368 | ~126 | decor walls |
| Buffs and debuffs | 402 | 51 statuses | the core set is covered; Terraria has far more potion variety |
| Mounts | 67 | 6 (hover-bike, dune strider, rotor skiff, tide ray, burrower, spring hopper) | variety only; every movement type exists |
| Projectile types | 1,137 | ~17 weapon styles plus enemy shots | visual variety |
| Liquids | 4 | 6 (water, uranium, acid, nectar, phase, fuel) | ✅ |
| Flora | tree types per biome, gem trees, herbs, vines, water plants, dye plants | 8 tree species plus gem trees, 8 wild harvestable plants, herbs, vines, water and dye plants; growth, felling, shaking, herb blooms, grass spread and cuttable grass plants follow the reference rules (docs/FLORA_PARITY.md) | ✅ |

## Systems: present ✅, partial ◐, missing ❌
| System | Status | Notes |
|---|---|---|
| Fishing (power, bait, crates, quest fish, biome pools) | ✅ | caught critters are bait; gold critters are the best bait |
| Events: rain, storms, dust storm, blizzard, blood moon (Rust Moon), eclipse (Dark Sun), slime rain (Swarm Rain), goblin (Scavenger Raid), pirates, defence event, martians (Precursor Probes), lantern night (Aurora Night), party (Colony Festival), meteors, pillars | ✅ | |
| Seasonal wave moons (pumpkin and frost analogs) | ✅ | Hollow Moon and Rime Moon: summoned by item, scored waves, bosses |
| Frost Legion-style invasion | ✅ | Cryo Brigade |
| Weather: rain, snow, dust, fog, storm, wind | ✅ | Windy Day event; wind pushes projectiles, dropped items and kites |
| Minecarts and rails | ✅ (redesigned) | replaced by glide-walks: airport-style moving walkway floors (regular 6 tiles/s, express 11) that add their speed to the rider's own walking; laid facing the placer, reversed by interacting; derelict transit walkways generate in the caverns |
| Fast travel (pylons) | ✅ | active near a town with two housed residents; travel from the map |
| Mounts as a system | ✅ | ground, flying, swimming, hover, jump and digging types; mounts are restored on load |
| Kites | ✅ | |
| Golf | ✅ | balls, clubs, tees, cups, strokes, Greenskeeper NPC; the ball returns to the pack on load |
| Paint and coatings | ✅ | paint tiles and walls; no echo-style coating |
| Dyes on armor and accessories | ✅ | dye vat crafts 8 dyes; dye colours the worn suit; dye solvent clears |
| Luck stat | ✅ | −1..+1; tilts rare drops, crits and calibration rolls |
| Aggro | ✅ | aggro-weighted target choice; taunt and stealth gear |
| Debuffs on enemies | ✅ | burning, poison, frostbite, armour shred, confusion, marked; immunities and boss resistance |
| Treasure bags | ✅ | Brutal salvage caches with an exclusive core-chip |
| Trophies and relics | ✅ | |
| Music boxes | ✅ | |
| Emotes | ✅ | hold ` and press 1–8 |
| Town pets and town slimes | ✅ | pets by license; four town gels move in once the colony has 3 residents |
| Travelling merchant | ✅ | ~45% of mornings with 2+ residents; 6 items from a 20-item pool; leaves at 18:00 |
| Sifting | ✅ | sediment sifter |
| Creative mode | ✅ | research and duplication, time, spawn and difficulty powers |
| Secret world seeds | ✅ | |
| Housing, happiness, bestiary, achievements, banners, loadouts, storage, signs, wiring, hooks, pets, statues, shimmer analog (phase pool) | ✅ | |
| Difficulty modes (Explorer, Classic, Brutal, Creative; Ironman character) | ✅ | |

## Known limits
- Enemy aggro applies to the main ground, flyer and caster AI families within a set distance.
- Wired decks toggle by interacting only.
- What remains is volume, not systems: furniture and decor sets, vanity, more projectile visuals, more mount and buff variety.
