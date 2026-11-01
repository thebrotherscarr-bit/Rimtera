# Rimtera: the long-term vision

Written 2026-11-01 with the owner, after the 0.3.5 check-in. This is the top-level plan: what the game is, what each
source game gives it, and the road to 1.0. `docs/PLAN.md` holds the step-by-step roadmap under it, and
`docs/DESIGN_COLONY_ESCAPE.md` holds the run's acts and the storyteller design.

## 1. The game

**Terraria is the base, with a sci-fi twist, an AI storyteller, and a more in-depth NPC system.** (The owner,
2026-11-01.) Everything below follows from that line.

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
- **The colony works by default, by skill.** There is no Work tab and there are no bills. People orient toward their
  preferred work from their background, and a better cook or chemist does it faster and better.
- **Then the depth that makes them people:**
  - random backgrounds and traits;
  - skills that grow with use;
  - things they can't or won't do;
  - lasting injuries: someone shot in a raid loses an arm and can only cook from then on;
  - **bionics** as the sci-fi twist: a lost arm can be replaced;
  - people you would rather not keep, and raiders you can capture and convert.
- **Needs stay simple: food only.** Mood and rest are left out for now and can come later.
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

**1.0 has every part in.** The whole base game has to be working and tested: every mechanic, the full run from
landfall to launch, and its assets.

| Milestone | Theme |
|---|---|
| 0.3.x | Foundations (done to 0.3.5), then the dev console and the steps below |
| **0.4.0** | The colony defends |
| **0.5.0** | People with stories |
| **0.6.0** | The storyteller |
| **0.7.0** | Outposts and travel |
| **0.8.0** | The Fracture and the zealots |
| **0.9.0** | The escape |
| **0.10.0** | Look, feel and sound of Rimtera: every visual and sound in the Rimtera style |
| **1.0.0** | Release: everything in, working and tested |
| **1.1.0** | Local co-op: split-screen for up to four players |
| **1.2.0** | Online: networked multiplayer |

**Multiplayer is designed for early** (§3.5): per-player input, camera, inventory and saves from 0.4.0 on, and a clean
line between game state and player commands, so 1.1 and 1.2 add players rather than rewrite the game.

**Later talks, not planned yet:** alternate endings and other win conditions. There is no post-game content: after
launch you may continue the world, and the story from there is yours.

### 4.1 Steps to 0.4.0

