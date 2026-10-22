# Player progression and starting items: Terraria vs Rimtera

A diff of the whole player journey: what a new character starts with, how they grow, which bosses and items gate
what, and where the run ends. It is a diff only. No game code was changed to write it.

**How to read it**
- **Terraria** is behavioural evidence only. Rimtera names, numbers and tables stay original.
- **(evidence)** means the fact is backed by the REA dumps in `docs/terraria-evidence/`. **(knowledge)** means it
  comes from Terraria 1.4.4 as remembered, where the dumps carry no `Player` type (no `SetupStartingItems`, no
  crystal handlers).
- **Rimtera** cells cite `file:line` (paths under the repo root) or an `ItemId` / `EnemyKind` / flag.
- **Status:** ✅ equivalent, ◐ partial or different on purpose, ❌ missing.
- **Already covered elsewhere:** hardmode internals are in [HARDMODE_DIFF.md](HARDMODE_DIFF.md). Item and weapon
  counts are in [ITEMS_DIFF.md](ITEMS_DIFF.md). Movement, housing and respawn mechanics are in
  [MECHANICS_PARITY.md](MECHANICS_PARITY.md). System-level gaps are in [TERRARIA_GAPS.md](TERRARIA_GAPS.md). This
  document links to them and does not repeat them.

> **Status (2026-10-21):** the tables below are the original diff and show the pre-fix state. All gaps G0–G16 are closed; see “Status: all gaps closed” at the end for the authoritative current state.

**Evidence used:**

| File | What it shows |
|---|---|
| `triggers.json` | The `NPC.downed*`, `NPC.saved*` and `NPC.unlocked*Spawn` flag fields. `NPC.DoDeathEvents` calls `WorldGen.StartHardmode`, `spawnMeteor` and `shadowOrbSmashed`. Also `NPC.ScaleStats_ByDifficulty` and `ScaleStats_ForExpertHardmode`, and `NPC.checkDead` reading `Player.difficulty`. |
| `main.json` | Methods and the fields they read: `Main.UpdateTime_SpawnTownNPCs` (the town NPC gates), `Main.AnyPlayerReadyToFightKingSlime` and `Main.CanStartInvasion` (both read `Player.statLifeMax`), `Main.UpdateTime_StartNight` (`spawnEye`, `spawnHardBoss`, `altarCount`, `downedMechBoss1-3`, `statLifeMax`, `statDefense`), and `Main.UpdateCreativeGameModeOverride` / `CreativePowerManager`. |
| `systems.json` / `item.json` | Places that read `Player.statLifeMax`, `statManaMax`, `ateArtisanBread`, `unlockedBiomeTorches` and `unlockedSuperCart`. |
| `events.json` | `CultistRitual.*` and `MoonlordCountdown`. |
| `gaps-GameModeID.json` | `GameModeID.Normal`, `Expert`, `Master` and `Creative`. |

---

## 1. Starting state

| Aspect | Terraria | Rimtera | Status | Notes |
|---|---|---|---|---|
| Classic starting kit | Copper shortsword, copper pickaxe, copper axe (knowledge) | Survey drill (`WoodenPickaxe`, power 20), shock baton (`WoodenSword`, 12 dmg), laser rifle (`CopperBow`) + 80 energy cells, 50 regolith, 30 hull panel (wood), 50 basalt, 12 nano-bananas, 1 Nano-Fabricator (`GameSimulation.cs:203-213`; names in `ItemSystem.cs:1627-1637`) | ◐ | Rimtera starts richer (a ranged weapon, blocks, food and a crafting station) and has no axe. That fits the colony framing. |
| Kit by character difficulty | Journey adds iron tools, a hammer, a starter summon staff, about 100 torches and rope, a recall mirror, a grappling hook and starter wings (knowledge, partial list) | One kit for every `CharacterDifficulty`. Creative gets the same items (`GameSimulation.cs:203`, `Creative.cs:16`) | ❌ | See gap G11. |
| Starting life / mana / defence | 100 / 20 / 0 (knowledge; `statLifeMax` / `statManaMax` evidence) | 100 life, 20 charge, 0 defence (`PlayerProgression.cs:331-333`). Colonist traits add `MaximumHealthBonus` (`PlayerProgression.cs:366-368`, for example +15 in `CoreSelfTests.cs:350`) | ✅ / ◐ | Traits can lift starting life above 100. That shifts the life-gated unlocks (Medic, Rust Moon). |
| Accessory slots at start | 5 (knowledge) | 5 (`PlayerProgression.cs:5,57`) | ✅ | |
| Spawn location | World spawn point near the centre, on the surface (knowledge) | Landing site: the nearest flat grass column to the centre, outside the blight, with a flattened pad (`World/LargeWorldGenerator.cs:1660-1696`; `SpawnColumn`, `GameSimulation.cs:216-218`) | ✅ | |
| Starting guide NPC | The Guide is present on day 1, needs a house, can die, and his voodoo doll summons the gate boss (knowledge) | `GuideNpc` is a static record at the landing site. It is immortal, needs no house and has a 6-tile talk radius (`Entities/Actors.cs:771-775`, `GameSimulation.cs:221`). The Station AI is unlocked unconditionally and moves into the first valid house (`GameSimulation.Town.cs:85`) | ◐ | Two guide figures. Neither takes part in the gate-boss summon. |
| Starting housing | None (knowledge) | None. A derelict crash ship and survivor structures generate elsewhere (`LargeWorldGenerator.cs:221`) | ✅ | |
| Crew (RimWorld layer) | n/a | 2 generated colonists beside the player, with seeded relations and a brain (`GameSimulation.cs:223-241`) | n/a | Rimtera only. |
| Day-1 surface loot | Surface chests (knowledge) | Two surface crates land near the landing site (`LargeWorldGenerator.Decor.cs:632-633`) | ✅ | |

