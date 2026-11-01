# Rimtera design: the colony escape

Written 2026-10-31. This is the game's design direction: what a run is, which rules make it, and which systems it needs. It sits above `docs/ROADMAP_0.2.0.md`. That roadmap's parity work is the baseline; this document says what is built on top of it.

## 1. The game in one paragraph

You crash-land alone with the station AI on a world shaped and played like Terraria. You build a shelter, dig down to learn where you are, and climb a Terraria-style progression ladder toward one goal: power a ship and get off the planet with as many survivors as you can. Along the way a RimWorld-style storyteller sends arrivals, raids, sickness, breakdowns and disasters, scaled to how far you have come. The people who join you work your stations, farms and lines of supply, and they die for good. You can lose the whole colony. Losing is meant to be a story worth telling, and the next run starts from nothing.

### 1.1 Settled direction (from the design conversation)

| # | Decision |
|---|---|
| 1 | Terraria is the baseline for feel and base mechanics. Every system is then twisted to Rimtera's needs. The end result is not a near-clone of either game. |
| 2 | The RimWorld storyteller and colony management are what change the game. They start as the colony layer and grow from there. |
| 3 | Dead colonists and town NPCs stay dead. Replacements come only through storyteller events. |
| 4 | The player respawns and goes back for what they dropped. Death drops scale with difficulty: Easy drops money, Medium drops money and items, Hard drops the whole bag except tools. |
| 5 | Colonists have skills that make the automations better: running stations, farming herbs for potions, fishing, cooking. They speed things up and improve them; they do not take the player's hands off the game. |
| 6 | Pylons move you around the world as you progress, and they are tied to the colony (staffed outposts). |
| 7 | The guide (the station AI) is important and must be protected. Rebuilding it after it falls is parked for later. |
| 8 | Nothing carries over between runs. |
| 9 | Winning means getting off the planet and beating the end boss. How many survivors come with you is the story of the run. |
| 10 | Ironman is the "impossible run": a hardcore mode on top of Hard, where the character dies for good. |
| 11 | Each town NPC's type decides the shop it runs, as in Terraria. Town NPCs also work stations and jobs to help: they make things faster and better, never automatic. |
| 12 | A pylon needs a staffed outpost, as Terraria's pylons need residents nearby. Items and potions take you home, as Terraria's recall items do. |
| 13 | The storyteller is picked at world creation and fixed for the run. |
| 14 | A run takes 10–14 hours from landfall to launch on a medium world (8–15 is the outer range). |

### 1.2 Target aesthetics (MDA)

| Rank | Aesthetic | Fed by |
|---|---|---|
| Primary | **Narrative** (emergent: what happened to this colony) | Storyteller, permadeath crew, mood and breaks, relationships, the arc, the escape roster |
| Primary | **Challenge** | Terraria combat and bosses, death drops, colony defence, the storyteller's pressure on a growing colony |
| Secondary | **Discovery** | Biomes, the diesel seas, the core, the Fracture, what the storyteller sends |
| Secondary | **Expression** | Building, base and outpost layout, who works what, loadouts |
| Supporting | **Sensation** | Game feel, music, sound |

Rule for every system: if it feeds neither narrative nor challenge, it needs a reason to exist.

## 2. A run, act by act

Acts are the storyteller's chapters. Each one unlocks new incidents and raises the stakes. Most of the world content already exists; the acts tie it to the colony.

| Act | What happens | Ends when | Already in the game | New |
|---|---|---|---|---|
| **1. Landfall** | Just you and the station AI hologram. Build shelter, find food, start digging. The AI moves into your first house. | First house + first crew member | Solo start, AI hologram, housing, crew arriving into free houses, storyteller quiet for three days | Act tracking; storyteller intro pacing |
| **2. Descent** | Dig down through the biomes to learn where you are. The world's two edges are oceans of diesel. Outposts and pylons open up. | You reach the irradiated core | Biomes, fuel seas at the edges, the irradiated core, pylons (two housed residents) | Outposts as staffed bases; storyteller raids reaching outposts |
| **3. The core** | Reaching the core wakes its gate boss. | Gate boss defeated | `CoreGateBoss` (Fission Tyrant) at the core | Arrival triggered by reaching the core, not just by summoning |
| **4. The Fracture** | The gate boss's death exposes the surface and starts hardmode. Climb the ladder again: steeper, fewer and harder steps, with the colony carrying more of the grind. | Ladder climbed to the zealots | `TriggerFracture`, hardmode ores and bosses, hard-tier gates | Steeper second ladder; bigger raids aimed at the colony |
| **5. The zealots** | The Astro-God's cultists arrive. They raid, preach, and can turn your own people. | The four fuel cores are taken | Cultist ritual, the Void Cantor, the four obelisk sieges | Zealot incidents: conversion, sabotage, desertion. Obelisks become the **four fuel cores** |
| **6. Collapse** | Collapse the four fuel cores back into the core. That calls the Astro-God. | Astro-God defeated | Astro-God, gated on `obelisks.down` | Collapse ritual at the core (replaces the summon) |
| **7. Launch** | The Astro-God's core powers the ship. While it fuels up, the storyteller throws everything at the colony. Board whoever is still alive. | The ship lifts off | — | Ship, fuel-up siege, boarding, the escape screen |

