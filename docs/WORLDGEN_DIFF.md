# World generation: Terraria vs Rimtera, pass by pass

**Evidence:** REA metadata of `Terraria.exe` 1.4.5. `WorldGen.AddPasses` registers 109 passes:
- 106 named by `Terraria.ID.GenPassNameID` fields, read in registration order from the method's field accesses. The list is in `docs/terraria-evidence/worldgen-passes.txt`.
- 3 dedicated pass classes: `TerrainPass`, `JunglePass` and `DitherSnakePass`.
- Feature builders on `WorldGen` include `TileRunner`, `Caverer`/`WavyCaverer`, `Lakinater`, `oceanCave`, `gemCave`, `Pyramid`, `GrowLivingTree`, `HellFort`, `makeTemple`, `MarblePileWithStatues`, `ShroomPatch`, `CloudIsland`, `IslandHouse`, `MineHouse`, `AddBuriedChest`, `placeTrap`, `PlaceSmallPile`, `PlaceUncheckedStalactite`, `ShimmerMakeBiome`, `TuneOceanDepth` and `ImproveAllChestContents`.
- Pass settings come from an embedded configuration (`Terraria.GameContent.WorldBuilding.Configuration.json`, read through `WorldGenConfiguration`).

Only names and structure are used. Every Rimtera pass, algorithm, number and name is original.

## Status (2026-10-21)

The world-generation completion pass in `World/LargeWorldGenerator.*.cs` closed the planning list below. A Small 4200×1200 world now contains every planned feature: 22 named sites, 36 tallies, all new tiles, all six gems, speleothem variants, statues, dye blossoms, the new cave walls and the phase pool. `WorldGenSelfTests` checks that inventory and the Tiny→Medium scaling, and `--engine-bench` prints the counts per size. The old 42/30/31 coverage count and the “Rimtera now” cells in section 2 are kept as the original diff for traceability, not as current status.

### 1. Scaling (closed)

The old bench (kept below) showed almost every feature capped at the old 1,600-wide setting. Every pass now reads `WorldGenDensity`: underground counts come from world area, surface and horizontal counts from width, and caps only prevent overcrowding. `WorldGenSelfTests` compares Tiny 1750×900 with Medium 6400×1800 (7.3× area, 3.7× width): underground feature counts land in the 5–10× band and surface features in the 2.4–5× band.

| Size | Cells | Hearts | Altars | Blight zones | Life cores | Caches | Pods | Islands |
|---|---|---|---|---|---|---|---|---|
| Tiny 1750×900 (before) | 1.6 M | 21 | 14 | 4 | 60 | 56 | 126 | 4 |
| Large 8400×2400 (before) | 20.2 M | 109 | 14 | 4 | 60 | 57 | 127 | 5 |

### 2. Pass coverage

The original diff counted 42 present, 30 partial, 31 missing and 3 out of scope. The completion pass built every ❌ and ◐ item's plan; the table below is the historical pass-by-pass mapping. The only intentional omissions are the two secret-seed variants (Skyblock and Dual Dungeons) and the housekeeping pass.

Each row below gives Rimtera's original equivalent and the plan that was implemented for every gap.

