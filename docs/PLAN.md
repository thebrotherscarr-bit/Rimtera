# Rimtera: long-term plan

Written 2026-10-31. This is the path from today's build to 1.0. It ties together the design (`docs/DESIGN_COLONY_ESCAPE.md`), the Terraria-parity roadmap (`docs/ROADMAP_0.2.0.md` and `docs/roadmap/`) and the UI review (`docs/UI_UX_REVIEW.md`). Each milestone ends in a release and a **discussion point** where we play it, talk, and adjust the next milestone before starting it.

## Where we are
- Released: **0.1.7**. On `main` since then: music streaming, the music player, the ambience layer, and the art and audio tooling.
- The Terraria baseline is good and largely in place; the parity roadmap lists what is left.
- The colony and storyteller layer that makes Rimtera its own game is designed but mostly unbuilt.

## How versions work
- **Milestones** are minor versions: 0.2.0, 0.3.0 … 1.0.0. Each has a goal and a discussion point.
- **Steps** inside a milestone are patch releases (0.1.8, 0.1.9 … on the way to 0.2.0; 0.2.1, 0.2.2 … on the way to 0.3.0). Every step ships a playable build.
- Every step: tests pass (core, Batch A, the deterministic demo), the Windows build is clean, it merges through a PR, and it is published. Commits carry only the owner's name.
- The parity roadmap keeps its IDs (PC-, BO-, EN-…). Its steps are folded into the milestones below rather than renumbered.

## Milestones

### M1 → 0.2.0 "Stakes and baseline"
**Goal:** losing means something, and the Terraria basics a player reaches for are all there.
| Step | Contents |
|---|---|
| 0.1.8 **Stakes** | Easy / Medium / Hard / Ironman death drops; town NPCs and crew die for good, and come back only through arrivals; the station AI as the colony's heart (its loss ends the run); a run chronicle; the run-end screen |
| 0.1.9 **Terraria basics** | Coin and ammo slots; pet, light pet, mount and hook slots; right-click to cancel a buff; tooltip comparison; the shop grid with buyback; the housing page with NPC heads |
| 0.1.10–0.1.12 **Foundations and combat** | Parity roadmap 0.1.8 (clock, statuses, movement, menus) and 0.1.9 (combat core) |
| 0.2.0 **Baseline release** | Balance pass on the above; docs |

**Discussion point D1** (after 0.1.8): does permadeath feel fair? Are death drops right? Should Explorer and Creative keep their safety nets?
**Discussion point D2** (at 0.2.0): what feels most "Terraria" and what feels off; anything to pull forward.

### M2 → 0.3.0 "The colony works"
**Goal:** crew and town NPCs make the player's work faster and better, never automatic.
- Work types and priorities, with job givers per work type (RimWorld's think-tree pattern, `docs/RESEARCH.md`).
- Station bills worked by Crafting colonists; farming and brewing; cooking; crew fishing; hauling and storage zones.
- Town NPCs work stations and jobs too, alongside their shops.
- UI: the colonist bar, the Work tab in the hub, bills at a station, the zone tool in the hotbar, click-to-inspect cards.

**Discussion point D3:** is the colony helping without playing the game for you? How much should skill matter?

### M3 → 0.4.0 "The storyteller"
**Goal:** a run tells a story: arrivals, troubles and calamities paced by what the player has done.
- Storyteller components; acts as the progress score; adaptation; population intent; on/off threat cycles; the big-threat guarantee; follow-up triggers; the queue with warnings; shared cooldown tags; difficulty dials; Steady, Wild and Builder, picked at world creation (`docs/DESIGN_COLONY_ESCAPE.md` §5).
- The incident catalogue: arrivals, threats, sickness, breakdowns, follow-ups.
- Mental breaks with three levels; inspirations.
- UI: letters and alerts on the right edge; the History tab (the run's story).
- Parity roadmap 0.1.10 (creature AI and the boss framework) and 0.1.12 (events and weather, rescheduled through the storyteller).

**Discussion point D4:** play a run on each storyteller. Which calamities were fun, which were cheap? Tune before adding pressure.

### M4 → 0.5.0 "Pressure"
**Goal:** the colony is something to defend, spread across the world.
- Raids aimed at the colony and at outposts; guard posts; town NPCs fighting back; walls and doors mattering.
- Outposts: every pylon needs a staffed outpost; recall items and potions.
- Parity roadmap 0.1.11 (world and the progression ladder) and 0.1.13 (town and colony: happiness, pylons, reforging, move-in gates).

**Discussion point D5:** is spreading out worth the risk? Do raids feel like they come for the colony, or for the player?

### M5 → 0.6.0 "The escape"
**Goal:** a run has an ending you can win.
- The seven acts; the core's gate boss waking when you reach it; the Fracture; the four fuel cores and their collapse; the Astro-God; the ship, its parts and research; the fuel-up siege; boarding; the win screen.
- The zealots (designed at **discussion point D6**, before this milestone starts).
- Parity roadmap 0.1.14 (bespoke bosses, gear depth, building depth).

**Discussion point D7:** play a full run. How long did it take? Was the hardmode ladder too long?

### M6 → 0.7.0 "Depth and reconciliation"
**Goal:** the Terraria and RimWorld interfaces read as one game.
- UI depth from the review: Schedule and Research tabs, factions, medical, bestiary filters and search, map pins, reforge, dyes and vanity accessories, Journey-mode powers.
- The Mechanic Mixer cards for every system, with MDA notes and the lens review (StewBones' tool).

**Discussion point D8:** the lens review results. Cut, swap or strengthen cards.

### M7 → 0.8.0 "Pacing and balance"
**Goal:** a run takes 10–14 hours on a medium world, and every difficulty is fair.
- Run pacing, the second ladder, the boss and gear curve, difficulty dials, storyteller tuning; playtests against the targets.
- Parity roadmap 0.2.0 (balance, coloured lighting, CI asset and performance gates, the full parity play-through).

**Discussion point D9:** playtest results against the targets.

### M8 → 0.9.0 "Beta" and 1.0.0
**Goal:** it looks, sounds and saves like a finished game.
- Art and audio passes, performance, save safety, settings completeness, an onboarding pass for new players, credits.
- 1.0: a final balance pass and release.

**Discussion point D10:** what is in and out for 1.0.

## Standing decisions
From the design conversation; reopened only at a discussion point.
- Terraria is the baseline for feel and base mechanics; every system is twisted to Rimtera's needs.
- Nothing carries over between runs. Winning is beating the end boss and launching.
- People die for good. The player respawns, except on Ironman (the impossible run).
- The storyteller is picked at world creation. Time is real-time; there are no speed buttons.

## Progress
| Step | Status | What shipped |
|---|---|---|
| 0.1.8 Stakes | Done | Death drops by difficulty (Easy part of the money, Medium money and items, Hard everything but tools, Ironman the impossible run). People die for good on every setting with stakes; a storyteller arrival stream (by day, into a free house, likelier the more people the colony is short) brings new crew and fills roles left by the dead. The station AI warns when hurt and its death ends the run. The run chronicle (landing, arrivals, deaths, bosses, the Fracture, falls) is saved with the world, and the end screen shows it. |
| 0.1.9 Terraria basics | Next | |
