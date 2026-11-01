# Breakdown 07: player, UI and persistence vs Terraria

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open, with the milestone `docs/PLAN.md` now plans it for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

Scope: player movement physics, the HUD, every screen and menu, pause and autopause, and saves. Key and mouse bindings are out of scope; see `docs/CONTROLS_PARITY.md`, which another agent is implementing now.

Method:
- Terraria evidence is v1.4.5.8 CIL. Methods not already in `docs/terraria-evidence/full/cil/` were dumped with a private copy of `scripts/dumpre.py` into the scratchpad (`Player::HorizontalMovement`, `JumpMovement`, `WallslideMovement`, `CheckDrowning`, `UpdateJumpHeight`, `KillMe`, `GetRespawnTime`, `get_breathCDMax`, `Main::DoUpdate`, `Main::DoUpdate_AutoSave`, `Main::CanPauseGame`). Citations read `Type::Method @IL-offset`. "(knowledge)" means general Terraria knowledge that the dumps did not settle.
- Rimtera behaviour comes from the working tree on 2026-10-28, which has uncommitted edits, so re-grep before editing. Movement numbers were measured with a scratch probe (below).
- Unit conversion: 1 Terraria tile = 16 px and the game runs at 60 ticks/s. So 1 px/tick = 3.75 tiles/s, and 1 px/tick² = 225 tiles/s².

Probe (private build `build-pu7`, `Systems/Pu7Probe.cs`, called from `Program.cs`; a fresh 96×56 world with a flat rock floor; default "Adventurer" colonist):

```
size=<1.25, 2.625> hp=100 moveMul=0.873
run top=4.37 tiles/s, stop ticks=7
jump hold=1t apex=1.80   hold=6t 2.56   hold=12t 3.47   hold=30t 3.77 tiles (airtime 69 ticks)
terminal fall=18.00 tiles/s
fall 24 tiles: 0 dmg   27: 23.9   30: 44.9   35: 79.6   45: dead (100 HP)
breath max=12s
```

Probe note for other agents: a `[ModuleInitializer]` probe that builds a `GameSimulation` deadlocks, because worldgen threads wait on the module-init lock. Call the probe from `Main` instead.

Status key: ✅ matches | ◐ partial | ❌ missing | ⚠ broken or bug | ➕ Rimtera-only (deliberate)

---

## Summary

**Counts (77 features):** ✅ 33 · ◐ 27 · ❌ 5 · ⚠ 6 · ➕ 6

**Top 10 issues by player impact**

1. **The player runs at 39% of Terraria's speed.** Measured top speed is 4.37 tiles/s against Terraria's 11.25. The body is the same size (1.25×2.625 tiles) and the world sizes are the same. Crossing a Large world on foot takes about 32 min instead of about 12.5. (M1, PU-1)
2. **The jump reaches 57% of Terraria's height, and falls are half as fast.** Apex is 3.77 tiles against about 6.6. Gravity is 24 tiles/s² against 90, and terminal speed is 18 tiles/s against 37.5. Movement feels floaty and short. (M3/M4, PU-1)
3. **⚠ A saved world is locked to the character that saved it.** If you pick character B and then a world saved by A, the game silently launches as A (`MenuSystem.ResolveCharacter`, `UI/MenuSystem.cs:1305-1326`). Terraria lets any character play any world. (C7, PU-9)
4. **⚠ Closing the window with Autosave off throws away the session.** `OnFormClosing` saves only when `AutoSave` is on (`UI/GraphicalSandbox.cs:1448-1455`). There are also no `.bak` backups of the world, character, map or profile index. (P4, P6, PU-11)
5. **⚠ Creative characters can enter normal worlds.** The character file carries inventory between worlds, so items duplicated in Creative leak into Classic and Brutal worlds. Terraria keeps Journey characters and worlds apart. (C6, PU-10)
6. **❌ There are no coin or ammo slots.** Ammo and credits sit in the 50-slot grid. (I2, PU-18)
7. **❌ There is no misc-equipment page (pet, light pet, mount, hook), and dyes and vanity are partial.** Dyes are consumed into hidden slots, and there are no vanity accessory slots and no hide toggles. (I4-I6, PU-19)
8. **◐ The shop is a text list.** You can only sell the held item, and there is no buyback. The housing panel cannot assign town NPCs to rooms and lists colonists rather than residents. (I13, I19, PU-21, PU-22)
9. **⚠ World creation has no Hazard row.** `_worldHazard` is always `Random` (`UI/MenuSystem.cs:326`), although `docs/UI_ARCHITECTURE.md` says the option is there. The overlay map also ignores the "Map enabled" setting. (C5, H5, PU-14, PU-15)
10. **◐ Survival tuning differs.** Breath lasts 12 s against 23.3 s. Respawn is 5 s against 10 s, and 20 s with any boss alive, which Terraria applies only in multiplayer. Autosave runs every 90 s on the UI thread against every 10 min (a hitch risk on Large worlds). (M6, M16, P3)

Nothing in this area **blocks play**. Items 3 and 4 can lose progress or a character's work, and items 1 and 2 shape every minute of play. Fix those first.

---

## Feature tables

### M. Player movement physics

