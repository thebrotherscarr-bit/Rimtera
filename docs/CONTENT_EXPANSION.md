# Content expansion: biomes, tiles, classes, items, events, town NPCs

Evidence: REA static metadata of `Terraria.exe` 1.4.5.8 — `docs/terraria-evidence/item.json` (`Terraria.Item`), `events.json` (`Sandstorm`, `BirthdayParty`, `LanternNight`, `DD2Event`, `CultistRitual`, `MoonlordCountdown`), `town.json` (`TownRoomManager`, `ShopHelper`), `main.json` (`Main` time/invasion/moon members). Only member names and structure were used. Every Rimtera name, number, item, event and NPC below is original.

## 1. Observed structure

**Items.** One item record carries everything: damage class flags (`melee`, `ranged`, `magic`, `summon`), `damage`, `crit`, `knockBack`, `useTime`/`useAnimation`, `autoReuse`, `channel`, `shoot`/`shootSpeed`, `ammo`/`useAmmo`, `mana`, `sentry`, `staff`, tool powers (`pick`, `axe`, `hammer`), placement (`createTile`, `createWall`, `tileBoost`), armor slots (`headSlot`/`bodySlot`/`legSlot`, `defense`, `vanity`), accessory slots (`accessory`, wing/shoe/shield/back slots), consumables (`healLife`, `healMana`, `buffType`/`buffTime`, `potion`, `consumable`), `rare`, `value`, `maxStack`, `prefix` (random modifiers), and `makeNPC` (critters as items).

**Events.** Each event is a small state machine with `UpdateTime`, natural start rolls at dusk/dawn (`CheckNight`, `CheckMorning`, `NaturalAttempt`), cooldown days, manual toggles and a world-clear reset (party, lantern night). Weather runs on intensity (`Sandstorm`: `Severity`, `IntendedSeverity`, `TimeLeft`, `HasSufficientWind`; `Main.StartRain`/`ChangeRain`/`slimeRain`). Invasions are point-and-wave driven (`StartInvasion`, `invasionSize`, `invasionProgress`/`Wave`, `UpdateInvasion`, `DD2Event` waves with per-enemy points and required kills, `DownedInvasionT1..T3`). Moon events (`bloodMoon`, `eclipse`, `pumpkinMoon`, `snowMoon`) feed spawn tables and a progress display. Progress bars are a UI layer (`DrawInvasionProgress`).

**Town NPCs.** Arrival is checked during time updates (`UpdateTime_SpawnTownNPCs`); each NPC needs a valid room (`TownRoomManager`: `HasRoom`, `SetRoom`, `KickOut`, `CanNPCsLiveWithEachOther`). Happiness comes from biome and neighbour preferences (`ShopHelper`: `LoveBiome`/`LikeBiome`/`DislikeBiome`/`HateBiome`, the same for NPCs, `RuinMoodIfHomeless`, `IsFarFromHome`) and scales shop prices between a low and high multiplier.

## 2. Rimtera design

### 2.1 Combat classes

| Class | Resource | Weapons | Stat | Set-bonus flavour |
|---|---|---|---|---|
| **Close Quarters** (melee) | — | blades, mauls, spears, tethers (whip-like), throwing discs | Melee skill | defense, lifesteal, thorns |
| **Ballistics** (ranged) | ammo (cells, slugs, rockets, darts) | rifles, pistols, launchers, crossbows, flamers | Shooting skill | ammo saving, crit |
| **Tech** (magic) | **Charge** (regenerating; mana analog) | emitters, beam rods, datapads (spell books), arc gloves | Intellectual skill | charge regen, beam width |
| **Command** (summoner) | drone slots + sentry slots | drone remotes (minions), turret kits (sentries), signal whips that buff drones | Social skill | drone slots, drone damage |

Every weapon has a class, damage, crit, knockback, use time, auto-reuse, projectile and speed, and ammo or charge cost. Damage scales with the matching colonist skill (ties into the RimWorld-style skill sheet). Crafted weapons roll a **calibration** (prefix): Precise, Overclocked, Reinforced, Featherweight, Volatile, Cracked, etc.

### 2.2 Progression tiers

| Tier | Material | Gate |
|---|---|---|
| 1 | Ferrite / Scrap-iron | start |
| 2 | Cobalt | Ferrite drill |
| 3 | Glacial, Pyrocite, Uranium (biome trio) | Overseer mech |
| 4 | Viridium | first blight heart broken |
| 5 | **Lumenite** (Crystal Spires) | the Fracture |
| 6 | **Voidsteel** (altar ore) | altar smashing |
| 7 | **Astral alloy** | Astro-God |

Each tier has a bar, drill, cutter (axe), demolisher (wall hammer), one weapon per class, a three-piece armor set per class (from tier 3), and a set bonus. That is about 190 progression items before biome drops and consumables.

### 2.3 New biomes

