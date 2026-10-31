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

**Run length target:** 8–15 hours from landfall to launch on a medium world. The double ladder is the main risk to that. The hardmode ladder must be shorter than the first, not a repeat of it.

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
| **Hard** | Everything except tools | Ironman (permadeath) | New: drop the whole bag except tools, keep respawning. Ironman stays as an extra option on top |
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

**Rule:** crew make things faster and better, never fully hands-off. The player still fights the bosses, explores, and decides what gets made. A strong colony shortens the grind; a weak one makes the player do it.

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
| 1 | **Storyteller** | Interval-gated incidents, category then weighted roll, refire timers, threat points from wealth, crew, progression and day (`Systems/Storyteller.cs`); six incidents; 72-hour grace; raids gated on the first boss | Acts (§2), adaptation to losses, population intent, storyteller personalities, an incident queue with warnings, about twenty new incidents (§5) | XL |
| 2 | **Colony work** | Crew survival behaviour (eat, rest, mine, idle: `AI/SurvivalBehavior.cs`), skills and passions, work-speed stats | Work types and priorities, job givers per work type (the think-tree pattern in `docs/RESEARCH.md`), a work tab | L |
| 3 | **Station bills** | Crafting at stations by the player | A bill queue per station ("make 10 healing potions", "keep 20 torches"), worked by Crafting colonists | M |
| 4 | **Farming and brewing** | Herbs grow; potions are crafted by hand | Grow zones, colonist planting and harvesting, brew bills | M |
| 5 | **Cooking and food** | Food items and needs | Meals with skill-based quality, cook bills, crew eating from storage | M |
| 6 | **Fishing by crew** | Player fishing, quest fish | A fishing spot job for crew | S |
| 7 | **Hauling and storage** | Chests | Storage zones or tagged chests; crew haul drops and products to them | M |
| 8 | **Outposts and pylons** | Pylons need two housed residents nearby | Outposts as named bases with their own crew and storage; raids can hit an outpost while you are away | M |
| 9 | **Permadeath** | Town NPCs respawn on a timer; crew can die | Turn off the respawn timer; graves; death mood thoughts; replacements only from arrivals | S |
| 10 | **Death drops** | Explorer, Classic, Salvager, Ironman, Creative | Easy / Medium / Hard as in §3.2 | S |
| 11 | **Colony defence** | Raids spawn waves; crew can fight | Raids aim at the colony (and outposts), not just the player; guard posts; doors and walls matter; town NPCs fight back | L |
| 12 | **Mood, health, breaks** | Mood, thoughts, health with body parts, a mental break state | Break behaviours that cause trouble (wandering off, refusing work, smashing things); inspirations | M |
| 13 | **Station AI protection** | AI moves into the first house; hologram until then | Health and damage, raids that target it, run loss on its destruction; rebuild parked | S |
| 14 | **The arc** | Core gate boss, the Fracture, cultist ritual, obelisks, Astro-God | Act tracking; core boss on arrival; obelisks re-themed as fuel cores; collapse ritual | M |
| 15 | **Zealots** | Cultist ritual as a boss gate | Zealot incidents: preaching, conversion of a colonist, sabotage, desertion | M |
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

### 5.2 What still needs mapping (REA pass 2)
The first pass covered the core pipeline. The parts that give RimWorld's storytellers their feel were not covered: the component types that make Cassandra's cycles and Randy's swings, adaptation to losses, population steering, the incident definitions' gating fields, and difficulty. `tools/rea_storyteller.ps1` extracts their member and type structure on your PC (where RimWorld and REA are installed) into `docs/rimworld-evidence/storyteller-pass2/`. The types it reads:

| Group | Types | What we want to learn |
|---|---|---|
| Storyteller definition | `StorytellerDef`, `StorytellerCompProperties` | Which knobs a storyteller has: its component list, point and population settings |
| Threat pacing | `StorytellerComp_OnOffCycle`, `StorytellerComp_ThreatsGenerator`, `ThreatsGenerator`, `ThreatsGeneratorParams` | How threats come in on and off windows, and the knobs of a cycle |
| Randomness | `StorytellerComp_RandomMain`, `StorytellerCompProperties_RandomMain`, `StorytellerComp_CategoryMTB`, `StorytellerComp_SingleMTB` | How a "random" storyteller weights categories and mean time between events |
| Arrivals and intro | `StorytellerComp_ClassicIntro`, `StorytellerComp_Triggered`, `StorytellerComp_FactionInteraction`, `StorytellerUtilityPopulation` | How the opening is paced, triggered follow-ups, visitors, and steering toward a target population |
| Adaptation | `StoryWatcher`, `StoryWatcher_Adaptation`, `StoryWatcher_PopAdaptation`, `StoryWatcher_Wealth`, `StoryWatcher_Strength`, `StoryState` | What the storyteller remembers: losses, strength, wealth, last fire times |
| Incidents | `IncidentDef`, `IncidentCategoryDef`, `IncidentTargetTagDef`, `IncidentQueue`, `QueuedIncident`, `FiringIncident` | An incident's gating fields (earliest day, population limits, refire tags) and delayed firing |
| Difficulty | `DifficultyDef`, `Difficulty` | Which parts of pacing difficulty scales |
| Example workers | `IncidentWorker_RaidEnemy`, `IncidentWorker_WandererJoin`, `IncidentWorker_RefugeePodCrash`, `IncidentWorker_Disease`, `IncidentWorker_ManhunterPack`, `IncidentWorker_TraderCaravanArrival` | How individual incidents gate themselves |
| Breaks | `MentalBreaker`, `MentalBreakWorker`, `InspirationHandler` | How mood turns into trouble or inspiration |

As with every evidence pass: member names, field names and type structure only. No method bodies, constants, curves or definitions are copied. Rimtera's numbers and names are its own.

### 5.3 Rimtera's storyteller (design, pending pass 2)
Built on the existing pipeline. Each part below becomes a component of Rimtera's own.

- **Acts.** The run's act (§2) gates which incidents can fire and sets a floor and ceiling for threat points.
- **Threat points.** Wealth + crew strength + act + days in act, then × adaptation, × difficulty.
- **Adaptation.** Recent losses (crew dead, buildings destroyed, a lost outpost) lower the next threats for a while. A long, quiet, prosperous stretch slowly raises them. This is what keeps calamity survivable more often than not, and what lets a colony claw back from the edge.
- **Population intent.** Each act has a crew size the storyteller aims for. Below it, arrival incidents get likelier; above it, rarer. This is how permadeath stays playable.
- **Personalities.** Three to start, names our own:
  - **Steady:** threats on a cycle of quiet days and danger days, rising with each act. The readable one.
  - **Wild:** any category on any roll, big swings both ways. The chaotic one.
  - **Builder:** long quiet stretches, gentle threats, generous arrivals. The calm one.
- **The queue and warnings.** Big threats are queued with a warning ("raiders sighted to the east, arriving at dusk"), so the player can read the odds and choose: prepare, run, or go out and meet them.

### 5.4 Incident catalogue (draft)
Existing: Raid, Cold snap, Heat wave, Toxic fallout, Solar flare, Cargo drop.

| Category | New incidents |
|---|---|
| **Arrivals** | Escape-pod survivor (crash site, maybe injured); Wanderer asks to join; Refugee chased by raiders (take them in and fight, or turn them away); Prisoner freed from a raid; Survivor found deep in a biome (rescue to recruit) |
| **Major threats** | Raid on the colony; Raid on an outpost while you are away; Sappers who dig through walls; Hunting pack of maddened creatures; Siege that camps and shells; Fuel-sea creatures surging inland; the Fracture's surface storm |
| **Minor threats** | Sickness that spreads; Crop blight; Station breakdown; Fire; A colonist's mental break; Short circuit in powered stations |
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

## 7. Open questions
1. **Hard drop:** keep Ironman (character permadeath) as a separate option, or fold it into Hard?
2. **Town NPCs vs crew:** are shopkeepers colonists who can work, or a separate class who only trade?
3. **Outposts:** does every pylon need an outpost, or only pylons beyond the first?
4. **Zealot conversion:** can a converted colonist be won back (Social skill), or are they lost?
5. **Storyteller choice:** picked at world creation only, or changeable during a run?
6. **Run length:** is 8–15 hours on a medium world the right target?