| # | Terraria pass | Original status | Rimtera before the pass | Plan (implemented) |
|---|---|---|---|---|
| 1 | Skyblock | — | special-seed variant | out of scope (secret seed) |
| 2 | Dunes And Pyramid Locations | ❌ | none | scrap dunes in the Junk Wasteland; sites for buried landers |
| 3 | Ocean Sand | ✅ | Flooding the acid coasts |  |
| 4 | Sand Patches | ✅ | Burying sand and scrap |  |
| 5 | Tunnels | ✅ | Carving tunnels |  |
| 6 | Mountain Caves | ◐ | Digging cave mouths | caves inside raised mountains, not only at their feet |
| 7 | Dirt Wall Backgrounds | ✅ | Raising background walls |  |
| 8 | Rocks In Dirt | ✅ | Scattering rocks in the soil |  |
| 9 | Dirt In Rocks | ✅ | Mixing soil into the rock |  |
| 10 | Clay | ✅ | Mixing clay, gravel and slate |  |
| 11 | Small Holes | ✅ | Pocking the ground |  |
| 12 | Dirt Layer Caves | ✅ | Hollowing shallow caves |  |
| 13 | Rock Layer Caves | ✅ | Opening the caverns |  |
| 14 | Surface Caves | ◐ | Digging cave mouths | more, smaller surface caves scaled by width |
| 15 | Wavy Caves | ✅ | Carving tunnels |  |
| 16 | Ice Biome | ✅ | Freezing the west |  |
| 17 | Grass | ✅ | Greening the grasslands |  |
| 18 | Mud Caves To Jungle Grass | ◐ | Raising the verdant canopy | underground canopy band: mud caverns with jungle grass reaching the cavern layer |
| 19 | Desert Biome | ◐ | Rusting the east | underground scrap hive: hardened scrap-sand caverns with sand walls and burrower nests |
| 20 | Glowing Mushroom Patches | ✅ | Growing the fungal hollows / spore groves |  |
| 21 | Marble | ✅ | Shaping mini-biomes (alabaster halls) |  |
| 22 | Granite | ✅ | Shaping mini-biomes (basalt columns) |  |
| 23 | Floating Islands | ✅ | Lifting sky islands | count must scale with width |
| 24 | Dirt To Mud | ◐ | Raising the verdant canopy | soil to mud through the whole canopy column |
| 25 | Silt | ◐ | falling Silt tile exists | silt pockets in the cavern layer |
| 26 | Ores And Shinies | ✅ | Threading ore veins / biome ores |  |
| 27 | Webs | ✅ | Webbing the mite warrens |  |
| 28 | Underworld | ✅ | Melting the underworld |  |
| 29 | Corruption And Crimson | ✅ | Seeding the blight | zone count must scale with width |
| 30 | Lakes | ◐ | Filling surface lakes | underground lakes and pools in the cavern layer |
| 31 | Stone To Ice And Silt Plus Mud Into Slush | ◐ | Freezing the west | slush pockets under the frozen biome |
| 32 | Dual Dungeons Dither Snake | — | special-seed variant | out of scope (secret seed) |
| 33 | Dungeon | ✅ | Raising the precursor dungeon |  |
| 34 | Mountain Cave Openings | ◐ | Digging cave mouths | openings into mountain caves |
| 35 | Beaches And Ocean Cleanup | ✅ | Flooding the acid coasts |  |
| 36 | Gems | ◐ | Growing crystal caves | gem ore tiles scattered in stone by depth |
| 37 | Gravitating Sand Cleanup | ❌ | none | drop unsupported sand and gravel after generation so nothing floats |
| 38 | Ocean Caves | ❌ | none | tide grottos: flooded caves under the acid coasts with loot |
| 39 | Shimmer | ❌ | none | phase pool: a deep-cavern lake of a new Phase liquid that transmutes dropped items and grants a phasing status |
| 40 | Dirt Wall Cleanup | ◐ | Tidying up | remove floating walls exposed to the sky |
| 41 | Pyramids | ❌ | none | buried landers: crashed triangular hulls in the scrap dunes with a loot chamber |
| 42 | Dirt Rock Wall Runner | ◐ | Raising background walls | wall-type variety runs (rock and soil walls woven together) |
| 43 | Living Trees | ✅ | Growing giant canopy trees | count must scale with width |
| 44 | Living Tree Walls | ✅ | Growing giant canopy trees |  |
| 45 | Demon And Crimson Altars | ✅ | Seeding the blight (altars) | count must scale with area |
| 46 | Surface Water In Jungle | ❌ | none | shallow pools on the canopy surface |
| 47 | Lihzahrd Temple | ◐ | Sealing the precursor vault | grow the vault into a maze temple of corridors and rooms in the deep canopy |
| 48 | Beehives | ✅ | Shaping mini-biomes (bloom hives) |  |
| 49 | Jungle Shrines | ❌ | none | canopy shrines: small mud-brick rooms with a chest |
| 50 | Settle Liquids | ✅ | Settling liquids |  |
| 51 | Remove Surface Water Above Sand | ❌ | none | drain stray surface water sitting on dune sand |
| 52 | Oasis | ❌ | none | coolant springs: water pools with palms in the scrap dunes |
| 53 | Shell Piles Marble Piles And Spike Pits | ❌ | none | shell piles on coasts, rubble piles in halls, spike pits in caves |
| 54 | Smooth World | ❌ | slopes exist (Batch A) but generation never uses them | slope and half-block exposed terrain edges |
| 55 | Waterfalls | ❌ | none | seeps: small water and lava sources in cave walls that pour into caverns |
| 56 | Fragile Ice Over Ice Biome Water | ✅ | Icing frozen ponds |  |
| 57 | Cave Wall Variety | ❌ | none | biome- and depth-based wall types inside caves |
| 58 | Life Crystals | ✅ | Hiding life cores | count capped at 60: must scale with area |
| 59 | Statues | ❌ | none | old-world statues, some wired to spawn creatures |
| 60 | Underground Houses And Buried Chests | ✅ | Burying cabins / supply caches | caches capped at 57: must scale with area |
| 61 | Surface Chests | ❌ | none | surface supply crates near the landing region |
| 62 | Chests In Jungle Shrines | ❌ | none | chests in canopy shrines |
| 63 | Underwater Chests | ❌ | none | chests at the bottom of lakes and coasts |
| 64 | Spider Caves | ✅ | Webbing the mite warrens |  |
| 65 | Gem Caves | ✅ | Growing crystal caves |  |
| 66 | Moss And Moss Caves | ◐ | moss tiles exist | coloured moss caves with glowing moss |
| 67 | Lihzahrd Temple Part2 | ◐ | Sealing the precursor vault | traps and furniture inside the temple |
| 68 | Cave Walls In Enclosed Spaces | ◐ | Raising background walls | fill enclosed cave pockets with walls |
| 69 | Underground Jungle Trees | ❌ | none | stunted canopy trees in underground mud caverns |
| 70 | Floating Island Houses | ◐ | Lifting sky islands | a house with a chest on each island |
| 71 | Quick Cleanup | ✅ | Tidying up |  |
| 72 | Pots Graveyards And Boulder Piles | ◐ | Scattering pots | graveyards (tombstone clusters) and boulder traps |
| 73 | Hellforges | ❌ | none | slag foundries: ruined forts in the molten chasm with a forge station |
| 74 | Spreading Grass On Surface Sunflowers Evils On Surface And Lava Cleanup | ◐ | Greening the grasslands | surface lava cleanup; sunflower analog |
| 75 | Surface Ore And Stone | ❌ | none | exposed ore and stone patches on the surface |
| 76 | Fallen Logs And Water Features | ❌ | none | fallen logs (breakable, timber) and small surface ponds |
| 77 | Traps | ◐ | cabins only | traps world-wide: dart, explosive, boulder, spike |
| 78 | Piles | ❌ | render-only flora | breakable rubble and loot piles as tiles |
| 79 | Spawn Point | ✅ | Choosing the landing site |  |
| 80 | Surface Dirt Walls To Grass Walls | ❌ | none | grass walls under the surface |
| 81 | Spawn Starter NPCs | ✅ | Binding the survivors / guide |  |
| 82 | Sunflowers Part2 | ❌ | none | solar lilies (calm buff nearby) |
| 83 | Trees | ✅ | Growing trees and herbs |  |
| 84 | Alchemy Herbs | ✅ | Growing trees and herbs |  |
| 85 | Dye Plants | ❌ | no dye system | dye plants (rare herbs for future dyes) |
| 86 | Webs In Spider Caves And Honey Plus Speleothems In Beehives | ◐ | webs / nectar | speleothems in hives |
| 87 | Grass Plants Evil Plants And Pumpkins On Surface | ◐ | Sprouting grass and flowers (real, cuttable grass-plant tiles; docs/FLORA_PARITY.md) | seasonal gourds |
| 88 | Glowing Mushroom Plants Underground And Jungle Plants | ✅ | Sprouting grass and flowers (cave jungle and glowshroom floors) |  |
| 89 | Jungle Plants Part2 | ✅ | Sprouting grass and flowers |  |
| 90 | Vines | ✅ | Growing trees and herbs (vines) |  |
| 91 | Flowers | ✅ | Sprouting grass and flowers (flower sprites on grass-plant tiles) |  |
| 92 | Mushrooms | ◐ | render-only flora |  |
| 93 | Exposed Gems In Ice Biome | ❌ | none | gem tiles on ice cave walls |
| 94 | Exposed Gems Underground | ❌ | none | gem tiles on cave walls |
| 95 | Long Moss | ❌ | none | hanging moss strands in moss caves |
| 96 | Dirt Walls Into Mud Walls In Jungle And Jungle Min Max | ◐ | Raising the verdant canopy | mud walls through the canopy column |
| 97 | Bee Larva In Beehives | ❌ | none | brood pods in bloom hives (breaking one summons a hive boss) |
| 98 | Micro Biomes | ◐ | Shaping mini-biomes | add campsites, rigged chests and mining caches |
| 99 | Settle Liquids Part2And Not The Bees | ✅ | Settling liquids |  |
| 100 | Cactus Palm Trees And Coral | ◐ | cactus and palms exist | coral on coasts |
| 101 | Tile Cleanup | ✅ | Tidying up |  |
| 102 | Lihzahrd Altar | ◐ | Sealing the precursor vault | an altar in the temple heart |
| 103 | Lilypads Cattails Bamboo And Seaweed | ❌ | none | water plants: lilypads, reeds, seaweed |
| 104 | Speleothems And Gem Trees | ❌ | none | stalactites and stalagmites everywhere underground; crystal trees |
| 105 | Broken Trap Cleanup | — | n/a | housekeeping |
| 106 | Final Cleanup | ✅ | Tidying up |  |
### 3. World-level techniques

