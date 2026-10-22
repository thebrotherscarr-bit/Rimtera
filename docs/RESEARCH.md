# Static game-architecture research

This is a metadata-and-control-flow summary of the locally installed game builds. The binaries were inspected statically; neither game was launched. No decompiled source was copied into Rimtera. Names, counts, metadata tokens, and short control-flow descriptions are retained as evidence for an independently written prototype.

## RimWorld: assembly and pawn-behavior structure

- Assembly: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- Version: `1.6.9676.17735`
- MVID: `61e41735-6189-4da4-9d21-0260257b5097`
- SHA-256: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`
- Metadata inventory: 16,157 non-module type definitions, 90,840 methods, and 63,106 fields.

### Namespace inventory

`Verse.AI` is the main shared pawn decision/job infrastructure namespace in this build. Including nested and generated types, it contains 521 type definitions: 502 classes, 3 interfaces, 6 structs, and 10 enums; those types declare 1,582 fields, 2,498 methods, and 273 properties.

`Verse.AI.Group` is the separate group/lord orchestration layer: 161 type definitions, 441 fields, 909 methods, and 98 properties. Its central model includes `Lord`, `LordJob`, and `LordToil`.

The behavior structure is layered rather than contained in one “pawn AI” class:

1. **Tree composition and conditions:** `ThinkNode` and its derived nodes compose conditional, tagged, subtree, and job-giver branches. The installed `Data/Core/Defs/ThinkTreeDefs/Humanlike.xml` configures the actual humanlike tree.
2. **Candidate selection:** `ThinkNode_PrioritySorter` asks eligible children for priorities, attempts the highest-scoring candidate, and tries another candidate when a job cannot be issued. The compiled `TryIssueJobPackage` method is metadata token `0x060064E5`; its equal-priority ordering is randomized.
3. **Domain job offers:** job-giver types return candidate jobs. For example, `JobGiver_GetFood` and `JobGiver_GetRest` are in the game-specific `RimWorld` namespace, not the shared `Verse.AI` namespace.
4. **Execution and movement:** `Job`, `JobDriver`, and `Pawn_JobTracker` represent and execute work; `Pawn_PathFollower` handles movement progress. `Pawn_MindState` carries additional pawn decision state.
5. **Group orders:** `Verse.AI.Group` supplies lord/job/toil structures for coordinated behavior.

In `Humanlike.xml`, the `SatisfyingNeeds` tag wraps a priority sorter containing autofeed, food, rest, and other need candidates; joy and meditation candidates follow. A separate starvation-conditioned food branch occurs earlier than the main colonist behavior core. This is data-driven ordering around code-driven scoring and job creation.

Targeted method evidence:

| Type and method | Metadata token | Observed structure |
| --- | --- | --- |
| `Verse.AI.ThinkNode_PrioritySorter.TryIssueJobPackage` | `0x060064E5` | Priority scan, candidate attempt, invalid-result fall-through, and no-job result when exhausted. |
| `RimWorld.JobGiver_GetFood.GetPriority` | `0x06007F1E` | Checks need/category/context and returns a food candidate score only when eligible. |
| `RimWorld.JobGiver_GetRest.GetPriority` | `0x06007F4F` | Checks rest eligibility, sleep context, group-toil restrictions, and timetable before scoring. |

The installed build uses food/rest priority values of 9.5 and 8 in the relevant eligible cases. Rimtera intentionally uses its own normalized need scale and configurable scores rather than cloning those constants or the original source logic.

## Terraria: world-generation structure

- Executable inspected: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`.
- `Terraria.WorldGen.GenerateWorld` sets up generation state and delegates to `Terraria.WorldBuilding.WorldGenerator`.
- `WorldGenerator` runs enabled `GenPass` stages in sequence, using each pass's weight for overall progress. The runner records pass results, supports cancellation/pause state, and initializes a deterministic random stream for each pass.
- `WorldGen.AddPasses` assembles named stages. Terrain setup precedes later features; ore placement and liquid settling occur in separate stages.
- The liquid stage performs repeated, bounded settling work and checks for remaining liquid activity.

This supports an ordered, weighted pass interface as an architectural pattern. Rimtera's terrain profile, cave pockets, ore clustering, and liquid relaxation are separately designed implementations, not translations of Terraria methods.

## Rimtera mapping

