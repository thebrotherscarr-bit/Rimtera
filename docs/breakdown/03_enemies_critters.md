# 03 · Enemies and critters: Rimtera compared with Terraria

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open, with the milestone `docs/PLAN.md` now plans it for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

This is a read-only audit. No game code was changed. Terraria (v1.4.5.8) is used only as evidence of behaviour. Rimtera names, numbers, code and art stay original.

**Sources**
- **Terraria CIL:**
  - `docs/terraria-evidence/full/cil/Spawner__*.il.txt`, which includes `GetSpawnRate`, `GetSpawnArea` and `SpawnNPC_*`.
  - `Terraria.NPC__SetDefaults.il.txt`.
  - New dumps made for this audit from `members-Terraria.NPC.json` and `members-Terraria.Player.json`: `NPC::CheckActive`, `CountKillForBannersAndDropThem`, `CatchNPC`, `GetImmuneTime`, `Player::GetBannerBuffEffect` and `ItemCheck_CatchCritters`. They were made with a copy of `scripts/ilall.py` pointed at the per-type JSON, and live in the scratchpad only.
  - The list of `NPC::AI_*` methods.
  - "(knowledge)" marks a Terraria fact that was not re-checked in the CIL.
- **Rimtera:** I read the code, ran `--entity-sprites`, and ran a private probe (`ProbeEnemies.cs`, a scratch build that is not in the repo). Probe results are tagged **[probe]**.

**Probe results [probe]**
- **Roster.** 465 `EnemyKind`, of which 33 are bosses, 102 passive and 97 catchable. There are 330 hostile non-boss kinds:
  - by family: Default 230, Caster 21, Hopper 21, Worm 19, DivingFlyer 17, Mimic 8, LeapingHopper 7, SkitteringHopper 7.
  - by type: 100 fly, 12 are stationary, 19 swim (17 of the swimmers carry `Flying = true`).
  - **0 have `Defense` > 0.**
- **Walls.** A walker clears a 1-tile step. It cannot cross a 2-, 3- or 5-tile wall, and stands pressed against it for 12 s.
- **Platforms.** A walker on a platform above the player never comes down.
- **Doors.** A closed door blocks walkers.
- **Lava.** Standing in lava for 3 s does no damage (70 → 70 HP).
- **Walls and fliers.** A "void wisp" (ghost analogue) cannot pass a wall.
- **Idle walkers.** A walker more than 22 tiles from the player does not move at all over 10 s.
- **Swimmers.** In a 10-deep pool, silver fish, pond minnows and tide leeches sink to the bed and stay there. The shark (a "flying" swimmer) chases the player and ends up pressed into a wall.
- **Spawns.** On a 220×70 grassland world, 2000 attempts gave:
  - by day: 220 hostile and 86 passive spawns;
  - by night: 541 hostile and 31 passive spawns;
  - the night population peaked at 6 live hostiles.
- **Sprites.** All 462 creature sprites exist with base, idle, move and attack frames, so 0 are missing. Three sprite pairs are shared: CaveBat/SkyHawk, CaveMoth/GeigerBug and Wraith/GlowStalker.
- **Promised ranged attacks.** 12 hostile names promise a ranged attack that does not exist (contact-only):
  - Spitting barnacle, Refractor turret, Spitting orchid
  - Raider marksman, Scavenger sniper, Pirate gunner, Boarding cannon
  - Probe gunner, Orchid moth, Puff bomber, Crypt marksman, Brigade lobber
- **No item drops.** 48 event units have no registry item drops (credits only).

---

## Summary

**Counts (93 features):** ✅ 41 · ◐ 26 · ❌ 16 · ⚠ 6 · ➕ 4

**Top 10 issues by player impact**
1. **Non-boss enemies never shoot (A8, A9).** Only the 21 Caster-family kinds, bosses and dart traps create hostile projectiles. That leaves the twelve "marksman/gunner/spitter/turret/cannon" units, and the 12 stationary kinds, harmless unless the player touches them. This **blocks the intended play** of the ocean, canopy, crystal, pirate and dungeon rosters.
2. **Ground pathing is trivially cheesed (B2, B3, B8).** A 2-tile wall stops every walker, walkers never drop through platforms, and a walker out of alert range stands frozen. In Terraria, fighters jump several tiles, turn around when stuck and wander all the time.
3. **Every creature has 0 defense (D2).** The `EnemyArchetype.Defense` field exists but is never set. That makes armour penetration and defense-based weapon tiering meaningless against creatures. Terraria's `SetDefaults` writes `defense` 635 times (zombie 6, slime 2, demon eye 2).
4. **There is no swimming AI (A10).** Fish and tadpoles sink to the bed. Sharks and jellies are fliers that can leave the water. Walkers ignore liquid drag.
5. **Contact hitbox (D5).** Contact damage uses centre-to-centre distance below 1.15 tiles, not box overlap, and worm segments test 0.9 tiles from the player's centre. Large bodies have a harmless rim, and a worm can pass the player's legs without biting.
6. **Statue spawns are a farm (C16).** Wired statues release creatures that drop full items, credits and banner kill counts. Terraria's statue spawns drop nothing.
7. **The side spawn stream ignores the off switches (C15).** `UpdateFamilySpawns` ignores creative spawn-rate 0 and events' `NoHostiles`. It also seeds a 260-HP Tunnel wyrm underground before the Fracture.
8. **There are no spawn-rate controls (C8).** There is no water-candle, battle-potion, calming-potion, peace-candle or sunflower analogue. Terraria's `GetSpawnRate` reads `Player.enemySpawns`, `calmed` and `sunflower`.
9. **Kills drop no heart or charge pickups (G3).** Terraria's `NPCLoot_DropHeals` keeps the fight loop sustaining.
10. **AI families are missing (A12–A15).** There are no ghosts that phase through walls, no chargers, no wall-climbing spiders and no split-on-death or summoner enemies. A large share of rosters run the one Default walker or flier.

---

## Feature tables

