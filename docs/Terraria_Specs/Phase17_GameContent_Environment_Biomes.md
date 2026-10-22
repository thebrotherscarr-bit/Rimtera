# Terraria.GameContent — Scene Metrics, Biomes, Audio, Backgrounds, and Shops

## Evidence identity and scope

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`; assembly `Terraria` 1.4.5.8; SHA-256 `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- Static metadata and targeted ILSpy 11.1.0.9782 decompilation of `Terraria.SceneMetrics`, `Terraria.Player`, `Terraria.Main`, `Terraria.GameContent.ShopHelper`, `Terraria.WorldGen.SpawnTravelNPC`, and `Terraria.Chest` members. Game code was not run. `docs\Decompiled_Terraria` is absent; these observations come from the installed managed executable.

## 1. Scene scan and biome-zone matrices

### 1.1 Scan geometry and cadence

`SceneMetrics.AssumedConstantScreenSize = (1920,1200)`, `ZoneScanPadding = 25`, therefore:

```text
ZoneScanSize = (1920/16 + 2*25 - 1, 1200/16 + 2*25 - 1)
             = (169,124) tiles
TileCenter = Center.ToTileCoordinates().ClampedInWorld()
scanRect = ClampToWorld(Utils.CenteredRectangle(TileCenter, ZoneScanSize))
```

This build does **not** use a 99×87 scan rectangle. `Player.UpdateSceneMetrics()` calls `SceneMetrics.Scan` centered on `Player.Center` with NPC-position scanning enabled. Scan is cached for the same `Main.GameUpdateCount` and center; a changed tick or center resets arrays and repeats the work. Nested loops scan `x = Left..Right-1`, then `y = Top..Bottom-1`. Active tiles increment `_tileCounts[tile.type]`; inactive tiles with liquid increment `_liquidCounts[tile.liquidType()]`. The scan also records screen-only tile effects, candles, banners, NPC positions, and ore-finder candidates before aggregation and zone calculation.

### 1.2 Literal tile-count equations

`_tileCounts` length is `TileID.Count`; `_liquidCounts` length is `LiquidID.Count`. The following are the exact source tile-ID sets used by `AggregateTileCounts` in this build. `T[n]` denotes `_tileCounts[n]`.

```text
ShimmerTileCount = _liquidCounts[3]
HoneyBlockCount = T[229]
Holy0 = T[109]+T[492]+T[110]+T[113]+T[117]+T[116]+T[164]+T[403]+T[402]
SnowTileCount = T[147]+T[148]+T[161]+T[162]+T[164]+T[163]+T[200]

JungleTileCount = T[60]+T[61]+T[62]+T[74]+T[225]
if !remixWorld: JungleTileCount += T[226]

F = -10; if Main.infectedSeed: F *= 3    // F becomes -30
Evil0 = T[23]+T[661]+T[24]+T[25]+T[32]+T[112]+T[163]+T[400]+T[398]+T[27]*F
Blood0= T[199]+T[662]+T[201]+T[203]+T[200]+T[401]+T[399]+T[234]+T[352]+T[27]*F
if remixWorld: Evil0 += T[474]; Blood0 += T[195]
MushroomTileCount = T[70]+T[71]+T[72]+T[528]
MeteorTileCount = T[37]
DungeonTileCount = T[41]+T[43]+T[44]+T[481]+T[482]+T[483]
SandTileCount = T[53]+T[112]+T[116]+T[234]+T[397]+T[398]+T[402]+T[399]+T[396]+T[400]+T[403]+T[401]
PartyMonolithCount = T[455]
GraveyardTileCount = T[85] - T[27]/2
DesertSandTileCount = max(0, SandTileCount - OceanSandTileCount)
```

Before evil-biome arbitration, `Holy0`, `Evil0`, and `Blood0` are independently clamped at zero. Let those clamped values be `H`, `E`, and `B`:

```text
HolyTileCount = max(0, H - E - B)
EvilTileCount = max(0, E - H)
BloodTileCount = max(0, B - H)
```

Thus Hallow overlap is subtracted from both evil counts, and the original pre-subtraction Hallow count is used for both subtractions. Sunflower tile `27` contributes `-10` per tile to Evil/Blood counts, or `-30` per tile in `infectedSeed`. Graveyard count is clamped to zero after its sunflower subtraction; `HasSunflower` is cleared when `GraveyardTileCount > GraveyardTileMin` (`16`).

### 1.3 Threshold, maximum, and zone-property matrix

