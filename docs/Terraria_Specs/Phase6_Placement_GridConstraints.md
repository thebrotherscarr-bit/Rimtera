# Player placement and WorldGen — placement/grid constraints

## 1. Equations and data variables

| Field / relation | Available observation | Exactness / scope |
|---|---|---|
| `Player.PlaceThing_Tiles` | Checks tile item, reach, smart-cursor/interaction state, animation/timing, and replacement eligibility | Metadata token `0x06000A4E`; detailed distance and LOS equations not captured |
| Multi-cell object gate | `TileObject.CanPlace` evaluates footprint, anchors, style/alternate data, and obstructions | Exact per-object anchor table unavailable |
| Legacy tile placement | `WorldGen.PlaceTile`; successful placement is framed and followed by hooks/network updates | Structure only |
| Wall placement | Reach/use timing; cardinal adjacent tile-or-wall backing; duplicate wall rejected | Exact reach formula unavailable |
| Wall replacement | Adds a friendly-wall condition | Exact IDs/flag table unavailable |
| Replacement hit threshold | Accumulated replacement damage `>= 100` enters `WorldGen.ReplaceTile` path | Observed in existing scoped report; not an instant swap |
| Replacement validity | Explicit allow/deny sets; solidity/platform, liquid, rope, frame-important, style, expected-drop, and replacement-feasibility checks | Full set contents unavailable |
| Rimtera reach proposal (not Terraria data) | `distanceSquared = dx*dx + dy*dy`; require `distanceSquared <= reachTiles*reachTiles` | Project option; no original numeric radius asserted |
| Rimtera LOS proposal (not Terraria data) | Grid ray traversal rejects an intervening opaque/solid cell before target | Project option; exact Terraria LOS routine not captured |

## 2. Step-by-step state conditional logic array

```text
[OBSERVED 0] Validate selected placeable item, reach, smart-cursor/interaction state, timing, and replacement eligibility.
[OBSERVED 1] For custom multi-cell objects, evaluate footprint, anchors, style/alternate, and obstruction rules through TileObject.CanPlace.
[OBSERVED 2] Commit custom objects through TileObject.Place; legacy tiles use WorldGen.PlaceTile.
[OBSERVED 3] Frame placed cells and run post-placement/network hooks.
[OBSERVED 4] Replacement is a separate mining-like path: select pickaxe, accumulate replacement-hit damage, require threshold 100, then call WorldGen.ReplaceTile.
[OBSERVED 5] Validate explicit replacement allow/deny and tile-specific constraints; do not treat every occupied tile as replaceable.
[OBSERVED 6] Wall placement checks reach/timing, cardinal backing, duplicate-wall rejection; wall replacement additionally checks friendly-wall state.
[UNKNOWN 7] Exact distance limits, line-of-sight formula, and all item/tile numeric tables are not captured in available evidence.
[RIMTERA DESIGN 8] Validate every footprint cell, LOS/reach, support, wall/liquid policy, and explicit replacement allow-list before any write.
[RIMTERA DESIGN 9] Commit atomically; report the changed cells and neighboring frame-dirty region.
[RIMTERA DESIGN 10] Keep replacement damage/drop policy explicit; do not implement a free instant swap unless a separate project rule authorizes it.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / numeric data | Coverage |
|---|---|---|---|
| `PlaceThing_Tiles` | item tile ID, target, reach, smart cursor, timing, replacement state | Exact reach values unavailable | Structural route; token `0x06000A4E` |
| `TileObject` | footprint, anchors, style/alternate, obstruction | Per-object tables unavailable | Conditional placement route |
| `WorldGen.PlaceTile` | legacy tile ID and target cell | Tile-specific constraints unavailable | Call boundary observed |
| Tile replacement | pickaxe, current/expected tile, replacement allow/deny sets | Hit threshold `100` | Mining-like rather than instant |
| Wall placement | wall ID, cardinal backing, current wall, friendly-wall condition | Distance/timing values unavailable | Structural checks observed |
| Input corpus | `C:\Users\novad\Desktop\Rimtera\docs\Decompiled_Terraria` | `NOT_FOUND` at generation time | Existing `docs\RESEARCH.md` only; no source-body extraction |