- `AI/SurvivalBehavior.cs` separates need-conditioned candidate creation from priority selection and re-evaluates on each update.
- `World/WorldGeneration.cs` provides weighted named stages with independent deterministic seeds and bounded/cancellable fluid relaxation.
- `World/TileGrid.cs` stores row-major cells with independent tile, wall, damage, liquid kind, and normalized float liquid depth.
- `Entities/HealthComponent.cs` owns a body-part tree and injury list, advances injury severity per game-day, derives per-part integrity, and recalculates a coverage-weighted consciousness value with configurable pain impact.

These are deliberately small foundation modules, not complete replicas of either game's systems.

## Phase 2 follow-up: mining, fluids, and pawn health

### Terraria mining and item drops

The inspected executable is a managed x86 PE: assembly `Terraria` version `1.4.5.8`, MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`, SHA-256 `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`. REA's static identity result classified it as .NET Framework CIL; it did not load or execute the game.

The mining entry is `Terraria.Player.PickTile`, not `Player.PickAmmo` (which is the ammo-selection route). The selected declarations and CIL anchors place the work in this sequence:

1. `Player.PickTile` (`0x06000B43`, 597 IL bytes) calls `PickTile_DetermineDamage` (`0x06000B44`, 96 bytes), which obtains a hit-buffer slot and asks `GetPickaxeDamage` (`0x06000B49`, 1,380 bytes) for this hit's damage amount.
2. `GetPickaxeDamage` starts from pick power and uses tile-specific categories/overrides. The inspected body includes fractional or amplified increments and pick-power gates; examples of encoded gates in this build include 50, 55, 65, 100, 110, 150, 200, and 210. World depth, special-world flags, tile frames, and tile sets also affect selected cases. This is not a single generic `hardness` coefficient.
3. `PickTile` accumulates damage in `hitTile`; the tile-break threshold is 100. Below threshold it asks `WorldGen.KillTile` to process a failed hit for effects. At/above threshold it invokes the successful tile-break path (with client/server-specific handling).
4. `WorldGen.KillTile` (`0x0600133B`, 3,121 IL bytes) first applies breakability, special-object, and world-state checks. On the ordinary successful drop path it calls `KillTile_DropItems` (`0x0600133E`, 142 bytes), then clears the tile state. The drop helper obtains primary/secondary item IDs and stack counts through `KillTile_GetItemDrops` (`0x0600133F`, 8,954 bytes) and creates item entities using `Item.NewItem` with a tile-break source and tile-space-to-pixel coordinates. `noItem`, `stopDrops`, net mode, special tile entities, and tile-specific drop rules can bypass or alter that path.

`WorldGen.CheckTileBreakability` (`0x0600131A`, 580 IL bytes) is a separate structural gate: it checks neighboring/support conditions and special tile cases. It is not the pickaxe-power formula. The type-scoped REA `inspect-managed-members` result supplied member tokens, CIL body sizes, opcode summaries, and anchors; the selected C# method views came from local ILSpy 11.1.0.9782.

### Terraria fluid worklist and local relaxation

REA inventory found `Terraria.Liquid` (21 fields, 25 methods) and `Terraria.LiquidBuffer` (3 fields, 2 methods). The liquid state is a tile byte amount (0–255) plus a type, accompanied by active-cell coordinates, counts, delay/kill state, buffer counters, skip/quick-settle flags, and network-change sets. The static initializer in this build sets `maxLiquidBuffer=50,000`, `maxLiquid=25,000`, and `cycles=10`; runtime settings can lower/change limits.

The observed processing path is a bounded active-cell simulation, not a full-grid uniform CA sweep each tick:

1. `Liquid.AddWater` rejects out-of-range, empty, checked, or blocked cells. If the active list is at capacity it queues coordinates in `LiquidBuffer`; otherwise it marks the tile checked, initializes an active liquid record, and increments the active count.
2. `Liquid.UpdateLiquid` processes a slice of that active list over several cycles. `wetCounter` divides work; a quick-fall mode bypasses some skipping/delay behavior. After a cycle, inactive records are removed and buffered cells are admitted. Long-lived buffer pressure can enter a panic/quick-settle mode.
3. Each active cell's `Liquid.Update` reads its left, right, above, below, and current tiles. It first tries downward flow into a compatible, non-blocking neighbor, filling the destination toward byte maximum 255 and scheduling affected cells again.
4. Remaining liquid is redistributed horizontally across the available same-type, non-solid neighborhood. The inspected method uses neighborhoods extending from adjacent cells out to two or three tiles, averages the local byte volumes (with rounding), writes changed amounts, and re-enqueues affected cells. This repeats over future worklist passes rather than solving one global equilibrium equation.
5. Liquid-type interactions are separate: lava, honey, and shimmer checks and `LiquidCheck` handle incompatible neighboring fluids and possible merge/reaction tiles. `QuickWater` provides a generation/settling sweep from `maxY` toward `minY`, calling `SettleWaterAt` for non-empty cells.

Rimtera deliberately uses an easier-to-reason-about normalized float depth in `[0,1]`, a bounded downward transfer, and capped horizontal equalization. It does not reproduce Terraria's active array, networking, special-fluid reactions, byte quantization, or panic scheduler.

### RimWorld hediff lifecycle, severity, and body capacities

REA's managed artifact parser hit its configured metadata representation ceiling on the RimWorld DLL: `inspect-managed-members --type Verse.Hediff` reported `coverage=unavailable` with a `resource-limit` issue at the 64 MiB representation budget. This is an analysis limitation, not evidence that the types or methods are absent. The following observations are from targeted ILSpy 11.1.0.9782 type/method decompilation against the same local assembly; they are not REA Evidence.

The health structure is a health tracker plus a hediff collection and a body definition tree, with component hooks—not a flat list of stat penalties:

1. `Verse.Hediff` carries a definition, severity, age, and optional `BodyPartRecord`. Setting severity clamps against the definition's min/max and lethal boundary, detects stage changes, and notifies the pawn health tracker when relevant. The base `TickInterval` advances age and dispatches definition/stage hediff givers; it is not itself the universal severity-growth formula.
2. `Verse.Pawn_HealthTracker.HealthTickInterval` snapshots the current hediff list, calls each hediff's interval tick and post-tick hooks, removes conditions whose `ShouldRemove` is true, then advances the immunity handler. `Verse.HediffSet.AddDirect` validates/attaches a condition, runs add hooks, merges compatible conditions, and dirties health caches.
3. `Verse.HediffWithComps.PostTickInterval` gathers severity adjustments from each attached `HediffComp` and applies their sum through the parent `Hediff.Severity` property. `HediffComp_SeverityModifierBase` samples `SeverityChangePerDay` on a 200-tick interval and converts the per-day amount to that interval. `HediffComp_Immunizable` selects an immune/not-immune severity rate, with a per-instance random factor and configured factors from other hediffs. A bleeding hediff giver is a separate periodic route that adjusts severity using the set's aggregate bleed rate.
4. `Verse.BodyPartRecord` is a recursive tree node: it stores parent/children, part definition, group/tag-related data, coverage, and depth/height. `HediffSet` associates injuries/missing-part hediffs with those records. `GetPartHealth` starts from part max health, subtracts injury severity, returns zero for a missing part, and respects non-destroyable parts.
5. Abstract capacity calculation is dispatched through `PawnCapacityDef.Worker`. The base `Verse.PawnCapacityWorker` returns full capacity; concrete workers are in `RimWorld`. `PawnCapacityUtility` calculates part efficiency from missing ancestors, part health ratio, added parts, and stage offsets; tagged efficiencies aggregate relevant body records. It then applies hediff/gene capacity offsets, post-factors, set maxima, minimums, and rounding.
6. Example workers combine different structures: consciousness draws on consciousness-source parts, pain, breathing, blood pumping, and filtration; movement combines tagged limb cores/segments/digits plus pelvis/spine and related capacities; manipulation combines its limb chain and consciousness; sight aggregates tagged source parts. Thus a hand injury and a missing arm are not necessarily equivalent: the topology, missing state, and affected tags determine which worker is reduced.

`Entities/HealthComponent.cs` is an independent reduction of these architectural ideas. It advances its own injury severity rates, computes tagged-part integrity from an explicit body tree, tracks missing ancestors, and recalculates consciousness. It does not implement RimWorld's infection/immunity rules, hediff defs, body-part capacity formulas, surgery, or game ticks.

### Tooling and source scope

- The installed `rea-agents@latest` resolved to version `6.7.0`. Its available commands include `inspect-managed-artifact` and `inspect-managed-members`, but not the requested `export-workspace`; invoking that unknown command printed CLI help and did not create `docs/Decompiled_Terraria`.
- REA's read-only doctor check found no Codex MCP registration in `C:\Users\novad\.codex\config.toml`; no client configuration was modified. The supported local CLI was used for Terraria; RimWorld's size limit required a type-scoped decompiler fallback.
- The [C# Corner REA article](https://www.c-sharpcorner.com/article/rea-mcp-server-how-ai-agents-can-analyze-binaries-without-source-code) is general workflow context, not evidence about either game binary. The [RimWorld Access health guide](https://rimworldaccess.com/colony/health-and-medical/) is player-facing context, not implementation source. Binary/type/method observations above come from the installed local builds.

## Phase 3: Terraria movement, items, spawning, bosses, and events

### Evidence scope and identity

The local Terraria executable remains `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`, assembly `Terraria` 1.4.5.8, MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`, SHA-256 `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`. These notes combine type-scoped REA static member/CIL metadata with small ILSpy method views. They describe the installed build, not a timeless Terraria specification. The executable was not run, and no whole-game decompilation/export was attempted.

