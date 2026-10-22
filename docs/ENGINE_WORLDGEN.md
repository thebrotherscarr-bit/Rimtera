# Engine, world generation, and blight: Terraria structure vs Rimtera (diff and design)

Evidence: `rea inspect-managed-members` on `Terraria.exe` 1.4.5.8 (SHA-256 `960a03bf…a2f3`): `docs/terraria-evidence/engine.json` (Liquid, LiquidBuffer, LightingEngine, LightMap, TileLightScanner, Lighting, WorldFile, GenPass, WorldGenerator), `main.json` (Terraria.Main), and the earlier `worldgen.json` (WorldGen). Only type, member and field names and their relationships were used; no method bodies, constants, tile tables or content were copied. All Rimtera algorithms, names and numbers are original.

## 1. Observed structure (Terraria)

**Frame loop.** `Main.DoUpdate` → `DoUpdateInWorld` runs fixed-rate gameplay; drawing is separate (`DoDraw` → `DoDraw_WallsAndBlacks`, `DoDraw_Tiles_NonSolid`, `DoDraw_Tiles_Solid`, `DoDraw_DrawNPCsBehindTiles`/`OverTiles`, `DoDraw_Waterfalls`, `DrawLiquid`, interface layers). Tile draws are cached into render targets (`ClearCachedTileDraws`, `ApplyPendingBackgroundTargetSwap`) and the background picks a style per depth band (`DrawBackground_DirtBackground`, `_DrawRockLayer`, `_DrawMagmaLayer`, `_DrawUnderworldBlackBox`).

**World ticking.** `WorldGen.UpdateWorld` performs random tile updates split into `UpdateWorld_OvergroundTile` and `UpdateWorld_UndergroundTile` plus `UpdateWorld_GrassGrowth`; `hardUpdateWorld` adds the post-progression evil spread. `SpreadGrass`, `SpreadGrassWalls`, `SpreadDesertWalls` grow terrain; `Convert` / `Convert_ActuallyConvertTile` / `_ActuallyConvertWall` / `ConvertTreeAndGround` / `CanEvilReplace` change tiles between biomes.

**Liquids.** `Liquid` keeps an active list (`numLiquid`, `maxLiquid`, `curMaxLiquid`) with a `LiquidBuffer` for newly woken cells (`AddBuffer`/`DelBuffer`), per-cell checks (`LiquidCheck`, `LavaCheck`, `SettleWaterAt`, `CreateLiquidMergeTile`, `GetLiquidMergeTypes`), and a `panicMode` that settles the whole world when the list overflows (`StartPanic`, `QuickWater`).

**Lighting.** `LightingEngine` runs a state machine (`ProcessScan` → `ProcessBlur` → `Present`) over a padded `LightMap` area; `TileLightScanner` seeds light from the surface (`ApplySurfaceLight`), tiles (`ApplyTileLight`), walls, liquids and the underworld (`ApplyHellLight`) and masks blockers (`GetTileMask`, `LightIsBlocked`); `LightMap.Blur`/`BlurLine` propagates with separate decay through air, water, solid tiles. Per-frame dynamic lights are added with `AddLight`.

**World files.** `WorldFile` writes a header with section pointers (`SaveFileFormatHeader`, `SaveHeaderPointers`) then sections (`SaveWorldTiles`/`LoadWorldTiles`, chests, signs, NPCs, tile entities, footer); tiles are stored compactly with run lengths; `DoRollingBackups` keeps backups.

**Generation.** `WorldGenerator` runs `GenPass` instances (`Name`, `Weight`, `Apply`, enable/disable) with progress. Feature builders seen on `WorldGen`: `TileRunner` (blob walker used for most features), `OreRunner`, `DirtyRockRunner`, `MudWallRunner`, `digTunnel`, `Caverer`, `WavyCaverer`, `LavaLayerCaverer`, `CaveOpenater`, `oceanCave`, `gemCave`, `FloatingIsland`/`IslandHouse`, `Pyramid`, `AddLifeCrystal`, `AddBuriedChest`, `PlacePot`, traps, `FillWallHolesInArea`, cleanup passes.