**A run is lost** when you can no longer launch: the colony has fallen and nobody is left to rebuild it (or, until the guide-rebuild rules exist, when the station AI is destroyed). See §3.4.

**Run length target:** 10–14 hours from landfall to launch on a medium world (8–15 is the outer range). The double ladder is the main risk to that. The hardmode ladder must be shorter than the first, not a repeat of it.

## 3. Core rules

### 3.1 Permadeath for people
- Colonists and town NPCs who die are gone. Their housing frees up; their skills and relationships are lost.
- Replacements come only from storyteller arrival incidents (§5.4). The storyteller's population intent (§5.3) makes arrivals more likely when the colony is small, and less likely when it is large.
- Bodies and graves stay in the world. Mood thoughts follow ("my friend died", "a colonist was left unburied").

### 3.2 Player death and drops
The player respawns at their bed or the landing site and goes back for what they dropped. This maps onto the existing character difficulties:

| Setting | Drops | Existing | Change |
|---|---|---|---|
| **Easy** | Money | Classic drops 40% of credits | Rename to Easy; drop all carried money |
| **Medium** | Money and items | Salvager drops every carried item | Rename to Medium; add money |
| **Hard** | Everything except tools | — | New: drop the whole bag except tools, keep respawning |
| **Ironman** (the impossible run) | Everything except tools, and the character dies for good | Ironman (permadeath) | Hardcore mode on top of Hard: one life for the whole run |
| Explorer, Creative | Nothing | Exist | Unchanged (no-stakes modes) |

Dying always costs time: while you are dead and walking back, the colony is on its own.

### 3.3 Skills make the colony work
Colonists already have twelve skills with passions. Each work type uses one skill. Skill sets speed and quality; passion sets mood from doing it.

| Work | Skill | Effect |
|---|---|---|
| Operate a station | Crafting | Works the station's bill queue: crafts faster, with a chance of an extra item at high skill |
| Farm | Plants | Plants and harvests herbs and crops; herbs feed potions |
| Brew | Medical | Turns herbs into potions from a bill queue |
| Cook | Cooking | Meals and food buffs; keeps the crew fed |
| Fish | Animals | Fish, crates, the daily quest fish |
| Mine and haul | Mining | Mines marked areas; hauls ore and drops to storage |
| Build | Construction | Builds and repairs from player-placed blueprints |
| Guard | Shooting or Melee | Holds a post; fights raids at home or at an outpost |
| Tend | Medical | Treats wounds and sickness; later, rebuilds the station AI |
| Research | Intellectual | Unlocks recipes and the ship's parts |
| Trade | Social | Better prices with traders and town NPCs |

Town NPCs work too: alongside running their shop, they operate stations and take jobs like the crew.

**Rule:** crew and town NPCs make things faster and better, never fully hands-off. The player still fights the bosses, explores, and decides what gets made. A strong colony shortens the grind; a weak one makes the player do it.

### 3.4 The station AI
- It is the colony's heart. It holds the ship's plans and the recipe guide.
- It must be protected: it lives in a house, takes damage, and raids can target it.
- Until the rebuild rules exist, its destruction ends the run.
- **Parked for later:** losing it blocks ship parts and the guide until a Medical colonist rebuilds it from salvage. That turns its loss into a crisis to recover from, not an instant end.

