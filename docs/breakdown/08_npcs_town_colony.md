# 08 · Town NPCs, housing and the colony: Rimtera vs Terraria

Area: every town NPC (move-in, shop, happiness, dialogue, services), housing validity, pylons, the travelling merchant, town pets and town gels, bound survivors, NPC death and respawn, the Station AI guide, and the RimWorld-side colony layer: crew colonists, their needs, moods, skills, jobs and AI, the storyteller and raids, the colony UI, drones and sentries on the HUD, and the "Crew 0" HUD line.

Method: I read the code. Pointers are `file:line` on `main` as of 2026-10-28; other agents are editing UI files, so UI line numbers may drift a little. I ran one scratch probe in a private build (`scratchpad/npc08/NpcProbe.cs`, results quoted as **Probe A–E**). Terraria evidence: CIL dumped from `members-ALL.json` with the evidence scripts (`WorldGen.StartRoomCheck`/`CheckRoom`, `Main.UpdateTime_SpawnTownNPCs`, `ShopHelper.ProcessMood`/`GetNearbyResidentNPCs`/`IsFarFromHome`/`LimitAndRoundMultiplier`). "(knowledge)" marks Terraria behaviour from general knowledge, not from the IL. Terraria is behavioural evidence only.

Status key: ✅ matches · ◐ partial · ❌ missing · ⚠ broken/bug · ➕ Rimtera-only (deliberate)

User ruling applied: **the player gets no automatic eating or healing.**

---

## Summary

**Counts (105 feature rows):** ✅ 27 · ◐ 40 · ❌ 7 · ⚠ 15 · ➕ 16.

The Terraria-style town layer mostly works. Arrivals by day (one per check), flag-gated move-ins and shops, housing reasons, death with a respawn delay, sheltering at night, the angler-style quest, the travelling merchant, pets, gels, pylons and the Station AI guide all run, and `ParityWorldTests` covers them. The gaps are in the tooling around them: the housing menu, the happiness model, reforging, and NPCs that cannot defend themselves.

The colony layer is the problem. Crew arrive like town NPCs, which is good, but then:
- they follow the player everywhere and leave their house;
- they have no jobs and cannot fight;
- they die permanently;
- they raise raid strength;
- they can smash the player's crafting stations or eat the player's food during mental breaks.

The RimWorld needs and mood system also still runs on the player character. Crew medics also auto-tend the player, which the new ruling forbids.

**Top 10 issues by player impact**

| # | Issue | Why it matters | Step |
|---|---|---|---|
| 1 | **Crew medics tend the player's wounds automatically** (`GameSimulation.cs:2126–2153`). The patient list is `ColonyPawns`, which includes the player (`GameSimulation.cs:301`), and the colonist sheet advertises it (`ColonistSheetState.cs:299`). | **Breaks the ruling: no automatic healing for the player.** | NC-1 |
| 2 | **Player hunger, rest, joy and comfort drain, and drive a RimWorld mood** (`GameSimulation.cs:650`, `2004–2033`, `2091–2094`). Probe E: after 144 game hours without eating, food = 0, rest = 0, mood 10 %, and "on the edge". At extreme risk the player works 1.35× slower (`Actors.cs:113–115`). Rest only recovers by sleeping in a bed. HUD bars show FOOD and REST (`GraphicalSandbox.cs:2406`). | There is no auto-eat, but the player still pays a RimWorld upkeep tax that Terraria never charges. Needs a ruling on what the bars do. | NC-2 |
| 3 | **Crew follow the player across the world** (distance > 9 → "Following", `GameSimulation.cs:1366–1408`). Their home is reset to wherever they stand near the player (`:1413`). Creatures may target them (`GameSimulation.PlayerSystems.cs:331`). | Crew trail you into caverns and boss fights. They never fight back (no attack code) and die permanently. | NC-3, NC-5 |
| 4 | **Crew have no jobs.** "Working around camp" only adds joy and comfort (`GameSimulation.cs:1394–1399`). "Foraging" makes food from nothing. No mining, hauling, building, farming, crafting or guarding. Skills never grow, although the UI says "passions learn faster" (`ColonistSheetState.cs:178`). | The colony half of the game gives the player nothing back. Each crew member also adds 16 raid threat points (`Storyteller.cs:138`). | NC-4, NC-17 |
| 5 | **Mental breaks damage the player's belongings.** Tantrum hits Workbench, Furnace, Anvil, turrets, hydroponics, solar and oxygen tiles (`GameSimulation.cs:1449–1453`, `1512–1531`). Food binge eats Berries from the player's inventory (`:1497–1510`). | A colonist you never asked for can wreck your crafting setup. This fights the Terraria loop. | NC-7 |
| 6 | **The housing panel lists crew, not town NPCs** (`InventoryUi.cs:551–553`). It has no list of residents, no assign or evict, and no banners. | Terraria's housing menu (query, assign, see who lives where) is the main town tool. Here "No crew yet." shows while ten NPCs live in town. | NC-8 |
| 7 | **Crew don't claim their room.** Probe B: the room a crew member moved into still reports "This room is suitable housing." `CheckHousing` only counts `_townNpcs` homes (`GameSimulation.Town.cs:28`). | A town NPC can move into the same room, and one house is counted twice. | NC-3 |
| 8 | **Unhappy residents leave the colony** at happiness ≤ −5 (`GameSimulation.Town.cs:46`). Probe C: an Archivist at −10.5 left on the first daytime check. It stays unlocked, so it moves back into any free house and can leave again. | Terraria NPCs never leave because they are unhappy (they only raise prices). You lose shops and see spam. | NC-9 |
| 9 | **No reforge service** (no Goblin Tinkerer analog). Calibrations only roll at crafting (`GameSimulation.cs:789`). The Mechanic survivor sells drone parts only. | A core Terraria gold sink and gear-polish loop is missing. | NC-12 |
| 10 | **The happiness model diverges.** It adds a score and turns it into a linear price (`GameSimulation.Town.cs:330–353`): no solitude bonus, no far-from-home or homeless 1.5 cap, and the sell price also moves. Pylons are crafted, not sold by happy NPCs. There is no happiness report text. Gels and the merchant count as neighbours and toward pylons. | Price feedback and pylon unlocks don't teach the player how to build a town. | NC-10, NC-11 |

