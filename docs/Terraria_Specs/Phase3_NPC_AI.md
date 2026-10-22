# Phase 3 — Terraria.NPC / AI

## 1. Equations and data variables

| Variable / relation | Observed value or rule | Units / scope |
|---|---|---|
| AI dispatch | `NPC.AI(aiStyle, npcType, ai[0..3], target, velocity, worldState)` | style/type-specific direct controllers |
| Candidate spawn extent | approximately `0.70 × screenDimension` from player tile | ordinary candidate rectangle; zoom/scope can enlarge |
| Safe extent | approximately `0.52 × screenDimension` from player tile | screen-relative exclusion rectangle |
| Random tile sampling | `sampleCount <= 50` | candidate cells per selected search |
| Spawn-rate denominator | `spawnRate' = spawnRate × contextFactors` | smaller denominator yields more attempts |
| Example context factors | hardmode `0.9` plus one cap slot; night `0.6`; surface night Blood Moon `0.3`, cap `1.8`; daytime Eclipse `0.2`, cap `1.9`; underground desert `0.2`, cap `3.0` | selected branches; other biome/event factors exist |
| Eye of Cthulhu transition | ordinary `< 0.50 life`; expert `< 0.65`; additional expert thresholds `< 0.12`, `< 0.04` | phase/state-specific |
| Twin boss thresholds | checks at `0.90`, `0.80`, `0.70`, `0.60`, principal transition `0.40`, further checks `0.75`, `0.50`, `0.25`, `0.10` | effects differ by NPC/style/state |
| Invasion size | base `80 + 40 × eligiblePlayers`; pirate adds `40 + 20 × eligiblePlayers`; Martian `160 + 40 × eligiblePlayers` | eligible player has max life `>= 200` |

## 2. Step-by-step state conditional logic array

```text
[0] NPC.AI dispatches by aiStyle and type-specific branches; common families include slime, flying eye, fighter, worm, and town routines.
[1] Fighter-style: select/validate player target; apply direct steering and collision/line-of-sight gates.
[2] Flying-eye-style: suppress ordinary gravity; steer/bounce on horizontal and vertical axes.
[3] Worm-style: maintain linked segment references; validate neighbors; repair orphaned segments into head/tail forms when allowed.
[4] SpawnNPC -> TrySpawnAnNPC -> GetSpawnRate/GetSpawnArea/FindSpawnTile -> off-screen validation -> regional flags -> SpawnAnNPC.
[5] FindSpawnTile: sample at most 50 candidates; reject solid foreground/house-wall locations; optionally scan downward; require outside safe rectangle and valid spawn space.
[6] CheckNotSpawningOnScreen: reject candidate visible/safe for any active player, with a dual-dungeon exception.
[7] Apply population and rate modifiers from depth, time, biome, town count, weather, hardmode, and event flags; select matching regional population.
[8] Invasion branch: check invasion eligibility/front/player position separately from ordinary regional spawning.
[9] Eye of Cthulhu: use ai[0..3] state slots; opening approach/dash substates; below difficulty-specific life threshold transition; later rotating/charge behavior; expert low-life branches.
[10] Eater of Worlds: link head/body/tail through AI slots and realLife; couple segment motion; no shared life-percent phase threshold observed.
[11] Retinazer/Spazmatism: separate approach/attack/coast substates; target-vector velocity; state-conditional projectile creation and life checks.
[12] Skeletron Prime: create linked arm NPCs (types 128–131); coordinate head/arms through AI slots and attack states.
[13] Destroyer: link segments to head; state-condition probes/projectiles; worm-linked movement rather than grid pathfinding.
[14] Frame selection runs as a parallel visual state layer; do not equate frame assignment with AI transition state.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / thresholds | Tick-rate / coverage |
|---|---|---|---|
| NPC AI styles | slime 1, flying-eye 2, fighter 3, Eater-of-Souls-style 5, worm 6, town 7; Eye 4; twins 30/31; Prime 32; Destroyer 37 | type-specific state gates | per NPC AI update; selected style families |
| Boss type IDs | Eye 4; Eater head 13; Retinazer 125; Spazmatism 126; Prime 127; Destroyer head 134; Prime arms 128–131 | thresholds listed above where observed | scoped IDs only; not a full NPC database |
| Spawn area | player tile, screen dimensions, candidate/safe rectangles | about `0.70×` / `0.52×` screen extents | per spawn search |
| Spawn scheduler | `spawnRate`, `maxSpawns`, biome/depth/time/town/weather/event flags | selected factors listed above | per spawn attempt/region evaluation |
| Worm links | AI predecessor/successor/count slots; `realLife` | segment-count/config specific | per segment AI update |
| Invasion | invasion type, front, remaining size, progress display | size formulas above; not a fixed numbered-wave loop | event update |
| Pathfinding | selected controllers use steering/collision/state transitions | no generic A* loop observed in scoped routines | per AI update |
| Input corpus | `docs/Decompiled_Terraria` | unavailable at generation time | based only on bounded observations in `docs/RESEARCH.md` |