| Metric | Threshold used by `EnoughTiles...` | Declared maximum / variant | Resulting zone gate |
|---|---:|---:|---|
| Shimmer | `300` | — | `ZoneShimmer` also requires underground-for-shimmer depth and `!ZoneDungeon`. |
| Corruption | `300` | `CorruptionTileMax=1000` | `ZoneCorrupt = EvilTileCount >= 300`. |
| Crimson | `300` | `CrimsonTileMax=1000` | `ZoneCrimson = BloodTileCount >= 300`. |
| Hallow | `125` | `HallowTileMax=600` | `ZoneHallow = HolyTileCount >= 125`. |
| Jungle | `140` | `JungleTileMax=700` | `ZoneJungle = JungleTileCount >= 140 && !ZoneUnderworldHeight`. |
| Snow | `1500` normal; `300` if `WorldGen.Skyblock.lowTiles` | `SnowTileMax=6000` | `ZoneSnow = SnowTileCount >= selected threshold`. |
| Desert | `1500` normal; `300` if `WorldGen.Skyblock.lowTiles` | no DesertTileMax field observed | `ZoneDesert = DesertSandTileCount >= selected threshold`. |
| Glowing Mushroom | `100` | `MushroomTileMax=160` | `ZoneGlowshroom = MushroomTileCount >= 100`. |
| Meteor | `75` | — | `ZoneMeteor = MeteorTileCount >= 75`. |
| Dungeon | `250` | — | Requires count, dungeon-height gate, and `Main.wallDungeon[tileSafely.wall]`. |
| Graveyard | `28` | min `16`, max `36` | `ZoneGraveyard = GraveyardTileCount >= 28`. |

`*TileMax` fields are static declared limits; `AggregateTileCounts` does not clamp every count to those maxima. Additional exact gates: `ZoneSkyHeight = TileCenter.Y <= worldSurface*0.35`; Overworld spans `0.35*worldSurface < Y <= worldSurface`; Dirt is `worldSurface < Y <= rockLayer`; Rock is `rockLayer < Y <= UnderworldLayer`; Underworld is `Y > UnderworldLayer`. In drunk worlds or worlds without a surface, the dungeon height reference becomes `max(worldSurface, dungeonY+40)`. Lihzahrd is wall `87`; Granite walls `{184,180}`; Marble `{183,178}`; Hive `{108,86}`; Gem Cave walls `48..53`. `ZoneUndergroundDesert` requires `ZoneDesert`, below surface, a sandstone/hardened-sand conversion wall or wall `223`, and `!wallHouse[wall]`. Rain requires raining plus surface atmospherics; sandstorm requires desert + surface atmospherics + active Sandstorm and suppresses `ZoneRain`.

When `dualDungeonsSeed && BelowSurface && !ZoneUnderworldHeight`, the code scans down at most `300` tiles using `NPCSpawningFlagsForDualDungeons` and overwrites a selected set of zones from that secondary scan.

## 2. Background style selection and parallax

### 2.1 Surface background selector (`Main.GetPreferredBGStyleForPlayer`)

Precedence and selected `bgStyle` indices:

1. Beach: Hallow `6`; otherwise Corrupt chooses `1` when `BloodTileCount <= EvilTileCount`, else `8`; otherwise Crimson `8`; otherwise `HoneyBlockCount > 400 ? 3 : 4`.
2. Glowshroom `9`.
3. Desert: dominant corruption (`EvilTileCount > BloodTileCount && EvilTileCount > HolyTileCount`) `5`; else Crimson with `BloodTileCount > HolyTileCount` `14`; else Hallow `13`; else `2`.
4. Hallow `6`; Corrupt `1` or `8` using the same Blood/Evil comparison; Crimson `8`; Jungle `3`; Snow `7`.
5. Forest fallback uses screen-center tile X and `treeX[]`: default `0`; between `treeX[0]` and `[1]` => `10`; between `[1]` and `[2]` => `11`; at/after `[2]` => `12`.

`DrawBG` transitions to the preferred style: baseline layer transition speed `0.05`; menu speed `0.02`; changing to a non-forest style waits until `bgDelay > 30`, and to a forest style waits until `bgDelay > 60`; forest-to-forest with equal resolved world-background ID requests an instant transition. `instantBGTransitionCounter` overrides transition speed to `1`. Layer alphas advance/decrease by the transition amount and clamp to `[0,1]`.

### 2.2 Cave/underground style and texture selection

`DrawBackground_PickUndergroundBackgroundStyle` first selects `caveBackStyle[0..3]` from screen-center tile X vs `caveBackX[0..2]`, then adds `3`. A snow-count/vertical room test can set style `1`; jungle count above its threshold can override to `11` (with the snow-vs-jungle comparison when style is `1`). In a beach, corruption selects `19`, else crimson `21`, else Hallow `20`, else `18`. Away from beaches and in the band `(rockLayer+60, magmaLayer-60)`, snow + corruption/crimson/Hallow select `15/16/17`, while corruption/crimson/Hallow select `12/13/14`. `ZoneGlowshroom` is the final override to style `2`. `DrawBackground_UpdateBackgroundStyles` maps the chosen style and old style to texture-index arrays; style changes crossfade old/new selections.