REA evidence IDs for the member inventories used in this phase:

| Scope | REA evidence ID |
| --- | --- |
| `Terraria.Player` | `ev_850aec9f2e1ecb93b4109dc7d81c627079b23270586332a403730ca30d686ae4` |
| `Terraria.Item` | `ev_118c6a19ceb0e5e4245adb8905e49688fc7345137efdc5a899e2d28f48a4c506` |
| `Terraria.Recipe` | `ev_65825e184c084ab926b0e9927768c27b62bd8d2985a816e10191bf5ff270eeea` |
| `Terraria.NPC` | `ev_4016c8b8fdcd2f4b180e75d7f9aeba929444e47c292bd811a6d3537cadefd726` |
| `Terraria.Main` | `ev_cb7b55c5945115025ddb920ebe0b5a7414d58516a6b2d00844fca983d11cc6c3` |
| nested `NPC.Spawner` | `ev_e621cb19ad624e781b7ffb15274bb9100c505257235243a951c566bbef6b2c93` |

“Observed” below means present in the inspected metadata/decompiled method body. Architectural simplifications and mappings into Rimtera are inferences/design choices, not original-code equivalence. This is consistent with the REA overview's recommendation to scope one feature and distinguish evidence from inference ([REA GitHub Repo Explained](https://lucijatomsic.substack.com/p/rea-reverse-engineer-anything-github-explained)).

