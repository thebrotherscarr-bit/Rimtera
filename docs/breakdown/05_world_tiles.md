# 05 — World, tilemaps and world generation (breakdown vs Terraria)

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open. An arrow gives the milestone `docs/PLAN.md` now plans the step for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "Not done, not scheduled" marks steps of parity 0.1.7 and 0.1.8 that no release shipped and no milestone holds yet: release 0.1.7 did not carry parity step 0.1.7's fix list. "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

Area: world sizes and layers, every generation pass, biomes and structures, the tile catalogue, walls, liquids, framing and slopes, falling blocks, blight and crystal spread, ores and the altar cycle, the Fracture, random tile updates, wiring, the map, and the save format.

**How this was checked (2026-10-28, `main` at 8bfc052 plus the uncommitted working tree)**
- I read the code under `World/` and the world-side `Systems/` files. File:line pointers are given in every row.
- **Live probe** (in a private scratch copy, never in the repo): a `[ModuleInitializer]` probe dumped every `TileId` from `TileCatalog`, `TileDropTable`, `TilePhysics` and `ContentTiles` (355 ids). It also generated a **Small 4200×1200** world through the real `LargeWorldGenerator` pipeline (blight Radiated, seed 777001) and measured feature counts, structure bounds, liquid volumes, the surface strip, ore depth and the compressed save size. The numbers below marked "probe" come from that run.
- **Terraria evidence:**
  - `docs/terraria-evidence/worldgen-passes.txt` (the 106 named passes).
  - `cil/Terraria.Player__HasEnoughPickPowerToHurtTile` and `lite/Terraria.Player__GetPickaxeDamage.lite.txt` (pick-power gates).
  - A fresh lite dump of `WorldGen::SmashAltar`, `WorldGen::initializeHardMode`, `WorldGen::GERunner`, `Liquid::UpdateLiquid`, `Liquid::LiquidCheck` and `Liquid::DelWater`, made with `scripts/lite.py` over `members-ALL.json`.
  - "(knowledge)" marks behaviour taken from general Terraria knowledge, not from the dump.
- The full `--self-test` suite in the same scratch build (with the uncommitted working tree): **615 checks passed**. The existing world self-tests do not catch the gaps below; each step adds one.
- I treated the existing docs (`WORLDGEN_DIFF.md`, `WORLD_SIZE_AND_MAP.md`, `ENGINE_WORLDGEN.md`) as claims and re-checked them. `WORLDGEN_DIFF.md` says every pass is closed. That is true for **presence**, but several features are under-sized or leak progression (below).

---

## Summary

**Counts (141 rows):** ✅ 91 · ◐ 29 · ❌ 8 · ⚠ 5 · ➕ 8

| Status | Count |
|---|---|
| ✅ matches | 91 |
| ◐ partial | 29 |
| ❌ missing | 8 |
| ⚠ broken / bug | 5 |
| ➕ Rimtera-only (deliberate) | 8 |

Generation is in good shape. 88 ordered passes run in 4.4 s on Small. 191 of 355 tile types appear in a fresh world, nothing floats, every one of the 22 named sites exists, the save is 1.3 MB, and the map reveals by light. The problems are **sizes and gates, not presence**.

### Top 10 issues by player impact

| # | Issue | Status | Why it matters | Step |
|---|---|---|---|---|
| 1 | **Precursor temple brick needs no pick power** (`PrecursorBrick` MinimumToolPower 0, `World/ContentTiles.cs:214`; Terraria Lihzahrd brick 210). The starter drill digs into the temple, bypassing the vault door and key. | ⚠ blocks progression | Skips the bulb-boss gate and all temple loot | WT-1 |
| 2 | **Viridium (hardmode tier-1 ore) generates before the Fracture.** The probe found 10,054 cells at mean depth 0.76, mineable at power 35 (`LargeWorldGenerator.cs:1181`). It is also the first ore the altar cycle seeds. | ⚠ | Hardmode tier 1 is free in pre-hardmode; repeats PROGRESSION_DIFF G4 | WT-2 |
| 3 | **Fracture scars are about 10× too thin.** Each arm is at most 19 tiles wide (`GameSimulation.Blight.cs:418`). Terraria's `GERunner` sweeps `Next(200,250) × maxTilesX/4200` tiles and converts walls too. | ⚠ | Hardmode biomes barely appear, so their creatures, fishing and materials are starved | WT-3 |
| 4 | **Blight zone can sit inside the Verdant Canopy.** The canopy only tests its two end columns against blight (`LargeWorldGenerator.Content.cs:229`). The probe saw zone 1444–1570 inside canopy 1406–1658. | ⚠ | Eats the only jungle on Small; jungle loot and creatures shrink | WT-4 |
| 5 | **Jungle (canopy) is 6% of width and stops at row 691 of 1200.** Terraria's jungle runs from the surface to the underworld. | ◐ | Underground-jungle content (hives, temple approach, bulbs) is cramped | WT-5 |
| 6 | **Altar ore yield is capped at 24 veins for every world size** (`GameSimulation.Blight.cs:273`). Placement ignores tier depth. | ⚠ | Medium and Large get the Small yield: hardmode ore is scarce on big worlds | WT-6 |
| 7 | **Blight stone and other stones have no pick gate.** Void rock is 0 (Terraria evil stone 65). Pyrocite and obsidian are 35 (Terraria 65/55). | ◐ | Removes the evil-pick / hellstone rungs of the pick ladder | WT-7 |
| 8 | **Water is scarce underground** (probe: 3,348 full cells on Small, against 10,721 fuel and 9,899 uranium). | ◐ | Fishing spots, water walking, bottles and obsidian-making feel absent | WT-8 |
| 9 | **Acid never generates.** The oceans are fuel seas, and acid only appears from acid rain in basins. The acid-sand recipe (`BatchAItems.cs:91`), acid fishing and the acid rod depend on it. | ◐ | Recipe and fishing content are effectively unreachable | WT-9 |
| 10 | **Dungeon, temple and sky islands are small** (probe: dungeon 169×169 ending above the cavern row; temple about 100×20; islands 19–30 wide). | ◐ | Late-game exploration set pieces are short | WT-10, WT-11 |

---

## Feature tables

Legend: ✅ matches · ◐ partial · ❌ missing · ⚠ broken or bug · ➕ Rimtera-only (deliberate).

### A. World sizes, layers and storage

| # | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| A1 | World sizes | Small 4200×1200, Medium 6400×1800, Large 8400×2400 (WORLD_SIZE_AND_MAP evidence) | The same, plus Tiny 1750×900 (`UI/MenuSystem.cs:313-319`) | ✅ | ✅ as audited |
| A2 | Tiny size | — | 1750×900 quick-test size | ➕ | ➕ kept |
| A3 | Legacy tiny generator | — | Worlds narrower than 400 use the old 5-pass pipeline (`GameSimulation.Blight.cs:14`, `GameSimulation.cs:178-191`); tests only | ➕ | ➕ kept |
| A4 | Cell storage | `Tile` struct | 8-byte `WorldCell`: tile u16, wall u16, liquid units u8 (0–240), kind and flags u8, damage u8, frame mask u8 (`World/TileGrid.cs:720-835`) | ✅ | ✅ as audited |
| A5 | Space layer (low gravity, wind, its own spawns) | Gravity scales down above about 0.35 × worldSurface; space spawns (knowledge) | No space band and no altitude gravity: no "space" or "altitude" in `Systems/` | ❌ | ❌ WT-14 → 0.5.0; PU-4 → 0.5.0 |
| A6 | Sky band and floating islands | Islands high above the surface | Islands sit at rows 46–86 of 1200 (probe) | ✅ | ✅ as audited |
| A7 | Surface band | worldSurface about 25–30% of height | `_surface` is clamped to 9–36% of height (`LargeWorldGenerator.cs:481`); probe average 267/1200 = 22% | ✅ | ✅ as audited |
| A8 | Underground and cavern rows | rockLayer about 35–40% | `_rockLine` per column; cavern row = max surface + 16% (`LargeWorldGenerator.cs:498-503`); probe 517/1200 = 43% | ✅ | ✅ as audited |
| A9 | Underworld (the Irradiated Core) | The bottom 200 rows (17% on Small) | `_moltenTop` average 1007/1200 (84%), with uranium lakes, ash ceiling and core rock (`MeltUnderworld`, `LargeWorldGenerator.cs:784`) | ✅ | ✅ as audited |
| A10 | Bedrock floor | No bedrock: the world edge | One bedrock row (probe 4,200 cells) | ➕ | ➕ kept |
| A11 | Biome detection | Zones for sky/space, dungeon, underground desert, granite, marble, graveyard, meteor, shimmer and others (knowledge) | `BiomeZone` has 15 zones (`Systems/BiomeScanner.cs:6-31`). There is no Sky/Space, Dungeon (uses a wall test in spawning), Scrap Hive, Basalt/Alabaster, Graveyard, Meteor or Phase zone | ◐ | ❌ WT-15 → 0.5.0 |

### B. World-generation passes (88 Rimtera passes, `World/LargeWorldGenerator.cs:177-276`, against the 106 named Terraria passes)