## 2. Stat growth

| Upgrade | Terraria | Rimtera | Status | Notes |
|---|---|---|---|---|
| Life crystal | +20 each up to 400 (15 uses) (knowledge) | **Life core** (`ItemId.LifeCore`). +10 each, at most 10, so +100 for a cap of **200** (`Entities/Actors.cs:91-95`, `GameSimulation.Blight.cs:373-385`). Found as `TileId.LifeCore` in caves (at least 10 per large world, `CoreSelfTests.cs:1243`) and in supply caches (`GameSimulation.Blight.cs:307`) | ◐ | **Should this match Terraria's numbers?** Half the per-use gain and half the cap. |
| Life fruit | +5 each from 400 to 500, hardmode jungle (knowledge) | None | ❌ | Gap G7. |
| Mana crystal | +20 each up to 200 (9 uses); craftable from fallen stars (knowledge) | **Charge crystal**: +20 each, at most 9, so a cap of **200** (`Systems/Content/PlayerItems.cs:17-18`, `GameSimulation.Combat.cs:84-85`). Crafted from 6 energy crystals + a power cell at the Nano-Fabricator (`PlayerItems.cs:82`), sold by the travelling merchant (`ContentFillTown.cs:29`), dropped by the cache mimic (`NpcDropRegistry.cs:157`) | ✅ | Same curve. |
| Vital Crystal, Aegis Fruit, Arcane Crystal, Ambrosia, Gummy Worm, Galaxy Pearl, Artisan Loaf (1.4.4 one-use permanent buffs) | Small permanent boosts: life regen, +defence, mana regen, mining/build speed, +fishing, +luck, crafting range (knowledge; `Player.ateArtisanBread` is read by `Chest.SetupShop` (evidence)) | None. The character file only stores `LifeCoresUsed` and `ChargeCrystalsUsed` (`CharacterFile.cs:22-23`) | ❌ | Gap G9. |
| Torch God's Favor | Unlocks automatic biome torches (`unlockedBiomeTorches` evidence) | None | ❌ | Low priority. |
| Minecart upgrade | `unlockedSuperCart` (evidence) | None | ❌ | Low priority. |
| Extra accessory slot | Demon Heart (+1, Expert/Master); Master mode +1 more, for 7 in total (knowledge) | Fixed at 5 (`PlayerProgression.cs:5,57`) | ❌ | Gap G8. |
| Temporary max-life buffs | Lifeforce potion +20% (knowledge) | `StatusId.Lifeforce` +80, `Vitality` +30 (`PlayerProgression.cs:165,183`) | ✅ | |
| Accessory with max life | Band of Regeneration line, Charm of Myths (knowledge) | Vital core (+50), Nova heart (+60) (`ArsenalItems.Data.cs:542,549`), core-chips (`PlayerSystemsContent.cs:144`) | ✅ | |
| Achievements for stat growth | "Get a Life", "Topped Off" (knowledge) | `life-cores-5`, `first-crystal`, `charge-crystals-max` (`Achievements.cs:38-40`). There is no max-life achievement | ◐ | |

## 3. Early-game loop

| Step | Terraria | Rimtera | Status | Notes |
|---|---|---|---|---|
| First station | Workbench from wood (knowledge) | A Nano-Fabricator is in the starting kit. Recipe: 10 wood (`CraftingSystem.cs:59-60`). The Field Workbench takes 6 wood and needs no station (`ContentRecipes.cs:61`) | ✅ | |
| Furnace / anvil | Furnace from stone, wood and torches; anvil from 5 iron or lead bars (knowledge) | Ore Refinery from 20 basalt; Power Forge from 12 basalt and more (`CraftingSystem.cs:62-75`). The anvil needs **no bars** | ◐ | The forge is cheaper than Terraria's anvil, so the first metal tier comes faster. |
| Ore ladder (pre-hardmode) | Copper/tin → iron/lead → silver/tungsten → gold/platinum, then evil ore, meteorite and hellstone (knowledge) | Ferrite (`TileId.CopperOre`) or scrap-iron → cobalt → glacial, pyrocite, uranium or meteorite (three parallel biome bars at tier 3) (`ContentRecipes.cs:81-87`). A generic "Metal alloy" (`CopperBar`) comes from most ores (`CraftingSystem.cs:78,174-281`) | ◐ | Three rungs instead of four plus extras. Biome bars replace the evil, meteor and underworld bars. |
| Mining gates (pre-hardmode) | Basic ores 0; meteorite 50; evil ore and obsidian 55; hellstone and evil stone 65 (knowledge) | 0 for ferrite and scrap-iron. **35** for cobalt, glacial, uranium, pyrocite, viridium and obsidian (`World/TileGrid.cs:655-666,686`). 50 for meteorite (`World/ContentTiles.cs:168`) | ◐ | Every pre-hardmode biome ore shares one threshold (35). The ferrite drill (38) opens all of them at once. Pyrocite, the "hellstone" analog, needs 35, not a higher rung. |
| Tool power ladder | Copper 35 … gold/platinum 55-59, Nightmare/Deathbringer 65, Molten 100 (knowledge) | Survey drill 20, ferrite drill 38 (`ItemSystem.cs:1627-1630`); ferrite rock drill 42, cobalt 55, glacial 70, viridium 85, lumenite 100, voidsteel 120, astral 150 (`Content/ContentItems.cs:340-346`) | ◐ | |
| Gear tiers and stations | Armour per ore bar at the anvil (knowledge) | Generated tier gear: tiers 1-2 at the Power Forge, 3 at Power Forge Mk2, 4 at Alloy Anvil, 5 at Nano-Fabricator Mk2, 6 at Void Forge, 7 at Astral Assembler (`ContentRecipes.cs:13-18`). Armour sets: Scav (1) → Cobalt trooper (2) → biome sets (3) → Viridium (4) → Lumenite (5) → Voidsteel (6) → Astral (7) (`ContentItems.cs:378-401`), plus 27 arsenal sets (`ArsenalItems.Data.cs:423-449`) | ✅ | |
| Station gating | The Hellforge needs underworld travel; hardmode anvils need hardmode bars (knowledge) | Power Forge Mk2 needs the **Overseer circuit** from the first boss (`ContentRecipes.cs:73`, `GameSimulation.World.cs:245-246`). Ore Refinery Mk2 and the Alloy Anvil need heart fragments from blight hearts (`ContentRecipes.cs:74-75`) | ✅ | A good boss-to-station gate. |
| First night | Zombies and flying eyes; Blood Moon possible (knowledge) | Night spawn tables; the Rust Moon (blood-moon analog) only once max HP ≥ 120 or the Overseer is down (`WorldEvents.cs:429-430`, flag set in `GameSimulation.World.cs:471-472`) | ✅ | |
| First boss options | King Slime (Slime Crown, or natural spawn gated by `AnyPlayerReadyToFightKingSlime`: `statLifeMax` + `statDefense` (evidence)); Eye of Cthulhu (summon item, or natural at night via `spawnEye` gated on life/defence in `UpdateTime_StartNight` (evidence)) | **Overseer mech** (`EnemyKind.StoneSentinel`, 520 HP) from the Reactor key (5 ferrite bars + 3 energy crystals at the Nano-Fabricator) (`ContentItems.cs:490`, `ContentRecipes.cs:339`). Also the Swarm pheromone (Alpha Skitter: bio-gel + berries at the Chem Lab) and Spore crown (Spore Monarch) | ◐ | No boss ever **arrives on its own** before the Fracture. Gap G6. |