Legend: ✅ matches · ◐ partial · ❌ missing · ⚠ broken or exploitable · ➕ Rimtera-only (deliberate). Line numbers are from today's tree.

### A. AI families (compared with Terraria aiStyles)
Rimtera's dispatcher is `GameSimulation.UpdateEnemies` (`Systems/GameSimulation.cs:1610`). It calls `StepAiFamily` (`Systems/EnemyAiFamilies.cs:24`) for families other than Default. The family enum is at `Entities/Actors.cs:369`.

Terraria has about 127 aiStyles; named helpers include `AI_001_Slimes`, `AI_002_FloatingEye`, `AI_003_Fighters` (20,049 instructions), `AI_005_EaterOfSouls`, `AI_006_Worms`, `AI_026_Unicorns`, `AI_065_Butterflies`, `AI_107_ImprovedWalkers`, `AI_108_DivingFlyer`, `AI_114_Dragonflies`, `AI_116_WaterStriders`, `AI_118_Seahorses` and more, plus the inline cases in `NPC::AI` (113,899 instructions).

| ID | Feature | Terraria | Rimtera (code) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| A1 | Fighter / walker (Default ground) | aiStyle 3: always walks toward or away from the target. Jumps high when blocked, turns around when stuck, some open doors. | It walks only within 22 tiles (`alertRadius`, GameSimulation.cs:1630), jumps only when a 1-tile step is ahead (`IsGroundBlockedAhead` :1601) and never turns around. [probe] | ◐ | ❌ EN-2 → 0.4.0 |
| A2 | Slime hopper | aiStyle 1: crouch, then hop. | `MossSlime`, `RockMite` and the `Hops` kinds jump every 1.1 s (:1655). There are three hopper families (`StepHopper` EnemyAiFamilies.cs:258). | ✅ | ✅ as audited |
| A3 | Default flier | aiStyles 2 (eye bounce), 14 (bat erratic), 5 (eater) and 44: several distinct movement models. | One model, `MoveFlyingEnemy` (:1812): it lerps toward the target and bounces off tiles at 35%. Eyes, bats, harpies and drones all move the same way. | ◐ | ❌ EN-13 → 0.4.0 |
| A4 | Diving flier | `AI_108_DivingFlyer` | `StepDiver` (EnemyAiFamilies.cs:195): hover above the target, dive, pull up. 17 kinds. | ✅ | ✅ as audited |
| A5 | Worm | `AI_006_Worms`: segmented, burrows. | `StepWorm` (:50): the head steers underground and arcs in the air under gravity, segments trail it, and projectiles hit the segments (`EnemyProjectileDistance` GameSimulation.Player.cs:1166). 19 kinds. | ✅ | ✅ as audited |
| A6 | Caster | aiStyle 8: teleports, then fires a slow bolt. | `StepCaster` (:109): teleports after 3.8 s, then fires 3 homing bolts. 21 kinds. | ✅ | ✅ as audited |
| A7 | Mimic | aiStyle 25: disguised until it is touched or hit. | `StepMimic` (:232): unmasks within 2.6 tiles or when hurt, then leaping hops. 8 kinds. | ✅ | ✅ as audited |
| A8 | Stationary attacker (antlion, man eater, turret) | aiStyles 19 and 13: they shoot or lunge from a fixed spot. | 12 `Stationary` kinds never move and have no attack, so they hurt only on contact (`ContentCreatures.cs:162`). | ❌ | ❌ EN-1 → 0.4.0 |
| A9 | Ranged non-boss (archer, gunner, spitter) | Many fighter, flier and turret variants shoot (skeleton archer, goblin archer, hornet, snow balla, pirate deadeye, sniper…). | No such family. Only casters, bosses (`FireBossVolley` :1789) and dart traps make `ProjectileTeam.Enemy` shots. [probe] found 12 names that promise ranged attacks. | ❌ | ❌ EN-1 → 0.4.0 |
| A10 | Swimmer | aiStyles 16 (fish), 18 (jellyfish) and `AI_118_Seahorses`: confined to water, flop on land. | No swim AI. 17 of 19 swimmers are flagged `Flying` and use `MoveFlyingEnemy` with no liquid check. Passive "flying" fish drift down to the bed (`UpdatePassiveCreature` :1722). Non-flying swimmers walk the bed. [probe] | ⚠ | ❌ EN-3 → 0.4.0 |
| A11 | Critter AI | aiStyles 7, 24, 64–68, `AI_112_FairyCritter`, `AI_114`, `AI_115`, `AI_116`: birds land and take off, fireflies glow, snails crawl walls, ducks swim. | Two models in `UpdatePassiveCreature`: walk in 3-second legs, or flier drift, plus a 4 s flee when hurt. | ◐ | ❌ EN-16 → 0.4.0 |
| A12 | Phasing ghost or skull | aiStyles 22 and 10: `noTileCollide`, they drift through walls. | Every non-boss flier collides with tiles, and the void wisp cannot pass a wall [probe]. There is no per-archetype "phases" flag. | ❌ | ❌ EN-13 → 0.4.0 |
| A13 | Charger | `AI_026_Unicorns`: run up, then gallop at speed. | There is no charge family. The Shard strider and the hounds are Default walkers. | ❌ | ❌ EN-13 → 0.4.0 |
| A14 | Wall climber (spiders) | aiStyle 40 (wall-crawling spiders). | Burrow spider, Silk spider-mite and Venom recluse are Default walkers. | ❌ | ❌ EN-13 → 0.4.0 |
| A15 | Split-on-death and summoners | Mother slime and corrupt slime split. Brood and hive enemies spawn minions. | Nothing spawns on death, and the Mite broodmother does not breed. | ❌ | ❌ EN-13 → 0.4.0 |
| A16 | Boss controller (bosses belong to another area) | Unique AI per boss. | One shared approach, telegraph and volley loop (`UpdateBossController` :1751), with phase 2 under a threshold. | ◐ | ❌ BO-1 → 0.4.0 |
| A17 | Aggro on colonists and residents | n/a | `AggroGoal`: crew and town NPCs draw attacks, with distance penalties (GameSimulation.PlayerSystems.cs:30). | ➕ | ➕ kept |

