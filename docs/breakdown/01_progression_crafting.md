# Breakdown 01: Progression and crafting

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open. An arrow gives the milestone `docs/PLAN.md` now plans the step for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "Not done, not scheduled" marks steps of parity 0.1.7 and 0.1.8 that no release shipped and no milestone holds yet: release 0.1.7 did not carry parity step 0.1.7's fix list. "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

A read-only audit of the whole player journey (from a fresh character to the endgame) against Terraria. It covers starting kits, stat growth, stations and recipes, ores and tool power, the boss chain and its gates, town NPC unlocks, and difficulty and death rules. No game code was changed.

**Method.** Every row was checked against the code on `main` (with the working tree as of 2026-10-28), not against the older diff docs. Live data came from `dotnet run -- --craft-audit` and `--craft-audit --items`, run on a private build. Terraria behaviour is cited as **(evidence)** where it comes from `docs/terraria-evidence/full/` or from IL dumped with its `scripts/ilall.py`, and as **(knowledge)** otherwise. Methods dumped for this audit:
- `Player.GetRespawnTime`, `Player.DropCoins` and `Player.DropItems`
- `Player.IsItemSlotUnlockedAndUsable` and `Player.GetAmountOfExtraAccessorySlotsToShow`
- `Main.AnyPlayerReadyToFightKingSlime`
- `WorldGen.StartHardmode`
- `Player.ItemCheck_UseLifeCrystal` and `Player.ItemCheck_UseLifeFruit`

**Status key:** ✅ matches | ◐ partial | ❌ missing | ⚠ broken or a leak | ➕ Rimtera-only (deliberate).

---

## Summary

**Counts (107 features):** ✅ 56 · ◐ 32 · ⚠ 9 · ❌ 3 · ➕ 7

Nothing hard-blocks play today. The craft audit reports 0 unreachable items, 0 blocked milestones and 0 unbuildable stations. Issue 1 below is a **soft block**: a new player can meet a boss stronger than the hardmode gate boss before their first boss fight.

**Top 10 issues by player impact**

| # | Issue | Status | Step |
|---|---|---|---|
| 1 | **The Rust Moon spawns the Moon Eater (1600 HP) before any boss.** The event unlocks at 120 max life (`WorldEvents.cs:435-440`), and after 25 event kills its final boss spawns (`WorldEvents.cs:348-352`). That boss has more life than the Fission Tyrant (1150), the hardmode gate boss. It also drops 6 void essence, a tier-6 material. Terraria's pre-hardmode Blood Moon has no boss. | ⚠ soft block | PC-1 |
| 2 | **Pre-hardmode mining has no gates.** Every pre-Fracture ore, including pyrocite (the hellstone analog), obsidian and the hardmode-tier **viridium**, needs power 35. That is the starting drill's power (`TileGrid.cs:662-673,693`; `GearCurve.DrillPower(1)=35`). Only meteorite (50) is gated. The cobalt, glacial, uranium, meteorite and pyrocite drills (41-80) unlock no new ore. | ⚠ | PC-5 |
| 3 | **Hardmode materials are reachable before the Fracture:** viridium ore and bars, and void essence. Viridium veins are generated with the world, and its bars smelt at the Ore Refinery Mk2, which the audit reaches at stage 9, before any boss. Void essence comes from deep chests, slag chests, dungeon chests, dungeon creatures and the Moon Eater. Together they unlock tier-4 to tier-6 ammo and food and the Abyss bait. | ⚠ | PC-6 |
| 4 | **Big bosses are summonable before the Fracture with no gate:** the Protocol Dragon (2800 HP, from the Signal core at the Research Terminal), the Abyss Leviathan (3000 HP, from Abyss bait) and the Moon Eater (from its lure at the Chem Lab). After the Scout Mech, dusk incursions also bring event bosses of 1200-1500 HP before the Fracture. All of these outclass the gate boss and can drop permanent upgrades early. | ⚠ | PC-1..PC-4 |
| 5 | **The craft audit cannot see pre-Fracture boss leaks.** It always takes the lowest-`Order` ready milestone, so the Fission Tyrant (index 5) fires before the Spore Monarch, the Abyss Leviathan, event bosses and others. Their loot is then dated after the Fracture, and the "97 tier-4+ items before the Fracture" figure is an undercount (`CraftingAudit.cs:286-287,787`). | ⚠ tooling | PC-7 |
| 6 | **A Classic death destroys half of every material stack** (`GameSimulation.cs:2561-2570`), while the creation screen says "Drop part of your carried materials" (`WorldSettings.cs:124`). Terraria Softcore keeps all items and drops 50% of coins (evidence: `DropCoins`). | ⚠ | PC-8 |
| 7 | **The power curve is compressed.** The life cap is 300 (cores ×10 + lifebloom ×20), against Terraria's 500. Every boss hit is capped at 100 (`Hardmode.cs:23`). Boss life grows 11.5× from the first boss to the final boss, while weapon damage grows 14×, so the Astro-God dies in fewer hits than the Overseer. In Terraria the final boss takes about 2.6× the first boss's time to kill. | ◐ | PC-12, PC-15 |
| 8 | **There are at most 6 accessory slots (Terraria allows 7).** The auxiliary socket opens slot 6 in every world, including Explorer and Classic. On Annihilation the free slot makes a dropped socket useless (`GrowthItems.cs:179`). | ◐ | PC-11 |
| 9 | **Creative research is saved in world flags**, not on the character (`Creative.cs:27,52`). It is lost in every new world. Terraria keeps research per player (`CreativePowerManager.ResetDataForNewPlayer` runs at character creation, evidence). | ⚠ | PC-13 |
| 10 | **The Tinker Bench is a pre-boss station**, so 35 tier-4/5 accessory combinations plus ammo and tonics (97 items in total) arrive before the Fracture. Terraria's workshop comes from the goblin tinkerer, after the goblin army. | ◐ | PC-16 |

---