**Evil biome.** Generation uses `ChasmRunner`/`ChasmRunnerSideways` + `AddShadowOrb` (one evil) or `CrimStart`/`CrimEnt`/`CrimVein`/`CrimPlaceHearts` (the other); `CheckOrb`/`DestroyHeart`/`FixHearts` handle the breakable hearts; `SmashAltar` seeds new ore after progression; spread happens through `UpdateWorld`/`hardUpdateWorld` with `CanEvilReplace` gating.

## 2. Rimtera before this pass

| Area | Rimtera had | Gap |
|---|---|---|
| Loop | fixed 60 Hz tick; renderer draws every visible tile each frame | no random tile updates, no growth/spread |
| Liquids | `LiquidSettler.Settle` over the whole grid every 0.2 s | no active list; cost grows with world area |
| Lighting | none (night tint only) | no light propagation, no light sources |
| Saves | every `WorldCell` as JSON | huge files for large worlds |
| Rooms | full rebuild on any tile change | too slow for large worlds |
| Worldgen | 160×72 – 320×144; 7 passes (terrain, caves, ores, crystals, biome layout, liquids) | no layered terrain, tunnels, chasms, islands, structures, walls, loot, life cores |
| Evil biome | static radiated pockets underground | no spread, no chasms/hearts/altars, no purification, no world choice |

## 3. Rimtera design (original)

- **Blight (world choice, like the two evils): Radiated Blight or Overgrowth.** Surface blight zones (2–4, away from the landing site) with chasms: Radiated = straight shafts lined with caustic crystal and radiated soil; Overgrowth = tangled vine caverns lined with overgrown soil and mossy rock. Hearts at the bottoms (Reactor cores / Spore hearts): breaking one gives loot; the third summons that blight's boss. Blight altars break only after the **Fracture** (defeating the last easy-tier boss), each smash seeding high-tier ore. The Fracture also carves diagonal blight stripes and speeds spread. Spread: random tile updates convert neighbouring convertible tiles (soil/grass, rock, sand/scrap, ice) — grass-only before the Fracture. Purification: a purifier sprayer (converts back in a radius on impact) and a purifier beacon that blocks spread nearby. Blight feeds the biome scanner, spawns (blight creatures from the sheets) and the deep pockets (deep pockets follow the world's blight).
- **Engine:** a `WorldTicker` doing random tile updates (grass regrowth, blight spread, crystal regrowth) proportional to world area; an active-liquid set with a wake buffer and a whole-world settle fallback; region/room rebuilds limited to dirty areas; RLE-compressed cells in saves (old JSON-cell saves still load); renderer-side light map (sun from open sky, light-emitting tiles and liquids, player/crew lights, blur with decay through air/solid/liquid) and depth-banded backgrounds.
- **World generation:** Tiny/Small/Medium/Large are 1750×900 / 4200×1200 / 6400×1800 / 8400×2400 (the constructor default stays 160×72 for tests). About 30 ordered stages cover layered-noise terrain, caves and tunnels, all biomes, blight zones, the Molten Chasm, ore/gem veins, life cores, caches and salvage pods, floating islands, structures, walls, lakes and seeps. `WorldGenDensity` scales underground counts by area and surface counts by width. The completion pass adds silt/slush, exposed gems, moss caves and long moss, speleothems, water plants and coral, rubble/shell piles, fallen logs, traps, statues, graveyards, slag foundries, canopy shrines, temple rooms and altar, buried landers, coolant springs, tide grottos, the phase pool, surface crates and sunken chests. Cleanup settles loose sand, smooths exposed edges into slopes and half blocks, drains stray dune water, fills enclosed cave walls and tops up chest loot.