### 1. Player movement and physics

Relevant members include `Player.Update` (`0x06000990`), `HorizontalMovement` (`0x0600094E`), `JumpMovement` (`0x0600095D`), `DashMovement` (`0x0600095E`), `WingMovement` (`0x06000965`), `GrappleMovement` (`0x06000969`), `GetGrapplingForces` (`0x0600096B`), and `DryCollision` (`0x0600097E`).

- **Tick setup and gravity:** each player update resets working values: maximum fall speed 10, default gravity 0.4, jump height 15, jump speed 5.01, original run speed 3, run acceleration 0.08, and run slowdown 0.2. Water, honey, shimmer, world depth, debuffs, gravity direction, equipment, and mounts then modify these values. On the ordinary dry path vertical velocity accumulates gravity (multiplied by gravity direction) and is clamped at the current fall-speed limit. These values are pixels per simulation update, not seconds-based physics constants.
- **Horizontal acceleration and friction:** input accelerates `velocity.X` toward the current run-speed limit using `runAcceleration`; reversing direction first applies `runSlowdown`. On ground with no direction input, horizontal speed approaches zero by `runSlowdown` each update. In air, no-input slowdown is half that amount unless a special movement mode takes over. Ground type, wings, wind, mounts, portal physics, and equipment alter this common path.
- **Jumping:** `jumpHeight` is a counter/eligibility budget, not a world-space height. While the jump counter is positive and jump remains held, vertical velocity is assigned to `-jumpSpeed * gravDir` each update and the counter is consumed. Grounded/sliding states, release state, water, mounts, and per-ability double-jump flags gate a new jump. The game therefore mixes state/counter logic with a velocity assignment rather than using a single ballistic impulse formula.
- **Dash, wings, and grapples:** dashing is a state machine keyed by dash type, `dashDelay`, and `dashTime`; activation can set a type-specific horizontal velocity (the inspected branches include 16.9, 14.5, 21.9, and 16 pixels/update times direction), while later ticks damp it and terminate the dash on its own timing/collision rules. Wing movement applies per-ability lift and drag, clamps vertical speed, and consumes `wingTime` (normally one unit/update, with selected hover states using a half-unit; special lift states alter velocity by 0.2–0.4). Grapple movement gathers valid hook anchors, averages their target positions, derives a pull vector, and caps its magnitude (11 by default, with hook-specific limits found from 12 to 16 in the selected method). Jump input can detach hooks or jump away.
- **Tile collision:** `TileCollision` delegates to the shared collision solver with the player's hitbox and platform/gravity options. `DryCollision` splits unusually large movement into substeps based on the smaller of 16 pixels and the hitbox dimensions, applies step-up/down, resolves tile/water collision, then updates slopes and conveyors. This is swept/subdivided movement with tile-specific collision semantics, not just a final-position overlap test.

