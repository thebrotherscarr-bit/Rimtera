# World size and the world map: Terraria structure vs Rimtera

Evidence: REA metadata of `Terraria.exe` 1.4.5.8 — `docs/terraria-evidence/map.json` (`Map.WorldMap`, `Map.MapTile`, `Map.MapHelper`, `Map.MapOverlayDrawContext`, `IO.WorldFileData`, `WorldBuilding.WorldGenConfiguration`) plus `main.json` and `worldgen.json`. REA confirms `WorldGen.WorldSizeSmallX/Y`, `WorldSizeMediumX/Y`, `WorldSizeLargeX/Y` are compile-time integer constants (`Static | Literal | HasDefault`) but does not extract their values; the dimensions below are the publicly documented ones. No code or tables were copied.

## 1. Observed structure

### Sizing
- **World size:** a world is a fixed tile grid chosen at creation: Small 4200×1200, Medium 6400×1800, Large 8400×2400. These are stored in the world file header (`WorldFileData.WorldSizeX/Y`, `WorldSizeName`) and applied before generation (`WorldGen.SetWorldSize`, `clearWorld`). `Main.maxTilesX/Y` bound every system.
- **Bands:** vertical bands are fractions of the height (`Main.worldSurface`, `Main.rockLayer`, an underworld band at the bottom). Horizontal layout reserves oceans at both edges (`oceanDistance`, `beachDistance`, `TuneOceanDepth`) and places the dungeon and jungle on opposite sides.
- **Generation scaling:** passes scale their feature counts and sizes with the world area, configured per pass and biome (`WorldGenConfiguration.GetPassConfiguration`, `CreateBiome`).

### Map
- **Data:** the map is a second grid the size of the world (`WorldMap._tiles`, `MaxWidth`/`MaxHeight`) of compact `MapTile`s: a lookup type plus a light level. A tile becomes known only when lit near the player (`UpdateLighting`, `UnlockMapTilePretty`, `IsRevealed`). Changes are queued and batched (`QueueUpdate`, `ConsumeUpdate`, `maxMapUpdates`).
- **Colours:** come from a lookup built once per tile, wall and liquid type, with depth-based sky, dirt, rock and underworld gradients (`MapHelper.Initialize`, `colorLookup`, `skyPosition`/`dirtPosition`/`rockPosition`/`hellPosition`, `CalcSkyGradient`).
- **Saves:** the map is saved per player per world as its own compressed, chunked file (`SaveMap`, `InternalSaveMap`, `LoadMapVersionCompressed`, `MapChunkSize`, `WorldFileData.MapFileName`).
- **Views:** three ways to see the map:
  - a minimap (`miniMapX/Y/Width/Height`, `mapMinimapScale`, `mapMinimapAlpha`)
  - an overlay (`mapOverlayScale`, `mapOverlayAlpha`)
  - a fullscreen map with pan and zoom (`mapFullscreen`, `mapFullscreenScale`, `mapFullscreenPos`, `PanTargetMapFullscreen`)

  All three draw icons for players, NPC heads, bosses, pylons, death markers and spawn (`MapIcons`, `MapOverlayDrawContext`). `mapStyle` cycles between minimap, overlay and off.

## 2. Rimtera now (2026-10-21)

The sizing and map work below is implemented; the table records the current state rather than a gap list.

| Area | Rimtera | Status |
|---|---|---|
| Sizes | Tiny 1750×900, Small 4200×1200, Medium 6400×1800, Large 8400×2400 (tiles) | ✅ Large is 20.2 M cells, the reference footprint |
| Cell | `WorldCell` is packed into 8 bytes: tile (u16), wall (u16), liquid units (u8), liquid kind + flags (u8), damage (u8), frame mask (u8) | ✅ Large's raw grid is about 161 MB; measured process use is about 700 MB |
| Generation | ~30 ordered stages in `LargeWorldGenerator`; per-pass `WorldGenDensity` scales underground features by area and surface features by width | ✅ Tiny→Medium (7.3× area) scales feature counts 5–10×; surface features scale 2.4–5× with width |
| Rooms | region map with dirty-area framing and an outdoors fallback for open regions over the room cap | ✅ no full-world rebuild while playing |
| Map | `WorldMapState`: 64×64 chunks allocated on first reveal, light-gated reveals, per-character/per-world `.rmap`, minimap/overlay/fullscreen with pan, zoom, icons and pylon travel | ✅ everything in section 1 except third-party compatibility |

## 3. Rimtera design (implemented 2026-10-21)

- **World sizes:** Small 4200×1200, Medium 6400×1800, Large 8400×2400 (the reference dimensions, matching the requested map scale). Plus a Tiny 1750×900 option for quick tests and low-memory machines. The menu shows tile size and an approximate memory figure.
- **Compact cells:** pack `WorldCell` to 8 bytes:
  - tile ushort, wall ushort, liquid amount byte (0–255) plus kind (3 bits), damage (5 bits)
  - frame mask byte (atlas coordinates derived from the mask, not stored)
  - spare byte for paint or flags

  Large becomes about 160 MB, accessed through the existing `TileGrid` API so callers don't change.