## 4. Pre-hardmode path through the gate boss

Rimtera's challenge order is `EnemyArchetypes.BossProgression` (`Entities/Actors.cs:497-505`). Summons are in
`Content/ContentItems.cs:487-508` and their recipes in `Content/ContentRecipes.cs:339-358`.

| Terraria boss (knowledge, flags as evidence) | Unlocks in Terraria | Rimtera analog | Summon / spawn | Unlocks in Rimtera | Status |
|---|---|---|---|---|---|
| King Slime (`downedSlimeKing`) | Nothing much | Alpha Skitter (Easy, 650 HP) | Swarm pheromone; also the Swarm Rain final boss (`WorldEvents.cs:440-444`) | Easy-boss tag → dusk incursions (`GameSimulation.cs:271-277,2359-2361`) | ✅ |
| Eye of Cthulhu (`downedBoss1`) | Evil-ore drops; counts toward the Dryad | Overseer mech (`StoneSentinel`, tier **Gate**) | Reactor key (tier 1) | Overseer circuit → Power Forge Mk2; the Chief Engineer moves in; Swarm Rain; Rust Moon; **dungeon opens** (`GameSimulation.Physics.cs:68,1201`) | ◐ The first boss also acts as the dungeon key. |
| Eater of Worlds / Brain of Cthulhu (`downedBoss2`; `shadowOrbSmashed`, `spawnMeteor` from `DoDeathEvents`) | Meteor falls, goblin army (if a player has ≥ 200 HP: `CanStartInvasion` reads `statLifeMax`), Tavernkeep | Blight hearts: every 3rd heart summons a boss (`GameSimulation.Blight.cs:20,249-253`). Overgrowth → **Plant Overgrowth** (Easy). Radiated → **Cyber-Swarm Leader** (tier **Hard**, one of the hardmode trio) | Breaking a reactor core or spore heart | The first heart: Head Researcher, Scavenger Raid, 50% meteor that night (`GameSimulation.World.cs:230,259-266`, `GameSimulation.Town.cs:97`, `WorldEvents.cs:445-446`) | ❌ A radiated world puts a hardmode boss in front of a pre-hardmode player. Killing it also counts toward `HardTrioDefeated` (`GameSimulation.Hardmode.cs:65`) and drops 20 blight essence before the Fracture. Gap G1. |
| Queen Bee (`downedQueenBee`) | Witch Doctor | Bloom Matriarch (tier **Hard**, 1800 HP) | Breaking a bloom hive (`GameSimulation.Items.cs:713-721`) or Royal nectar (ungated) | Arborist moves in (`GameSimulation.World.cs:234`); hive wings and gear at tier 4 | ◐ Tier mismatch: a Hard-tier boss and tier-4 loot are open before the Fracture. Gap G2. |
| Skeletron (`downedBoss3`) | Dungeon access, Clothier, Mechanic rescue | Overseer mech again: the Dungeon Guardian blocks the dungeon until it falls (`GameSimulation.Physics.cs:1193-1210`) | Reactor key | Dungeon loot, survivors | ◐ The dungeon opens after the **cheapest** boss, not mid-way through pre-hardmode. Gap G12. |
| Deerclops (`downedDeerclops`) | Optional | None (frozen boss) | — | — | ❌ (optional) |
| Damaged Mining Drone (no Terraria twin) | — | Easy, 600 HP | Faulty mining chip (circuit boards + cobalt ore) | Cobalt ore | n/a |
| Scout Mech (no twin; the last easy boss) | — | Easy, 900 HP (`GameSimulation.Blight.cs:26-27`) | Scout beacon (cobalt bars + power cells at the Power Forge) | **Required** before the Core igniter works (`GameSimulation.Hardmode.cs:111-114`) | ◐ Rimtera adds a prerequisite Terraria's gate boss lacks. |
| Wall of Flesh (`WorldGen.StartHardmode` called from `NPC.DoDeathEvents` (evidence)) | Hardmode, Pwnhammer, emblem, Demon Heart (Expert) | **Fission Tyrant** (`EnemyKind.FissionTyrant`, Gate, 1150 HP) | Core igniter, only in the Irradiated Core and only after the Scout Mech (`GameSimulation.Hardmode.cs:109-118`) | The Fracture, the Core breaker (altar tool), the Isotopist (`NpcDropRegistry.cs:45-46`, `GameSimulation.Town.cs:104-105`) | ◐ No emblem or extra-slot drop (gap G8). Details in [HARDMODE_DIFF.md](HARDMODE_DIFF.md) row 2. |