| Technique (evidence) | Rimtera now | Status |
|---|---|---|
| Per-pass configuration and size scaling (`WorldGenConfiguration`, `GetPassConfiguration`) | `WorldGenDensity` table plus area/width scaling for every pass | ✅ |
| Ocean depth tuning (`TuneOceanDepth`, `oceanDepths`) | coast width and depth scale with world width | ✅ |
| Terrain smoothing (`SmoothWorld`, `SlopeTile`) | exposed edges receive slopes and half blocks after terrain is final | ✅ |
| Generation-time falling-sand fix (`GravitatingSandCleanup`) | unsupported falling blocks are settled before liquids | ✅ |
| Chest-content improvement (`ImproveAllChestContents`) | a final pass tops up every chest with at least one useful item for its depth | ✅ |
| Background threading with progress (`TransformWorldOnBackgroundThread`) | worker thread with progress per pass | ✅ |
| Map reset on new world (`clearWorld`) | map is per world and character | ✅ |

## Implementation (complete)
- **Generator:** `World/LargeWorldGenerator.*.cs`
  - `WorldGenDensity` scaling for every pass
  - all ❌ and ◐ items above as new passes, in the reference's relative order where it matters (structures before liquids, cleanup last)
  - new tiles and liquid: Phase liquid, speleothems, gem tiles, rubble piles, statues, fallen logs, water plants, coral, brood pods, slag foundry, canopy shrine, buried lander
  - placeholder art is kitbashed with `tools/sprites/kitbash.py`; the generated-art pass remains queued separately
