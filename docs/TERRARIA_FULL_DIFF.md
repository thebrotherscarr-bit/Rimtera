# Terraria base game vs Rimtera: full diff

Evidence: REA metadata of `Terraria.exe` 1.4.5.8. Earlier files in `docs/terraria-evidence/`, plus `systems.json` (Wiring, Collision, Chest, Sign, Gore, Dust, SoundEngine, BestiaryDatabase, AchievementsHelper, ItemSlot), the `Player` members in `fishing.json`, the NPC and projectile `AI_###` families, and WorldGen helpers. Member names and structure only; every Rimtera system, number and name is original.

Status key: ✅ present · ◐ partial · ❌ missing.

## Player
| Terraria element (evidence) | Rimtera | Notes |
|---|---|---|
| Run, jump hold, dash (`JumpMovement`, `DashMovement`, `UpdateJumpHeight`) | ✅ | late jump, step-up |
| Wings, rocket boots, double jumps (`WingMovement`, `RefreshDoubleJumps`, `RocketBootVisuals`) | ◐ | wing energy exists; accessories only grant tags |
| Grappling hooks (`GrappleMovement`, `QuickGrapple`, projectile `AI_007_GrapplingHooks`) | ◐ | rope constraint exists; no hook projectile |
| Ropes and ladders (`CanMoveForwardOnRope`, `IsRope`, `GetRopeEnds`) | ❌ | |
| Breath and drowning (`CheckDrowning`, `breathCDMax`, `DrownCollision`) | ❌ | |
| Fall damage (`ResetFallDamage`) | ◐ | fall distance measured, no damage |
| Lava, water, honey movement (`LavaCollision`, `ShouldFloatInWater`) | ◐ | heat damage only; no swim or slow |
| Buffs and debuffs with icons, potion sickness (`AddBuff`, `UpdateBuffs`, `ApplyPotionDelay`, `QuickHeal`/`QuickMana`/`QuickBuff`) | ◐ | status effects exist; no quick keys, sickness or icon bar |
| Life and mana regen, crystals (`UpdateLifeRegen`, `UpdateManaRegen`, `ItemCheck_UseManaCrystal`) | ◐ | life cores; charge regen; no charge crystals |
| Immunity frames after a hit (`GiveImmuneTimeForCollisionAttack`, `SetImmuneTimeForAllTypes`) | ❌ | |
| Death: tombstone, respawn timer, boss blocks respawn (`DropTombstone`, `GetRespawnTime`, `AnyBossHindersRespawnTime`) | ◐ | countdown; no tombstone or boss rule |
| Item pickup range and magnet (`GrabItems`, `PullItem_Pickup`, `CanGoIntoAmmoOnPickup`) | ❌ | drops go straight to the inventory |
| Equipment loadouts (`TrySwitchingLoadout`) | ❌ | |
| Vanity and dyes (`ApplyEquipVanity`, `UpdateDyes`) | ◐ | vanity items exist |
| Pets and light pets (`UpdatePet`, `UpdatePetLight`) | ❌ | |
| Mounts (`QuickMount`, `ItemCheck_HandleMount`) | ❌ | |
| Teleport items (`TeleportationPotion`, `ItemCheck_UseTeleportRod`, recall) | ❌ | |
| Character carries inventory across worlds (separate player file) | ❌ | inventory lives in the world save |

## Inventory and UI
| Element | Rimtera | Notes |
|---|---|---|
| 50 slots, hotbar 10, coin and ammo slots, trash, favorites (`ItemSlot`) | ◐ | 40-ish slots and hotbar; no trash, ammo or favorites |
| Quick stack to nearby chests, sort, loot all, deposit all (`QuickStackAllChests`, `TryMoveCoinsInChest`) | ❌ | |
| Personal storage: bank and safe (`Chest.CreateBank`) | ❌ | |
| Signs (`Sign.TextSign`, `ReadSign`) | ❌ | |
| Buff icon bar with timers | ❌ | |
| Bestiary (`BestiaryDatabase`: entries, filters, drops) | ❌ | |
| Achievements (`AchievementsHelper` notify hooks) | ❌ | |
| Audio: sound effects and music (`SoundEngine`) | ❌ | volumes saved, no playback |
| Particles (`Dust`, `Gore`) | ◐ | renderer bursts only |

## Combat and NPCs
| Element | Rimtera | Notes |
|---|---|---|
| Melee swing hitbox, spears, flails, boomerangs, yo-yos, whips (projectile AIs 3, 15, 19, 99, 165) | ◐ | items exist; one generic melee check |
| Bombs and explosives destroying tiles (`AI_016_Bombs`) | ❌ | |
| Crits, knockback, armor penetration | ✅ | |
| Enemy AI families: slime, floating eye, fighter, worm, flyer, caster, diving flyer, critters (`AI_001` … `AI_006`, 107, 108, 112–118) | ◐ | ground, flying, boss; no worms, casters or divers |
| Mimics (`AI_087_BigMimic`, statue mimics) | ❌ | |
| Kill banners: kill count → banner → bonus near it (`HasNPCBannerBuff`, `GetBannerBuffEffect`) | ❌ | |
| Statues that spawn creatures on wire (`Statue`, `StatueStyleToItem`) | ❌ | |