### B. Pathing through tiles

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| B1 | Step-up and small ledges | Walk up slopes and 1-tile steps. | A jump triggers on a blocked step; jump impulse 8.5 against gravity 24 gives about 1.5 tiles (`_enemyMovement`, GameSimulation.cs:55). | ✅ | ✅ as audited |
| B2 | Clearing walls | Fighters jump several tiles when blocked, then turn and give up (knowledge). | It cannot cross 2 tiles and stays stuck forever [probe]. A 2-tall dirt wall makes the player fully safe. | ⚠ | ❌ EN-2 → 0.4.0 |
| B3 | Platforms | Fighters drop through platforms when the target is below (knowledge). | `MovementInput.Vertical` is never set for creatures, so they never drop [probe]. | ❌ | ❌ EN-2 → 0.4.0 |
| B4 | Closed doors | Normal enemies cannot open them. | Closed doors are solid [probe]. | ✅ | ✅ as audited |
| B5 | Door-breaking on blood moon and invasions | Zombies and goblins bash doors open (knowledge). | None. | ❌ | ❌ unchanged since the audit |
| B6 | Liquids | Water slows NPCs, swimmers stay inside it, some enemies are lava-immune. | `PawnMovementSystem` has no liquid term for creatures, so walkers move at full speed underwater. | ◐ | ❌ EN-3 → 0.4.0 |
| B7 | Lava damage to creatures | NPCs take lava damage unless `lavaImmune` (knowledge). | None: 70 → 70 HP after 3 s in lava [probe]. | ❌ | ✅ EN-5 0.1.15 |
| B8 | Idle wandering | Fighters keep walking, and turn at walls or ledges. | Speed is 0 outside the 22-tile alert radius (`horizontal = 0`, :1632), so a creature stands frozen [probe]. Flyers wiggle in place. | ❌ | ❌ EN-2 → 0.4.0 |
| B9 | Fliers and tiles | Most fliers collide; ghosts do not. | They collide and bounce back (`MoveFlyingEnemy`). Huge flying bosses clip through. | ✅ | ✅ as audited |

### C. Spawning (`Systems/GameSimulation.Spawning.cs`)

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| C1 | Spawn tick and rate | `defaultSpawnRate` (600) and `defaultMaxSpawns` (5) (knowledge for the numbers). A 1-in-spawnRate roll each tick (`GetSpawnRate`, `SpawnNPC_*`). | An attempt every 0.65 s with 16% × factors, at most 85% (`UpdateSpawning` :36). Spawns come faster on average than Terraria at day. | ◐ | ❌ EN-8 → 0.4.0 |
| C2 | Population cap | maxSpawns, weighted by `npcSlots`. | Base 4 × event, content and biome factors, clamped to 1–20. Critters count toward it. The night peak was 6 [probe]. | ✅ | ✅ as audited |
| C3 | Time and depth factors | `GetSpawnRate` IL: night ×0.6 rate and ×1.3 cap; underground (`rockLayer`) ×0.4–0.5 and ×1.7–1.9; blood moon ×0.3 and ×1.8; eclipse ×0.2 and ×1.9; hardmode ×0.9 and +1. | `CombatTuning.SpawnFactors` (CombatModel.cs:726): night 1.7× rate and 1.5× cap; underground 1.45× and 1.35×; blight 1.3× and 1.25×. The underground boost is weaker than Terraria's 2–2.5×. | ◐ | ❌ EN-8 → 0.4.0 |
| C4 | Spawn position | Random tiles in a 0.7-screen box outside a 0.52-screen safe box, in **both** axes (`GetSpawnArea` IL: `sWidth/16×0.7`, `×0.52`). | Left or right only, 22–40 tiles out, on ground within ±10 rows of the player's depth (`TryFindSpawnGround` GameSimulation.cs:2203). Nothing spawns above or below. Cave spawns need a roof. | ◐ | ❌ EN-8 → 0.4.0 |
| C5 | Safe distance | `safeRangeX` is 0.52 of the screen (about 62 tiles at 1080p). | `SafeSpawnHalfWidth` is 22 tiles against a view half-width of 18 (GameSimulation.cs:46). | ✅ | ✅ as audited |
| C6 | Town calming | Town NPCs nearby cut spawns; three or more nearly stop them (knowledge). | ×1/(1+0.6n), and ×0.12 at three or more (`SpawnFactors`). `TownNpcsCalmingSpawns` counts within 45 tiles. | ✅ | ✅ as audited |
| C7 | Spawns while a boss lives | Only a few special cases, such as the Wall of Flesh (`Main.wofNPCIndex` in `GetSpawnRate`). | Rate ×0.35 and cap ×0.6 while any boss is alive. | ◐ | ❌ EN-8 → 0.4.0 |
| C8 | Spawn-rate items and buffs | `Player.enemySpawns` (battle potion, water candle: ×0.5 rate), `calmed` (×1.65), `sunflower` (×1.2), plus invisibility, peace candle and shadow candle (IL reads all three fields). | None. The only knobs are the creative-mode slider (`Creative.cs:84`) and the "Hardened" seed. | ❌ | ❌ EN-8 → 0.4.0 |
| C9 | Ramp-down as the population fills | Rate ×0.6/0.7/0.8/0.9 while `nearbyActiveNPCs` is below 20/40/60/80% of max (`GetSpawnRate` IL 1692–1825). | Hard cap only. | ❌ | ❌ EN-8 → 0.4.0 |
| C10 | Biome rosters | Zone flags pick pools. | `BiomeSpawnTables` holds weighted day and night tables for 14 zones. Context bands and content-fill cover grassland and caves (`BiomeSpawnTables.cs:13`, `ContentFill` :291). | ✅ | ✅ as audited |
| C11 | Hardmode roster share | Hardmode pools replace or extend the old ones. | 55% of rolls go to `HardTables` after the Fracture (`HardShare`). | ➕ | ➕ kept |
| C12 | Water spawns | Sharks, jellyfish and piranhas spawn in water. | `IsSwimmer` with `TryFindSwimSpot` needs at least 3 cells of water (Spawning.cs:150). | ✅ | ✅ as audited |
| C13 | Event and weather rosters | Rain, blood moon, invasions and so on override pools. | `_worldEvents.PickSpawn`, `HardmodeEventCreature`, `CoverageSpawn` (GameSimulation.CreatureCoverage.cs:58). | ✅ | ✅ as audited |
| C14 | Dungeon roster | ZoneDungeon pool. | Rolled while the player stands on dungeon wall (`PickDungeon`). | ✅ | ✅ as audited |
| C15 | Side "family" spawn stream | n/a | `UpdateFamilySpawns` (EnemyAiFamilies.cs:300): every 45 s, at most 2 alive. **It ignores** `CreativeSpawnRate = 0`, `NoHostiles` events and the biome (except core and ocean). It spawns the 260-HP Tunnel wyrm and the Rift caster before the Fracture. | ⚠ | ✅ EN-7 0.1.7 |
| C16 | Statue spawns | Statue NPCs drop no loot or coins and are flagged `SpawnedFromStatue` (knowledge). | `TriggerStatue` (GameSimulation.WorldGenContent.cs:109) calls `SpawnCreature`, so the creatures drop items, credits and banner counts. This is farmable. | ⚠ | ✅ EN-6 0.1.7 |
| C17 | Critter spawning | Critters have their own conditions: daytime surface, town boosts, rain worms and so on. | Critters sit in the hostile tables and share the cap. Towns cut critters too (×0.12). | ◐ | ❌ EN-16 → 0.4.0 |