### 3.5 Between runs
Nothing carries over. The world, the crew and the character are all for one run. (The character profile and settings stay; the run's progress does not.)

## 4. Systems pass

What each system needs, what exists, and what is missing. Sizes are rough: S (days), M (a week or two), L (several weeks), XL (a release step).

| # | System | Exists now | Missing | Size |
|---|---|---|---|---|
| 1 | **Storyteller** | Interval-gated incidents, category then weighted roll, refire timers, threat points from wealth, crew, progression and day (`Systems/Storyteller.cs`); six incidents; 72-hour grace; raids gated on the first boss | A component interface; acts as the progress score; adaptation and population watchers; on/off threat cycles and the big-threat guarantee; follow-up triggers; the queue with warnings; shared cooldown tags; difficulty dials; three personalities; about twenty-five new incidents (§5) | XL |
| 2 | **Colony work** | Crew survival behaviour (eat, rest, mine, idle: `AI/SurvivalBehavior.cs`), skills and passions, work-speed stats | Work types and priorities, job givers per work type (the think-tree pattern in `docs/RESEARCH.md`), a work tab | L |
| 3 | **Station bills** | Done in 0.2.3: a bill queue per station (make N, keep N, forever) in the hub's Bills tab, worked by Operate colonists who fetch from and store to chests near the station | Town NPCs working stations; bills that take ingredients from the player | M |
| 4 | **Farming and brewing** | Done in 0.2.5: grow zones planted and harvested by Farm crew; brew bills at chem labs worked by Brew crew | Crops beyond herbs; planters as zone soil only | M |
| 5 | **Cooking and food** | Done in 0.2.6: cook bills worked by Cook crew; crew eat from storage, most filling first | Meal quality from Cooking skill | M |
| 6 | **Fishing by crew** | Done in 0.2.6: Fish crew fish open water near home and store the catch | Crew using rods and bait | S |
| 7 | **Hauling and storage** | Done in 0.2.4: tagged chests (eight item categories); crew and residents haul drops and products to the chest that takes them | Storage zones on the floor; priorities between tagged chests | M |
| 8 | **Outposts and pylons** | Pylons need two housed residents nearby; recall items | Every pylon needs a staffed outpost (decided); outposts as named bases with their own crew and storage; raids can hit an outpost while you are away; recall items and potions to get home | M |
| 9 | **Permadeath** | Town NPCs respawn on a timer; crew can die | Turn off the respawn timer; graves; death mood thoughts; replacements only from arrivals | S |
| 10 | **Death drops** | Explorer, Classic, Salvager, Ironman, Creative | Easy / Medium / Hard as in §3.2 | S |
| 11 | **Colony defence** | Raids spawn waves; crew can fight | Raids aim at the colony (and outposts), not just the player; guard posts; doors and walls matter; town NPCs fight back | L |
| 12 | **Mood, health, breaks** | Mood, thoughts, health with body parts, a mental break state | Three break levels with thresholds, time below threshold and a cooldown; break behaviours that cause trouble (wandering off, refusing work, smashing things); the "final straw" in the break message; inspirations | M |
| 13 | **Station AI protection** | AI moves into the first house; hologram until then | Health and damage, raids that target it, run loss on its destruction; rebuild parked | S |
| 14 | **The arc** | Core gate boss, the Fracture, cultist ritual, obelisks, Astro-God | Act tracking; core boss on arrival; obelisks re-themed as fuel cores; collapse ritual | M |
| 15 | **Zealots** | Cultist ritual as a boss gate | Zealot incidents: preaching, conversion of a colonist, sabotage, desertion. **On hold:** details to be discussed when act 5 is built | M |
| 16 | **Ship and launch** | — | Ship blueprint and parts (research and boss drops), a buildable ship, the fuel-up siege, boarding, liftoff | L |
| 17 | **Run end** | Death and respawn screens | Win and loss screens with the story log: who joined, who died and how, who escaped | S |
| 18 | **Run pacing** | World sizes, Terraria-paced ladder | A shorter, steeper hardmode ladder; tuning to the 8–15 hour target | M |

### 4.1 Order to build in
1. **Stakes first:** permadeath (9), death drops (10), station AI protection (13), run end screens (17). Small, and they change how every other system feels.
2. **The colony works:** work types (2), station bills (3), hauling (7), then farming and brewing (4), cooking (5) and fishing (6).
3. **The storyteller grows up:** acts, adaptation, population intent, personalities and the incident queue (1), with the arrival incidents first so permadeath has its replacements.
4. **Pressure on the colony:** colony defence (11), outposts (8), breaks (12).
5. **The ending:** the arc changes (14), zealots (15), ship and launch (16), pacing (18).

### 4.2 Effect on the 0.2.0 roadmap
- Most of the roadmap's parity work (movement, combat, creatures, world, events) stays. It is the baseline this design needs.
- Roadmap step 0.1.13 (town and colony) moves earlier and grows: it becomes steps 1 and 2 above.
- The roadmap's colony goal, "they never cost the player anything", is replaced. Under this design the colony is the main thing the player can lose.
- Events and weather (0.1.12) move from the clock into the storyteller (§5.4): blood-moon, eclipse and invasion analogues become incidents the storyteller schedules.

## 5. The storyteller, in depth

### 5.1 What RimWorld's storyteller is made of
From the REA metadata already gathered (`docs/RESEARCH.md`, Phase 4: member and type names only, nothing copied):

- **An interval check, not a per-frame roll.** `Storyteller` ticks on a fixed number of checks per day (`CheckInterval`, `IntervalsPerDay`) and fires what its components propose (`MakeIncidentsForInterval`, `TryFire`). Rimtera already does this.
- **Components.** A storyteller is a list of `StorytellerComp`s, each proposing incidents its own way (`MakeIntervalIncidents`). `StorytellerComp_RandomMain` rolls a category first, then a weighted incident in it (`ChooseRandomCategory`, `TrySelectRandomIncident`, `IncidentChanceFinal`). Rimtera has one built-in component of this kind.
- **Incident workers.** Each incident decides if it can fire now (`CanFireNow`), whether it fired too recently (`FiredTooRecently`), how much more or less likely it is right now (`ChanceFactorNow`), and what it does (`TryExecute`). Rimtera's `IStoryIncident` matches this.
- **Parameters.** `IncidentParms` carries the threat points, whether it was forced, the faction, a pawn count and how they arrive.
- **Threat points.** `StorytellerUtility.DefaultThreatPointsNow` works from wealth and colonist curves (`PointsPerWealthCurve`, `PointsPerColonistByWealthCurve`) with global minimum and maximum points. Rimtera's `ComputeThreatPoints` is its own formula of the same shape.
- **A queue.** The storyteller keeps an `incidentQueue` for incidents that fire later.

### 5.2 What REA pass 2 found
`tools/rea_storyteller.ps1` read 35 of 39 types from RimWorld 1.6 (`docs/rimworld-evidence/storyteller-pass2/summary.json`: field names, method signatures and calls only). Four types are not in this version under those names: `IncidentWorker_ManhunterPack`, `IncidentWorker_RefugeePodCrash`, `StoryWatcher_Strength` and `StoryWatcher_Wealth`. As with every evidence pass, no method bodies, constants, curves or definitions were read or copied; the numbers below are left for Rimtera to choose.

| Part | Evidence (names) | What it means |
|---|---|---|
| **Storyteller definition** | `StorytellerDef`: `comps`, `pointsFactorFromDaysPassed`, `adaptDaysMin`, `adaptDaysMax`, `adaptDaysGameStartGraceDays`, `pointsFactorFromAdaptDays`, `adaptDaysLossFromColonistLostByPostPopulation`, `adaptDaysLossFromColonistViolentlyDownedByPopulation`, `adaptDaysGrowthRateCurve`, `populationIntentFactorFromPopCurve`, `populationIntentFactorFromPopAdaptDaysCurve`, `disablePermadeath` | A storyteller is a list of components plus a handful of curves. Personality comes from which components it has and how the curves are shaped. |
| **Adaptation** | `StoryWatcher_Adaptation`: `adaptDays`, `Notify_PawnEvent`, `ResolvePawnEvent`, `AdaptationWatcherTick` (reads days since settling) | One number, "adapt days", grows over time at a rate set by a curve and is clamped between a minimum and maximum. Losing a colonist, or one being violently downed, takes some away, scaled by population. Threat points are multiplied by a factor of it. A grace period at the start protects new colonies. |
| **Population adaptation** | `StoryWatcher_PopAdaptation`: `adaptDays`; `StoryState`: `colonistCountTicks`, `RecordPopulationIncrease` | A separate counter tracks how long the colony has been at its size, which feeds population intent. |
| **Population intent** | `StorytellerUtilityPopulation`: `CalculatePopulationIntent`, `AdjustedPopulationValue`, `PopulationValue_Prisoner`, `PopulationValue_Slave`, `PopulationValue_Child`; `IncidentDef.populationEffect`; `StorytellerCompProperties.minIncChancePopulationIntentFactor` | The storyteller has a target colony size. Each incident says whether it adds or removes people, and its chance is scaled by how much the colony "wants" people right now. Prisoners, slaves and children count for less than a full colonist. |
| **Threat cycles** | `StorytellerCompProperties_OnOffCycle`: `onDays`, `offDays`, `minSpacingDays`, `numIncidentsRange`, `acceptFractionByDaysPassedCurve`, `acceptPercentFactorPerThreatPointsCurve`, `acceptPercentFactorPerProgressScoreCurve`, `forceRaidEnemyBeforeDaysPassed`; `ThreatsGenerator` and `ThreatsGeneratorParams` with the same fields | Threats come in "on" windows separated by "off" windows. A window holds a number of incidents with a minimum spacing. Whether a proposed threat is accepted depends on days passed, the threat points, and a **progress score**. The same generator is reused elsewhere (quests). |
| **Random storyteller** | `StorytellerCompProperties_RandomMain`: `mtbDays`, `categoryWeights`, `maxThreatBigIntervalDays`, `randomPointsFactorRange`; `ChooseRandomCategory`, `GenerateParms` | Incidents arrive on a mean time between events, by weighted category, with threat points randomly scaled within a range. A big threat is guaranteed if too many days pass without one. |
| **Background events** | `StorytellerComp_CategoryMTB`, `StorytellerComp_SingleMTB`, `StorytellerComp_FactionInteraction` | Other components each add their own stream: one category on a mean time, one specific incident on a mean time, visitors and traders. |
| **Opening** | `StorytellerComp_ClassicIntro`; `Difficulty.allowIntroThreats` | A component scripts the start of a game. |
| **Reactions** | `StorytellerComp_Triggered.Notify_PawnEvent` (adds to the `IncidentQueue` when a colonist is downed or dies) | Some incidents are follow-ups to something that happened, queued for later. |
| **The queue** | `IncidentQueue`: `queuedIncidents`, `IncidentQueueTick`; `QueuedIncident`: `fireTick`, `retryDurationTicks`, `triedToFire` | Incidents can be scheduled for a later time. If one cannot fire when its time comes, it keeps retrying for a while. |
| **Memory** | `StoryState`: `lastThreatBigTick`, `lastFireTicks`, `recentRandomIncidents`, `Notify_IncidentFired` | When the last big threat came, when each incident last fired, what fired recently. |
| **Incident gating** | `IncidentDef`: `baseChance`, `category`, `targetTags`, `earliestDay`, `minPopulation`, `minGreatestPopulation`, `minRefireDays`, `minDifficulty`, `disabledWhen`, `pointsScaleable`, `minThreatPoints`, `maxThreatPoints`, `allowedBiomes`, `disallowedBiomes`, `tags`, `refireCheckTags`, `chanceFactorByPopulationCurve`, `letterLabel`, `letterText`, `durationDays` | Every incident carries its own gates. `tags` with `refireCheckTags` give related incidents a shared cooldown (two kinds of raid don't stack). |
| **Difficulty** | `DifficultyDef` and `Difficulty`: `threatScale`, `allowBigThreats`, `allowIntroThreats`, `adaptationGrowthRateFactorOverZero`, `adaptationEffectFactor`, `threatsGeneratorThreatCountFactor`, `colonistMoodOffset`, `cropYieldFactor`, `mineYieldFactor`, `fishingYieldFactor`, `researchSpeedFactor`, `diseaseIntervalFactor`, `raidLootPointsFactor`, `allowExtremeWeatherIncidents`, `fixedWealthMode`, `lowPopConversionBoost`, `enemyDeathOnDownedChanceFactor`, and more | Difficulty is many separate dials, not one number: how hard threats hit, how fast adaptation grows and how much it matters, base mood, yields, research speed, disease frequency. |
| **Mental breaks** | `MentalBreaker`: `ticksBelowMinor`, `ticksBelowMajor`, `ticksBelowExtreme`, `MinorBreakMTBDays`, `MajorBreakMTBDays`, `ExtremeBreakMTBDays`, `MajorBreakMoodFraction`, `ExtremeBreakMoodFraction`, `MinTicksBelowToBreak`, `MinTicksSinceRecoveryToBreak`, `RandomFinalStraw`; `MentalBreakWorker`: `BreakCanOccur`, `CommonalityFor` | Three break levels with their own mood thresholds. Mood must stay below a threshold for a while before a break can roll, breaks roll on a mean time per level, and there is a cooldown after recovering. The break names the thought that was the "final straw". |
| **Inspirations** | `InspirationHandler`: `StartInspirationMTBDaysAtMaxMood`, `MinMood`, `TryStartInspiration` | The good side of mood: above a minimum mood, an inspiration rolls on a mean time. |
| **Arrivals** | `IncidentWorker_WandererJoin`: `RelationWithColonistWeight`, `CanSpawnJoiner`, `GeneratePawn` | A joiner can be related to someone already in the colony. |

### 5.3 Rimtera's storyteller
Built on the existing pipeline (`Systems/Storyteller.cs`), with each part below as a Rimtera component. Every number is Rimtera's own, chosen in tuning.

**Pieces**
- **Acts as the progress score.** RimWorld scales threat acceptance by a progress score. Rimtera's progress score is the run's act (§2) plus bosses down. Each act also opens its own incident pool and sets a floor and ceiling for threat points.
- **Threat points.** Wealth + crew strength + act, then × days-in-run factor, × adaptation factor, × difficulty's threat scale.
- **Adaptation.** One "adapt days" number per run. It grows each day (faster on harder settings), is clamped between a minimum and a maximum, and starts with a grace period. A colonist death takes a large bite out of it, a colonist downed in a fight a smaller one, both scaled by how small the colony is. Its factor multiplies threat points. This is what keeps losses from snowballing into a guaranteed wipe, and lets a battered colony recover.
- **Population intent.** Each act has a target crew size. Every incident declares its population effect (an arrival adds one, a deadly sickness may remove one). Arrivals get likelier the further the colony is below its target. That makes permadeath playable: deaths bring replacements over time. Captured raiders, if added later, count as part of a colonist.
- **Threat cycles.** An on/off cycle component: a few quiet days, then an "on" window with a number of threats spaced apart. The acceptance chance rises with days passed, threat points and the act.
- **Big-threat guarantee.** The random component guarantees a major threat if too many days pass without one, so the Wild storyteller cannot stay quiet forever.
- **Background streams.** Separate components on mean times: arrivals, traders and visitors, weather and world events, sickness.
- **Opening.** An intro component scripts act 1: the AI's first advice, the first arrival once a house stands, the first small threat after the grace period.
- **Follow-ups.** A triggered component reacts to colony events and queues a follow-up. A colonist dies, and later a mourning break or a vengeful relative arrives. A raid is beaten, and later its survivors return. A zealot preacher visits, and later someone converts.
- **The queue with warnings.** Big threats are queued a few hours ahead with a warning shown to the player, and retry for a while if they cannot fire. This is how the player reads the odds.
- **Shared cooldowns.** Incidents carry tags. Related incidents share a refire timer, so two raid types cannot land back to back.
- **Difficulty as separate dials.** Threat scale, big threats on or off, adaptation speed and strength, base mood, farm, mine and fishing yields, research speed, sickness frequency, and the death-drop tier (§3.2). Easy, Medium and Hard are presets of these dials, and a custom setting can expose them all.
- **Breaks and inspirations.** Three break levels (minor, major, extreme) with their own mood thresholds. Mood must stay low for a while before a break can roll, there is a cooldown after one, and the break names its final straw ("broke after Mira died"). Above a mood threshold, inspirations roll instead (a colonist works much faster for a day).
- **Related arrivals.** A joiner can be related to a living or dead colonist (a sibling looking for them), which feeds the narrative aesthetic.

**Personalities**, as presets of the components above. The storyteller is picked at world creation and stays for the whole run.

| | Steady | Wild | Builder |
|---|---|---|---|
| Threats | On/off cycle; windows grow with each act | Random categories on a mean time; big swings in threat points; big-threat guarantee | Long off windows; small on windows |
| Adaptation | Normal | Weak (losses barely soften what comes next) | Strong |
| Population target | Middling | Swings | Generous |
| For | Readable challenge | Chaos and stories | Building and the colony |

**Built on what exists.** Rimtera's `Storyteller` already has the interval check, category roll, refire timers and threat points. New code: a component interface, adaptation and population watchers, the queue with warnings, tags, act gating, difficulty dials, and the three presets.

### 5.4 Incident catalogue (draft)
Existing: Raid, Cold snap, Heat wave, Toxic fallout, Solar flare, Cargo drop.

| Category | New incidents |
|---|---|
| **Arrivals** | Escape-pod survivor (crash site, maybe injured); Wanderer asks to join (sometimes a relative of a colonist, living or dead); Refugee chased by raiders (take them in and fight, or turn them away); Prisoner freed from a raid; Survivor found deep in a biome (rescue to recruit) |
| **Major threats** | Raid on the colony; Raid on an outpost while you are away; Sappers who dig through walls; Hunting pack of maddened creatures; Siege that camps and shells; Fuel-sea creatures surging inland; the Fracture's surface storm |
| **Minor threats** | Sickness that spreads; Crop blight; Station breakdown; Fire; A colonist's mental break; Short circuit in powered stations |
| **Follow-ups** (queued by the triggered component) | Mourning after a death; a vengeful relative of someone you killed; beaten raiders returning; a preacher's visit followed by a conversion |
| **World events** | The blood-moon, eclipse, invasion and rain analogues from `Systems/EventManager.cs`, rescheduled by the storyteller instead of fixed dusk and dawn odds |
| **Zealots (act 5+)** | Preacher visits; a colonist converts; sabotage of a station or the ship; a converted colonist deserts with supplies; cult raid |
| **Good** | Cargo drop; Trader ship; Inspiration (a colonist works much better for a day); Ally visit |
| **Launch (act 7)** | Fuel-up siege: waves until the ship is fuelled |

## 6. Draft Mechanic Mixer cards
To go into `design/cards/` in the StewBones Mixer format once this direction is agreed. Each card names its Terraria or RimWorld baseline (influence) and our twist.

| Card | Slot | Influence | Our twist | Aesthetic |
|---|---|---|---|---|
| Escape run | core | Terraria progression ladder | The ladder builds a ship; the run ends when you leave or the colony falls | challenge |
| Storyteller acts | threat | RimWorld storytellers | Acts tied to the Terraria ladder; threats scale with how far you've come | narrative |
| Permadeath crew | threat | RimWorld colonists | Everyone but you dies for good; replacements only by arrivals | narrative |
| Death drops | threat | Terraria death drops | Easy, Medium and Hard drop tiers; dying leaves the colony alone | challenge |
| Skilled work | economy | RimWorld work priorities | Crew skills speed up and improve Terraria's stations, farms, fishing and cooking | expression |
| Station bills | economy | RimWorld bills | Terraria stations run queued recipes worked by crew | expression |
| Outposts | growth | Terraria pylons | A pylon needs a staffed outpost, which the storyteller can raid | challenge |
| Station AI heart | threat | Terraria's guide | The guide holds the ship plans; protect it or lose the run | narrative |
| Zealots | variety | Terraria's cultists | Cultists can turn your own crew | narrative |
| Fuel cores | variety | Terraria's celestial pillars | Collapse four fuel cores into the planet's core to call the end boss | discovery |
| Launch siege | core | RimWorld's ship launch | Hold the colony while the ship fuels; board whoever lives | challenge |
| Planet secrets | variety | Terraria biomes | Diesel seas at the edges, an irradiated core, the surface torn by the Fracture | discovery |

## 7. Decisions and open questions

### 7.1 Decided (2026-10-31)
| Question | Decision |
|---|---|
| Hard drop and Ironman | Ironman stays as its own setting: the "impossible run", a hardcore mode where the character dies for good, on top of Hard's drop rules |
| Town NPCs and shops | Each town NPC's type decides the shop it runs, as in Terraria |
| Shopkeepers and work | Town NPCs also operate stations and take jobs, for efficiency: they help the player's work, they do not automate it |
| Outposts and pylons | Every pylon needs a staffed outpost, as in Terraria. Items and potions take you home |
| Zealot conversion | On hold. Discuss when act 5 (the zealots) is built |
| Storyteller choice | Picked at world creation, fixed for the run |
| Run length | 10–14 hours from landfall to launch on a medium world; 8–15 is the outer range |

### 7.2 Still open
1. **Zealots:** whether a converted colonist can be won back, and how conversion works, when act 5 is designed.