**Biome and ore gating (pre-hardmode)**

| Gate | Terraria | Rimtera | Status |
|---|---|---|---|
| Evil ore | Demonite/crimtane from evil-boss drops and orbs, mined at 55 (knowledge) | No evil-ore tier. Hearts drop caustic shards and uranium, or viridium ore (`GameSimulation.Blight.cs:220-243`) | ◐ |
| Meteorite | Falls after an orb is smashed or the evil boss dies (`spawnMeteor`, evidence); mined at 50 | 50% meteor after each heart (`GameSimulation.World.cs:264-265`); Starfall season (`World.cs:467-468`); mined at 50 (`ContentTiles.cs:168`) | ✅ |
| Hellstone / underworld | Mined at 65; needs an obsidian path to cross lava (knowledge) | Pyrocite in the Irradiated Core, mined at **35** (`TileGrid.cs:666`) | ◐ Should the number match? A 50-55 tier would make the cobalt drill matter. |
| Dungeon | Blocked by the Dungeon Guardian until Skeletron is down | Same mechanism, keyed to the Overseer (`GameSimulation.Physics.cs:1193-1210`) | ✅ mechanism / ◐ timing |
| Jungle | Open from the start; hardmode Plantera and chlorophyte | Verdant Canopy open; the Rotting seed (Plant Overgrowth) needs jungle roots at the Bio Lab (`ContentRecipes.cs:342`) | ✅ |
| Hardmode material reachable early | Hardmode ores only exist after altars are smashed | **Viridium** generates in deep rock before the Fracture and drops from hearts and bosses. The Alloy Anvil (tier-4 station) and the **Viridium drill (85)** can be built before the Fracture | ❌ The drill meets the altar threshold (75, `HardmodeContent.cs:13`) and the voidsteel threshold (85, `ContentTiles.cs:174`). Gap G4. |

## 5. Hardmode through the final boss (chain only)

Details live in [HARDMODE_DIFF.md](HARDMODE_DIFF.md) ("Status after the hardmode pass"). The chain, with the
missing links:

| # | Terraria step (flags as evidence) | Rimtera step | Status | Missing link |
|---|---|---|---|---|
| 1 | Pwnhammer smashes altars; each smash seeds the next ore tier (`altarCount` is read by `UpdateTime_StartNight`) | Core breaker (power 75) smashes rad/root altars; the cycle is viridium → lumenite → voidsteel (`GameSimulation.Blight.cs:92,260-270`) | ◐ | Any drill with power ≥ 75 also smashes altars, including the pre-Fracture viridium drill. Lumenite needs 70 and voidsteel 85 (`ContentTiles.cs:173-174`), so one viridium drill mines the whole hardmode ladder (gap G4). |
| 2 | Mechanical bosses (`downedMechBoss1-3`, `downedMechBossAny`); natural spawn via `spawnHardBoss` | Hard trio: Cyber-Swarm Leader, Renegade Overseer, Warship Core. Summons refuse before the Fracture (`GameSimulation.Hardmode.cs:119-120`); natural dusk arrival (`HardBossNightChance` 0.2) | ◐ | Blight hearts bypass the gate (gap G1). There is no player-strength check on natural arrivals. |
| 3 | Plantera (`downedPlantBoss`) after all three mechs | Mutated Flora from canopy bulbs after `hardmode.trio.down` (`GameSimulation.Hardmode.cs:121-122,140-141`) | ✅ | |
| 4 | Golem (`downedGolemBoss`), temple key from Plantera | Vault key from Mutated Flora → Vault Colossus (`NpcDropRegistry.cs:43-44`, `GameSimulation.Hardmode.cs:123-124`) | ✅ | |
| 5 | Lunatic Cultist (`downedAncientCultist`, `CultistRitual.*`) | Void Cantor at `RitualSite` after the Colossus (`GameSimulation.Hardmode.cs:125-126,175-178`) | ✅ | |
| 6 | Four pillars (`downedTower*`), shields | Four obelisks with sieges, shielded until 20 kills each (`GameSimulation.Hardmode.cs:36-41,179-180`) | ✅ | |
| 7 | Moon Lord (`MoonlordCountdown`, `downedMoonlord`); the Celestial Sigil is crafted from pillar fragments | Astro-God after a 60 s countdown (`GameSimulation.World.cs:401-406,482-494`) | ◐ | The **Astral sigil** has no gate (`HardmodeSummonRefusal` has no AstroGod case, `GameSimulation.Hardmode.cs:109-128`). Its recipe uses lumenite, voidsteel and prism shards, not obelisk drops (`ContentRecipes.cs:354`). Players can skip the cultist and the obelisks entirely (gap G5). |
| 8 | Hardmode-only optional bosses (Queen Slime, Empress of Light, Duke Fishron, event moons) | Prism Empress (crystal altar, or the Prism catalyst at tier 5); Abomination (Tainted tissue: heart fragments + gel, **ungated**); Abyss Leviathan (fishing); event bosses at dusk once any easy boss is down (`GameSimulation.cs:2382-2391`) | ◐ | Breaking a crystal altar and using the Tainted tissue spawn Hard bosses before the Fracture. Dusk incursions bring 1200-1500 HP event bosses after only the first easy kill (gap G2). |

## 6. Town NPCs as a progression track