Also worth noting:
- The "Crew 0 · Drones 0/1 · Sentries 0/1" HUD line always shows. Probe A: base drone and sentry slots are 1 (`GraphicalSandbox.cs:2368–2371`). Fixed in NC-16.
- Town NPCs never fight back. Fixed in NC-6.

**Blocks play / blocks the ruling:**
- NC-1 must land to honour the ruling.
- NC-2 needs a decision (see open questions).
- NC-3 is the main colony play-blocker: crew wander into danger and die with no way to stop them.

---

## Feature tables

### T1. Town NPC roster (one row per NPC kind)

Catalog: `Systems/TownNpcs.cs:198–321`, `Systems/Content/ContentFillTown.cs:43–56`, `Systems/Content/TownContent.cs:38–62`, `Systems/GameSimulation.Leisure.cs:199`. Unlocks: `GameSimulation.Town.cs:94–123`, `TownContent.cs:136–148`, `ContentFillTown.cs:122–139`, `FreeSurvivor` `GameSimulation.Town.cs:209`.

| # | Rimtera NPC (id) | Terraria analog | Move-in: Terraria vs Rimtera | Shop / service | Status |
|---|---|---|---|---|---|
| R1 | Station AI (0) | Guide | T: present from world start, homeless until housed (knowledge). R: always unlocked (`Town.cs:97`); a hologram at the landing site until housed (`Town.cs:178–187`); advice from `GuideAdvisor` (`GameSimulation.Guide.cs:84`) read from the recipe data. | Sells a work bench, torch and rope (the Guide sells nothing) | ✅ |
| R2 | Quartermaster (1) | Merchant | T: 50 silver carried, piggy bank included (knowledge). R: ≥ 50 credits **in the inventory only** (`Town.cs:98`). | 13 basics, none gated | ◐ |
| R3 | Medic (2) | Nurse | T: max life > 100 and the Merchant present (knowledge). R: max HP > 120, no merchant check (`Town.cs:101`). Probe A: a fresh player has 100 HP, so this needs 3 life cores rather than 1. | Heal for credits (see T2-S1) | ◐ |
| R4 | Armorer (3) | Arms Dealer | T: a gun or bullets carried (knowledge). R: any Ranged weapon except the Copper bow (`Town.cs:103–105`), so bows count too. | Ammo and guns, flag-gated | ◐ |
| R5 | Chief Engineer (4) | Steampunker or Mechanic role | R: after the first boss (`Town.cs:106`). | Wiring, drones, turrets | ➕ |
| R6 | Astro-Botanist (5) | Dryad / Witch Doctor | T Dryad: after any boss (knowledge). R: after placing hydroponics (`Town.cs:108`). | Purifier gel, planters; no purity report | ◐ |
| R7 | Head Researcher (6) | Wizard-like | R: after a heart breaks (`Town.cs:110`). | Charge (magic) gear | ➕ |
| R8 | Trade Consul (7) | (second Travelling Merchant) | R: ≥ 2 residents (`Town.cs:112`); a 1-in-6 chance per daytime hour (`Town.cs:70`); leaves at dusk (`Town.cs:46`). Overlaps with R22. | Mobility and accessory gear | ◐ |
| R9 | Tide-Warden (8) | Angler | T: found asleep on the beach (knowledge). R: carry anything with fishing power (`Town.cs:114`). | Daily quest fish (`GameSimulation.Fishing.cs:281–329`), rewards that scale with the count, milestone items | ✅ |
| R10 | Demolitionist (9) | Demolitionist | T: explosives carried and the Merchant present (knowledge). R: a bound survivor in worldgen structures (`GameSimulation.cs:231–237`). | Explosives | ◐ |
| R11 | Tailor (10) | Clothier / Stylist | T: bound NPCs gated by bosses or biomes (knowledge). R: bound from world start. | Cloth and vanity; no hair or style service | ◐ |
| R12 | Cartographer (11) | (none) | Bound | Info gear, beacon | ➕ |
| R13 | Mechanic (12) | Mechanic / Goblin Tinkerer | T: Mechanic bound in the dungeon after Skeletron; Goblin bound after the goblin army (knowledge). R: bound from world start. | Drone parts and turrets; **no reforge** | ◐ |
| R14 | Pilot (13) | (none) | Bound | Wings and hooks | ➕ |
| R15 | Cook (14) | (none; food vendor) | Bound | Food and the cooking station | ➕ |
| R16 | Arborist (15) | (none) | Bound | Cutters and timber | ➕ |
| R17 | Archivist (16) | (none) | Bound | Glyph blocks, vault core | ➕ |
| R18 | Isotopist (17) | Cyborg-like | R: after the core-gate boss (`Town.cs:117`) | Hardmode ores and rods | ✅ |
| R19 | Aeronaut (18) | (wings vendor) | R: after any boss of the hard trio (`Town.cs:119`) | Wings | ✅ |
| R20 | Relic Warden (19) | (post-Plantera vendor) | R: after the bulb boss (`Town.cs:121`) | Relics | ✅ |
| R21 | Greenskeeper (120) | Golfer | T: rescued underground in the desert (knowledge). R: after finding a golf ball. | Golf gear and kites. Shares the `npc_colony_worker` sprite (see T2-H11) | ✅ |
| R22 | Travelling Merchant (140) | Travelling Merchant | T: some mornings, needs ≥ 2 NPCs, leaves at dusk, random stock (knowledge). R: 45 % of days, 6–10 h, ≥ 2 residents, leaves at 18 h, 6 of 20 items seeded by day (`ContentFillTown.cs:14–31`, `97–120`). | Rotating stock. Prices move with happiness (Terraria uses its own price table, `ShopHelper.GetTravelingMerchantPrices`) | ✅ |
| R23–26 | Town gels ×4 (141–144) | Town slimes | T: each slime has its own discovery condition (knowledge). R: a 34 % roll per hour while the player stands in the gel's biome, once there are ≥ 3 residents (`ContentFillTown.cs:122–139`). | No shop, but the **"Sell held item" option still works on gels** (`GraphicalSandbox.cs:1101–1107`) | ◐ |
| R27 | Field Zoologist (160) | Zoologist | T: 10 % of the bestiary (knowledge). R: 10 % (`TownContent.cs:140`); rarer stock at 25 %. | Critter goods | ✅ |
| R28 | Pigment Trader (161) | Dye Trader | T: a dye material carried, plus a boss defeated (knowledge, medium confidence). R: a dye material carried (`TownContent.cs:144`). | Dyes and paintbrush | ◐ |
| R29 | Party Planner (162) | Party Girl | T: 14 NPCs; parties start at random (knowledge). R: 10 residents (`TownContent.cs:146`); parties on demand with a 24 h cooldown (`TownContent.cs:98–117`). | Party: joy for everyone plus Regeneration for the player, **player-triggered** | ◐ |
| R30 | Missing Terraria roles | Goblin Tinkerer (reforge), Stylist, Tax Collector, Truffle, Santa, Princess, Pirate, Skeleton Merchant, Tavernkeep | No analog. Reforge is the only one that matters for play. | n/a | ❌ |