| # | Terraria pass(es) | Rimtera pass (file) | Probe (Small) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| B1 | Terrain (TerrainPass), mountains | Sketching the horizon, Raising mountains (`LargeWorldGenerator.cs:395-483`) | surface rows 110–325 | ✅ | ✅ as audited |
| B2 | Dirt/rock layering, Rocks In Dirt, Dirt In Rocks | Laying down strata, Scattering rocks, Mixing soil (`:484-565`) | | ✅ | ✅ as audited |
| B3 | Clay, Sand Patches, Silt | Mixing clay/gravel/slate, Burying sand and scrap, Pocketing silt | clay 3,892, gravel 8,285, silt 5,684 | ✅ | ✅ as audited |
| B4 | Small Holes, Dirt/Rock Layer Caves, Tunnels, Wavy Caves | Pocking, Hollowing shallow caves, Opening the caverns, Carving tunnels (`:581-650`) | | ✅ | ✅ as audited |
| B5 | Surface Caves, Mountain Caves and their openings | Digging cave mouths, Hollowing mountain caves | 8 mountain caves, 55 surface pockets | ✅ | ✅ as audited |
| B6 | Ice Biome, Stone To Ice, Slush | Freezing the west, Pocketing slush (`:699`) | snow 140k, slush 3,703, 202 pockets | ✅ | ✅ as audited |
| B7 | Desert surface, Dunes | Rusting the east, Raising scrap dunes | 7 dunes | ✅ | ✅ as audited |
| B8 | Underground desert | Hollowing the scrap hive | 1 hive, 492×272, 180 burrower nests | ✅ | ✅ as audited |
| B9 | Oceans, Beaches, TuneOceanDepth | Filling the oceans (fuel) (`LargeWorldGenerator.Terrain.cs:260-300`) | 2 × 231 wide, 10,721 fuel cells | ✅ | ✅ as audited |
| B10 | Ocean Caves | Carving tide grottos | 4 grottos about 20×6 | ◐ small | ❌ WT-11 → 0.5.0 |
| B11 | Glowing Mushroom biome | Spreading spore groves, Growing fungal hollows | 10 hollows of 60–86 × 30–42 | ✅ | ✅ as audited |
| B12 | Marble, Granite | Shaping mini-biomes (alabaster halls, basalt columns) | alabaster 2,336, basalt 618 | ✅ | ✅ as audited |
| B13 | Underworld, Hellforges | Irradiating the core, Ruining the core reactors | 3 slag foundries; uranium 9,899 cells; 40 uranium lakes | ✅ | ✅ as audited |
| B14 | Corruption/Crimson chasms and orbs | Seeding the blight, Rooting deep blight pockets (`:922-1160`) | 4 zones of about 100 × 160 deep; 28 hearts | ✅ | ✅ as audited |
| B15 | Evil biome placement rules | Kept away from jungle, snow and dungeon (knowledge) | Avoids spawn, coast and frozen/junk, but **not the canopy** (canopy checks only its end columns, `LargeWorldGenerator.Content.cs:229`): zone 1444–1570 ended up inside canopy 1406–1658 | ⚠ | ❌ WT-4 → 0.5.0 |
| B16 | Demon/Crimson Altars | Seeding the blight (altars) | 20 altars | ✅ | ✅ as audited |
| B17 | Ores And Shinies | Threading ore veins, Growing biome ores (`:1161-1230`) | ferrite 44,923 (depth 0.38), scrap-iron 13,724, cobalt 20,960 (0.64), glacial 4,913, uranium 1,413, pyrocite 1,197 (0.91) | ◐ only two generic depth tiers (ferrite, cobalt); Terraria has four ore pairs, with the alternate chosen per world | ❌ PC-5 → 0.5.0 |
| B18 | Hardmode ores absent before hardmode | Only altar smashing creates them | **Viridium generates** (`:1181`, 10,054 cells), plus viridium in Overgrowth blight pockets (`:1203`) | ⚠ | ❌ WT-2 → 0.5.0 |
| B19 | Gems, Gem Caves, Exposed Gems | Growing crystal caves, Setting gems, Exposing gems | 554 gem veins, 324 exposed, about 335–390 cells per gem | ✅ | ✅ as audited |
| B20 | Jungle (JunglePass, Mud Caves To Jungle Grass, Dirt To Mud, mud walls) | Raising the verdant canopy, Sinking the canopy roots (`LargeWorldGenerator.Content.cs:219`) | 1 canopy **252 wide (6%)**; underground canopy rows 360–691 | ◐ narrow, does not reach the core | ❌ WT-5 → 0.5.0 |
| B21 | Lihzahrd Temple, Part 2, Altar | Sealing the precursor vault, Winding the vault temple | vault 48×20 + temple 48×12, 12 rooms, 1 altar | ◐ small (Terraria temple is roughly 150+ wide and 100+ tall, knowledge) | ❌ WT-10 → 0.5.0 |
| B22 | Beehives, Larva | Bloom hives, Seeding brood pods | 2 hives (52×30), 2 brood pods | ✅ | ✅ as audited |
| B23 | Jungle Shrines and their chests | Building canopy shrines | 5 shrines with chests | ✅ | ✅ as audited |
| B24 | Living Trees and their walls | Growing giant canopy trees | 4 trees, about 20×40 | ✅ | ✅ as audited |
| B25 | Dungeon | Raising the precursor dungeon | 169×169 at x 3150–3319, rows 233–402: **ends above the cavern row (517)**; 9 dungeon chests, 72 dungeon pots | ◐ small and shallow | ❌ WT-10 → 0.5.0 |
| B26 | Pyramids | Burying landers | 3 landers about 25×13 with lander chests | ✅ | ✅ as audited |
| B27 | Floating Islands, Island Houses | Lifting sky islands | 6 islands (19–30 wide) each with a house and sky chest | ◐ island size | ❌ WT-11 → 0.5.0 |
| B28 | Underground Houses, Buried Chests | Burying cabins, Burying supply caches | 40 cabins, 94 caches, 59 cabin chests | ✅ | ✅ as audited |
| B29 | Surface/Underwater chests | Hiding surface and sunken chests | 6 crates, 7 sunken (11 sunken chest tiles) | ✅ | ✅ as audited |
| B30 | Life Crystals | Hiding life cores | 50 | ✅ | ✅ as audited |
| B31 | Statues (some wired) | Raising old-world statues | 50 statues, 6 wired | ✅ | ✅ as audited |
| B32 | Traps | Rigging traps | 60 boulder, 71 dart, 30 explosive, 40 spike pits, 1,047 trap wires | ✅ | ✅ as audited |
| B33 | Pots, Piles | Scattering pots, Heaping rubble and shell piles | 973 pots, 1,593 rubble, 24 shell piles | ✅ | ✅ as audited |
| B34 | Spider Caves, Webs | Webbing the mite warrens | 849 silk, 42 cocoons | ✅ | ✅ as audited |
| B35 | Shimmer | Pooling the phase lake | 1 pool of 38×14, 263 phase cells | ✅ | ✅ as audited |
| B36 | Oasis | Tapping coolant springs | 5 | ✅ | ✅ as audited |
| B37 | Lakes (surface and underground) | Filling surface lakes, Pooling underground lakes, ponds, canopy pools | 10 surface lakes, 30 underground lakes, 17 ponds; **only 3,348 water cells in total** | ◐ cave water is sparse | ❌ WT-8 → 0.5.0 |
| B38 | Waterfalls | Cutting seeps | 13 water, 3 lava seeps, 21 uranium falls | ✅ | ✅ as audited |
| B39 | Settle Liquids (both) | Settling liquids (`:1617`) | 1.09 s, the slowest pass | ✅ | ✅ as audited |
| B40 | Gravitating Sand Cleanup | Settling loose sand | 3,448 settled; **0 floating fallers** | ✅ | ✅ as audited |
| B41 | Smooth World | Smoothing the terrain | 55,358 slopes, 1,169 half blocks | ✅ | ✅ as audited |
| B42 | Wall passes (Dirt Wall Backgrounds, Cave Wall Variety, Enclosed, Grass walls, Dirt Wall Cleanup) | Raising background walls, Weaving cave walls, Clearing sky-exposed walls (`:1507`) | 27 wall types in use; 7,889 grass walls; 21,755 sky walls cleared | ✅ | ✅ as audited |
| B43 | Moss, Long Moss | Growing moss caves | 20 caves, 4 colours, 291 long moss | ✅ | ✅ as audited |
| B44 | Speleothems, Gem Trees | Hanging speleothems, biome/gem trees | 9,112 speleothems, 54 gem-tree trunks | ✅ | ✅ as audited |
| B45 | Surface Ore And Stone | Exposing surface ore and stone | 32 | ✅ | ✅ as audited |
| B46 | Fallen Logs, Water Plants, Coral, Cattails, Lilypads | Felling logs, Planting water life | 29 logs, 286 water plants, 81 coral fans | ✅ | ✅ as audited |
| B47 | Grass/flowers/herbs/trees/vines/mushrooms/dye plants/pumpkins | Growing trees and herbs, Sprouting grass, lilies/dyes/gourds (flora agent) | 163 trees, 69 herbs, 227 dye blossoms, 25 gourds | ✅ (see FLORA_PARITY) | ✅ as audited |
| B48 | Micro Biomes (campsites, trail caches, enchanted sword shrine) | Campsites and caches, Enshrining weapons | 10 campsites, 15 mining caches, 15 rigged chests, 8 weapon shrines | ✅ | ✅ as audited |
| B49 | Remove Surface Water Above Sand, Lava cleanup | Draining the dunes, Draining surface uranium | 6 drained, 0 cooled | ✅ | ✅ as audited |
| B50 | Fragile Ice Over Ice Biome Water | Icing frozen ponds | 14 thin ice | ✅ | ✅ as audited |
| B51 | ImproveAllChestContents | Improving chest contents | 113 chests improved | ✅ | ✅ as audited |
| B52 | Spawn Point, Spawn Starter NPCs | Choosing the landing site, Binding the survivors | spawn x=2100 (centre) | ✅ | ✅ as audited |
| B53 | Secret seeds (drunk, FTW, remix, and others) | 4 phrase variants (`LargeWorldGenerator.Seeds.cs:24-33`) | | ◐ different set | ◐ unchanged since the audit |
| B54 | Derelict ship and transit walkways | — | Crashing the derelict, Laying derelict transit walkways | ➕ | ➕ kept |
| B55 | Pass configuration and scaling | `WorldGenConfiguration` | `WorldGenDensity` area/width rates (`World/WorldGenDensity.cs`); Tiny→Medium scaling self-test | ✅ | ✅ as audited |
| B56 | Background thread with progress | `TransformWorldOnBackgroundThread` | Pipeline with per-pass weights and progress (`World/WorldGeneration.cs:13-60`) | ✅ | ✅ as audited |