Terraria conditions are evidence where `Main.UpdateTime_SpawnTownNPCs` or an `NPC.SpawnAllowed_*` /
`NPC.saved*` / `NPC.unlocked*` member shows them. Thresholds are knowledge. Rimtera's conditions are in
`GameSimulation.Town.cs:82-110` and `GameSimulation.World.cs:228-235`; survivors are listed in `TownNpcs.cs:185-189`.

| Terraria NPC → condition | Rimtera NPC → condition | Status | Notes |
|---|---|---|---|
| Guide → start | Guide figure (`GuideNpc`) + **Station AI** → always (`Town.cs:85`) | ✅ | |
| Merchant → 50 silver carried (`SpawnAllowed_Merchant`) | **Quartermaster** → 50 credits carried (`Town.cs:86-87`) | ✅ | Same shape. |
| Nurse → max life above 100, with the Merchant present (`SpawnAllowed_Nurse`) | **Medic** → max HP **> 120**, or a colonist needs tending (`Town.cs:88-89`) | ◐ | **Number differs.** With +10 cores, 120 needs 3 cores (2 if a trait adds life). Should it be > 100 to match? |
| Arms Dealer → gun or bullets carried (`SpawnAllowed_ArmsDealer`) | **Armorer** → a ranged weapon other than the starting laser rifle (`Town.cs:90-92`) | ✅ | |
| Demolitionist → explosive carried (`SpawnAllowed_Demolitionist`) | **Demolitionist** → freed survivor, or Scavenger Raid cleared (`World.cs:232`) | ◐ | Different trigger. |
| Dye Trader → dye material (`SpawnAllowed_DyeTrader`) | None (the dye vat exists) | ❌ | |
| Dryad → first major boss (`downedBoss1/2/3` read) | **Head Researcher** → first blight heart broken (`Town.cs:97-98`) | ◐ | Keyed to the evil biome, not a boss. |
| Clothier → Skeletron (`downedBoss3`) | **Chief Engineer** → Overseer down (`Town.cs:93-94`) | ✅ | |
| Witch Doctor → Queen Bee (`downedQueenBee`) | **Arborist** → Bloom Matriarch down, or freed survivor (`World.cs:234`) | ✅ | |
| Angler → found asleep at the ocean (`savedAngler`) | **Tide Warden** → carrying a fishing rod (`Town.cs:101-102`) | ◐ | No rescue. |
| Goblin Tinkerer / Mechanic / Stylist / Golfer / Bartender → rescued (`savedGoblin`, `savedMech`, `savedStylist`, `savedGolfer`, `savedBartender`) | 8 survivor kinds bound in structures: Demolitionist, Tailor, Cartographer, Mechanic, Pilot, Cook, Arborist, Archivist (`TownNpcs.cs:185-189`, `GameSimulation.cs:244-250`); Greenskeeper → golf ball found | ✅ | Rescued from the start. Terraria gates the Goblin rescue behind the goblin army and the Mechanic behind Skeletron. |
| Painter → 8 NPCs | **Trade Consul** → 2 residents; leaves at night (`Town.cs:99-100,46`) | ◐ | |
| Party Girl → 14 NPCs (`unlockedPartyGirlSpawn`) | None | ❌ | |
| Zoologist → bestiary progress (`GetBestiaryProgressReport` call) | None (the bestiary exists) | ❌ | Gap G13. |
| Tavernkeep → after the evil boss (`savedBartender`) | The Station AI hosts the Defense Protocol (`GameSimulation.Items.cs:105-111`) | ◐ | |
| Pirate → pirates defeated (`downedPirates`) | **Pilot** → Pirate Boarding cleared, or freed survivor (`World.cs:233`) | ✅ | |
| Steampunker → any mech (`downedMechBossAny`) | **Aeronaut** → any trio boss (`Town.cs:106-107`) | ✅ | |
| Cyborg → Plantera (`downedPlantBoss`) | **Relic Warden** → Mutated Flora (`Town.cs:108-109`) | ✅ | |
| Wizard / Tax Collector / Truffle → hardmode rescues and biomes (`savedWizard`, `savedTaxCollector`, `unlockedTruffleSpawn`) | **Isotopist** → Fission Tyrant (`Town.cs:104-105`); **Archivist** → vault opened | ◐ | |
| Santa → Frost Legion (`downedFrost`) | None | ❌ (seasonal) | |
| Princess → every other NPC (`unlockedPrincessSpawn`) | None | ❌ | |
| Travelling Merchant → 2 NPCs, random days | **Travelling Merchant** → 2 residents, 45% of mornings (`ContentFillTown.cs:14-20`) | ✅ | |
| Town slimes (`unlockedSlime*Spawn`) | 4 **town gels**, found in their biome once 3 residents live in town (`ContentFillTown.cs:22,35-41`) | ✅ | |
| Town pets (`boughtCat`, `boughtDog`, `boughtBunny`) | None | ❌ | |

Housing rules are the same shape (`HousingValidator`, `TownNpcs.cs:112-173`); see [MECHANICS_PARITY.md](MECHANICS_PARITY.md) §D.

## 7. Mobility progression

