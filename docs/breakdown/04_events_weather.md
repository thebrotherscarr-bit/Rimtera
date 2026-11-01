# 04 · Events, invasions and weather: Rimtera vs Terraria

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open. An arrow gives the milestone `docs/PLAN.md` now plans the step for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "Not done, not scheduled" marks steps of parity 0.1.7 and 0.1.8 that no release shipped and no milestone holds yet: release 0.1.7 did not carry parity step 0.1.7's fix list. "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

Read-only audit (2026-10-28, branch `main` with uncommitted work). Terraria v1.4.5.8 is behavioural evidence only. Nothing here copies its code, names or assets.

**Evidence used**
- Terraria CIL dumped with `docs/terraria-evidence/full/scripts/ilall.py` and `lite.py` into a private scratch folder (not the repo). Methods dumped: `Main.UpdateTime`, `UpdateTime_StartDay`, `UpdateTime_StartNight`, `UpdateTimeRate`, `StartInvasion`, `UpdateInvasion`, `InvasionWarning`, `UpdateWeather`, `StartRain`, `StopRain`, `ChangeRain`, `UpdateWindyDayState`, `get_IsItStorming`, `NewLightning`, `StartSlimeRain`, `startPumpkinMoon`, `startSnowMoon`, `stopMoonEvent`, the `Sandstorm::*`, `LanternNight::*`, `BirthdayParty::*`, `DD2Event::*` and `CultistRitual::*` types, `Cloud.UpdateClouds`, `Rain.MakeRain`, `WorldGen.dropMeteor`, `NPC.SpawnOnPlayer`, `NPC.SetEventFlagCleared` and `Spawner.GetSpawnRate`. The repo already held `cil/Spawner__ShouldSpawnInvasionEnemies.il.txt` and `Spawner__SlimeRainSpawns.il.txt`. `callers.py` was used for wind, graveyard and lantern-night cross-references. Offsets are quoted as `Method@IL`; "(knowledge)" marks behaviour not confirmed in the dump.
- Rimtera: a code read plus one scratch probe (`EvProbe`), built in a private copy and never in the repo. The probe ran 12 in-game days with every gate flag set and counted natural starts and weather shares. It also started a Hollow Moon at 23:00 and ran it to the end, forced rain and checked the spawn roster, played a full Scavenger Raid, measured lightning at 60 Hz and at 4 Hz, started a Windy Day, and sampled 1,000 natural rolls per event. Results are quoted as **(probe)**.
- These docs were checked against the code: `docs/FISHING_EVENTS_WEATHER.md` §3.2-3.4, `TERRARIA_GAPS.md` rows 31-34 (all "✅") and `parity/D_world.md` D1/D7/D8. Their "✅" verdicts are too generous; see the tables. D8's "UI hookup pending" note is out of date: the renderer now passes `SkyLightLevel` (`UI/GraphicalSandbox.cs:2096`).

**Code map (Rimtera)**

| Piece | Where |
|---|---|
| Event framework (specs, natural rolls, waves, quotas, rewards, save) | `Systems/WorldEvents.cs` (`WorldEventSystem`, `BuildSpecs` at :424-507) |
| Wave moons, Cryo Brigade and Windy Day specs; moon scoring | `Systems/GameSimulation.Seasons.cs` |
| Legacy second event system (mothfall, deep-earth tremors, hostile incursion) | `Systems/EventManager.cs`, registered in `Systems/GameSimulation.cs:239-264` and advanced at `:2405` |
| Host: clock boundaries, triggers, meteor, weather effects, lightning, sleep | `Systems/GameSimulation.World.cs` (`AdvanceContentClock` :441, `ApplyWeatherEffects` :509, `StrikeLightning` :601, `MeteorStrike` :358) |
| Weather state, rolls and easing | `Systems/Weather.cs` |
| World flags and first-set hooks | `Systems/WorldFlags.cs`, `WireTriggers` in `GameSimulation.World.cs:222` |
| Clock, moon phase, sky light | `GameSimulation.cs:631-650, 2385-2415`; `GameSimulation.World.cs:81-140` |
| Event spawning (roster takeover, multipliers) | `GameSimulation.Spawning.cs:46-108` |
| Obelisk sieges, cultist ritual, astral countdown | `GameSimulation.Hardmode.cs:440-520`; `GameSimulation.World.cs:401, 481` |
| Lumen Tempest and grave field | `GameSimulation.CreatureCoverage.cs:20-60, 166-190` |
| Wind on items and projectiles, kites, windy-day wind | `GameSimulation.Leisure.cs:256-270, 371-460` |
| Event starter items | `Content/ContentItems.cs:533-539`, `CreatureCoverage.cs:166-179`, `GameSimulation.Leisure.cs:125-130`; use in `GameSimulation.Items.cs:103-120` |
| UI: event bars, weather and lightning, clouds, moon, audio | `UI/GraphicalSandbox.cs:2163-2245, 2926-2950`; `GraphicalSandbox.Sky.cs:40-95`; `GraphicalSandbox.Leisure.cs:131`; `UI/AudioDirector.cs:91-133` |

---

## Summary

**Counts (73 rows):**

| Status | Time and moon (T) | Weather (W) | Framework (F) | Events (E) | Total |
|---|---|---|---|---|---|
| ✅ matches | 4 | 5 | 4 | 11 | **24** |
| ◐ partial | 3 | 10 | 1 | 18 | **32** |
| ❌ missing | 2 | 1 | 3 | 1 | **7** |
| ⚠ broken / bug | 0 | 2 | 1 | 3 | **6** |
| ➕ Rimtera-only (deliberate) | 0 | 4 | 0 | 0 | **4** |

Each row is counted once, by its gap. Some ✅ and ◐ event rows also note a deliberate ➕ extra, such as final bosses or reward bundles.

**Top 10 issues by player impact**

