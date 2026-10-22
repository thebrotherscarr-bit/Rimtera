# Flora parity

Rimtera's flora now follows the reference game's flora **rules and numbers**. The evidence is the reference executable (v1.4.5.8). The rules are written in Rimtera's own code and words and apply to Rimtera's own plants. No reference code, names or assets were copied.

- **Rules and constants:** `Systems/FloraParity.cs`
- **Simulation side** (felling, shaking, cutting, placement): `Systems/FloraParity.Game.cs`
- **Random updates:** `Systems/WorldTicker.cs`
- **Tests:** `Systems/FloraParitySelfTests.cs`

## Evidence

CIL dumps are in `docs/terraria-evidence/full/cil/`. Switch tables are decoded to their jump targets.

To rebuild them, run `python docs/terraria-evidence/full/scripts/dumpflora.py docs/terraria-evidence/full/scripts/flora_methods.txt`. The script uses `ilall.DB` and `interp.Method` and reads `members-ALL.json` once.

`@N` is an IL offset in the named method. Unless another class is named, methods belong to `WorldGen`.

## Rules

Status values:

- **MATCHED:** same rule and numbers.
- **CHANGED:** Rimtera changed to match.
- **PARTIAL:** matched with a noted adaptation.
- **DIFFERENT:** a deliberate difference, listed below.

### Random tile updates

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| Above-ground random updates per tick = cells × 3e-5, at a random column and a row above the world surface. Below-ground updates = cells × 1.5e-5, at rows from the surface to the bottom (`UpdateWorld` @256–@297, loops @525–@1220 and @1348–@1433). | One stream of cells × 2e-4 (1/5000) over the whole world. Plants grew from it. | Flora has its own stream at the reference rates: `FloraParity.OvergroundUpdatesPerCell` and `UndergroundUpdatesPerCell` in `WorldTicker.FloraTick`. The surface line is Rimtera's depth line, 0.45 × height. | CHANGED |
| Rain multiplies the above-ground count by 1.5 (`UpdateWorld` @421–@440). | No rain effect. | `RainUpdateMultiplier` applies when `FloraConditions.Raining` is true. | MATCHED |
| Each above-ground update has a 1 in `Lerp(151, 151×2.8, clamp(width/4200 − 1)) × 100` chance to plant a wild herb (`UpdateWorld` @442–@547). | On any grass update, 1/12 × 1/3 sprouted a herb (no crowding within 3). | `HerbSpawnOneIn(width)`: 15 100 for widths up to 4200. | MATCHED |