### T2. Town systems

| # | Feature | Terraria | Rimtera (verified) | Status |
|---|---|---|---|---|
| S1 | Medic heal service | Nurse heals life and removes debuffs. Price scales with missing life, debuffs, progression and happiness (knowledge). | `HealAtMedic` (`Town.cs:412–423`): 0.5 credits × missing HP × price multiplier. Heals HP and injuries. Statuses are **not** cleared. Explicit player action. | ◐ |
| S2 | Angler quest | One fish a day, rewards scale with completions (knowledge). | ✅ as R9 | ✅ |
| S3 | Party service | Random parties (knowledge) | Player-triggered (R29) | ➕ |
| S4 | Reforge | Goblin Tinkerer rerolls the prefix for coins (knowledge) | None | ❌ |
| S5 | Station AI advice | Guide: crafting help and hints | `GuideTips` / `GuideAdvisor` (`GameSimulation.Guide.cs:70–330`) | ✅ |
| S6 | Pet permit | Licences bought from the Zoologist, used by hand (knowledge) | A licence bought from the Quartermaster (`TownNpcs.cs:211`) is filed automatically every half second while carried (`TownContent.cs:154`); the pet moves into a free house (`GameSimulation.Leisure.cs:510–546`). Petting gives the player 0.5 h Regeneration and joy (explicit action). | ◐ |
| S7 | Shop gating | Flags, bosses, biome, moon, time, hardmode (knowledge) | Flags only (`Town.cs:362–368`); merchant rotation | ◐ |
| S8 | Buy | Coins | Credits; price = value × multiplier, rounded up (`Town.cs:356–386`) | ✅ |
| S9 | Sell price | A fixed 1/5 of value, not affected by happiness (knowledge) | 0.2 × value × calibration ÷ multiplier (`Town.cs:389–392`) | ◐ |
| S10 | Dialogue | Many conditional lines: night, events, party, other NPCs present (knowledge) | 2–3 lines rotating by day, a mood suffix, the quest fish (`Town.cs:426–437`) | ◐ |
| S11 | Happiness report | A text report of each factor | One line: "Happiness +x · prices ×y" (`GraphicalSandbox.cs:1119`) | ◐ |
| H1 | Arrival cadence | Check every 7200 ticks (≈ 2 game hours), daytime only, one NPC per check (IL `Main.UpdateTime_SpawnTownNPCs`, `ldc.i4 7200`) | Hourly, day only, one per check (`Town.cs:37–90`) | ✅ |
| H2 | Room size | Minimum 60 room tiles (IL `StartRoomCheck`: `numRoomTiles` vs 60); maximum `maxRoomTiles` (750, knowledge) | 60 … 400 cells (`TownNpcs.cs:121`, `RegionMap.cs:55`) | ◐ |
| H3 | Background walls | Every interior tile needs a player-placed wall; natural walls are unsafe (knowledge) | ≥ 90 % of cells have any wall (`TownNpcs.cs:122`, `169`) | ◐ |
| H4 | Light, table, chair, door/platform | Required (knowledge, IL `RoomNeeds`) | Required, with reasons (`TownNpcs.cs:140–178`) | ✅ |
| H5 | One NPC per room | Yes; town pets may share (knowledge) | Town NPC claims enforced (`Town.cs:28`). Pets need an unclaimed room but don't block later NPCs (`Leisure.cs:532–546`). | ✅ |
| H6 | Crew claim rooms | n/a | **No.** Probe B: the crew member's room still reads "suitable" | ⚠ |
| H7 | Housing menu | Query a room, list NPCs and their homes, assign and evict, banners (knowledge) | "Check this room" plus a **crew** list (`InventoryUi.cs:535–560`); `P` key check at the cursor (`GraphicalSandbox.cs:472`) | ⚠ |
| H8 | Homeless NPC behaviour | Wanders near the spawn point | Walks toward the player (`Town.cs:312–316`) | ◐ |
| H9 | Shelter at night and in events | Go home at night and in invasions (knowledge) | `IsNight || Modifiers.Shelter` (`Town.cs:280`) | ✅ |
| H10 | NPCs fight back | Town NPCs attack nearby enemies: throw, shoot or cast (knowledge, `AI_007_TownEntities`) | None. NPCs only take contact damage (`Town.cs:242–272`). | ❌ |
| H11 | Distinct sprites | Each NPC is unique | 4 shared groups: `npc_colony_worker` (Head Researcher, Tide-Warden, Aeronaut, Greenskeeper), `npc_store_clerk` (Quartermaster, Trade Consul), `npc_base_engineer` (Chief Engineer, Isotopist), `npc_archivist` (Archivist, Relic Warden) | ◐ |
| H12 | Death and respawn | Dies; comes back through a later daytime spawn check (knowledge) | 160 HP, 2 HP/s regen; on death a 10 h respawn flag, then a normal arrival (`Town.cs:224–239`) | ✅ |
| H13 | Town NPC HP saved | n/a | `TownNpcSnapshot` has no HP, so HP resets to 160 on load (`TownNpcs.cs:104`) | ◐ |
| H14 | Town calms spawns | Fewer spawns near NPCs (knowledge) | `TownNpcsCalmingSpawns` (`GameSimulation.Spawning.cs:22`) | ✅ |
| H15 | Unhappy NPC leaves | Never | Leaves at ≤ −5 (`Town.cs:46`); Probe C | ⚠ |
| H16 | Bound survivors | Rescue by talking; then a normal resident (knowledge) | Talk frees them, they unlock and look for a house (`GameSimulation.Items.cs:594–598`, `Town.cs:209–216`) | ✅ |
| H17 | Survivor progression gating | Bound NPCs appear after their boss or event (knowledge) | All 8 are placed at worldgen, so they are available from day 1 | ◐ |
| P1 | Happiness: biome | Love/like/dislike/hate multiply the price (×0.88/×0.94/×1.06/×1.12, knowledge) | Adds ±1/±2 to a score; price = 1 − 0.06·score (`Town.cs:330–353`) | ◐ |
| P2 | Happiness: neighbours | Within 25 tiles (IL `GetNearbyResidentNPCs`, `ldc.r4 25`), multiplicative | Within 25 tiles ✅, ×0.75 per affection. Uses current positions, not homes. | ◐ |
| P3 | Crowding / solitude | > 3 neighbours: ×1.05 each; ≤ 2: ×0.95 (IL `ProcessMood`, `1.05`/`0.95`) | Crowding −0.5 per neighbour above 3 ✅; **no solitude bonus** | ◐ |
| P4 | Homeless / far from home / evil biome | Price to the maximum, clamped to 1.5 (IL `ProcessMood`: 1000; far = 120 tiles) | Homeless −2 (≈ ×1.12); no far-from-home or evil check | ◐ |
| P5 | Clamp and rounding | 0.75–1.5, rounded to 0.01 (IL `LimitAndRoundMultiplier`) | Clamped 0.75–1.5, not rounded (`Town.cs:353`) | ✅ |
| P6 | "Not really town NPC" | Pets, slimes and the merchant are excluded from neighbour counts (IL `IsNotReallyTownNPC`) | Gels and the merchant count as neighbours and crowd others | ◐ |
| P7 | Dead helper | n/a | `Systems/NpcHappiness.cs` (`PriceMultiplier` from biome tags) is never called | ⚠ |
| Y1 | Pylon source | Sold by an NPC whose happiness gives a price ≤ 0.9, in the matching biome (knowledge) | Crafted at the anvil (`GameSimulation.Travel.cs:173–178`) | ◐ |
| Y2 | Pylon activation | ≥ 2 NPCs nearby, in its biome, one per type (knowledge) | ≥ 2 housed residents within 40 tiles, biome checked on placement, one per type (`Travel.cs:431–445`) | ✅ |
| Y3 | Pylon count excludes pets, slimes, merchant | Yes (knowledge, medium confidence) | Gels and the merchant (who has a `HomeCell`) are counted (`Travel.cs:445`, `ContentFillTown.cs:117`) | ◐ |
| Y4 | Pylon blocked in danger or boss fight | Yes (knowledge) | Only downed or dead blocks it (`Travel.cs:479`) | ❌ |
| M1 | Merchant prices | A special table (IL `GetTravelingMerchantPrices`) | Happiness multiplier | ◐ |
| G1 | Gel discovery | Item or location conditions per slime (knowledge) | A biome roll (R23–26) | ◐ |
| G2 | Gels as residents | Take housing; don't trade (knowledge) | Take housing ✅; tradeable through Sell ⚠ | ⚠ |
| G3 | Gels and merchant feed crew capacity | n/a | `CrewCapacity` counts every housed resident, so gels and the merchant count (`Town.cs:193–194`) | ◐ |

