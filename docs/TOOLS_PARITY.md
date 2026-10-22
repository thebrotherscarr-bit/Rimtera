# Tools parity: drills, cutters, demolishers and utility tools

This pass makes Rimtera's tools follow Terraria's tool rules.

**Evidence:** `Terraria.exe` v1.4.5.8, read with REA. Citations use the form `Type.Method @IL offset` and point into `docs/terraria-evidence/full/cil/*.il.txt`. About 270 methods were dumped for this pass with `scripts/dumpre.py` (see the evidence `README.md`). Terraria is evidence for rules and numbers only. The code, names and words are Rimtera's own.

**Builds on:**
- `docs/GEAR_DIFF_MECHANICS.md` §4: accumulate-to-100, pick hardness, the axe and hammer formulas, pick speed and box reach. This pass keeps those and corrects §4.5 (wall exposure) and §4.8 (healing).
- `docs/GEAR_DIFF_ITEMS.md` and `Systems/Content/GearCurve.cs`: tool power and use time per tier. These are unchanged.

**Where the code is:**
- `Systems/ToolParity.cs`: the pure rules.
  - Use profiles (`ToolUseProfile`) and the reach box.
  - Which tool hits which tile.
  - The demolisher's shape cycle (`NextPoundShape`, `CanPound`).
  - Wall rules (`CanSmashWall`, `IsWallTarget`).
  - The hit ledger (`HitLedger`).
- `Systems/ToolParityTargeting.cs`: smart-cursor targeting (`SmartToolTargeting.Find`).
- `Systems/ToolParityUse.cs` (partial `GameSimulation`):
  - Swing and press/hold tracking, and the reach box for the held item.
  - Ledger aging.
  - The demolisher.
  - The utilities: wire tool, wire cutter, actuator, buckets, paint tools and the shaping hammer.
- Wiring:
  - `GameSimulation.cs`: `Tick` calls `UpdateToolUse`, plus `Mine`, the `UseSelected` hook, `IsInReach` and `PlacementReachRadius`.
  - `GameSimulation.Items.cs`: `Demolish`, `HammerWall`, `AddTileHitDamage`, `PrepareMiningHit`, `Cut`, the bug net and the placement constants.
- UI:
  - `UI/SmartCursor.cs`: tools go through `SmartToolTargeting`; there is a new wall-placement spot; the reach box replaces the circle.
  - `UI/GraphicalSandbox.cs`: the cursor outline and placement preview use the reach box.
  - `UI/ActorAnimator.cs`: a tool's swing lasts its swing time.
- Tests: `Systems/ToolParitySelfTests.cs` (check names start with `tools`), hooked into `CoreSelfTests`.

**Status key:**
- **CHANGED**: Rimtera now follows the rule.
- **MATCHED**: the rule was already in place and was verified.
- **DIFF**: a deliberate difference, with the reason in §8.

**Slope numbering.** The reference stores slopes 1–4. `Tile.SmoothSlope @205` has the switch targets `[310, 294, 278, 326, 326, 259, 240]`, decoded with `scripts/sw.py`. A floor block with solid ground on its left gets slope 1 and one with solid ground on its right gets slope 2. That gives this mapping:

| Reference slope | Rimtera shape | Solid part |
|---|---|---|
| 1 | `SlopeUpLeft` | bottom-left |
| 2 | `SlopeUpRight` | bottom-right |
| 3 | `CeilingLeft` | top-left |
| 4 | `CeilingRight` | top-right |

`Tile.leftSlope` (2 or 4) and `topSlope` (1 or 2) agree.