### D. Stats and combat on creatures

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| D1 | HP and damage by tier | `SetDefaults` IL: type 1 slime has 25 HP, 7 damage, 2 defense; type 2 demon eye has 60 HP, 18 damage; type 3 zombie has 45 HP, 14 damage, 6 defense. | Authored per archetype. The swarm bug has 24 HP and 8 damage. Hard-roster kinds have 200–600 HP. `DefenseBalance.EnemyHitScale` raises damage for tough creatures. | ✅ | ✅ as audited |
| D2 | Creature defense | `stfld NPC.defense` appears 635 times in `SetDefaults`. | `EnemyArchetype.Defense` (Actors.cs:441) is **0 for all 465** [probe]. `CreatureDefense` (Combat.cs:185) is wired but unused in practice. | ❌ | ✅ EN-4 0.1.15 |
| D3 | Knockback resistance | Per NPC: `knockBackResist` appears 531 times (demon eye 0.8, zombie 0.5). | Derived: HP bulk, size and worm flag; bosses 0.85–1 (`KnockbackResistance` CombatModel.cs:610). | ◐ | ✅ EN-4 0.1.15 |
| D4 | Knockback soft caps | Soft caps above 8/10/12/14, hard cap 16, ×1.4 on a crit. | `KnockbackTaken` (:473) matches. | ✅ | ✅ as audited |
| D5 | Contact hitbox | Hitbox rectangles intersect. | Centre distance must be under 1.15 tiles (GameSimulation.cs:1666). Worm segments must be within 0.9 of the player **centre** (EnemyAiFamilies.cs:96). Bosses use the rectangle. | ⚠ | ✅ EN-12 0.1.15 |
| D6 | Contact cadence | Player immunity frames after a hit. | Per-creature `ContactCooldown` plus the player's immunity frames. | ✅ | ✅ as audited |
| D7 | Difficulty scaling | Expert and Master multiply HP and damage (knowledge). | `WorldSettings.EnemyScale` is 0.75, 1, 1.4 or 2 (incoming ×, outgoing ÷). | ✅ | ✅ as audited |
| D8 | Post-Fracture rescale of **all** regular creatures | Not done: hardmode only adds new pools. | `ApplyHardmodeScaling` (Hardmode.cs:186) applies ×1.7 HP and ×1.3 damage (Classic) to every non-boss, **including** hard-roster natives that were authored for hardmode. | ➕ | ➕ kept |
| D9 | Per-attacker hit immunity | `NPC.immune[player]` (`GetImmuneTime` IL). | `MarkHitBy` and `CanBeHitBy` per attacker id (Actors.cs:636). | ✅ | ✅ as audited |
| D10 | Thorns reflection | Thorns potion. | `CombatStats.Thorns` reflects contact hits (:1679). | ✅ | ✅ as audited |

### E. Debuffs on creatures (`Systems/GameSimulation.PlayerSystems.cs`)

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| E1 | Debuff set | About 20 NPC debuffs: on fire, poisoned, venom, cursed inferno, ichor (−defense), frostburn, confused, midas, daybroken, shadowflame, oiled, Betsy's curse and others (knowledge). | Six: burning 6/s, poison 4/s, frostbite 2/s plus slow, armour shred (×1.25 damage taken), confused (stumbles), marked (×1.1) (`AfflictionSeconds` :65, `UpdateAfflictions` :198). | ◐ | ✅ EN-17 0.1.15; IC-3 0.1.15 |
| E2 | Immunities | A per-NPC `buffImmune[]` table. | A keyword match on the display name (`ResistsAffliction` :78). Brittle: any name with "drone" resists poison, and any name containing "fire" resists burning. | ◐ | ✅ EN-5 0.1.15 |
| E3 | Bosses | Bosses are immune to confusion, and some to many debuffs. | Bosses ignore confusion and keep afflictions for half as long. | ✅ | ✅ as audited |
| E4 | Damage-over-time kills | Count as kills. | The shared defeat loop (:1693) gives drops, banners and the bestiary entry. | ✅ | ✅ as audited |