### T3. Colony systems (RimWorld side)

| # | Feature | Reference | Rimtera (verified) | Status |
|---|---|---|---|---|
| C1 | Crew arrival through housing | (RimWorld wanderers) | One crew member per two housed residents, plus one per boss defeated, up to 5, by day into a free house (`Town.cs:80–90`, `193`). | ➕ |
| C2 | Crew cap counts **total joins** | n/a | `WanderersJoined < 5` (`Town.cs:81`). Dead crew are never replaced after 5 joins. | ◐ |
| C3 | Crew needs (food, rest, joy, comfort) | RimWorld needs | Drain (`SurvivalBehavior.cs:35–46`); restore at camp (`GameSimulation.cs:1377–1400`). Food comes from "Foraging" out of nothing. | ➕ |
| C4 | Crew routine | n/a | Follow the player if > 9 tiles away; home drifts to the player (`GameSimulation.cs:1366–1413`). | ⚠ |
| C5 | Crew jobs / work AI | RimWorld work types | None. "Working around camp" is a label (`GameSimulation.cs:1394–1399`). `ActivityKind` has only Eat, Rest, Mine and Idle (`AI/SurvivalBehavior.cs:11`), and Mine is unused for crew. | ❌ |
| C6 | Crew combat | Colonists fight | No attack code. Creatures may pick crew as targets (`GameSimulation.PlayerSystems.cs:331–345`, `366–386`). "Alert" is a label only. | ❌ |
| C7 | Crew death | Permanent in RimWorld | Permanent. Death memories go to relations (`GameSimulation.cs:1571–1581`). No respawn. | ◐ |
| C8 | Skills | Gain XP by doing | Fixed at creation. **No XP anywhere** (no writes to `SkillLevels`). The UI promises passions (`ColonistSheetState.cs:178`). | ⚠ |
| C9 | Skills used | n/a | Medical (tend quality), Plants (forage rate) | ◐ |
| C10 | Moods and thoughts | RimWorld | Memories, situational thoughts, mood easing (`GameSimulation.cs:2004–2117`, `Characters/Thoughts.cs`) | ➕ |
| C11 | Mental breaks | RimWorld | Thresholds, rolls, states (`Characters/MentalBreaks.cs`). Behaviours in `GameSimulation.cs:1457–1555`. | ➕ |
| C12 | Break: tantrum | n/a | Damages crafting stations and furniture near the colonist (`GameSimulation.cs:1449–1453`, `1512–1531`) | ⚠ |
| C13 | Break: food binge | n/a | Eats the **player's** Berries (`GameSimulation.cs:1497–1510`) | ⚠ |
| C14 | Relations and social | RimWorld | Opinions, interactions, social fights (`Characters/Relations.cs`; `GameSimulation.cs:2055–2059`) | ➕ |
| C15 | Crew medic tending | RimWorld doctors | Tends crew **and the player** (`GameSimulation.cs:2126–2153`). Probe D did not catch a tend within 1500 ticks, but the code path has no player exclusion. | ⚠ |
| C16 | Emotes | n/a | Crew and residents emote and wave back (`GameSimulation.PlayerSystems.cs:650–705`) | ✅ |
| C17 | Colony sheet UI | RimWorld pawn tabs | Overview, Skills, Health, Mood, Social and Crew pages, read-only (`UI/ColonistSheetState.cs:44–79`, `201–235`) | ◐ |
| C18 | Colony orders | Work priorities, draft, zones | None | ❌ |
| C19 | "Crew N" HUD | n/a | Always shows "Crew 0", plus "Drones 0/1 · Sentries 0/1" (Probe A: base slots 1) and the player's mood (`GraphicalSandbox.cs:2368–2371`) | ⚠ |
| C20 | Drones and sentries | Terraria minion and sentry slots | Combat-area feature (see 06); here only the HUD line | ✅ |
| W1 | Storyteller | RimWorld storyteller | Hourly roll, categories, refire cooldowns, adaptive rest, 72 h opening grace (`Storyteller.cs:81–336`) | ➕ |
| W2 | Raids | Terraria invasions approach the town (knowledge) | After the first boss; 2–8 biome creatures spawn just off-screen **around the player, wherever they are** (`GameSimulation.cs:2321–2350`) | ◐ |
| W3 | Raid strength from crew | n/a | +16 points per crew member (`Storyteller.cs:138`), but crew don't fight | ⚠ |
| W4 | Cold snap, heat wave, fallout, solar flare | n/a | Work. Exposure hurts the player (`GameSimulation.cs:1968–1983`). Cross-area. | ➕ |
| W5 | Cargo drop | n/a | Free Berries, arrows, bars and a biome shard (`GameSimulation.cs:2352–2376`) | ➕ |
| W6 | `WandererJoinsIncident` | n/a | Defined but not registered (`Storyteller.cs:107–108`); dead code | ◐ |