| Kind | Terraria (knowledge) | Rimtera | Status | Notes |
|---|---|---|---|---|
| Hooks | About 30 hooks: an early craftable hook, gem hooks, biome hooks, double and triple hooks in hardmode, and a post-final hook. Range, speed and hook count rise with progression | **One** grapple hook (`ContentItems.cs:286`): 5 cobalt bars + 3 rope at the Tinker Bench (`ContentRecipes.cs:186`). Sold ungated by the Aeronaut and the travelling merchant (`TownNpcs.cs:275`, `ContentFillTown.cs:28`); found in chests (`GameSimulation.Physics.cs:715`) | ❌ | No hook ladder. Gap G10. |
| Boots and jumps | Hermes-type run boots, double-jump bottles, rocket boots from the Goblin Tinkerer, combined into Terraspark boots in hardmode | Jet boots (`rocket-boots` tag), double-jump canister, climbing claws, sprint servos Mk I-III, pathfinder and inferno treads, mobility rig (`PlayerItems.cs:63-64`, `ArsenalItems.Data.cs:528-543`) | ✅ | Good combine ladder. |
| Wings | **Hardmode only** (apart from a rare early pair); need flight souls from sky enemies | Ferrite (tier 1), cobalt (2) and four tier-3 wingpacks are craftable before the Fracture with **no flight essence** (`ArsenalItems.Data.cs:572-577`). So are many early mob and boss wings (scrap, moth-silk, leaf, bat-sail, frost-feather, spore, hive). Essence is needed from viridium wingpacks up (`ArsenalItems.Data.cs:578-581`) | ❌ | Wings make the early vertical game trivial. Gap G3. |
| Rocket pack / glider | Not in Terraria | Glider wings (ungated), rocket pack (`ore.voidsteel`) (`TownNpcs.cs:275`) | n/a | |
| Mounts | Slime saddle (King Slime), bee mount (Queen Bee), minecart (early), turtle, Fishron, UFO in hardmode, drill mount after the final boss | Ore cart (ferrite), dune strider, spring hopper, tide ray, burrower (pre-boss metals); rotor skiff and hover-bike (cobalt; the skiff flies for 3.5 s) (`GameSimulation.Travel.cs:104-117,165-176`). None are boss drops or hardmode | ◐ | The whole mount roster comes early. A digging mount (the burrower) before any boss. |
| Teleport | Magic Mirror, Recall potion, Rod of Discord (hardmode) | Recall capsule (early), phase rod (prism + lumenite; merchant after `blight.fracture`) (`PlayerItems.cs:90-91`, `ContentFillTown.cs:31`) | ✅ | |

## 8. Difficulty and character modes

| Aspect | Terraria | Rimtera | Status | Notes |
|---|---|---|---|---|
| World modes | `GameModeID.Normal`, `Expert`, `Master`, `Creative` (evidence) | `WorldDifficulty.Explorer`, `Classic`, `Brutal` (`WorldSettings.cs:4-9`) | ◐ | No Master tier. |
| Enemy scaling | Expert about ×2 life and damage, Master about ×3 (knowledge); `ScaleStats_ByDifficulty` and `ScaleStats_ForExpertHardmode` (evidence) | ×0.75 / ×1 / ×1.4 (`WorldSettings.cs:94-99`); hardmode ×1.35/1.15, ×1.7/1.3, ×2.1/1.5 (`GameSimulation.Hardmode.cs:54-59`); boss hit cap of 100 | ◐ | **Should Brutal match Expert's ×2?** Brutal is softer. |
| Defence effectiveness | 50% Classic, 75% Expert, 100% Master (knowledge) | 50% / 75% on Brutal (`Content/DefenseBalance.cs:19`) | ✅ | Matches the Expert value. |
| Expert extras | Treasure bags, expert-only accessories, Demon Heart | On Brutal: salvage caches replace boss loot (`GameSimulation.PlayerSystems.cs:388-395`), exclusive core-chips (`PlayerSystemsContent.cs:138-146`) | ◐ | No extra-slot item (gap G8). |
| Master extras | Relics, master-only pets, +1 accessory slot | Relics drop on **Brutal** (`GameSimulation.PlayerSystems.cs:413-414`) | ◐ | Relics sit on the Expert analog. |
| Journey / Creative | Research (per-item counts), duplication, powers: time, weather, spawn rate, godmode, difficulty slider (`CreativePowerManager`, `Main.UpdateCreativeGameModeOverride` (evidence)). Journey characters only enter Journey worlds | **Creative** is a *character* difficulty (`WorldSettings.cs:36-37`, `Creative.cs:16`). Research needs 1/3/10/25 copies depending on stack size (`Creative.cs:19-25`). Powers: freeze/set time, godmode, spawn rate 0-3×, switch between the three world difficulties (`Creative.cs:71-116`) | ◐ | No weather or spread-freeze power and no starting kit (gap G11). A Creative character can enter any world. |
| Softcore | Drops half the carried coins; keeps items (knowledge; `NPC.checkDead` reads `Player.difficulty` (evidence)) | **Explorer**: keeps everything; 3 s respawn (`GameSimulation.Player.cs:938-939`) | ◐ | Explorer is gentler than Softcore. |
| Classic death | — | **Classic**: drops 40% of credits at the death site (`GameSimulation.Combat.cs:448-459`) and **destroys** half of each material stack (`GameSimulation.cs:2477-2485`) | ◐ | Between Softcore and Mediumcore. Should it match Softcore's 50% coins with no item loss? |
| Mediumcore | Drops every carried item on the ground (knowledge) | None | ❌ | |
| Hardcore | Permanent death | **Ironman**: saves the world, moves the character file to `deleted/` and returns to the title (`UI/GraphicalSandbox.cs:783-789`, `CharacterFile.cs:90-98`) | ✅ | |
| Journey death | No drops | **Creative** falls into the default branch of `DropDeathCredits` and drops **100%** of credits (`GameSimulation.Combat.cs:454-459`) | ❌ | Looks like a bug. Gap G0. |
| Respawn timer | About 10 s; longer while a boss lives (knowledge; exact Expert multiplier unsure) | 5 s, Explorer 3 s, 20 s while a boss lives (`GameSimulation.Player.cs:92-93,938-939`) | ◐ | Should it match? Numbers differ. |

## 9. Endgame and post-game