- **Checks:** `WorldGenSelfTests` confirms every new site and tally appears on a Small world, that no falling block rests on air, that exposed edges are shaped, that generation data survives a save, and that counts scale between Tiny and Medium. `--engine-bench` prints every feature count per size.
- **Not modelled:** the two secret-seed pass variants (Skyblock, Dual Dungeons) are deliberate out-of-scope items.

## 4. Terrain shape and caves: measured (2026-11-01)

The owner's first play of 0.2.9 found the world "flat and weird", with few natural caves and caverns. The pass list above
says *which* passes exist; this section measures *what they make*, side by side with a Terraria-style reference.

**Method.** Both sides are on a Small world (4200×1200), columns 10–90% of the width.
- **Rimtera:** its own `SketchHorizon` and `RaiseMountains` (the surface line before any later pass), and a fully
  generated world for the caves.
- **Terraria reference:** a reconstruction from memory of 1.4's `TerrainPass` walk and `TileRunner` cave passes. Only
  Small Holes, Dirt Layer Caves and Rock Layer Caves are included, on a flat block with the surface at 28% of the height
  and the rock layer at 40%. It is approximate and leaves out surface caves, wavy caves, mountain caves, lakes and biome
  caves, so it *understates* Terraria's openness.

The measuring tools were scratch harness code (`map`, `diff`) and are not shipped.

