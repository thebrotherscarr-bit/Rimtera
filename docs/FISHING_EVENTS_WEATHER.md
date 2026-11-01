# Fishing, events, world triggers and weather: Terraria structure vs Rimtera

Evidence: REA metadata of `Terraria.exe` 1.4.5.8 — `docs/terraria-evidence/fishing.json` (`FishingAttempt`, `Projectile`, `Player`), `triggers.json` (`NPC`, `Cloud`, `Rain`, `AmbienceServer`, `MoonlordCountdown`), plus `events.json`, `main.json`, `worldgen.json`. Member names and structure only; every number, table, item and name in Rimtera is original.

## 1. Observed structure

### Fishing
- **Cast and wait:** a bobber projectile (`AI_061_FishingBobber`) floats on the liquid surface (`GetWaterLine`), with splash effects and fake fish swimming nearby (`SpawnFakeFishInWater`).
- **Context:** `TryBuildFishingContext` / `PrepareFishingContext` builds a `FishingAttempt`:
  - liquid type (`inLava`, `inHoney`, `CanFishInLava`)
  - pool size (`GetFishingPondWidth`, `GetFishingPondState`, `waterNeededToFish`)
  - fishing level from `Player.GetFishingConditions` (best pole `Fishing_GetBestFishingPole`, bait `Fishing_GetBait`, multipliers `Fishing_GetPowerMultiplier`, accessories `accFishingLine`/`accLavaFishing`/`accFishFinder`, `fishingSkill`, crate potion)
  - biome and depth of the bobber
- **Roll:** `FishingCheck` → `FishingCheck_RollDropLevels` (rarity tiers) → `FishingCheck_RollItemDrop` (biome/depth/liquid tables, `crate`) → `FishingCheck_ProbeForQuestFish` (`questFish`) → `FishingCheck_RollEnemySpawns` (fishing up enemies, which can include bosses) → `SetFishingCheckResults`.
- **Reel:** `ItemCheck_PullFishingBobbers` / `PullBobber` / `ConsumeBait` / `AI_061_..._GiveItemToPlayer`.
- **Crates:** `OpenFishingCrate`.
- **Quest NPC:** daily quest fish (`Main.anglerQuest`, `AnglerQuestSwap`, `anglerWhoFinishedToday`), rewards scaling with completed count (`GetAnglerReward_*`, `anglerQuestsFinished`).
- **Displays:** fish-finder style info display (`displayedFishingInfo`).
- **Weather link:** rain raises bite chance and transforms some catches (`NPC.FishTransformationDuringRain`).

### World time, triggers and progression
- **Time loop:** `Main.UpdateTime` runs with a time rate (`dayRate`, `UpdateTimeRate`). Day and night transitions roll events (`UpdateTime_StartDay`, `UpdateTime_StartNight`), and town NPC arrivals are checked periodically (`UpdateTime_SpawnTownNPCs`).
- **Progression flags:** a large set of `downed*` flags (bosses, invasions, moon events) plus `NPC.SetEventFlagCleared` / `OnGameEventClearedForTheFirstTime` drives what can happen next. NPC arrivals are gated by `unlocked*Spawn` flags and `SpawnAllowed_*` checks.
- **Death triggers:** `DoDeathEvents` celebrates boss deaths, advances slime-rain kill quotas and spawns follow-up effects.
- **World triggers:**
  - breaking orbs or hearts (`CheckOrb`, `DestroyHeart`) can start a meteor or an invasion and summons a boss on the third
  - smashing altars seeds ore (`SmashAltar`)
  - meteors fall (`HandleMeteorFall`)
  - hardmode starts from a boss kill
  - countdown events (`MoonlordCountdown`) and summon rituals (`CultistRitual`)
- **Seasons:** `checkHalloween`, `checkXMas`.