The background horizontal parallax coefficient is:

```text
caveParallax = 1 - bgScroll / 500f
bgParallax = caveParallax
bgStartX = int(0 - IEEERemainder(layerWidth + screenPosition.X*bgParallax, layerWidth)
               - layerWidth/2) - int(drawOffset.X)
```

`bgScroll` is persisted under `BackgroundParallax`; the load/UI route recomputes `caveParallax` from it. Background layers use their own `layerWidth`, vertical repeat and draw offset, but this shared horizontal formula gives camera-motion parallax and tile wrapping.

## 3. Audio/music update and trigger precedence

`Main.UpdateAudio` exits when audio is unsupported or music is not loaded; updates SoundEngine when appropriate; `musicVolume==0` forces `curMusic=0`; dedicated server returns; `FocusHelper.AllowMusic` pauses/resumes audio. Drunk-world/swap state chooses `UpdateAudio_DecideOnNewMusic` or `UpdateAudio_DecideOnTOWMusic`. The new-music selector tracks the highest current `musicFade[]` and `lastMusicPlayed`, checks visible/nearby NPC boss/event categories, then selects through an ordered `if/else` chain. NPC music flags are generated only for active NPCs within a screen rectangle expanded by `5000` pixels around their center (selected types use `1600`); invasion sets and generic `boss` are separately classified.

High-level `newMusic` precedence in `UpdateAudio_DecideOnNewMusic`:

```text
[0] Music off / title-mode selection (menu path has its own seed/title-style branches).
[1] Credits Roll, Torch God minigame, visible boss/invasion tracks and special NPC categories.
[2] Eclipse; slime rain; Remix blood-moon region; Shimmer.
[3] Town-music gate: TownNPCCount >= 3 && !ZoneShadowCandle; day/night and graveyard/surface conditions choose its track.
[4] Sandstorm; Underworld; surface/underground depth; dungeon; biome-specific track selection.
[5] Pumpkin Moon and Snow Moon conditions near the surface override the chosen track at the end.
[6] UpdateAudio applies an active SceneMetrics music-box track mapping after the scene decision.
```

Selected exact scene assignments include Eclipse `27`, eligible surface slime rain `48`, Shimmer `91`, town day/night `46/47`, Sandstorm `40`, Underworld `36`, Dungeon `23`, Corrupt surface/underground `8/10` (or Crimson-dominant variants `16/33`), Crimson `16/33`, Desert `21`, Underground Desert `61`, Graveyard `53`, and surface Snow `14`; the complete CIL selector contains additional bosses, world seeds, weather and depth branches. Music-box IDs are mapped individually by an explicit branch table; `SceneMetrics.ActiveMusicBox` is populated by the scene scan.

## 4. Shop price / happiness modifier curves

`ShopHelper.GetShoppingSettings(player,npc)` starts at `1.0` and calls `ProcessMood`. The mood/personalities are generated from `PersonalityDatabase`/`PersonalityDatabasePopulator`; each applicable personality trait multiplies the running scalar.

| Trait or context | Exact multiplier / branch |
|---|---:|
| Love biome / NPC | `×0.88` |
| Like biome / NPC | `×0.94` |
| Dislike biome / NPC | `×1.06` |
| Hate biome / NPC | `×1.12` |
| `npc.loveStruck` | applied first as `×0.9` |
| `npcsWithinHouse > 3` | `×1.05` for each loop iteration `i=3..count-1`; text is dislike crowded, or hate crowded when count `>6` |
| space condition (`house <=2 && village <4`) | `×0.95` |
| homeless/far from home, or player in an evil biome | set working value to sentinel `1000` before final clamp |
| final happiness clamp/round | `round(clamp(value,0.75,1.5)*100)/100` |

Remix world, NPC type `368` (Traveling Merchant), type `453` (Skeleton Merchant), town pets, and `IsNotReallyTownNPC` return before resident happiness rules (after the love-struck factor). Type `663` has a special loneliness check; without two residents in both house/village measures it uses the `1000` sentinel. `LowestPossiblePriceMultiplier=0.75`, `HighestPossiblePriceMultiplier=1.5`, `MaxHappinessAchievementPriceMultiplier=0.82`.

Separate merchant-specific price functions: Traveling Merchant raw distance factor is `d = Distance(merchantCenter/16, (spawnTileX,spawnTileY)) / (Main.maxTilesX/2)`, then `num=1.5-d`, return `(2+num)/3 = (3.5-d)/3`. Skeleton Merchant uses moon phase multipliers `1.0` default; phases `1/7:1.1`, `2/6:1.2`, `3/5:1.3`, `4:1.4`, plus `0.1` during daytime.

