# Easy mode and hardmode: Terraria progression vs Rimtera

**Evidence:** REA member names from `Terraria.exe`:
- **World:** `WorldGen.StartHardmode`, `initializeHardMode`, `hardUpdateWorld`, `hardModeWorldUpdates`, `SmashAltar`, `altarCount`, `spawnHardBoss`, `GeneratePlanteraBulbOnAllMechsDefeated`, `AttemptToGeneratePlanteraBulbAt`, `AddLihzahrdAltar`, `TriggerLunarApocalypse`, `UpdateLunarApocalypse`, `GetRidOfCultists`.
- **Main and spawning:** `Main.hardMode`; `NPC.MechSpawn`, `SpawnMechQueen`, `NPC.ScaleStats_ForExpertHardmode`, `CommonMasterBossLifeReduction`, `GetAttackDamage_CappedAtMaster`, `NPCLoot_DropAltar`, `UpdateNPC_BloodMoonTransformations`.
- **Kill flags:** the `NPC.downed*` set:
  - pre-hardmode bosses
  - the three mechanical bosses and `downedMechBossAny`
  - Plantera, Golem, the Cultist
  - the four towers
  - the Moon Lord
  - the events
- **Events:** `CultistRitual`, `DD2Event` tiers, `CheckProgressPumpkinMoon` / `CheckProgressFrostMoon`, `LunarShieldPower*`, `MoonLordCountdown`.

Only names and structure are used. Every Rimtera boss, item, name and number is original.

## Terraria's progression, as structure
1. **Easy mode:**
   - Bosses are fought in a loose order by summon item or natural spawn. Killing the world-evil boss unlocks meteors and the goblin invasion; the dungeon boss opens the dungeon.
   - The world evil spreads only through grass.
2. **Gate:** the underworld boss, summoned in the bottom band, triggers `StartHardmode`:
   - two diagonal V stripes carve through the world, the world evil on one side and the Hallow on the other
   - the Hallow spreads and competes with the evil
   - an announcement plays
3. **Hardmode world:**
   - spread is faster and reaches underground
   - altars break only with the special hammer the gate boss drops
   - each smash seeds the next of three hardmode ore tiers, and later smashes yield less
   - smashing summons a hostile spirit
4. **Hardmode spawns:**
   - every biome gets a stronger roster of new enemies, not scaled copies
   - mimics appear underground
   - underground evil and Hallow enemies drop soul materials (light and night)
   - flying sky enemies drop flight souls, used for wings
5. **Hardmode crafting:** new anvil and forge tiers built from hardmode bars craft hardmode gear.
6. **Mechanical tier:**
   - three bosses summoned with items made from hardmode bars plus souls
   - each can also spawn on its own at night once the player is strong enough, with a warning message
   - they drop the soul materials for top-tier hardmode gear
7. **Chain after the mechanical bosses:**
   - all three defeated → bulbs grow in the underground jungle
   - jungle boss → temple key → temple boss
   - then the dungeon cultist → four lunar pillars (shielded until enough of their creatures die)
   - → the final boss after a countdown
8. **Hardmode events:**
   - pirate invasion
   - solar eclipse (random day)
   - two seasonal moon events summoned with items, scored in waves
   - Martian invasion
   - harder tiers of the defence event
   - Blood moons get stronger creatures
9. **Hardmode town NPCs:** some only move in after particular bosses fall.
10. **Difficulty scaling:**
   - expert and master difficulties scale enemy health and damage
   - hardmode gives scaled expert stats
   - bosses' damage has a ceiling

## Original diff (pre-hardmode pass)

> The table below records the state before the hardmode pass. The authoritative current status is “Status (after the hardmode pass)” near the end of this document.

