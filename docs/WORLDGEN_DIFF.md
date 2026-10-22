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