Rimtera's `Entities/PawnMovement.cs` is deliberately different: seconds-based tunable acceleration, friction, gravity, impulse jump, dash timer, wing energy, optional rope-length constraint, and axis-separated grid collision. It has no equipment tables, pixel/frame constants, projectile attacks, or Terraria ability identifiers.

### 2. Item, recipe, projectile, and placement structure

The `Terraria.Item` member view exposes ordinary data fields rather than one monolithic weapon object: `damage`, `knockBack`, `useTime`, `useAnimation`, `useStyle`, `shoot`, `shootSpeed`, `useAmmo`, `ammo`, plus legacy category booleans such as `melee`, `magic`, `ranged`, `summon`, and `sentry`. For this build, do not describe those booleans as a single `DamageType` field. Item defaults are populated through a family of `SetDefaults*` methods; the type also has helper constructors such as `SetWeaponValues`, `DefaultToBow`, `DefaultToMagicWeapon`, and `DefaultToRangedWeapon`.

`Player.ItemCheck_Shoot` (`0x06000AF7`) starts from the held item's projectile, speed, damage, and knockback values; routes ammo-bearing items through `PickAmmo`; applies weapon/ability modifiers; constructs an item-with-potential-ammo entity source; and calls `Projectile.NewProjectile` on multiple specialized paths. The common call shape carries source, spawn position, velocity, projectile ID, damage, knockback, and owner. Thus weapon behavior is a pipeline, with item defaults, ammo, player modifiers, projectile defaults, and special weapon branches—not merely `Item.damage` copied to a projectile.

`Recipe` is similarly data-driven: result `createItem`, ingredient entries, recipe-group alternatives, required crafting tile, liquid requirements (water/lava/honey), biome requirements (snow/graveyard), and other condition flags. `PlayerMeetsEnvironmentConditions` (`0x06000503`) is the environment gate; recipe setup/group methods build lookup tables and available recipes.

Placement is a gated interaction pipeline:

1. `PlaceThing_Tiles` (`0x06000A4E`) checks item tile ID, reach, smart-cursor/interaction state, animation/timing, and replacement eligibility. Many special tile classes use separate exceptions.
2. Custom multi-cell objects go through `TileObject.CanPlace` (anchors, footprint, style/alternate data, and obstruction checks); successful placement uses `TileObject.Place`. Legacy tiles use `WorldGen.PlaceTile`. The placed cells are framed (for example, `WorldGen.SquareTileFrame`) and post-placement hooks/network updates follow.
3. Replacement is its own mining-like operation: `PlaceThing_TryReplacingTiles` (`0x06000A54`) selects a pickaxe, accumulates replacement hit damage to the 100 threshold, then calls `WorldGen.ReplaceTile`. `PlaceThing_ValidTileForReplacement` (`0x06000A55`) checks explicit deny/allow sets, tile solidity/platform cases, liquids, ropes, frame-important objects, existing style, expected drops, and whether replacement would work. It is not “replace any occupied cell.”
4. Wall placement (`PlaceThing_Walls`, `0x06000A4C`) requires reach and use timing plus an adjacent cardinal cell containing a tile or wall, and rejects a duplicate wall. Wall replacement adds a friendly-wall condition. This is a different support rule from foreground tile/object anchors.

Rimtera's `World/PlacementSystem.cs` implements project-level policies for footprints, solid anchors, optional wall backing, liquid policy, allow-listed replacement, reach/line-of-sight, actor overlap, wall adjacency, and dirty-region reporting. `World/TileFramingSystem.cs` consumes those regions and stores Rimtera-owned eight-neighbor masks and atlas coordinates in `TileGrid`; the original Terraria frame table remains unverified and is not copied.

### 3. NPC behavior and spawn regions

