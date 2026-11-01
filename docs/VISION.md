# Rimtera: the long-term vision

Written 2026-11-01 with the owner, after the 0.3.5 check-in. This is the top-level plan: what the game is, what each
source game gives it, and the road to 1.0. `docs/PLAN.md` holds the step-by-step roadmap under it, and
`docs/DESIGN_COLONY_ESCAPE.md` holds the run's acts and the storyteller design.

## 1. The game

**A Terraria playthrough with more chaos and calamity.** You crash-land, dig, craft and fight up a Terraria ladder of
bosses and gear, and you gather people who guard and help you. A RimWorld storyteller, picked when the world is made,
runs every event and raid. It warns you before they come, so each one plays like a small tower defence of your base
and your people's outposts. Because the storyteller and the people differ every time, no two runs play the same.

- **The run:** 10–14 hours from landfall to launch on a medium world. Beating the end boss and launching the ship is
  the ending, as beating the Moon Lord is in Terraria.
- **Keep going if you want:** after the ending, you can carry on in the same world for a long-scale playthrough:
  building, farming bosses, growing the colony.
- **Replayability:** comes from the survival loop and the storyteller's variety, not from a longer base game.

## 2. What each game brings

| | From Terraria | From RimWorld |
|---|---|---|
| **The building block** | Simplicity: digging, building, movement, combat, crafting, biomes | — |
| **Progress** | Boss-gated ladder; ores; gear tiers; hardmode | Research that takes time |
| **Pressure** | The events and invasions themselves | The storyteller that schedules them, with advance warning |
| **People** | Move-in rules, shops, happiness, housing | Skills, backgrounds, traits, injuries, permadeath, gear, prisoners |
| **Build your way** | Class gear, accessories, event drops | Equipping your people |

**Where they mix:** push yourself to progress, and let your people semi-automate around you. For example, build a
potion setup, then find a good chemist who keeps you stocked with healing for your next dive. That is handy, never
required. You can always do it yourself.

## 3. The pillars

### 3.1 Progress: bosses open research, research takes time
- Each boss opens the next **research tier**. Studying it takes time, and your researchers' skill speeds it up.
- A finished tier unlocks the new recipes, and the **station upgrade** that makes them. That upgrade is built at
  the old station from materials, the way Terraria makes you craft a new anvil for hardmode ores.
- The ladder:
  - Overseer → Nano-Fabricator Mk2
  - Scout Mech → Ore Refinery Mk2
  - Fission Tyrant → Alloy Anvil
  - a hard-trio boss → Void Forge
  - Mutated Flora → Astral Assembler
- Boss parts make the next drill tier.
- **Events fill the power gaps** between bosses. They drop the gear that carries you to the next boss, and the
  class-build gear (melee, ranged, tech, command).

### 3.2 The station AI powers the base
- The station AI powers the crafting stations. It runs on power and has to be kept fed.
- Losing it doesn't end the run. The stations go dark, and you have to rebuild it before you can progress again.
- That makes it something raids aim for and you defend.

### 3.3 People: shallow first, then RimWorld depth
- **To start:**
  - a trade (what they sell and which stations they work);
  - skills that make them better at that work;
  - a small outpost or home area they guard;
  - gear you equip them with.
- **The colony works by default, by skill.** No bills or work orders to set: people do their trade's work on their
  own, and a better cook or chemist does it faster and better.
- **Then the depth that makes them people:**
  - random backgrounds and traits;
  - things they can't or won't do;
  - lasting injuries: someone shot in a raid loses an arm and can only cook from then on;
  - people you would rather not keep, and enemies you can capture and convert.
- **Losing someone matters.** A dead master chemist slows your potions until you find another.

### 3.4 Pressure: the storyteller runs every event
- The storyteller is picked at world creation and fixed for the run.
- It schedules raids, invasions, arrivals and calamities, scaled to your progress, with **advance warning**. Raids
  aim at your base, your people's outposts and the station AI.
- Your people hold their areas while you are away. When you are home, you fight beside them.

### 3.5 Together: local co-op
- Split-screen local play for up to four players is the multiplayer goal.
- Online play is not planned yet.

## 4. The road to 1.0

| Milestone | Theme | Contents |
|---|---|---|
| 0.3.x | Foundations (done to 0.3.5), then progress | 0.3.6–0.3.11, below |
| **0.4.0** | The colony defends | One kind of character; guards and outposts; equipping people; storyteller raids with warnings (tower defence) |
| **0.5.0** | People with stories | Backgrounds and traits; incapabilities; lasting injuries; light mood; prisoners and conversion; sending people away |
| **0.6.0** | The storyteller | Run acts; storytellers to pick from; the incident catalogue; difficulty dials; letters and the History tab |
| **0.7.0** | Outposts and travel | Staffed outposts as pylon anchors; raids on outposts; recall |
| **0.8.0** | The Fracture and the zealots | A shorter hardmode ladder; zealot incidents; the four fuel cores |
| **0.9.0** | The escape | The Astro-God, the ship and its fuel-up siege, launch, the escape screen, and **continue the world** afterwards |
| **1.0.0** | Release | Run length 10–14 h tuned on medium worlds; balance; polish; onboarding |
| **1.1.0** | Local co-op | Split-screen for up to four players |

**Co-op is designed for early.** Each player's input, camera and inventory should be per-player from 0.4.0 on, so
1.1 adds the split screen instead of rewriting the game. Anything that assumes a single player gets a note when it's
touched.

### 4.1 Steps to 0.4.0

| Step | Contents |
|---|---|
| 0.3.6 | **Research tiers by boss.** Bosses open research tiers. Studying takes time and unlocks the tier's recipes and its station upgrade. The 0.2.7 study-point projects fold in. |
| 0.3.7 | **The upgrade ladder and boss-part drills.** Station upgrades built from materials. The next drill tier comes from boss parts. The Alloy Anvil and viridium gear wait for the Fracture. |
| 0.3.8 | **The station AI powers the base.** Power and fuel. Stations go dark without it. Rebuild it to carry on. |
| 0.3.9 | **The colony by default.** People work their trade by skill with no bills to set: a chemist keeps healing in stock, a cook keeps food. Bills and the Work tab become optional, then are cut or hidden. |
| 0.3.10 | **Events fill the power gaps.** Each gap between bosses gets an event, and each event drops class gear. |
| 0.3.11 | **One kind of character.** The crew merge into the town NPCs. |
| 0.4.0 | **The colony defends.** Guards and outposts, equipping people, storyteller raids with warnings, a balance pass. Split into 0.4.x steps when we get there. |

## 5. Rules for every system
1. Terraria's simplicity first. A system that needs a manual to use is too complicated.
2. People help and never take the game out of your hands. Their work is a bonus: faster and better, never required.
3. Every system feeds the challenge or the run's story, or it needs a reason to exist.
4. Every run should differ: the storyteller and the people are the variety.