- **Bands:** surface at 25–35% of height (noise per column), dirt layer to about 45%, cavern layer to about 85%, Molten Chasm in the bottom 10%. Acid Coast oceans 250–450 tiles wide at both edges. Biome widths become fractions of the world width, with feature counts scaled by area.
- **Generation:** chunked and parallel where passes are independent, with progress per pass. Target: under 20 s on Large on a normal desktop.
- **Engine:**
  - region map keeps rooms exact (rooms cap at 1024 cells), marks any open region over the cap as Outdoors without a full count, and never fully rebuilds
  - random tile updates, liquids, lighting and rendering stay local, as now
  - world ticker samples per chunk
- **World map:**
  - a `WorldMap` grid of compact map tiles (colour index + light)
  - revealed by the light map around the player (not by distance) plus explored chunks
  - colours from the sprite-average lookup plus depth gradients
  - saved per character per world as a compressed chunked file next to the world save
- **Map UI:**
  - minimap (resizable, zoom, transparency)
  - overlay mode
  - fullscreen map on M with drag-pan, wheel zoom and icons for player, colonists, town NPC heads, bosses, blight hearts, death marker, spawn and placed beacons
  - Tab cycles minimap → overlay → off, matching the settings already in Options

## 4. Reveal mechanics (map and player)

Additional evidence: call edges and field accesses in `map.json`, `engine.json`, `main.json`, `fishing.json` (Player), `worldgen.json`, and `map-layers.json` (`MapIconOverlay`, `IMapLayer`, `LightingEngine`).

### Observed
- **Reveal comes from lighting:** `LightingEngine.ProcessArea` runs scan → blur → present, then calls `ExportToMiniMap`. That method intersects the processed light area (screen plus padding, `Rectangle.Inflate`/`Intersect`) and, in a parallel loop over columns, writes every cell whose light is high enough into the world map with its light level. It runs only when the map is enabled and `updateMap` is set.
- **What a revealed cell stores:** the tile type the player saw and how bright it was. The map shows what was seen, not live state; it updates only when the area is lit again. `WorldMap.UnlockMapTilePretty` → `UpdateLighting` / `UpdateType` maintain this, and `QueueUpdate`/`ConsumeUpdate` batch redraws of changed map chunks.
- **Section unlock:** `UnlockMapSection` (called from `Main.DoDraw`) reveals whole sections when they arrive or load, separately from lighting.
- **Light is the gate, not distance or line of sight:** a dark cave next to the player stays black on the map; a torch-lit room far away (still inside the processed area) is revealed. This is why torches and glowing tiles matter for mapping.
- **Player side:**
  - the map belongs to the player: saved per player per world (`Player.InternalSaveMap`, `WorldFileData.MapFileName`)
  - spawning and teleporting reset `mapTime` to force a refresh
  - the fullscreen map is opened by the player (`Player.TryOpeningFullscreenMap`) and blocks most actions while open (`TryInterruptingItemUsage`, `ToggleInv`, `QuickGrapple` check `mapFullscreen`)
  - death records a marker for the map
- **World side:** `WorldGen.clearWorld` resets all map flags, and section re-framing touches `mapTime`.
- **Icons:** drawn by an ordered set of `IMapLayer`s (`MapIconOverlay.AddLayer`/`Draw`): spawn, players, NPC heads and bosses, pylons (whose reveal is checked with `IsRevealed` before you can teleport), pings and death markers.

### Rimtera before the map pass (historical)
- The minimap marked every cell within 22 tiles of the player as explored, regardless of light or what was there.
- It drew live tile colours from the current world, and the explored flags were lost on quit or load.
- No fullscreen map, icons, death marker or section reveal.

### Rimtera map design (implemented)
- **`WorldMapState`** (`UI/WorldMap.cs`): stored in 64×64 chunks allocated on first reveal, so unexplored space costs nothing. Each revealed cell stores what was seen (tile, wall or liquid key) and the light it was seen at.
- **Reveal:** after each `LightMap.Compute`, every cell in the lit area brighter than a threshold is recorded; a darker re-sighting keeps the brighter light. Sky and open air above the surface reveal as sky when the column is lit. Dark caves stay hidden until lit (lamps, lava, crystals, the player's own light), the same rule as the reference.
- **Drawing:**
  - map colours come from sprite-average tile colours, shaded by the remembered light
  - unrevealed cells are black
  - air uses depth gradients (sky, underground, cavern, chasm)
- **Saving:** per character per world in `maps/<world>_<character>.rmap`, written alongside world saves and loaded when the world starts. Deflate-compressed chunk list.
- **Player integration:** spawn and respawn re-reveal around the landing site, death leaves a marker, and the fullscreen map blocks gameplay input (the world keeps running).
- **Views:**
  - the minimap reads the remembered map
  - the fullscreen map (M) supports drag-pan and wheel-zoom
  - icon layers, in order: spawn, death marker, colonists, Station AI, bosses (only on revealed cells), player