`NPC.AI` (`0x060005AC`) dispatches on `aiStyle`; the observed common families include slimes (`AI_001_Slimes`), floating eyes (`AI_002_FloatingEye`), fighters (`AI_003_Fighters`), Eater-of-Souls-style behavior (`AI_005_EaterOfSouls`), worms (`AI_006_Worms`), and town entities (`AI_007_TownEntities`). These are shared style routines with additional NPC-type branches, not one interchangeable “default AI.” Fighter examples target players and use collision/line-of-sight checks; flying-eye examples remove gravity and steer/bounce in both axes; worm behavior maintains a linked segment chain. Terraria's logic is largely direct steering and collision/state transitions in these methods, not generic A* pathfinding.

Natural spawning is isolated in nested `NPC.Spawner`. The observed route (`SpawnNPC` → `TrySpawnAnNPC` → `GetSpawnRate`/`GetSpawnArea`/`FindSpawnTile` → off-screen validation → tile/biome flags → `SpawnAnNPC`) is player-relative and staged:

- `GetSpawnArea` derives candidate and safe rectangles from screen dimensions in tiles. The ordinary candidate rectangle spans about 70% of the screen width/height on either side of the player's tile; the safe rectangle uses about 52%. Scope/zoom can enlarge both.
- `FindSpawnTile` samples up to 50 random candidate cells, rejects solid foreground and house-wall locations, sometimes scans down to ground, then requires the chosen tile to be outside the safe area and to have spawn space. Separate height/seed rules can mark sky-spawn cases.
- `CheckNotSpawningOnScreen` checks the tile against a screen-sized rectangle plus per-axis safe-range margins for every active player (with a dual-dungeon exception). A candidate can therefore be off one player's screen yet rejected because it is visible/safe for another player.
- `GetSpawnRate` changes both the random denominator (`spawnRate`) and local population cap (`maxSpawns`) using hardmode, depth, time, biome, town count, weather, and event flags. Examples in this build: ordinary hardmode multiplies the rate denominator by 0.9 and adds one slot; night multiplies it by 0.6; a surface night Blood Moon further multiplies the denominator by 0.3 and the cap by 1.8; a daytime Eclipse uses 0.2 and 1.9; underground desert uses 0.2 and 3.0. Underground/jungle/snow/dungeon/sandstorm branches add their own factors. A smaller denominator means more spawn attempts.
- The chosen tile and wall are checked again to derive spawn flags, then `SpawnAnNPC` chooses a matching regional population. Invasions take a separate branch through `NPC.Spawner.ShouldSpawnInvasionEnemies` (`0x06003EAD`) and the invasion-front/player position.

Rimtera has a bounded off-screen spawn scheduler and original slime/moth behavior in `Systems/GameSimulation.cs`. It consumes event spawn/cap modifiers, but does not reproduce Terraria's regional biome matrix, NPC population tables, or invasion branches.

### 4. Boss state machines and phase evolution

The metadata/defaults map NPC type 4 (`EyeofCthulhu`) to `aiStyle == 4`; Eater of Worlds head type 13 uses worm style 6; Retinazer/Spazmatism types 125/126 use styles 30/31; Skeletron Prime type 127 uses style 32; and Destroyer head type 134 uses style 37. Style-4 and styles 30–32/37 logic is inline within the large `NPC.AI` body, while the worm and Destroyer paths also have dedicated methods. This mapping was checked against the local defaults and `NPCID` constants.

- **Eye of Cthulhu / style 4:** the state lives in `ai[0..3]`. Its opening state steers toward a position above the target and cycles approach/dash substates; the dash vector is normalized to a selected speed and later damped. The main transition is below 50% life in ordinary mode and below 65% in expert mode. Later states add rotating/charge behavior and, in expert mode, periodic servant spawns. Additional expert thresholds at 12% and 4% change attack handling/speed. This is a real state graph with target-relative velocity math, not a generic percentage-to-animation table.
- **Eater of Worlds / style 6:** the head creates a configured number of segments; `ai` links predecessor/successor and remaining segment count, and `realLife` ties body parts to the head. Segment updates validate neighbors and transform orphaned middle segments into a head or tail where appropriate. The scoped worm method shows segment count/chain management and movement coupling; no single shared life-percentage phase gate was found there.
- **Retinazer and Spazmatism / styles 30 and 31:** each has approach/attack/coast substates and direct target-vector velocity assignment. The routines contain additional behavior thresholds at 90%, 80%, 70%, and 60% life, a principal state transition at 40%, and later checks at 75%, 50%, 25%, and 10%; exact effects vary by style/state. Projectile creation is inside those state branches and uses computed projectile velocity. These are two related but distinct controllers, not one shared “twin phase percentage” formula.
- **Skeletron Prime / style 32:** initial state creates linked arm NPCs (types 128–131); the head and arms then coordinate through AI slots and attack states. No single life-percent transition was observed in the scoped style-32 branch.
- **Destroyer / style 37:** the dedicated method connects body segments to a head and spawns probes/projectiles in state-dependent branches; worm-like linked movement is more central than grid pathfinding. The selected method did not show a common life-percentage phase gate.