### T4. Player vs colonist automation (the ruling)

| # | Check | Rimtera (verified) | Status |
|---|---|---|---|
| A1 | Player auto-eats | Removed: `UpdateAutonomousBehavior` is a no-op (`GameSimulation.cs:1911–1919`); `Player.Brain = null` (`GameSimulation.cs:228`). Eating is manual (`EatSelectedOrBerry`, `:1246`). | ✅ |
| A2 | Player auto-rests or auto-heals at camp | Removed (same as A1) | ✅ |
| A3 | Player tended by crew medics | Yes (C15) | ⚠ |
| A4 | Player needs drain | Food, rest, joy and comfort drain (`GameSimulation.cs:650–653`); comfort drains with cold and rain (`:1968`, `GameSimulation.World.cs:521`) | ⚠ |
| A5 | Player mood and break risk | Thoughts and mood run for the player; "on the edge" message; at extreme risk × 1.35 cooldowns (`GameSimulation.cs:2024–2033`, `Actors.cs:108–115`). Probe E: mood 10 %, on the edge after 144 h unfed. | ⚠ |
| A6 | Player loses control in a break | No. The player is skipped (`GameSimulation.cs:2024–2033`). | ✅ |
| A7 | Party, petting, Medic heal | Each needs a deliberate player action | ✅ |
| A8 | Natural regeneration | Terraria-style `LifeRegenerationPerHour` (`GameSimulation.cs:661`). Not automation; covered by area 06. | ✅ |
| A9 | Player stats from colonist skills | Accuracy, melee, shooting and mining multipliers come from the backstory (`Actors.cs:99–101`, `GameSimulation.cs:1229`). Cross-area (06). | ◐ |

---

## Integration steps

**NC-1 · Player is never auto-tended** *(blocks the ruling)*
- **Change:** in `UpdateTending`, skip `patient.IsPlayerCharacter`. Change the sheet text to "Crew with Medical skill tend each other". Optional: a "Tend me" talk option on a crew medic, as an explicit action.
- **Files:** `Systems/GameSimulation.cs:2126–2153`, `UI/ColonistSheetState.cs:299`.
- **Size:** S. **Depends on:** none.
- **Acceptance:** a self-test puts a bleeding player beside a Medical ≥ 1 colonist for 48 game hours. The player's injuries are never tended, while a bleeding colonist next to them is.

