# 02 · Bosses and mini-bosses: Rimtera vs Terraria

Read-only audit (2026-10-28, branch `main` with uncommitted work). Terraria v1.4.5.8 is behavioural evidence only. Nothing here copies its code, names or assets.

**Evidence used**
- Terraria CIL dumped with `docs/terraria-evidence/full/scripts/ilall.py` into a private scratch folder (not the repo): `NPC.AI` (the giant dispatcher, 113,900 instructions, which holds aiStyles 4/11/27-31/43/51/54 for the Eye, Skeletron, Wall of Flesh, Twins, Prime, Queen Bee, Plantera and the Brain), `NPC.AI_015_KingSlime`, `AI_006_Worms`, `AI_037_Destroyer`, `AI_045_Golem`/`AI_047_GolemFist`, `AI_069_DukeFishron`, `AI_077..081` (Moon Lord core, hands, head, true eyes), `AI_084_LunaticCultist`, `AI_110_Betsy`, `AI_117_BloodNautilus`, `AI_120_HallowBoss`, `AI_121_QueenSlime`, `AI_123_Deerclops`, `Player.ItemCheck_UseBossSpawners`, `Player.SummonItemCheck`, `NPC.SpawnOnPlayer`, `NPC.SpawnWOF`, `NPC.DoDeathEvents_DropBossPotionsAndHearts`, `Main.UpdateTime_StartNight`, `Main.UpdateAudio_DecideOnNewMusic`, `BigProgressBarSystem..ctor`, `ItemDropDatabase.RegisterBoss_*`. Offsets are quoted as `Method@IL`.
- Rimtera: a code read plus one scratch probe (`BossProbe`, built in a private copy, never in the repo). The probe spawned every boss for 8 s against an idle player who was healed each tick. It also tested summon refusals, dawn, distance, death and loot on three difficulties. Its results are quoted as **(probe)**.
- Claims in `docs/CREATURE_COVERAGE.md` §3, `PROGRESSION_DIFF.md` and `HARDMODE_DIFF.md` were checked against the code. The coverage doc's summon and gate table is accurate. Its implied "analog parity" is not: every boss runs the same AI.

---

## Summary

**Counts:** 33 bosses (`IsBoss`), 9 coverage mini-bosses and 4 obelisks. This doc has 79 rows: 38 shared-system rows (table A), 33 per-boss rows (table B) and 8 mini-boss and obelisk rows (table C).

| Status | Shared systems (A) | Per boss (B) | Mini-bosses and obelisks (C) | Total |
|---|---|---|---|---|
| ✅ matches | 13 | 0 | 0 | **13** |
| ◐ partial | 14 | 20 | 8 | **42** |
| ❌ missing | 6 | 9 | 0 | **15** |
| ⚠ broken / bug | 3 | 0 | 0 | **3** |
| ➕ Rimtera-only (deliberate) | 2 | 4 | 0 | **6** |

**Top 10 issues by player impact**

1. **Every boss fights the same way** (❌ A2, every row in B). One shared controller runs all 33 bosses (`GameSimulation.cs:1750-1786`): approach, a 0.55 s telegraph, a fanned volley, then recovery. Phase 2 is the same tweak for all of them: +1 shot, 1.25× projectile speed, a 0.69× cooldown and 1.3× movement. In the probe all 33 bosses closed to 0.4-4.7 tiles of the player and moved −12 to −14 tiles: the same signature for each. Terraria gives every boss its own AI with distinct attacks.
2. **No multi-part or worm bosses** (❌ A9). The analogs of the Eater of Worlds, Destroyer, Twins, Skeletron, Prime, Golem, Wall of Flesh, Moon Lord and Martian Saucer are all one rectangle. Worse, `UpdateEnemies` skips AI families for bosses (`!enemy.IsBoss &&`, `GameSimulation.cs:1642`), so the Fallout Hive-mind's `DivingFlyer` family is dead data (⚠ A8).
3. **A boss far away never leaves, and it blocks the game** (❌ A14). Bosses are exempt from distance despawn (`Spawning.cs:198`). In the probe, a boss 188 tiles away was still alive after 10 s, with no timeout. While any boss lives, summon items refuse (`Items.cs:79`), natural spawns are throttled, dusk incursions, bulbs, the ritual and hard-boss arrivals are skipped, and respawn takes 20 s. The only way out is to die.
4. **Night bosses summoned by item stay through dawn** (◐ A13). Only the 4 dusk-incursion bosses and bosses tagged as natural night arrivals leave at dawn. In the probe the Overseer mech (Eye of Cthulhu analog), Moon Eater, Hollow Regent, Eclipse Lord, Cyber-Swarm Leader and Warship Core were all still present at 07:00. Terraria's night bosses flee at day (`AI_037_Destroyer@2408 Main.IsItDay`).
5. **Summon items have almost no time or biome rules** (◐ A17). In the probe, the Reactor key worked at 08:00, the Rotting seed on grassland, the Fallout hive cell outside the radiated deeps, the Rusted lure by day and the Scout beacon anywhere. Terraria: Suspicious Looking Eye at night only (`ItemCheck_UseBossSpawners@326 Main.IsItDay`), Worm Food in the Corruption only (`@478 get_ZoneCorrupt`), mechanical summons at night only (`@626`, `@852`).
6. **No enrage rules** (❌ A11). Terraria has day enrage for Skeletron and the Empress (`AI_120_HallowBoss` reads `Main.dayTime` 4×), temple enrage for Golem (`AI_045_Golem` reads `ZoneLihzhardTemple`), ocean enrage for Duke Fishron, and jungle enrage for Plantera. Rimtera has none, so arenas and biome rules mean nothing during a fight.
7. **Two bosses can be alive at once, but the bar shows one** (⚠ A19, A30). Event finale bosses (`World.cs:182`) and fished-up Leviathans ignore the one-boss rule. In the probe, an event finale during a summoned fight gave 2 live bosses. `DrawBossBar` draws only `FirstOrDefault` (`GraphicalSandbox.cs:3170`).
8. **No boss minions** (❌ A10). There are no servants, creepers, bees, probes, slimes or Hungry. Terraria uses minions to pace almost every boss fight.
9. **Boss loot gaps** (◐/❌ A24, A25, A29). No boss drops healing potions or hearts (Terraria: `DoDeathEvents_DropBossPotionsAndHearts` drops items 28/188/499/3544 by tier, plus hearts). The Fission Tyrant (Wall of Flesh analog), Void Cantor and Fallout Hive-mind have empty weapon pools (probe: pool 0). The 33 core-chips are plain stat sticks that cycle through 5 templates by index (`PlayerSystemsContent.cs:138-145`). Terraria's expert items each have a unique effect.
10. **One music theme and a single-body boss bar for everything** (◐ A30, A31). The procedural `"boss"` theme plays for all 33 bosses (`AudioDirector.cs:112`). Terraria picks per-boss tracks (`UpdateAudio_DecideOnNewMusic`), and `BigProgressBarSystem..ctor` registers special aggregate bars for the Twins, EoW, BoC, Golem, the pillars, Moon Lord, the Old One's Army, the Flying Dutchman, the Martian Saucer, the Cultist and Deerclops.