### Surface

| Measure | Terraria walk | Rimtera profile |
|---|---|---|
| Mean height change per column, inland | 0.36 tiles | 1.02 tiles |
| Columns stepping 2+ tiles, inland | 7% | 26% |
| Height range, inland | 154 tiles | 246 tiles |
| Hills ≥ 6 tiles per 1000 columns, inland | 5.1 | 6.5 |
| **Height range within ±300 columns of spawn** | **137 tiles** | **24 tiles** |
| Hills per 1000 columns around spawn | 6.7 | 3.3 |

**What makes it feel flat and weird**

1. **The spawn plateau.** `RaiseMountains` flattens about 420 columns around the landing site, which is the first thing a
   player sees. Terraria's spawn stays on its rolling surface: 137 tiles of relief there against Rimtera's 24.
2. **Jitter instead of hills.** The surface is three smoothed noises:
   - broad: a control point every width/9 columns, ±4.5% of the height;
   - mid: every width/40 columns, ±2%;
   - fine: every 5 columns, ±0.6% (±7 tiles on Small).

   The fine layer makes a quarter of the columns step 2+ tiles, so the ground looks bumpy without hill shapes, while the
   broad layer is so slow that each stretch reads as level.

   Terraria walks the surface in runs of 5–40 columns: plateau, hill, dale, mountain, valley. Each run holds a steady
   slope, so most steps are 0–1 tiles and the land forms readable hills and valleys.
3. **Isolated mountains.** Mountains are separate Gaussian bumps lifted 5–11% of the height, about 2.4 per 1000 columns.
   They read as lone peaks on flat land rather than mountain country.

### Caves

| Depth below the surface | Terraria-style, open | Rimtera, open |
|---|---|---|
| 0–25 | 25% | 6% |
| 25–60 | 33% | 14% |
| 60–120 | 30% | 18% |
| 120–200 | 33% | 19% |
| 200–300 | 36% | 21% |
| 300–450 | 35% | 26% |
| 450–600 | 34% | 25% |
| 600–750 | 39% | 22% |

| Underground connectivity (surface + 10 down to 85% of the height) | Terraria-style | Rimtera |
|---|---|---|
| Separate open regions | 2,757 | 4,887 |
| Open space in regions of 2,000+ cells | 54% | 37% |
| Open space in pockets under 200 cells | 13% | 25% |

**What makes the caves thin**

1. **Short carvers.** Rimtera's `Blob` walks 2–30 steps and shrinks to half its radius, so it makes round pockets.
   Terraria's runner walks 30–200 steps for dirt-layer caves and 50–300 for rock-layer caves. Each one is a long, winding
   cave that meets others, and that is where the connected cave systems come from.

   | Pass | Terraria (per million cells) | Rimtera (per million cells) |
   |---|---|---|
   | Rock-layer caves / Caverns | 130 caves, width 6–20, 50–300 steps | 385 caverns, radius 3–7.5, 10–30 steps |
   | Dirt-layer caves / Shallow caves | 30 caves, width 5–15, 30–200 steps | 312 caves, radius 2–4.5, 8–22 steps |
   | Small holes / Pocking the ground | 1,500 × two runners, one 8–15 wide and 7–30 steps long | 1,111 pocks, radius 1–2.4, 2–6 steps |

   The only long carvers in Rimtera are Tunnels (14 per million cells, radius 1.4–2.8), which are thin.