### C. Tile catalogue (355 `TileId`s, all registered: the probe found 0 ids without a definition)

Sources: `World/TileGrid.cs:3-~360` (enum) and `:639-702` (core definitions); `World/ContentTiles*.cs` (content tiles); `Systems/PlayerTiles.cs`; Leisure and Travel tile tables. Drops come from `TileDropTable.GetDrop` (`Systems/ItemSystem.cs:2222-2268`), light from `TilePhysics.GetTileLight` (`World/TilePhysics.cs:210-231`), falling from `TilePhysics.Falls` (`:203`). The full per-id dump (id, name, solid, HP, resistance, frame group, minimum power, drop, light radius, falls, traits) was produced by the probe. The rows below are by property.

| # | Property | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| C1 | Every tile has a definition | | 355/355 | ✅ | ✅ as audited |
| C2 | Solid and non-solid, platforms, doors | per-tile flags | `IsSolid`/`BlocksLiquid` plus `TileTraits` (Door, Platform, Climbable and others, `World/ContentTiles.cs:5-41`) | ✅ | ✅ as audited |
| C3 | Durability | `HitTile` 0–100 with per-tile pick multipliers (dirt ×2 and so on) | HitPoints/Resistance, plus `CombatModel.DrillHardness` tiers (soft ×2, ÷2/÷3/÷4) and `DrillDamage` (`Systems/Content/CombatModel.cs:540-571`) | ✅ | ✅ as audited |
| C4 | Pick gates: meteorite | 50 (GetPickaxeDamage offset 439) | 50 | ✅ | ✅ as audited |
| C5 | Pick gates: evil stone and pearlstone | 65 (offsets 379-421) | void rock, rift soil and pearl stone **0** | ❌ | ❌ WT-7 → 0.5.0 |
| C6 | Pick gates: obsidian and evil ore | 55 (offsets 462-513) | obsidian 35, vitrified glass 35; no evil-ore tile | ◐ | ❌ WT-7 → 0.5.0 |
| C7 | Pick gates: hellstone | 65 (offset 562) | pyrocite 35 | ◐ | ❌ WT-7 → 0.5.0 |
| C8 | Pick gates: dungeon brick | 100 (offset 702, tileDungeon) | precursor dungeon brick 70; old derelict plating 0 | ◐ | ❌ WT-7 → 0.5.0 |
| C9 | Pick gates: Lihzahrd brick and altar | 210 (offsets 588-639) | **precursor brick 0**, vault altar 85 | ⚠ | ❌ WT-1 not done, not scheduled |
| C10 | Pick gates: hardmode ores | cobalt/palladium 100, mythril/orichalcum 110, adamantite/titanium 150, chlorophyte 200 (offsets 771-861, 359) | viridium 35, lumenite 85, voidsteel 100 | ◐ (PROGRESSION_DIFF G4) | ❌ WT-7 → 0.5.0 |
| C11 | Unbreakable tiles | none (world edge) | bedrock (255 HP, resistance 100); explosion-proof list (`GameSimulation.Physics.cs:1101-1105`) | ✅ | ✅ as audited |
| C12 | Drops | per-tile item | Every natural block maps to an item; containers, pots and plants are handled by special handlers (`GameSimulation.Blight.cs:146-190`); biome-ore byproduct shards | ✅ | ✅ as audited |
| C13 | Light-emitting tiles | torches, lamps, gems, mushrooms, lava | 18 explicit lights plus a `Light` trait; liquid uranium, phase and fuel glow in `UI/LightMap.cs:96-100`; lamps toggle by wire (state bit) | ✅ | ✅ as audited |
| C14 | Falling tiles | sand ×4, silt, slush, pearlsand | 12 kinds including boulder (`TilePhysics.cs:203-205`); runtime `FallingBlock` with damage (`GameSimulation.Physics.cs:8-340`) | ✅ | ✅ as audited |
| C15 | Paint (tile and wall) | 31 paints, PaintID | No paint system | ❌ | ◐ paint exists (12 paints, brush, roller, scraper, glow coating; the audit missed it); WT-18 → 0.6.0 |
| C16 | Actuators and inactive tiles | yes | `HasActuator`/`IsInactive` cell flags, placed and removed (`GameSimulation.Physics.cs:524-550`) | ✅ | ✅ as audited |
| C17 | Thin ice and fragile tiles | breaks when fallen on | `Fragile` trait, thin ice | ✅ | ✅ as audited |

### D. Walls

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| D1 | Natural and built wall ids | about 340 walls | 18 built walls (`WallIds` 1-18) and 27 natural walls (100-126) (`World/TileGrid.cs:359-430`) | ◐ fewer, enough for biomes | ❌ WT-17 → 0.5.0 |
| D2 | Biome walls in generation | yes | 27 in use (probe) | ✅ | ✅ as audited |
| D3 | Walls convert with blight, hallow and hardmode stripes | `Convert_ActuallyConvertWall`; GERunner rewrites walls (offsets 576-764) | `BlightRules.Convert` handles tiles only; walls never convert (`Systems/WorldTicker.cs:17-30`) | ❌ | ❌ WT-13 → 0.5.0 |
| D4 | Walls block housing and spawns | yes | region map and spawning read walls (other area) | ✅ | ✅ as audited |