| Aspect | Terraria (knowledge) | Rimtera | Status | Notes |
|---|---|---|---|---|
| Final boss | Moon Lord | Astro-God (`EnemyKind.AstroGod`, Final, 6000 HP) (`Entities/Actors.cs:492-493`) | ✅ | |
| Final-boss drops | One weapon from a pool of about 9, luminite ore, a portal gun, a movement accessory, a gravity accessory (Expert) | 1-2 picks from a 7-weapon pool (`GameSimulation.Gear.cs:362-377`, `ArsenalItems.Data.cs:49,129,159,201,266,281,316`) plus the Astral compass, Nova wings, a rocket launcher, 40 energy crystals, 30 viridium ore and **20 astral ore** (`NpcDropRegistry.cs:92-93`, `GameSimulation.World.cs:248-249`) | ✅ | Good shape. |
| Top material | Luminite (final boss) + four fragments (pillars) → lunar armour and weapons | Astral ore: 12 from each obelisk and 20 from the Astro-God (`NpcDropRegistry.cs:49-52`); astral bar = 2 astral ore + lumenite + voidsteel at the Astral Assembler (`ContentRecipes.cs:78,91`) | ◐ | Obelisk ore makes tier 7 reachable before the final boss (Terraria allows the same with fragments). Each obelisk gives the same ore, so there is no per-class fragment. |
| Final tier | Lunar armour sets, Zenith, post-final wings, drill mount | Tier 7: 4 astral armour sets + Astral paragon (`ContentItems.cs:398-401`, `ArsenalItems.Data.cs:449`); astral drill (150), astral wingpack (16 flight essence); combine accessories that need astral bars: omni-emblem, Juggernaut core, Nova heart (`ArsenalItems.Data.cs:546-549`) | ✅ | |
| Re-summon | The Celestial Sigil can be re-crafted | The Astral sigil can be re-crafted (ungated; see §5 row 7) | ◐ | |
| Post-game goals | Bestiary completion, Zenith crafting, pets, relics | Achievements `astro-slayer`, `tier-7-gear` (`Achievements.cs:24,31`); bestiary; relics on Brutal | ◐ | There is no final "combine everything" weapon quest. |
| Endgame achievements | About 100, including progression ones ("Hardmode!", "Mecha Mayhem", "Champion of Terraria") | 39 total (`Achievements.cs:16-57`). No achievement for the Fracture, the trio, the vault boss or the obelisks (only `vault-opened`) | ◐ | Gap G14. |

---

## Gaps to close (prioritised)

Each gap is small and self-contained, with original Rimtera names. "Match?" marks a place where a number differs
from Terraria and needs a design decision.

**P0: bugs and holes in the progression chain**

- **G0. Creative death drop:** add `CharacterDifficulty.Creative => 0f` to `DropDeathCredits`
  (`GameSimulation.Combat.cs:454-459`). It is a one-line fix.
- **G1. Radiated hearts summon a hardmode boss:**
  - Every 3rd reactor core spawns the Cyber-Swarm Leader (`GameSimulation.Blight.cs:251`).
  - Replace it with a new Easy-tier blight avenger, the **Fallout Hive-mind** (a kitbashed swarm, about 800 HP).
  - Alternatively, use the Damaged Mining Drone.
  - Also stop non-hardmode spawns from counting toward `HardTrioDefeated`.
- **G2. Hard-tier bosses open before the Fracture:**
  - Add refusals in `HardmodeSummonRefusal` for the Abomination (Tainted tissue) and the Prism Empress (crystal altar and catalyst).
  - Either retier the Bloom Matriarch to Easy (the Queen Bee analog), with its loot dropped to tier 3, or gate the hive and Royal nectar behind the Fracture.
  - Limit pre-Fracture dusk incursions to Event bosses below about 1000 HP, or gate them on the Scout Mech.
- **G3. Pre-hardmode wings:**
  - Make the tier 1-3 wingpacks and early mob wings **glide rigs**: featherfall plus horizontal speed, with no climb.
  - Wing climb unlocks only with flight essence.
  - Keep one rare early pair, the **salvaged thruster fins**, as a chest find. That mirrors Terraria's single early pair.
- **G4. One viridium drill opens the whole hardmode ore chain:**
  - Smash altars only with an `altar-breaker` tag carried by the Core breaker (later demolishers inherit it), not with "power ≥ 75".
  - Raise lumenite to 85 (needs a viridium drill) and voidsteel to 100 (needs a lumenite drill).
  - Stop pre-Fracture viridium generation, or keep it but move the Alloy Anvil to post-Fracture by requiring the Core breaker's **fission slag** drop.
  - Match? Terraria needs each hardmode pick for the next ore: 100 / 110 / 150.
- **G5. The Astral sigil skips the endgame:**
  - Refuse unless `obelisks.down`, or change its recipe to need **obelisk cores** (one from each obelisk; the sigil needs all four).

**P1: stat growth and slots**

- **G7. Life ceiling:**
  - Match? Life cores give +10 ×10 (cap 200) against Terraria's +20 ×15 (400) + 5 ×20 (500).
  - Option A: life cores give +20 ×10 (cap 300), plus a hardmode **lifebloom pod** at +5 ×20 (cap 400), grown in the underground Verdant Canopy after the Fracture.
  - Option B: keep 200 and add +5 ×20 lifebloom pods (cap 300).
  - Either way, scale boss damage to the chosen ceiling.
- **G8. Extra accessory slot:**
  - Add an **auxiliary socket** (one-use) found only in the Fission Tyrant's Brutal salvage cache. It gives a 6th slot.
  - Store an `AuxSocketUsed` flag in `CharacterFileData` and extend `EquipmentSlot` with `Accessory6` (append-only).
- **G9. Permanent one-use upgrades:** a `PermanentUpgrades` bit field in the character file, with these original items:
  - **Nanite marrow**: +4 defence.
  - **Coolant pearl**: +charge regen.
  - **Fortune lattice**: +luck.
  - **Bait gland**: +fishing.
  - **Servo grease**: +mining/build speed.
  - **Fabricator manual**: +crafting-station reach.
  - **Lumen accord**: biome-tinted torches.
  - Sources: Brutal caches and late bosses.
