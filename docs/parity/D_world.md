# Parity audit D: world systems, town NPCs and inventory

This audit covers the contract in `docs/MECHANICS_PARITY.md` section D. The probes live in `Systems/ParityWorldTests.cs` and run from `CoreSelfTests` (`dotnet run -- --self-test`). Each probe checks how the system behaves. The numbers are Rimtera's own tuning, not copied constants.

Status key: **OK** means it already behaved correctly. **FIXED** means it was changed in this audit. **DIFF** means it intentionally differs, and the reason is given.

| # | Behaviour | Status | Probe (check text prefix) | Notes |
|---|-----------|--------|---------------------------|-------|
| D1 | Day is longer than night | FIXED | `D1 a full cycle has a longer day…` | Day and night were both 12 clock hours. The clock now runs `NightClockPace = 1.5×` faster at night (`GameSimulation.ClockPace`, applied in `AdvanceClock`). A cycle is now about 60% day and 40% night in real time. Clock-hour rules (dusk 18:00, dawn 06:00) are unchanged. Needs, statuses and regen still use unscaled game hours. |
| D1 | Moon phase advances each night | FIXED | `…moon phase advances once per night` | Added `MoonPhase` (8 phases; it changes at dawn so it stays fixed for a whole night) and `MoonPhaseName`. It is derived from the clock, so saves need no new field. |
| D1 | Blood-moon-like event has a small chance at nightfall | OK | `D1 the Rust Moon…` | The Rust Moon rolls 12% at dusk once eligible. Rolls are deterministic per seed and day. |
| D1 | Sleeping speeds time | OK | `D1 sleeping in a bed…` | Time runs ×4 in a bed at night. |
| D2 | Each NPC needs its own unlock condition | OK | `D2 each NPC needs its own unlock condition` | |
| D2 | NPC needs a free valid house | OK | `D2 unlocked NPCs move into free valid houses…` | |
| D2 | Houses checked during the day, one NPC at a time | FIXED | `…one per check`, `D2 houses are checked during the day only…` | Arrivals and homeless move-ins used to happen at any hour, and several freed survivors could move in during one check. Now nobody moves in at night, and at most one NPC moves in per hourly check. |
| D2 | A dead town NPC respawns after a delay if a house is valid | FIXED | `D2 hostiles hurt residents…` | Town NPCs had no health and could not die. They now have 160 HP, take contact damage from hostile creatures (0.6 s hurt cooldown) and regenerate 2 HP/s. On death they are removed and a persisted flag `npc.<kind>.respawn_hour` holds them back for `TownRespawnHours = 10` game hours. After that they come back through the normal daytime arrival into any free valid house. API: `HurtTownNpc`, `TownNpcRespawnHour`. |
| D3 | Housing reports why a room fails | FIXED | `D3 housing reports why a room fails…` | Reasons already existed for enclosure, size, walls, light, door, table and chair. Added a separate "too large (n/400 cells)" reason; this case used to be reported as "not enclosed". |
| D3 | Door or platform entrance | FIXED | same probe (platform hatch) | Platforms and doors (open or shut) are now room boundaries in `RegionMap` (`Bars`). A platform hatch therefore no longer leaks the room, and an open door no longer merges two rooms. Housing accepts a door, an open door or a platform next to the room as its entrance. |
| D3 | An NPC claims a room; two can't share | OK | `D3 a resident claims its room…` | |
| D4 | Happiness changes shop prices | OK | `D4 neighbours and crowding…` | |
| D5 | Pickups stack onto existing stacks, then the hotbar, then the main inventory | OK | `D5 pickups top up existing stacks first…` | `Inventory.Add` fills matching stacks first, then empty slots in order (hotbar slots 0–9 come first). |
| D5 | Per-kind stack maximums; credits combine | OK, with a small fix | `D5 stacks cap at a per-kind maximum…` | `GetAddableCount` now only counts stacks with the same calibration, matching what `Add` actually does. It used to over-report room, so ground items could be attracted into a full inventory. Credits are one denomination (stack 99 999) and merge into one stack. |
| D5 | Favourites aren't sorted or deposited | OK (pre-existing) | — | Covered by existing tests. |
| D6 | Recipe needs materials and every required station in range | OK | `D6 a recipe crafts only…` | |
| D6 | Crafting takes from chests in range | DIFF | — | Rimtera crafts from the player inventory only. This is intentional and noted in the contract. |
| D6 | Output goes to the cursor or stacks into the inventory | DIFF (partial) | same probe | Output stacks into the inventory atomically. There is no cursor-held output; that is a UI concern and is not changed here. |
| D7 | Invasions start on triggers; progress counts kills | OK | `D7 an invasion advances wave by wave…` | |
| D7 | Events end on waves or timer | OK | same, plus `D7 a timed event ends…` | |
| D7 | Can't sleep through an event | FIXED | `D7 you cannot sleep through an event…` | `TrySleep` refuses while a night, day or invasion event runs (`WorldEventSystem.BlocksSleep`). An event that starts while you sleep wakes you. Weather, calm and party events don't block sleep. |
| D8 | Rain starts and stops randomly; storms and wind | OK | `D8 rain starts and stops…` | |
| D8 | Rain reduces surface lighting | FIXED (sim side); UI hookup pending | `D8 rain and storms reduce surface light` | Added `GameSimulation.SkyLightLevel` (0–1). It folds together time of day, moon phase (full moon is brighter), cloud, rain, snow, dust and storms. The renderer (`UI/GraphicalSandbox.cs`, the `_lighting.Compute(..., 1f - night * 0.68f, ...)` call) still uses a clock-only curve and should pass `game.SkyLightLevel` instead. UI files were outside this audit's ownership. |
| D9 | Bait consumed with a chance based on bait power | OK | `D9 bait is consumed less often…` | Pulled out into `GameSimulation.BaitConsumeChance(power)` so it can be tested. The curve is unchanged. |
| D9 | Fishing power = rod + bait + bonuses | OK | same probe | |
| D9 | Catch depends on biome, depth and liquid | OK | `D9 the catch depends on…` | |
| D9 | Daily quest fish | OK | `D9 a daily quest fish rotates…` | |
| D10 | Saving | OK | `D10 a save round-trip keeps the clock…` | Autosave runs every 90 s and on exit in the UI. World and character files are separate (pre-existing). |

## Files touched
- `Systems/GameSimulation.World.cs`: night clock pace, moon phase, sky light level, sleep blocked or interrupted by events.
- `Systems/GameSimulation.cs`: one line, `AdvanceClock(elapsedHours * ClockPace)`.
- `Systems/WorldEvents.cs`: `BlocksSleep`.
- `Systems/GameSimulation.Town.cs`: arrivals by day only and one at a time; NPC health, contact damage, death and respawn delay.
- `Systems/TownNpcs.cs`: NPC hit points; housing reasons (too large, door or platform entrance).
- `Systems/RegionMap.cs`: platforms and doors are room boundaries.
- `Systems/ItemSystem.cs` (Inventory only): `GetAddableCount` respects calibration.
- `Systems/GameSimulation.Fishing.cs`: `BaitConsumeChance`.
- `Systems/ParityWorldTests.cs` (new) and one line in `Systems/CoreSelfTests.cs`.
