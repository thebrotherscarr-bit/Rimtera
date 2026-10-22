# Parity B: mining, building and tiles

Probes: `Systems/ParityBuildTests.cs`. They run from `CoreSelfTests` via `ParityBuildTests.Run(Check)`, and every check name starts with `parity B<n>:`.
Each probe builds an 80x48 world and clears an arena: a rock floor at row 30, with no walls or liquid. It sets cells directly, drives `GameInput` ticks at 60 Hz, and asserts behaviour, not exact numbers.
All tuning is Rimtera's own. No Terraria code, constants or tables were used.

| # | Behaviour | Status | Test | Notes |
|---|-----------|--------|------|-------|
| B1 | Every block has a pick-power requirement. A weaker drill does no damage and says "needs a stronger drill". | OK | `parity B1: a drill below a block's pick power…` | Already in place: `TileDefinition.MinimumToolPower`, checked in `Mine`. |
| B2 | Hits add up. Damage per hit comes from tool power against hardness. Partial damage heals after a few seconds. Cracks show progress. | FIXED | `parity B2: hits accumulate damage…` | Damage already added up per hit (power ÷ resistance). New: `_tileHits` records each hit. `UpdateTileDamageHealing` (run from `UpdatePhysics`) clears a block's damage after `TileDamageHealSeconds = 5` s with no hit. New: `GraphicalSandbox` draws 1–4 crack lines on damaged tiles (`DrawTileCracks`). **Superseded by `docs/TOOLS_PARITY.md` 3.8:** damage no longer heals with time; it mends as later hits age it (the hit ledger), as in the reference. |
| B3 | Tools swing at their use speed and repeat while held. Reach is a fixed range. Out of range, nothing happens. | OK | `parity B3: a held drill auto-swings…` | The UI resends the action every tick while the mouse is held. The sim gates swings with `UseCooldownTicks`. Reach is `PlayerReach` (5 tiles from the player's centre) plus `GearReachBonus`. Small difference: reach is a circle, not a box. |
| B4 | Broken blocks drop as ground items that pop out and are collected by walking near them. | FIXED | `parity B4: mined blocks pop out…` | Before, mined drops went straight into the inventory, because `InstantCollectRadius` covered the whole reach. Now `HandleBrokenTile` sets `_groundDrops`, so `CollectOrDrop` spawns a popping `GroundItem` and magnet pickup does the rest. This covers plain blocks, ores, trees, cactus, rope, herbs, pots, doors, container boxes and their contents. `ForceBreakTile` (debug/tests) still delivers straight to the inventory. Special loot (hearts, caches, pods) is unchanged. |
| B5 | Hammers remove background walls, but only walls with an exposed side. | FIXED | `parity B5: hammers remove only walls…` | `Demolish` used to remove any wall. Now `HammerWall` needs a neighbour cell with no wall, and the refunded wall item drops on the ground. |
| B5 | Hammers cycle the slope and half-block shape of blocks. | FIXED | same | Shaping and wall removal were split across two tools. Now both hammers do both: demolishers shape a block when you aim at one, and the shaping hammer knocks out a wall when you aim at empty space in front of it. |
| B5 | Axes chop trees: the whole tree above falls and drops timber and saplings. Pickaxes can't cut trunks. | FIXED | `parity B5: drills can't cut trunks…` | Before, drills could mine trunks. `PrepareMiningHit` now refuses tree parts for drills. Cutters still fell the whole tree, and timber, saplings and fruit now drop as ground items. |
| B6 | Placing blocks needs an adjacent block or wall. | FIXED | `parity B6: blocks need a neighbour or wall…` | Before, solid blocks could float in mid-air (`AnchorRule.None`). `BlockPlacementRefused` now requires a non-air neighbour or a wall behind the cell. |
| B6 | Blocks can't overlap the player or creatures. | FIXED | same | Placement already checked overlap with the player and colonists. It now also checks creatures and town NPCs. |
| B6 | Placing onto an existing block swaps it when the held block is a different type. | FIXED | same | New `TrySwapBlock`: aiming a block item at a different plain block swaps them if a drill in the pack can mine the old block. The old block drops as an item. Furniture, doors and special tiles never swap. The old right-click "replace" action still works. |
| B7 | Walls need an adjacent wall or block. | OK | `parity B7: walls need…` | Already handled by `PlacementSystem.TryPlaceWall` (`requireAdjacentBacking`). |
| B8 | Furniture needs solid support (or a wall, for wall-mounted items). Doors need 3 free cells. | OK | `parity B8: furniture needs support…` | Floor furniture uses `SolidBelow`, and wall-hung pieces use `SolidAdjacent`. Every furniture piece is one cell wide, so support always covers its full width. Tall doors check a 3-cell footprint. |
| B8 | Removing the support breaks the furniture and drops it as an item. | FIXED | same | Before, losing support did nothing. `CheckSupport` now asks `FurnitureHeld`: floor pieces need a solid block, platform or furniture under them, and wall-hung pieces need a wall or a neighbouring block. A wall removal, or a lost side or bottom block, queues a check. Furniture that fails breaks through `HandleBrokenTile` and drops its item and any contents. |
| B9 | Containers can't be mined while they hold items. | FIXED | `parity B9: full containers can't be mined…` | Before, breaking a container spilled its contents. `PrepareMiningHit` now refuses to mine or cut a container that holds items or unopened loot, with the message "Empty the … before breaking it." |
| B9 | Opening a container shows its contents. | OK | same | `Interact` returns `ContainerOpened` with the inventory. |
| B9 | An open container is range-checked and closes when you walk away. | DIFF | same | The chest panel is modal and pauses the game, so the player can't walk away while it is open. Added `GameSimulation.ContainerInReach(cell)`, which the probe checks, so a future non-pausing panel can close itself. |
| B10 | An empty bucket picks up a full liquid cell. A filled bucket pours one cell. Liquids then flow and settle. | OK | `parity B10: buckets scoop and pour…` | Already in place (`UseUtility`, `TryAddLiquid`, the active liquid simulator). |
| B11 | Sand and gravel fall when unsupported and land as blocks. | OK | `parity B11: unsupported sand falls…` | Already in place (`FallingBlock`, `Settle`). |
| B11 | Falling sand hurts creatures it lands on. | FIXED | same | Before, only the player was hurt. Falling blocks moving faster than 6 tiles/s now deal `FallingBlockDamage = 10` to creatures, with a 0.5 s per-source immunity. |
| B12 | Torches can be placed on walls or attached to blocks, and they give light. | OK | `parity B12: torches mount…` | Already in place (`PhysicsAnchorOk`, `TilePhysics.GetTileLight`). |
| B12 | Torches are knocked out by liquid. | FIXED | same | Before, torches survived in water. Liquid entering a torch cell now queues a support check, and the torch pops off as an item. Wall torches also drop when their wall or block support goes. |

Totals: 6 OK (B1, B3, B7, B10, plus the existing parts of B8 and B11), 9 behaviours FIXED (B2, B4, B5, B6, the support-loss part of B8, B9 mining, B11 creature damage, B12 liquid), 1 DIFF (B9 walk-away close, because the chest UI pauses the game).

## Existing tests updated for B4/B6 (intent kept)

- `CoreSelfTests` "selected block placement…": a wall is set behind the target, because floating blocks are no longer allowed.
- `CoreSelfTests` "deep ore needs a Ferrite drill…": counts the ore in the inventory plus on the ground.
- `ContentSelfTests` "…demolishers refund them": counts the refunded wall item in the inventory plus on the ground.
- `BatchASelfTests` tree felling and herb harvest: count items in the inventory plus on the ground.

## Not changed

- Saves do not store `_tileHits`. Damage on a partly mined block when the game is saved does not heal after loading. This is harmless: the next hit records it again.
- Unlike Terraria, the block under a chest can still be mined. The chest then falls off and spills its contents.