### Trees

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| A sapling grows on 1 random update in 20 (`UpdateWorld_OvergroundTile` @4353–@4396 → `AttemptToGrowTreeFromSapling`). | 1 in 3. | `SaplingGrowOneIn = 20`. | CHANGED |
| Tree ground must be fit for a tree: grass, blight grasses, jungle, mushroom and hallowed grass, snow, ash grass (`IsTileTypeFitForTree`). Palms grow on sand. A tile beside the base must also be fit (`GrowTree` @190–@399). | Soil and mud also counted, and no neighbour was needed. | `FloraParity.SaplingGroundFits`, used both when placing a sapling and when it grows. Bare soil and mud are refused. `WorldTicker.TryGrowSapling` checks the neighbour. | CHANGED |
| No liquid at the base row. Trunk height is `Next(5, 17)` (@500–@513). The crown needs height + 4 clear rows (@602–@606), five wide (`EmptyTileCheck` @659–@700), and the sapling row plus the row above it clear three wide. | Height 5–10, shrunk to fit. Only the trunk column and crown cells were checked. | Height 5–16. The sapling row and the row above must be clear three wide. The trunk needs 4 clear rows above it, five wide; if that room is short the tree grows shorter (minimum 5). | PARTIAL (see difference 4) |
| Hitting any trunk tile damages that tile. When it breaks, every tile above loses support and is killed. Each killed tile runs its own drop (`KillTile`, `CheckTree`). | The trunk fell from the hit segment up, but timber was trunks × 2 for the whole tree. | `FellTree` rolls drops for every felled trunk segment, the leafy top and the two leafy branches. The stump below the cut stays. | CHANGED |
| Every tree tile drops 1 wood. The stack gains 1 when `genRand.Next(35) ≤ axe` (the axe field is tooltip power ÷ 5), otherwise on `Main.rand.Next(3) == 0` (`KillTile_GetTreeDrops` @255–@344; stack +1 in `KillTile_GetItemDrops` @6513–@6542). | 2 per trunk segment, with no tool effect. | `FloraParity.SegmentWood(axe)`. The axe field is taken from the cutter that hit the tree (`OnCutterHitFlora`); blasts and debug breaks use 0. | CHANGED |
| Leafy tops and branches (frameX ≥ 22, frameY ≥ 198) drop an acorn on `Next(2) == 0`, but only for tree types that drop acorns. Jungle and mushroom trees do not (`KillTile_GetTreeDrops` @0–@88, `TreeTypeDropsAcorns`). | 1–2 saplings per fell, always, plus 1 in 3 for 1–2 berries. | One 1-in-2 sapling roll each for the top and the two branches. Canopy (jungle) and shroom trees roll none. The fell no longer drops berries. | CHANGED |
| Each mushroom-tree tile drops a glowing mushroom on 1 in 2 instead of wood (`KillTile_GetTreeDrops` @218–@244). | 2–4 glow spores per fell. | Each shroom segment drops a glow spore on 1 in 2, plus stalkwood (see difference 6). | PARTIAL |
| A gem-tree tile drops its gem on 1 in 10, otherwise stone. Its tops and branches drop the gem's tree seed on 1 in 2 (`SetGemTreeDrops`). | 2–4 of the crown's gem per fell. | Each geode segment drops its gem on 1 in 10, and the top and branches drop it on 1 in 2. Each segment also drops gemwood (see difference 5). | PARTIAL |
| Each biome grows its own tree (`GetTreeType`, `TryGrowingTreeByType`). | Already true: 8 species plus geode trees. | Unchanged. Saplings take the species of their ground. | MATCHED |
| A canopy (jungle) plant on jungle grass becomes a tree on 1 update in 500 (`UpdateWorld_GrassGrowth` @4035–@4154). Mushroom grass grows a mushroom tree on 1 in 250 above ground and 1 in 300 below (@1714, @3634). Forests never sprout trees on their own. | Open ground sprouted saplings (1/12 × 1/6, none within 4). | Canopy and jungle grass with a grass plant on top: 1 in 500 grows a tree. Glowshroom grass: 1 in 250 above the surface line, 1 in 300 below, grows a shroom tree. Saplings no longer sprout on their own. | CHANGED |

### Shaking trees

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| An axe hit that does not break a shakeable tile shakes the tree (`KillTile` @1448–@1477 → `ShakeTree`). Clicking a tree does nothing. | Clicking (interacting with) a tree shook it. Cutter hits did not. | A cutter hit calls `OnCutterHitFlora` → `ShakeTree`. Clicking no longer shakes. | CHANGED |
| Each tree shakes once until the shake list resets (`ShakeTree` @0–@105; `ResetTreeShakes`). It needs a leafy top that is not buried in solid tiles: `Collision.SolidTiles` over x ± 2, y ± 2 (@110–@201). | Once per tree per day. No crown check. | Once per tree per day, and only with a canopy at the top that has no solid block within 2 cells. | MATCHED |
| The first roll that hits wins. Acorn ×1–2 on 1 in 7 (tree types 1, 6, 7, 12; @962–@1045). Mushroom seeds on 1 in 8 (@1050). The tree's own drop ×1–3 on 1 in 12 (@1210–@1326). Coins on 1 in 20 (@1331–@1568). A critter on 1 in 15 (forest and hallow, @1573). An owl on 1 in 50 (forest, at night, @1914). A butterfly on 1 in 20 (forest or hallow, by day, dry, @3135). Fruit on 1 in 12 (per tree type, @3502–@4331). | 40% berries, 20% sapling, 10% timber, 30% nothing. | `FloraParity.RollShake` follows the same order and chances, mapped to Rimtera's items and critters. Drops fall from the crown as ground items. | CHANGED |
| Coins: copper ×50–99. On 1 in 30, gold ×1 (+1 on 1 in 5, +1 on 1 in 10). Otherwise, on 1 in 10, silver ×1–20 (@1331–@1525). | No coins. | `ShakeCredits`: 1 credit per 10 copper and 10 per silver; gold is capped at 100 credits a coin. | PARTIAL |

### Herbs

Bloom rules map onto Rimtera's six herbs as follows:

| Rimtera herb | Reference counterpart | Rimtera rule |
|---|---|---|
| Sunpetal | Daybloom | Daytime |
| Moonglow | Moonglow | Night |
| Rainreed | Waterleaf | Rain |
| Emberroot | Fireblossom | Dusk |
| Sporecap | Blinkroot | Flicker |
| Frostbell | Shiverthorn | Settled |

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| Every random update on a herb runs `GrowAlch` (`UpdateWorld_OvergroundTile` @198–@230). | — | Herbs grow from the flora stream. | MATCHED |
| A young herb matures on 1 in 50; Shiverthorn needs a further 1 in 2 (`GrowAlch` @148–@278). Otherwise an extra 1 in 50 applies in the herb's own conditions: Daybloom by day, Moonglow at night, Waterleaf in rain, Blinkroot below the surface, Fireblossom in the underworld (@279–@837). | 1 in 2, no conditions. | Same chances and conditions: Sunpetal by day, Moonglow at night, Rainreed in rain, Sporecap below the surface line, Emberroot in the bottom sixth of the world. | CHANGED |
| Blooms (`IsAlchemyPlantHarvestable`): Daybloom by day; Moonglow at night; Deathweed at night with a full or blood moon; Waterleaf in rain or clouds; Fireblossom when `time > 40500` (the last quarter of the day) and not raining, unless below the surface. Blinkroot flickers: on 2 of 3 updates, bloom on with 1 in 2, or bloom off (`GrowAlch` @838–@1028). Shiverthorn blooms for good on 1 in 30 once mature (@1029–@1124). | Day, Night, Rain > 0.15, Cold (snow or night), Always. | `HerbBloomRule` has Daytime, Night, Rain (any rain), Dusk (day progress > 0.75 and not raining, unless below the surface), Flicker and Settled (stored in state bit 1), and FullMoon (moon phase 0). | CHANGED |
| Liquid kills a herb, except water for Moonglow and Waterleaf and lava for Fireblossom (`GrowAlch` @0–@143). | Not modelled. | `TilePhysics.HerbSurvivesLiquid`. | CHANGED |
| Drops: a young herb (tile 82) drops nothing (switch index 82 → @8953). A grown herb drops itself ×1, plus 1–3 seeds when it can be harvested (`KillTile_GetItemDrops` @8523–@8661, `IsHarvestableHerbWithSeed`). | Blooming: herb 1–2 + seeds 1–3. Mature: herb 1. Young: nothing. | Blooming: herb 1 + seeds 1–3. Mature: herb 1. Young: nothing. | CHANGED |
| Herbs are placed on their own soils, or in a clay pot or planter box for any herb (`PlaceAlch` @141–@785). | Own soils only. Planters did not hold herbs. | Own soils, or a plant pot or herb planter. A herb in a planter keeps its support. | CHANGED |
| `PlantAlch` picks a random column and a depth (1 in 40 deep, else 1 in 10 anywhere, else below the surface). It scans down to the first ground that is open and dry above, needs fewer than 5 herbs within 15 × width/4200 cells, then plants whichever herb suits the ground (`PlantAlch` @0–@489, `PlaceSuitableHerbHere`). | See the random-updates table. | `WorldTicker.PlantWildHerb`. | MATCHED |

### Grass and grass plants

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| A grass update spreads into every dirt or mud tile in its 3×3 that is exposed (some 3×3 neighbour is empty or non-solid) and has no lava nearby (`UpdateWorld_GrassGrowth` @2692–@3063, `SpreadGrass` @263–@455). | Soil open above and touching grass turned to grass on 1 in 2 when the soil was updated. | Grass, hallow and prismatic grass spread into soil. Jungle and canopy grass spread into mud. Glowshroom grass spreads into mud and fungal soil. | CHANGED |
| Grass that spreads into dirt does so only above the world surface (`SpreadGrass` @239–@258); jungle and mushroom grass spread anywhere. Hallowed grass spreads only in hardmode (`AllowedToSpreadInfections`). | No depth rule. | Grassland grass spreads only above the surface line. Hallow and prismatic grass spread only after the Fracture. Blight keeps Rimtera's own spread (`BlightRules`). | CHANGED |
| A grass tile with an open, dry top grows a plant: 1 in 12 on forest grass, 1 in 10 on evil, hallowed and ash grass (@2146–@2339), 1 in 7 on jungle grass (@3868–@4010), 1 in 10 on mushroom grass (@3455–@3614). | Plants were render-only decorations. | New tiles `GrassPlant` and `TallGrassPlant`, using the same chances (`FloraParity.PlantOneIn`). | CHANGED |
| A short plant grows tall on 1 in 20 (`UpdateWorld_OvergroundTile` @4519–@4616 and @4618–@4708). | — | `TallGrassOneIn = 20`. | MATCHED |
| Grass plants drop nothing, unless the player carries a blowpipe (seeds) or the plant is the mushroom style (`KillTile_GetItemDrops` @5708–@5744, @5871; `KillTile_ShouldDropSeeds`). A plant on the wrong ground is removed (`PlantCheck`). | — | Grass plants drop nothing. They pop off when their ground goes. Their sprite follows the ground below them. | PARTIAL |
| Placing a block over a grass plant destroys the plant (`Player.PlaceThing_Tiles_PlaceIt_KillGrassForSolids`). | — | `ClearPlantForPlacement` handles the player's placement. `PlacementSystem` treats grass plants as replaceable and does not count them as anchors. | MATCHED |

