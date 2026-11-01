# What Rimtera is missing from Terraria

Sources:
- **Terraria:** REA ID tables (`docs/terraria-evidence/gaps-*.json`, `items-*.json`): NPCID 766, ItemID ~5,400, TileID 755, WallID 368, ProjectileID 1,137, BuffID 402, MountID 67, PaintID 33, GameModeID 4.
- **Rimtera:** the game's own catalogues (`EnemyArchetypes`, `ItemCatalog`, the enums), counted on the 0.2.0 branch.

Earlier passes cover the base mechanics (`docs/MECHANICS_PARITY.md`), world generation (`docs/WORLDGEN_DIFF.md`), items (`docs/ITEMS_DIFF.md`) and hardmode (`docs/HARDMODE_DIFF.md`). The nine area audits in `docs/breakdown/` go deeper, row by row, and carry a "0.2.0 status" column.

**Revised for 0.2.0 (2026-11-01).** The first version of this page marked every system ✅ once it existed. The area audits showed that several of those systems exist but do not yet behave like Terraria's (events and paint most of all). The table below now says ◐ for those, with what is missing and where it is planned. Placeholder art (kitbashed recolours or reused sprites) is still used for some content.

## Content counts (0.2.0 branch)
| Category | Terraria | Rimtera | Gap |
|---|---|---|---|
| Bosses and event mini-bosses | ~36 | 33 bosses plus 9 event mini-bosses; every boss has a summon item or trigger ([CREATURE_COVERAGE.md](CREATURE_COVERAGE.md)) | Count ✅. Most bosses share one controller; bespoke patterns, worm and multi-part bosses are planned (parity 0.1.10 and 0.1.14 → 0.4.0 and 0.6.0) |
| Hostile creatures | ~450 distinct (766 NPC ids including segments and variants) | 330 hostile kinds (not bosses, not passive) | Terraria has more segment and variant ids; every biome, variant and event has its own roster. Several AI families are still missing (ranged, stationary, chargers, wall climbers, swimmers) |
| Critters | ~100 (with gold variants) | 102 passive kinds: 11 gold, 13 water, all catchable as bait | ✅ |
| Town NPCs and pets | ~40 | 29 town kinds: 20 NPCs plus Greenskeeper, Travelling Merchant, 4 town gels and town pets | ✅ count; see the town rows below |
| Items | ~5,400 (about 4,000 furniture, blocks, banners, vanity) | 1,990 | Combat and gear at parity; furniture, decor and vanity volume is not |
| Tiles | 755 | 355 tile ids | Furniture and decor sets |
| Walls | 368 | ~126 (not re-counted) | Decor walls |
| Buffs and debuffs | 402 | 64 statuses | The core set is covered, including the creature-inflicted debuffs of 0.1.15; Terraria has far more potion variety |
| Mounts | 67 | 6 (hover-bike, dune strider, rotor skiff, tide ray, burrower, spring hopper) | Variety only; every movement type exists, each with its own fall rule (0.1.14) |
| Projectile types | 1,137 | ~17 weapon styles plus enemy shots | Visual and behaviour variety (IC-12, IC-11 → 0.6.0 and 0.4.0) |
| Liquids | 4 | 6 (water, uranium, acid, nectar, phase, fuel) | ✅ count; flow speed and reactions differ (WT-12 → 0.5.0) |
| Flora | Tree types per biome, gem trees, herbs, vines, water plants, dye plants | 8 tree species plus gem trees, 8 wild harvestable plants, herbs, vines, water and dye plants; growth, felling, shaking, herb blooms, grass spread and cuttable grass plants follow the reference rules (docs/FLORA_PARITY.md) | ✅ |
| Paints | 31 paints and 2 coatings (PaintID 33) | 12 paints and 1 coating (glow) | ◐ volume |