| # | Element | Rimtera (before) | Original status |
|---|---|---|---|
| 1 | Easy bosses, summon items, dungeon gated by the gate boss (StoneSentinel) | 4–5 Easy bosses, dungeon guardian | ✅ |
| 1 | Evil spread is grass-only before the gate | blight spreads on grass only before the Fracture | ✅ |
| 1 | Unlocks after the world-evil boss (meteors, first invasion) | events exist, gating unclear | ◐ |
| 2 | Gate boss summoned in the bottom band | the Fracture fires on "the last easy boss defeated", not a core boss | ◐ |
| 2 | V stripes of evil and Hallow | two diagonal blight scars; a static Crystal Spires zone stands in for the Hallow | ◐ |
| 2 | The Hallow spreads and competes with the evil | crystal does not spread | ❌ |
| 3 | Faster spread that reaches underground | spread is faster after the Fracture | ◐ (verify underground) |
| 3 | Altars need a special hammer | altars only need the Fracture | ❌ |
| 3 | Smashes cycle three ore tiers with less yield each time, and summon a spirit | smashes seed high-tier ore | ◐ |
| 4 | A hardmode spawn roster per biome | no Fracture gating found in spawn tables | ❌ |
| 4 | Mimics underground only in hardmode | Cache mimic exists | ◐ |
| 4 | Soul materials (light, night, flight) from biome and sky enemies | none | ❌ |
| 5 | Hardmode anvil and forge tiers | station tiers exist (Mk2 machines) | ◐ (verify gating) |
| 6 | Three summoned mechanical-tier bosses, natural night spawn with warning, soul drops | Hard-tier bosses exist | ◐ (summon items, natural spawn and soul drops to check) |
| 7 | Chain: bulbs after all three → jungle boss → temple key → temple boss → cultist → pillars → final | Event bosses plus the Astro-God final | ◐ (no gated chain) |
| 8 | Hardmode events and stronger blood moons | events exist | ◐ (hardmode tiers missing) |
| 9 | NPCs who arrive after bosses | 16 town NPCs with conditions | ◐ |
| 10 | Difficulty and hardmode stat scaling, boss damage ceiling | difficulty scaling exists | ◐ |

## Plan (original Rimtera design)
1. **Gate boss in the core:**
   - The Fracture is triggered by defeating a Core gate boss summoned in the Irradiated Core, by an original item or ritual.
   - It drops a **core breaker** hammer, the only tool that can smash altars.
   - The old "last easy boss" trigger becomes this boss's requirement.
2. **Fracture world change:**
   - the blight scar plus a mirrored prism scar in a V
   - Crystal Spires become a spreading Prism biome that converts tiles and competes with the blight
   - spread reaches underground
3. **Altars:**
   - smashing needs the core breaker
   - each smash cycles three hardmode ore tiers (original ores, or existing high tiers) with less yield each time
   - each smash spawns a hostile wraith-like core spirit
4. **Hardmode rosters:** after the Fracture, every biome's spawn table adds a stronger roster, either existing hard creatures or kitbashed variants.
5. **Soul materials:** three original essences (prism, blight, flight) dropped by underground prism or blight enemies and by sky enemies. They're used in wings, hardmode summon items and top gear.
6. **Hardmode stations:** a hardmode anvil and forge tier, crafted from hardmode bars, required for hardmode gear recipes.
7. **Hard tier bosses:**
   - three (or more) summoned by items made from hardmode bars plus essences
   - each can also spawn on its own at night after the Fracture, with a warning
   - each drops essences
8. **Post-hard chain:**
   - all three Hard bosses down → canopy bulbs grow
   - the bulb boss → a vault key that opens the precursor vault temple
   - the vault boss (Vault Colossus) → a cultist-style ritual at the dungeon
   - → four pillar events, each shielded until its creatures are killed
   - → the Astro-God final boss, after a countdown
9. **Hardmode events:**
   - harder tiers of the existing invasion and moon events
   - stronger blood-moon creatures
   - a solar-flare eclipse analog with hard creatures
10. **NPCs:** two or three town NPCs gated on later bosses.
11. **Scaling:**
    - hardmode multiplies regular enemy health and damage per difficulty
    - boss damage has a ceiling
    - expert hardmode gets its own scale

## Status (after the hardmode pass)
Code: `Systems/GameSimulation.Hardmode.cs` (progression logic), `Systems/Content/HardmodeContent.cs` (items, recipes),
`Systems/BiomeSpawnTables.cs` (hard rosters), `Systems/WorldEvents.cs` (obelisk sieges), `Systems/HardmodeSelfTests.cs` (11 checks).
Every chain step is a world flag, so saves persist it.