`NPC.FindFrame` (`0x06000625`) is a separate, broad animation/frame-selection method. State transitions and projectile motion were easier to establish in `NPC.AI`; frame assignment should be treated as a parallel visual layer rather than conflated with the attack state machine. Rimtera should define its own animation state contract alongside, but separate from, AI state.

### 5. Time events, invasions, and town behavior

`Main.UpdateTime` (`0x06000F7C`) advances the clock and delegates to event systems; dawn/dusk entry methods perform the start rolls. The following are eligibility-conditioned odds in this exact build, not per-frame global probabilities:

- **Blood Moon at dusk** (`UpdateTime_StartNight`, `0x06000F80`): if normal events are permitted, the time is not fast-forwarded, no Eye-spawn event takes precedence, and the moon phase is not 4, a 1-in-9 roll is used (1-in-6 in the anniversary world); at least one active player must have max life above 120. That's 11.1%/16.7% only among eligible dusk checks.
- **Solar Eclipse at dawn** (`UpdateTime_StartDay`, `0x06000F81`): with normal event starts allowed, hardmode active, and at least one mechanical boss down, the start chance is 1-in-20 (5%) on the eligible dawn roll.
- **Goblin invasion start:** after a shadow orb is smashed, before goblins are defeated the eligible chance is 1-in-3; after that it is 1-in-30 pre-hardmode or 1-in-60 in hardmode. The start routine also requires an eligible player and no active invasion. Pirate invasion has a separate hardmode/altar gate and 1-in-30 or 1-in-60 roll based on progression.
- **Invasion size and “waves”:** `StartInvasion` (`0x06000F6F`) counts active players with max life at least 200. Base invasion size is `80 + 40 × eligiblePlayers`; pirate type 3 adds `40 + 20 × eligiblePlayers`; Martian type 4 uses `160 + 40 × eligiblePlayers`. `UpdateInvasion` (`0x06000F6C`) moves the invasion front toward spawn and clears the event when size reaches zero. `ReportInvasionProgress` stores a `progressWave` display value, but the inspected generic invasion loop is a quota/progress controller, not a fixed numbered-wave scheduler.
- **Spawn and town flags:** `NPC.Spawner.GetSpawnRate` consumes `bloodMoon`/`eclipse` flags to adjust spawn pressure and population cap. In `AI_007_TownEntities`, a broad hazard flag is set for night, rain, Eclipse, slime rain, or certain storms; the routine then searches for a suitable resting/home position. The scoped town routine does not test `bloodMoon` or `invasionType` directly, so those should not be described as identical town-AI triggers. This build's town combat/defense logic is also modified by difficulty and progression flags.

`Systems/EventManager.cs` is a configurable clock-boundary scheduler. It supports seeded rolls, progression/biome gates, durations, active flags, shelter behavior, and spawn/population multipliers. Its probabilities and event names are Rimtera configuration, not copied Terraria odds; the current spawn scheduler consumes `GetSpawnModifiers()`.

### Rimtera code mapping

| New module | Independent implementation boundary |
| --- | --- |
| `World/PlacementSystem.cs` | Validates footprints/support/liquid/wall backing/replacement before atomic writes and returns cells needing reframe. |
| `World/TileFramingSystem.cs` | Computes project-owned eight-neighbor connectivity masks, diagonal suppression, 16×16 atlas coordinates, and bounded dirty-region updates. |
| `Systems/CraftingSystem.cs` | Indexes original Rimtera recipes; checks ingredient groups, nearby physical stations, environment tags/liquids, guide unlocks, and applies atomic inventory exchanges. |
| `Systems/GameSimulation.cs` | Integrates place/mine/replace/craft/guide actions; adds framing updates to tile mutations and runs original actor/spawn behavior. |
| `Entities/PawnMovement.cs` | Time-step-based acceleration, friction, gravity, jump, dash, wings, rope constraint, and grid collision with configurable tuning. |
| `Systems/EventManager.cs` | Seedable dusk/dawn event rolls, eligibility gates, timed active flags, and modifiers consumable by a future spawn scheduler. |