## A. Starting state and kits

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| A1 | Classic kit | Copper shortsword, pickaxe (35) and axe (evidence: `tables/starting_items.md`) | Ferrite stiletto, ferrite drill (power 35), ferrite hatchet (`Content/StarterKits.cs:16-19`, applied at `GameSimulation.cs:203-210`) | ✅ | ✅ as audited |
| A2 | Same kit for Mediumcore and Hardcore | Difficulties 0, 1 and 2 share one branch (evidence) | Explorer, Classic, Ironman and Salvager all get `StarterKits.Standard` (`GameSimulation.cs:206`) | ✅ | ✅ as audited |
| A3 | Kit prefixes | `Prefix(-1)`: random, 1 in 4 none (evidence) | `StarterKits.Roll`: 1 in 4 none (`StarterKits.cs:35-36`) | ✅ | ✅ as audited |
| A4 | Food in the kit | none | 12 nano-bananas (`Berry`) | ➕ (hunger) | ➕ kept |
| A5 | Journey kit | Iron sword, pick, axe and hammer; finch staff with the finch **pre-summoned**; 100 torches; 100 rope; a reusable Magic Mirror; grappling hook; wings worn (evidence) | Creative supply drop on **first world entry** (`GrowthItems.cs:313-327`): cobalt (iron-tier) stiletto, drill, hatchet and demolisher; ferrite drone uplink; 100 glow sticks; 100 rope; **3 consumable** recall capsules; grapple; glider wings worn (`StarterKits.cs:22-30`) | ◐ (no reusable recall, drone not pre-summoned, given per world entry rather than at creation) | ❌ PC-14 not done, not scheduled |
| A6 | Kit top-up for old saves | n/a | `TopUpStarterKit` (`CharacterFile.cs:175-190`) | ➕ | ➕ kept |
| A7 | Base life, mana, defence | 100 / 20 / 0 (evidence: `player_stats.md` §2) | 100 / 20 / 0 (`PlayerProgression.cs:350-352`) | ✅ | ✅ as audited |
| A8 | Trait life bonus | none | `ColonistStats.MaximumHealthBonus` (`PlayerProgression.cs:395`). It shifts the life-gated unlocks (Medic, Rust Moon, Overseer patrol) | ➕ | ➕ kept |
| A9 | Accessory slots at start | 5 (evidence: `IsItemSlotUnlockedAndUsable`, where slots 8 and 9 are the extra ones) | 5 (`PlayerProgression.cs:60-61`) | ✅ | ✅ as audited |
| A10 | First station from wood | Work Bench ← 10 wood by hand | Nano-Fabricator ← 10 timber or hull panels by hand. Audit: kit plus one tree gives 2 stations and 21 items | ✅ | ✅ as audited |
| A11 | Guide figure | Guide NPC | Station AI resident plus `GuideNpc` | ➕ | ➕ kept |

## B. Stat growth

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| B1 | Life crystal | +20 while below 400, so 15 uses (evidence: `ItemCheck_UseLifeCrystal @30/@57`) | Life core +10, at most 10 (`Actors.cs:91-95`, `Blight.cs:382-399`), for a cap of 200. At least 10 per million cells (`WorldGenDensity.cs:92`) | ◐ half the gain and half the cap | ❌ PC-12 → 0.5.0 |
| B2 | Life fruit | +5 from 400 to 500, hardmode jungle (evidence: `ItemCheck_UseLifeFruit`) | Lifebloom pod +5, at most 20, only after all 10 cores; grows on underground canopy ground after the Fracture (`GrowthItems.cs:41-46,141-157,261-279`). Cap **300** | ◐ cap 300 against 500 | ❌ PC-12 → 0.5.0 |
| B3 | Mana crystal | +20 to 200 (evidence) | Charge crystal +20, at most 9 = 200 (`Actors.cs:133`, `Combat.cs:108`). Sold by the travelling merchant (audit stage 0) | ✅ | ✅ as audited |
| B4 | Demon Heart slot | +1 slot, usable only in Expert+ worlds (evidence: `IsItemSlotUnlockedAndUsable` checks `extraAccessory && expertMode`) | Auxiliary socket from the Fission Tyrant on Brutal+ (`GrowthItems.cs:333-341`). It opens slot 6 in **every** world (`GrowthItems.cs:179`) | ◐ | ❌ PC-11 → 0.6.0 |
| B5 | Master-mode slot (7th) | +1 slot in Master (evidence: `CanMasterModeAccessoryBeShown`) | Annihilation gives slot 6 free, but the logic is `socket OR Annihilation`, so there is no 7th slot and the socket is wasted there | ❌ | ❌ PC-11 → 0.6.0 |
| B6 | One-use permanent buffs | Aegis Fruit +4 defence, Arcane Crystal (mana regen), Galaxy Pearl (+luck), Gummy Worm (+3 fishing), Ambrosia (mining/build speed), Artisan Loaf (+crafting range) (knowledge) | Seven bit flags (`GrowthItems.cs:12-29`): nanite marrow +4 def, coolant pearl +0.5 charge regen, fortune lattice +0.1 luck, bait gland +10 fishing, servo grease +15% mining, fabricator manual +4 station reach, Lumen accord. They drop from event bosses, always on Brutal+ and 1 in 3 otherwise (`GrowthItems.cs:60-69,343`) | ◐ luck and fishing amounts are well above Terraria's (knowledge); some sources are pre-Fracture (E11, E15) | ❌ PC-17 → 0.5.0 |
| B7 | Vital Crystal (life regen) | +life regen permanent (knowledge) | none | ❌ | ❌ PC-17 → 0.5.0 |
| B8 | Torch God's Favour | biome torches (`unlockedBiomeTorches`, evidence) | Lumen accord (Lumen Tempest, Solar Flare Entity), a biome-tinted light (`GrowthItems.cs:189-201`) | ✅ | ✅ as audited |
| B9 | Growth saved on the character | per player | `CharacterFileData` stores cores, crystals, pods, socket, upgrades and the creative-kit flag (`CharacterFile.cs:22-32,214-218`) | ✅ | ✅ as audited |
| B10 | Lifeforce potion | +20% max life (knowledge) | flat +80 (`PlayerProgression.cs:202`) | ◐ | ❌ X-CR-9 → 0.5.0 |
| B11 | Life-cap achievement | "Topped Off" (knowledge) | `full-vitality` (`Achievements.cs:62,165`) | ✅ | ✅ as audited |