### Weather
- **Rain:** `StartRain`, `ChangeRain`, `StopRain`, `rainTime`, `maxRaining`, `get_IsItStorming`, plus rain particles (`Rain.MakeRain`, `GetRainFallVelocity`).
- **Wind:** `windSpeedTarget`/`windSpeedCurrent`, `windCounter`, `extremeWindCounter`, `UpdateWindyDayState` ("windy day"). Wind drives rain angle, kite drops and butterfly rules (`TooWindyForButterflies`), with ambient wind physics (`_ambientWindSys`).
- **Clouds:** `Cloud.UpdateClouds`, `addCloud`, rare clouds, `cloudAlpha` and a cloud background layer.
- **Weather update:** a dedicated `UpdateWeather` ties the above to the time loop; a sandstorm has severity and intended severity and needs sufficient wind.
- **Ambience:** `AmbienceServer` spawns background entities (sky and underworld) when the player can see them.
- **Weather events:** slime rain (kill quota summons a boss) and ladybug rain.

## 2. Rimtera before this pass

| Area | Rimtera has | Gap |
|---|---|---|
| Fishing | nothing (bug net and fishing rod sprites only) | the whole loop |
| Time | fixed clock, dawn/dusk boundaries, storyteller hourly | no time rate, seasons or day counter triggers |
| Progression | defeated-boss list, Fracture flag, hearts/altars counters | no general world-flag registry, no first-clear hooks, no flag-gated NPC unlocks |
| World triggers | hearts → boss, altars → ore, Fracture | no meteor fall, no invasion start on hearts, no slime-rain-style quota events |
| Weather | storyteller temperature shifts, toxic fallout overlay | no rain, wind, clouds, storms or sandstorm |
| Events | content agent building the framework (doc CONTENT_EXPANSION 2.5) | — |

## 3. Rimtera design (original)