1. **The day is about 6× shorter than Terraria's** (◐ T1). A full cycle lasts **250 real seconds (probe)**: 0.08 game hours per second, with nights 1.5× faster (`GameSimulation.cs:42`, `World.cs:93`). Terraria's cycle is 24 minutes: the day ends at `time > 54000` and the night at `time > 32400` ticks (`UpdateTime@2873/@2820`). Every event chance is per in-game day, so events come about 6× more often per real hour (a Rust Moon roughly every 35 real minutes). Each one is also short: a natural Rust Moon lasts about 100 s against 9 minutes for a Blood Moon. **This needs a user decision** (open question 1).
2. **Obelisk shields drop on their own after 48 game hours** (⚠ E-L2). The siege specs are quota events, not invasions (`Waves` = 0), so `Advance` ends them when `HoursLeft` runs out (`WorldEvents.cs:263-266`). `UpdateObeliskShields` then finds no state and clears the shield (`Hardmode.cs:488-491`). At the current clock that happens after about 8 real minutes. Terraria's pillar shields only fall to kills.
3. **A second, hidden event system runs alongside the real one** (⚠ F2). `EventManager` (`GameSimulation.cs:239-264`) still rolls "Moth-ray swarm" (16% at dusk), "Deep-core tremors" (8% at dawn) and "Hostile incursion" (30% at dusk once an easy boss is down; it spawns a random event boss, `TrySpawnEventBoss` :2421). These events have no progress bar, are not saved, are not counted as events, and silently multiply spawns (`Spawning.cs:46`). The probe saw 3 of them in 12 days.
4. **Event rosters ignore where the player is** (⚠ W5 and E-L2). `PickSpawn` hands 70% of all spawn rolls to any active event's roster (`WorldEvents.cs:178-186`), whatever the depth or biome. Rain, acid rain, blizzard and dust-storm creatures therefore spawn in caves and in the dungeon (probe C: during rain `PickSpawn(0.1)` = DrizzleGel), and obelisk rosters spawn across the whole map. Terraria spawns rain, invasion and slime-rain enemies only on the surface near the invasion front (`Spawner.ShouldSpawnInvasionEnemies@25-72`), and pillar enemies only near their pillar.
5. **Night and day events overrun their boundaries, and moons "clear" when they time out** (⚠ E-PM2, ◐ E-BM5, ◐ E-SE4). Every event ends on a fixed duration, never at dawn or dusk. A Hollow Moon started at 23:00 ended at **08:00, with `event.hollow_moon.cleared` set and completions = 1 at wave 1 (probe B)**: expiry counts as a clear (`WorldEvents.cs:266`, `completed: !IsInvasion && KillsPerWave == 0`). An item-started Rust Moon at 02:00 runs until 13:00. Terraria ends moon events at dawn (`UpdateTime_StartDay@89-131`, `stopMoonEvent`) and eclipses at nightfall (`UpdateTime@1891-1897`).
6. **Events change nothing in the sky, the light or the music** (❌ F3, F4, F5). There is no red sky or red moon on a Rust Moon, no darkened sun on a Dark Sun, no pillar, festival, aurora or graveyard visuals, and no per-event music. `SkyLightLevel` reads only time, moon phase and weather (`World.cs:121-136`). `AudioDirector` plays one "event" sting and raises tension to 0.7 (`AudioDirector.cs:101-130`).
7. **The Old One's Army analog is just a kill quota** (❌ E-OOA). Defense Protocol has no crystal to defend, no fail state, no difficulty tiers, no breaks between waves and no medals. Terraria's DD2 event has all of these (`DD2Event.StartInvasion`, `CheckProgress`, `Difficulty_1..3_GetEnemiesForWave`, `WinInvasionInternal`, `ShouldBlockBuilding`, `DropMedals`).
8. **The Martian Madness analog arrives in early hardmode with no probe to trigger it** (⚠ E-MM1). Precursor Probes rolls 5% at dusk once `ore.voidsteel` is set, which happens at the third altar smash (`Blight.cs:304`). Terraria's Martian Madness comes after Golem and starts when a Martian Probe escapes after scanning the player (knowledge; `StartInvasion@134-215` sets the type-4 size and position).
9. **Weather is rolled from the zone the player stands in, not from the world's climate** (◐ W1, W4, W15, W22). `RollTargets(Day, slot, CurrentBiome.Zone, …)` runs at four fixed hours (`World.cs:449-453`). Snow or dust rolled in the Frozen or Junk zone keeps falling after you walk to the grassland. Lightning depends on the frame rate: **14 strikes per minute at 60 Hz against 0 at 4 Hz in a forced storm (probe)**, because there is one 25% roll per 60 ticks. Wind never pushes the player (❌ W11). Fog stays above 0.2 for 21% of the time (probe).
10. **Natural triggers differ from Terraria's** (◐ across E):
   - Swarm Rain *needs* rain; Terraria's slime rain needs *no* rain (`UpdateTime@717`).
   - The Rust Moon ignores the dark moon; Terraria blocks the Blood Moon on a new moon (`StartNight@921-927`).
   - Scavengers and pirates roll at dusk; Terraria's invasions roll at dawn (`StartDay@446-626`).
   - Aurora Night is never set up by a first boss clear; Terraria lights a lantern night after one (`NPC.OnGameEventClearedForTheFirstTime` stores `LanternNight.NextNightIsLanternNight`).
   - There is no 1-in-50 meteor night after the second boss (`StartNight@96-121`).

**Blocks play?** Nothing crashes, and no soft-lock was found. Issue 2 breaks the endgame gate, because obelisks become killable without the siege. Issue 3 can drop a 1,200-1,500 HP event boss on an early player at dusk (G2c in `QUEUED_WORK.md` only limits this before the Fracture). Treat both as **must-fix before the next release**.

---

## Feature tables

### T. Clock, moon phases and time rate

| # | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| T1 | Length of a day and night | Day lasts 54,000 ticks and night 32,400 at 60 Hz, so a full cycle is 24 min (`UpdateTime@2820` night ends at `time > 32400`, `@2873` day ends at `time > 54000`) | 0.08 game h per real second, nights at 1.5× pace (`GameSimulation.cs:42, 648-649`; `World.cs:93`); **250 s per cycle (probe)** | ◐ (6× faster; open question) | ✅ EV-17 0.1.11 |
| T2 | Day-to-night split | 62.5 / 37.5 | about 60 / 40 (`NightClockPace`) | ✅ | ✅ as audited |
| T3 | Moon phase | 8 phases, +1 at each day start (`StartDay@169-183`) | 8 phases, flipping at dawn and derived from the clock (`World.cs:101-109`) | ✅ | ✅ as audited |
| T4 | Effects of the moon phase | No Blood Moon on a new moon (`StartNight@921-927`); moon phase also affects fishing and luck (knowledge) | Luck only (`PlayerSystems.cs:250-253`); the Rust Moon and fishing ignore the phase | ◐ | ❌ EV-5 → 0.4.0 |
| T5 | Sleep and time rate | All players asleep → fast-forward (`UpdateTimeRate@61-86`); creative freeze (`@92-103`) | ×4 in a bed at night; refused or woken by events (`World.cs:402-427, 472-476`); creative freeze (`Creative.cs:71`) | ✅ | ✅ as audited |
| T6 | Sundial / moondial skip and cooldowns | `Sundialing`/`Moondialing` skip to dawn or dusk; the cooldowns reset on a Blood Moon or eclipse (`StartNight@1018-1027`, `StartDay@314-320`) | none | ❌ | ❌ EV-15 → 0.4.0 |
| T7 | Real-date seasons | `checkHalloween`, `checkXMas` at each day start (`StartDay@121-126`) gate seasonal drops and items | `SeasonFor`: Harvest (1 Oct-10 Nov), Starfall (10 Dec-6 Jan) make festivals, meteors and seasonal fish more likely (`World.cs:144-148, 467-470`) | ◐ (deliberate reskin; nothing seasonal is *gated*) | ◐ unchanged since the audit |
| T8 | Sky light from time, moon and weather | Moon brightness, cloud dimming (knowledge) | `SkyLightLevel` (`World.cs:121-136`), now used by the renderer (`GraphicalSandbox.cs:2096`) | ✅ | ✅ as audited |
| T9 | Clock, moon and weather readouts (watch, sextant, weather radio) | Info accessories show the time, moon phase, weather and wind | `MoonPhaseName` is never referenced by the UI; there is no weather or moon info line | ❌ | ❌ EV-16 → 0.4.0 |

