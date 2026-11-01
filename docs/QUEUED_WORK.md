# Queued work

Work held back until the user says to start it. Run the two queues together when they do.

## 1. Asset generation: PAUSED 2026-10-22 (animations 169/398 done; textures and environment art finished)
Started in GPU-sharing mode: 4 GB VRAM reserved (raised from 2 GB at the user's request), below-normal priority, `--pause 10`. Log: `art/generated/overnight.log`.
- Command: `python tools/comfy/overnight.py`. It runs block textures, then environment art (sky, weather, backgrounds, flora), then character animations. It skips work already done.
- Setup is in `docs/COMFYUI.md`. Start the server with `tools/comfy/start_comfy.ps1`.
- Lists refresh from the game first (`--tile-sprites`, `--entity-sprites`, `--flora-sprites`). `anim_entities.tsv` has 414 actors (101 critters, 255 enemies, 28 bosses, 21 NPCs).
- The last texture run stopped on sheet 6 of 6 with a dropped connection (`art/generated/textures/run.log`). A re-run picks it up.
- Afterwards, rebuild or publish to copy the new art.

## 2. Progression fixes (from `docs/PROGRESSION_DIFF.md`): DONE 2026-10-21
All of G0–G16 are done; see the status table at the end of PROGRESSION_DIFF.md. The table below is kept for reference.
The code locations below were checked on 2026-10-21.

| Gap | Fix | Where |
|---|---|---|
| G0 | Creative characters drop 100% of their credits on death. Add `CharacterDifficulty.Creative => 0f`. | `Systems/GameSimulation.Combat.cs` `DropDeathCredits` (the switch at ~454) |
| G1 | Every third radiated blight heart spawns `CyberSwarmLeader`, a hard-trio boss. Before the Fracture, spawn an easy-tier avenger instead (e.g. the Damaged Mining Drone, or a new kitbashed "Fallout Hive-mind" at ~800 HP). Hearts also give `ViridiumOre` early. | `Systems/GameSimulation.Blight.cs:236` (loot) and `:251` (spawn) |
| G2a | Tainted tissue (Abomination), Prism catalyst (Prism Empress) and Royal nectar (Bloom Matriarch) are not gated. Refuse them before the Fracture with a post-Fracture boss set in `HardmodeSummonRefusal`. | `Systems/GameSimulation.Hardmode.cs:109`; items in `Systems/Content/ContentItems.cs:498/506/507` |
| G2b | Breaking a crystal altar spawns the Prism Empress, and a bloom hive or brood pod spawns the Bloom Matriarch, at any time. Use the same gate: before the Fracture, give the shards but no boss, with a message. | `Systems/GameSimulation.Items.cs:715-731`; `Systems/GameSimulation.WorldGenContent.cs:84-93` |
| G2c | Dusk incursions pick from all event bosses (Solar Flare Entity, Void Stalker, Asteroid Miner Mech, Cyber Pirate Captain; 1200–1500 HP). Before the Fracture, limit the pool to bosses under ~1000 HP, or gate it on the Scout Mech. | `Systems/GameSimulation.cs` `TrySpawnEventBoss` (~2382); list in `Entities/Actors.cs:507` |
| G4 | The altar test is `ToolPower >= 75` (`CanSmashAltars`), so the Viridium drill (power 85, tier 4) works without the Core breaker. Change it to the Core breaker, or tier-5+ drills crafted after it. Raise lumenite ore to min tool 85 and voidsteel to 100. Decide whether viridium stays available before the Fracture. | `Systems/GameSimulation.Hardmode.cs:68`; `Systems/Content/ContentItems.cs:340-346`; `World/ContentTiles.cs:173-174` |
| G5 | The Astral sigil has no gate. Refuse it unless `obelisks.down` is set, and update the test at `Systems/ContentSelfTests.cs:483` to set that flag. | `Systems/GameSimulation.Hardmode.cs` `HardmodeSummonRefusal` |

Decision noted for G2a: the Bloom Matriarch's loot is tier 4, so gating it behind the Fracture fits Rimtera's data. Terraria's analogue, Queen Bee, is pre-hardmode, so retiering it to Easy with tier-3 loot would be the other option.

All of G0–G16 are done, G3 and G6–G16 included: see `docs/PROGRESSION_DIFF.md`, "Status: all gaps closed (2026-10-21)".
This table is kept as history. One correction (PC-21, 0.2.2): G2c's Scout Mech gate still let every incursion boss in
before the Fracture; PC-4 gates the incursion roll on the Fracture.