## 1. Targeting and reach

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 1.1 | Item range check: `IsInTileInteractionRange(tileTarget, Simple, item.tileBoost)` (`Player.IsTargetTileInItemRange @0–@22`). The box is the body's edge cells ± range, where range = `min(tileRange × mult, 20) + tileBoost` (`TileReachCheckSettings.GetRanges`, `.GetTileRegion`, `..cctor @10–@25`: limit 20). | Box of 5/3 plus gear bonus. No per-tool reach and no cap. | `ToolParity.ReachBox`: base (5 + gear, 3 + gear), each capped at 20, plus the held item's boost. Used by `IsInReach` for every item use, by the smart cursor, by the cursor outline and by the placement preview. | CHANGED |
| 1.2 | Per-tool `tileBoost` (`tools.csv`): copper pickaxe, axe and hammer −1; tin to adamantite 0; chlorophyte +1; lunar picks, axes and hammers +4, lunar drills +3; wrenches and wire cutter +20; rope and chain +3; bottomless bucket +2. | None. | `ToolUseProfile.TileBoost`. The ferrite (copper-tier) pick, drill, hatchet, cutter and demolisher and the two starter drills get −1. Tier 7 tools get +4 (channel drills +3). The wire tool and wire cutter get +20. Climbing rope gets +3. | CHANGED (tier mapping: D4) |
| 1.3 | Smart cursor search region = the reach box with the item's tileBoost (`SmartCursorHelper.SmartCursorLookup @243–@449`). Steps run in order (`@625–@994`): axe, force-to-minable, shinies, solids, …, hammers, wire cutter, walls, …, empty buckets. | A circle of radius 5. | Every tool step searches only the reach box (`ToolReachBox`). | CHANGED |
| 1.4 | Force cursor (`Step_ForceCursorToAnyMinableThing`): the tile under the mouse, if in reach, active, `CanKillTile`, non-solid or solid-top, and hittable by this tool. Axe tiles need `axe`, hammer tiles need `hammer`, everything else needs `pick`. | — | `SmartToolTargeting.ForceToBreakable`. | CHANGED |
| 1.5 | Pickaxe "shinies" (`Step_Pickaxe_MineShinies`): only tiles in `TileID.Sets.SmartCursorPickaxePriorityOverride` anywhere in reach, nearest the mouse. That set is tiles 12, 665 and 639 (`Sets..cctor @976–@1004`; blob read with `scripts/blob.py`), i.e. life crystals. Ores are **not** prioritised in 1.4.5. | Every ore and crystal in a 0.35-cosine cone toward the cursor. | Only life cores, anywhere in reach, nearest the cursor (`LifeCores`). | CHANGED |
| 1.6 | Pickaxe solids (`Step_Pickaxe_MineSolids`, details below). | One ray from the body centre, 0.25-cell steps. | `FirstSolidAlongLines`: the same line set. Lines are walked cell by cell (D3). | CHANGED |
| 1.7 | Axe (`Step_Axe @75–@1300`): every axe tile in reach, normalised to the bottom of its trunk, cactus, palm or mushroom column. The foot nearest the mouse wins. | Not handled. | `TrunkFoot`. Crown cells are skipped. | CHANGED |
| 1.8 | Hammer (`Step_Hammers`), details below. | Not handled. | `WallAlongLines`, then `NearestWall`. Smart-cursor hammering never pounds. | CHANGED |
| 1.9 | Wire cutter and empty bucket (`Step_WireCutter`, `Step_EmptyBuckets`): any wire or actuator, or any liquid cell whose 3×3 holds more than 100/255 of one liquid, in reach and nearest the mouse. | Not handled. | `NearestWire`, `NearestScoop`. | CHANGED |
| 1.10 | Wall placement (`Step_Walls`): empty cells (no wall, no solid block) next to a tile or wall, nearest the mouse. | Not handled. | `SmartCursor.WallSpot`. The line-of-sight check is omitted (D3). | CHANGED |
| 1.11 | While using, the desired direction is locked (`_lockedDesiredDirection`, `UsingSmartCursorWithContinuity`). | The direction is locked. | The cursor offset from the body is locked, so the line set keeps its shape. | MATCHED |

**`Step_Pickaxe_MineSolids` in detail:**
- Direction (`@165–@307`): the direction to the mouse snaps to horizontal when |dx| > 3|dy|, and to vertical when |dy| > 3|dx|.
- Line set (`@349–@1042`):
  - Sideways: one line per body row from the leading-edge column.
  - Vertical: one line per body column from the head row, or the feet row when aiming up-diagonal.
  - Each line ends at the matching cell of a body-sized box centred on the mouse.
- Candidates (`@1057–@1506`): the first solid, non-solid-top tile along each line (`Collision.HitLine`), filtered by `CanKillTile`.
- Choice (`@1953–@2162`): the candidate nearest the body **centre** wins, if it is inside the reach box.