### 3.1 Fishing
- **Rods:** each of the 7 tiers has a rod (fishing power 10–60) plus a few special rods.
- **Bait:** critters caught with the bug net become bait with a bait power of 5–50; worms, grubs and fireflies are low tier, golden critters high. Bait is consumed on a bite with a chance that falls as bait power rises.
- **Accessories:** line (never breaks), lure (+power), Acid Wader (fish in acid), Lava Lure (fish in lava), Catch Scanner (shows the current pool's catch table).
- **Cast:** the bobber is a projectile that lands on liquid. A bite comes after a random wait that shrinks with power and with rain, and a short reel window opens.
- **Catch roll:** power = rod + bait + accessories + buffs, × pool factor (pools under 75 cells are penalised), × time-of-day factor (dawn and dusk best), × rain bonus.
  - Rarity tiers: Junk, Common, Uncommon, Rare, Epic, Legendary.
  - Tables keyed by liquid (water, acid, lava, nectar), biome zone (all Rimtera zones including the blight types) and depth (surface, underground, caverns, chasm).
- **Catches:** fish (raw food, cookable), materials, crates per biome and tier (open for loot), junk, quest fish, rare accessories and weapons, and enemies (e.g. "Bloodfin Ambusher" during Rust Moon).
- **Boss:** a fishing-summoned boss, the Abyss Leviathan, from a special bait used in acid.
- **Quest NPC:** the Angler-analog "Tide-Warden" town NPC gives one quest fish per day, rewards scale with completed quests, and new quest fish unlock with progress.

### 3.2 World flags and triggers
- **Flag registry:** a `WorldFlags` registry (persisted) with named flags and counters, for example:
  - `boss.<kind>.down`, `event.<id>.cleared`, `blight.fracture`, `hearts.broken`
  - `npc.<id>.unlocked`, `quests.fishing`
- **Hooks:** `OnFirstClear` fires one-time unlocks and messages.
- **Triggers** (each a small rule: condition → action, evaluated on its own signal):
  - Breaking a blight heart: 50% meteor fall that night, and a Scavenger Raid becomes eligible.
  - Third heart: the boss (already implemented).
  - First Fracture: Crystal Spires start spreading and Dark Sun becomes eligible.
  - First Overseer kill: Swarm Rain becomes eligible.
  - Altar smash: ore seeding plus a chance of a vengeful wisp.
  - Kill quotas: Swarm Rain → Alpha Skitter; Rust Moon kill score → wave-based escalation.
  - Countdowns: Astral Sigil use → a 60 s countdown → Astro-God.
  - Town NPC unlocks: set by flags, not by inventory scans.
- **Time:** a time-rate multiplier (sleeping in a bed during the night speeds time up to 4×), a day counter and two seasonal windows by real date (harvest festival, starfall) that swap a few spawns and decorations.

### 3.3 Weather
- **State:** `WeatherState` (persisted) holds cloud cover 0–1, rain intensity 0–1, wind (signed, tiles/s), a storm flag, a sandstorm/dust intensity and fog.
- **Changes:** targets are rolled at dawn, noon, dusk and midnight from biome climate (Frozen → snow instead of rain, Junk → dust storms, Acid Coast → acid rain, Canopy → frequent rain) and current events. Current values ease toward their targets.
- **Effects:**
  - rain fills open-air liquid cells slowly (acid rain near the coast and blight)
  - rain boosts fishing, slows fire and wets pawns (cold and comfort via the temperature model)
  - storms add lightning strikes that can hit tall metal structures or lure creatures
  - wind pushes projectiles and particles, makes kites and paragliders possible and moves clouds
  - dust storms cut visibility and increase Junk spawns
  - snow covers surface ground over time in the Frozen zone
  - fog darkens the light map
- **Ambience:** background clouds, rain and snow particles, flying sky-critters and distant silhouettes, spawned only where the player can see open sky.

### 3.4 Events (complements CONTENT_EXPANSION 2.5)
- **Lifecycle:** each event is an `IWorldEvent` with eligibility (world flags, day number, biome, weather), natural rolls on time boundaries, cooldown days, a duration or kill quota, waves with point values, a spawn-table override, a progress bar, rewards and a save state.
- **Clearing:** first clears set `event.<id>.cleared` and can unlock town NPCs and items.
- **Weather events:** rain, storm, dust storm, blizzard and acid rain are part of this model, so they can gate other events (Swarm Rain only during rain, Aurora Night only on clear cold nights).

## 4. Status at 0.2.0 (2026-11-01)

The design in §3 is built: the fishing loop with the Tide-Warden and the Abyss Leviathan (`Fishing.cs`,
`GameSimulation.Fishing.cs`), the world flag registry (`WorldFlags.cs`), the weather model (`Weather.cs`) and the event
framework (`WorldEvents.cs`). §2 describes the state before that pass. What has changed since, and what still differs
from Terraria (the details are in `docs/breakdown/04_events_weather.md`, with its "0.2.0 status" column):

| Area | Status | Notes |
|---|---|---|
| Fishing | ✅ | Crates hold hardmode ores only after the Fracture. Fishing finds of tier 4 and above are on the craft audit's whitelist (`docs/CRAFTING_PROGRESSION.md` §6) |
| Clock | ✅ changed (0.1.11) | Terraria's 24-minute day (15 minutes of day, 9 of night). The 4× sleep rate in §3.2 still applies, but sleep no longer drains buffs. Crops, arrivals, events and the storyteller hang on the clock, so they now run six times slower in real time |
| Event ends | ✅ (0.1.11) | Moons end at dawn and day events at dusk. Outlasting an event is not clearing it |
| Obelisk shields and the second event system | ❌ | Shields still drop when the siege times out, and the legacy `EventManager` still rolls three hidden events (EV-1, EV-2: planned for parity step 0.1.7, not done, not scheduled) |
| Event spawn areas | ❌ | Event rosters still take spawn rolls anywhere, including caves and the dungeon (EV-3 → 0.4.0) |
| Natural triggers | ◐ | Several triggers differ from Terraria's shape, such as Swarm Rain needing rain and invasions rolling at dusk (EV-5 → 0.4.0) |
| Defense Protocol | ◐ | A kill quota, not a defend-the-core event (EV-7 → 0.4.0) |
| Event skies and lighting | ❌ | No red sky on a Rust Moon and no dark sun (EV-8, PE-10 → 0.4.0). Event music is in |
| Weather source | ◐ | Rolled from the zone the player stands in, not from the world's climate (EV-9 → 0.4.0) |
| Lightning and wind | ◐ | Lightning depends on the frame rate; wind does not push the player (EV-10, EV-11 → 0.4.0) |
| Seasons by real date | ➕ | Kept as designed (harvest festival, starfall) |