### F. Despawning

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| F1 | Far despawn | `CheckActive` IL: outside a box of `activeRangeX/Y` (about 2.1 screens), `timeLeft` counts down from `activeTime` (750 ticks, about 12.5 s; knowledge for the value). | Beyond 48 tiles for 3 s (`UpdateDespawning` Spawning.cs:203, `CombatTuning.DespawnDistance`). | ◐ | ❌ EN-9 → 0.4.0 |
| F2 | Exemptions | Bosses, town NPCs and `DoesntDespawnToInactivity` (IL). | Exempt: bosses, event and raid units, stationary kinds and disguised mimics (`CanDespawn` :198). | ✅ | ✅ as audited |
| F3 | Dusk bosses at dawn | Night bosses leave at day. | `UpdateBossesAtDawn` (:235). | ✅ | ✅ as audited |
| F4 | Night creatures at dawn | Zombies and eyes wander off at day (`EncourageDespawn`, `DespawnEncouragement_AIStyle3_*`). | None: night spawns stay until killed or far away. | ❌ | ❌ EN-9 → 0.4.0 |
| F5 | Bosses leave after the player dies | Yes. | `BossesLeaveOnPlayerDeath`, after 3 s (:253). | ✅ | ✅ as audited |

### G. Drops, banners and the bestiary

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| G1 | Item drops | Rule-based chances, many 1/N (knowledge). | `NpcDropRegistry` gives guaranteed drops (NpcDropRegistry.cs:13) plus `CreditDrop`. Chance drops come from `ArsenalItems.MobDrops` (Gear.cs:382). | ◐ | ✅ EN-11 0.1.15 |
| G2 | Rare-drop granularity | 1/500, 1/1000 and so on. | Whole percents only (`GearRandom.Next(100) < percent`), so nothing below 1%. | ◐ | ✅ EN-11 0.1.15 |
| G3 | Heart and mana pickups | `NPCLoot_DropHeals` (IL, 392 instructions). | None. | ❌ | ✅ EN-10 0.1.15 |
| G4 | Event unit loot | Invasion units drop items. | 48 event kinds have no registry drops: credits, event loot and essence only [probe]. | ◐ | ✅ EN-11 0.1.15 |
| G5 | Boss bags, trophies, relics | Expert bags, a 10% trophy, Master relics. | `BossDropsOrBag`, trophy 10% with luck, relics in Annihilation (PlayerSystems.cs:393–416). | ✅ | ✅ as audited |
| G6 | Luck extra drops | Luck nudges some rolls. | Positive luck re-rolls mob drops (`OnCreatureDefeatedPlayerSystems`). | ➕ | ➕ kept |
| G7 | Banner per kills | `CountKillForBannersAndDropThem` IL: 50 kills by default (knowledge). | `KillsPerBanner = 50` for every hostile non-boss kind (`PlayerItems.EarnsBanner`). | ✅ | ✅ as audited |
| G8 | Banner bonus | `GetBannerBuffEffect` is data-driven: about +50% damage dealt and some damage-taken reduction (knowledge). Range is the nearby screen area. | +25% dealt and ×0.8 taken within 30 tiles (`BannerDamageBonus`, `BannerDamageTakenFactor`). | ◐ | ✅ EN-18 0.1.15 |
| G9 | Banner item and placement | Every enemy has a banner. | `BannerFor(kind)` gives ids 20000+kind. Placed banners are tracked, and one knocked down drops back (`RefreshBanners`). | ✅ | ✅ as audited |
| G10 | Bestiary tiers | Seen, then killed, then drops revealed over kills (knowledge). | Tiers are seen, 1 kill and 10 kills; drops and biomes are logged (`Bestiary.cs`). Sightings come from 24 tiles. | ✅ | ✅ as audited |
| G11 | Bestiary screen | Yes. | `OpenBestiary` (GraphicalSandbox.Screens.cs:87). | ✅ | ✅ as audited |
| G12 | Kill counts persist | Yes. | `KillCounts` live in the world snapshot. | ✅ | ✅ as audited |

### H. Critters

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| H1 | Roster | About 100 critters including gold variants. | 102 passive kinds, 97 catchable (`ContentCreatures.cs:15`, `ContentFillData.Critters`). | ✅ | ✅ as audited |
| H2 | Catching | `ItemCheck_CatchCritters` IL: the net hitbox must intersect the critter. | `SwingBugNet` (GameSimulation.Items.cs:427): within 1.6 tiles of the target and within reach. Every catchable kind has a same-named item [probe 0 missing]. | ✅ | ✅ as audited |
| H3 | Release | Yes. | `ReleaseCritter` (:448). | ✅ | ✅ as audited |
| H4 | Bait power | Per critter. | `BaitPowerFor` (ContentItems.cs:543) and `FillCritter.BaitPower` feed into `Fishing`. | ✅ | ✅ as audited |
| H5 | Gold variants | Rare (about 1 in a few hundred; knowledge), worth 10 gold. | 10 gilded variants and the Golden critter. Their weights are **7%** of the base (`GoldShare`, ContentFillData.cs:19, 67–75), so they are common. Value 600. | ⚠ | ❌ EN-15 → 0.4.0 |
| H6 | Lava critters | You need a lava-proof net, or you get burnt (`CatchCritters` IL calls `Player.Hurt` and `AddBuff`(24, 300)). | No rule. | ❌ | ❌ EN-16 → 0.4.0 |
| H7 | Town critters | Towns raise critter spawns (knowledge). | Towns lower them along with hostiles. | ◐ | ❌ EN-16 → 0.4.0 |