- **G6. Natural first-boss arrival:**
  - The **Overseer patrol**: once max HP ≥ 140 and defence ≥ 6, a dusk roll may bring the Overseer mech with a warning line, like `spawnEye` and `AnyPlayerReadyToFightKingSlime`.
  - Reuse `HardBossNightChance`-style cooldown logic.

**P2: modes, NPCs, mobility and polish**

- **G11. Creative starting kit:** a **colony supply drop**:
  - ferrite rock drill
  - ferrite cutter
  - 3 recall capsules
  - grapple hook
  - 100 glow sticks
  - featherfall pill
  - glider wings
  Granted once on a Creative character's first world entry.
- **G10. Hook ladder:** three more hooks, each with range, speed and hook count:
  - **Scrap tether**: scrap-iron, short range.
  - **Mag-grapple**: the current hook.
  - **Twin lash**: two hooks, viridium, post-Fracture.
  - **Phase tether**: astral, instant retract.
- **G12. Dungeon timing:**
  - Re-key the Dungeon Guardian (`PhysicsContent.DungeonGateBoss`) to the Scout Mech, or to a new mid-tier **Derelict Warden**.
  - Make the Reactor key need a heart fragment, so the dungeon opens mid-way through pre-hardmode.
- **G13. Missing NPC tracks:**
  - **Field Zoologist**: bestiary at 10% or more.
  - **Pigment Trader**: dye material carried.
  - **Party Planner**: 10 residents.
  - Town pets via a **companion permit** bought from the Quartermaster.
- **G15. Unlock and death numbers:**
  - Match? The Medic needs > 120 (Terraria > 100).
  - Match? Classic drops 40% of credits (Terraria Softcore drops 50% of coins) and destroys half of each material stack (Terraria Softcore loses no items).
  - Match? The respawn timer is 5/3/20 s (Terraria about 10 s).
  - Consider adding a **Salvager** character difficulty (Mediumcore analog: drop all carried items at the death site).
- **G16. Brutal scaling:**
  - Match? Brutal is ×1.4 against Expert's ×2.
  - Consider a fourth world tier, **Annihilation** (Master analog): ×2.5, relics move here, and +1 slot from G8 is granted free.
- **G14. Progression achievements:**
  - **Crust Breaker**: cause the Fracture.
  - **Triple Threat**: the hard trio.
  - **Colossus Toppled**: the vault boss.
  - **Four Pillars Fall**: all obelisks.
  - **Full Vitality**: life at its cap.

---

**Unsure points:**
- Journey's full starting list.
- Terraria's respawn and Expert respawn values.
- The dungeon-brick pickaxe threshold, left out of the tables on purpose.
- The exact effect sizes of the 1.4.4 permanent buffs.

All of these are (knowledge), not evidence.

---

## Status: all gaps closed (2026-10-21)
The suite passed 446 checks at the time of this pass; the current suite is 550. The new suites were `ProgressionChainSelfTests`, `GrowthSelfTests`, `ModesSelfTests` and `DefenseSelfTests`.

| Gap | What was done |
|---|---|
| G0 | Explorer and Creative drop no credits on death. |
| G1 | Before the Fracture, radiated hearts summon the new Easy-tier **Fallout Hive-mind** (800 HP; summon item: Fallout hive cell). Spore hearts give cobalt, not viridium. Hard-trio kills before the Fracture don't count toward the trio. |
| G2 | Before the Fracture: Tainted tissue, the Prism catalyst and Royal nectar are refused. Altars, hives and brood pods give loot but no boss. Dusk incursions wait for the Scout Mech. |
| G4 | Altars need the `altar-breaker` capability: the Core breaker and later tools. Lumenite needs tool power 85, voidsteel 100. The Alloy Anvil needs **fission slag** from the Fission Tyrant. |
| G5 | The Astral sigil needs `obelisks.down`. |
| G6 | **Overseer patrol:** with max life ≥ 140 and defense ≥ 6, dusk has a 25% chance (3-day cooldown) of bringing the Overseer, with a warning line, until it is beaten. |
| G7 | **Lifebloom pods** (+5 ×20, after all life cores; cap 300). They grow on underground canopy mud after the Fracture. |
| G8 | **Auxiliary socket** (Fission Tyrant on Brutal+) opens accessory slot 6. Annihilation worlds open it for free. |
| G9 | Seven one-use permanent upgrades, dropped by late bosses (always on Brutal+, else 1 in 3): nanite marrow, coolant pearl, fortune lattice (+luck), bait gland, servo grease, fabricator manual, Lumen accord (biome-tinted light). |
| G3 | Tier 1–3 and early-mob wings are glide rigs with no climb. Climbing needs flight essence or tier 5+. **Salvaged thruster fins** are a rare early climbing pair found in chests. |
| G10 | Hook ladder: scrap tether, mag-grapple, **twin lash** (two hooks), **phase tether** (instant retract). |
| G11 | Creative characters get a colony supply drop once, on their first world entry. |
| G12 | The Dungeon Guardian gate is the Scout Mech. The Reactor key needs a blight heart fragment. |
| G13 | New town NPCs: **Field Zoologist** (bestiary ≥ 10%), **Pigment Trader** (carry a dye material), **Party Planner** (10 residents; throws a 3-hour colony party). The **companion permit** from the Quartermaster brings a town pet. |
| G14 | New achievements: Crust Breaker, Triple Threat, Colossus Toppled, Four Pillars Fall, and Full Vitality (300 life). |
| G15 | Rimtera's own death and unlock numbers are kept. New **Salvager** character mode: on death, every carried item drops at the death site for recovery. |
| G16 | Brutal stays ×1.4. New **Annihilation** world tier: ×2 creatures, hardmode ×2.6/×1.8, defense counts in full, relics drop only here, free sixth slot. |

Also: Annihilation is part of the "Hardened" secret seed, which now only raises a world's difficulty and never lowers it.