| Step | Contents |
|---|---|
| 0.3.6 | **The dev console.** Trigger any game state, start any event, spawn any item or creature, and test anything in the game (§6). |
| 0.3.7 | **Research tiers by boss.** Each boss opens the next research tier. Studying it takes time, and finishing it unlocks the tier's recipes and stations. The 0.2.7 study-point projects fold in. Terraria's own ladder stays the base. |
| 0.3.8 | **The station AI powers the base.** It boots on energy cells or ore. Its fuel is tied to progress, and higher-tier stations need more power. The stations go dark without it, and you rebuild it to carry on. |
| 0.3.9 | **The colony by default.** The Work tab and bills are removed. People orient toward their preferred work from their background, and work it by skill: a chemist keeps healing in stock, a cook keeps food. |
| 0.3.10 | **One kind of character.** The crew merge into the town NPCs. *(Done: every resident is a person with skills who works by skill; the crew became settlers.)* |
| 0.3.11 | **Multiplayer groundwork.** Per-player input, camera, inventory and saves; game state separated from player commands. *(Done: `docs/MULTIPLAYER.md`.)* |
| 0.4.0 | **The colony defends.** Guards and outposts, equipping people (with Terraria's gear), purposeful storyteller raids with warnings, a balance pass. Steps: 0.4.0 equipping people *(done)*, 0.4.1 guards and guard posts *(done)*, 0.4.2 outposts *(done)*, 0.4.3 raids with warning *(done)*, 0.4.4 balance pass *(done)*. |

### 4.2 0.5.0: People with stories

| Step | Contents |
|---|---|
| 0.5.1 | **Backgrounds and traits.** A random childhood and adulthood set each person's skill spread, their preferred work, and what they won't or can't do. *(Done.)* |
| 0.5.2 | **Skills grow by doing.** A chemist gets better the more they brew. *(Done.)* |
| 0.5.3 | **Health and lasting injuries.** Wounds land on body parts. A lost arm limits someone to one-handed work. *(Done.)* |
| 0.5.4 | **Bionics.** Replacement limbs and implants, crafted and fitted. *(Done.)* |
| 0.5.5 | **Food as the one need.** People eat from storage; going hungry slows them, then hurts them. *(Done.)* |
| 0.5.6 | **Prisoners and conversion.** Down a raider, hold them in a cell, and recruit them. Send away anyone you'd rather not keep. *(Done.)* |

### 4.3 0.6.0: The storyteller

| Step | Contents |
|---|---|
| 0.6.1 | **Storytellers to pick** at world creation: three personalities (steady, chaotic, builder-friendly) and difficulty dials. *(Done.)* |
| 0.6.2 | **Acts follow Terraria's progress:** landfall, descent, the core, the Fracture, the zealots, the collapse, launch. Each act opens new events. *(Done.)* |
| 0.6.3 | **The threat budget and warnings.** Strength scales with progress and wealth. Warnings come as letters with a countdown. *(Done.)* |
| 0.6.4 | **The event catalogue.** Terraria's events and invasions under the storyteller, plus arrivals, sickness, power outages for the station AI, supply drops and traders. |
| 0.6.5 | **Purposeful events.** Each act's key events push you toward the next step of progress. |
| 0.6.6 | **The History tab:** the run's story so far. |

### 4.4 0.7.0: Outposts and travel

| Step | Contents |
|---|---|
| 0.7.1 | **Outposts.** A staffed home away from the base, guarded by the people who live there. |
| 0.7.2 | **Pylons need a staffed outpost.** Recall items take you home. |
| 0.7.3 | **Raids reach outposts.** Defences use Terraria's traps and sentries, manned by your people. |
| 0.7.4 | **The station AI's power reaches outposts,** so their stations work while the power holds. |

### 4.5 0.8.0: The Fracture and the zealots

| Step | Contents |
|---|---|
| 0.8.1 | **The Fracture as Terraria's hardmode.** A shorter second ladder, checked against the 10–14 hour run. |
| 0.8.2 | **Hardmode research tiers and hardmode events,** scheduled by the storyteller. |
| 0.8.3 | **The zealots.** Cultists raid the base and outposts. They do not convert your people. |
| 0.8.4 | **The four fuel cores:** siege events at the obelisks. |

### 4.6 0.9.0: The escape

| Step | Contents |
|---|---|
| 0.9.1 | **The collapse.** The fuel cores collapse into the core and call the Astro-God. |
| 0.9.2 | **The ship,** researched and built from parts and fuelled by the Astro-God's core. |
| 0.9.3 | **The fuel-up siege:** the storyteller's climax while the ship fuels. |
| 0.9.4 | **Boarding and the escape screen:** who made it out, and the run's chronicle. |
| 0.9.5 | **Continue the world** after launch, if you want to. |

### 4.7 0.10.0: Look, feel and sound of Rimtera

Before 1.0, the whole game has to look like Rimtera, feel like Rimtera and sound like Rimtera. The style track (§7)
fills assets in along the way. This milestone closes it out: no placeholder, mismatched sprite or synth stand-in left.

| Step | Contents |
|---|---|
| 0.10.1 | **The style guide, final.** Palette, outline, lighting and scale rules, and each biome's palette, settled from the boss sheet and the original sheets (drafted early, §7). |
| 0.10.2 | **Every sprite in style.** Items, creatures, bosses, NPCs (each with its own sprite), tiles, walls and backgrounds, redrawn or refined to match the guide. A style check confirms each one. |
| 0.10.3 | **Animation.** Every character, creature and boss animates in the same frame style. |
| 0.10.4 | **The UI drawn from art.** Panels, slots, bars, buttons, cursor and title in the boss-sheet look. The renderer changes to draw the UI art rather than code shapes. |
| 0.10.5 | **Music.** Every biome, event, boss and the escape has its theme, in one Rimtera sound. |
| 0.10.6 | **Sound effects, ambience and voices.** Every cue (`--sound-cues`) has a recorded or generated sound, and every biome has its ambience. |
| 0.10.7 | **Game feel.** Hit feedback, screen shake, particles, lighting and the final mix, so it plays like Rimtera. |

### 4.8 1.0.0: Release
- Every part in, working and tested: all mechanics, the whole run, and all assets in the Rimtera style.
- Run length tuned to 10–14 hours on medium worlds, plus a balance pass across the whole run.
- Onboarding, save stability and polish.

## 5. Rules for every system
1. Terraria's simplicity first. A system that needs a manual to use is too complicated.
2. People help and never take the game out of your hands. Their work is a bonus: faster and better, never required.
3. Every system feeds the challenge or the run's story, or it needs a reason to exist.
4. Every run should differ: the storyteller and the people are the variety.

## 6. Testing: the dev console
- **A dev console from 0.3.6 on.** It can trigger any game state:
  - set the time, progress flags, defeated bosses and the Fracture;
  - start or stop any event or raid;
  - spawn any item, creature, boss or NPC;
  - give or take health and statuses;
  - edit any NPC's skills, background and injuries;
  - fill or empty the station AI's power;
  - teleport.
- It is how the owner and the automated tests reach any situation fast.
- Every new mechanic adds its console commands in the same step that adds the mechanic.
- The self-tests (core, Batch A, the deterministic demo, the asset check, the bench) keep running on every step.

## 7. Style: art, audio and feel through ComfyUI
- **The goal:** before 1.0 the game looks like Rimtera, feels like Rimtera and sounds like Rimtera (milestone 0.10.0).
- **The pipeline:**
  - Art and audio are made locally in ComfyUI, with the flows in `docs/COMFYUI.md` and `docs/AUDIO_FLOW.md`.
  - The owner decides when ComfyUI runs.
  - Claude asks for assets to be generated or refined, and integrates what comes back.
- **The style:**
  - The Rimtera style is the one in the docs (`docs/ART_PROMPTS.md`, `docs/BIOME_THEMES.md`) and the original art
    sheets in `art/sheets/`.
  - The **boss sheet (`sheet_00c_bosses`)** is the preferred reference for style choices: riveted gunmetal, amber-gold
    accents.
  - The **style guide** (`docs/STYLE_GUIDE.md`, drafted 2026-11-01 from the four reference sheets in `docs/style/`) is used by every request and settled in 0.10.1.
- **A style track runs through every milestone:**
  1. A step that adds things to see or hear lists them in `docs/ASSET_REQUESTS.md`. Each entry gives the sprite or
     sound name, its size or length, the flow to use, a prompt and the style reference.
  2. Until the real asset arrives, a kitbashed placeholder keeps the asset check clean.
  3. When the owner's ComfyUI run delivers the asset, it replaces the placeholder in a later step.
- **0.10.0 closes the track:** every placeholder replaced, every sprite and sound checked against the guide, the UI
  drawn from art, and the game-feel pass done.