### E. Liquids

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| E1 | Liquid kinds | water, lava, honey, shimmer | water, lava (liquid uranium), nectar, phase, acid, fuel (`World/TileGrid.cs:513-527`) | ✅ | ✅ as audited |
| E2 | Flow model | `Liquid.UpdateLiquid` active list with settle | Event-driven active set, bottom-first, 12k cells per step, whole-world fallback past 80k (`World/ActiveLiquidSimulator.cs:12-330`) | ✅ | ✅ as audited |
| E3 | Viscosity (lava and honey flow slower) | per-liquid delay; `UpdateLiquid` sets `delay = 10` (offset 561), lava slower (knowledge) | Every liquid flows at the same rate | ❌ | ❌ WT-12 → 0.5.0 |
| E4 | Water + lava → obsidian; honey + water → honey block; honey + lava → crispy honey | `Liquid.GetLiquidMergeTypes` (LiquidCheck offset 700) | vitrified glass, hive comb, core crust; plus acid/water → salt crust and lava/fuel → char slag (`ActiveLiquidSimulator.cs:62-77`) | ✅ | ✅ as audited |
| E5 | Shimmer reactions with other liquids | shimmer merges into blocks (`ShimmerCheck`, DelWater offset 1077) | Phase reacts with nothing | ❌ | ❌ WT-12 → 0.5.0 |
| E6 | Lava burns grass to dirt and mud | DelWater offsets 827-985 | Lava only stops grass spreading (`WorldTicker.cs:478`); it never burns grass | ◐ | ❌ WT-12 → 0.5.0 |
| E7 | Acid and fuel liquids | — | Acid harms and slows; fuel slows and can be swum | ➕ | ➕ kept |
| E8 | Acid availability | — | **0 acid cells generated**; acid only comes from acid rain filling basins (`GameSimulation.World.cs:516`). The acid-sand recipe and acid fishing depend on it | ◐ | ❌ WT-9 → 0.5.0 |
| E9 | Water evaporates in the underworld | yes (knowledge) | no evaporation anywhere | ❌ | ❌ WT-12 → 0.5.0 |
| E10 | Buckets, sponges and pumps | buckets, sponges, inlet/outlet pumps | buckets for water, lava and fuel (`GameSimulation.Items.cs:474-498`); no pumps | ◐ | ❌ WT-12 → 0.5.0 |
| E11 | Rain fills basins | — | `FillBasinsWithRain` | ➕ | ➕ kept |
| E12 | Liquid light | lava glows | uranium, phase and fuel glow (`UI/LightMap.cs:96-100`) | ✅ | ✅ as audited |

### F. Framing, slopes and falling blocks

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| F1 | Autotiling | `TileFrame` with merge rules (dirt-merge and others), 3 random variants per frame | 8-neighbour mask by frame group; only same-group tiles connect (`World/TileFramingSystem.cs:66-100`); the renderer bleeds edges between groups (`UI/TileArt.cs:86-97`) | ◐ no data-side merge rules or variants | ❌ WT-17 → 0.5.0 |
| F2 | Region reframe | local `SquareTileFrame` | `ReframeRegion` on dirty 3×3 areas; `ReframeAll` is parallel at load (`TileFramingSystem.cs:23-64,102-140`) | ✅ | ✅ as audited |
| F3 | Slopes and half blocks | 5 slopes plus half | `TileShape` with 6 shapes (`World/TileGrid.cs:606-619`); collision by shape (`TilePhysics.cs:190-197`); generation smoothing | ✅ | ✅ as audited |
| F4 | Slopes in the framing mask | slopes change frames | mask ignores shape | ◐ (render only) | ❌ WT-17 → 0.5.0 |
| F5 | Falling sand at runtime | falls when unsupported, lands or breaks | `FallingBlock` list, lands and hurts (`GameSimulation.Physics.cs:235-340`) | ✅ | ✅ as audited |
| F6 | Falling blocks after generation | `GravitatingSandCleanup` | Settling loose sand: 0 floating (probe) | ✅ | ✅ as audited |

### G. Blight, crystal spread, the Fracture and altars

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| G1 | Pre-hardmode evil spread | grass only | grass-like tiles only before the Fracture (`WorldTicker.cs:12-37`) | ✅ | ✅ as audited |
| G2 | Hardmode spread to stone, sand, ice and grass | yes | every convertible tile after the Fracture, 3 attempts and a 1.5× tick rate (`WorldTicker.cs:126,185-197`) | ✅ | ✅ as audited |
| G3 | Mud and jungle grass | jungle grass → corrupt jungle grass; mud stays mud (knowledge) | mud and canopy grass → blight soil (`WorldTicker.cs:21-23`) | ◐ | ❌ WT-5 → 0.5.0 |
| G4 | Hallow spreads and competes | hallow converts natural tiles; it does not purify evil | Crystal Spires spread after the Fracture **and purify blight** (`WorldTicker.cs:199-205,230-241`) | ◐ deliberate twist | ❌ WT-13 → 0.5.0 |
| G5 | Spread blockers | none apart from clentaminator and sunflowers' minor effect (knowledge) | Purifier beacon blocks within 12 tiles (`WorldTicker.cs:79,243-255`) | ➕ | ➕ kept |
| G6 | Hardmode stripes (the V) | Two `GERunner` sweeps from the dungeon-side region (initializeHardMode offsets 203-713), width `Next(200,250)·maxTilesX/4200` (GERunner offset 396/543); converts walls | Two arms from 0.22 and 0.78 of the width, half-width `clamp(W/140, 2, 9)` → **at most 19 tiles** (`GameSimulation.Blight.cs:406-457`); tiles only | ⚠ about 10× too thin | ❌ WT-3 → 0.5.0 |
| G7 | Altars locked until hardmode, smashed by the pwnhammer | `SmashAltar` returns early if not hardMode (offset 14) | locked until the Fracture; needs the `altar-breaker` capability (`GameSimulation.cs:968-973`, `GameSimulation.Hardmode.cs:69-90`) | ✅ | ✅ as audited |
| G8 | Altar ore cycle | `altarCount % 3` → cobalt/palladium, mythril/orichalcum, adamantite/titanium, the alternate picked per world; then OreRunner(5–12) per spot (offsets 158-1037) | viridium → lumenite → voidsteel (`GameSimulation.Blight.cs:92`) | ◐ no per-world alternates | ❌ WT-6 → 0.5.0 |
| G9 | Altar ore amount and depth | spot count scales with world size and falls per cycle; deeper tiers spawn lower (knowledge, OreRunner y bands) | `veins = clamp(cells/40000, 3, 24) × yield` — **capped at 24 on Small, Medium and Large alike**; placed anywhere from 35% of height down, with no tier banding (`GameSimulation.Blight.cs:273-301`) | ⚠ | ❌ WT-6 → 0.5.0 |
| G10 | Smash side effects | wraith spawn chance, chat message, achievement (offsets 1246-1276) | hostile core spirit every smash, message, flag (`:303-307`) | ✅ | ✅ as audited |
| G11 | Hearts (orbs) | breaking 3 summons the boss; meteor chance | heart loot and avenger boss (`GameSimulation.Blight.cs:220-265`) | ✅ (boss side: other area) | ✅ as audited |
| G12 | Meteor landing | after an orb break or randomly, from a world event | MeteorFall event (`Systems/WorldEvents.cs:21,257,504`) | ✅ | ✅ as audited |
| G13 | Purification | powder and solution | Purifier beacon and `BlightRules.Purify` (`WorldTicker.cs:51-58`) | ◐ (items: other area) | ❌ WT-13 → 0.5.0 |