### I. Hostile projectiles

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| I1 | Caster bolts | Slow bolt toward the target. | 3 bolts at 4.5 speed, homing at 1.6 rad/s (`UpdateHomingProjectiles` :175). | ✅ | ✅ as audited |
| I2 | Boss volleys | Unique per boss. | A fanned volley, faster in phase 2 (`FireBossVolley`). | ✅ | ✅ as audited |
| I3 | Trap darts | They hit NPCs too (knowledge). | `ProjectileTeam.Enemy` darts hit the player only (Physics.cs:971). | ◐ | ◐ unchanged since the audit |
| I4 | Projectile variety | Gravity lobs, tile-piercing shots and on-hit debuffs (stinger poison, cursed flames). | Straight or homing only. Shots die on solid tiles. A `DamageKind` adds a wound type, but there are no debuffs on the player. | ◐ | ❌ IC-12 → 0.6.0 |
| I5 | Projectile art | Sprite per projectile. | Enemy bolts are drawn as a pen line. `SpriteName` (for example `bolt_homing`) is ignored (GraphicalSandbox.cs:~2077). | ◐ | ❌ IC-11 → 0.4.0 |

### J. Sprites and animations (`UI/SpriteAtlas.cs`, `UI/AnimationCatalog.cs`)

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| J1 | Base sprite, idle, move and attack frames for each creature | Frame sheets. | 462/462 present (`--entity-sprites` checked against `Assets/Sprites` with the atlas prefixes). 5 bosses use hand-made sheets. | ✅ | ✅ as audited |
| J2 | Unique art per kind | Yes. | 3 shared pairs: CaveBat with SkyHawk (a hostile bat drawn as a passive hawk), CaveMoth with GeigerBug, Wraith with GlowStalker [probe]. | ◐ | ❌ EN-14 → 0.4.0 |
| J3 | Attack animation for regular enemies | Yes. | `EnemyFrame` uses `_attack_` frames only while `Telegraphing` (bosses), so regular creatures never show their generated attack frames. | ◐ | ❌ EN-14 → 0.4.0 |
| J4 | Worm segments | Segment sprites. | Tinted ellipses (`DrawWormBody`, GraphicalSandbox.World.cs:321). | ◐ | ❌ EN-14 → 0.4.0 |
| J5 | Mimic disguise | Disguise matches the chest type. | Every mimic draws as `furn_cabin_chest` (GraphicalSandbox.cs:2059), including the urn, frozen, junk-pile and lead-box mimics. | ◐ | ❌ EN-13 → 0.4.0 |
| J6 | Hit flash and health bars | Yes. | `IsFlashing` and `DrawHealthBar`. | ✅ | ✅ as audited |
| J7 | Affliction overlay | Debuff dust. | `GraphicalSandbox.PlayerSystems.cs:98`. | ✅ | ✅ as audited |

---

## Integration steps

Sizes are S (under a day), M (1–3 days) and L (more than 3 days).

**EN-1: Ranged and stationary attackers.** It blocks the intended play of several rosters.
- **Change:**
  - Add `EnemyAiFamily.Shooter`, append-only, and give `EnemyArchetype` non-boss ranged fields: `ShotSpeed`, `ShotCooldown`, `ShotRange`, `ShotGravity`, `ShotSprite` and `ShotInflict`.
  - Add a `StepShooter` that keeps distance, aims, telegraphs (sets `Telegraphing`, so attack frames play) and fires.
  - Stationary kinds use the same step without moving.
  - Assign the family to the 12 named units in [probe] and to the 12 `Stationary` kinds.
- **Files:**
  - `Entities/Actors.cs` (enum and record)
  - `Systems/EnemyAiFamilies.cs`
  - `Entities/ContentCreatures.cs` (`Stationary`/`Family` switch at :162)
  - `tools/sprites/content_fill_gen.py`, which regenerates `ContentFillData.cs`
  - `Systems/Content/CreatureCoverageData.cs`
  - `GameSimulation.UpdateProjectiles` (gravity for lobbed shots)
- **Size:** L.
- **Dependencies:** EN-14 (projectile art); the combat area (player damage taken from shots).
- **Acceptance:** In a self-test arena, a Raider marksman placed 12 tiles away damages the player with a projectile within 5 s, and a Spitting barnacle 6 tiles away does the same. Also, no non-boss archetype whose name matches marksman, gunner, spit, turret, cannon or lobber is contact-only.

**EN-2: Fighter pathing.**
- **Change:**
  - Add a per-archetype `JumpPower` (default about 5 tiles).
  - Jump when the wall ahead is no higher than the jump power. If it is still blocked after about 1.5 s, turn around for 3 s.
  - Set `Vertical = 1` (drop-through) when the target is more than 2 tiles below on a platform.
  - Keep walking in roam legs outside the alert radius, like critters.
- **Files:** `Systems/GameSimulation.cs` (`UpdateEnemies` :1652–1665, `IsGroundBlockedAhead`), `Entities/Actors.cs`.
- **Size:** M.
- **Dependencies:** none (it reuses `PawnMovementSystem`).
- **Acceptance:** Turn the probe P2, P3 and P10 cases into self-tests:
  - a walker crosses a 3-tile wall within 6 s;
  - a walker on a platform reaches the player below within 5 s;
  - an unalerted walker's x position changes by at least 2 tiles over 10 s;
  - a 7-tile wall still holds.

**EN-3: Swimmer AI and liquid drag.**
- **Change:** Add a `Swimmer` family. Steer inside cells with liquid of at least 0.5 and clamp at the surface. Out of liquid, apply gravity and flop. Remove `Flying` from swimmer archetypes. Ground walkers in liquid get 0.5× speed and reduced gravity.
- **Files:** `EnemyAiFamilies.cs`, `ContentCreatures.cs`, `ContentFillData` generator, `UpdatePassiveCreature`, `PawnMovementSystem` (a liquid hook).
- **Size:** M.
- **Dependencies:** world and liquids area.
- **Acceptance:**
  - In a 10-deep pool, a silver fish and a shark keep their bodies below the surface row for 20 s, and the shark never ends above the surface.
  - A shark placed on land is grounded within 1 s.
  - A walker's speed in water is at most 60% of its speed on land.

