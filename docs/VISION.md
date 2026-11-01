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
| **Progress** | Terraria's ladder as it is: ores, bars, gear, bosses, hardmode | Research that takes time, opened by bosses |
| **Pressure** | The events and invasions themselves | The storyteller that schedules them, with advance warning |
| **People** | Move-in rules, shops, happiness, housing | Skills, backgrounds, traits, injuries, permadeath, gear, prisoners |
| **Gear and build your way** | All of it: gearing, items, equipment, accessories, class builds | Only the act of equipping your people (with that same Terraria gear) |

**Where they mix:** push yourself to progress, and let your people semi-automate around you. For example, build a
potion setup, then find a good chemist who keeps you stocked with healing for your next dive. That is handy, never
required. You can always do it yourself.

## 3. The pillars

### 3.1 Progress: Terraria's ladder, with research on top
- **The base is Terraria's progression, as it is:**
  - ores to bars to gear;
  - bosses that gate the world (the dungeon, the Fracture as hardmode, the hardmode ores);
  - stations you craft up to, including a new anvil for hardmode ores;
  - class builds from gear you craft and find, as in Terraria.
- **Research adds time on top.** Beating a boss opens the next research tier. Studying it takes time, and when it
  finishes it unlocks the new recipes and stations that tier brings. Better researchers study faster.

- **Gear is Terraria's.** Gearing, itemization, equipment and accessories all follow Terraria: the same kinds of
  weapons, armour sets, accessories and class builds, crafted and found the same way. Your people wear that same
  gear: RimWorld supplies the act of equipping them, not a separate item system.

### 3.2 The station AI powers the base
- The station AI powers the crafting stations.
- **It starts on energy cells, and its fuel is tied to progress.** It boots on early cells or ore. Better fuels from
  deeper and later in the ladder raise its power, and the higher-tier stations need that power to run. Keeping it fed
  is part of climbing the ladder. The exact fuels and tiers are designed in 0.3.8.
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

### 3.4 Pressure: the storyteller runs every event, and events have a purpose
- The storyteller is picked at world creation and fixed for the run.
- It runs every event: raids, invasions, arrivals and calamities. They come scaled to your progress, with **advance
  warning**.
- Events are purposeful: they push you through the run's story and toward the next step of progress, rather than
  being random noise.
- Raids aim at your base, your people's outposts and the station AI. That gives each one a tower-defence feel: your
  people hold their areas while you are away, and fight beside you when you are home.

### 3.5 Together: co-op and online
- **Local split-screen co-op for up to four players** comes in 1.1.
- **Networked multiplayer** follows it.
- Both are pre-planned from 0.4.0 on:
  - Each player's input, camera, inventory and save state are kept per-player.
  - The simulation keeps a clean line between the game state and each player's commands, so it can later be shared
    across a network.

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
| **1.2.0** | Online | Networked multiplayer |

**Multiplayer is designed for early** (§3.5): per-player input, camera, inventory and saves from 0.4.0 on, and a clean
line between game state and player commands, so 1.1 and 1.2 add players rather than rewrite the game.

### 4.1 Steps to 0.4.0

| Step | Contents |
|---|---|
| 0.3.6 | **Research tiers by boss.** Each boss opens the next research tier. Studying it takes time, and finishing it unlocks the tier's recipes and stations. The 0.2.7 study-point projects fold in. Terraria's own ladder stays the base. |
| 0.3.7 | **The station AI powers the base.** It boots on energy cells or ore. Its fuel is tied to progress, and higher-tier stations need more power. The stations go dark without it, and you rebuild it to carry on. |
| 0.3.8 | **The colony by default.** People work their trade by skill with nothing to set. A chemist keeps healing in stock; a cook keeps food. |
| 0.3.9 | **One kind of character.** The crew merge into the town NPCs. |
| 0.3.10 | **Multiplayer groundwork.** Per-player input, camera, inventory and saves; game state separated from player commands. |
| 0.4.0 | **The colony defends.** Guards and outposts, equipping people, purposeful storyteller raids with warnings, a balance pass. Split into 0.4.x steps when we get there. |

## 5. Rules for every system
1. Terraria's simplicity first. A system that needs a manual to use is too complicated.
2. People help and never take the game out of your hands. Their work is a bonus: faster and better, never required.
3. Every system feeds the challenge or the run's story, or it needs a reason to exist.
4. Every run should differ: the storyteller and the people are the variety.