## World and tiles
| Element | Rimtera | Notes |
|---|---|---|
| Slopes and half blocks with hammer (`SlopeTile`, `PoundTile`, `SlopeCollision`) | ❌ | |
| Platforms: one-way, drop-through, stairs (`PoundPlatform`, `SolidTileAllowPlatformTopFrame`) | ❌ | |
| Trees: grow from saplings, chop with axe, drops, shaking (`GrowTree`, `AttemptToGrowTreeFromSapling`, `KillTile_GetTreeDrops`, `ShakeTree`) | ❌ | |
| Vines, cacti, herbs with bloom rules (`GrowMoreVines`, `GrowCactus`, `GrowAlch`, `IsAlchemyPlantHarvestable`) | ❌ | |
| Falling sand (`BlockBelowMakesSandFall`, `SpawnFallingBlockProjectile`) | ❌ | |
| Liquid mixing: water + lava → obsidian (`CreateLiquidMergeTile`) | ❌ | |
| Breakable pots (`PlacePot`, `SpawnThingsFromPot`) | ◐ | salvage pods |
| Torches and lights placed by player (`CheckTorch`, `ToggleTorch`) | ◐ | lamps; no torch item for walls |
| Wiring: wire, switches, pressure plates, timers, logic gates, actuators, teleporters, traps (`Wiring.*`, `PlaceWire`, `Actuate`, `CheckLogicGate`, `TripWire`, `placeTrap`) | ❌ | |
| Dungeon with guardian (`IsDungeon`, dungeon gen) | ❌ | |
| Underground cabins, chests, traps, enchanted shrine, living trees, pyramids, spider nests (`AddBuriedChest`, `GrowLivingTree`, `Pyramid`, `placeTrap`) | ◐ | caches, pods, vault, derelict |
| Meteor showers (`StartMeteorShower`) | ◐ | single meteors |
| Tombstones and graveyards | ❌ | |

## Implementation plan (original designs)
- **Batch A (world physics and tiles):**
  - slopes and half blocks with collision
  - platforms (one-way; hold S to drop through)
  - ropes and ladders with climbing
  - trees from saplings (chop for timber and saplings), vines, cacti, herbs with growth and bloom
  - falling sand and gravel
  - water + lava → obsidian, nectar + water → hive comb
  - player-placed torches as lights
  - wiring: wire tool, switches, levers, pressure plates, timers, AND/OR/NOT gates, actuators that toggle solidity, wired doors and lamps, dart and explosive traps, teleporter pads
  - bombs and dynamite that break tiles
  - worldgen for buried cabins with chests and traps, a Precursor Dungeon with a guardian that guards it until a boss falls, living-tree analogs (giant canopy trees), spider nests (mite warrens), pots, and shrines with a weapon
- **Batch B (player, items, NPCs, saves):**
  - ground item entities with pickup range and magnet (accessory)
  - immunity frames
  - breath, drowning and the rebreather
  - fall damage (fall-negating accessories)
  - swimming in liquids
  - mobility accessories working: double jump, rocket boots, glider wings, grappling hook projectile, climbing claws
  - quick heal, quick charge and quick buff keys with potion sickness
  - charge crystals
  - tombstones and a boss blocking respawn
  - loadouts (3)
  - personal storage (Cargo Pod: a bank reachable anywhere via an item)
  - trash, favorites, sort, quick stack, loot all and deposit all for chests
  - signs
  - pets and light pets
  - one mount (a hover-bike)
  - recall and teleport items
  - character files: inventory, equipment, max HP and charge persist across worlds, saved separately from the world
  - kill banners
  - enemy AI families: worm (segmented), caster (teleports and fires), diving flyer, mimic, slime hop variants
  - bestiary data (kills, drops seen, biomes) and achievements (event hooks)
- **Batch C (UI and audio, renderer):**
  - buff bar
  - inventory additions (trash, favorites, sort, loadouts, storage)
  - sign editing
  - bestiary and achievements screens
  - procedural sound effects and ambient music with volume settings
  - drawing for slopes, platforms, ropes, trees, wires (wire view), ground items, pets, mounts, tombstones and worm segments

## Status after Batches A–C
All three batches are in. This section records that milestone; the current `--self-test` suite passes 550 checks.
- **A (world):** slopes and half blocks, platforms, ropes and ladders, trees, plants and herbs, falling sand, liquid mixing, torches, wiring with gates, actuators, traps and teleporters, bombs, dungeon with guardian, cabins, shrines, pots.
- **B (player):** ground items with a magnet, immunity frames, breath, fall damage, swimming, mobility gear, grappling hook, quick heal/charge/buff with potion sickness, tombstones, respawn blocked while a boss lives, loadouts, Cargo Pod bank, trash, favorites, sort, quick stack, signs, pets, hover-bike, recall and phase rod, character files, kill banners, worm/caster/diver/mimic/hopper AI, bestiary, achievements.
- **C (UI):** drawing for all of the above (wire view while holding wire tools, `L` cycles layer), buff bar, breath bubbles, keys (`Q` heal, `J` charge, `Y` buff, `T` hook, `U` mount, `F1`–`F3` loadouts, `N` bestiary, `O` achievements, `?` controls), inventory actions, bank and sign screens, achievement toasts, procedural audio.
- **HUD:** Terraria-style layout. Hotbar top-left, vitals above the minimap, buffs under the hotbar, boss bar bottom-centre, events bottom-right, and the world fills the window.
- **Still open:** projectile collision ignores slopes and actuated blocks; mined supports drop nothing from collapsed trees, vines and ropes; climbing has no animation of its own. Meteor showers are implemented (`MeteorFall`/`MeteorStrike`).
- **Saves:** a world's cells go in a raw-deflate sidecar (`<id>.rimtera.cells`) beside a small JSON envelope. Generation metadata is saved too, so loading never regenerates. On a Large world (8400×2400), a save is about 8 MB and takes 0.35 s; a load takes 0.7 s; the game runs in about 700 MB. Older saves with base64 cells inside the JSON still load. `--engine-bench --leak` checks that saving and loading leave no world-sized buffers behind.