| # | Element | Rimtera now | Status |
|---|---|---|---|
| 1 | Easy bosses, summon items, dungeon gate | unchanged | ✅ |
| 1 | Evil spreads only through grass before the gate | unchanged | ✅ |
| 1 | Unlocks after the world-evil boss | hearts unlock raids and meteors (flags); not re-audited in this pass | ◐ |
| 2 | Gate boss summoned in the bottom band | **Fission Tyrant** (`EnemyKind.FissionTyrant`, BossTier.Gate, 1150 HP), summoned by the **Core igniter** (uranium rods + caustic shards + a heart fragment at Power Forge Mk2). The igniter only works inside the Irradiated Core (zone or bottom 20% of the world) and only after the Scout Mech (the last easy boss, `GameSimulation.LastEasyBoss`) has fallen. Its death calls `TriggerFracture`; the Scout Mech no longer does. It is in `BossProgression` after the Scout Mech | ✅ |
| 2 | V stripes of evil and Hallow | right arm = the world's blight, left arm = a mirrored prism scar (Crystal Spires tiles: prismatic grass, pearl stone, pearl sand); both respect purifier beacons. `FractureCellsConverted` and `FracturePrismCells` count them | ✅ |
| 2 | The Hallow spreads and competes with the evil | prism (Crystal Spires) spreads through `WorldTicker` after the Fracture, converting natural ground and blight, while blight converts it back | ✅ |
| 3 | Faster spread that reaches underground | blight takes rock, sand and ice after the Fracture (3 tries per update, ×1.5 updates); prism converts rock into pearl stone at any depth (tested underground) | ✅ |
| 3 | Altars need a special hammer | the **Core breaker** (tool power 75, tier 4) drops from the Fission Tyrant; mining an altar needs a drill of at least that power (`GameSimulation.CanSmashAltars`), and ordinary drills get a refusal. The debug/test `ForceBreakTile` still only checks the Fracture | ✅ |
| 3 | Three ore tiers with less yield each time, and a spirit | smashes cycle viridium → lumenite → voidsteel (`AltarOreCycle`); veins shrink 20% per full cycle (floor 35%, `AltarYieldFactor`). Each smash releases a hostile **fission wisp** (core spirit), plus the old 1-in-3 vengeful wisp. Voidsteel (and the `ore.voidsteel` flag) now comes from the third smash. Viridium also generates in deep rock before the Fracture, so hardmode tier 1 is not exclusive | ◐ |
| 4 | A hardmode roster per biome | `BiomeSpawnTables.HardTables`: all 15 zones (Grassland and Caverns included) get a stronger roster of existing hard creatures, each with at least one kind new to that biome. These take 55% of rolls after the Fracture. No kitbashed variants were needed | ✅ |
| 4 | Mimics underground only in hardmode | the cache mimic is only in the hard Caverns roster; the ambient family spawner turns it into a boulder hopper before the Fracture | ✅ |
| 4 | Soul materials | **Prism essence** (underground Crystal Spires creatures), **blight essence** (underground overgrowth or radiated-deeps creatures, plus the hard blight kinds) and **flight essence** (flyers killed in the open sky above the surface). 45% drop chance after the Fracture; event creatures excluded | ✅ |
| 5 | Hardmode anvil and forge tiers | reused: the **Alloy Anvil** (viridium bars) is the hardmode anvil for tier-4+ gear; the **Void Forge** (lumenite bars + void essence) is the hardmode forge that smelts voidsteel and builds tier-6 gear. Tier-6 generated gear also needs 3 prism + 3 blight essence | ✅ |
| 6 | Three summoned mechanical-tier bosses | Cyber-Swarm Leader, Renegade Overseer and Warship Core (`GameSimulation.HardTrio`). Their summons are now made from hardmode bars plus essences at the Alloy Anvil and refuse before the Fracture. At dusk after the Fracture an undefeated one may come on its own: a 20% deterministic roll, at most once per 3 days, with a warning line, and it leaves at dawn. They drop blight, prism and flight essence (20 each) | ✅ (no player-strength check) |
| 6 | Wings need flight essence | viridium, lumenite, voidsteel and astral wingpacks need 4/8/12/16 flight essence | ✅ |
| 7 | Bulbs after all three | flag `hardmode.trio.down`. Canopy bulbs (`TileId.CanopyBulb`) grow once in the underground band of the canopy columns (whole width on small worlds). Breaking one summons the **bulb boss** (Mutated Flora); its Mutagen bloom refuses until the trio is down | ✅ (bulbs do not regrow) |
| 7 | Temple key, then temple boss | Mutated Flora now drops the vault key (moved from the Bloom Matriarch), which opens the vault door (`vault.opened`). The Vault Colossus comes from the vault altar, or from the Vault core, which refuses before the vault is open (and takes lumenite bars, so it is not made before the Fracture). The temple is **sealed** (WT-1, D-35, 0.2.2): its precursor brick and glyph need drill power 122 (Terraria's Lihzahrd brick, pickaxe power 210), above every drill before the Vault Colossus (the void auger, 120), and stay explosion-proof, so the maze and the vault door are the only way in | ✅ |
| 7 | Cultist, then pillars, then the final boss | Vault Colossus down sets `ritual.ready`. Walking within 14 tiles of the dungeon entrance (`RitualSite`) wakes the **Void Cantor** (new Hard boss; the Void hymnal also summons it after the Colossus). Its death sets `obelisks.started` and raises four **obelisks** (ember, rift, gale, shard), each running its own siege event (new `WorldEventKind`s with a kill quota). Each obelisk stays shielded (`EnemyAgent.Shielded`) until 20 of its siege creatures die. All four down sets `obelisks.down` and starts the 60-second Astro-God countdown. Siege creatures spawn around the player, not around their obelisk | ✅ / ◐ |
| 8 | Hardmode events, stronger blood moons | after the Fracture, 40% of upgradable event picks become hard creatures: the Rust Moon gains abominations, corroded mechs, eclipse shades and wisps; raids gain reavers, brutes and scrap elementals; the defence protocol gains ogres, wyverns and mages; acid rain and dust storms gain heavies. The Dark Sun (solar-flare eclipse, random dawn) and Pirate Boarding were already gated on the Fracture | ✅ (no seasonal wave-moons) |
| 9 | NPCs after bosses | **Isotopist** (after the Fission Tyrant), **Aeronaut** (after any of the trio) and **Relic Warden** (after the bulb boss), each with a gated shop | ✅ |
| 10 | Difficulty and hardmode scaling, boss ceiling | after the Fracture regular creatures get ×1.35/×1.15 (Explorer), ×1.7/×1.3 (Classic) or ×2.1/×1.5 (Brutal) health and damage, applied once per creature. Bosses keep their tuning, but no boss hit can exceed 100 after difficulty (contact and volleys) | ✅ |

**Sprites:** the new sprites were kitbashed with `tools/sprites/kitbash.py` (the `_HARDMODE` recipes):
- `boss_fission_tyrant`, `boss_void_cantor`
- `creature_{ember,rift,gale,shard}_obelisk`
- `tool_core_breaker`, `item_core_igniter`, `item_void_hymnal`
- `item_{prism,blight,flight}_essence`
- `deco_canopy_bulb`

**Not covered:** the debug challenge key (`TrySummonNextBoss`) still walks the boss list without the summon gates.

**Corrections (PC-21, 0.2.2):**
- The Power Forge Mk2 (row 5) is also mined as the Reactor forge before the Overseer; only the crafted copy needs the Overseer circuit.
- Duke Fishron's analog, the **Abyss Leviathan**, now waits for the Fracture (PC-3): abyss bait in acid does nothing before
  it (the bait is kept, with a hint), and the bait takes flight essence.
- The dusk incursions that "wait for the Scout Mech" admitted every incursion boss before the Fracture. PC-4 (stream C)
  gates them; until then `--craft-audit` lists them under "Bosses reachable before the Fracture".
- PC-1..4 status: PC-3 is closed in 0.2.2 (stream B); PC-1, PC-2 and PC-4 are stream C's in the same release.