### Vines, cacti and other plants

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| A grass or vine tile with empty, lava-free space below grows a vine on each update (`UpdateWorld_OvergroundTile` @4924–@5135). Growth is limited by `GrowMoreVines`: vines in x ± 4 and from 6 rows above to 10 rows below weigh 1 + 2 × (rows below), and growth stops above 60. | Grass undersides grew a vine on 1 in 6; vines lengthened on 1 in 3 up to 8 long. | `WorldTicker.GrowVineBelow` uses the density rule. Grass, jungle, canopy and hallow grass grow vines. | CHANGED |
| Vines drop a vine only with the cordage guide (`KillTile_GetItemDrops` @5891–@5931). | Nothing. | Nothing. | MATCHED |
| A cactus grows on 1 update in 15 (`UpdateWorld_OvergroundTile` @2104–@2149). It stops when its cells reach `Next(11, 13)` or its stem reaches `Next(2, 8)` (`GrowCactus` @1015–@1059, @1698–@1712). Open desert sand sprouts a cactus on 1 in 300 (@2744–@2786). | 1 in 3 up to 5 tall. Sand sprouted cacti on 1/12 × 1/4. | 1 in 15 with both caps, giving at most 7. Sprouting is 1 in 300 on open desert sand above the surface line, with the cells on each side clear. | CHANGED |
| Melee swings cut every cuttable tile in the item's hitbox (`Player.ItemCheck_CutTiles` @0–@594). Projectiles that can cut tiles cut what they pass, but never tree trunks (`Projectile.CutTilesAt` @166–@345). Nothing growing in a clay pot or planter box is cut (`CanCutTile` @14–@176). | Swings and shots ignored plants. | `FloraParity.IsCuttable`: grass plants, tall grass, vines, long moss, herbs, pots, wild plants, wild mushrooms, wild gourds, dye blossoms, reeds, lily pads and seaweed. The melee path cuts a 3×3 square around the target. Shaped swings cut along the blade every tick. Player projectiles cut each cell they enter. Shaped melee projectiles (yo-yos, boomerangs and flails) cut each cell they pass. Plants in planters are skipped. | CHANGED |

### World generation and rendering