Nothing here blocks play. Issue 3 is the one closest to blocking: a boss that drifts away traps the player out of summons, events and normal spawning until they die.

---

## A. Shared boss systems

| # | Feature (Rimtera pointer) | Terraria behaviour (evidence) | Rimtera actual behaviour | Status |
|---|---|---|---|---|
| A1 | Boss data row: `EnemyArchetype` with `Tier`, `ProjectileSpeed`, `VolleyCount`, `PhaseThreshold`, `AttackRange`, `AttackCooldown`, `TelegraphSeconds` (`Entities/Actors.cs:400-445`). Tiers Gate/Easy/Hard/Event/Final (`Actors.cs:389`) | `NPC.SetDefaults` sets `boss`, `lifeMax`, `damage`, `defense`, `aiStyle`, `knockBackResist=0`, `npcSlots` per type | One record per boss with volley tuning only. No parts, no attack list, no `Defense` set on any boss (all 0) | ◐ |
| A2 | Shared boss controller `UpdateBossController` + `FireBossVolley` (`Systems/GameSimulation.cs:1750-1808`) | A distinct AI per boss: `aiStyle` 4/11/15/27-31/37/43/45/51/54/69/77-84/110/120/121/123 (methods above) | The same state machine for all 33 bosses: Pursuing → Telegraphing 0.55 s → fanned volley (0.17 rad step) → Recovering 0.35 s. Probe: every boss ends 0.4-4.7 tiles from the player with nearly the same path | ❌ |
| A3 | Phase 2 (`GameSimulation.cs:1754-1758`, `PhaseThreshold`) | Many bosses have 2-3 phases with new attacks (Eye transforms at 50%, Twins at 40%, Plantera at 50%, Empress at 50%, Duke at 50% and 15%, Moon Lord's core opens after the eyes) | One threshold per boss (0.4-0.6). Phase 2 = +1 shot, ×1.25 projectile speed, ×0.69 cooldown, ×1.3 move, and the message "overloads its systems!". Probe: all 33 reach phase 1 at 39% HP | ◐ |
| A4 | Volley projectile (`FireBossVolley`) | Per boss: lasers, stingers, homing bolts, bubbles, sharknadoes and so on, each with its own speed, count and pattern | Fan of N straight bolts. Damage kind is Shot (Burn for the Solar Flare Entity). Lifetime 3 s, knockback 4. Capped by `BossDamageCap` | ◐ |
| A5 | Boss contact hit (`GameSimulation.cs:1682-1689`) | Contact damage on overlap, using the boss's `damage` | Hits when the player is within 0.6 tiles of the boss rectangle, 1.4 s cooldown, Crush damage | ✅ |
| A6 | Flying boss movement (`MoveFlyingEnemy`, `GameSimulation.cs:1810-1838`) | Flyers dash, circle and hover offsets per AI. Most ignore tiles (`noTileCollide`) | Lerps toward a point 2.5 tiles above the player at 2.8 tiles/s × multiplier. Passes through terrain, stays inside the world. No dashes | ◐ |
| A7 | Ground boss movement (`GameSimulation.cs:1657-1667`) | King Slime and Queen Slime teleport when stuck (`AI_015_KingSlime_FindTeleportSpot`). Golem jumps. Skeletron and the Wall of Flesh ignore tiles | Walks with `MovementInput` and jumps one step when blocked. Cannot reach a player on a pillar or behind a wall. No teleport or leap | ◐ |
| A8 | AI families for bosses (`GameSimulation.cs:1642` `!enemy.IsBoss && …StepAiFamily`) | n/a | The Fallout Hive-mind declares `Family = DivingFlyer` (`Actors.cs:474-476`), but the boss path never reads families. Dead data, and Worm/Caster families are unavailable to bosses | ⚠ |
| A9 | Multi-part and worm bosses | EoW, Destroyer (`AI_006_Worms`, `AI_037_Destroyer`, `realLife` ×17), Skeletron and Prime hands, Golem head and fists (`AI_047_GolemFist`), Twins, Wall of Flesh eyes and mouth, Moon Lord hands, head and core (`AI_077-079`), Martian Saucer guns, Dutchman cannons | None. Every boss is one body (`SegmentCount` is 0 for all 33 in the probe) | ❌ |
| A10 | Boss minions | Servants of Cthulhu, Creepers, Bees, Probes, Hungry, King Slime's slimes (`AI_015` `NewNPC`), Queen Slime's crystal slimes, Cultist's dragons, Golem fists, Moon Lord's true eyes | None. Bosses only fire volleys | ❌ |
| A11 | Enrage conditions | Skeletron by day; Empress by day (`AI_120_HallowBoss_IsGenuinelyEnraged`, `Main.dayTime` ×4); Golem outside the temple (`AI_045_Golem` reads `ZoneLihzhardTemple`/`ZoneJungle`); Duke outside the ocean; Plantera outside the jungle; Queen Bee outside the jungle | None. Biome and time have no effect after spawn | ❌ |
| A12 | Leave on player death (`BossesLeaveOnPlayerDeath`, `GameSimulation.Spawning.cs:249-262`) | Most boss AIs flee or `EncourageDespawn` when every target is dead (`AI_015@… Player.dead`, `NPC.EncourageDespawn`) | All living bosses leave 3 s after death. Probe: the boss was gone within 6 s | ✅ |
| A13 | Leave at dawn (`UpdateBossesAtDawn`, `Spawning.cs:235-246`; `AdvanceHardmodeClock`, `Hardmode.cs:268-280`) | Night bosses (Eye, Twins, Destroyer, Prime, Skeletron as day enrage) flee at daybreak (`AI_037_Destroyer@2408 Main.IsItDay`). Event bosses go when their event ends | Leaves only if the kind is one of the 4 `EventBosses` or its id is in `_nightBossIds` (natural patrol and hard-boss dusk arrivals). Probe: the Overseer, Moon Eater, Hollow Regent, Eclipse Lord, Cyber-Swarm Leader and Warship Core stay at 07:00 when summoned by item | ◐ |
| A14 | Leave by distance or timeout (`CanDespawn`, `Spawning.cs:198`) | `CheckActive`/`timeLeft`: a boss with no valid target flies off and despawns. EoW despawns outside the Corruption (`AI_006_Worms` reads `ZoneCorrupt`) | Bosses are exempt from despawn. Probe: still alive 188 tiles away after 10 s, with no limit. A stranded boss blocks summons, events and spawning | ❌ |
| A15 | Live bosses saved with the world (`GameSimulation.EntitiesSave.cs:17-80`, `NightBossIds`) | Hostile NPCs are not saved. Leaving the world ends the fight | Bosses (with HP and phase) round-trip through save and load | ➕ |
| A16 | "Already here" summon refusal (`GameSimulation.Items.cs:79-83`) | `Player.SummonItemCheck` refuses only when the **same** boss type is alive | Refuses while **any** boss is alive (probe: a Swarm pheromone during the Overseer fight was refused). Stricter, but consistent | ◐ |
| A17 | Time and biome refusals (`HardmodeSummonRefusal`, `Hardmode.cs:136-166`; `CoverageSummonRefusal`, `CreatureCoverage.cs:193-203`) | Night only: Eye, mech summons, Pumpkin and Frost moon items (`ItemCheck_UseBossSpawners@326,626,852`). Biome: Worm Food/Flesh in the evil biome (`@478 ZoneCorrupt`), Deer Thing in snow, Gelatin Crystal in the Hallow, Truffle Worm fished in the ocean, Lihzahrd Power Cell at the altar | Only the Core igniter (Irradiated Core, after the Scout Mech), Frostbitten lure (frozen), Prism gel lattice (spires and Fracture), Regent's wick and Seraph frost-core (night and Fracture), plus progression gates. Probe: the Reactor key at 08:00 spawned, the Rotting seed on grassland spawned, the Fallout hive cell outside the radiated deeps spawned, the Rusted lure by day spawned, the Scout beacon away from the dungeon spawned | ◐ |
| A18 | Spawn placement (`SpawnBoss`, `GameSimulation.cs:734-746`) | `NPC.SpawnOnPlayer` places the boss off-screen, about 60-100 tiles away depending on type | 12 tiles in front of the player's facing, on the surface column (flyers +4), raised out of solid ground. Underground summons appear on the **surface** above the column (`FindSurface`) | ◐ |
| A19 | One boss at a time | Several bosses can coexist (each summon checks only its own type). Bars stack | The rule is inconsistent: item summons refuse, but event finales (`World.cs:182-188`) and fishing (`Fishing.cs:200`) do not check. Probe: 2 live bosses | ⚠ |
| A20 | Natural triggers: Overseer patrol (`Hardmode.cs:337-356`), hard trio at dusk (`Hardmode.cs:296-322`), canopy bulbs (`Hardmode.cs:398-441`), ritual (`Hardmode.cs:443-454`), blight hearts (`Blight.cs:252`), bloom hive and crystal altar (`Items.cs:783-798`, `WorldGenContent.cs:86-93`), vault (`WorldGenContent.cs:145`), fishing (`Fishing.cs:200-224`), dusk incursions (`GameSimulation.cs:2422-2455`), event finales (`WorldEvents.cs:340-352`), astral countdown (`World.cs:482-494`) | Eye at night (`UpdateTime_StartNight@207-406`: `!downedBoss1`, `statLifeMax≥200`, `statDefense>10`), mech bosses at night after the Wall of Flesh, Plantera bulbs, Cultist ritual, orb/heart smashing, the larva, Golem altar, Duke by Truffle Worm, Moon Lord 60 s after the pillars | Present for every analog. The Overseer patrol needs life 140 and defense 6, a scaled version of 200/10 | ✅ |
| A21 | Boss hit ceiling `BossDamageCap = 100` (`Hardmode.cs:23,124-130,182-195`) | None | A deliberate Rimtera cap on any single boss hit | ➕ |
| A22 | Respawn timer with a boss alive (`GameSimulation.Player.cs:996-1010`) | `GetRespawnTime` is longer while a boss is alive. The timer is set at death (`KillMe@1553`) | 20 s while any boss lives, set at death. Bosses leave at 3 s, but the timer stays 20 s (probe: 14 s still left after the boss was gone) | ◐ |
| A23 | Defeat record: `_defeatedBosses` + `boss.<key>.down` flags (`GameSimulation.cs:1699-1706`, `World.cs:240-241`), saved (`GameSimulation.cs:384,583-590`) | `NPC.downed*` flags, saved | Both are recorded and saved (probe: flag and set both true). A hard-trio kill before the Fracture is un-recorded (`Hardmode.cs:207-208`) | ✅ |
| A24 | Guaranteed drops (`NpcDropRegistry`) + gear pool (`OnCreatureDefeatedArsenal`, `GameSimulation.Gear.cs:365-378`) + credits | `ItemDropDatabase.RegisterBoss_*`: a weapon 1-of-N, materials, and the expert bag in expert mode | Drops go to the inventory, plus one pool item on the ground (a 35% chance of a second when the pool has 4+ items), plus `CreditDrop`. The Fission Tyrant, Void Cantor and Fallout Hive-mind have an **empty** pool (probe) | ◐ |
| A25 | Boss healing potions and hearts | `DoDeathEvents_DropBossPotionsAndHearts`: lesser/healing/greater/super healing potions (items 28/188/499/3544) by boss, plus hearts (58) | None | ❌ |
| A26 | Salvage cache on Brutal and Annihilation (`BagReplacesBossLoot`, `OpenTreasureBag`, `GameSimulation.PlayerSystems.cs:388-436`) | Expert and Master treasure bag: the boss's loot pool + an expert item + money | The cache replaces the loot. Opening it gives drops ×1.25, one pool item, the core-chip and credits (probe: ferrite ore, energy crystal, hand-cannon, core-chip, credits). One cache per boss, all 33 exist | ✅ |
| A27 | Trophy (`OnCreatureDefeatedPlayerSystems`, `PlayerSystems.cs:409-413`) | 10% trophy (`RegisterBossTrophies`) | 10%, tilted by luck, any difficulty. Placeable `TileId.BossTrophy`, gives back its item when broken | ✅ |
| A28 | Relic (`PlayerSystems.cs:414-415`) | Master-mode relic, 100% | Annihilation only, 100% (probe: dropped on Annihilation) | ✅ |
| A29 | Core-chip, the expert item (`PlayerSystemsContent.cs:138-145`) | A unique expert accessory or item with its own effect per boss (Royal Gel, Worm Scarf, Brain of Confusion, Shield of Cthulhu…) | A generic accessory. `i % 5` picks +crit, +defense, +move, +life or +charge regen (probe: Overseer crit 5, Alpha def 5, Drone move 1.07, …) | ◐ |
| A30 | Boss bar (`DrawBossBar`, `UI/GraphicalSandbox.cs:3168-3185`) | `BigProgressBarSystem`: per-boss bars with a head icon. Aggregate bars for multi-part bosses and events (`..ctor` registers types 125/126, 13-15, 266, 245/246/249, 422/493/507/517, 396-398, 548/549, 491/492, 393-395, 440, 668). Several bosses stack | One bar for the **first** living boss: HP numbers and the name. No icon, no stacking, no mini-boss bars, no shield state for the obelisks | ◐ |
| A31 | Boss music (`UI/AudioDirector.cs:96-131`, `AudioEngine.cs:652`) | `UpdateAudio_DecideOnNewMusic` picks Boss 1-5, Plantera, Moon Lord, Queen Slime, Empress, Deerclops, event themes and so on by boss type | One procedural `"boss"` theme for every boss, tension 1.0 | ◐ |
| A32 | Arrival sting (`AudioDirector.cs:96-99`) | A roar sound on spawn | Plays `boss` once when the first boss appears | ✅ |
| A33 | Map and minimap boss icons (`GraphicalSandbox.cs:3002-3008, 3124-3126`) | Boss head icons on the map | Minimap square, plus a full-map sprite icon on revealed cells | ✅ |
| A34 | Awake and defeat messages (`Items.cs:96`, `GameSimulation.cs:1704-1706`, `World.cs:186`) | "X has awoken!" and "X has been defeated!" | Present, with flavour lines per trigger | ✅ |
| A35 | Debug "Summon next boss" (`TrySummonNextBoss`, `GameSimulation.cs:721-732`; bound in `UI/GraphicalSandbox.cs:454,957`, `ConsoleSandbox.cs:143`) | n/a | Unbound by default (`PreferencesAndProfiles.cs:240`), but when bound it skips **every** refusal: the hard trio before the Fracture, the Astro-God without the obelisks or the countdown | ⚠ |
| A36 | Difficulty scaling for bosses (`ScaleIncoming`, `GameSimulation.cs:2548`; `ApplyHardmodeScaling`, `Hardmode.cs:176-189`; `DefenseBalance.EnemyHitScale`) | Expert and Master: ×2/×3 life, ×2/×3 damage, plus extra attacks | Incoming damage scaled by `EnemyScale`, outgoing divided (same effect as more HP). The hardmode ×HP pass skips bosses. No extra attacks on harder difficulties | ✅ |
| A37 | Natural spawn throttle while a boss lives (`Spawning.cs:29-56`) | The spawn rate falls while a boss is alive | Yes, through `CombatTuning.SpawnFactors(…, bossAlive)` | ✅ |
| A38 | Boss knockback (`KnockEnemy`, `GameSimulation.Combat.cs:236-243`) | `knockBackResist = 0` on nearly every boss | Uses `CombatTuning.KnockbackResistance(archetype)` (shared table, not boss-specific in this file). Not checked further here; see the combat area | ✅ |

## B. Per boss

AI column: "generic" means the A2 controller with this row's volley values. The analog column follows `docs/CREATURE_COVERAGE.md` §3. The hard-trio mapping is my inference (see the open questions). HP and damage values are Rimtera's. Terraria's absolute numbers (Eye 2,800, Moon Lord about 145k) are on another damage scale, so only relative order is compared.

| # | Boss (kind, file) | Tier / HP / dmg / body | Analog and Terraria AI (evidence) | Rimtera AI (probe) | Trigger and refusals | Loot | Despawn | Status |
|---|---|---|---|---|---|---|---|---|
| B1 | Overseer mech `StoneSentinel` (`Actors.cs:461`) | Gate / 520 / 20 / ground | Eye of Cthulhu (aiStyle 4 in `NPC.AI`): hovers, spawns Servants, 3 dashes; at 50% transforms and dashes faster; flees at day | Generic, 1 bolt, 8 speed. Walks (not flying). Probe: 2 shots in 8 s, contact range 0.4 | Reactor key (no night rule ⚠ probe). Natural dusk patrol at life 140 / def 6 (`Hardmode.cs:337`) ✅ | Drops + 3-item pool + Overseer circuit (`World.cs:245`); opens the Swarm Rain | Patrol version leaves at dawn; item version stays (probe) | ◐ |
| B2 | Alpha Skitter `AlphaSkitter` (`Actors.cs:465`) | Easy / 650 / 18 / ground | King Slime (`AI_015_KingSlime`): big hops, teleports near the target, sheds slimes as HP drops, shrinks | Generic walk (bosses skip the hop code at `:1655`), 3-shot fan | Swarm pheromone, any time and place. Swarm Rain finale (`WorldEvents.cs:450`) | Drops + 1-item pool | Stays; no distance despawn | ◐ |
| B3 | Damaged Mining Drone (`Actors.cs:467`) | Easy / 600 / 16 / flyer | No twin (Rimtera's own) | Generic hover, 2-shot | Faulty mining chip, anywhere | Drops + 3 pool | — | ➕ |
| B4 | Plant Overgrowth (`Actors.cs:469`) | Easy / 820 / 20 / ground | Brain of Cthulhu (aiStyle 54/55): Creepers orbit; phase 2 teleports with illusions; crimson only | Generic walk at 0.6×, 5-shot | Rotting seed (works on grassland ⚠ probe). Every 3rd spore heart (`Blight.cs:252`) | Drops + 4 pool | — | ❌ |
| B5 | Fallout Hive-mind (`Actors.cs:474`) | Easy / 800 / 22 / flyer | Eater of Worlds (`AI_006_Worms`): worm segments that split; despawns outside the Corruption (`ZoneCorrupt`) | Generic hover; `DivingFlyer` ignored (A8). Not a worm | Fallout hive cell (works outside radiated ⚠ probe). Every 3rd radiated heart before the Fracture (`Blight.cs:265`) | Drops; **empty pool** | — | ❌ |
| B6 | Scout Mech (`Actors.cs:471`) | Easy / 900 / 22 / ground | Skeletron (aiStyle 11 + hands 12): spinning head, 2 swatting hands, day enrage; summoned at the dungeon by night | Generic walk, 2-shot | Scout beacon anywhere (probe). Opens the dungeon (`Physics.cs:68`) | Drops + 2 pool | — | ❌ |
| B7 | Spore Monarch (`ContentCreatures.cs:191`) | Easy / 900 / 20 / flyer | No twin (fungal) | Generic, 4-shot | Spore crown, no biome rule | Drops + 5 pool | — | ➕ |
| B8 | Hoarfrost Behemoth (`CreatureCoverageData.cs:135`) | Easy / 1000 / 22 / ground | Deerclops (`AI_123_Deerclops`): walks, ice spikes forward and on both sides (`_MakeSpikes*`), rubble shower, shadow hands; leaves by `timeLeft` | Generic, 4-shot | Frostbitten lure, Frozen Wasteland only ✅ | Drops + 2 pool | — | ◐ |
| B9 | Fission Tyrant (`ContentCreatures.cs:216`) | Gate / 1150 / 24 / ground | Wall of Flesh (aiStyle 27-29, `SpawnWOF`): a wall the full height of the world sweeping one way, laser eyes, mouth with Hungry, tongue pulls a fleeing player; never despawns | Generic walk at 0.8×, 4-shot. Leaves when the player dies | Core igniter, Irradiated Core, after the Scout Mech ✅ (`Hardmode.cs:138-145`) | Core breaker + slag; **empty pool** (no emblem analog, G8) | Leaves on death (Wall of Flesh does not) | ❌ |
| B10 | Cyber-Swarm Leader (`Actors.cs:479`) | Hard / 1400 / 26 / flyer | The Destroyer (inferred; `AI_037_Destroyer`): 80-segment worm, Probes, segment lasers, flees at day | Generic hover, 5-shot | Swarm resonator (no night rule); natural dusk arrival after the Fracture ✅. Radiated-heart avenger after the Fracture | Drops + 4 pool + blight essence | Dusk version leaves at dawn; item version stays (probe) | ❌ |
| B11 | Renegade Overseer (`Actors.cs:481`) | Hard / 1700 / 30 / ground | Skeletron Prime (inferred; aiStyle 31-35): head + saw, vice, cannon and laser arms, spin, flees at day | Generic walk, 3-shot | Renegade credentials (no night rule); dusk arrival ✅ | Drops + 4 pool + prism essence | As B10 | ❌ |
| B12 | Warship Core (`Actors.cs:487`) | Hard / 2600 / 34 / flyer | The Twins (inferred; aiStyle 30/31): two eyes, lasers and cursed flames, dashes, phase 2 at 40%, flee at day | Generic hover, 5-shot, range 15 | Warship transponder (no night rule); dusk arrival ✅ | Drops + 2 pool + flight essence | Item version stays at dawn (probe) | ❌ |
| B13 | Abomination (`Actors.cs:485`) | Hard / 2300 / 34 / ground | No twin | Generic, 4-shot, phase at 60% | Tainted tissue, Fracture-gated ✅ | Drops + 1 pool | — | ➕ |
| B14 | Bloom Matriarch (`ContentCreatures.cs:193`) | Hard / 1800 / 26 / flyer | Queen Bee (aiStyle 43): horizontal charges, bee swarms, stinger rain; post-Fracture is a deliberate change (G2) | Generic hover, 5-shot | Royal nectar (no biome rule) or breaking a bloom hive; Fracture-gated ✅ | Drops + 5 pool; unlocks the Arborist (`World.cs:234`) | — | ◐ |
| B15 | Prism Empress (`ContentCreatures.cs:195`) | Hard / 2600 / 32 / flyer | Empress of Light (`AI_120_HallowBoss`): bolts, lances, sun dance, everlasting rainbow, dashes; phase 2 at 50% (`_IsInPhase2`); one-shots by day (`_IsGenuinelyEnraged`, `Main.dayTime`) | Generic hover, 7-shot, range 14. No day rule | Prism catalyst (no biome or time rule) or a crystal altar; Fracture-gated ✅ | Drops + 7 pool | — | ◐ |
| B16 | Gel Sovereign (`CreatureCoverageData.cs:137`) | Hard / 2300 / 30 / ground | Queen Slime (`AI_121_QueenSlime`): hops, ground pound, crystal minions; phase 2 flies (`_FlyMovement`) | Generic walk, 6-shot | Prism gel lattice in the spires after the Fracture ✅ | Drops + 2 pool | — | ◐ |
| B17 | Mutated Flora (`Actors.cs:483`) | Hard / 1900 / 30 / **ground** | Plantera (aiStyle 51/52): flies on vine hooks attached to tiles; seeds and thorn balls; phase 2 at 50% with tentacles; enraged outside the jungle | Generic **walk** at 0.55×, 7-shot. A ground walker cannot climb the jungle caves it spawns in | Canopy bulbs after the trio ✅; Mutagen bloom gated on the trio ✅ | Drops incl. vault key ✅ + 2 pool | — | ◐ |
| B18 | Vault Colossus (`ContentCreatures.cs:197`) | Hard / 3400 / 38 / ground | Golem (`AI_045_Golem` + `AI_047_GolemFist`): body jumps, 2 punching fists, head detaches and fires lasers; enraged outside the temple (`ZoneLihzhardTemple`) | Generic walk at 0.7×, 5-shot | Vault core needs `vault.opened` ✅ (`Hardmode.cs:159`); vault trigger (`WorldGenContent.cs:145`) | Drops + 3 pool; `ritual.ready` ✅ | — | ❌ |
| B19 | Void Cantor (`ContentCreatures.cs:218`) | Hard / 3200 / 36 / flyer | Lunatic Cultist (`AI_084_LunaticCultist`): teleports, fireballs, ice mist, lightning orbs, ancient light, phantasm dragons, clone ritual (find the real one) | Generic hover, 6-shot | Ritual at the dungeon gate after the Colossus ✅ (`Hardmode.cs:443-454`); Void hymnal gated ✅ | Drops; **empty pool** | — | ◐ |
| B20 | Astro-God (`Actors.cs:532`) | Final / 6000 / 45 / flyer | Moon Lord (`AI_077-081`): core shielded until both hands and the head die; true eyes; phantasmal deathray, bolts, spheres; leech | One body, generic 8-shot fan, range 18. Probe: 720 damage per 8 s, max hit 90 | Astral sigil gated on the obelisks ✅ (`Hardmode.cs:155`); 60 s countdown ✅ (`World.cs:401-406, 482-494`) | Drops + 9 pool + 20 astral ore | — | ❌ |
| B21 | Moon Eater (`ContentCreatures.cs:199`) | Event / 1600 / 28 / flyer | Blood Moon leader (Dreadnautilus, `AI_117_BloodNautilus`): dashes, blood bolts, spin | Generic | Rusted lure (works by day ⚠ probe); Rust Moon finale | Drops + 2 pool | Item version stays at dawn (probe) | ◐ |
| B22 | Eclipse Lord (`ContentCreatures.cs:201`) | Event / 2200 / 34 / flyer | Mothron (eclipse): flies, charges, lays eggs | Generic | Eclipse orb (no day rule); Dark Sun finale | Drops + 5 pool | Stays past dawn (probe) | ◐ |
| B23 | Scavenger Warlord (`ContentCreatures.cs:203`) | Event / 1100 / 24 / ground | Goblin army leader (the Goblin Summoner role) | Generic | Warlord banner; Scavenger Raid finale | Drops + 4 pool | — | ◐ |
| B24 | Pirate Dreadnought (`ContentCreatures.cs:205`) | Event / 2600 / 34 / flyer | Flying Dutchman (multi-part: 4 cannons, aggregate bar) | Generic one body | Dreadnought flag; Pirate Boarding finale | Drops + 4 pool | — | ◐ |
| B25 | Protocol Dragon (`ContentCreatures.cs:207`) | Event / 2800 / 36 / flyer | Betsy (`AI_110_Betsy`): fireball breath, flame wave, wing gusts, summons wyverns | Generic | Protocol key; Defense Protocol finale | Drops + 4 pool | — | ◐ |
| B26 | Mothership Core (`ContentCreatures.cs:226`) | Event / 3200 / 38 / flyer | Martian Saucer (types 393-395: turrets + core, deathray; aggregate bar) | Generic, 8-shot | Mothership beacon; Probes finale | Drops + 6 pool | — | ◐ |
| B27 | Abyss Leviathan (`ContentCreatures.cs:213`) | Event / 3000 / 36 / flyer | Duke Fishron (`AI_069_DukeFishron`): dashes, bubbles, sharknadoes; phases at 50% and 15%; enraged outside the ocean | Generic. No ocean rule | Abyss bait fished in acid ✅ (`Fishing.cs:200-224`). No summon item (deliberate) | Drops + 2 pool | — | ◐ |
| B28 | Hollow Regent (`CreatureCoverageData.cs:139`) | Event / 3400 / 40 / flyer | Pumpking (Pumpkin Moon wave boss; scythes, flame; has a bar) | Generic | Hollow Moon wave 8; Regent's wick at night after the Fracture ✅ | Drops + 2 pool | Item version stays at dawn (probe) | ◐ |
| B29 | Rime Seraph (`CreatureCoverageData.cs:141`) | Event / 3600 / 42 / flyer | Ice Queen (Frost Moon; ice shards and frost waves; has a bar) | Generic | Rime Moon wave 8; Seraph frost-core at night after the Fracture ✅ | Drops + 2 pool | Stays past dawn | ◐ |
| B30 | Solar Flare Entity (`Actors.cs:491`) | Event / 1300 / 26 / flyer | Rimtera dusk incursion | Generic; Burn bolts | Sunspot lens; dusk incursion after an easy boss, pool gated by HP before the Fracture (`GameSimulation.cs:2447-2455`) | Drops + 4 pool | Leaves at dawn ✅ (probe) | ➕ |
| B31 | Void Stalker (`Actors.cs:493`) | Event / 1200 / 30 / flyer | Rimtera dusk incursion | Generic | Void lure; incursion | Drops + 4 pool + void essence | Dawn ✅ | ◐ (part of the ➕ incursion set; marked ◐ for the shared gaps) |
| B32 | Asteroid Miner Mech (`Actors.cs:495`) | Event / 1500 / 28 / ground | Rimtera dusk incursion | Generic | Claim marker; incursion | Drops + 3 pool | Dawn ✅ | ◐ |
| B33 | Cyber-Pirate Captain (`Actors.cs:497`) | Event / 1250 / 26 / ground | Rimtera dusk incursion | Generic | Bounty notice; incursion | Drops + 4 pool | Dawn ✅ | ◐ |

Note on B31-B33: these are Rimtera-only bosses like B30. They count as ◐ only because they share A2's single pattern. If you decide that four dusk bosses with one pattern each is fine, recount them as ➕.

Per-boss damage over 8 s against an idle, unarmoured player healed every tick (probe, Classic): 147 (Mining Drone) to 720 (Astro-God). Largest single hit: 18 (Drone) to 90 (Astro-God, under the 100 cap). Every Easy boss deals 150-230 per 8 s. That is lethal in about 4-5 s to a 100-HP player without armour. Check this against the tier-armour table from `--item-census` in the combat area.

## C. Mini-bosses and obelisks

| # | Unit (pointer) | Analog | Terraria | Rimtera | Status |
|---|---|---|---|---|---|
| C1 | Crypt paladin (`CreatureCoverageData.cs:69`) | Paladin | Rare post-Plantera dungeon spawn; throws a hammer; tanky | Rare `dungeon-hard` spawn, 1250 HP, default ground AI (no hammer), rare-weapon drop | ◐ |
| C2 | Hollow lancer, Ember rootwalker (`:71-73`) | Headless Horseman, Mourning Wood | Pumpkin Moon wave mini-bosses; Mourning Wood fires flaming wood; both have bars | Wave units, default AI, no bar | ◐ |
| C3 | Frostspire pine, Cryo tank (`:80-81`) | Everscream, Santa-NK1 | Frost Moon mini-bosses with bars; ornament and laser/rocket patterns | Stationary or default walker, no ranged pattern, no bar | ◐ |
| C4 | Blizzard colossus, Grit elemental, Corrosion nautilus, Scavenger hexer (`CreatureCoverage.cs:141-147`; spawned once by `SpawnEventMiniBosses`, `GameSimulation.CreatureCoverage.cs:131-150`) | Ice Golem, Sand Elemental, Blood Eel / Dreadnautilus role, Goblin Summoner | Mid-event elites with their own attacks | Arrive once after the Fracture when event progress passes 0.25-0.4 ✅. Default AI | ◐ |
| C5 | Mini-boss loot (`ArsenalItems.Coverage.cs`) | — | A rare weapon each | A rare coverage weapon each ✅ | ◐ (grouped with C1-C4 for the missing AI) |
| C6 | Obelisks ×4 (`ContentCreatures.cs:220-227`; shields `Hardmode.cs:476-490`) | Lunar pillars | Shielded until 100 kills of that pillar's enemies (knowledge); each pillar has its own enemies and an aggregate bar showing the shield | 4200 HP, stationary, shielded until `ObeliskShieldKills = 20` (`WorldEvents.cs:122`); 3 natives each ✅. Not `IsBoss`, so no bar and no shield readout | ◐ |
| C7 | Astral countdown after the obelisks (`Hardmode.cs:492-503`) | Moon Lord countdown | 60 s after the last pillar | 60 s ✅ | ◐ (counted with C6 because there is no bar or visible countdown text beyond messages; `DrawEventBars` shows the countdown, so this is close to ✅) |
| C8 | Mimics (cache, urn, strongbox, frozen, junk, lead-box, cargo) | Mimics (big mimics are hardmode mini-bosses) | Big biome mimics jump, dash, and drop a biome weapon | `Mimic` family ✅ (enemies area); no "big mimic" boss-tier unit | ◐ |

---

## Integration steps

Sizes: S ≈ under a day, M ≈ 1-3 days, L ≈ a week or more. "Self-test" means a new `Check` in `Systems/*SelfTests.cs`.

**BO-1 · Boss pattern framework (the base for most steps below).**
Change: replace the single `UpdateBossController` with data-driven attack patterns. Add `BossPattern` records (Dash, HoverShoot, Fan, Ring, Slam/Shockwave, Teleport, SummonMinions, Beam, Spin, Charge) and a per-boss sequence per phase. Keep A2 as the fallback for units that declare no pattern.
Files: new `Systems/BossPatterns.cs`; `Systems/GameSimulation.cs` (`UpdateEnemies`, `UpdateBossController`, `FireBossVolley`); `Entities/Actors.cs` (`EnemyArchetype.Patterns`, `PhaseThresholds[]`).
Size: L. Dependencies: Enemies area (shared AI-family code), Combat (projectile kinds and damage).
Acceptance: self-test spawns each boss for 20 s and records a behaviour signature (pattern ids used, peak speed, minions spawned, projectile count). No two bosses share a signature. Each boss uses at least 2 patterns per phase, and phase 2 adds at least 1 new pattern.

**BO-2 · Let bosses use AI families (quick fix).**
Change: remove the `!enemy.IsBoss` guard at `GameSimulation.cs:1642`, or route boss families through BO-1, so the Fallout Hive-mind's `DivingFlyer` runs.
Files: `Systems/GameSimulation.cs`, `Systems/EnemyAiFamilies.cs`. Size: S. Dependencies: none.
Acceptance: self-test shows the Hive-mind diving (vertical speed above the hover speed at least once in 10 s).

**BO-3 · Worm bosses.**
Change: segmented bosses with shared HP (`realLife`-style) for the Fallout Hive-mind (EoW analog, may split) and Cyber-Swarm Leader (Destroyer analog, drones from segments). Reuse the `Worm` family segment code.
Files: `Systems/EnemyAiFamilies.cs`, `Entities/Actors.cs` (`SegmentCount` on boss rows), `UI/ActorAnimator.cs`/sprite atlas for segments.
Size: L. Dependencies: BO-1, BO-2, Presentation (segment sprites).
Acceptance: the boss has at least 20 segments; damage to any segment lowers the shared bar; the boss burrows through tiles; the kill drops loot once.

**BO-4 · Multi-part bosses.**
Change: a parent/child part model (body + hands, fists, cannons, eyes). Parts have their own HP and patterns. The parent is invulnerable until the required parts are dead. Targets: Scout Mech (2 hands), Renegade Overseer (4 arms), Warship Core (twin core), Vault Colossus (2 fists + detachable head), Astro-God (2 hands + head before the core), Pirate Dreadnought and Mothership Core (turrets).
Files: `Entities/Actors.cs` (`BossPartRow`, `EnemyAgent.ParentId`), `Systems/GameSimulation.cs`, save code in `GameSimulation.EntitiesSave.cs`.
Size: L. Dependencies: BO-1, BO-10 (aggregate bar), Presentation.
Acceptance: self-test shows the Astro-God core takes 0 damage until both hands and the head die, then takes damage. Saving mid-fight and loading keeps the part links.

**BO-5 · Boss minions.**
Change: a `SummonMinions` pattern with per-boss minion kinds and caps (Overseer: drones; Alpha Skitter: swarmers on hit; Bloom Matriarch: hive drones; Plant Overgrowth: orbiting spore pods; Fission Tyrant: core wisps; Void Cantor: void wyrm).
Files: `Systems/BossPatterns.cs`, `Entities/Actors.cs`. Size: M. Dependencies: BO-1, Enemies area (minion archetypes).
Acceptance: minions appear within 15 s, stay under the cap, are removed or freed when the boss dies, and do not count toward natural spawn caps.

**BO-6 · Despawn rules per boss (closes issues 3 and 4).**
Change: add `BossLeaveRule` flags to the archetype: `AtDawn` (Overseer, hard trio, Moon Eater, Eclipse Lord stays by day, Hollow Regent, Rime Seraph, the dusk four), `OnTargetLost` (more than 120 tiles away, or out of sight for 30 s), `LeaveBiome` (Fallout Hive-mind outside radiated, Abyss Leviathan out of acid/sea → enrage instead via BO-7). Apply the rules to item-summoned bosses, not only `_nightBossIds`. When a boss leaves, show "X has left" and do not record a defeat.
Files: `Systems/GameSimulation.Spawning.cs` (`UpdateBossesAtDawn`, `CanDespawn`), `Systems/GameSimulation.Hardmode.cs:268-280`, `Entities/Actors.cs`.
Size: M. Dependencies: Events area (event bosses end with their event).
Acceptance: rerun the probe. The Overseer, Moon Eater, Hollow Regent, Rime Seraph, Cyber-Swarm Leader and Warship Core summoned at 02:00 are gone at 07:00. A boss 188 tiles away is gone within 30 s. A summon item works again right after.

**BO-7 · Enrage rules.**
Change: `BossEnrageRule` (Daytime, OutsideZone(zone), OutsideArena(rect)) with stat multipliers and a pattern swap. Prism Empress by day; Scout Mech by day; Vault Colossus outside the vault; Mutated Flora outside the canopy; Abyss Leviathan out of liquid or off the coast; Bloom Matriarch outside the canopy or hive.
Files: `Systems/BossPatterns.cs`, `Systems/GameSimulation.cs`. Size: M. Dependencies: BO-1, World area (zone queries).
Acceptance: self-test shows the Prism Empress at noon dealing at least 2× damage with an "enraged" message, and the Vault Colossus enraged when the player leaves the vault rectangle.

**BO-8 · Summon refusal table (closes issue 5).**
Change: one data table `BossSummonRules` {NightOnly, DayOnly, Zone, NearTile, Gate}. Night: Reactor key, Swarm resonator, Renegade credentials, Warship transponder, Rusted lure, Eclipse orb (day, like the Solar Tablet). Zone: Rotting seed → Overgrowth, Fallout hive cell → Radiated Deeps, Spore crown → Fungal Hollows, Royal nectar → Bloom hive/Canopy, Prism catalyst → Crystal Spires, Scout beacon → the dungeon entrance at night. Fold in the existing refusals from `HardmodeSummonRefusal`/`CoverageSummonRefusal`. Change the "already here" check to the same boss kind (Terraria's per-type rule), or keep any-boss and document it (open question).
Files: `Systems/GameSimulation.Items.cs:77-101`, `Systems/GameSimulation.Hardmode.cs:136-166`, `Systems/GameSimulation.CreatureCoverage.cs:193-203`, Guide text (`GameSimulation.Guide.cs:332`).
Size: S-M. Dependencies: Progression area (which items are which tier), UI (item tooltips list the condition).
Acceptance: the probe shows the Reactor key refusing at 08:00, the Rotting seed refusing on grassland and working in the Overgrowth. The refusal messages appear. The item is not consumed on refusal.

**BO-9 · One-boss consistency.**
Change: route every boss spawn (event finale, fishing, dusk, bulbs, ritual) through one `TrySpawnBoss` that applies the same rule. Either defer the finale until the current boss leaves, or allow concurrency and rely on BO-10 stacking.
Files: `Systems/GameSimulation.World.cs:182-188`, `Systems/GameSimulation.Fishing.cs:200-224`, `Systems/GameSimulation.cs:734`. Size: S. Dependencies: Events area.
Acceptance: probe "event host boss while boss alive" gives the documented result (deferred finale, or 2 bosses with 2 bars).

**BO-10 · Boss bar upgrade.**
Change: stacked bars (up to 3), each with a head icon (sprite atlas). An aggregate bar for multi-part bosses (sum of parts). Bars for event mini-bosses and obelisks (shield count until it breaks, then HP).
Files: `UI/GraphicalSandbox.cs:3168-3185` (`DrawBossBar`), `UI/SpriteAtlas.cs`. Size: M. Dependencies: UI and presentation area (another agent is editing UI input; touch only drawing), BO-4.
Acceptance: screenshot with 2 bosses shows 2 labelled bars. An obelisk shows "Shield 12/20". The Astro-God's bar drains through its parts.

**BO-11 · Boss potions and hearts.**
Change: every boss drops a healing item scaled by tier (Gate/Easy: lesser, Hard: standard, Event/Final: greater or super), plus a few life pickups, outside the salvage cache (Terraria drops these even in expert).
Files: `Systems/GameSimulation.cs:1693-1706` or `GameSimulation.PlayerSystems.cs:398-416`. Size: S. Dependencies: Items area (which tonic is which tier).
Acceptance: self-test shows every boss kill leaving at least 5 healing items whose tier matches the boss tier, on every difficulty.

**BO-12 · Fill the empty weapon pools.**
Change: add 2-4 tier-appropriate weapons and items each to the Fission Tyrant (plus a class-emblem analog, gap G8), Void Cantor and Fallout Hive-mind.
Files: `Systems/Content/ArsenalItems.Data.cs`/`ArsenalItems.Coverage.cs` (loot sources), sprites. Size: S-M. Dependencies: Items and Combat areas.
Acceptance: `ArsenalItems.BossPools` has at least 2 entries for all 33 bosses (extend the existing coverage self-test).

**BO-13 · Unique core-chips.**
Change: give each of the 33 core-chips one signature effect, not an `i % 5` stat (for example the Overseer's dash, the Alpha Skitter's fall immunity and bounce, the Hive-mind's on-hit spore cloud, the Fission Tyrant's +1 accessory slot, the Astro-God's gravity control).
Files: `Systems/Content/PlayerSystemsContent.cs:138-145`, gear effects (`GameSimulation.Gear.cs`). Size: L. Dependencies: Items and accessories area.
Acceptance: `--item-audit` lists a distinct effect tag per chip, and no two chips have identical stats.

**BO-14 · Per-boss music.**
Change: map boss kind or tier to music themes (at least gate/easy, hard, event, final and astro). Add style rows in `AudioEngine.cs:652`.
Files: `UI/AudioDirector.cs:112`, `UI/AudioEngine.cs`. Size: M. Dependencies: Presentation area.
Acceptance: `--music-preview` plays 5 distinct boss themes. In game, the theme matches the boss's tier.

**BO-15 · Spawn placement.**
Change: spawn off-screen about 30-50 tiles away (flyers above, ground bosses on the surface or, underground, the same layer as the player), never inside the player's view.
Files: `Systems/GameSimulation.cs:734-746`. Size: S. Dependencies: none.
Acceptance: self-test shows a boss summoned in a cave appearing in that cave band, not on the surface, and at least 30 tiles away.

**BO-16 · Respawn timer follows the bosses.**
Change: recompute the remaining respawn time when the last boss leaves (`min(remaining, base)`).
Files: `Systems/GameSimulation.Player.cs:996-1040`. Size: S.
Acceptance: probe shows at most 5 s left once the bosses are gone.

**BO-17 · Boss state on save and quit.**
Change: decide whether to keep A15. If not, drop living bosses on save (or on load) and give back nothing, as Terraria does.
Files: `Systems/GameSimulation.EntitiesSave.cs`. Size: S. Dependencies: Save system owner.
Acceptance: a save made mid-fight loads with no boss and a normal spawn rate (or the chosen rule is documented and tested).

**BO-18 · Debug summon key.**
Change: make `TrySummonNextBoss` call the same refusal path, or hide it behind a dev flag (`--kit`/creative).
Files: `Systems/GameSimulation.cs:721-732`. Size: S. Dependencies: Controls owner (do not touch input code while it is being edited).
Acceptance: with the key bound, the Warship Core cannot be summoned before the Fracture.

**BO-19 · Fission Tyrant as a wall.**
Change: a full-height moving barrier that advances steadily toward one side of the world. Its weak points fire beams. A "tether" pulls the player if they get more than N tiles ahead. It does not leave on player death; it resets instead.
Files: `Systems/BossPatterns.cs`, `Systems/GameSimulation.Hardmode.cs`. Size: L. Dependencies: BO-1, BO-4.
Acceptance: self-test shows the Tyrant moving at least 1 tile/s in one direction for 20 s regardless of the player, and the tether pulling a player who is more than 40 tiles ahead.

**BO-20 · Astro-God phases.**
Change: the hand/head/core parts (BO-4) plus beam, sphere and leech patterns. Shield the core until both hands and the head are dead.
Files: as BO-4. Size: L. Dependencies: BO-1, BO-4, BO-10.
Acceptance: as BO-4, plus a phase-change message per destroyed part.

**BO-21 · Obelisk shield count.**
Change: decide the kill quota (20 now; Terraria 100). Expose it in the boss bar (BO-10).
Files: `Systems/WorldEvents.cs:122`. Size: S. Dependencies: Events area.
Acceptance: the quota matches the decision, and the bar shows the remaining count.

**BO-22 · Mini-boss patterns.**
Change: give the 9 mini-bosses one or two patterns each through BO-1 (paladin hammer throw, rootwalker flame wood, pine ornament burst, cryo-tank rockets, colossus ice wave, hexer summon).
Files: `Systems/Content/CreatureCoverageData.cs`, `Systems/BossPatterns.cs`. Size: M. Dependencies: BO-1, Enemies area.
Acceptance: each mini-boss fires at least one ranged pattern within 10 s in a self-test.

**BO-23 · Boss balance pass.**
Change: with BO-1 in place, tune time-to-kill (target 60-180 s with tier-matched gear) and damage taken (survivable with tier armour and potions).
Files: `Entities/Actors.cs`, `ContentCreatures.cs`, `CreatureCoverageData.cs`, `Systems/Content/DefenseBalance.cs`. Size: M. Dependencies: Combat area, Progression area (expected gear per tier).
Acceptance: a new `--boss-balance` style report (or an extension of `--item-census`) shows every boss inside the target band at its progression gear tier.

**BO-24 · Behaviour self-test suite.**
Change: turn the probe used for this doc into a permanent self-test: dawn, distance, death, refusal, loot-on-difficulty and the per-boss signature check.
Files: new `Systems/BossSelfTests.cs`, register in `Program.cs` self-test list. Size: S. Dependencies: none (land it first; it guards BO-1 to BO-22).
Acceptance: `dotnet run -- --self-test` runs the boss suite. It fails today on dawn and distance, and passes after BO-6.

Suggested order: BO-24 → BO-2, BO-6, BO-8, BO-9, BO-11, BO-16, BO-18 (all S/M, fix the play-affecting issues) → BO-1 → BO-5, BO-7, BO-10, BO-15 → BO-3, BO-4 → BO-19, BO-20, BO-22 → BO-12, BO-13, BO-14 → BO-23.

---

## Cross-area dependencies

- **Enemies and creatures (03):** AI families (`EnemyAiFamilies.cs`), the Worm segment code (BO-2, BO-3), minion archetypes (BO-5), mimics (C8), knockback resistance (A38).
- **Items and combat (06):** projectile kinds and damage kinds for new patterns, boss weapon pools (BO-12), core-chip effects (BO-13), healing-item tiers (BO-11), the balance pass (BO-23), and the `BossDamageCap` interplay with defense.
- **Progression and crafting (01):** summon recipes and stations, the gates in `HardmodeSummonRefusal`, the G2/G5/G8 gaps, the dungeon gate on the Scout Mech (B6), the post-Fracture Bloom Matriarch.
- **World and tiles (05):** zone queries for refusals and enrage (BO-7, BO-8), the vault and dungeon rectangles, canopy bulbs, the ritual site.
- **Events:** event finales and mini-boss timing (`WorldEvents.cs`, `GameSimulation.CreatureCoverage.cs`), event-end cleanup for tagged bosses (BO-6, BO-9), obelisk quotas (BO-21).
- **Presentation and engine (09):** boss bar, icons, segment and part sprites, per-boss music (BO-10, BO-14). The UI input code is being edited by another agent; BO-18 must wait for it.
- **Save system:** live bosses and part links (BO-4, BO-17).

## Open questions for the user

1. **Hard-trio analogs:** is the mapping Cyber-Swarm Leader = Destroyer (worm), Renegade Overseer = Skeletron Prime (arms) and Warship Core = Twins (two bodies) right? It decides which ones get BO-3 and which get BO-4.
2. **One boss at a time:** keep Rimtera's stricter "no summon while any boss lives" rule, or switch to Terraria's per-type rule (several bosses at once, stacked bars)?
3. **Night-only summons:** should the Overseer's Reactor key and the hard-trio items be night-only like Terraria's, or stay usable any time for convenience?
4. **Bosses across save and quit:** keep them (current Rimtera behaviour), or drop them on reload like Terraria?
5. **Obelisk shield quota:** keep 20 kills (much faster than Terraria's 100), or raise it?
6. **Scope of unique AI:** do you want all 33 bosses fully bespoke (BO-1 to BO-4, several weeks)? Or bespoke AI for the progression bosses only (Overseer, Alpha Skitter, Plant Overgrowth, Hive-mind, Scout Mech, Fission Tyrant, hard trio, Mutated Flora, Vault Colossus, Void Cantor, Astro-God), with pattern presets for the event and optional bosses?
7. **Mutated Flora as a ground walker** in vertical jungle caves: make it a hooked flyer like its analog?
8. **Core-chips:** unique effects (BO-13, L) or keep the stat sticks for now?