**`Step_Hammers` in detail:**
- Lines (`@57–@1615`): the same line set as the pickaxe. The first wall hit along each line (`Collision.HitLineWall`) is a candidate if `HitWallSubstep` passes. The candidate nearest the body centre wins.
- Fallback (`@1615–@2141`): otherwise every `HitWallSubstep` wall in reach, nearest the mouse.
- Altar cells are excluded in both cases. Both set `poundRelease = false`, so smart-cursor hammering never pounds.
- `HitWallSubstep` passes when the wall is a built (house) wall or any of the 8 cells around it has no wall. It fails when a block sits in front of the wall and all 8 cells around are solid.

## 2. Use behaviour

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 2.1 | A hit happens when `toolTime == 0 && itemAnimation > 0 && controlUseItem` (`ItemCheck_UseMiningTools @150–@325`). `StartActualUse` sets `toolTime = 1` for tools (`@0–@50`). For non-channel items, `toolTime` follows `itemTime` (`ItemCheck_OwnerOnlyCode @798–@818`). | Every use was gated by `ActionCooldownTicks`. | Unchanged in mechanism: `ActionCooldownTicks` plays the role of `itemTime`/`toolTime`. The first press hits on its first tick. Holding hits every use time. | MATCHED |
| 2.2 | Auto-reuse (`ItemCheck_AutoReuseLogic @40–@145`): with `autoReuse`, holding the button starts the next swing when the last one ends. Without it, the button must be released. All picks, axes and hammers have `autoReuse = 1` (`tools.csv`). | Any held item repeated. There was no swing state. | `UpdateToolUse` tracks the swing (`ToolSwingTicksRemaining`, `ToolSwingLength`, `ToolSwingProgress`). A swing starts on a fresh press, or while held for auto-reuse and channel tools. Releasing the button is remembered (2.4). | CHANGED |
| 2.3 | Swing length = `useAnimation` (`Player.ApplyItemAnimation`). Picks: copper 23, iron 20, gold 20, cobalt 25, mythril 25, adamantite 25, lunar 12. Axes: 30/27/26/35/35/35/25. Hammers: 33/30/28/27/27/35/30. Drills and chainsaws: 25 (`tools.csv`). | The swing drawing lasted the cooldown. | `ToolUseProfile.UseAnimation` per tier. `ActorAnimator.UseProgress` reads `ToolSwingProgress` for drills, cutters and demolishers. | CHANGED |
| 2.4 | Channel tools (drills and chainsaws, `channel = 1`, `autoReuse = 0`): `toolTime` counts down on its own and resets to `useTime − 1` (`ItemCheck_OwnerOnlyCode @820–@856`). They hit every `useTime` ticks while held. The `itemTime = useTime × pickSpeed` stored after a hit is not read, so **mining speed does not speed up drills**. | Drills were not distinguished from picks. | `ToolParity.IsChannelTool`: the seven content drills (ferrite to astral), the drone-core and claim-jumper drills, the prism bore, the void auger, the plasma saw and the chain cutter. `DrillHitTicks` uses the raw use time for them and `(int)(useTime × pickSpeed)` for picks (`ActuallyUseMiningTool @1034–@1059`). | CHANGED (colonist skill: D2) |
| 2.5 | Axe and hammer hits use `ApplyItemTime` (the plain `useTime`, `@402`, `@978`). Wall hits use `useTime / 2` (`TryHittingWall @158–@167`). Only `pick > 0` scales by `pickSpeed`. | Cutter and demolisher cooldowns were the use time; wall hits were half. | Unchanged. | MATCHED |
| 2.6 | Block and wall placement: `itemTime = (int)(useTime × tileSpeed / wallSpeed)` (`PlaceThing_Tiles_PlaceIt @225–@230`, `PlaceThing_Walls @530–@535`). Dirt block `useTime` 10, wood wall 7, rope 8 (`items.csv`). | 6 ticks per block and 4 per wall. | `BlockPlaceTicks = 10`, `WallPlaceTicks = 7`, both still divided by the build bonus. | CHANGED |