2. **An empty top layer.** The first 25 rows under the surface are 6% open. Shallow caves start 6 tiles down and avoid
   the spawn, and cave mouths number 6.5 per 1000 columns. Terraria pocks the surface band too and adds surface caves, so
   holes and entrances show from the start.
3. **Few water caves.** In the reference, a fifth of the small holes are left water-filled. Rimtera has only its
   underground lakes (6 per million cells).

### Done in 0.3.0: Rimtera matched to the reference

The owner asked for every number to match the reference, with a few more natural openings on the surface. Both models
now ship in the game:
- `Systems/WorldShape.cs` measures a world;
- `Systems/WorldShapeReference.cs` builds the reference;
- `dotnet run -- --worldgen-diff [--size Small|Medium|Large] [--seed N]` prints the two side by side;
- `WorldGenSelfTests` holds the match.

Unlike the first measurement above, both sides now measure from the ground actually present (lake surfaces count as
ground, and a 21-column median smooths over shafts). Blight chasms are left out of the surface figures, because the
reference has nothing like them.

**Generator changes (`World/LargeWorldGenerator.cs`, `World/WorldGenDensity.cs`)**
- **Surface:** a run-based walk (plateaus, rises, dips, climbs, drops; runs of 5–40 columns) replaces the three
  stacked noises.
- **Mountains and valleys:** fewer (0.9 and 0.8 per 1000 columns), wider and lower, with no sharp crest.
- **Landing site:** a 16-column pad instead of a 420-column plateau.
- **Long cave carver (`Runner`):** a tapering diamond walked 30–300 steps.
  - Shallow caves: 14 per million cells, 5–15 wide, 30–200 steps.
  - Caverns: 80 per million cells, 6–20 wide, 50–300 steps.
  - Small holes: 1000 pairs per million cells, a small cut in the surface band and an 8–15-wide one below.
  - Tunnels: cut to 4 per million cells.