### W. Weather simulation

| # | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| W1 | Rain start and stop | A random roll each tick, 1/(3600×5.75) with a clear sky or 1/(3600×4.25) when cloudy (`UpdateTime@579-666`); `rainTime` sets the length (`StartRain@480-500`); `StopRain`. No slime rain or lantern night may be running (`@411-431`). | Deterministic targets rolled at 06/12/18/00 from the player's *current zone* (`Weather.cs:59-100`, `World.cs:449-453`); rain > 0.35 for **26.6% of the time (probe)** | ◐ | ❌ EV-9 → 0.4.0 |
| W2 | Rain intensity and changes mid-rain | `maxRaining` and `cloudAlpha`; `ChangeRain` re-rolls during rain (`UpdateTime@380-396`, `ChangeRain@258-286`) | `Rain` 0.3-1.0, eased toward the target (`Weather.cs:120-135`) | ✅ | ✅ as audited |
| W3 | Cloud cover | `numClouds` is driven by `cloudAlpha`, and `weatherCounter` (3,600-10,800 ticks) re-rolls it (`UpdateWeather@1761-2437`) | `CloudCover` rolled and eased; sprite or ellipse clouds scale with cover and drift with the wind (`Sky.cs:77-95`, `GraphicalSandbox.cs:2222-2240`) | ✅ | ✅ as audited |
| W4 | Snow | Rain falls as snow in the snow biome (knowledge) | `Snow` is targeted only if the player stood in Frozen at the roll. It keeps falling elsewhere, and snowflakes are drawn whenever `Snow > Rain` (`GraphicalSandbox.cs:2185`). Snow settles on Frozen ground (`World.cs:582-598`). | ◐ | ❌ EV-9 → 0.4.0 |
| W5 | Rain's effect on spawns | `maxSpawns += maxSpawns × cloudAlpha` and a slightly faster rate (`GetSpawnRate@905-935`); rain enemies spawn on the surface only | The Rain event: ×1.1 spawns, and its roster takes 70% of rolls **everywhere, underground and in the dungeon too** (probe C; `WorldEvents.cs:178-186`) | ⚠ | ❌ EV-3 → 0.4.0 |
| W6 | Rain fills water | No | `FillBasinsWithRain` around the player (`World.cs:534-572`) | ➕ | ➕ kept |
| W7 | Thunderstorm and lightning | Storming means `cloudAlpha ≥ _maxRain` and `|windTarget| ≥ _maxWind` (`UpdateWindyDayState@121-178`). Lightning is a flash and a sound only, with a random delay (`UpdateWeather@29-330, @735-739`; `NewLightning`). | The `Storm` flag is rolled when precipitation > 0.75 (50%). Strikes do 25 damage to the player and 60 to creatures and favour metal (`World.cs:601-623`). There is one 25% roll per 60 ticks (`GameSimulation.cs:44, 1938`): **14 strikes/min at 60 Hz, 0 at 4 Hz (probe)**. | ⚠ (frame-rate dependent) | ❌ EV-10 → 0.4.0 |
| W8 | Rain and storm audio | Rain ambience, storm music (`_shouldUseStormMusic`) | Rain loop `SetRain`, thunder sound (`AudioDirector.cs:91-111`); no storm theme | ◐ | ◐ PE-17 partly (after 0.1.7: biome and weather ambience beds) |
| W9 | Wind model | Target ±0.8; changes every 7,200-28,800 ticks plus an extreme-wind counter (`UpdateWeather@1068-1744`); current eases toward target × (1 + 0.556 × rain) (`@851-910`) | `Wind` ±3 tiles/s (±6 with dust), rolled 4×/day, a linear ease, no link to rain (`Weather.cs:96, 120-135`) | ◐ | ❌ EV-11 → 0.4.0 |
| W10 | Wind on projectiles | `Projectile.ShouldUseWindPhysics` (`Main.windPhysics` && not `Sets.WindPhysicsImmunity`); `Projectile.Update@1378-1438` adds `windSpeedCurrent × windPhysicsStrength` | Player ranged and thrown shots are pushed in open air (`Leisure.cs:382-390`). Content weapons *also* get `WindDrift` at spawn (`Combat.cs:322`), so they are pushed twice. | ◐ | ❌ EV-11 → 0.4.0 |
| W11 | Wind on the player | The `WindPushed` buff and `Player.windPushed` flag move the player (`Player.HorizontalMovement@251`, `UpdateBuffs@17069`) | none | ❌ | ❌ EV-11 → 0.4.0 |
| W12 | Wind on dropped items | No dump evidence | Airborne items are pushed (`Leisure.cs:376-381`) | ➕ | ➕ kept |
| W13 | Kites | Need `Projectile.MinimumWindStrengthToFlyKite`; a `LeashedKite` follows the wind | 3 kites; open sky and \|wind\| ≥ 0.8 needed; the line follows the wind and drops when the wind dies (`Leisure.cs:395-460`) | ✅ | ✅ as audited |
| W14 | Windy Day | A *state*, not an event: day 03:00-12:00 equivalent (`time` 10,800-43,200), `|windTarget| ≥ _maxWind` and a clear sky → windy music (`UpdateWindyDayState@32-105`); windy-day critters and enemies | An *event* rolled at 10% at dawn when dry, lasting 10 h. It forces wind to ±7.5 (`Leisure.cs:267-268`; probe G wind = -7.5) and brings its own roster (`Seasons.cs:39-42`). No music. | ◐ | ❌ EV-12 → 0.4.0 |
| W15 | Sandstorm | Starts only with wind ≥ 0.6 (`Sandstorm.HasSufficientWind`). `TimeLeft` is 28,800-86,400 ticks (`StartSandstorm`) and burns 15× faster when the wind drops (`UpdateTime@57-104`). Severity 0.4-1.4 (`ChangeSeverityIntentions`). Desert only: ×0.9 rate and ×1.2 cap (`GetSpawnRate@1057-1110`). | `Dust` is rolled only with the player in the Junk zone (30%), with wind ±6. The overlay, visibility and spawn ×1.35/1.25 (+×1.3 in Junk) follow, plus a 4-creature roster (`WorldEvents.cs:404-406`). No wind gate, and it persists outside the biome. | ◐ | ❌ EV-9 → 0.4.0 |
| W16 | Blizzard | Snow biome with rain and strong wind; blizzard shader (`BlizzardShaderData.windSpeed`) | The event runs when `Snow > 0.5`, with wind -5 and a 4-creature roster (`WorldEvents.cs:407-411`, `World.cs:205-207`) | ◐ | ❌ EV-9 → 0.4.0 |
| W17 | Fog | Graveyard fog only (`DrawSurfaceBG_GetFogPower` reads `GraveyardVisualIntensity`) | Weather fog: 30% at dawn, 8% otherwise; above 0.2 for **21% of the time (probe)**; darkens the view (`Weather.cs:93`, `GraphicalSandbox.cs:2196-2199`) | ➕ | ➕ kept |
| W18 | Acid rain | No | Acid rain near the coast, the deeps or the Fracture. It burns the player outdoors (6 HP/h × rain) and fills basins with acid (`World.cs:514-523`) | ➕ | ➕ kept |
| W19 | Ambient weather visuals | `Rain.MakeRain`, `AmbientWindSystem`, sky entities (meteor, lost kite), swaying tiles | Rain and snow streaks slanted by the wind, a dust tint, fog, lightning bolts and flashes (`GraphicalSandbox.cs:2163-2218`); no swaying flora or sky entities | ◐ | ❌ PE-13 → 0.4.0 |
| W20 | Rain boosts fishing | Rain raises fishing power (knowledge) | A shorter bite wait and more power with rain (`GameSimulation.Fishing.cs:157, 189`) | ✅ | ✅ as audited |
| W21 | Weather is saved | World file | `ContentSaveState.Weather` (`World.cs:625-648`) | ✅ | ✅ as audited |
| W22 | Weather scope | Global, per world | Global value but rolled from the player's local zone; acid-rain damage and rosters apply anywhere | ◐ | ❌ EV-9 → 0.4.0 |