## Systems: present ✅, partial ◐, missing ❌
| System | Status | Notes |
|---|---|---|
| Day and night clock | ✅ | Terraria's 24-minute day: 15 minutes of day, 9 of night (0.1.11). Buffs, regeneration and needs run on real time ("vital hours"). `--fast-clock` keeps the old 250-second cycle for testing |
| Movement, falls, breath, swimming | ✅ | Terraria's run, jump, gravity and terminal speed (0.1.14); fall damage 10 per tile past 25 and about 23 s of air (0.1.12); swimming needs fins (the Diver's kit), otherwise the player only paddles up |
| Death, respawn and stakes | ✅ | Death drops by character difficulty (0.1.8): Easy drops part of the money, Medium money and items, Hard everything but tools, and Ironman is the impossible run (drops everything but tools and dies for good). Explorer keeps everything. Terraria's respawn timer (0.1.11). The bed sets the spawn point (0.1.12). Ironman spectate after death (0.2.0). Rimtera adds permadeath for town NPCs and crew, with storyteller arrivals |
| Creature defense, immunities and debuffs | ✅ | Tiered creature defense, explicit immunities and lava damage; 77 creatures inflict debuffs on the player; weapons apply afflictions (0.1.15). Balance against boss life is the 0.2.0 balance pass |
| Healing | ✅ | Four-tier healing and charge ladder plus a restoration stim, 60 s potion sickness, boss potions and hearts, heart and charge-orb pickups (0.1.15) |
| Fishing (power, bait, crates, quest fish, biome pools) | ✅ | Caught critters are bait; gold critters are the best bait |
| Events (rain, storms, dust storm, blizzard, Rust Moon, Dark Sun, Swarm Rain, Scavenger Raid, pirates, Defense Protocol, Precursor Probes, Aurora Night, Colony Festival, meteors, obelisks) | ◐ | Every event exists and ends at its dawn or dusk boundary (0.1.11). Still different from Terraria: obelisk shields drop when their siege times out, not only to kills (EV-1); a legacy second event system still rolls the Moth-ray swarm, Deep-core tremors and dusk incursions with no progress bar (EV-2); EV-1 and EV-2 were planned for parity step 0.1.7 but are not done and not scheduled; event creatures spawn anywhere, not only on the surface or near the front (EV-3); natural triggers differ (EV-5); the Defense Protocol is a kill quota, not defend-the-core (EV-7); there are no event skies or event lighting (EV-8, PE-10). Event music exists. Planned for 0.4.0 |
| Seasonal wave moons (pumpkin and frost analogs) | ◐ | Hollow Moon and Rime Moon: summoned by item, waves, bosses. The wave count and loot-by-wave shape differ (EV-13 → 0.4.0) |
| Frost Legion-style invasion | ✅ | Cryo Brigade |
| Weather: rain, snow, dust, fog, storm, wind | ◐ | All present, with the Windy Day event and wind on projectiles, items and kites. Weather is rolled from the zone the player stands in, not the world's climate (EV-9); lightning depends on the frame rate (EV-10); wind does not push the player (EV-11). Planned for 0.4.0 |
| Minecarts and rails | ✅ (redesigned) | Replaced by glide-walks: airport-style moving walkway floors (regular 6 tiles/s, express 11) that add their speed to the rider's own walking; laid facing the placer, reversed by interacting; derelict transit walkways generate in the caverns |
| Fast travel (pylons) | ◐ | Active near a town with two housed residents; travel from the map. Not tied to happiness, and not blocked in danger or a boss fight (NC-11 → 0.5.0) |
| Mounts as a system | ✅ | Ground, flying, swimming, hover, jump and digging types; per-mount falls (0.1.14); a mount slot (0.1.9); mounts are restored on load |
| Kites | ✅ | |
| Golf | ✅ | Balls, clubs, tees, cups, strokes, Greenskeeper NPC; the ball returns to the pack on load |
| Paint and coatings | ◐ | Brush, roller and scraper paint tiles and walls, and a glow coating. 12 paints against Terraria's 31; no echo coating (WT-18 → 0.6.0). An earlier audit row said "no paint system"; that was wrong |
| Dyes on armor and accessories | ◐ | The dye vat crafts 8 dyes; a dye colours the worn suit; dye solvent clears it. There are no dye slots, and worn armour is not drawn on the player yet (PU-19, PE-3 → 0.6.0) |
| Equipment slots | ◐ | Vanity slots for head, body and legs; pet, light pet, mount and hook slots (0.1.9); coin and ammo slots (0.1.10). No dye slots or visibility toggles yet (PU-19 → 0.6.0) |
| Shops | ◐ | Sell any carried stack and buy back the last ten sales (0.1.9). No shop grid or Ctrl-click sell yet (PU-21 → 0.5.0) |
| Reforging | ❌ | No reforge service (NC-12 → 0.5.0) |
| Luck stat | ✅ | −1..+1; tilts rare drops, crits and calibration rolls |
| Aggro | ✅ | Aggro-weighted target choice; taunt and stealth gear |
| Debuffs on enemies | ✅ | Burning, poison, frostbite, armour shred, confusion, marked, venom, corrosion, wet; explicit immunities (0.1.15) and boss resistance |
| Treasure bags | ✅ | Brutal salvage caches with an exclusive core-chip |
| Trophies and relics | ✅ | |
| Music boxes | ✅ | |
| Emotes | ✅ | The Emotes tab in the inventory hub (0.2.0); hold ` and press 1–8 still works |
| Town pets and town slimes | ✅ | Pets by license; four town gels move in once the colony has 3 residents |
| Travelling merchant | ✅ | ~45% of mornings with 2+ residents; 6 items from a 20-item pool; leaves at 18:00 |
| Sifting | ✅ | Sediment sifter |
| Creative mode | ◐ | Research and duplication, time, spawn and difficulty powers; Creative characters play only Creative worlds (0.1.13). Research is stored in the world, not on the character (PC-13), and the weather and spread-freeze powers are missing (X-CR-10). Not scheduled yet |
| Secret world seeds | ✅ | A different set from Terraria's |
| Housing | ◐ | Room checks, NPC claims, the housing page with residents and Move here (0.1.9). Crew do not yet claim their own rooms (NC-3 → 0.5.0) |
| Happiness | ◐ | Biome, neighbour and crowding factors change prices. The factors and the report differ from Terraria's (NC-10 → 0.5.0) |
| Town NPCs fighting back | ❌ | NC-6 → 0.5.0 |
| Bestiary, achievements, banners, loadouts, storage, signs, hooks, pets, statues, shimmer analog (phase pool) | ✅ | Bestiary detail is thinner than Terraria's (PU-23 → 0.4.0) |
| Wiring | ◐ | Wire, switches, plates, timers, gates, actuators, traps and teleporters. No junction box and fewer triggers and outputs (WT-16 → 0.6.0) |
| Boss behaviour | ◐ | Every boss has a summon, refusals, despawn rules and loot. Most share one controller; per-boss patterns, minions, enrage, worm and multi-part bodies, the wall boss and Astro-God phases are planned (parity 0.1.10 → 0.4.0, 0.1.14 → 0.6.0) |
| Difficulty modes (worlds: Explorer, Classic, Brutal, Annihilation; characters: Explorer, Easy, Medium, Hard, Ironman, Creative) | ◐ | All present. Brutal and Annihilation are being re-scaled to Terraria's Expert (≈×2) and Master (≈×3) in the 0.2.0 balance pass (D-6) |
| Coloured lighting | ✅ | On High quality, light tints the scene per channel (0.2.0). Standard stays monochrome for speed |
| Visible armour | ❌ | Worn armour and vanity are not drawn on the player (PE-3 → 0.6.0) |
| Screen shake | ❌ | Camera smoothing exists (0.1.14); screen shake does not (PE-16, not scheduled) |

## Known limits
- Enemy aggro applies to the main ground, flyer and caster AI families within a set distance.
- Wired decks toggle by interacting only.
- Beyond the ◐ and ❌ rows above, what remains is volume: furniture and decor sets, vanity, more projectile visuals, more mount and buff variety (X-TG-2, deferred past 0.2.0).