## 5. Traveling Merchant arrival and stock-pool pipeline

### 5.1 Time-based arrival roll and candidate pool

In the non-client `Main.UpdateTime` branch, if no Traveling Merchant is present and time is not fast-forwarded, the eligible window is `dayTime && time < 27000`. Let `r=max(dayRate,1)` and `N=int(27000/r)*4`; each eligible update checks `Main.rand.Next(N)==0`. Only after that roll does it require at least two active town NPCs excluding types `37` and `453`. `WorldGen.SpawnTravelNPC` separately rejects Eclipse, non-day, active invasion, or an already-active type `368`.

Candidate array construction:

1. Collect active town NPCs except type `37` that are housed (`!homeless`); store NPC index and `(homeTileX,homeTileY)`.
2. Only if that set is empty, collect active homeless town NPCs except type `37` for which `TownManager.HasRoom(type,out roomPosition)` succeeds; use the returned room position.
3. If still empty, return. Otherwise select candidate uniformly by `Main.rand.Next(candidateCount)`.
4. Search left/right within `10` tiles for floor plus three-block headroom. Up to `30` random x samples choose the arrival location; first `20` samples preferentially reject positions within one tile of the home point.
5. For homes at/above world surface, scan outward distances `20..499` on both sides at x offsets `±2*n`; scan y from `max(10,homeY-n)` to `min(homeY+n,worldSurface)`. Require a solid floor, no liquid in the three headroom cells, no solid overlap in the 3×3 clearance, and no active player rectangle intersecting the NPC safe-range rectangle. If this fallback finds none, the function still uses its initialized candidate coordinates.
6. Spawn NPC type `368` at tile `(x,y)`, store home tiles, mark homeless, face toward its home, and network-update. Merchant departure is checked after daytime/time `48600`; `UnspawnTravelNPC` removes it only when its expanded safe-range rectangle intersects no active player.

### 5.2 Traveling-shop logical stock count and selection arrays

`Chest.SetupTravelShop` clears `Main.travelShop`, chooses the highest-luck player, sets the logical item target `num=Main.rand.Next(4,7)` (`4..6`), then increments `num` for each successful independent `RollLuck(4)`, `(8)`, `(16)`, `(32)`; expert mode adds a `RollLuck(2)` increment; Peddler's Satchel adds `1`; tenth-anniversary world adds `1` and adds another unless `getGoodWorld`. A physical slot counter `count` and logical addition counter `added` are distinct because bundles occupy multiple array slots.

The rarity denominator vector is exactly `[100,200,300,400,500,600]`. Candidate helper calls apply six independent `RollLuck(rarity[tier])` checks against type/progression-specific item branches; hardmode first runs up to `5000` attempts for a candidate with minimum rarity `2`, then ordinary items are added until `added >= num`. A painting is separately attempted for up to `5000` iterations. On repeated attempts, rarity floors progressively: at attempts `>4200`, `>4300`, `>4400`, `>4500`, `>4600`, `>4700`, rarity slots `[0]..[5]` respectively become `1`.

`SetupTravelShop_CanAddItemToShop` rejects item `0`, exact duplicates, and item `3637` if any of its incompatible paired-set IDs (`3621..3622` or `3633..3642`) already occurs. `SetupTravelShop_AddToShop` increments `added` once per logical selection and writes linked companion items for defined sets; type `3637` instead chooses one of six two-item alternatives with `Main.rand.Next(6)`. The complete candidate/item progression pool is embodied in the type-specific `SetupTravelShop_GetItem` / `SetupTravelShop_GetPainting` branches and is intentionally not flattened into an unrelated content dataset here.

## 6. NPC biome-preference data matrix

PersonalityDatabasePopulator.Populate_BiomePreferences creates biome predicates for Ocean, Forest, Snow, Desert, Jungle, Underground, Hallow, and Mushroom and registers exactly 25 NPC-keyed preference lists. AffectionLevel values are Love 100, Like 50, Dislike -50, Hate -100; BiomePreferenceListTrait.ModifyShopPrice chooses the highest-affection preference among all matching biomes. This resolves simultaneous-zone overlap deterministically. The complete NPC → biome table, exact NPC type IDs, and predicate fields are in Phase18_NPC_Drops_Happiness_Registries.md.

Predicate field matrix: Ocean = player.ZoneBeach; Forest = player.ShoppingZone_Forest; Snow = player.ZoneSnow; Desert = player.ZoneDesert; Jungle = player.ZoneJungle; Underground = player.ShoppingZone_BelowSurface; Hallow = player.ZoneHallow; Mushroom = player.ZoneGlowshroom; dangerous-biome override = Corruption, Crimson, or Dungeon. The separate multiplier vector remains Love 0.88, Like 0.94, Dislike 1.06, Hate 1.12.