**NC-2 · Settle the player's needs** *(needs the user's ruling, Q1)*
- **Recommended change:** stop advancing `Player.Needs` and drop the Hungry/Starving/Tired/Exhausted situational thoughts for the player. Remove `AtBreakingPoint` from `ActionCooldownScale`. Replace the FOOD/REST bars with nothing, or with a Well Fed buff indicator. Food then only gives buffs (`StatusId.WellFed` already exists in `ArsenalItems.Data.cs:646`).
- **Alternative (keep RimWorld flavour):** keep the needs, but make them buff-only: full means a small bonus, empty means no penalty.
- **Files:** `Systems/GameSimulation.cs:650–653`, `2024–2033`, `2091–2094`; `Entities/Actors.cs:108–115`; `UI/GraphicalSandbox.cs:2368`, `2406–2407`; `Systems/CharacterFile.cs:234`; `UI/ColonistSheetState.cs:122–139`.
- **Size:** M. **Depends on:** area 06 (food and buffs), area 09 (HUD).
- **Acceptance:** after 144 h with no eating or sleeping, the player's mood is unchanged by needs, `ActionCooldownScale == 1`, and no "on the edge" message appears. Eating cooked food applies Well Fed.

**NC-3 · Crew live at home and claim their room** *(colony play-blocker)*
- **Change:**
  - Give `ColonistAgent` a `HomeCell`, set it on arrival (`Town.cs:84–89`).
  - `CheckHousing`/`FindFreeHouse` treat crew homes as claimed.
  - Remove "Following" and the `HomePosition = player` drift (`GameSimulation.cs:1366–1413`). Crew wander within about 12 tiles of home, shelter at night and in events like residents, and stay home in danger.
  - Save `HomeCell` (`ColonistSnapshot`).
- **Files:** `Entities/Actors.cs:792+`, `Systems/GameSimulation.cs:1322–1434`, `Systems/GameSimulation.Town.cs:24–31`, `80–90`, `125–138`, `Systems/GameSaveData.cs`.
- **Size:** M. **Depends on:** NC-8 (the UI shows crew homes).
- **Acceptance:** Probe B again: the crew room reports "already claimed". After the player walks 150 tiles away for 2 game hours, every crew member is within 12 tiles of home. Save and load keep homes.

**NC-4 · Give crew real jobs that feed the Terraria loop** *(scope needs Q2)*
- **Change:** a small job system. Candidate jobs, picked by the user:
  - **Gardener:** harvest planters and hydroponics into the nearest chest.
  - **Hauler:** carry ground items inside the town radius into chests.
  - **Cook:** turn raw food in a chest into Well Fed food at a cooking station.
  - **Guard:** see NC-5.
  - **Miner:** dig a player-marked area into a chest.
  Skills set the speed. Jobs only use chests and stations, never the player's inventory.
- **Files:** a new `Systems/GameSimulation.CrewJobs.cs`; `AI/SurvivalBehavior.cs` (`ActivityKind`); a work page in `UI/ColonistSheetState.cs`.
- **Size:** L. **Depends on:** NC-3; area 05 (tiles and chests), area 01 (recipes).
- **Acceptance:** with a gardener and ripe planters, the chest gains produce each game day. Nothing leaves the player's inventory. A self-test per job.

**NC-5 · Crew defend the town**
- **Change:** a crew member with a weapon kind (from the Shooting or Melee skill) attacks hostiles within 10 tiles of home. Damage scales with skill and with bosses defeated. Crew flee indoors below 30 % HP. Also count only combat-capable crew in raid threat (W3).
- **Files:** `Systems/GameSimulation.cs:1322+`, `Systems/GameSimulation.PlayerSystems.cs:325–386`, `Systems/Storyteller.cs:135–140`.
- **Size:** M. **Depends on:** NC-3; area 06 (projectile API).
- **Acceptance:** a slime placed 6 tiles from a housed guard dies without the player acting. Raid points don't rise for crew with no combat skill.

**NC-6 · Town NPCs fight back**
- **Change:** each resident gets a simple attack (thrown or shot projectile; the damage table depends on its role and on progression flags) when a hostile is within 8 tiles by day. Gels and pets stay passive.
- **Files:** `Systems/GameSimulation.Town.cs:242–322`, `Systems/TownNpcs.cs` (an attack field on the definition).
- **Size:** M. **Depends on:** area 06 (projectiles), area 03 (enemy hit API).
- **Acceptance:** a resident next to a weak hostile kills it, or hurts it, within 10 s, and takes less total damage than now.

**NC-7 · Mental breaks never cost the player**
- **Change:**
  - Tantrum: shout and kick harmlessly, or damage only crew-owned decor; never stations.
  - Food binge: eat from a colony chest or "forage"; never `Player.Inventory`.
  - `TryStartBreak(hasFood:)` reads colony food.
- **Files:** `Systems/GameSimulation.cs:1449–1531`, `2040–2044`.
- **Size:** S. **Depends on:** none.
- **Acceptance:** force each break kind for 24 h beside a Workbench, Furnace and Anvil with Berries in the player's pack. Every tile and the Berry count are unchanged.

**NC-8 · A real housing menu**
- **Change:**
  - List every resident, crew member and pet with its home, happiness and price multiplier.
  - "Check room" at the player or the cursor.
  - **Assign** (pick an NPC, click a valid room, which sets `HomeCell`) and **Evict** (clears it).
  - Optional banner markers on claimed rooms.
- **Files:** `UI/InventoryUi.cs:535–560`; a new `GameSimulation.AssignHome(TownNpc|ColonistAgent, GridPoint)` and `Evict` in `Systems/GameSimulation.Town.cs`.
- **Size:** M. **Depends on:** NC-3; area 09 (UI framework).
- **Acceptance:** a test assigns the Quartermaster to room 2; `HomeCell` is in room 2 and room 1 becomes free. The panel lists ten residents by name.

**NC-9 · Unhappy residents stay**
- **Change:** delete the `GetHappiness ≤ −5` departure (`Town.cs:46`). Keep the Trade Consul's dusk exit.
- **Files:** `Systems/GameSimulation.Town.cs:42–51`.
- **Size:** S. **Depends on:** none.
- **Acceptance:** Probe C again: an Archivist at −10.5 is still present after 48 h, with prices ×1.50.