- **Acid Coast** (world edges, ocean analog): acid sea, salt crust, coral-like crystal reefs, shell beaches, tide-pool critters, sea creatures, Acid-tide event.
- **Fungal Hollows** (underground, mushroom analog): glowshroom grass, giant mushrooms, spore-lit caves, fungal creatures, Spore Monarch mini-boss.
- **Verdant Canopy** (surface jungle and underground Bloom Hives): mud, canopy trees, hive comb, nectar, bees analog "Pollen drones", Hive Queen analog "Bloom Matriarch".
- **Crystal Spires** (post-Fracture good biome, hallow analog): prismatic grass, pearl stone, crystal shards; spreads like the blight and competes with it.
- Mini-biomes: **Geode Chambers** (gems), **Basalt Columns** and **Alabaster Halls** (granite/marble analogs), **Mite Warrens** (spider-nest analog).
- Structures: **Precursor Vault** (late temple analog, locked by a key from the Verdant boss), derelict crash ship (existing), buried caches, sky islands.

### 2.4 New tiles (≈70)

Natural: mud, clay, gravel, silt, salt crust, acid sand, shell sand, coral reef, ash, slate, marble-like alabaster, basalt column, geode crystal (6 colours), hive comb, nectar, pearl stone, prismatic grass, glowshroom grass, giant mushroom cap/stem, canopy leaves, tree trunk/branches (growing trees with saplings), five moss colours, cobweb-like "mite silk", thin ice, meteorite.

Crafted: bricks for every stone and ore, glass, reinforced glass, five plating colours, astro-timber planks, platforms (wood/metal/glass), doors, trapdoors, chairs, tables, beds, chests (per biome), shelves, lamps and glow-lamps (per crystal colour), banners, signs, paintings, plant pots, herb planters, conveyor, ladders.

Walls: a natural and a crafted wall for every block above.

### 2.5 Events

| Event | Type | Trigger | Effect |
|---|---|---|---|
| Rainfall / Acid rain | weather | random at dawn/dusk; acid near blight or coast | spawns, mood, crop growth; acid hurts unsheltered pawns |
| Dust storm | weather | Junk Wasteland, windy days | visibility, spawns, scrap drops |
| Blizzard | weather | Frozen Wasteland | cold, slows movement |
| **Rust Moon** | night | 1 in N eligible nights after HP ≥ 120 | husks rise, doors open, stronger spawns |
| **Dark Sun** | day | post-Fracture, rare dawn roll | eclipse creatures |
| **Swarm Rain** | day | after first boss | swarm bugs fall from the sky; kill quota summons the Alpha Skitter |
| **Scavenger Raid** | invasion | after breaking a blight heart, or summoned with a Scrap Beacon | wave invasion with a progress bar |
| **Pirate Boarding** | invasion | post-Fracture; summoned with a Pirate Transmitter | ends with the Cyber-Pirate Captain |
| **Defense Protocol** | invasion | summoned at the Station AI with a Signal Core | tower-defense waves; three tiers |
| **Precursor Probes** | invasion | random after Voidsteel | late saucer-style invasion |
| **Aurora Night** | calm night | random, cooldown | no hostile spawns, mood buff, sky lights |
| **Colony Festival** | party | random or manual; needs 3+ town NPCs | celebration, mood buff, festival items |
| **Meteor fall** | world | breaking a blight heart | crater of meteorite ore |

Each event uses one shared shape: eligibility, natural roll, duration or kill quota, waves, spawn-table override, progress bar, rewards, cooldown, save state. Boss summon items exist for every boss.

### 2.6 Town NPCs and survivors

Housing: a RegionMap room is a valid **Quarters** if it is enclosed, has backing walls, at least 60 cells, a light source, a door, a table-like and a seat-like furniture tile, and is not already claimed. Arrival is checked hourly; each NPC has a condition:

| NPC (sprite) | Arrives when | Sells / does |
|---|---|---|
| Station AI (guide) | always | recipes, hints |
| Quartermaster | you hold 50+ credits | basics, lamps, rope |
| Medic | max HP above 120 or a colonist is injured | heals for credits, stims |
| Armorer (security commander) | you own a ballistic weapon | ammo, guns |
| Chief Engineer | Overseer mech defeated | wiring, tools, drones |
| Astro-Botanist | a hydroponics tray is placed | seeds, herbs, purifier gel |
| Head Researcher | first blight heart broken | datapads, charge stims |
| Trade Consul | random days, leaves at dusk | rotating rare stock |
| Survivors (rescued) | found bound in structures; free them | become specialists (demolitions, tailor, cartographer…) |

Happiness: each NPC loves, likes, dislikes or hates biomes and neighbours, plus crowding. It sets shop prices, feeds the colony mood system, and an unhappy NPC can leave. Currency: **credits**, dropped by creatures and sold for at NPCs.

### 2.7 Implementation order

1. Item model: classes, charge, drone slots, calibrations, rarity, value, tiers; the full progression catalog; crafting tree; loot tables.
2. Tiles and walls; trees and herbs; biome generation passes for the new biomes and mini-biomes.
3. Event framework and events; invasion progress bar; boss summon items.
4. Housing, town NPC arrival, happiness, shops and credits; survivors.
5. Art: sheets from `docs/ART_PROMPTS.md`, extracted by `tools/sprites`; until then, generated placeholder tints.