## C. Crafting and stations

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| C1 | Opening by hand | Work Bench, torches, platforms | Fabricator ← timber; hull panel ← timber; platforms, ladder, torches (25 hand recipes, audit) | ✅ | ✅ as audited |
| C2 | Hand-recipe breadth | 324 by hand (evidence: `recipes.py`) | 25. Torches need ferrite ore, not gel; there is no hand campfire | ◐ | ❌ X-CR-6 → 0.5.0 |
| C3 | Furnace | 20 stone + 4 wood + 3 torches @ Work Bench | Ore Refinery: 20 basalt + 5 hull panels @ Fabricator (`ContentRecipes.cs`, audit) | ✅ | ✅ as audited |
| C4 | Anvil | 5 iron or lead **bars** | Power Forge: 12 basalt + 8 ferrite **ore** @ Fabricator | ◐ skips the smelt step | ❌ X-CR-3 → 0.5.0 |
| C5 | Hellforge analog | Found in the underworld before any boss (knowledge) | Power Forge Mk2: crafted with the Overseer circuit, **or mined from the Reactor forge** ruin. The audit reaches it at stage 6, before any boss. That is Terraria parity, but `PROGRESSION_DIFF.md` §3 wrongly calls the circuit a hard gate | ✅ | ✅ as audited |
| C6 | Hardmode anvil | Mythril anvil from hardmode bars | Alloy Anvil: 10 viridium bars + heart fragment + **3 fission slag** (Fission Tyrant) (`ContentRecipes.cs:76`) | ✅ | ✅ as audited |
| C7 | Adamantite-forge tier | Built from the Hellforge | Nano-Fabricator Mk2 (lumenite + prism) → Void Forge (lumenite + void essence) (`ContentRecipes.cs:77-78`) | ✅ | ✅ as audited |
| C8 | Ancient Manipulator | Cultist drop | Astral Assembler: voidsteel + astral ore (obelisks, after the Cantor) (`ContentRecipes.cs:79`, audit stage 71) | ✅ | ✅ as audited |
| C9 | Tinkerer's Workshop timing | Sold by the goblin tinkerer after the goblin army (knowledge) | Tinker Bench: 6 ferrite bars + 10 hull panels @ Fabricator, before any boss (`ContentRecipes.cs:68`); 152 recipes | ◐ | ❌ PC-16 → 0.5.0 |
| C10 | Station inheritance | `Recipe.SetupTileInheritance` (evidence) | `Implied()` (`TileGrid.cs:591-605`) includes Kiln→Refinery and Bio Lab→Chem Lab. It is applied **one level only** (`CraftingSystem.cs:519-522`), so the Astral Assembler counts as Fabricator Mk2 but not as Fabricator or Ore Refinery | ◐ | ❌ PC-18 not done, not scheduled |
| C11 | Station reach | `Player.AdjTiles` box (knowledge, about 4×3) plus Artisan Loaf | 6-tile circle (`CraftingSystem.cs:27`) + 4 with the fabricator manual (`GrowthItems.cs:204`) | ◐ more generous | ❌ X-CR-7 → 0.5.0 |
| C12 | Recipe groups | `RecipeGroups.Wood` and others | `IngredientRequirement.Alternatives` (`CraftingSystem.cs:6-10`) | ✅ | ✅ as audited |
| C13 | Guide-taught recipes | The Guide shows recipes but never locks them | 5 recipes need a reveal material (`CraftingSystem.cs:95-229`) | ➕ | ➕ kept |
| C14 | Craft audit | n/a | Milestone order lets the Fracture fire before other ready pre-Fracture bosses, so leaks are dated late (`CraftingAudit.cs:286-287`) | ⚠ | ❌ PC-7 not done, not scheduled |
| C15 | Hardmode-tier gear before hardmode | Not possible (hardmode bars) | 97 tier-4+ items before the Fracture (audit; an undercount, see C14) | ◐ | ◐ X-CR-1 whitelist 0.2.0; PC-16 → 0.5.0 |
| C16 | Dye Vat | Sold by the Dye Trader | Craftable at the Fabricator, and the Pigment Trader also sells it | ◐ | ❌ X-CR-5 → 0.5.0 |

Station ladder (audit, in order): Fabricator (2) → Refinery (3) → Power Forge (4) → Power Forge Mk2 (6) → Refinery Mk2 (9) → Chem Lab, Bio Lab, Loom, Sawmill, Kiln, Cooking, Tinker, Drone Bay, Ammo Press, Charge Reactor, Research Terminal, Dye Vat (10-21) → Alloy Anvil (44, after the Fission Tyrant) → Fabricator Mk2 (46) → Void Forge (48) → Astral Assembler (71, after the Cantor).

## D. Ores, bars and tool power

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| D1 | Ore ladder | 4 pre-hardmode pairs + evil + meteor + hellstone; 3 hardmode pairs; chlorophyte; luminite | T1 ferrite/scrap → T2 cobalt → T3 glacial, uranium, pyrocite, meteorite → T4 viridium → T5 lumenite → T6 voidsteel → T7 astral (`GearCurve.cs:3-10`) | ◐ compressed, by design | ❌ PC-5 → 0.5.0 |
| D2 | Bar ratios | 3-5 ore per bar; hellstone + obsidian (knowledge) | 3:1 early, pyrocite 3 + obsidian, lumenite 4, voidsteel 4 + essence, astral 2 + lumenite + voidsteel (`ContentRecipes.cs:82-92`) | ✅ | ✅ as audited |
| D3 | Pick power curve | copper 35 … luminite 225 (evidence: `tables/tools.csv`) | 35/41/55/60 (meteorite)/80 (pyrocite)/88/103/114/128, mapped from Terraria (`GearCurve.cs:19-41`) | ✅ | ✅ as audited |
| D4 | Pre-hardmode mining gates | demonite/crimtane and obsidian 55, hellstone 65, meteorite 50, dungeon brick 65 (knowledge) | Cobalt, glacial, uranium, pyrocite, **viridium**, obsidian and geodes 35; meteorite 50 (`TileGrid.cs:662-673,693`; `ContentTiles.cs:168,183-184`; `ContentTiles.Theme.cs:26`). The kit drill mines all of them | ⚠ | ❌ PC-5 → 0.5.0 |
| D5 | Hardmode gates | mythril 110, adamantite 150 (knowledge) | lumenite 85, voidsteel 100 (`ContentTiles.cs:174-175`) = Terraria 110 and 150 through `DrillPowerFromTerraria` | ✅ | ✅ as audited |
| D6 | Altar smashing | Pwnhammer only | `altar-breaker` capability on the core breaker and later tools (`Hardmode.cs:69-89`) | ✅ | ✅ as audited |
| D7 | Altar ore seeding | Each smash seeds the next tier | Cycle viridium → lumenite → voidsteel (`Blight.cs:92,256-290`) | ✅ | ✅ as audited |
| D8 | Hardmode ore before hardmode | None exists | Viridium veins generated with the world (`LargeWorldGenerator.cs:1181`, `WorldGeneration.cs:174`; 385 per million cells, `WorldGenDensity.cs:82`); viridium ore in biome chests (audit stage 0); bars at Refinery Mk2 (audit stage 9); viridium bolts, rounds and mini-rockets at the Ammo Press | ⚠ | ❌ PC-6 → 0.5.0 |
| D9 | Souls-of-Night analog before hardmode | Hardmode only | Void essence (tier 6) from depth-3 chest finds (`WorldGenContent.cs:192`), slag chests (`:181`), dungeon chests (`Physics.cs:721`), dungeon creatures (`ContentFillData.cs:230-231`), Moon Eater (`NpcDropRegistry.cs:168`). It enables void darts and void fuel (T6), void noodles (T5) and Abyss bait | ⚠ | ❌ PC-6 → 0.5.0 |
| D10 | Flight souls | Hardmode | Flight essence after the Fracture only (audit stage 43) | ✅ | ✅ as audited |
| D11 | Crates | Hardmode crates hold hardmode ore only in hardmode | Contents gated (`Fishing.cs`), but the Astral crate itself can be caught at stage 0 | ◐ | ❌ X-CR-8 → 0.5.0 |
| D12 | Top material | Luminite + fragments | Astral ore from the obelisks and the Astro-God; Astral bar at the Assembler | ✅ | ✅ as audited |
| D13 | Meteorite | After an orb or evil boss (`spawnMeteor`, evidence) | 50% after each heart; Starfall season; power 50 | ✅ | ✅ as audited |