**EN-4: Creature defense and explicit knockback resistance.**
- **Change:** Set `Defense` on every hostile archetype from a tier table. As a starting point: pre-boss 0–6, mid 8–14, post-Fracture 16–30, late 30–50. Add an optional `KnockbackResist` override (fall back to the formula). Expose both in `--balance`.
- **Files:** `Entities/ContentCreatures.cs`, `Entities/Actors.cs` table, the ContentFill generator, `CreatureCoverageData.cs`, `Systems/Content/CombatModel.cs`.
- **Size:** M.
- **Dependencies:** weapons and gear area (DPS curves, armour penetration), and the `GearCurve` self-tests.
- **Acceptance:**
  - Every hostile non-boss kind with more than 150 HP has `Defense` > 0.
  - The `StatParitySelfTests` time-to-kill bands still pass.
  - A hit of 10 on a defense-20 creature does 1 before penetration and more with penetration.

**EN-5: Explicit immunities and lava.**
- **Change:** Replace the keyword match in `ResistsAffliction` with `EnemyArchetype.Immune` (a flag set per status) plus `LavaImmune`. Add lava damage to creatures that are not immune (about 30/s, plus burning) in the enemy tick.
- **Files:** `GameSimulation.PlayerSystems.cs:78`, `Entities/Actors.cs`, roster data, `GameSimulation.cs` (`UpdateEnemies`).
- **Size:** M.
- **Acceptance:**
  - The "Glow firefly" can burn.
  - The "Pollen drone swarm" policy is decided explicitly.
  - A Spore husk dies in lava within 5 s, and a Core husk does not.

**EN-6: Statue spawns give no loot.**
- **Change:** Tag statue spawns (an `EnemyAgent.FromStatue` saved in the snapshot). Skip drops, credits, kill counts and banners for them in the defeat loop.
- **Files:** `GameSimulation.WorldGenContent.cs:109`, `GameSimulation.cs:1693`, `GameSimulation.Player.cs` (`OnCreatureDefeatedBatchB`), `Actors.cs` and the snapshot.
- **Size:** S.
- **Acceptance:** Kill 60 statue gel hoppers: 0 credits, 0 items, `KillCounts` unchanged, no banner.

**EN-7: Fold the family spawn stream into the main spawner.**
- **Change:** Fold the Batch B kinds into the biome or fill tables, or give `UpdateFamilySpawns` the same guards: `CreativeSpawnRate`, `NoHostiles`, town factor, dungeon and biome. Gate the Tunnel wyrm (260 HP) behind `FractureActive`, or retune a pre-Fracture worm.
- **Files:** `Systems/EnemyAiFamilies.cs:300`, `Systems/BiomeSpawnTables.cs`.
- **Size:** S.
- **Acceptance:**
  - With creative spawn rate 0, 30 game minutes give zero spawns.
  - During a `NoHostiles` event there are no hostile family spawns.
  - Before the Fracture, no Tunnel wyrm spawns.

**EN-8: Spawn-rate controls and spawn-area parity.**
- **Change:**
  - Add a `SpawnRateBonus` and `SpawnCapBonus` from statuses and placed decor: a battle-stim status, a calming-stim status, a hive-beacon placeable (water-candle analogue) and a peace-lamp placeable.
  - Fold them into `CombatTuning.SpawnFactors`.
  - Add the population ramp (C9).
  - Sample spawn cells in an annulus above and below the player as well (C4).
  - Strengthen the underground factor to match C3.
- **Files:** `Systems/Content/CombatModel.cs`, `GameSimulation.Spawning.cs`, items (`ContentItems.cs`, recipes), status ids.
- **Size:** M.
- **Dependencies:** items, buffs and crafting area (new items and recipes), and the decor scan (`ScanPlacedDecor`).
- **Acceptance:**
  - The C9 probe with a battle stim has at least 1.8× the spawns and a cap of at least 1.8×.
  - With a calming stim it has at most 0.65×.
  - Underground spawns can appear at least 10 rows above or below the player.

**EN-9: Despawn parity.**
- **Change:** Night-flagged kinds (a new `Nocturnal` flag) walk away and despawn after dawn, out of view. Lengthen `DespawnSeconds` to about 12 s and make `DespawnDistance` relative to the view (about 2 views).
- **Files:** `GameSimulation.Spawning.cs`, `CombatModel.cs`, archetype data.
- **Size:** S.
- **Acceptance:** Night spawns are gone by 08:00 if off-screen. A creature 40 tiles away for 5 s stays.

**EN-10: Heal and charge pickups.**
- **Change:** A chance on kill to drop a heart or charge orb, weighted toward a hurt player or low charge. Picking it up heals 20 or restores 100 charge.
- **Files:** `GameSimulation.Combat.cs` (`OnCreatureDefeated`), `GroundItems.cs`, `ItemSystem.cs`.
- **Size:** M.
- **Dependencies:** items and pickups area.
- **Acceptance:** Over 200 kills with the player at half health, between 15 and 60 heart pickups drop. Picking one up heals and never stacks in the inventory.

**EN-11: Drop rules.**
- **Change:** Let `NpcDropDefinition` carry a chance (numerator and denominator) and conditions (hardmode, night, difficulty). Give the 48 event units item drops. Allow chances below 1%.
- **Files:** `Systems/NpcDropRegistry.cs`, `GameSimulation.Gear.cs:382`, `ArsenalItems`, `CraftingAudit` (drop sources).
- **Size:** M.
- **Dependencies:** items and crafting area.
- **Acceptance:** A 1/500 rule hits within 0.5–3× the expected rate in 50,000 rolls, and `--craft-audit` still reports every item obtainable.

**EN-12: Contact hitboxes.**
- **Change:** Use rectangle overlap (with a small inset) for non-boss contact. Test worm segments against the player rectangle.
- **Files:** `GameSimulation.cs:1666`, `EnemyAiFamilies.cs:96`.
- **Size:** S.
- **Acceptance:** A 1.6×2 creature whose box overlaps the player box by 0.2 tiles hits. A worm segment touching the player's feet hits.