## 3. Drills (pickaxes)

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 3.1 | A pick never damages axe tiles (`Main.tileAxe`: trees 5, mushroom trees 72, cactus 80, palm 323, fallen log 488, gem and vanity trees 583–589/596/616/634, 704; read with `scripts/setinit.py`) or hammer tiles (`tileHammer`: altars 26, orbs and hearts 31, 695, 696). Its swing still sets `itemTime` (`ActuallyUseMiningTool @32–@117`, `@420`, `@1034`). | Drills refused trunks and crowns. Hearts could be drilled. | `PrepareMiningHit` refuses `ToolParity.IsCutterTile` (trunks, crowns, cactus, fallen logs) and hearts. The swing costs the drill's hit time. | CHANGED |
| 3.2 | Walls need a hammer: picks never hit walls (`TryHittingWall @116` requires `hammer > 0`). | Same. | Unchanged. | MATCHED |
| 3.3 | No-fail tiles break in one hit: `tileNoFail` → 100 (`ActuallyUseMiningTool @134–@150`, `GetPickaxeDamage @2`). These are plants, vines, herbs, torches, books, cobwebs, coin piles and similar (68 ids). Rope tiles also take 100 (`GetPickaxeDamage @1007–@1036`). | Torches, plants and rope took the soft-ground ×2 damage, so weak drills needed 2–3 hits. | `ToolParity.IsOneHitTile`: anything a swing can cut (grass plants, wild plants, herbs, pots, vines), plus rope, torches and fragile tiles. One hit. | CHANGED |
| 3.4 | `CanKillTile` (`WorldGen.CanKillTile_2`): a tile under a tree trunk, cactus, chest, altar, dresser or fallen log of another type takes 0 damage (`PickTile_DetermineDamage @34–@46`), but the swing still costs time. | No such rule. | `ToolParity.CanBreakUnder`: a cell under a trunk, cactus, fallen log, container, altar or heart takes 0 damage, with the message "Clear what stands on … first". Used by drills, cutters and demolishers. | CHANGED |
| 3.5 | Hardness, minimum pick power, accumulate to 100 (`GetPickaxeDamage`, `HitTile.AddDamage`). | `GEAR_DIFF_MECHANICS` 4.1–4.3. | Unchanged. | MATCHED |
| 3.6 | Multi-tile objects break as a whole when any cell's damage reaches 100. The hit position is normalised for a few types (`GetPickaxeDamage @1039–@1378`). | Hearts and altars clear their 2×2; doors clear their frame; containers spill their contents (`HandleBrokenTileCore`, `HandleBrokenContentTile`). | Unchanged. Rimtera furniture is otherwise one cell. | MATCHED |
| 3.7 | Drops: `KillTile` drops the tile's item (`KillTile_GetItemDrops`, `KillTile_DropItems`). | `TileDropTable` and the special handlers. Drops land on the ground. | Unchanged. | MATCHED |
| 3.8 | Damage mending (`HitTile`): each hit sets the entry's `timeToLive = 60` (`HitTileObject.AddDamage @14`). Every hit that does damage calls `HitTile.Prune` (`PickTile @582–@591`, `ActuallyUseMiningTool @388–@397`, `@964–@973`, `PickWall @134–@143`). Prune is the **only** caller that ages entries (`scripts/callers.py`). Each prune decrements every entry's ttl; once ttl < 48/36/24/12 the damage drops by 2/5/7/10 per prune. An entry clears at ttl ≤ 1, when damage < 0, or when its tile or wall is gone (`HitTile.Prune_1 @0–@243`). There are 500 slots. **Damage does not mend with time.** | Damage healed 5 s after the last hit. Wall damage also healed after 5 s. | `HitLedger` (tiles and walls, 500 slots) with the same life, decay and clearing rules. `AgeHitLedger` runs after each damaging hit and keeps the crack drawing in step. The 5-second timer is gone; `UpdateTileDamageHealing` only drops stale entries. | CHANGED |

## 4. Cutters (axes)

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 4.1 | Axes damage only `tileAxe` tiles: `tileDamage += (int)(axe × 1.2)`, ×3 on cactus (`ActuallyUseMiningTool @420–@505`). A swing at anything else applies no item time (`@506–@513` skip to `@1034`, and `pick = 0`). | Cutters hit "organic" tiles (planks, fungus, hive, coral, furniture, jungle grass). A miss still cost the cooldown. | `Cut` hits only `ToolParity.IsCutterTile` tiles. A miss costs no time and gives the message "Cutters only fell trees, cactus and logs." Damage is unchanged (`CombatTuning.CutterDamage`). | CHANGED |
| 4.2 | Felling: killing a trunk segment brings down everything above (`WorldGen.KillTile` → tree checks). Tree drops come from `KillTile_GetTreeDrops`. | The flora agent owns felling and drops. | `Cut` still calls the flora hook `OnCutterHitFlora` (shake and axe memory) and the existing break path (`HandleBrokenTile` → `FellTree`). | MATCHED (flora owns it) |
| 4.3 | Chainsaws channel (2.4). | — | The plasma saw and chain cutter are channel tools. | CHANGED |