The summaries above are observations of structure and control-flow in the local binary. Rimtera's modules are clean, reduced designs; they intentionally do not reproduce original source, content tables, art, exact tuning, netcode, or all special cases.

## Phase 4: RimWorld storyteller, rooms, and temperature (REA 6.7.0)

### Evidence scope and identity

- Tool: local `tools/rea` checkout (rea-agents 6.7.0), compiled with `npm run compile`; command `rea inspect-managed-members <assembly> --type ... --format json`. The Windows native-controls module was absent; managed static inspection does not need it.
- Local tool change: `tools/rea/src/dotnet/ManagedDecodeBudget.ts` raises `MAX_METADATA_REPRESENTATION_BYTES` from 64 MiB to 512 MiB because RimWorld's metadata exceeded the default budget. This is a local resource limit only.
- Target: `RimWorldWin64_Data\Managed\Assembly-CSharp.dll`, SHA-256 `5cf1b5be399d5b1c9c56ca72c9d35b4ecf307feacf5859d04ac5a1aa5926356a`. Raw evidence: `docs/rimworld-evidence/storyteller.json`, `docs/rimworld-evidence/rooms-temperature.json`. Coverage was reported `complete` for the selected types. Only member names, field names and type structure are used; no method bodies, constants, curves or content definitions were copied.

### Storyteller and incidents

`Storyteller` exposes `StorytellerTick`, `MakeIncidentsForInterval`, `TryFire`, `RecordIncidentFired`, an `incidentQueue`, `lastIncidentTick`, and `CheckInterval`/`IntervalsPerDay` fields: incidents are considered on a fixed interval, not every frame. `StorytellerComp` declares `MakeIntervalIncidents`, `GenerateParms`, `TrySelectRandomIncident`, `UsableIncidentsInCategory`, and `IncidentChanceFinal`; `StorytellerComp_RandomMain` adds `ChooseRandomCategory`, i.e. category first, then a weighted incident. `IncidentWorker` declares `CanFireNow`/`CanFireNowSub`, `FiredTooRecently`, `ChanceFactorNow`, and `TryExecute`/`TryExecuteWorker`. `IncidentParms` carries `points`, `forced`, `faction`, `pawnCount` and arrival options. `StorytellerUtility.DefaultThreatPointsNow` sits beside wealth/colonist point curves (`PointsPerWealthCurve`, `PointsPerColonistByWealthCurve`) and global min/max point fields.

### Regions, rooms, and temperature

`RegionDirtyer` keeps `dirtyCells` and is notified on walkability and thing changes; `RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms` regenerates regions from dirty cells and combines them into contiguous districts and rooms. `RegionMaker.FloodFillAndAddCells` builds regions by flood fill. `Room` exposes `TouchesMapEdge`, `OpenRoofCount`, `UsesOutdoorTemperature`, `IsHuge`, `PsychologicallyOutdoors`, `PushHeat`, and a `TempTracker`. `RoomTempTracker.EqualizeTemperature` uses separate wall, no-roof, thin-roof and deep/underground equalisation rates. `GenTemperature` combines seasonal/biome base temperature with `OffsetFromSunCycle`, and defines comfortable and safe temperature ranges and `PushHeat`.

### Rimtera mapping

| Rimtera module | Independent implementation boundary |
| --- | --- |
| `Systems/BiomeScanner.cs` | Counts Rimtera biome tile families in a window around the player and applies Rimtera thresholds/priority (pattern from the Phase 17 scene-scan notes). |
| `Systems/RegionMap.cs` | Dirty-cell driven flood fill of air cells into regions/rooms; enclosure and size rules are Rimtera's own. |
| `Systems/TemperatureModel.cs` | Per-room equalisation toward a biome + time-of-day outdoor temperature with Rimtera rates; heat/cool sources from stations, lava and water. |
| `Systems/Storyteller.cs` | Interval-gated incident pipeline: categories, weighted selection, per-incident cooldowns, threat points from Rimtera's own wealth/colonist/progression formula. |