- **Surface caves:** chutes, wide mouths, long descents and level galleries (6.45 per 1000 columns in all, about twice
  the reference's carver count). Only the landing pad is spared.
- **Flooding cave pockets:** a third of enclosed pockets under 2,500 cells fill with water. Skipped: anything built, a
  survivor's spot, or sand overhead.
- **Transit walkways:** no longer laid under sand.

**Small world (4200×1200), Rimtera against the reference, three seeds (31337, 7, 99)**

| Measure | Reference | Rimtera |
|---|---|---|
| Mean step (tiles) | 0.35–0.57 | 0.54–0.64 |
| Columns stepping 2+ tiles | 6–8% | 8–10% |
| Inland relief | 143–190 | 149–238 |
| Hills per 1000 columns | 9.2–9.5 | 8.6–11.0 |
| Relief around spawn | 67–139 | 54–77 (24 before) |
| Open, 0–25 tiles down | 7–14% | 11–13% |
| Open, 25–60 | 11–29% | 13–20% |
| Open, 60–120 | 18–34% | 17–26% |
| Open, 120 tiles down and deeper | 31–37% | 28–36% |
| Open space in caves of 2,000+ cells | 55% | 50–54% |
| Open space in pockets under 200 cells | 14–15% | 17–18% |
| Surface openings reaching 20+ tiles down, per 1000 columns | 1.2–4.8 | 3.3–6.3 (by request, more) |
| Underground water | 11–12% | 10–12% |

The surface steps a little more than the reference because the extra cave mouths notch the ground where they open.
Medium (6400×1800) lands the same way. Generation stays inside the bench budget: Small takes 4.8 s against a soft
budget of 8 s.

**What is still not compared.** Structures and biome widths have no reference numbers, because the Terraria side here
is a measuring model, not Terraria's code. Section 2 maps every Terraria pass to its Rimtera counterpart instead. Section
5 adds the world's contents.

## 5. World contents: measured (0.3.1)

`Systems/WorldCensus.cs` counts what generation puts in a world, by Terraria's layers:
- **sky:** more than 20 tiles above the ground line;
- **surface:** down to 40 tiles below it;
- **underground:** down to a tenth of the height below it;
- **caverns:** down to the Irradiated Core;
- **underworld:** the core.

It counts chests by layer and kind, pots, life cores, altars, and ore cells per million cells of each layer.
`WorldCensusReference` holds Terraria-style ranges for a world of the same area. They are reconstructed from the
structure of 1.4's chest, pot, life crystal, altar and "Shinies" passes, so **every reference figure is approximate**,
and the ranges are deliberately wide. `--worldgen-diff` now prints the census and the comparison after the shape
numbers, and `WorldGenSelfTests` holds both.

**Small world, three seeds (31337, 7, 99)**

| Measure | Before | After | Reference (approx.) |
|---|---|---|---|
| Chests | 120 | 183–198 | 150–300 |
| Chests underground and in the caverns | 78 | 124–145 | 100–220 |
| Dungeon chests | 9 | 22 | 15–35 |
| Underworld chests | 3 | 6–8 | 5–14 |
| Pots | 936 | 938–999 | 600–1500 |
| Life cores | 50 | 50 | 35–110 |
| Altars | 20 | 20 | 10–32 |
| Ore per million cells, surface | 20,775 | 20,175–22,202 | about 22,554 |
| Ore per million cells, underground | 39,190 | 25,574–26,330 | about 23,589 |
| Ore per million cells, caverns | 26,135 | 26,734–28,433 | about 24,534 |

**Generator changes**
- **Burying cave chests:** a new pass before chests are improved.
  - Cabin chests on cave floors through the underground and caverns, 12 per million cells.
  - Slag chests in the Irradiated Core, 1 per million cells (at least 3).
  - Strongboxes along the dungeon's halls, 3 per 1000 columns (at least 4).
  - Each is kept clear of other containers.
- **Ore:** half the copper veins and 40% of the scrap-iron pockets that land in the underground layer are dropped, and
  copper veins are 10% more common overall. The underground held about two-thirds more ore than the reference; it now
  matches, and the surface rises to the reference.

**Chest contents.** Terraria's chests always hold one primary item for their kind, with supplies rolled on top. Every
generated Rimtera chest now does the same.

The chest's family comes from its tile, depth and biome. Each family has a pool, and a chest holds one item from it:

| Family | Primary pool |
|---|---|
| Surface | return boomerang, dart blower, sprint servos, headlamp, compass chip, depth meter, life monitor, miner's gauntlet, scrap tether, spark emitter |
| Underground and caverns | double-jump canister, climbing claws, grapple hook, magnet ring, sprint servos, scrap chakram, lucky charm, night-vision goggles |
| Frozen | thermal sleeve, frost-grip gloves, grav damper |
| Canopy | power glove, sprint servos, barb mesh, climbing claws |
| Sunken | coral pendant, oxygen tank, hydro skimmers, fishing line |
| Sky | glider wings, lucky charm, tractor coil, double-jump canister |
| Underworld | regen implant, thermal regulator, blink module, leech coil |
| Dungeon | grav anchor, blink module, reflex chip, targeting lens |

On top of the primary, a chest rolls supplies for its depth tier:
- light: torches, or glow sticks deeper down;
- rope;
- bars of that depth's metal;
- throwables or ammo;
- healing stims and buff stims;
- recall capsules;
- credits.

A supply is skipped when the chest's own table already holds that kind of item.

The precursor vault's locked chests keep their fixed loot.

The biome material and the rare thruster fins are unchanged. Terraria's tier mix within each layer (which metals sit
where) is left for 0.3.3, which works on the ore and crafting ladder.