## 5. Demolishers (hammers)

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 5.1 | Order of one use (`ActuallyUseMiningTool` → `TryPoundingTile` → `TryHittingWall`): first hammer tiles, then a pound, then walls. | First shapeable block, then the wall. | `UseDemolisher` in the same order. | CHANGED |
| 5.2 | Hammer tiles (altars, orbs and hearts) take `tileDamage += hammer` (not ×1.5) and break at 100 (`@151–@345`). A hammer below 80 or a pre-hardmode altar hurts the player (`@204–@265`). | Hearts were drilled. Demolishers could not touch altars at all, despite `CanSmashAltars` listing them. | Hearts and altars are demolisher targets. Damage per hit = the demolisher's power. Altars keep Rimtera's altar-breaker rule (D1). | CHANGED |
| 5.3 | Pounding needs a fresh press (`poundRelease`). It is set on button release (`ItemCheck_UseMiningTools @330–@340`) and cleared after every pound (`TryPoundingTile @2956`). It is also cleared when a held swing reaches a cell that is not a hammer target (`@150–@218`). Any active non-hammer **solid** tile (or platform) is poundable (`IsTilePoundable`). Pounding one spends the swing's item time and blocks the wall hit even when `CanPoundTile` refuses (`TryPoundingTile @49–@71`). | Every hit cycled the shape. | `_poundRelease` is tracked in `UpdateToolUse`. One pound per press. A refused pound still costs the use time. | CHANGED |
| 5.4 | The block cycle (`TryPoundingTile @2285–@2951`; `WorldGen.PoundTile` toggles half, `SlopeTile` sets the slope). Full → half. From half: a → b → a+2 → b+2 → full. a = 2 (b = 1) when the right side is solid (or a slope 1/3 block) and the left is not; otherwise a = 1. With a solid block above and none below, ceiling slopes come first: half → a+2 → b+2 → a → b → full. | Fixed order: full → half → up-right → up-left → ceiling-right → ceiling-left → full. | `ToolParity.NextPoundShape` follows the reference cycle with the mapping above. Default: half → `SlopeUpLeft` → `SlopeUpRight` → `CeilingLeft` → `CeilingRight` → full. | CHANGED |
| 5.5 | Platform cycle (`@227–@686`): a half platform → full. Full → a → b → full. a = 2 when there is a platform up-right or down-left, or a solid block right and none left. | Full → up-right → up-left → full. | The same rule: a stair neighbour up-right gives `SlopeUpRight` first. | CHANGED |
| 5.6 | `CanPoundTile`: not doors, traps, boulders or similar. Not when the tile above forbids sloping (chests, altars, …; `ForbidsSloping`). Not when `CanKillTile` refuses. | `TilePhysics.IsShapeable` only. | `ToolParity.CanPound` = `IsShapeable` + nothing sloping-forbidding above (containers, altars, hearts) + `CanBreakUnder`. | CHANGED |
| 5.7 | Walls: hit only if `CanPlayerSmashWall`. A built (`wallHouse`) wall always qualifies. A natural wall needs one of the 8 surrounding cells (diagonals count) with no wall or a built wall (`@0–@136`). | 4-neighbour rule for every wall. | `ToolParity.CanSmashWall`. Built walls are ids below `WallIds.FirstNatural`. | CHANGED |
| 5.8 | While held (`poundRelease == false`), the hammer hits the wall **behind** a block (`TryHittingWall @23–@89`): `canHitWalls` stays true because `TryPoundingTile` returned before clearing it. | Only empty cells' walls. | The same. After the press's one pound, holding works on the wall behind the block. | CHANGED |
| 5.9 | Wall damage `(int)(hammer × 1.5)` → 100 (`PickWall`, via `HitTile` and so the same mending as 3.8). | `GEAR_DIFF_MECHANICS` 4.5, with a 5 s heal. | Kept in the hit ledger. | CHANGED (mending) |
| 5.10 | Shaping hammer (Rimtera's building hammer). | `HammerShape` with the fixed cycle, 4-neighbour walls. | It goes through `UseDemolisher` with its own power (0 knocks a wall out in one hit). It can't touch hearts or altars. | CHANGED (D7) |

## 6. Utility tools

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 6.1 | Empty bucket (`ItemCheck_UseBuckets @470–@617`): needs liquid in the target and more than 100/255 of a cell of that liquid summed over the 3×3. It takes the whole target cell, then tops up to a full cell from same-liquid neighbours (`@1021–@1493`). Item time = `useTime` 10. | Needed ≥ 0.5 of a cell in the target. Took 1.0 from the target only. | `ScoopLiquid` follows the reference: threshold 94 of 240 units, top-up from neighbours, 10 ticks. | CHANGED |
| 6.2 | Full bucket (`@1534–@1971`): only when the target holds less than 200/255, is not a solid non-solid-top block, and holds no other liquid. Sets the cell to a full 255. | Added 1.0 wherever `TryAddLiquid` allowed. | `PourLiquid`: refuses at 188 or more of 240 units, other liquids and solid blocks. Fills the cell to full. 10 ticks. | CHANGED |
| 6.3 | Wrench (`ItemCheck_UseWiringTools @226–@413`): places one wire, consuming one wire item. It never removes wire. `useTime` 5, `tileBoost` +20. | The wire tool toggled: it removed wire that was already there. | `UseWireTool`: lays wire on the active layer only. On a wired cell it does nothing and costs no time. 5 ticks, +20 reach. With no spool it falls back to Rimtera's remote door trigger. | CHANGED |
| 6.4 | Wire cutter (`@1004–@1265`): one piece per use, actuator first, then wire 4, 3, 2, 1. `useTime` 5, +20. | Removed every wire layer and the actuator at once. | `UseWireCutter`: actuator, then layer 3 → 2 → 1, one per use. 5 ticks, +20 reach. | CHANGED |
| 6.5 | Actuator item `useTime` 10 (`items.csv` 849). | 6 ticks. | The fitting rules stay with the wiring code; a fitted actuator costs 10 ticks. | CHANGED |
| 6.6 | Paintbrush and roller (`PlaceThing_Paintbrush`, `PlaceThing_PaintRoller`, `Player.TryPainting`): one paint per use; nothing and no time if the colour is already there. `useTime` 10. | Same rules, but the item's own cooldown (6). | `UsePaintParity`, 10 ticks. | CHANGED |
| 6.7 | Scraper (`PlaceThing_PaintScraper_TryScraping @0–@230`): removes the tile's paint and coating first, and only if there are none, the wall's. One per use, 10 ticks. | Cleared tile, wall and glow at once. | Block paint and coating first, then wall paint. | CHANGED |
| 6.8 | Rope (`PlaceThing_Tiles_CheckRopeUsability`): using rope on rope extends the run downward. `tileBoost` +3, `useTime` 8. | Extension already worked (`TryPlacePhysicsTile`). No extra reach. | +3 reach through the held item's boost. | CHANGED (reach) / MATCHED (extension) |
| 6.9 | Bug net: `useTime` 25 (`items.csv` 1991). | 12. | 25. | CHANGED |
| 6.10 | Right-click features (`ItemCheck_ManageRightClickFeatures`): item-specific alternate uses (shields, bags and a list of special item ids). | — | No Rimtera tool has an alternate use. | n/a |
| 6.11 | Fishing rods and bobbers. | `GameSimulation.Fishing.cs`. | Not part of this pass. | not covered |

## 7. Smart cursor in the UI

`SmartCursor.Resolve` sends drills, cutters, demolishers, the wire cutter and the empty bucket to `SmartToolTargeting.Find`:
- It passes the locked cursor offset while the button is held.
- When a tool step finds nothing, the cell under the cursor is used.

Blocks keep Rimtera's fill-spot strategy, now inside the reach box. Walls use `WallSpot`. Melee and ranged weapons keep lock-on. The cursor outline is amber inside the held item's reach box and red outside it. The placement preview shows only inside it.

## 8. Deliberate differences

- **D1. Altar-breaker rule.** Rimtera keeps its progression gate (`PROGRESSION_DIFF.md` G4):
  - Altars need the Fracture and a tool with the altar-breaker capability. That can be a drill (the core breaker) or a demolisher.
  - The reference needs hammer ≥ 80 in hardmode and costs half your life otherwise. Rimtera only shows a message.
- **D2. Colonist mining skill.** Drill hit time is still divided by the colonist's mining skill, for picks and channel drills (`GEAR_DIFF_MECHANICS` D1). Pick speed (the `mining` tag) applies to picks only, as in the reference.
- **D3. Simplified line casting.**
  - `Collision.HitLine` steps through neighbouring cells with gravity and grapple special cases. Rimtera walks the straight line cell by cell and stops at the first solid block (or first wall).
  - Multiplayer cursor offsets (`MineSolids @1555–@1953`) are not modelled.
  - `TryFindingWallToHammer` picks among the 2×2 cells toward the cursor's sub-cell quadrant. That is not ported because the simulation gets whole cells, not a sub-cell cursor.
  - The wall-placement step skips its line-of-sight check.
  - `IsHoveringOverAnInteractableTileThatBlocksSmartCursor` is not ported.
- **D4. Reach tiers.** Rimtera tier 1 (ferrite) stands for both copper and tin. It takes copper's −1, because the starting tools are the copper-tier ones. Unique tools use their tier's boost; tier-1 uniques (prospector's pick, coral pick, …) get 0. Tier 6 tools get 0 (the adamantite and titanium boost), not chlorophyte's +1.
- **D5. Channel swing drawing.** The reference holds a channel tool's animation frame while channelling. Rimtera restarts the swing each `useAnimation` so the drill keeps moving on screen. Hit timing follows 2.4.
- **D6. Drops into the pack.** The wire cutter and actuator removal put the wire spool or actuator straight into the inventory. The reference spawns the item at the cell.
- **D7. Rimtera-only tools.**
  - The shaping hammer has power 0 and knocks a wall out in one hit.
  - The wire tool keeps its remote door trigger when no spool is carried.
  - The plating sprayer recolours hull plating.
  - Cutters no longer cut planks, fungus, hives, coral or furniture. Drills mine those, as pickaxes do in the reference.
- **D8. Melee speed and tool swings.** The reference shortens a melee tool's `itemAnimation` by melee speed. Rimtera's tool swing length ignores melee speed. Hit timing is unaffected in both.
- **D9. Paint order.** The brush and roller use the first paint in Rimtera's paint list. The reference uses the first in inventory order.
- **D10. Other damage sources.** Tantrum damage and pre-existing crack damage carry into the ledger on the next tool hit. Like tool damage, they no longer heal with time.

## 9. Tests

`Systems/ToolParitySelfTests.cs`: 27 checks, all passing. They cover:
- profiles: reach boost by tier, swing ladder, channel and auto-reuse, utility timings, block and wall times
- the reach box (pure and in game: copper-tier 4 cells, astral pick 9)
- first-press hit, hold cadence and swing length, swing ending on release, channel drill timing
- drills refused on cactus and hearts; a heart smashed by demolisher power; cutters ignoring blocks for free; cactus ×3; one-hit torches and rope; no mining under a container
- the pound cycle; the wall-on-right and ceiling-first orders; platform stairs; one pound per press and the wall behind a held block
- natural and built wall exposure (diagonals count) and the half-time wall hit
- ledger decay (pure) and in game (no time heal; mends as other hits age it)
- smart cursor: drill line, life-core priority, trunk foot, exposed wall only, nothing out of reach
- buckets: threshold, top-up and pour limit
- wire tool and wire cutter order
- the paint scraper order

**Existing checks updated for the Terraria-accurate rules.** Each has a comment pointing here.
- `StatParitySelfTests` "a hit leaves …/100 damage …": damage now stays when left alone (3.8).
- `StatParitySelfTests` "the drill reaches exactly 5 cells …": the copper-tier drill's −1 boost is added (1.2).
- `ParityBuildTests` B2: partial damage stays put (3.8).
- `ParityBuildTests` B5:
  - Exposure is tested on natural rock walls (built walls can be hit from anywhere, 5.7).
  - A surrounded plank wall is knocked out and drops its item.
- `BatchASelfTests` shaping-hammer and save checks: with ground on both sides the first slope is `SlopeUpLeft` (5.4).
- `CoreSelfTests` "integrated tool-gated replacement": waits out the 10-tick block placement (2.6).