**EN-13: Missing AI families.**
- **Change:**
  - Ghost: a `Phases` flag that skips tile collision for non-boss fliers.
  - Charger: run up, then dash.
  - Wall climber: cling to wall or solid columns and climb.
  - Splitter or summoner: on death or on a timer, spawn N children.
  - Erratic bat flight and bouncing eye flight.
  - Assign them to the matching analogues: Void wisp, Grave wisp, Crypt spirit; Shard strider, Snow-wolf, hounds; Burrow spider, Silk spider-mite, Venom recluse; Mite broodmother, gel kinds.
- **Files:** `Systems/EnemyAiFamilies.cs`, `Entities/Actors.cs`, roster data.
- **Size:** L.
- **Dependencies:** EN-2 (shared pathing helpers).
- **Acceptance:** One self-test per family:
  - the ghost crosses a 5-thick wall;
  - the charger's peak speed is at least 2× its walk speed;
  - the spider climbs a 6-tile wall;
  - the broodmother spawns at least 2 mites on death.

**EN-14: Creature animation and projectile art.**
- **Change:**
  - Play `_attack_` frames on contact hits, dives, caster casts and shooter shots.
  - Draw `ProjectileState.SpriteName` for enemy shots.
  - Give worms segment and tail sprites.
  - Draw each mimic as its own container.
  - Give CaveBat, CaveMoth and Wraith their own sprites.
- **Files:** `UI/SpriteAtlas.cs` (`EnemyFrame` :595), `UI/GraphicalSandbox.cs:2059–2080`, `GraphicalSandbox.World.cs:321`, `tools/comfy` lists.
- **Size:** M.
- **Dependencies:** the art pipeline (the ComfyUI run is paused; per the user's memory, resume only on the user's go).
- **Acceptance:** A screenshot test shows the attack frame within 0.2 s of a contact hit, enemy bolts draw a sprite, and `--entity-sprites` lists no shared base sprite between a hostile and a critter.

**EN-15: Gold critter rarity.**
- **Change:** Drop `GoldShare` from 0.07 to about 0.005, or roll gold as a per-spawn upgrade of the base critter. Raise the catch value toward a "rare" price.
- **Files:** the `ContentFillData` generator and `ContentFillCreatures.CatchValue`.
- **Size:** S.
- **Acceptance:** 10,000 grass critter spawns give between 0.2% and 1% gold.

**EN-16: Critter polish.**
- **Change:**
  - Lava critters (Ember newt and the like) burn the player unless a heat-proof net is used.
  - Netting a critter records it in the bestiary.
  - Town presence stops lowering critter spawns.
  - Add bird, firefly and snail movement variants.
- **Files:** `GameSimulation.Items.cs:427`, `CombatModel.SpawnFactors`, `UpdatePassiveCreature`.
- **Size:** M.
- **Acceptance:** Netting an Ember newt with the basic net applies Burning. Catching records `Seen`. Town critter spawns stay at 80% or more of the wild rate.

**EN-17: More creature afflictions.**
- **Change:** Add a defense shred that subtracts defense (meaningful after EN-4), a stronger venom DoT, a "salvage" (midas) bonus that adds credits, and a wet-extinguishes-burning rule. Wire them to weapons and ammo.
- **Files:** `GameSimulation.PlayerSystems.cs`, `StatusId`, the weapon data.
- **Size:** M.
- **Dependencies:** EN-4; weapons area.
- **Acceptance:** A unit test per affliction for DPS, the defense delta and the credit bonus.

**EN-18: Banner and despawn numbers check.**
- **Change:** Re-measure `GetBannerBuffEffect` and the `NPCBannerBuff` table data (an `InitializeArray` blob) with `scripts/blob.py`. Set the banner bonus and range from it. Do the same for `NPC.activeTime` and `activeRangeX` from `NPC..cctor`.
- **Files:** `GameSimulation.Player.cs` constants, `CombatModel.cs`.
- **Size:** S.
- **Acceptance:** The numbers in this doc are replaced by IL citations, and the self-tests are updated.

**Blocks play:** EN-1 (harmless shooter and turret rosters) and EN-2 (2-tile wall cheese). EN-6 is an economy exploit.

---

## Cross-area dependencies
- **Combat and weapons (area 02 or the gear areas):** EN-4 defense changes time-to-kill and makes armour penetration matter. EN-1 shots need player damage-taken and immunity rules. EN-17 needs on-hit weapon hooks.
- **Items, crafting and economy:** EN-8 spawn items and their recipes, EN-10 pickups, EN-11 drop rules (and `CraftingAudit` drop sources), EN-6 credit balance.
- **World, liquids and tiles:** EN-3 liquid queries, EN-5 lava, EN-2 platforms (`TilePhysics.IsPlatform`) and EN-13 wall-climb tests.
- **Events and bosses:** C13 rosters, D8 hardmode rescale, A16 boss AI and G4 event loot. The `UpdateFamilySpawns` cleanup (EN-7) touches event `NoHostiles`.
- **Art pipeline:** EN-14 (and EN-1 projectile sprites) wait on the paused ComfyUI queue.
- **UI and input:** none required. The bestiary screen exists. The other agent's controls work does not overlap.

## Open questions for the user
1. **Hardmode rescale (D8).** Should post-Fracture ×1.7 HP and ×1.3 damage keep applying to creatures that were already authored for hardmode, which scales them twice? Terraria never rescales; it only swaps the pools.
2. **Walls as defence (B2).** How high should a walker jump? Terraria fighters clear several tiles. Should a 2-tile wall stay a valid early-game defence?
3. **Gold critters (H5).** Keep them common (7% of the base) as a Rimtera choice, or make them rare like Terraria's?
4. **Banners (G8).** Keep +25% dealt and −20% taken within 30 tiles, or match Terraria's numbers after EN-18 measures them?
5. **Event units (G4).** Should the 48 event creatures drop materials, or is "credits plus event loot" deliberate?
6. **Shooter roster (EN-1).** Is it acceptable to turn the 12 stationary kinds into shooters, or should some (for example the Snapjaw plant) become tethered lungers instead?