### H. Random tile updates

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| H1 | Overground and underground update counts | `UpdateWorld` rates scaled by area | `FloraTick` uses the reference rates (`WorldTicker.cs:140-160`) | ✅ | ✅ as audited |
| H2 | World-pass rules (spread, crystals) | the same random update | 1/5000 cells per tick (`WorldTicker.cs:78`), Rimtera-tuned | ◐ rate not from evidence | ❌ WT-13 → 0.5.0 |
| H3 | Grass spread, vines, cactus, saplings, herbs | yes | `SpreadGrass`, `GrowVineBelow`, `GrowCactus`, `TryGrowSapling`, `GrowHerb` (`WorldTicker.cs:328-540`) | ✅ | ✅ as audited |
| H4 | Energy crystal regrowth | — | Rock next to crystals regrows energy crystal (`WorldTicker.cs:207-213`) | ➕ | ➕ kept |
| H5 | Hardmode-only regrowth (chlorophyte spread, crystal shards, life fruit) | yes (knowledge) | post-Fracture lifeblooms (`GrowthItems.TryGrowLifebloom`); no ore regrowth | ◐ | ◐ unchanged since the audit |

### I. Wiring and mechanisms

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| I1 | Wire colours | 4 (red, blue, green, yellow) | 3 layers (`Systems/Wiring.cs:13`) | ◐ | ❌ WT-16 → 0.6.0 |
| I2 | Wire storage and save | per tile | sparse dictionary; survives save (BatchASelfTests:334) | ✅ | ✅ as audited |
| I3 | Triggers | switch, lever, plate, timers, sensors | switch, lever, pressure plate, 1/3/5 s timers | ◐ no sensors (day, night, player above, liquid) | ❌ WT-16 → 0.6.0 |
| I4 | Logic | logic gates with lamps | AND/OR/NOT/XOR: inputs on layers 1-2, output on layer 3 (`GameSimulation.Physics.cs:910-935`) | ◐ different model | ❌ WT-16 → 0.6.0 |
| I5 | Outputs | doors, lamps, actuators, traps, teleporters, statues, pumps, fireworks, music boxes, announcement box | doors, lamps, actuators, dart and explosive traps, teleporter pads, wired statues (`GameSimulation.Physics.cs:890-960`) | ◐ no pumps or spike/flame traps | ❌ WT-16 → 0.6.0 |
| I6 | Pulse budget | `Wiring` with a per-trip limit | 128 network pulses per trigger (`GameSimulation.Physics.cs:873`) | ✅ | ✅ as audited |
| I7 | Junction box | yes | none | ❌ | ❌ WT-16 → 0.6.0 |

### J. Map, minimap and reveal

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| J1 | Reveal by light | `LightingEngine.ExportToMiniMap` | `RevealFromLight` at threshold 0.12 over the lit window (`UI/WorldMap.cs:73-90`, called at `UI/GraphicalSandbox.cs:2102`) | ✅ | ✅ as audited |
| J2 | Remembered tile and light | MapTile type plus light | 64×64 chunks of key + light, allocated lazily (`UI/WorldMap.cs:16-130`) | ✅ | ✅ as audited |
| J3 | Per player per world file | `.map` | `maps/{world}_{character}.rmap` (`UI/GraphicalSandbox.cs:1052,1354`) | ✅ | ✅ as audited |
| J4 | Minimap, overlay, fullscreen, icons | yes | yes (WORLD_SIZE_AND_MAP; UI agent) | ✅ | ✅ as audited |
| J5 | Map colour per liquid | water, lava, honey, shimmer | `CellKind` Water/Lava; liquid id kept in the key so the renderer can tint | ✅ | ✅ as audited |

### K. Save size and world-file format

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| K1 | World file | binary .wld with sections | JSON envelope plus a binary deflated cell sidecar, written to temp files then moved (`UI/WorldSaveRepository.cs:60-90`) | ✅ | ✅ as audited |
| K2 | Cell codec | RLE per tile | RLE varint runs of 8-byte cells, deflate, version byte, v1 still decodes (`Systems/CellCodec.cs:7-40`) | ✅ | ✅ as audited |
| K3 | Size | Small .wld is a few MB (knowledge) | Small cells **1.32 MB** (raw 40.3 MB) (probe) | ✅ | ✅ as audited |
| K4 | Generation metadata saved | chests, signs, tile entities | sites, tallies and chest tiers saved (`GameSaveData.cs:109-207`; WorldGenSelfTests §5) | ✅ | ✅ as audited |
| K5 | Hardmode state | flags, altarCount, ore tiers | `FractureActive`, `HeartsBroken`, `AltarsSmashed` (`GameSaveData.cs:44-47`) | ✅ | ✅ as audited |

---

## Integration steps

**WT-1 — Gate the precursor temple brick** (blocks play: progression bypass)
- **Change:**
  - Set `MinimumToolPower` on `PrecursorBrick` and `PrecursorGlyph` to a post-bulb-boss rung, above the voidsteel drill (for example 110), or make them unbreakable until a `temple.open` flag (Terraria: Lihzahrd brick 210, opened by the key after Plantera).
  - Keep `IsExplosionProof`.
  - Make sure no recipe or vault-key path needs the brick broken.
- **Files:** `World/ContentTiles.cs:214` (and the glyph line), `Systems/GameSimulation.cs:962` (message).
- **Size:** S.
- **Dependencies:** tools ladder (area 02/03: which drill reaches 110), hardmode bosses (bulb boss gate).
- **Acceptance:** a self-test generates a world, takes the best pre-bulb drill, mines a `PrecursorBrick` cell and gets "needs a stronger drill". After the bulb-boss flag, the brick breaks with the top drill.

**WT-2 — Remove pre-Fracture viridium generation**
- **Change:**
  - Drop the `ViridiumOre` call in `OreVeins` and replace it with a deep **cobalt** band, or a new pre-hardmode "gold-tier" ore if area 01/02 wants a fourth tier.
  - In `BiomeOres`, Overgrowth pockets host glacial or uranium-tier ore instead of viridium.
  - Heart loot already guards viridium (`GameSimulation.Blight.cs:236`).