| # | Feature | Terraria | Rimtera (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|
| M1 | Run top speed and acceleration | `maxRunSpeed` 3 px/tick = **11.25 tiles/s**, `runAcceleration` 0.08 (≈0.63 s to top), `runSlowdown` 0.2 (≈0.25 s to stop). Hermes-type boots: `accRunSpeed` 6 = 22.5 tiles/s (`Player..ctor @1246`, `player_stats.md`, `HorizontalMovement`) | `MaximumRunSpeed` 5, accel 24, friction 40 (`Entities/PawnMovement.cs:83-86`; player settings `Systems/GameSimulation.cs:55`). The colonist `MoveSpeedMultiplier` also scales it (`GameSimulation.cs:675`). Probe: **4.37 tiles/s** (×0.873), stops in 7 ticks | ◐ | ✅ PU-1 0.1.14 |
| M2 | Brakes faster than it accelerates; reversing brakes first | slowdown > acceleration | Same shape (`PawnMovement.cs:240-254`, parity A1) | ✅ | ✅ as audited |
| M3 | Jump height and variable hold | `jumpHeight` 15 ticks × `jumpSpeed` 5.01 px/tick with gravity 0.4 gives an apex of **≈6.6 tiles** (derived). Early release cuts it short (`Player.cctor @190-222`, `UpdateJumpHeight`, `JumpMovement`) | Impulse 9.5 tiles/s, sustain 0.2 s, gravity 24 (`PawnMovement.cs:90,102,256-282`). Probe: apex **3.77** held, 1.80 on a tap | ◐ | ✅ PU-1 0.1.14 |
| M4 | Gravity and terminal fall | gravity 0.4 px/tick² = **90 tiles/s²**, `maxFallSpeed` 10 = **37.5 tiles/s** (`Update @21-72`) | 24 tiles/s², 18 tiles/s (`PawnMovement.cs:88-89`). Probe: 18.0 | ◐ | ✅ PU-1 0.1.14 |
| M5 | Fall damage | `(fallTiles − 25 − extraFall) × 10`, counted from the `fallStart` tile. Clouds and bouncy platforms, `noFallDmg` and wings negate it. A mount **multiplies** it by `Mount.FallDamage` (`Update @6348-7066`) | `10 + (fall − 25) × 7` (`Systems/GameSimulation.Player.cs:389-400`). A mount, featherfall, flight, rocket boots, liquid or a latched hook **negates** it (`:407-409`). Probe: 27 tiles → 24 (T: 20), 30 → 45 (T: 50), 35 → 80 (T: 100) | ◐ | ✅ PU-2 0.1.12 |
| M6 | Breath and drowning | `breathMax` 200, −1 per `breathCDMax` 7 ticks gives **23.3 s**. Diving helmet ×6, breathing reed ×2 while not using an item. At 0 breath: −2 HP per 7 ticks ≈ 17 HP/s. Refills +3/tick (≈1 s) (`CheckDrowning`, `get_breathCDMax`) | 12 s, rebreather ×3, gills infinite. Drowning −6 HP per 0.5 s = 12 HP/s; refills ×4 (3 s) (`GameSimulation.Player.cs:76-77,411-440`) | ◐ | ✅ PU-3 0.1.12 |
| M7 | Swimming and liquid physics | No swimming without flippers: repeated water hops. Liquids cut gravity and max fall (water ≈0.2 and 5; honey 0.1 and 3) and change jump values (`Update @338-590`, (knowledge) for the flipper rule) | Everyone swims: held jump strokes up at 4.6 tiles/s in any liquid. Water ×0.6 speed, sink cap 2.2 tiles/s (`GameSimulation.Player.cs:255-299,378-383`) | ◐ | ✅ PU-7 0.1.14 |
| M8 | Liquid exit boost | Breaching the surface with jump held clears a 1-tile lip | `LiquidExitBoost` (`:81,376-377`, parity A8) | ✅ | ✅ as audited |
| M9 | Wall slide and wall jump | Gear only (climbing claws or shoe spikes) (`WallslideMovement`) | Needs the `climb` tag: cling at 1.6 tiles/s, wall jump (`:306-313,366-371`) | ✅ | ✅ as audited |
| M10 | Coyote time 0.08 s, jump buffer 0.1 s | None | `LateJumpSeconds`, `JumpBufferSeconds` (`PawnMovement.cs:105,117`) | ➕ | ➕ kept |
| M11 | Dash without gear (key or double tap) | Gear only (`DoCommonDashHandle`) | Every player can dash, with a 0.45 s cooldown (`PawnMovement.cs:169-193`) | ➕ | ➕ kept |
| M12 | Double jump, rocket, wings and refill on landing | Per-gear values | Tags `double-jump`, `flight`, `rocket-boots`, `glide` (`GameSimulation.Player.cs:315-338`, parity A9). The gear values belong to the items area | ✅ | ✅ as audited |
| M13 | Mount physics | Each mount has its own run, jump, fly time and fall-damage factor (`UpdateJumpHeight @272-389` calls `Mount.JumpHeight` and `JumpSpeed`) | `MountSpeedFactor`. Fall is always negated; a mounted fall caps at 9 tiles/s (`:252-253,384-386`) | ◐ | ✅ PU-6 0.1.14 |
| M14 | Low gravity in space (top of the world) | `gravity *= clamp((y/16 − margin) / worldSurface·k, 0.25, 1)` near the world top, a little different in the remix seed (`Update @2420-2540`) | None. Gravity is the same at every height | ❌ | ❌ PU-4 → 0.5.0 |
| M15 | Surface effects (ice, mud, webs, slopes, half blocks, platforms, ropes) | Yes | Parity A2, A5-A7 (`TilePhysics.SurfaceGrip`, `PawnMovement.cs:224-228`) | ✅ | ✅ as audited |
| M16 | Respawn timer | 600 ticks = **10 s**, ×1.5 in Expert or above. A living boss adds 10 s **only in multiplayer** (`GetRespawnTime @0-127`) | 5 s (Explorer 3 s), 20 s while any boss lives, single-player included (`GameSimulation.Player.cs:95-96,1021-1022`) | ◐ | ✅ PU-5 0.1.11 |
| M17 | Knockback and immunity flashing | Yes | Parity A10 (`Actors.cs:130` `HitImmunitySeconds` 0.67) | ✅ | ✅ as audited |
| M18 | Water walking applied twice per tick | — | `GearWaterWalk` is called twice in a row (`GameSimulation.Player.cs:301-302`). It is harmless today only because the second call returns early when `velocity.Y < 0` | ⚠ | ✅ PU-8 0.1.7 |