## E. Bosses and gates

Boss life values come from `Entities/Actors.cs:460-534`, `Entities/ContentCreatures.cs:191-216` and `Systems/Content/CreatureCoverageData.cs:135`.

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| E1 | Pre-hardmode roster | KS, EoC, EoW/BoC, QB, Skeletron, Deerclops | Overseer 520 (Gate), Alpha Skitter 650, Mining Drone 600, Plant Overgrowth 820, Scout Mech 900, Fallout Hive-mind 800, Spore Monarch 900, Hoarfrost Behemoth 1000 | ✅ | ✅ as audited |
| E2 | Natural first boss | King Slime readiness: `statLifeMax > 140 && statDefense > 8` (evidence: `AnyPlayerReadyToFightKingSlime`) | Overseer patrol: max HP ≥ 140 and defence ≥ 6, 25% per dusk, 3-day cooldown (`Hardmode.cs:325-358`) | ✅ (defence 6 against 9) | ✅ as audited |
| E3 | Dungeon gate | Skeletron (mid pre-hardmode) | Dungeon Guardian keyed to the Scout Mech (`Physics.cs:68,1168`) | ✅ | ✅ as audited |
| E4 | Evil-biome boss | EoW/BoC from orbs or a summon | Every 3rd heart: Fallout Hive-mind before the Fracture, Cyber-Swarm Leader after it; Plant Overgrowth (`Blight.cs:20,214-259`) | ✅ | ✅ as audited |
| E5 | Wall of Flesh analog | Voodoo doll in the underworld, no prerequisite boss | Core igniter refuses until the **Scout Mech** falls and the player stands in the Irradiated Core (`Hardmode.cs:136-145`) | ◐ extra prerequisite | ❌ BO-19 → 0.6.0 |
| E6 | Hardmode start | `WorldGen.StartHardmode`: sets `hardMode`, transforms the world on a thread (evidence) | Fission Tyrant death → `FractureActive`, blight and prism scars | ✅ | ✅ as audited |
| E7 | Hardmode-only optional bosses | Queen Slime, EoL and others need hardmode | `FractureGatedBosses` refuse (Abomination, Prism Empress, Bloom Matriarch) + trio + Gel Sovereign, Hollow Regent, Rime Seraph (`Hardmode.cs:93-96,146-154`; `CreatureCoverage.cs:193-203`) | ✅ | ✅ as audited |
| E8 | Natural mech spawns | `spawnHardBoss` | Trio at dusk after the Fracture, 20%, 3-day cooldown (`Hardmode.cs:300-323`) | ✅ | ✅ as audited |
| E9 | Post-mech chain | Plantera → Golem → Cultist → pillars → Moon Lord | Mutated Flora (after the trio) → vault key → Colossus → Void Cantor → 4 obelisks → Astral sigil, which needs `obelisks.down` (`Hardmode.cs:155-162`) | ✅ | ✅ as audited |
| E10 | Pre-hardmode night event boss | Blood Moon: no boss before hardmode (knowledge) | Rust Moon final boss **Moon Eater 1600 HP** after 25 kills, at 120 HP or after the Overseer (`WorldEvents.cs:435-440`). The Moon Eater lure (Chem Lab) is also ungated | ⚠ | ❌ PC-1 not done, not scheduled |
| E11 | Old One's Army tiers | Tier 1 pre-hardmode (Dark Mage); Betsy tier 3, after Golem (knowledge) | Defense Protocol always ends in the **Protocol Dragon 2800 HP** (`WorldEvents.cs:464-468`). The Signal core is crafted at the Research Terminal before any boss (audit). The dragon drops the servo grease upgrade | ⚠ | ❌ PC-2 not done, not scheduled |
| E12 | Duke Fishron analog | Hardmode bait only | Abyss Leviathan 3000 HP from Abyss bait in acid (`Fishing.cs:220-224`). No Fracture check; the bait is acid eel + void essence (D9) | ⚠ | ❌ PC-3 not done, not scheduled |
| E13 | Goblin-army boss | none before hardmode | Scavenger Raid → Warlord 1100 HP after the first heart | ◐ | ◐ unchanged since the audit |
| E14 | Dusk incursions | none | After the Scout Mech, before the Fracture: all four event bosses (1200-1500 HP, above the gate boss) (`GameSimulation.cs:2441-2457`); the "< 1000 HP" filter matches none of them | ◐ | ❌ PC-4 not done, not scheduled |
| E15 | Boss life curve | EoC 2800 → WoF 8000 → Moon Lord about 145k (knowledge); first-to-final ×52 against copper-to-lunar damage ×20 | Overseer 520 → Fission 1150 → Astro-God 6000: ×11.5 life against ×14.4 melee base (`GearCurve.cs:117`) | ◐ late bosses die faster than early ones | ◐ PC-15 in work |
| E16 | Boss hit ceiling | none | 100 at every tier and difficulty (`Hardmode.cs:23,124-130`) | ◐ | ◐ PC-15 in work |
| E17 | Hoarfrost Behemoth | Deerclops (optional) | Easy, summoned in the frozen wastes | ✅ | ✅ as audited |
| E18 | Final-boss loot | Weapon pool, luminite, portal gun, wings | 1-2 picks from the weapon pool + compass, wings, astral ore (`NpcDropRegistry.cs`, `Gear.cs:362-377`) | ✅ | ✅ as audited |
| E19 | Progression achievements | "Hardmode!", "Mecha Mayhem" and others | `crust-breaker`, `triple-threat`, `colossus-toppled`, `four-pillars-fall` (`Achievements.cs:58-61,156-160`) | ✅ | ✅ as audited |