- **Files:** `World/LargeWorldGenerator.cs:1181,1203`; `World/WorldGenDensity.cs:82`; `docs/WORLDGEN_DIFF.md`.
- **Size:** S.
- **Dependencies:** progression (PROGRESSION_DIFF G4), crafting audit (viridium recipes must stay reachable after the Fracture).
- **Acceptance:** `WorldGenSelfTests` adds a check that Tiny and Small worlds contain 0 `ViridiumOre`, `LumeniteOre` and `VoidsteelOre` cells. `--craft-audit` shows viridium items only after "fracture".

**WT-3 — Widen the Fracture scars and convert walls**
- **Change:**
  - Arm width = `random(200,250) × width/4200`, so about 200–250 tiles on Small.
  - The start column is chosen on the dungeon side, as in `initializeHardMode`.
  - Each arm runs diagonally to the bottom.
  - Convert natural walls too (radiated, overgrowth or crystal wall ids).
  - Add `BlightRules.ConvertWall` and `CrystalRules.ConvertWall`.
  - Batch the reframe with a progress message, since it touches a large area.
- **Files:** `Systems/GameSimulation.Blight.cs:406-457`, `Systems/WorldTicker.cs:17-30`, `World/TileGrid.cs:359-430` (crystal wall ids).
- **Size:** M.
- **Dependencies:** creatures (biome detection of the larger zones), performance (reframe of about 300k cells: check `--engine-bench`).
- **Acceptance:**
  - On Small, `FractureCellsConverted + FracturePrismCells ≥ 150,000` and the arms are at least 180 tiles wide at the surface.
  - A beacon still shelters its radius.
  - The Fracture completes in under 1 s on Large.

**WT-4 — Keep blight out of the canopy (and the canopy out of blight)**
- **Change:** in `VerdantCanopy`, reject a span if **any** blight zone overlaps `[left-8, right+8]`. Also place the canopy **before** `SeedBlight`, or have `SeedBlight` avoid reserved canopy columns from the layout sketch.
- **Files:** `World/LargeWorldGenerator.Content.cs:226-232`, pass order in `World/LargeWorldGenerator.cs:203-219`.
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** over 20 seeds (Tiny and Small), no blight zone intersects any canopy span, and canopy surface tiles stay above 80% `CanopyGrass`.

**WT-5 — A full-height, wider jungle**
- **Change:**
  - Canopy span about 12–15% of width.
  - The underground canopy (mud, jungle grass, mud walls) runs from the surface to `_moltenTop`.
  - The vault temple and hives sit inside it.
- **Files:** `World/LargeWorldGenerator.Content.cs:219-280` (VerdantCanopy, UndergroundCanopy), `World/WorldGenDensity.cs:68`.
- **Size:** M.
- **Dependencies:** WT-4; creatures (canopy spawn tables), flora (canopy trees and bulbs).
- **Acceptance:** on Small, the canopy is at least 500 wide, mud is present in at least 60% of canopy columns at 0.8 × height, and `WorldGenSelfTests` still scales.

**WT-6 — Scale altar ore with world size and band it by tier**
- **Change:**
  - Spots = `area × k / (1 + smashes/3)`. Pick k so Small gives roughly today's first-smash yield, and Large about 4× that.
  - Remove the 24 cap.
  - Tier 1 between the cavern row and 70% of height, tier 2 from 55%, tier 3 from 70% down to `moltenTop`.
  - Optional: per-world alternates (two ore families per tier, saved).
- **Files:** `Systems/GameSimulation.Blight.cs:92-95,267-308`, `Systems/GameSaveData.cs` (if alternates are added).
- **Size:** M.
- **Dependencies:** progression and hardmode (ore names), save format.
- **Acceptance:** the first smash on Large seeds at least 3× the cells of the first smash on Small, and the mean depth of tier-3 ore is greater than tier 1.

**WT-7 — Restore the pick ladder rungs on world tiles**
- **Change:**
  - Blight rock, rift soil and pearl stone 65-equivalent.
  - Obsidian and vitrified glass 55-equivalent.
  - Pyrocite 65-equivalent.
  - Dungeon brick 100-equivalent.
  - These map onto Rimtera's drill scale: decide with area 03.
- **Files:** `World/TileGrid.cs:653-693`, `World/ContentTiles.cs` (pearl stone, vitrified glass).
- **Size:** S.
- **Dependencies:** tools ladder (area 03), crafting audit.
- **Acceptance:** `--craft-audit` stays green; a `ParityWorldTests` table asserts the gates.

**WT-8 — More cave water**
- **Change:**
  - Raise the underground-lake rate and size.
  - Add a "cavern drips" pass that fills a share of closed cave basins with water in the dirt and rock layers, and lava (uranium) basins in the lower caverns.
  - Settle afterwards.
- **Files:** `World/LargeWorldGenerator.Terrain.cs` (UndergroundLakes), `World/WorldGenDensity.cs`.
- **Size:** S.
- **Dependencies:** fishing (area 06/07: depth-based catch tables).
- **Acceptance:** Small has at least 20,000 water cells below the surface, with no water on the surface of sand (the existing drain still runs).

**WT-9 — Make acid a real liquid**
- **Change:** generate acid pools in the Radiated blight zones and deeps (for example, 1–2 per zone at the chasm floors) and some acid tide pools on the coasts. Alternatively, retarget the acid-sand recipe and acid fishing to fuel.
- **Files:** `World/LargeWorldGenerator.cs` (SeedBlight / DeepBlightPockets), `Systems/Content/BatchAItems.cs:91`, `Systems/Fishing.cs`.
- **Size:** S.
- **Dependencies:** fishing, crafting audit.
- **Acceptance:** a probe on Small with Radiated blight finds at least 500 acid cells, and the acid-sand recipe is craftable next to one.

**WT-10 — Grow the precursor dungeon and temple**
- **Change:**
  - **Dungeon:** extend it down past the cavern row (corridors and rooms to about 0.6 × height). Add spike corridors, more chests, and a few locked biome chests (the vault-key analog).
  - **Temple:** about 3× its current footprint, as a maze of rooms with traps and the altar at the heart.
- **Files:** `World/LargeWorldGenerator.Structures.cs` (PrecursorDungeon), `World/LargeWorldGenerator.Content.cs` (PrecursorVault, VaultTemple).
- **Size:** L.
- **Dependencies:** WT-1, WT-5; creatures (dungeon roster), items (biome chest loot).
- **Acceptance:** on Small the dungeon bounds reach below `CavernRow` and hold at least 20 chests; the temple bounds are at least 140×60.