### H. HUD

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| H1 | Life and mana display | Hearts and stars (bar styles are an option) | Riveted panel: LIFE, CHARGE, FOOD and REST bars; low life pulses (`UI/GraphicalSandbox.cs:2644-2659`) | ✅ | ✅ as audited |
| H2 | Hotbar with the selected item's name | 10 slots, name above | `DrawHud` (`:2597-2613`) | ✅ | ✅ as audited |
| H3 | Buff bar | Icons with time. Hover shows name and description. **Right-click cancels** a removable buff | Icons, time and the name on hover. No description, no cancel (`UI/GraphicalSandbox.World.cs:458-490`) | ◐ | ✅ PU-16 0.1.9 |
| H4 | Breath meter | Bubbles while submerged | 10 bubbles over the head (`GraphicalSandbox.World.cs:492-512`) | ✅ | ✅ as audited |
| H5 | Minimap, overlay or off | `mapStyle` cycled with Tab and **saved** in config. "Map enabled" hides both minimap and overlay | Cycle works (`GraphicalSandbox.cs:439-440`), with zoom and opacity keys (`:928-929`). `MapView` is not saved and resets to Minimap each session. **The overlay ignores `MinimapEnabled`** (`:2381-2384`) | ⚠ | ❌ PU-15 not scheduled |
| H6 | Info accessory readouts | 12 info items (time, weather, moon, fishing power, compass, depth, ore, lifeform, radar, kill tally, DPS, speed), each with an on/off eye toggle, and readable from the inventory (`RefreshInfoAccsFromItemType`) | 4 readouts: compass, depth, ore, life (`Systems/GameSimulation.InfoGear.cs:36-62`), shown in the status panel (`GraphicalSandbox.cs:3320`). The clock is always on the HUD. No toggles | ◐ | ❌ PU-17 → 0.4.0 |
| H7 | Boss and event bars | Yes | `DrawBossBar`, `DrawEventBars` (`:3171,3187`) | ✅ | ✅ as audited |
| H8 | Item text and damage numbers (toggles) | Yes | General settings (`MenuSystem.cs:1211-1212`) | ✅ | ✅ as audited |
| H9 | Hide the whole UI (F11) | Yes | Only key help on `/`. Tracked in `CONTROLS_PARITY.md` §1 | ◐ | ◐ unchanged since the audit |
| H10 | Death screen and countdown | Yes | `HandlePlayerDeath` → countdown (`GraphicalSandbox.cs:1080-1099`) | ✅ | ✅ as audited |
| H11 | Player stats display | Defense shield in the inventory; tooltips | HUD line (DEF, mood, credits, crew, drones) plus the full colonist sheet (`UI/ColonistSheetState.cs`) | ➕ | ➕ kept |

### C. Title and creation screens

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| C1 | Title menu | Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, Exit | Single Player, Settings, Exit Game (`MenuSystem.cs:734-743`) | ◐ | ❌ PU-24 not scheduled |
| C2 | Character list: favourite, rename, delete | Yes | Cards with face, backstory and difficulty; Favorite, Rename and Delete; delete moves the file to `deleted/` (`MenuSystem.cs:768-816`, `CharacterFile.cs:95-104`) | ✅ | ✅ as audited |
| C3 | Character difficulty | Journey, Classic, Mediumcore, Hardcore | Creative, Classic, Salvager, Ironman, plus Explorer (`Systems/WorldSettings.cs:33-43`); credit share per difficulty (`GameSimulation.Combat.cs:570-576`) | ✅ | ✅ as audited |
| C4 | Appearance | 160+ hair styles, free HSV colour for hair, eyes, skin, shirt, undershirt, pants and shoes, hex copy and paste, 10 clothing styles (knowledge) | Presets: 6 skin, 8 hair, 8 hair colours, 6 eyes, 6 outfit colours (`Characters/ColonistSystem.cs:77-81`, `MenuSystem.cs:884-939`). Adds backstory, traits and skills | ◐ | ❌ PU-25 → 0.5.0 |
| C5 | World creation | Name, seed (with text), size, difficulty, evil | Name and random name, size (+Tiny), difficulty, Blight, seed (text hashed with FNV-1a) (`MenuSystem.cs:1089-1145`). **No Hazard row**: `_worldHazard` never changes from Random (`:326,1139`), although `UI_ARCHITECTURE.md:40` says it does | ⚠ | ✅ PU-14 0.1.13 |
| C6 | Journey-only pairing | A Journey character plays only Journey worlds, and the reverse (knowledge) | A Creative character can enter any world (`Systems/Creative.cs:16`). The character file carries items across worlds (`CharacterFile.cs:7-36`), so duplicated items reach normal worlds | ⚠ | ✅ PU-10 0.1.13 |
| C7 | Any character on any world | Yes | `ResolveCharacter` replaces the chosen character with the save's character whenever that character still exists (`MenuSystem.cs:1305-1326`) | ⚠ | ✅ PU-9 0.1.7 |

### S. Settings

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| S1 | General | Autosave, autopause, map, placement preview, … | Autosave, autopause, map enabled, placement preview, item text, damage text (`MenuSystem.cs:1206-1213`) | ✅ | ✅ as audited |
| S2 | Video | Resolution, window mode, frame skip, lighting, quality, background, parallax, particles, … | Window mode, resolution, frame skip, lighting (`:1220-1238`). `BackgroundEnabled`, `BackgroundParallax`, `Particles`, `WeatherEffects`, `FlashyEffects` and `GraphicsQuality` exist in `GamePreferences` (`UI/PreferencesAndProfiles.cs:65-104`) but have no menu rows | ◐ | ❌ PU-26 not scheduled |
| S3 | Volume | Music, sound, ambient | Same (`:1239-1243`) | ✅ | ✅ as audited |
| S4 | Interface | Zoom, UI scale, health bars, language, … | Zoom, UI scale, health bars, guide hints (`:1214-1219`). No language choice | ◐ | ❌ PU-26 not scheduled |
| S5 | Cursor | Colour, border, size, smart cursor mode | Same (`:1244-1252`) | ✅ | ✅ as audited |
| S6 | Controls | Rebindable list | See `CONTROLS_PARITY.md` (in progress by another agent) | ✅ | ✅ as audited |