**NC-10 · Happiness parity**
- **Change:**
  - Multiplicative factors: biome love/like/dislike/hate ×0.88/0.94/1.06/1.12; the same for neighbours.
  - Solitude ×0.95 at ≤ 2 neighbours; crowding ×1.05 per neighbour above 3.
  - Homeless or > 120 tiles from home: clamp to 1.5. Round to 0.01.
  - Neighbours measured from homes, excluding gels, pets, the merchant and bound survivors.
  - The sell price no longer divides by the multiplier.
  - Add a happiness report: one line per factor.
  - Delete the dead `Systems/NpcHappiness.cs`.
- **Files:** `Systems/GameSimulation.Town.cs:326–392`, `UI/GraphicalSandbox.cs:1119`, `Systems/NpcHappiness.cs`.
- **Size:** M. **Depends on:** none.
- **Acceptance:** numeric self-tests cover love biome alone (0.88 × 0.95 → 0.84), crowded (5 neighbours → ×1.1025), homeless (1.50), and a gel next door (no change). Sell price equals value / 5.

**NC-11 · Pylons tied to happy towns**
- **Change:**
  - A resident whose price ≤ 0.90 and whose home is in a pylon biome sells that pylon (keep the recipe or drop it, Q4).
  - Pylon activity counts only real residents (no gels or merchant).
  - Teleport is refused while a boss lives or a hostile is within 10 tiles.
- **Files:** `Systems/GameSimulation.Travel.cs:173–178`, `431–445`, `476–480`; `Systems/GameSimulation.Town.cs:356`.
- **Size:** M. **Depends on:** NC-10; area 05 or travel owner.
- **Acceptance:** a happy Field Zoologist in the canopy offers the Canopy pylon, and loses the offer when crowded. A pylon with 1 resident and 2 gels is dormant. Teleporting with a slime adjacent fails with a message.

**NC-12 · Reforge service**
- **Change:** the Mechanic (or Chief Engineer, Q3) gets "Recalibrate held item". Cost = item value × ⅓ × the happiness multiplier. Roll a new `Calibration` from the item's group (the same roller as crafting, `GameSimulation.cs:789`). Allow tools.
- **Files:** `Systems/GameSimulation.Town.cs` (new `Recalibrate`), `UI/GraphicalSandbox.cs:1110` (menu entry).
- **Size:** M. **Depends on:** area 06 (IC-13 reforge, calibration groups).
- **Acceptance:** recalibrating a sword 50 times charges credits each time, gives at least 5 distinct calibrations, never one outside the melee group, and refuses with too few credits.

**NC-13 · Medic parity**
- **Change:** the heal also clears harmful statuses. Cost = (missing HP + 2 per debuff) × a progression factor (1 + 0.5 per boss tier) × the multiplier.
- **Files:** `Systems/GameSimulation.Town.cs:412–423`.
- **Size:** S. **Depends on:** area 06 (status list).
- **Acceptance:** a poisoned player at 50/100 HP pays more after a boss kill than before, and leaves without Poison.

**NC-14 · Move-in tuning**
- **Change:**
  - Medic: max HP > 100 and the Quartermaster present.
  - Armorer: firearm or bullet ammo, not bows.
  - Quartermaster: count credits in the cargo pod (bank) too.
  - Gate the bound survivors by progression. Spawn them at worldgen, but bind them only after their boss: for example the Mechanic after the first dungeon boss, the Tailor after the Stone Sentinel (Q5).
  - Party Planner at 14 residents (or keep 10 as a deliberate choice).
  - Pigment Trader: also needs any boss.
- **Files:** `Systems/GameSimulation.Town.cs:94–123`, `Systems/Content/TownContent.cs:136–148`, `Systems/GameSimulation.cs:231–237`.
- **Size:** S–M. **Depends on:** area 01 (progression order).
- **Acceptance:** a fresh player with one life core (110 HP) and a housed Quartermaster gets the Medic. A bow alone does not unlock the Armorer.

**NC-15 · Gels and pets don't trade**
- **Change:** hide "Shop" and "Sell held item" when the definition's shop is empty or `IsGel`. `Sell` returns 0 for gels.
- **Files:** `UI/GraphicalSandbox.cs:1099–1108`, `Systems/GameSimulation.Town.cs:395–409`.
- **Size:** S. **Depends on:** none.
- **Acceptance:** `Sell(gel, slot)` returns 0 and the talk menu has no shop entries.

**NC-16 · HUD clean-up ("Crew 0")**
- **Change:** show "Crew n" only when n > 0. Show drones and sentries only while one is active (or a summon item is held). Drop "Mood" from the line if NC-2 removes player mood.
- **Files:** `UI/GraphicalSandbox.cs:2368–2371`.
- **Size:** S. **Depends on:** NC-2, area 09.
- **Acceptance:** a fresh world's HUD line reads "Day 1 · time · DEF · credits" only. With 1 crew member, "Crew 1" appears.

**NC-17 · Skills grow, or stop promising it**
- **Change:** add XP from tending, foraging and jobs (NC-4), with passions × 1.5 / × 2. Otherwise remove the passion text.
- **Files:** `Characters/ColonistSystem.cs` (`SkillLevels` becomes mutable and saved), `Systems/GameSimulation.cs:2126+`, `UI/ColonistSheetState.cs:178`.
- **Size:** M (or S to remove). **Depends on:** NC-4.
- **Acceptance:** a medic who tends 30 times gains a Medical level, and a burning passion gains faster than none.

**NC-18 · Crew replacement**
- **Change:** cap on **living** crew (5), with a 24 h delay after a death before a newcomer, like the 10 h NPC respawn flag.
- **Files:** `Systems/GameSimulation.Town.cs:81–90`, `Systems/Storyteller.cs:436`.
- **Size:** S. **Depends on:** NC-3.
- **Acceptance:** after 5 joins and 1 death, a sixth crew member arrives 24 h later if a house is free.