| Reference rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|
| World generation covers the surface grass in plants (the reference's "Grass Plants", "Jungle Plants", "Glowing Mushroom Plants" and "Flowers" passes). | Render-only sprites on about 16% of exposed ground (`SpriteCatalog.DecorationsFor`). | The new pass "Sprouting grass and flowers" (`ContentFillFlora.GenerateGrassPlants`, run just before the landing site is chosen) places real plants on 2/3 of open plant ground above the surface line, and on 3/4 of cave jungle and glowshroom floors. A quarter of them are tall. The render-only layer now draws only rocks, logs, crystals and debris (`SpriteCatalog.DebrisFor`). Plants are drawn from `SpriteCatalog.PlantSpritesFor(ground)`. | CHANGED |

## Deliberate differences

1. **Two random-update streams.** Blight and crystal spread, crystal regrowth, wild-plant and geode-tree regrowth, and lifeblooms keep Rimtera's own world pass at cells × 2e-4. Their balance was tuned to that rate. Only the reference's flora rules run at the reference rates.
2. **Surface line.** The reference splits its updates at its world-surface row. Rimtera uses its existing depth line at 0.45 × height, the same line the biome scanner uses. "Underworld" means the bottom sixth of the world.
3. **Crowns, tops and branches.** Rimtera trees have a crown of cells, not random branch tiles along the trunk. The crown counts as the leafy top. The crown cells directly beside the top trunk segment count as the two leafy branches. Each gets the reference's wood and sapling rolls. Other crown cells drop nothing.
4. **Short trees instead of failed growth.** When the room above a sapling is short of height + 4 rows, Rimtera grows a shorter tree (at least 5 tall). The reference fails and retries. Tree growth also does not check background walls.
5. **Geode trees** drop gemwood per segment, not stone, so gemwood stays obtainable. Rimtera has no gem-tree seed, so the 1-in-2 roll on the top and branches drops the crown's gem, and geode trees still regrow on deep rock by themselves (Rimtera rule).
6. **Shroom trees** drop stalkwood per segment as well as the reference's 1-in-2 glow spore, so stalkwood stays obtainable. Prismwood also sheds 1–3 prism shards, a Rimtera extra.
7. **Shake drops mapped to Rimtera:**
   - Acorn → sapling.
   - Mushroom seeds → glow spore.
   - Fruit → berry.
   - Coins → credits.
   - Critters → thistle finch or regolith vole; at night, a dusk glider; on a clear day, a sunbeam moth.

   Not modelled: the very rare 1-in-100 to 1-in-1000 special items, Halloween drops, hostile drops from evil trees, bee and hornet swarms, and jungle and ash critters.
8. **Herbs.** Each herb keeps its own Rimtera soils. Rain means `Weather.Rain > 0`, because Rimtera has no separate cloud cover. The FullMoon rule (Deathweed) exists, but none of the six herbs uses it. Herbs can still be picked by clicking them (a Rimtera convenience). There is no Staff of Regrowth, so no bonus harvest.
9. **Cacti** are single columns without arms. Both of the reference's caps are applied to the column's height.
10. **Vines** skip the reference's line-of-sight test in `GrowMoreVines`. The density weights and limit are the same.
11. **Grass plants.** One short tile and one tall tile cover every grass type; the sprite comes from the ground below. There is no mushroom style and no blowpipe seed drop.
12. **Seasonal and water plants.** Wild gourds are cut like grown pumpkins, but nothing grows them (Rimtera has no Halloween season for this). Seaweed, reeds, lily pads and coral are cut or dropped by the reference's rules but do not grow. Bamboo and sea oats have no Rimtera analog. Lifeblooms keep Rimtera's own life-fruit rule.
13. **Cutting area.** Rimtera's simple melee path cuts a 3×3 square around the target instead of the item's sprite hitbox. Shaped melee projectiles (yo-yos, boomerangs and flails) cut the cells they pass; shaped ranged projectiles do not cut plants.

## Hooks in shared files

Each hook is one line:

- `Systems/GameSimulation.Items.cs` → `Cut`: `OnCutterHitFlora(point, tile, item);` (shake, and remember the axe).
- `Systems/GameSimulation.cs` → `AttackMelee`: `CutPlantsAround(target);`
- `Systems/GameSimulation.cs` → `UpdateProjectiles`: `CutPlantAt(tile, FloraCut.Projectile)` for player shots.
- `Systems/GameSimulation.WeaponStyles.cs` → `UpdateSwing`: `CutPlantsAlong(origin, Direction(swing.BladeAngle), swing.Reach);`
- `Systems/GameSimulation.WeaponStyles.cs` → `MoveStyled`: shaped melee projectiles call `CutPlantAt` on each cell they pass.
- `World/PlacementSystem.cs`: grass plants count as replaceable and are not anchors.
- `World/TileGrid.cs`: `GrassPlant = 410` and `TallGrassPlant = 411`.
- `UI/GraphicalSandbox.World.cs`: draws grass plants.
- `UI/GraphicalSandbox.cs`: the decoration loop uses `DebrisFor`.
- `UI/SpriteAtlas.cs`: `PlantSpritesFor` and `DebrisFor`.

## Performance and save size

- The flora stream adds cells × 4.5e-5 updates per tick, about 36 on a 0.8 M-cell world and about 900 on a Large 20.2 M-cell world. Each update reads only a few neighbouring cells.
- Grass plants fill cells that were already air. A save stores every cell either way, so plants add no new fields.
- The renderer draws one prop per plant. The plant sprite arrays are shared, and `DebrisFor` is cached per ground tile.