**WT-11 — Larger sky islands and ocean grottos**
- **Change:**
  - Island width 40–90 on Small, with a lake island variant.
  - Tide grottos 2–3× larger.
- **Files:** `World/LargeWorldGenerator.cs:1320-1373`, `World/LargeWorldGenerator.Decor.cs` (TideGrottos).
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** island mean width is at least 45 and grotto cells are at least 3× today's, measured by probe.

**WT-12 — Liquid behaviour parity**
- **Change:**
  - Per-kind flow rate: lava/uranium every 3rd step, nectar every 5th.
  - Phase merges with any other liquid into a new "phase-glass" block.
  - Water evaporates below `_moltenTop`.
  - Lava burns grass to soil and canopy grass to mud.
- **Files:** `World/ActiveLiquidSimulator.cs:62-330`, `Systems/WorldTicker.cs`.
- **Size:** M.
- **Dependencies:** a new tile id (append-only), art (tile sprite).
- **Acceptance:** a liquid self-test: a 10-cell lava column spreads slower than water; water poured in the Core empties within N steps; phase next to water hardens.

**WT-13 — Wall conversion by blight and crystal spread**
- **Change:** random updates on blight tiles also convert adjacent natural walls (soil, rock and others → the radiated or overgrowth wall); purifiers revert them.
- **Files:** `Systems/WorldTicker.cs:179-241`.
- **Size:** S.
- **Dependencies:** WT-3.
- **Acceptance:** after 10⁶ random updates next to blight, natural walls in a 5-tile ring are converted.

**WT-14 — Space band (low gravity)**
- **Change:**
  - Above about 8% of height, scale gravity by altitude (as in Terraria, where gravity scales down above 0.35 × worldSurface).
  - Add a `Space` reading in `BiomeScanner` for spawns and music.
- **Files:** `Systems/GameSimulation.Player.cs` (gravity), `Systems/BiomeScanner.cs`.
- **Size:** S.
- **Dependencies:** movement (area 04), creatures and spawns, audio.
- **Acceptance:** a player at row 20 falls slower than at the surface; the zone reads Space.

**WT-15 — Missing biome readings**
- **Change:** add `BiomeZone` readings for Dungeon (precursor wall and brick), Scrap Hive, Alabaster/Basalt, Phase pool and Graveyard.
- **Files:** `Systems/BiomeScanner.cs:6-31,91`.
- **Size:** S.
- **Dependencies:** creatures (area for spawn tables), fishing, music.
- **Acceptance:** the BiomeScanner self-test reads each zone at a generated site of each kind.

**WT-16 — Wiring completeness**
- **Change:**
  - A 4th wire layer.
  - Sensors: day/night, player-above, liquid.
  - Inlet/outlet pumps that move liquid between networks.
  - A junction box.
  - Spike and flame traps.
- **Files:** `Systems/Wiring.cs`, `Systems/GameSimulation.Physics.cs:860-960`, `World/TileGrid.cs` (append ids), `World/ContentTiles.cs`.
- **Size:** M.
- **Dependencies:** items and recipes, art.
- **Acceptance:** BatchA wiring self-test: a pump pair moves water; a junction box routes straight through; a day sensor toggles a lamp at dawn.

**WT-17 — Framing variants and cross-group merges in data**
- **Change:**
  - Store a 2-bit random variant (seeded from x and y, so it can be derived) for natural blocks.
  - Add merge pairs (soil merges with rock, ores, clay, mud and sand) so masks join across groups.
  - Use slopes in the mask.
- **Files:** `World/TileFramingSystem.cs:66-100`, `UI/TileArt.cs`.
- **Size:** M.
- **Dependencies:** rendering (area 08), tile sprites.
- **Acceptance:** `--tile-sprites` shows variants; the framing self-test checks that a soil cell beside rock has the matching neighbour bit.

**WT-18 — Paint**
- **Change:** a tile and wall paint byte (use the spare flag bits or extend the cell), paint items and a scraper, and renderer tinting.
- **Files:** `World/TileGrid.cs:720-835` (cell layout and save codec version 3), `Systems/CellCodec.cs`.
- **Size:** L.
- **Dependencies:** items, UI, save format, rendering.
- **Acceptance:** painted cells survive a save round-trip; the codec stays backward compatible.

---

## Cross-area dependencies

| This area needs | From area | Why |
|---|---|---|
| Final drill power ladder | Tools / progression (02/03) | WT-1, WT-7: the numeric gates on temple, evil stone, pyrocite and dungeon brick |
| Ore tier naming (does a pre-hardmode gold tier exist?) | Progression / items (01/02) | WT-2, WT-6 |
| Spawn tables for new or larger zones | Creatures (06) | WT-3, WT-5, WT-14, WT-15 |
| Fishing tables for acid, cave water and space | Fishing / events (07) | WT-8, WT-9 |
| Gravity and movement | Movement / controls (04) | WT-14 |
| Rendering of variants, paint and new liquids | Rendering / UI (08) | WT-12, WT-17, WT-18 |
| Pumps, sensors and paint items and recipes | Items / crafting (01) | WT-16, WT-18 |

| Other areas depend on this one for | |
|---|---|
| Hardmode biome size (WT-3) | creature and material availability after the Fracture |
| Temple gate (WT-1) | bulb-boss and endgame progression |
| No viridium before the Fracture (WT-2) | the hardmode gear chain (PROGRESSION_DIFF G4) |

## Open questions for the user

1. **Viridium before the Fracture:** remove it entirely (WT-2), or keep a small amount deliberately as a teaser? Should a fourth pre-hardmode ore tier ("gold" analog) be added to fill the gap between cobalt and the hardmode ores?
2. **Crystal Spires purifying blight** (G4): this differs from Terraria, where hallow and evil only compete for natural ground. Keep the twist?
3. **Fracture scar width** (WT-3): match Terraria (about 200–250 tiles per arm on Small), or keep Rimtera's narrow scars and rely on faster spread?
4. **Acid** (WT-9): should acid pools generate (in blight zones or on coasts), or should acid content be retargeted to the fuel seas?
5. **Space band** (WT-14): do you want low gravity and space spawns, or is the sky deliberately normal-gravity in this setting?
6. **Paint** (WT-18): in scope at all? It costs a cell-format change.
7. **Temple gate** (WT-1): a tool-power gate (Terraria-style), or an explicit "sealed until the bulb boss falls" flag?