### F. Event framework

| # | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| F1 | Shared event shape: eligibility, natural roll, duration or quota, waves, roster, rewards, cooldown, save | Each event is hand-coded (`invasionType`, `bloodMoon`, `eclipse`, `pumpkinMoon`, `DD2Event` …) | `WorldEventSpec` / `WorldEventSystem` with save via `WorldEventSnapshot` (`WorldEvents.cs:76-336`). Rolls are deterministic per seed, day and event; the 1,000-day hit rates match their chances (probe E: Rust Moon 104/1000 at 0.12, Scavenger 106 at 0.10, Dark Sun 47 at 0.06). | ✅ | ✅ as audited |
| F2 | One event system | — | **Two systems.** Legacy `EventManager` (mothfall 16% at dusk, deep-earth 8% at dawn, hostile incursion 30% at dusk with a random event boss) runs beside `WorldEventSystem`. It is unsaved, has no bar, and its spawn multipliers stack (`GameSimulation.cs:239-264, 2405-2412`; `Spawning.cs:46-52`). Probe: 3 legacy starts in 12 days. | ⚠ | ❌ EV-2 not done, not scheduled |
| F3 | Event sky (Blood Moon red, eclipse, pillar skies, lantern and party skies, Moon Lord) | `SkyManager` activates "Lantern", "Party", the pillar skies and so on (`LanternNight.UpdateTime@45-96`, `BirthdayParty.UpdateTime@28-79`) | Only a biome sky tint (`Backdrop.cs:50`, `GraphicalSandbox.cs:1888`); no event looks | ❌ | ❌ EV-8 → 0.4.0; PE-10 → 0.4.0 |
| F4 | Event lighting (eclipse darkness, Blood Moon tint) | (knowledge) | `SkyLightLevel` ignores events | ❌ | ❌ EV-8 → 0.4.0; PE-10 → 0.4.0 |
| F5 | Event music | Per-event music IDs (Eclipse, Pirates, Goblins, Martians, pumpkin and frost moons, pillars, OOA, windy day, graveyard, storm) | One "event" sting and tension 0.7 (`AudioDirector.cs:101-130`) | ❌ | ◐ after 0.1.7: event music (AudioScene); EV-8 → 0.4.0 |
| F6 | Progress bar | Invasion bar with icon and % (`ReportInvasionProgress`), DD2 waves, moon wave and score | Up to 3 bars at the bottom right, with "wave n/N" and hours left (`GraphicalSandbox.cs:2926-2943`); moon score lines (`GraphicalSandbox.Leisure.cs:131`). Weather events (Rainfall …) also get a bar, which Terraria does not do. | ◐ | ◐ unchanged since the audit |
| F7 | Invasion exclusivity and overlap rules | One `invasionType`; pumpkin and frost moons cancel the Blood Moon (`UpdateTime@68-90`); events block lantern nights (`LanternsCanStart`) | One invasion at a time (`CanStart` :208); a Rust Moon can run alongside a wave moon, and the Aurora Night alongside anything | ✅ (minor overlap gap noted in EV-5) | ❌ EV-5 → 0.4.0 |
| F8 | World flags | `downedGoblins/Frost/Pirates/Martians` via `NPC.SetEventFlagCleared` (`UpdateInvasion@17-127`); DD2 tiers | `event.<kind>.cleared` on completion, plus first-set hooks that unlock NPCs (`WorldEvents.cs:322-326`, `World.cs:222-236`) | ✅ | ✅ as audited |
| F9 | Sleep refused during night, day and invasion events | (Terraria lets you sleep; time just won't skip in some cases) | `BlocksSleep` (`WorldEvents.cs:144-145`) | ✅ (deliberate) | ✅ as audited |

### E. Events, one by one

Columns: trigger / starting item / waves and scoring / unique creatures / end and rewards / flags / spawn, sky and music.

| # | Event (Rimtera name) | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| E-BM1 | **Blood Moon → Rust Moon**: natural trigger | 1/9 at nightfall (1/6 in the anniversary seed). Not on a new moon. Needs a player with `statLifeMax > 120` (`StartNight@899-991`). | 12% at dusk if max HP ≥ 120 or the Overseer is down (`WorldEvents.cs:412-418`, `World.cs:471-472`); no moon check; one natural start per boundary, ordered by kind | ◐ | ❌ EV-5 → 0.4.0 |
| E-BM2 | Rust Moon: starting item | Bloody Tear (knowledge) | Corroded flare, refused by day (`CreatureCoverage.cs:178`, `GameSimulation.CreatureCoverage.cs:208`) | ✅ | ✅ as audited |
| E-BM3 | Rust Moon: spawns | Rate ×0.3 (≈3.3× faster) and cap ×1.8 (`GetSpawnRate@479-508`) | ×2.2 / ×1.8, with the roster taking 70% of rolls | ✅ | ✅ as audited |
| E-BM4 | Rust Moon: unique creatures | Blood zombies, drippers, a Blood Moon fishing pool, Dreadnautilus (knowledge) | 5-unit roster + Corrosion Nautilus mini-boss (`CreatureCoverage.cs:145`) + fishing enemies 12% (`GameSimulation.Fishing.cs:226`) | ✅ | ✅ as audited |
| E-BM5 | Rust Moon: end and rewards | Ends at dawn (`StartDay@89-106`); no completion reward | 11 h duration (a natural one ends at 05:00, an item-started one can run into the day). Moon Eater final boss after 25 kills; credits + lure on the boss kill (➕). | ◐ | ✅ EV-4 0.1.11 |
| E-SE1 | **Solar Eclipse → Dark Sun**: trigger | Hardmode after any mech boss, 1/20 at day start (`StartDay@281-326`) | 6% at dawn after the Fracture (`WorldEvents.cs:419-423`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-SE2 | Dark Sun: item | Solar Tablet (knowledge) | Umbral lens, refused at night | ✅ | ✅ as audited |
| E-SE3 | Dark Sun: spawns and units | Rate ×0.2 / cap ×1.9 (`GetSpawnRate@588-617`); roster tiers by mech and Plantera; Mothron mini-boss | ×2.4 / ×1.8; 3-unit roster, + `LateDarkSun` after Mutated Flora (`GameSimulation.CreatureCoverage.cs:79-95`) | ◐ | ❌ EV-3 → 0.4.0 |
| E-SE4 | Dark Sun: end | Ends when the day ends (`UpdateTime@1891-1897`) | 9 h (a natural one ends at 15:00, an item-started one runs into the night). Eclipse Lord final boss after 30 kills (➕). | ◐ | ✅ EV-4 0.1.11 |
| E-GA1 | **Goblin Army → Scavenger Raid**: trigger | At day start: 1/3 once an orb is smashed and goblins are not yet beaten; otherwise 1/30 before hardmode, 1/60 in hardmode (`StartDay@446-549`) | 10% at dusk after a heart is broken, 3-day cooldown (`WorldEvents.cs:430-435`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-GA2 | Scavenger Raid: item | Goblin Battle Standard | Scrap beacon (`ContentItems.cs:536`) | ✅ | ✅ as audited |
| E-GA3 | Scavenger Raid: size and progress | Size 80 + 40 per player with ≥ 200 HP (`StartInvasion@48-105`); the bar counts kills | 3 waves of 8 / 10 / 12 kills, then the Warlord (`RecordKill` :282-302); **31 kills to clear (probe D)** | ◐ | ❌ EV-3 → 0.4.0 |
| E-GA4 | Scavenger Raid: approach and spawn area | The army marches from a world edge to spawn with warnings (`UpdateInvasion@196-396`); enemies only spawn within 3,000 px of the front, on the surface, near town NPCs (`ShouldSpawnInvasionEnemies@25-244`) | Each wave appears at once just off screen around the player, anywhere, even underground (`World.cs:161-180`) | ◐ | ❌ EV-3 → 0.4.0 |
| E-GA5 | Scavenger Raid: creatures, end, rewards, flags | Goblin units, Goblin Summoner mini-boss in hardmode; `downedGoblins` → Goblin Tinkerer | 5 units + Hexer; Warlord final boss (➕); credits + 2 crates (➕); `event.scavenger_raid.cleared` → `npc.demolitionist.unlocked` (probe D) | ✅ | ✅ as audited |
| E-PI1 | **Pirate Invasion → Pirate Boarding**: trigger | Hardmode with altars smashed, 1/30 at day start (1/60 once beaten) (`StartDay@566-626`); size +40 + 20/player (`StartInvasion@112-127`) | 5% at dusk after the Fracture (`WorldEvents.cs:436-441`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-PI2 | Pirate Boarding: item, units, end | Pirate Map; Flying Dutchman mini-boss; `downedPirates` → Pirate NPC | Pirate transmitter; 4 units; Dreadnought final boss; credits, crates, Bounty notice; `npc.pilot.unlocked` | ✅ | ✅ as audited |
| E-FL1 | **Frost Legion → Cryo Brigade** | Snow Globe only (no natural start); kill-based size; `downedFrost` (`UpdateInvasion@56-71`) | Cryo brigade orders, after the Fracture; 1 wave of 30 kills; 3 units; rewards (`Seasons.cs:33-37`) | ✅ | ✅ as audited |
| E-MM1 | **Martian Madness → Precursor Probes**: trigger | After Golem: a Martian Probe that escapes after scanning starts it; size 160 + 40/player, starts at spawn (`StartInvasion@134-215`) | 5% at dusk once `ore.voidsteel` is set (third altar, `Blight.cs:304`); no probe trigger; no starter item | ⚠ (far too early) | ❌ EV-6 → 0.4.0 |
| E-MM2 | Precursor Probes: waves, units, boss | Martian units, Martian Saucer mini-boss; `downedMartians` | 3 waves × 12, 4 units, Mothership Core final boss; credits + astral crate | ◐ | ❌ EV-6 → 0.4.0 |
| E-PM1 | **Pumpkin Moon / Frost Moon → Hollow Moon / Rime Moon**: start | Item at night, after Plantera; `startPumpkinMoon` / `startSnowMoon` reset the score and set wave 1 | Hollow lantern / Rime beacon at night, after the Fracture (`Seasons.cs:29-32`, `Leisure.cs:125-128`) | ◐ (gate) | ◐ unchanged since the audit |
| E-PM2 | Moons: end | At dawn (`StartDay@131` → `stopMoonEvent`) | A 9 h duration. Started at 23:00 it **ended at 08:00, and the expiry set `event.hollow_moon.cleared` with completions = 1 at wave 1 (probe B)** | ⚠ | ✅ EV-4 0.1.11 |
| E-PM3 | Moons: waves and scoring | 15 waves (pumpkin) or 20 (frost); points per kill to a per-wave threshold (`NPC.waveKills`, `totalInvasionPoints`); score display | 10 waves; threshold 12 + 8 × wave; points from HP (1-6), mini-bosses 18, boss 60 (`Seasons.cs:45-102`, `RecordSeasonKill` :148-178) | ◐ | ❌ EV-13 → 0.4.0 |
| E-PM4 | Moons: mini-bosses, bosses, rewards | Waves add mini-bosses and bosses progressively; loot rolls per kill and improves with the wave | Mini-boss from wave 4, a second from wave 6, the boss from wave 8; tier loot by the wave reached at the end + 2 items per mini-boss kill | ◐ | ❌ EV-13 → 0.4.0 |
| E-OOA | **Old One's Army → Defense Protocol** | Eternia Crystal on its stand; 3 difficulty tiers; crystal HP and failure; timers between waves; building blocked in the arena; Defender medals (`DD2Event.*`) | Signal core used next to the Station AI; 3 waves × 10 + Protocol Dragon; credits + boards; no crystal, failure, tiers, medals or timers (`WorldEvents.cs:442-447`, `Items.cs:106-112`) | ❌ | ❌ EV-7 → 0.4.0 |
| E-L1 | **Lunar events → obelisk sieges**: start | Lunatic Cultist killed → 4 pillars; cultist ritual at the dungeon (`CultistRitual.*`) | Void Cantor ritual → 4 obelisks spread across the map (`Hardmode.cs:440-476`) | ✅ | ✅ as audited |
| E-L2 | Obelisk shields | Shield of 100 (expert 150) kills of the pillar's own enemies *near the pillar*; shields never time out | Shield of 20 kills (`WorldEvents.cs:120`); rosters spawn anywhere (`PickSpawn`); **the specs expire after 48 h → the shield drops without kills** (`WorldEvents.cs:263-266`, `Hardmode.cs:478-491`) | ⚠ | ❌ EV-1 not done, not scheduled |
| E-L3 | Moon Lord countdown | `MoonLordCountdown` 3,600 ticks (60 s) with screen shake (`UpdateTime@1044-1363`) | `AstralCountdownSeconds` = 60 with a warning at 10 s (`World.cs:481-494`, `Hardmode.cs:509-519`) | ✅ | ✅ as audited |
| E-SR | **Slime Rain → Swarm Rain** | Day, before 07:30 (`time < 27000`), not raining, no invasion; slimes fall from the sky (`Spawner.SlimeRainSpawns`). 150 kills (75 after King Slime) → King Slime. Lasts 32,400-54,000 ticks (`UpdateTime@717-887`, `StartSlimeRain@143-178`). | 8% at dawn after the Overseer, **only while it rains**; ground spawns; 30 kills → Alpha Skitter; 8 h (`WorldEvents.cs:424-429`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-LN | **Lantern Night → Aurora Night** | Next night after the first clear of a boss or event (`NPC.OnGameEventClearedForTheFirstTime`), or 1/14 after the Moon Lord; 5-10 night cooldown; blocked by moons, invasions and bosses (`LanternNight.NaturalAttempt`, `LanternsCanStart`); stops rain and clouds (`UpdateTime@11-34, 293-296`) | 10% at dusk when dry and clear; no hostile spawns; joy for pawns; 4-day cooldown (`WorldEvents.cs:454-456`, `World.cs:528-530`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-PA | **Party → Colony Festival** | Party Girl present, 1/10 a day (1/7 in the anniversary seed), 5-10 day cooldown (`BirthdayParty.NaturalAttempt@9-160`); NPCs celebrate; spawns *not* suppressed | 8% at dawn with ≥ 3 housed NPCs (+1/6 in the Harvest season); Festival flare; no hostile spawns; rewards (`WorldEvents.cs:457-460`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-MF | **Meteor fall** | 1/50 each night after the second boss (`StartNight@96-121`) plus orb-smash meteors (knowledge); `WorldGen.dropMeteor` | 50% after a heart break (next dusk), 1/8 at Starfall dusks; crater + a 2 h roster (`World.cs:304-311, 358-385, 461-468`) | ◐ | ❌ EV-5 → 0.4.0 |
| E-TG | **Torch God → Lumen Tempest** | Many torches nearby underground → a swarm → Torch God's Favor (knowledge) | ≥ 40 lights near a deep player → a 12-kill swarm → Lumen accord (biome light tint) (`GameSimulation.CreatureCoverage.cs:166-187`) | ✅ | ✅ as audited |
| E-GY | **Graveyard → grave field** | Tombstone-tile threshold 28 (min 16, max 36) (`SceneMetrics..cctor`); fog, a darker sky, ghost spawns, lightning 1/120 at full intensity (`UpdateWeather@761-783`), music, `Recipe.needGraveyardBiome` | ≥ 6 markers in a 61×31 box → a 4-creature roster takes some rolls (`GameSimulation.CreatureCoverage.cs:31, 43-51`); no fog, visuals, music, lightning or recipes | ◐ | ❌ EV-14 → 0.4.0 |


---

## Integration steps

Sizes: S = under half a day, M = 1-2 days, L = 3 days or more. Every acceptance item names a self-test unless it says "play-test".

**EV-1 · Obelisk shields never time out (must-fix).**
- Change: give the four obelisk specs `IsInvasion`-style "no expiry". The cleanest way is a new `WorldEventSpec.NoExpiry` flag that `Advance` honours. Also make `UpdateObeliskShields` keep the shield when the event state is missing, unless the event is cleared or forced.
- Files: `Systems/WorldEvents.cs`, `Systems/GameSimulation.Hardmode.cs`.
- Size: S. Dependencies: none (bosses area 02 owns the obelisk actors).
- Acceptance: a self-test starts the siege, advances 200 game hours with no kills and asserts all four obelisks are still `Shielded`. Twenty roster kills near one obelisk then drop only that shield.

**EV-2 · Retire the legacy `EventManager` (must-fix).**
- Change: delete the three registrations and the `_eventManager` advance and multiplier path. If they are wanted, re-express Moth-ray swarm and Deep-core tremors as `WorldEventSpec`s (saved, with a bar). Fold the "hostile incursion" into the G2c rule as a natural *boss night* in the bosses area, or drop it. Keep `EventManager.cs` only if `CoreSelfTests.cs:123` still needs it; otherwise remove that test with it.
- Files: `Systems/GameSimulation.cs:75, 239-264, 286, 2395-2412`; `Systems/GameSimulation.Spawning.cs:46-52`; `Systems/EventManager.cs`; `Systems/CoreSelfTests.cs`.
- Size: S-M. Dependencies: area 02 (event-boss pool, `TrySpawnEventBoss`).
- Acceptance: a 40-day probe shows no "Event started" message from a non-`WorldEventKind` source. The spawn multiplier equals the `WorldEventSystem` product alone. Save/load keeps every visible event.

**EV-3 · Make event spawns depend on location.**
- Change: give `WorldEventSpec` a `SpawnScope`: `Surface` (weather, Rust Moon, Dark Sun, raids, Swarm Rain, Windy Day), `Anywhere` (Meteor at its crater, Lumen Tempest underground), or `NearAnchor(x, radius)` (obelisks, the raid front). `PickSpawn` takes the spawn cell and only lets matching rosters take the roll. Weather rosters also need the matching biome: Blizzard → Frozen, Dust → Junk, Acid → coast, blight or Fracture.
- Files: `Systems/WorldEvents.cs` (`PickSpawn`, spec), `Systems/GameSimulation.Spawning.cs:100-106`, `Systems/GameSimulation.Hardmode.cs` (anchor = obelisk position).
- Size: M. Dependencies: area 03 (spawn tables), area 05 (dungeon and biome tests).
- Acceptance: a self-test with rain forced and the player in the dungeon, and again 40 tiles deep, spawns 200 creatures and finds none from a weather roster. An obelisk roster creature never spawns more than R tiles from its obelisk.

**EV-4 · End events at the right boundary, and stop counting expiry as a clear.**
- Change:
  - Add `WorldEventSpec.EndsAt` (`Dawn` for night events and moons, `Dusk` for Dark Sun, `null` for the rest).
  - In `Advance`, end the event when that boundary passes, whatever hours are left.
  - Moons end at dawn as "set", not "cleared". Only `RecordSeasonKill` reaching the final wave sets the cleared flag and the completion.
  - Give `completed` for timed events an explicit spec flag (`ClearsOnTimeout`: true for weather, Aurora, Festival and Windy, false for moons).
- Files: `Systems/WorldEvents.cs` (:255-268), `Systems/GameSimulation.Seasons.cs` (`EndMoon`, the specs).
- Size: S. Dependencies: none.
- Acceptance: a Hollow Moon started at 23:00 ends at 06:00 with no cleared flag and completions 0. A Rust Moon started at 02:00 ends at 06:00. A Dark Sun started at 15:00 ends at 18:00. The `WorldLeisureSelfTests` moon tests still pass.

**EV-5 · Align natural triggers with Terraria's shape (Rimtera numbers stay original).**
- Change:
  - **Rust Moon:** not on the dark moon (phase 4).
  - **Swarm Rain:** dry mornings only (rain < 0.2, before 09:00); creatures fall from the sky (spawn high and drop).
  - **Scavenger and pirate rolls:** move to dawn. The army marches from a world edge toward the colony (an `InvasionFront` x that moves each hour, announced at a quarter and at half distance).
  - **Aurora Night:** queued for the night after any first boss or event clear (a hook on `boss.*.down` / `event.*.cleared`), plus a rare chance after the Astro-God, with a 5-10 night cooldown. It cannot start with a Rust Moon, a moon, an invasion or a living boss.
  - **Meteor nights:** a small nightly chance once the second-tier boss is down.
  - **Festival:** needs a festival-host NPC (or keep 3+ NPCs as a deliberate change; open question 4). Drop `NoHostileSpawns`, or keep it as deliberate.
  - **Wave moons, Rust Moon and Dark Sun:** a wave moon cancels a running Rust Moon.
- Files: `Systems/WorldEvents.cs`, `Systems/GameSimulation.World.cs` (`AdvanceContentClock`, `WireTriggers`), `Systems/GameSimulation.Seasons.cs`, `Systems/GameSimulation.Spawning.cs`.
- Size: M. Dependencies: area 01 (which boss is "second tier"), area 08 (host NPC).
- Acceptance: a 1,000-night roll table shows no Rust Moon on phase 4 and no Swarm Rain while raining. The night after the first Overseer kill is an Aurora Night. A raid's front column moves toward the spawn column over ≥ 2 game hours before creatures spawn.

**EV-6 · Martian-analog gate and probe trigger.**
- Change: gate Precursor Probes on the Golem-analog boss flag (confirm with area 01). Remove the plain dusk roll. Add a "Precursor scout" creature that spawns in the outer thirds of the surface or sky after the gate; if it leaves the screen alive after seeing the player, the invasion starts. Add an optional starter item.
- Files: `Systems/WorldEvents.cs:448-453`, a new creature in `Systems/Content/CreatureCoverageData.cs`, `Systems/GameSimulation.Spawning.cs`.
- Size: M. Dependencies: areas 01, 02 and 03.
- Acceptance: with only `ore.voidsteel` set, 200 dusks start no Precursor Probes. With the gate set, a scout that escapes starts the event, and one killed in time does not.

**EV-7 · Defense Protocol becomes a defend-the-core event.**
- Change:
  - Using the signal core places a "Signal pylon" actor (HP, cannot be healed) at the Station AI. Creatures path to it and hit it.
  - Losing it fails the event with a message and keeps the core.
  - Add a pause between waves, with a skip action at the Station AI.
  - Three tiers keyed to progression (pre-Fracture, after a mech-analog, after the Golem-analog), each with its own roster, wave count and final boss.
  - Medal-like tokens drop per wave and feed a small shop.
  - Optional: block building near the pylon.
- Files: `Systems/WorldEvents.cs` (spec per tier), a new `Systems/GameSimulation.DefenseProtocol.cs`, `Systems/GameSimulation.Items.cs:106-120`, the Station AI shop in `Systems/TownNpcs.cs`.
- Size: L. Dependencies: area 08 (Station AI shop), area 03 (pathing to a target), area 02 (tier bosses).
- Acceptance: a self-test destroys the pylon and checks the event ends `completed: false` with no cleared flag. Clearing every wave sets `event.defense_protocol.t1.cleared`. Tier 2 refuses before its gate.

**EV-8 · Event sky, lighting and music.**
- Change: add `GameSimulation.EventAtmosphere` (sky tint, sun and moon colour, light multiplier, music theme), derived from the active events: Rust Moon red, Dark Sun dark sun with 0.35× daylight, Aurora green ribbons, Festival balloons, obelisk tint near each obelisk, grave field fog and dim, storm theme. The renderer and `AudioDirector` read it. Fold it into `SkyLightLevel`.
- Files: `Systems/GameSimulation.World.cs`, `UI/GraphicalSandbox.Backdrop.cs`, `UI/GraphicalSandbox.Sky.cs`, `UI/AudioDirector.cs`, music assets.
- Size: M (code) plus art and music (owned by area 09).
- Dependencies: area 09 (presentation), and the ComfyUI art queue (paused; waits for the user's go per `QUEUED_WORK.md`).
- Acceptance: a self-test asserts `SkyLightLevel` at noon during a Dark Sun is ≤ 0.5 × clear noon and that `EventAtmosphere.MusicTheme` is non-null for each night, day and invasion event. Play-test: the Rust Moon sky is visibly red.

**EV-9 · Weather from the world's climate, not the player's footprint.**
- Change: roll one global weather from the world (wind, cloud, rain). Each biome *renders and applies* it locally: rain → snow in Frozen, → acid on the coast, blight or Fracture; a dust storm only in Junk and only when |wind| ≥ a threshold (Terraria's `HasSufficientWind` shape); a blizzard = snow + strong wind. Couple the wind target to rain (stronger in storms) and use longer wind spells (2-8 h) rather than 4 fixed rolls. Storms set by cloud and wind thresholds, not a coin flip.
- Files: `Systems/Weather.cs`, `Systems/GameSimulation.World.cs:441-453, 509-530`, `UI/GraphicalSandbox.cs:2163-2200`, `Systems/WorldEvents.cs` (`SyncWeather` per local zone).
- Size: M. Dependencies: area 05 (biome lookup at a column), area 07 (save format: `WeatherState` gains fields, append-only).
- Acceptance: a self-test walks the player from Frozen to Grassland during snow; snow stops drawing and settling in the grassland while the rain value stays. A dust storm never starts with |wind| below the threshold. The share of storm hours is within ±2% between two runs at different tick rates.

**EV-10 · Lightning independent of the frame rate, and more Terraria-like.**
- Change: roll strikes per *game second* (a Poisson rate from rain × wind) inside `UpdateWorldRealtime`, not per environment tick. Keep damage strikes as ➕, but cap them at one per 3 s near the player. Add sky-only flashes with a thunder delay (Terraria's `NewLightning` shape). Add the graveyard rule: rare flashes in a grave field.
- Files: `Systems/GameSimulation.World.cs`, `UI/GraphicalSandbox.cs:2201-2218`, `UI/AudioDirector.cs:92-94`.
- Size: S. Dependencies: EV-9 (storm definition).
- Acceptance: a forced storm gives the same strike count (±20%) over 60 s at 60 Hz and at 4 Hz.

**EV-11 · Wind pushes the player on windy surfaces; wind applies once to projectiles.**
- Change: when |wind| ≥ the windy threshold, the player is outdoors on the surface and airborne (or walking on a light surface), add a horizontal push and a status icon, with a set of immune items. Remove the spawn-time `WindDrift` (`Combat.cs:322`) so `ApplyWind` alone pushes shots, and add per-weapon wind immunity for beams and lasers.
- Files: `Systems/GameSimulation.Leisure.cs`, `Systems/GameSimulation.Combat.cs`, `Systems/GameSimulation.Player.cs` (movement).
- Size: S-M. Dependencies: area 07 (movement), area 06 (weapon flags).
- Acceptance: a self-test checks that a jumping player drifts ≥ 1 tile downwind over 2 s on a windy day and 0 underground. A content bullet's x-velocity gain from wind equals `ApplyWind`'s alone.

**EV-12 · Windy Day as a weather state, plus its music.**
- Change: make "windy" a derived state (|wind| ≥ threshold, clear sky, morning to midday). The event spec becomes a weather-synced event like Rain (no fixed 10 h, no forced 7.5). Its roster spawns only in windy surface areas. Add a windy theme.
- Files: `Systems/GameSimulation.Seasons.cs`, `Systems/GameSimulation.Leisure.cs:267-268`, `Systems/WorldEvents.cs` (`SyncWeather`), `UI/AudioDirector.cs`.
- Size: S. Dependencies: EV-9, EV-8.
- Acceptance: with wind forced to 0 during a "Windy day", the event ends; a kite drops.

**EV-13 · Moon scoring shape.**
- Change: decide whether to stay at 10 waves (open question 5). Either way, give the per-kill drop chance a wave multiplier (Terraria's loot rises with the wave). Show a "wave n — score x / y" bar in the main event bar instead of a separate leisure line. Gate the moon items on the Mutated-Flora (Plantera-analog) flag rather than the Fracture, if area 01 agrees.
- Files: `Systems/GameSimulation.Seasons.cs`, `UI/GraphicalSandbox.cs:2926`, `UI/GraphicalSandbox.Leisure.cs:131`.
- Size: S-M. Dependencies: area 01 (gate), area 06 (drop tables).
- Acceptance: a self-test checks that the share of kills dropping moon loot at wave 8 is ≥ 2× the share at wave 1, and that the event bar shows the score.

**EV-14 · Grave field becomes a real mini-biome.**
- Change: count markers as Terraria's tile threshold does (min, threshold and max give an intensity 0-1). Intensity drives fog, a darker sky and stars, a grave music theme, rare flashes (EV-10) and daytime grave spawns. Add a `NeedsGraveField` recipe condition for one or two original recipes.
- Files: `Systems/GameSimulation.CreatureCoverage.cs`, `Systems/CraftingSystem.cs` (condition), the UI backdrop and audio.
- Size: M. Dependencies: area 01 (recipes), area 09 (visuals).
- Acceptance: a self-test checks that intensity is 0 at 3 markers, between 0 and 1 at 5 and 1 at 9, and that the grave roster spawns at noon with intensity ≥ 0.5.

**EV-15 · Sundial and moondial analogs.**
- Change: add a placeable "Chrono dial" that skips to dawn (or a "Lunar dial" that skips to dusk) with a 7-day cooldown, reset by a Rust Moon or Dark Sun. It is refused while an event blocks sleep.
- Files: `Systems/Content/*Items.cs`, `Systems/GameSimulation.World.cs`.
- Size: S. Dependencies: area 01 (recipe), area 08 (NPC shop).
- Acceptance: using the dial at 10:00 fast-forwards to 06:00 the next day, and a second use is refused until the cooldown ends.

**EV-16 · Clock, moon and weather readouts.**
- Change: show the time, the moon phase name (`MoonPhaseName`), the rain or wind readout and the active-weather icon. They can come from info gear (watch, sextant, weather radio analogs) or always, in the HUD.
- Files: `Systems/GameSimulation.InfoGear.cs`, `UI/GraphicalSandbox.cs` HUD.
- Size: S. Dependencies: area 07 (HUD and info accessories), area 06 (items).
- Acceptance: a self-test checks that `InfoLines` contains the moon phase name when the sextant analog is equipped.

**EV-17 · Make the day length a setting.** Depends on open question 1.
- Change: if the user wants Terraria pacing, change `GameHoursPerRealSecond` to about 0.0139 (a 24-minute cycle with a 1.5 night pace gives about 0.0139 day / 0.0208 night). Rescale everything that is per game hour and should stay real-time: needs, statuses, regen, crop growth, town arrivals, event durations, potion timers via `GameHoursPerRealSecond`, `TownRespawnHours` and the fishing bite wait. A world setting (Short / Standard) keeps both.
- Files: `Systems/GameSimulation.cs:42`, `Systems/WorldSettings.cs`, and every `* GameHoursPerRealSecond` user (`Player.cs:216, 675, 765`, `Phase.cs:55`).
- Size: M-L, because the balance pass touches the colony sim.
- Dependencies: areas 01, 07 and 08 (needs, regen, town).
- Acceptance: with "Standard", one full cycle is 1,440 ± 10 real seconds. A natural Rust Moon lasts about 9 real minutes. Colonist hunger per *real* minute stays within ±10% of today's.

---

## Cross-area dependencies

| Area | What this area needs from it, or gives it |
|---|---|
| 01 Progression and crafting | The gates for Dark Sun (mech-analog), Precursor Probes (Golem-analog), the wave moons (Plantera-analog), the second-tier boss for meteor nights; recipes for the dial, grave-field recipes and the Defense Protocol tiers |
| 02 Bosses | Event final bosses and mini-bosses; obelisk actors and shields; removal of the legacy "hostile incursion" boss drop (G2c); night bosses leaving at dawn (their #4) should share EV-4's boundary logic |
| 03 Enemies and critters | Spawn scopes (EV-3); slime-rain sky drop; the Precursor scout; pathing to the Defense Protocol pylon |
| 05 World and tiles | Biome lookup by column for global weather (EV-9); meteor crater and grave-field markers; rain basin filling |
| 06 Items and combat | Wind immunity per weapon (EV-11); moon drop-chance scaling; starter items (Martian analog) |
| 07 Player, UI and saves | Player wind push and movement; HUD readouts (EV-16); `WeatherState`/`WorldEventSpec` save fields (append-only) |
| 08 NPCs, town and colony | NPC unlocks from event clears (Goblin Tinkerer analog → which NPC?); festival host NPC; Station AI shop and medals; a day-length change affects the colony sim |
| 09 Presentation | Event skies, light, music themes, grave-field fog, sky entities (EV-8, EV-14); art is on the paused ComfyUI queue |
| Fishing (in area 06 or 08) | Moon-phase fishing modifier; Rust Moon fishing enemies already exist |

---

## Open questions for the user

1. **Day length.** Should a day-night cycle stay at about 4 minutes (RimWorld-style colony pacing) or move toward Terraria's 24 minutes? This sets how often every event happens and how long it lasts (EV-17). A world setting would allow both.
2. **Event end bosses.** The Rust Moon, Dark Sun, Scavenger Raid, Pirate and Precursor events all end with a summoned final boss, which Terraria does not do for Blood Moon, Eclipse or Goblins. Keep these as a deliberate Rimtera feature?
3. **Damaging weather.** Lightning that hits for 25 and acid rain that burns outdoors are Rimtera-only. Keep them, soften them, or make them opt-in by difficulty?
4. **Festival and Aurora Night suppress all hostile spawns.** Terraria's party and lantern night do not. Keep the "safe night" design?
5. **Wave moons.** Stay at 10 waves (shorter, since nights are short) or go to Terraria-like 15/20 once the day length is settled?
6. **Legacy events.** Should "Moth-ray swarm" and "Deep-core tremors" survive as proper `WorldEventSpec`s, or be removed with the legacy `EventManager`?