## F. Town NPCs as a progression track

Move-in conditions live in `GameSimulation.Town.cs:94-123`, `Content/TownContent.cs:136-148` and `GameSimulation.World.cs:232-235`.

| ID | Rimtera NPC → condition | Terraria analog (knowledge unless noted) | Status | 0.2.0 status |
|---|---|---|---|---|
| F1 | Station AI → always | Guide | ✅ | ✅ as audited |
| F2 | Quartermaster → 50 credits carried | Merchant → 50 silver | ✅ | ✅ as audited |
| F3 | Medic → max HP **> 120** | Nurse → > 100, Merchant present | ◐ | ❌ PC-19 → 0.5.0 |
| F4 | Armorer → a ranged weapon other than the kit rifle | Arms Dealer → gun or bullets | ✅ | ✅ as audited |
| F5 | Head Researcher → first heart broken | Dryad → first boss | ◐ | ❌ NC-14 → 0.5.0 |
| F6 | Chief Engineer → Overseer down | Clothier → Skeletron | ✅ | ✅ as audited |
| F7 | Arborist → Bloom Matriarch (now hardmode) or a freed survivor | Witch Doctor → Queen Bee | ✅ | ✅ as audited |
| F8 | Tide Warden → carrying a fishing rod | Angler → rescued | ◐ | ❌ NC-14 → 0.5.0 |
| F9 | 8 bound survivors from day 1 in random huts (`TownNpcs.cs:192-196`, `LargeWorldGenerator.Content.cs:497-505`) | Goblin after the goblin army; Mechanic in the dungeon after Skeletron; Wizard in hardmode | ◐ no rescue is gated | ❌ PC-20 → 0.5.0 |
| F10 | Trade Consul → 2 residents, leaves at night | Painter → 8 NPCs | ◐ | ❌ NC-14 → 0.5.0 |
| F11 | Isotopist → Fission Tyrant | Wizard / Tax Collector (hardmode) | ✅ | ✅ as audited |
| F12 | Aeronaut → any trio boss | Steampunker → any mech | ✅ | ✅ as audited |
| F13 | Relic Warden → Mutated Flora | Cyborg → Plantera | ✅ | ✅ as audited |
| F14 | Field Zoologist → bestiary ≥ 10% | Zoologist → bestiary progress | ✅ | ✅ as audited |
| F15 | Pigment Trader → carrying a dye material | Dye Trader → dye material | ✅ | ✅ as audited |
| F16 | Party Planner → 10 residents | Party Girl → 14 NPCs | ◐ | ❌ NC-14 → 0.5.0 |
| F17 | Travelling Merchant → 2 residents, 45% of mornings | 2 NPCs, random days | ✅ | ✅ as audited |
| F18 | Town pets → companion permit | License purchase | ✅ | ✅ as audited |
| F19 | none | Princess (all NPCs), Santa (Frost Legion), Tax Collector | ❌ low priority | ❌ deferred (X-NC-1) |
| F20 | Astro-Botanist → hydroponics placed | none | ➕ | ➕ kept |
| F21 | Greenskeeper → golf ball found | Golfer → rescued | ✅ | ✅ as audited |

## G. Difficulty modes and death rules

| ID | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| G1 | World tiers | Normal, Expert, Master (+Journey) (`GameModeID`, evidence) | Explorer, Classic, Brutal, Annihilation (`WorldSettings.cs:4-11`) | ✅ | ✅ as audited |
| G2 | Enemy scaling | Expert about ×2, Master about ×3 (knowledge) | ×0.75 / 1 / 1.4 / 2 (`WorldSettings.cs:96-102`); hardmode ×1.35-2.6 (`Hardmode.cs:52-59`) | ◐ | ◐ D-6 in work (0.2.0 balance stream) |
| G3 | Defence effectiveness | 50 / 75 / 100% (knowledge) | Same (`DefenseBalance.cs:19`) | ✅ | ✅ as audited |
| G4 | Expert and Master extras | Treasure bags, expert items, relics | Salvage caches and core-chips on Brutal+, relics on Annihilation (`PlayerSystems.cs:389-414`) | ✅ | ✅ as audited |
| G5 | Coin loss on death | `DropCoins`: drop 50% (Classic), 75% (Expert), 100% (Master) (evidence: `stack/2`, `stack/4`, `0` kept) | 40% for Classic and Salvager, 0 for Explorer and Creative, 100% otherwise; **not scaled by world difficulty** (`Combat.cs:562-593`) | ◐ | ✅ 0.1.8: death drops by character difficulty (replaces PC-9) |
| G6 | Classic item loss | Softcore: no items lost (knowledge; `KillMe` calls `DropItems` only for Mediumcore and Hardcore, evidence) | **Destroys** half of each material stack (`GameSimulation.cs:2561-2570`); the UI text says "drop" (`WorldSettings.cs:124`) | ⚠ | ✅ 0.1.8: death drops by character difficulty (replaces PC-8) |
| G7 | Explorer | none | Keeps everything; 3 s respawn | ➕ | ➕ kept |
| G8 | Mediumcore | `DropItems`: inventory, armour, dyes, misc equips and loadouts; destroys the copper starter tools (evidence) | Salvager: inventory only, worn gear kept, pile lasts 20 minutes longer (`Combat.cs:596-623`) | ◐ | ✅ 0.1.8: Medium drops money and items |
| G9 | Hardcore | Permanent death | Ironman: saves, moves the character file to `deleted/`, returns to title (`GraphicalSandbox.cs:888-894`, `CharacterFile.cs:94-100`) | ✅ | ✅ as audited |
| G10 | Journey death | No drops | Creative drops 0 credits (`Combat.cs:569`) | ✅ | ✅ as audited |
| G11 | Journey research scope | Per player (evidence: `CreativePowerManager.ResetDataForNewPlayer` at creation) | **World flags** `creative.research.*` (`Creative.cs:13,27,52`) | ⚠ | ❌ PC-13 not done, not scheduled |
| G12 | Journey thresholds and powers | Per-item research counts; time, weather, spawn, godmode, difficulty slider, spread freeze | 1/3/10/25 by stack size; time, godmode, spawn rate, difficulty (`Creative.cs:19-25,71-116`) | ◐ | ❌ X-CR-10 not done, not scheduled |
| G13 | Journey world lock | Journey characters only enter Journey worlds | Any world | ◐ | ✅ PU-10 0.1.13 |
| G14 | Respawn timer | 600 ticks (10 s); ×1.5 in Expert; the +10 s boss penalty applies **only in multiplayer** (evidence: `GetRespawnTime @0-@56`) | 5 s, Explorer 3 s, 20 s while a boss lives, single player too (`Player.cs:96-97,1021-1022`) | ◐ | ✅ PC-10 0.1.11 |