**NC-19 · Raids hit the colony, not the player's tunnel**
- **Change:** when the player is within about 150 tiles of the colony centre (the housed-resident centroid), spawn the raid at the colony edge and walk it in. Otherwise hold the raid until the player returns, as Terraria invasions approach spawn. Threat counts only combat crew (NC-5).
- **Files:** `Systems/GameSimulation.cs:2321–2350`, `Systems/Storyteller.cs:338–354`.
- **Size:** M. **Depends on:** NC-3, NC-5; area 03 or events (invasion overlap).
- **Acceptance:** a raid forced while the player is 300 tiles down a cave doesn't spawn there. It fires when the player returns, spawning ≥ 40 tiles from the nearest house and moving toward it.

**NC-20 · Save resident HP**
- **Change:** add HP to `TownNpcSnapshot` as an optional trailing field.
- **Files:** `Systems/TownNpcs.cs:104`, `Systems/GameSimulation.Town.cs:439–459`.
- **Size:** S. **Depends on:** none.
- **Acceptance:** a resident hurt to 40 HP saves and loads at 40 HP.

**NC-21 · Dead code**
- **Change:** remove `Systems/NpcHappiness.cs` (if NC-10 doesn't) and the unregistered `WandererJoinsIncident`, keeping the `MaximumCrew` constant elsewhere.
- **Files:** `Systems/NpcHappiness.cs`, `Systems/Storyteller.cs:434–451`.
- **Size:** S. **Depends on:** NC-18.
- **Acceptance:** the build passes with no references.

**NC-22 · Distinct sprites for the 4 shared groups**
- **Change:** add `npc_head_researcher`, `npc_tide_warden`, `npc_aeronaut`, `npc_greenskeeper`, `npc_trade_consul`, `npc_isotopist` and `npc_relic_warden` to the art queue.
- **Files:** `Systems/TownNpcs.cs`, `Systems/GameSimulation.Leisure.cs:199`, `docs/ART_PROMPTS.md`.
- **Size:** S (code) + art. **Depends on:** the ComfyUI art run (paused; resume only on the user's go).
- **Acceptance:** a self-test finds no two town NPC kinds sharing a sprite name.

**NC-23 · Dialogue depth**
- **Change:** add conditional lines for night, party, blood-moon-like events, a liked or disliked neighbour present (by name), low HP, and boss progress. Show a happiness report button (from NC-10).
- **Files:** `Systems/GameSimulation.Town.cs:426–437`, the catalog files.
- **Size:** M. **Depends on:** NC-10.
- **Acceptance:** during a party the Party Planner says a party line, and the Medic mentions a disliked neighbour when one lives within 25 tiles.

**NC-24 · Missing Terraria roles** *(decide per Q6)*
- **Change:** pick from the Stylist (hair and colour change service, using `CharacterLook`), a Tax-collector analog (passive credits from housed residents), and a Truffle analog (fungal-biome vendor). Skip the rest.
- **Files:** catalog files, `Systems/GameSimulation.Town.cs`.
- **Size:** M each. **Depends on:** area 01.
- **Acceptance:** per role.

**NC-25 · Colony orders UI (only if NC-4 lands)**
- **Change:** a Work page with per-crew job toggles and a "Stay home / Guard" stance.
- **Files:** `UI/ColonistSheetState.cs`.
- **Size:** M. **Depends on:** NC-4, NC-5.
- **Acceptance:** turning a crew member's Hauler job off stops them hauling within 1 game hour.

Suggested order:
1. NC-1, NC-7, NC-9, NC-15, NC-16, NC-20, NC-21 (all S, safe).
2. NC-2 (after the ruling).
3. NC-3, NC-8, NC-18.
4. NC-10, NC-11, NC-12, NC-13, NC-14.
5. NC-5, NC-6, NC-19.
6. NC-4, NC-17, NC-25.
7. NC-22, NC-23, NC-24.

---

## Cross-area dependencies

- **01 Progression and crafting:** survivor gating by boss (NC-14) and the order of new NPC roles (NC-24).
- **03 Enemies and creatures:** NPC and crew attacks need an enemy-hit API (NC-5, NC-6). Raid creature lists overlap with event invasions (NC-19).
- **05 World and tiles:** chests and stations as job targets (NC-4); room detection (`RegionMap`) for crew homes (NC-3); pylon placement (NC-11).
- **06 Items and combat:** reforge (IC-13 ↔ NC-12); Well Fed and status clearing (NC-2, NC-13); projectiles for defenders (NC-5, NC-6); the RimWorld body on the player (IC-4) overlaps with A4/A5 here; minion and sentry HUD numbers (NC-16).
- **09 Presentation and engine:** the HUD line (NC-16), the housing menu (NC-8), the colony work page (NC-25), sprites (NC-22).

---

## Open questions for the user

1. **Player needs:** should the player keep FOOD and REST bars at all?
   - My recommendation: no hunger, rest, joy or comfort upkeep for the player. Food gives buffs only (Well Fed).
   - Alternative: keep the bars as buff-only.
   - Right now an unfed player ends up "on the edge" and, at the extreme, works 35 % slower.
2. **What should crew be for?** Pick the jobs that matter (gardener, hauler, cook, guard, miner), or keep crew as social flavour that stays home and guards. Today they follow you, can't fight and die.
3. **Reforge NPC:** should the Mechanic (a bound survivor) or the Chief Engineer offer recalibration?
4. **Pylons:** keep the anvil recipe, switch to Terraria's "happy NPC sells it", or both?
5. **Survivors:** should the 8 bound survivors stay available from day 1, or be gated behind bosses as in Terraria?
6. **Extra roles:** do you want a Stylist, a Tax-collector analog or a Truffle analog?
7. **Trade Consul vs Travelling Merchant:** two dusk-leaving traders overlap. Merge them, or make the Consul a permanent resident?
8. **Raids:** keep RimWorld-style raids alongside Terraria-style invasions, or fold raids into the event system (NC-19)?