### I. Inventory hub and in-game screens

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| I1 | Main grid | 50 slots | 50 (`GameSimulation.Player.cs:89`) | ✅ | ✅ as audited |
| I2 | Coin and ammo slots | 4 coin + 4 ammo; ammo is drawn from the ammo slots first | None | ❌ | ✅ PU-18 0.1.10 |
| I3 | Armour and accessory slots | 3 armour + 5 accessories (+1 from the Demon Heart, +1 in Master) | 3 + 5, a 6th from the aux socket or Annihilation (`UI/InventoryUi.cs:556-581`) | ✅ | ✅ as audited |
| I4 | Vanity | Vanity armour, a vanity accessory beside each accessory, hide toggles | Vanity head, body and legs only (`InventoryUi.cs:582-584`) | ◐ | ◐ PU-19 partly (0.1.9 pet, light pet, mount and hook slots) |
| I5 | Dyes | One dye slot per armour or accessory slot; the dye item stays and can be taken back | Using a dye consumes it into the next hidden `DyeSlot`; solvent clears all (`GameSimulation.PlayerSystems.cs:495-522`). No dye slots in the UI | ◐ | ◐ PU-19 partly (0.1.9 pet, light pet, mount and hook slots) |
| I6 | Misc equipment page | Pet, light pet, minecart, mount and hook slots, with their dyes | Pets and mounts are summoned by using the item; no slots | ❌ | ◐ PU-19 partly (0.1.9 pet, light pet, mount and hook slots) |
| I7 | Loadouts | 3 | 3 (`InventoryUi.cs:516-527`) | ✅ | ✅ as audited |
| I8 | Crafting panel and guide | Yes | Crafting list, the "close to" toggle, recipe guide, station AI (`docs/UI_ARCHITECTURE.md` §Crafting) | ✅ | ✅ as audited |
| I9 | Chest actions | Loot all, deposit all, quick stack, restock, sort, rename | All six (`InventoryUi.cs:391-404`) | ✅ | ✅ as audited |
| I10 | Personal banks | Piggy bank, safe, defender's forge, void vault (40 each); void bag auto-pickup | One 40-slot Cargo pod (`GameSimulation.Player.cs:86,178`) | ◐ | ❌ PU-20 → 0.6.0 |
| I11 | Trash slot | Yes | Yes (`InventoryUi.cs:359-387`). Shift-click details in CONTROLS_PARITY | ✅ | ✅ as audited |
| I12 | Sort and quick stack to nearby chests | Yes | Yes (`InventoryUi.cs:346-348`) | ✅ | ✅ as audited |
| I13 | Housing panel | NPC head list, housing query, drag an NPC to a room, room status | "Check this room" plus a list of **colonists** (`InventoryUi.cs:588-611`). Town NPCs aren't listed and can't be assigned (`GameSimulation.Town.cs:52-75` assigns them automatically) | ◐ | ◐ PU-22 partly (0.1.9 housing page lists residents) |
| I14 | Emotes | Emote menu from an inventory button (~140), `/e` | Hold `` ` `` + 1-8: 8 emotes (`GraphicalSandbox.PlayerSystems.cs:127-188`) | ◐ | ✅ PU-27 0.2.0 |
| I15 | Bestiary | Filters, sort, search, stats panel, drop rates, tiered unlocks | Card list: sighted or kills, biomes, up to 4 seen drops; percent complete (`GraphicalSandbox.Screens.cs:87-112`) | ◐ | ❌ PU-23 → 0.4.0 |
| I16 | Achievements | 100+, global, reachable from the title and in game | 44 "Feats" per character (`Systems/Achievements.cs`), in game only (`GraphicalSandbox.Screens.cs:114-130`) | ◐ | ❌ PU-24 not scheduled |
| I17 | Fullscreen map | Pan, zoom, every town NPC head, bosses, pylons, spawn, death, other players | Pan, zoom, pylon travel, spawn, death, colonists, Station AI, bosses, player (`GraphicalSandbox.cs:3066-3170`). Other town NPC heads missing | ◐ | ❌ PU-15 not scheduled |
| I18 | NPC dialogue | Chat, Shop, special services, happiness | Talk, shop, sell, Medic, party, quest fish, happiness and price line (`GraphicalSandbox.cs:1289-1329`) | ✅ | ✅ as audited |
| I19 | Shop | Shop grid beside the inventory, click to buy, sell any item by shift-click or drag, buyback of sold items | Text list of offers; sells **only the held item**; no buyback (`GraphicalSandbox.cs:1331-1351`) | ◐ | ◐ PU-21 partly (0.1.9 selling and buyback; no shop grid) |
| I20 | Signs and tombstones | Edit text | `OpenSignScreen` (`GraphicalSandbox.Screens.cs:65-84`) | ✅ | ✅ as audited |
| I21 | Colonist sheet (K): health, mood, skills | — | `UI/ColonistSheetState.cs` | ➕ | ➕ kept |

### P. Pause, saves, multiple characters and worlds, multiplayer

| # | Feature | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| P1 | Pause and autopause rules | Single player: settings, the controls menu and achievements **always** pause. With autopause on: inventory (shops and chests included), sign editing and any in-game UI such as the bestiary (`Main::CanPauseGame @0-120`, `DoUpdate @2429-2460`) | Settings, NPC talk, shop, sign, bestiary and feats **always** pause. Inventory and colonist sheet pause only with autopause; with it off the player can move with the inventory open (`GraphicalSandbox.cs:631,914-926`) | ◐ | ✅ PU-13 0.1.14 |
| P2 | Losing window focus | The world keeps running (knowledge) | Keys released; the world keeps running (`GraphicalSandbox.cs:175-180`) | ✅ | ✅ as audited |
| P3 | Autosave cadence | Every **600,000 ms (10 min)** with autosave on (world + player) (`Main::DoUpdate_AutoSave @83-163`) | Every **90 s** when on; capture and write run **on the UI thread** (`GraphicalSandbox.cs:983-988,1382-1404`). Not measured on Large | ◐ | ✅ PU-12 0.1.13 |
| P4 | Save when leaving | Save & Exit always saves (knowledge) | The pause menu's Save & Exit and Save & Quit always save, on a worker thread (`:1164-1189`). **Closing the window saves only when Autosave is on** (`:1448-1455`) | ⚠ | ✅ PU-11 0.1.7 |
| P5 | Save format and versions | Binary `.wld` / `.plr` with version upgrade paths | JSON envelope plus a raw-deflate cells sidecar with atomic temp-file moves (`UI/WorldSaveRepository.cs:58-92`). Load needs **exact** version equality (`:108-110`), so the next version bump makes old saves unreadable (with a clear error). The sidecar is moved before the envelope, and both use fixed names, so a crash between the two moves pairs new cells with the old envelope | ◐ | ✅ PU-11 0.1.7 |
| P6 | Backups | `.bak` copies of world and player before each save (knowledge; `FileUtilities.Copy`) | None for world, cells, `.rchar`, `.rmap` or the profile index | ❌ | ✅ PU-11 0.1.7 |
| P7 | Character file separate from worlds | Yes | `.rchar` carries inventory, gear, loadouts, bank, upgrades, pets and feats (`Systems/CharacterFile.cs:7-36`) | ✅ | ✅ as audited |
| P8 | Map per character per world | Yes | `maps/<world>_<char>.rmap` (`GraphicalSandbox.cs:1353-1360`) | ✅ | ✅ as audited |
| P9 | Multiple characters and worlds: favourite, rename, delete | Yes | Yes; deleted saves go to `saves/deleted` (`WorldSaveRepository.cs:44-56`, `MenuSystem.cs:746-797`) | ✅ | ✅ as audited |
| P10 | Hardcore / Ironman death | Ghost; the player file is deleted (`KillMe @403-412`, `KillMeForGood`) | Saves the world, moves the character to `deleted/` and returns to the title (`GraphicalSandbox.cs:1088-1094`). No ghost spectating | ✅ | ✅ PU-28 0.2.0 |
| P11 | Mediumcore and softcore drops | Mediumcore drops items; softcore drops half the coins | Salvager stash and credit shares (`GameSimulation.Combat.cs:568-620`) | ✅ | ✅ as audited |
| P12 | Multiplayer | Host and join, dedicated server | None, not even stubs (grep finds nothing) | ❌ | ❌ PU-29 deferred |
| P13 | `--continue` straight into the last world | — | `Program.cs:28-29`, `ContinueLastWorld` | ➕ | ➕ kept |
| P14 | Deleted saves are recoverable | Windows recycle bin (knowledge) | `deleted/` folders with timestamps | ➕ | ➕ kept |

---

## Integration steps

Size: S < half a day, M 1-3 days, L more than 3 days. Each acceptance names a self-test (`--self-test`, in `Systems/*SelfTests.cs`) or a scripted probe or play-test.

**PU-1 Retune player movement to Terraria-like feel** (M1, M3, M4). Fix first.
- **Change:**
  - Give the player its own `PawnMovementSettings` in `GameSimulation.cs:55`; colonists and enemies keep theirs.
  - Targets: top run about 11 tiles/s; ground acceleration about 18 tiles/s² (≈0.6 s to top); friction about 45 tiles/s²; gravity about 90 tiles/s²; terminal about 37 tiles/s.
  - Jump: impulse about 18.8 tiles/s held for about 0.25 s, for a ≈6.5-tile apex. A tap gives 1.5-2.5 tiles.
  - Rescale the hard-coded mobility values in `GameSimulation.Player.cs`: stroke −4.6, double jump −8.6, wall jump, rocket −6.5, glide caps 2.2/1.6, hook release −7.5, mount cap 9, `LiquidExitBoost`.
  - Decide whether the colonist `MoveSpeedMultiplier` (0.873 in the probe) should slow the player character at all (open question 2).
  - Rimtera picks its own final numbers within ±10% of the targets. Treat Terraria's figures as behavioural targets only.
- **Files:** `Systems/GameSimulation.cs`, `Systems/GameSimulation.Player.cs`, `Systems/GameSimulation.Gear.cs`, `Entities/PawnMovement.cs` (defaults stay for AI), `Systems/ParityMovementTests.cs`, the camera follow in `UI/GraphicalSandbox.cs`.
- **Size:** L.
- **Dependencies:** combat (enemy chase speeds, projectile leads, knockback in tiles/s); items and gear (wing, rocket and boots values); mounts (relative speeds); worldgen (gaps and jumps designed around a 3.8-tile jump).
- **Acceptance:** a new self-test `PU1 player feel` on a flat 96×56 arena checks:
  - top speed 10-12 tiles/s;
  - 90% of top speed in 0.5-0.7 s;
  - stop in 0.2-0.3 s;
  - held apex 6-7 tiles and tap apex 1.5-2.5;
  - terminal 35-40 tiles/s.

  All `A*` parity checks still pass. Play-test: crossing a Small world's surface takes 6-8 min.

**PU-2 Fall damage curve** (M5).
- **Change:** damage = (tiles − 25) × 10, counted from the highest point. Wings negate it. A mount multiplies by a per-mount factor (0 for flying mounts) instead of negating. Keep featherfall, liquid and the latched hook.
- **Files:** `GameSimulation.Player.cs:389-409`, the mount specs.
- **Size:** S.
- **Dependencies:** PU-1 (faster falls mean longer distances), mounts.
- **Acceptance:** probe: 25 tiles → 0, 26 → 10, 30 → 50, 35 → 100; a ground mount at 30 tiles takes the factor × 50.

**PU-3 Breath and drowning** (M6).
- **Change:**
  - base air about 23 s;
  - drowning about 17 HP/s;
  - refill about 1 s;
  - rebreather ×6 (diving-helmet analogue), a reed-type item ×2.
- **Files:** `GameSimulation.Player.cs:76-77,411-440`, the item tags.
- **Size:** S.
- **Dependencies:** items (the rebreather multiplier).
- **Acceptance:** self-test: a submerged head reaches 0 breath at 23 ± 1 s; 100 HP lasts 5-7 s more; breath is full within 1.5 s after surfacing.

**PU-4 Space gravity** (M14).
- **Change:** player gravity multiplier = clamp((y − topMargin) / (surfaceY × k), 0.25, 1) in the top band of the world. Show it as a "Low gravity" buff icon.
- **Files:** `Entities/PawnMovement.cs` (a gravity-scale input in `MovementInput`), `GameSimulation.cs:675`, `GameSimulation.Player.cs:ActiveBuffs`.
- **Size:** M.
- **Dependencies:** world area (sky islands and space layer height), PU-1.
- **Acceptance:** probe at the top band: apex ≥ 2× the surface apex; at the surface the multiplier is 1.0.

**PU-5 Respawn timer** (M16).
- **Change:** 10 s base, ×1.5 on Brutal and Annihilation, Explorer 3 s. No boss extension in single player; bosses already leave on death (`BossesLeaveOnPlayerDeath`).
- **Files:** `GameSimulation.Player.cs:95-96,1021-1032`.
- **Size:** S.
- **Dependencies:** bosses area (despawn on death).
- **Acceptance:** self-test: Classic gives 10 s, Brutal 15 s, and 10 s with a living boss.

**PU-6 Mount physics per mount** (M13).
- **Change:** a per-mount fall factor, jump height and speed, and fall cap in the mount specs. Replace the global negate and the 9 tiles/s clamp.
- **Files:** `GameSimulation.Player.cs:252-253,384-386`, the mount specs (`GameSimulation.Travel.cs`).
- **Size:** M.
- **Dependencies:** PU-1, PU-2, the travel and mounts owner.
- **Acceptance:** self-test: each mount's apex and top speed match its spec ±10%; the hover bike takes no fall damage; a ground mount takes reduced fall damage.

**PU-7 Swimming rule** (M7).
- **Change:** if the user wants Terraria's rule, free swimming needs a "swim" tag item (flippers analogue). Without it, jump is a hop that works only after a short cooldown. Otherwise mark M7 ➕.
- **Files:** `GameSimulation.Player.cs:293-299`.
- **Size:** S.
- **Dependencies:** items (a flipper item), open question 5.
- **Acceptance:** self-test: without gear, holding jump in deep water does not climb faster than 2 tiles/s; with gear the current stroke applies.

**PU-8 Remove the duplicate `GearWaterWalk` call** (M18).
- **Files:** `GameSimulation.Player.cs:301-302`.
- **Size:** S.
- **Acceptance:** water-walking self-tests still pass.

**PU-9 Decouple characters from worlds** (C7). Can lose work: fix early.
- **Change:**
  - `Launch` uses the selected character. The saved-character lookup moves to `--continue` only.
  - World snapshots store player position and state per character id (a `PlayerPositions` map), so a new character spawns at the landing site.
  - The character file stays the authority for the inventory.
- **Files:** `UI/MenuSystem.cs:1294-1326`, `UI/GraphicalSandbox.cs:1009-1073,1423-1446`, `Systems/GameSaveData.cs`, `GameSimulation.Restore`.
- **Size:** M.
- **Dependencies:** PU-11 (format bump).
- **Acceptance:** self-test: create A, play world W and save; create B and launch W. The player is B with B's inventory, at the spawn point. A's `.rchar` is unchanged; relaunching with A restores A's position.

**PU-10 Creative isolation** (C6).
- **Change:** worlds record `CreatedByCreative`. The menu greys out non-Creative worlds for a Creative character and the reverse, with an explanation, matching Terraria's split.
- **Files:** `UI/PreferencesAndProfiles.cs` (`WorldProfile`), `UI/MenuSystem.cs:1066-1080`.
- **Size:** S.
- **Dependencies:** progression (Creative powers).
- **Acceptance:** menu self-test: a Creative character sees Classic worlds disabled, and the reverse.

**PU-11 Save safety: always save on close, backups, versioned sidecar** (P4, P5, P6). Can lose work: fix early.
- **Change:**
  - `OnFormClosing` always saves an active world, using the same path as Save & Quit; or it asks first, if the user prefers.
  - Before each save, rotate `X` → `X.bak` for the world JSON, cells, `.rchar`, `.rmap` and the profile index.
  - Name the sidecar by save stamp (`<id>.<stamp>.cells`), write it first, then the envelope, then delete the old sidecar.
  - On load failure, offer "Load backup".
  - Accept `FormatVersion <= Current` through migration hooks, and refuse newer versions.
- **Files:** `UI/WorldSaveRepository.cs`, `Systems/CharacterFile.cs`, `UI/PreferencesAndProfiles.cs`, `UI/WorldMap.cs`, `UI/GraphicalSandbox.cs:1448-1455`.
- **Size:** M.
- **Dependencies:** none.
- **Acceptance:** self-tests:
  - a corrupt main JSON loads from `.bak`;
  - a simulated crash after the sidecar write but before the envelope still loads a consistent pair;
  - with Autosave off, closing the form writes a save;
  - a version-(N−1) snapshot fixture loads.

**PU-12 Autosave cadence and threading** (P3).
- **Change:** capture the snapshot on the game thread and serialise and write on a worker, as `SaveWithScreen` does. Set the interval as a preference (default 5 min, or keep 90 s if the user prefers). Skip a tick if a save is still running.
- **Files:** `UI/GraphicalSandbox.cs:983-988,1382-1404`.
- **Size:** M.
- **Dependencies:** PU-11.
- **Acceptance:** an `--engine-bench`-style measurement on a Large world: autosave adds at most one frame (≤ 17 ms) of UI-thread time; a self-test checks that the interval preference saves and loads.

**PU-13 Pause rules like Terraria** (P1).
- **Change:** with autopause off, NPC talk, the shop, signs and the bestiary keep the world running, as the inventory does. Settings, controls and feats always pause.
- **Files:** `UI/GraphicalSandbox.cs:631,914-926`.
- **Size:** S.
- **Dependencies:** NPC area (a hostile can hit the player while shopping).
- **Acceptance:** self-test or probe: with autopause off, `TickNumber` advances while `npc-shop` is open and stays still while `pause-menu` is open; with autopause on, both stay still.

**PU-14 Hazard row in world creation** (C5).
- **Change:** add a `Hazard: …` row that cycles with ← → and shows `WorldSettings.Describe(hazard)`. Fix the doc line if the row is dropped instead.
- **Files:** `UI/MenuSystem.cs:1089-1145`.
- **Size:** S.
- **Dependencies:** world area.
- **Acceptance:** menu self-test: cycling the row changes `WorldHazardDraft`, and the created `WorldProfile.Hazard` matches it.

**PU-15 Map fixes** (H5, I17).
- **Change:**
  - the overlay obeys `MinimapEnabled`;
  - save `MapView`, minimap scale and both opacities in `GamePreferences`;
  - draw every housed town NPC's head on the full map and the minimap.
- **Files:** `UI/GraphicalSandbox.cs:2381-2384,2726-2745,3066-3170`, `UI/PreferencesAndProfiles.cs`.
- **Size:** S.
- **Dependencies:** NPC area (head sprites), art.
- **Acceptance:** self-test: with the map disabled and overlay style, nothing draws; style and opacity survive a restart; the full map shows N heads for N housed NPCs.

**PU-16 Buff bar interactions** (H3).
- **Change:** the hover tooltip gets a description; right-click cancels a non-debuff (not potion sickness).
- **Files:** `UI/GraphicalSandbox.World.cs:458-490`, `GameSimulation.Player.cs:ActiveBuffs`, the click routing in `GraphicalSandbox.cs`.
- **Size:** S.
- **Dependencies:** items (buff descriptions).
- **Acceptance:** self-test: cancelling a mount buff dismounts; a debuff cannot be cancelled.

**PU-17 Info gear parity** (H6).
- **Change:** add readouts for weather, moon phase, fishing power, speed (tiles/s), DPS, kill tally of the last enemy and rare-creature radar, each with a HUD eye toggle saved in preferences. Keep the always-on clock as ➕.
- **Files:** `Systems/GameSimulation.InfoGear.cs`, `UI/GraphicalSandbox.cs:3280-3330`.
- **Size:** M.
- **Dependencies:** items (new info items or tags), fishing (fishing power).
- **Acceptance:** self-test: each tag adds its line; a toggled-off line hides while the item stays worn.

**PU-18 Coin and ammo slots** (I2).
- **Change:** 4 coin and 4 ammo slots outside the 50-slot grid. Ammo is picked from the ammo slots first; credits fill coin slots on pickup. Bump the character file version and migrate.
- **Files:** `Systems/ItemSystem.cs` (`Inventory`), `UI/InventoryUi.cs`, `Systems/CharacterFile.cs`, ammo picking in combat.
- **Size:** L.
- **Dependencies:** items and combat (ammo selection), PU-11 (migration).
- **Acceptance:** self-test: with ammo in slot A1 and the grid, firing uses A1 first; coins picked up land in coin slots; old `.rchar` files load.

**PU-19 Equipment page: dyes, vanity accessories, misc slots, visibility** (I4-I6).
- **Change:**
  - a dye slot per armour and accessory slot, holding the dye item (it is not consumed);
  - a vanity slot beside each accessory;
  - hide toggles;
  - a misc page with pet, light pet, mount and hook slots (equipping summons; QuickMount and QuickGrapple use them).
- **Files:** `Entities` equipment model, `Systems/GameSimulation.PlayerSystems.cs:495-522`, `UI/InventoryUi.cs:556-585`, `Systems/CharacterFile.cs`, the player rig drawing in `UI/GraphicalSandbox.Gear.cs`.
- **Size:** L.
- **Dependencies:** items and gear, art, PU-11.
- **Acceptance:** self-test: putting a dye in a dye slot recolours the piece, and removing it returns the dye; a hidden accessory keeps its stats but is not drawn; the pet slot summons and despawns the pet.

**PU-20 More personal banks** (I10).
- **Change:** up to 3 more 40-slot banks reached from new station items; a void-bag analogue that auto-deposits on a full grid.
- **Files:** `GameSimulation.Player.cs:86,178`, `UI/GraphicalSandbox.Screens.cs:18-63`, `CharacterFile.cs`.
- **Size:** M.
- **Dependencies:** items and crafting.
- **Acceptance:** self-test: each bank keeps its own contents across worlds; a full grid with the void-bag item moves a picked-up stack into it.

**PU-21 Shop grid** (I19).
- **Change:** a shop panel in `InventoryUiState` (the slot grid with prices) beside the player grid. Click buys, right-click-hold buys repeatedly, Shift-click on any grid item sells it, and a buyback row holds the last sold stacks.
- **Files:** `UI/InventoryUi.cs`, `UI/GraphicalSandbox.cs:1289-1351`, `Systems/GameSimulation.Town.cs` (`Buy`, `Sell`).
- **Size:** M.
- **Dependencies:** NPC area (prices, happiness), PU-13.
- **Acceptance:** self-test: selling slot 7 adds credits and puts the stack in buyback; buying it back costs the sell price.

**PU-22 Housing panel with town NPCs** (I13).
- **Change:** list town NPCs with their head, home status and happiness; a "query room" mode; click an NPC, then a room, to assign them. Show colonists under their own heading.
- **Files:** `UI/InventoryUi.cs:588-611`, `Systems/GameSimulation.Town.cs` (a public `AssignHome(npc, cell)`).
- **Size:** M.
- **Dependencies:** NPC area.
- **Acceptance:** self-test: `AssignHome` to a valid room sets `HomeCell`, and an invalid room is refused with a reason.

**PU-23 Bestiary detail** (I15).
- **Change:** filters (biome, boss or critter), search, sort, and a detail pane with HP, damage, defence and drops with chances once the kill tier unlocks.
- **Files:** `UI/GraphicalSandbox.Screens.cs:87-112`, `Systems/Bestiary.cs`.
- **Size:** M.
- **Dependencies:** enemies (stats), `NpcDropRegistry`.
- **Acceptance:** self-test: filtering by a biome shows only kinds seen there; a tier-3 entry lists its drop chances.

**PU-24 Feats on the title screen and catalogue scope** (C1, I16).
- **Change:** add Achievements and Credits rows to the title screen. Decide whether feats are global or per character (open question 6).
- **Files:** `UI/MenuSystem.cs:734-743`, `Systems/Achievements.cs`.
- **Size:** S.
- **Dependencies:** none.
- **Acceptance:** menu self-test: the title screen lists the new rows, and they open.

**PU-25 Appearance editor** (C4).
- **Change:** an HSV slider per colour part (skin, hair, eyes, shirt, pants, shoes), hex copy and paste, more hair and outfit styles.
- **Files:** `Characters/ColonistSystem.cs:77-81`, `UI/MenuSystem.cs:884-939`, `UI/CharacterLook.cs`, `UI/SpriteAtlas.cs`.
- **Size:** M.
- **Dependencies:** an art run (hair styles); the ComfyUI run is paused until the user's go.
- **Acceptance:** the menu preview recolours live; colours survive save and load as exact RGB.

**PU-26 Video settings rows** (S2, S4).
- **Change:** add rows for background, parallax, particles, weather effects, flashy effects and quality, all of which already exist in `GamePreferences`. Language: only if the user wants localisation.
- **Files:** `UI/MenuSystem.cs:1220-1238`.
- **Size:** S.
- **Dependencies:** presentation area (09).
- **Acceptance:** each row toggles its preference and the renderer honours it.

**PU-27 Emote menu** (I14).
- **Change:** an Emotes button in the inventory hub that opens a palette of emotes (keep `` ` `` + 1-8 as ➕).
- **Files:** `UI/InventoryUi.cs`, `UI/GraphicalSandbox.PlayerSystems.cs:127-188`.
- **Size:** S/M.
- **Dependencies:** art.
- **Acceptance:** clicking an emote shows its bubble over the player.

**PU-28 Ironman spectate (optional)** (P10).
- **Change:** after an Ironman death, keep a ghost camera until Esc, then go to the title.
- **Files:** `UI/GraphicalSandbox.cs:1088-1094`.
- **Size:** S.

**PU-29 Multiplayer** (P12). Document as out of scope unless the user says otherwise; no stubs.

---

## Cross-area dependencies

- **06 Items and combat:**
  - PU-1 changes the speeds that enemy chase, projectile lead and knockback tuning assume.
  - Gear constants (wings, rockets, boots, the rebreather, flippers, info items, dyes, ammo slots) come from items (PU-3, PU-7, PU-17, PU-18, PU-19).
- **03 Enemies and 02 Bosses:** chase speed against the new player speed (PU-1); single-player respawn and boss despawn rules (PU-5); bestiary stats (PU-23).
- **05 World and tiles:** a space or sky band for PU-4; jump-reachable structures after PU-1; the hazard choice (PU-14).
- **08 NPCs and town:** shop grid and prices (PU-21), housing assignment API (PU-22), NPC map heads (PU-15), and whether NPC chat pauses (PU-13).
- **Travel and mounts** (whoever owns `GameSimulation.Travel.cs`): mount specs (PU-6, PU-2).
- **09 Presentation and engine:** video rows (PU-26); autosave hitch measurement (PU-12); art for the appearance editor, emotes and NPC heads.
- **01 Progression:** Creative isolation (PU-10); the starter kit is already applied through the character file.
- **Controls (`CONTROLS_PARITY.md`, in progress):** F11 hide UI (H9), Esc as inventory, map keys, and loadout keys F1-F3. PU-13 and PU-15 must not clash with its edits to `GraphicalSandbox.cs` around lines 360-440 and 900-990.

## Open questions for the user

1. **Movement speed:** move to Terraria-like speeds (≈2.5× faster run, ≈1.75× higher jump, ≈3.7× gravity), or keep Rimtera's slower, RimWorld-paced feel? The worlds are Terraria-sized, so the slow pace makes travel long.
2. Should the player character's colonist stats (traits, injuries, hunger) change their run speed? The default Adventurer runs at 0.873×.
3. **Autosave:** keep every 90 s (safer, risks hitching on Large) or move to Terraria's 10 min? Should closing the window always save even with Autosave off?
4. **Characters and worlds:** confirm that any character may play any world (Terraria's rule), so the current swap to the saving character is a bug.
5. **Swimming:** keep "everyone swims" (➕), or need a flipper item as Terraria does?
6. **Feats:** per character (now) or global per machine (Terraria)?
7. **Creative:** keep Creative characters out of normal worlds (Terraria), or allow mixing on purpose?
8. Is **multiplayer** out of scope for good? Is **localisation** wanted?
9. **Respawn:** keep the 20 s wait while a boss lives in single player (Terraria has none), or follow Terraria?