## H. Mobility gates (progression view only; mechanics belong to the movement area)

| ID | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| H1 | Wings | Hardmode (souls of flight) | Tiers 1-3 and early wings are glide rigs with no climb (`Gear.cs:89`, `ArsenalItems.cs:217`); climbing needs flight essence (tier 4+), except the rare salvaged thruster fins (`GrowthItems.cs:102-106`) | ✅ | ✅ as audited |
| H2 | Hook ladder | about 30 hooks | Scrap tether → mag-grapple → twin lash (viridium + flight essence) → phase tether (`GrowthItems.cs:72-78,121-129`) | ✅ | ✅ as audited |

---

## Integration steps

Each step lists the change, the files, the size (S/M/L), its dependencies and the acceptance test. Steps marked **(decision)** need a user answer first (see "Open questions").

### PC-1: Gate the Rust Moon boss and the Moon Eater lure behind the Fracture (P0, soft block)
- **Change:** Before the Fracture, the Rust Moon runs as a quota event with no final boss and ends with its rewards. The Moon Eater spawns only after the Fracture (Terraria's Blood Moon has no pre-hardmode boss). Add a `MoonEater when !FractureActive` refusal for the lure. Before the Fracture, the Moon Eater's void-essence drop becomes credits or rust scrap.
- **Files:**
  - `Systems/WorldEvents.cs:435-440`: a `FinalBossGate` predicate on the spec, or a check in the quota branch at `:348`
  - `Systems/GameSimulation.CreatureCoverage.cs:193-203`
  - `Systems/NpcDropRegistry.cs:168`
- **Size:** S.
- **Dependencies:** events and creatures area (event roster ownership).
- **Acceptance:** a self-test does the following:
  1. Start a new world, set `player.health.120` and force the Rust Moon.
  2. Kill 25 event creatures: no boss spawns, and the event completes or keeps running with no boss.
  3. Set the Fracture and repeat: the Moon Eater spawns.
  4. Before the Fracture, `UseItem(MoonEaterLure)` returns the refusal.

### PC-2: Tier the Defense Protocol (P0)
- **Change:** Pick the final boss by progression:
  - Before the Overseer: none (waves only), or a new mini-boss built from `EventProtocolOgre`.
  - After the Fracture: Protocol Ogre-class.
  - After the Vault Colossus: Protocol Dragon (Betsy analog).
  - Scale the waves by tier.
- **Files:** `Systems/WorldEvents.cs:464-468` plus the event final-boss spawn path; `Systems/Content/ContentItems.cs:538` (Signal core tier).
- **Size:** M.
- **Dependencies:** events area; creature area if a new mini-boss is added.
- **Acceptance:** on a fresh world the Signal core starts the event and no 2800-HP boss appears. With `boss.vault_colossus.down` set, the dragon spawns. The craft audit (after PC-7) shows the servo grease only after the Colossus.

### PC-3: Gate the Abyss Leviathan behind the Fracture (P0)
- **Change:** In `RollCatch`, skip the Abyss-bait branch when `!FractureActive` and post a hint. Change the Abyss bait recipe to need flight essence (post-Fracture) instead of void essence.
- **Files:** `Systems/GameSimulation.Fishing.cs:220-224`, `Systems/Content/ContentRecipes.cs` (fishing block, `AbyssBait`), `Systems/CraftingAudit.cs:842`.
- **Size:** S.
- **Dependencies:** fishing area.
- **Acceptance:** a probe fishes in acid with bait before the Fracture and gets no Leviathan; after the Fracture the Leviathan spawns. In the audit, the Leviathan's drops come after the Fracture stage.

### PC-4: Pre-Fracture dusk incursions (decision)
- **Change:**
  - Option A: no incursion boss before the Fracture.
  - Option B: pre-Fracture incursions bring the event boss at ×0.6 life and damage, with no permanent-upgrade drop.
  - Either way, `GrowthItems.UpgradeBosses` drops wait for the Fracture.
- **Files:** `Systems/GameSimulation.cs:2441-2457`, `Systems/Content/GrowthItems.cs:343`.
- **Size:** S.
- **Dependencies:** combat area (scaling hooks).
- **Acceptance:** after the Scout Mech and before the Fracture, a forced incursion follows the chosen rule. Killing a Void Stalker before the Fracture never drops nanite marrow.

### PC-5: Rebuild the pre-hardmode mining ladder (P0)
- **Change:** Use thresholds that make each drill matter, mapped through `DrillPowerFromTerraria`:
  - ferrite and scrap 0
  - cobalt 0 (iron-tier ore is minable with copper in Terraria)
  - glacial and uranium 35
  - meteorite 50
  - obsidian 55 (the glacial or uranium drill, like Terraria's evil picks)
  - pyrocite **60** (needs the meteorite or glacial drill; Terraria hellstone 65)
  - viridium **80** (needs the pyrocite drill or the core breaker; Terraria cobalt 100 = molten pick)
  - lumenite 85 and voidsteel 100 unchanged

  Keep the tier-3 drill spread (55/60/80) as the ladder to these numbers.
- **Files:** `World/TileGrid.cs:662-673,693`, `World/ContentTiles.Theme.cs:26`, `World/ContentTiles.cs:183-184` (geodes), `Systems/Content/GearCurve.cs` (docs only).
- **Size:** M (an audit re-run and possible recipe moves).
- **Dependencies:** worldgen area (any guaranteed-path obsidian or pyrocite on the route to the Irradiated Core); tools area (`ToolParity`).
- **Acceptance:** a self-test asserts, for each ore, that its minimum power is greater than the previous tier's drill and at most the drill one rung below. A probe confirms the kit drill fails on pyrocite and viridium. `--craft-audit` shows the "Drill power 55/60/80" milestones each adding items, and the Fission Tyrant is still reachable: the core igniter path must not need a drill above what the Power Forge Mk2 tier gives.

### PC-6: Remove hardmode materials from the pre-Fracture world (P0)
- **Change:**
  - **Viridium:** either stop generating its veins (altars seed it, as in Terraria) or keep the veins behind PC-5's 80 gate. Either way, take viridium ore out of the pre-Fracture biome-chest tables.
  - **Void essence before the Fracture:**
    - Chest depth finds and slag chests: replace with obsidian or ancient power cells.
    - Dungeon chests: replace with pyrocite bars or credits.
    - Dungeon void scribes and crypt flitters: drop "precursor ash" or credits until the Fracture.
- **Files:** `World/LargeWorldGenerator.cs:1181`, `World/WorldGeneration.cs:174`, `Systems/GameSimulation.WorldGenContent.cs:181,192`, `Systems/GameSimulation.Physics.cs:721`, `Systems/Content/ContentFillData.cs:230-231`, the biome-chest table (wherever viridium ore is listed).
- **Size:** M.
- **Dependencies:** worldgen area (chest tables and vein passes); creature area (drops); this step depends on PC-7 for verification.
- **Acceptance:** after PC-7, `--craft-audit --items` lists `ViridiumOre`, `ViridiumBar`, `VoidEssence`, `VoidDarts`, `VoidFuel` and `ViridiumBolts` at or after the "Defeated Fission Tyrant" stage. The tier-6+ before-Fracture count is 0.

### PC-7: Make the craft audit honest about early bosses (P0 tooling)
- **Change:** Process every ready milestone that does not cause the Fracture before the Fracture-causing one. Give "Defeated Fission Tyrant" a sort key after all pre-Fracture milestones, or run a fixpoint pass with the Fracture held off. Add a section "Bosses reachable before the Fracture", with life and tier, that flags any boss above the gate boss's life. Fail the self-test on any item of tier ≥ 6 obtainable before the Fracture, outside a whitelist.
- **Files:** `Systems/CraftingAudit.cs:240-305,739-800`, `Systems/CraftingAuditSelfTests.cs`.
- **Size:** S-M.
- **Dependencies:** none. Run this first, because it verifies PC-1..PC-6.
- **Acceptance:** before PC-1..PC-6, the audit shows the Spore Monarch, Abyss Leviathan, Protocol Dragon, Moon Eater and incursion bosses as pre-Fracture. After them, only the intended easy bosses remain.

### PC-8: Fix the Classic death penalty (decision)
- **Change:**
  - Option A (Terraria parity): Classic keeps items and drops 50% of credits.
  - Option B (keep the Rimtera flavour): the halved material stacks **drop** as a recoverable pile, using the Salvager code path, instead of being deleted.
  - Either way, make the text match the behaviour.
- **Files:** `Systems/GameSimulation.cs:2557-2575`, `Systems/GameSimulation.Combat.cs:562-593`, `Systems/WorldSettings.cs:124`.
- **Size:** S.
- **Dependencies:** UI area (text only).
- **Acceptance:** `ParityCombatTests` death test: a Classic death with 40 basalt leaves either 40 in the pack (A) or 20 in the pack and 20 on the ground at the death site (B). No material count drops in total.

### PC-9: Scale the coin drop by world difficulty
- **Change:** Multiply the credit share by world tier, per `DropCoins`: Classic 50%, Brutal 75%, Annihilation 100%. Explorer and Creative stay at 0.
- **Files:** `Systems/GameSimulation.Combat.cs:562-575`.
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** a unit test of the four world tiers with a Classic character shows the drops in order.

### PC-10: Respawn timing (decision)
- **Change:** Base 10 s, ×1.5 on Brutal+. Drop the single-player boss penalty, or keep it as a deliberate Rimtera choice. Explorer stays faster.
- **Files:** `Systems/GameSimulation.Player.cs:96-97,1021-1032`.
- **Size:** S.
- **Dependencies:** UI (countdown).
- **Acceptance:** `GetRespawnDelay` table test.

### PC-11: Accessory slots 6 and 7
- **Change:** Make the auxiliary socket open its slot only in Brutal+ worlds (Terraria Demon Heart rule). Have Annihilation add a further slot, `Accessory7` (append-only enum), so the maximum is 7.
- **Files:**
  - `Systems/PlayerProgression.cs:5-7,46-72,359-361`
  - `Systems/Content/GrowthItems.cs:179`
  - `Systems/CharacterFile.cs`, `Systems/GameSaveData.cs` (snapshot field)
  - `UI/InventoryUi.cs`, `UI/GraphicalSandbox.Gear.cs` (slot drawing)
- **Size:** M.
- **Dependencies:** UI and inventory area (the other agent is editing input/UI; coordinate); save format.
- **Acceptance:** the self-test gives 5 slots with a socket in a Classic world, 6 with a socket on Brutal, and 7 with a socket on Annihilation. An old save loads with no item loss.

### PC-12: Life ceiling (decision)
- **Change:**
  - Option A (keep 300): document it as a deliberate choice and retune boss damage (PC-15).
  - Option B (match the shape): life cores give +20 ×10 (cap 300) and lifebloom +5 ×20 (cap 400).
  - Option C (full parity): add 5 more cores, for 400 + 100 = 500.
  - Every option needs boss damage and the Overseer patrol threshold reviewed.
- **Files:** `Entities/Actors.cs:91-95`, `Systems/Content/GrowthItems.cs:41-46`, `Systems/GameSimulation.Hardmode.cs:23,329`.
- **Size:** S code, L balance.
- **Dependencies:** combat area (damage curve).
- **Acceptance:** `GrowthSelfTests` caps match the chosen option, and the `full-vitality` achievement fires at the new cap.

### PC-13: Move Creative research onto the character
- **Change:** Store research counts in `CharacterFileData`, as a dictionary appended to the record. On load, migrate existing `creative.research.*` world flags into the character.
- **Files:** `Systems/Creative.cs:13-60`, `Systems/CharacterFile.cs:20-35,120-135,205-220`.
- **Size:** M.
- **Dependencies:** save format.
- **Acceptance:** the self-test researches an item in world A, creates world B with the same character, and the item is still researched.

### PC-14: Creative kit polish
- **Change:** Swap the 3 recall capsules for a reusable recall device (a Magic Mirror analog; add one if none exists). Pre-summon the drone uplink's drone on first entry.
- **Files:** `Systems/Content/StarterKits.cs:22-27`, `Systems/Content/GrowthItems.cs:313-327`.
- **Size:** S.
- **Dependencies:** items area (any new item).
- **Acceptance:** `StarterKitSelfTests`: a Creative first entry holds a reusable recall item and has one drone active.

### PC-15: Re-curve boss life against player damage
- **Change:** Set a time-to-kill target that grows from the first boss to the final boss (Terraria is about ×2.6). Raise the late bosses' life (Fission Tyrant about 2,500, trio 4-6k, Astro-God about 20-25k), or lower the tier-6/7 damage bases. Revisit `BossDamageCap = 100` per tier.
- **Files:** `Entities/Actors.cs:460-534`, `Entities/ContentCreatures.cs:191-216`, `Systems/Content/GearCurve.cs:117-121`, `Systems/GameSimulation.Hardmode.cs:23`.
- **Size:** L.
- **Dependencies:** combat area (owner); creatures area.
- **Acceptance:** a balance probe (`--balance`) reports each boss's hits to kill with tier-matched gear, rising with progression; the Astro-God's is at least 2× the Overseer's.

### PC-16: Tinker Bench timing and tier-4+ combinations (decision)
- **Change:**
  - Option A: keep the bench early, but give every tier-4+ combination a tier-matched input (a viridium bar, flight essence or fission slag).
  - Option B: the Tinker Bench is sold by the Mechanic after the Scout Mech (goblin-tinkerer analog), with the recipe removed.
- **Files:** `Systems/Content/ContentRecipes.cs:68`, `Systems/Content/ArsenalItems.Data.cs` (combo recipes), `Systems/TownNpcs.cs` (shop).
- **Size:** M.
- **Dependencies:** items and gear area.
- **Acceptance:** after PC-7, the audit's "Tier 4+ gear before the Fracture" lists only deliberate items (fishing finds and rare drops).

### PC-17: Permanent-upgrade parity
- **Change:** Add a vital-crystal analog (+life regen). Tune the fortune lattice to about +0.03-0.05 luck and the bait gland to about +3-5 fishing (decision). Source the upgrades from post-Fracture bosses only (PC-4).
- **Files:** `Systems/Content/GrowthItems.cs:12-69,198-240`, `Systems/ItemSystem.cs` (append an `ItemId`).
- **Size:** S.
- **Dependencies:** items area.
- **Acceptance:** `GrowthSelfTests`: the new upgrade raises `LifeRegenerationPerHour`, and all eight round-trip through the character file.

### PC-18: Apply station inheritance transitively
- **Change:** Compute the closure of `Implied()` in `GetNearbyStations`, so the Astral Assembler counts as Fabricator, Field Workbench and both refineries, as the doc claims.
- **Files:** `Systems/CraftingSystem.cs:500-525`.
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** a unit test: a world with only an Astral Assembler reports `Workbench`, `Furnace` and `OreRefineryMk2` as nearby.

### PC-19: Medic threshold (decision)
- **Change:** Use > 100 to match Terraria's Nurse, plus a Quartermaster-present check; or keep 120.
- **Files:** `Systems/GameSimulation.Town.cs:101-102`.
- **Size:** S.
- **Dependencies:** town area.
- **Acceptance:** an unlock test at 110 HP gives the chosen result.

### PC-20: Gate a few survivor rescues (optional)
- **Change:** Place the Mechanic survivor inside the dungeon (reachable after the Scout Mech), and put one survivor in a Fracture-only structure, giving progression rescues like Terraria's Mechanic and Wizard.
- **Files:** `World/LargeWorldGenerator.Content.cs:470-506`, `Systems/TownNpcs.cs:192-196`, `Systems/GameSimulation.cs:229-235`.
- **Size:** M.
- **Dependencies:** worldgen area.
- **Acceptance:** worldgen self-test: the Mechanic's spot lies inside `Features.Dungeon`.

### PC-21: Correct the stale docs
- **Change:** In `PROGRESSION_DIFF.md` §3/§4, `CRAFTING_PROGRESSION.md` §2 and `HARDMODE_DIFF.md`:
  - Note that the Power Forge Mk2 can be mined before the Overseer (the circuit is not a hard gate).
  - Note that "dusk incursions wait for the Scout Mech" admits all four event bosses.
  - Add PC-1..PC-4's leaks to the gap lists.
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** the docs match the audit output.

**Suggested order:** PC-7 → PC-1, PC-3 → PC-2, PC-4 → PC-5, PC-6 → PC-8, PC-9, PC-13, PC-18 → PC-11, PC-14, PC-17 → PC-12, PC-15, PC-16 (balance pass) → PC-10, PC-19, PC-20, PC-21.

---

## Cross-area dependencies

| Area | What this area needs from it | Steps |
|---|---|---|
| Combat and balance | Boss life and damage curve; `BossDamageCap`; hits to kill against gear tiers; incursion scaling hooks | PC-4, PC-12, PC-15 |
| Creatures and events | Event-spec final-boss gating; a Defense Protocol mini-boss; pre-Fracture drop swaps for void creatures and the Moon Eater | PC-1, PC-2, PC-6 |
| World generation | Viridium vein pass; chest loot tables (depth finds, slag, dungeon, biome chests); obsidian or pyrocite route; survivor spots | PC-5, PC-6, PC-20 |
| Fishing | Abyss bait gate and recipe; Astral crate catch timing | PC-3, D11 |
| Tools | Drill power numbers and the `ToolParity` tests | PC-5 |
| Items and gear | New items (vital analog, recall device); tier-4+ combination inputs | PC-14, PC-16, PC-17 |
| UI and inventory (being edited by another agent) | Slot 7 drawing; death-penalty text; respawn countdown | PC-8, PC-10, PC-11 |
| Saves | `CharacterFileData` append fields (research, slot 7) | PC-11, PC-13 |
| Town | Medic rule; Tinker Bench shop; survivor placement | PC-16, PC-19, PC-20 |

---

## Open questions for the user

1. **Life ceiling:** keep 300 (cores 10 × 10 + lifebloom 20 × 5), go to 400, or match Terraria's 500? (PC-12)
2. **Classic death:** should materials be kept (Terraria Softcore) or dropped as a recoverable pile? They should not be destroyed silently, as they are now. (PC-8)
3. **Pre-Fracture event bosses** (Moon Eater, Protocol Dragon, Abyss Leviathan, incursions): gate all of them behind the Fracture, or keep some as optional early challenges with weaker stats? (PC-1..PC-4)
4. **Mining ladder:** are the proposed thresholds acceptable (cobalt 0, glacial/uranium 35, obsidian 55, pyrocite 60, viridium 80)? Should viridium still generate in the world before the Fracture? (PC-5, PC-6)
5. **Tinker Bench:** keep it craftable early, with tier-matched combination inputs, or make it a Mechanic purchase after the Scout Mech? (PC-16)
6. **Brutal scaling:** Brutal stays ×1.4 (Terraria Expert ≈ ×2). Annihilation is ×2. Is that intended?
7. **Respawn:** adopt 10 s / ×1.5 on Brutal+? Keep the 20 s boss penalty in single player? (PC-10)
8. **Seventh accessory slot** on Annihilation, and the socket limited to Brutal+ worlds? (PC-11)
9. **Medic threshold:** keep > 120, or use > 100 like Terraria? (PC-19)
