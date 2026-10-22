# Creature coverage: biomes, events and bosses (Terraria vs Rimtera)

Every biome and biome variant, every event and every boss, with the creatures, mini-bosses, bosses and summon items each one
has, set against Terraria's structure. The coverage pass filled the gaps listed under "Before"; the "After" columns are the
current state. Self-tests: `Systems/CreatureCoverageSelfTests.cs` (11 checks, hooked into `CoreSelfTests`).

**How to read it**
- **Terraria** is behavioural and structural evidence only (v1.4.5.8). Rimtera names, numbers, code and art are original.
- **Evidence:**
  - `NPC.SpawnNPC` hands off to `Spawner.SpawnAnNPC` (19,738 CIL instructions, dumped with `docs/terraria-evidence/full/scripts/dumpre.py 'Spawner::'`).
  - The spawn pools branch on these fields, with the number of reads in brackets:
    - `Main.hardMode` (86), `Spawner.raining` (21), `ZoneSnow` (20), `ZoneGraveyard` (17), `ZoneCrimson` (14), `ZoneCorrupt` (13), `ZoneHallow` (9), `ZoneJungle` (7)
    - `NPC.downedPlantBoss` (7), `Main.invasionType` (5), `Main.bloodMoon` (5), `Main.eclipse` (2), `Main.pumpkinMoon`, `Main.snowMoon`, `Sandstorm.Happening`
    - `ZoneOldOneArmy`, `ZoneTowerSolar/Vortex/Nebula/Stardust`, `ZoneMeteor`, `ZoneDungeon`, `ZoneLihzhardTemple`, `ZoneGlowshroom`
  - Every Terraria creature named below exists in `gaps-NPCID.json`, which was checked by name.
  - `main.json` carries `InTorchGodMinigame` and `needTorchGodsFavor`.
- **Rimtera** rosters: `Systems/BiomeSpawnTables.cs`, `Systems/Content/ContentFillData.cs` and `Systems/Content/CreatureCoverageData.cs`. Events: `Systems/WorldEvents.cs` and `Systems/GameSimulation.Seasons.cs`. Coverage logic: `Systems/CreatureCoverage.cs` and `Systems/GameSimulation.CreatureCoverage.cs`.
- Counts are hostile kinds; critters are left out.
- **Status:** ✅ covered, ◐ partial or different on purpose, ❌ missing (before the pass).

## 1. Biomes (every `BiomeZone`)

Hardmode creatures take 55% of spawn rolls after the Fracture (`BiomeSpawnTables.HardShare`). Every zone now has at least 4 regular and 4 hardmode hostiles, and at least 2 natives found in no other zone's regular roster. The self-test checks this.

| Zone | Terraria analog | Regular (count: creatures) | Hardmode (count: creatures) | Biome boss / mini-boss | Before → after |
|---|---|---|---|---|---|
| Grassland (surface, night, soil layer) | Forest, surface night, underground | 20: swarm bug, spore rat, sky-ray, spore bat, astro-beast, thistle boar, meadow gel, hornet drone, bore beetle, dusk prowler, umbral gel, gloom kite, grave husk, drifting eye; soil layer: tunnel rat, burrow spider, clay golem, grub worm, mud gel, drill mite | 11: Neo-Terra reaver, terraformer drone, halo bat, crystal wraith, storm ray, wasteland beast, thorn behemoth, storm harrier, night revenant, shock gel, sky lancer | Overseer mech (Eye of Cthulhu), Alpha Skitter (King Slime) | ✅ → ✅ |
| Caverns | Cavern layer | 10: rock mite, security droid, spore bat, void wisp, gem crawler, cave lurker, slate gel, echo caster, crystal borer, hollow bat | 15: tunnel wyrm, rift caster, cache mimic, vengeful wisp, basalt golem, geode crab, bedrock crusher, delve wyrm, shade caster, urn mimic, geode titan, void mantis, runic caster, armored delver, strongbox mimic | Damaged Mining Drone (no twin); mimics | ✅ → ✅ |
| Frozen Wasteland | Snow / tundra | 10: snow-wolf, cryopterous, snow-mite, glacial golem, rime gel, frost flitter, tundra brute, drift leaper, cryo caster, permafrost borer | 8: glacial golem, Neo-Terra reaver, halo bat, dive kite, glacier titan, blizzard wraith, frozen mimic, rime diver | **Hoarfrost Behemoth** (Deerclops), new | ◐ no frozen boss → ✅ |
| Junk Wasteland | Desert | 11: junk-skitter, scavenger crab, salvage drone, scrap-mite, wasteland beast, rust gel, scrap hound, bolt drone, dune phantom, junk tinker, slag borer | 8: wasteland beast, terraformer drone, scrap elemental, Neo-Terra reaver, junk-pile mimic, scrap colossus, rust reaper, dune devourer | none (Terraria's desert has none) | ✅ → ✅ |
| Radiated Deeps | Corruption / Crimson | 10: rad-skitter, mutant rat, geiger bug, glow-stalker, rad-leech, isotope gel, fallout hound, rad moth, fallout seer, sludge worm | 7: rad-leech, glow-stalker, lesser abomination, void siphon, rad behemoth, isotope wraith, lead-box mimic | Fallout Hive-mind (Eater of Worlds / Brain of Cthulhu, from radiated hearts) | ✅ → ✅ |
| Overgrowth | the other evil biome | 8: spore rat, spore bat, swarm bug, astro-beast, root gel, blight crawler, thorn bat, root wyrm | 7: astro-beast, lesser abomination, corrupted survivor, void siphon, blight hulk, rot chanter, vine reaper | Plant Overgrowth (from spore hearts) | ✅ → ✅ |
| Irradiated Core (`MoltenChasm`) | Underworld | 11: irradiated husk, fission wisp, glow-stalker, rad-leech, rad-skitter, geiger bug, slag gel, fission hound, core borer, isotope imp, reactor bat | 6: irradiated husk, fission wisp, tunnel wyrm, lesser abomination, meltdown golem, critical wisp | Fission Tyrant (Wall of Flesh) | ✅ → ✅ |
| Fuel Sea (`AcidCoast`) | Ocean (shore and water) | 15: saltjaw shark, tide leech, reef crawler, brine skitterer, spitting barnacle, storm ray, drowned diver, brine golem, coral sentinel, fuel gel, pincer crab, gull diver, fuel eel, barb ray, ink squid | 8: saltjaw shark, brine golem, coral sentinel, storm ray, bloodfin ambusher, deep maw, reef horror, tide caller | Abyss Leviathan (Duke Fishron) | ✅ → ✅ |
| Fungal Hollows | Glowing mushroom | 13: spore husk, fungal crawler, puff drifter, mycelid shaman, shroom stalker, cap turtle, lantern bat, spore golem, spore gel, toadstool leaper, mycelium borer, glowcap mite, cap sentinel | 7: spore golem, shroom stalker, mycelid shaman, rift caster, mycelid hulk, spore phantom, puff bomber | Spore Monarch (no twin) | ✅ → ✅ |
| Verdant Canopy | Jungle (surface and underground) | 14: thorn lurker, canopy ape, snapjaw plant, mud crawler, spitting orchid, jungle stalker, pollen drone swarm, vine gel, horn beetle, canopy bat, spear frog, root lasher, dart lizard, thorn chanter | 8: jungle stalker, canopy ape, thorn lurker, hive guard, moss hornet, canopy wyrm, strangler bloom, silverback brute | Mutated Flora (Plantera, from canopy bulbs) | ✅ → ✅ |
| Bloom Hives | Bee hive | 5: hive guard, pollen drone swarm, mud crawler, comb gel, brood larva | 5: hive guard, pollen drone swarm, swarm queen, hive warden, **royal comb-guard** | Bloom Matriarch (Queen Bee, post-Fracture by design) | ◐ 1 hard native → ✅ |
| Crystal Spires | Hallow | 13: prism knight, shard strider, light mimic, crystal wraith, refractor turret, halo bat, pearl golem, spire glassfly, prism gel, shard leaper, lumen caster, pearl wyrm, glass kite | 8: prism knight, shard strider, pearl golem, crystal wraith, vengeful wisp, prism colossus, halo seraph, chroma serpent | Prism Empress (Empress of Light), **Gel Sovereign** (Queen Slime), new | ◐ no Queen Slime → ✅ |
| Mite Warrens | Spider cave | 4: silk spider-mite, mite broodmother, rock mite, web lurker | 5: mite broodmother, silk spider-mite, tunnel wyrm, brood tyrant, **venom recluse** | none (as in Terraria) | ◐ 1 hard native → ✅ |
| Precursor Vault | Lihzahrd temple | 5: vault sentinel, glyph drone, ancient guardian, alabaster hoplite, glyph caster | 4: ancient guardian, vault sentinel, precursor probe, vault lancer | Vault Colossus (Golem) | ✅ → ✅ |
| Precursor dungeon (dungeon wall) | Dungeon | 7: void wisp, glyph drone, alabaster hoplite, vengeful wisp, crypt hoplite, void scribe, crypt flitter | 5, **new** (after the Mutated Flora): crypt knight, hex necrotech, crypt marksman, crypt spirit, crypt paladin (mini-boss) | Dungeon Guardian (Scout Mech gate); Void Cantor at the entrance | ❌ no hard dungeon → ✅ |

### Biome variants (Terraria distinguishes them; Rimtera now does too)

The variant takes 55% of the rolls where it applies (`CreatureCoverage.VariantShare`). The share is chosen by a hash of the spawn roll, so the random stream is unchanged. The variant's hardmode roster joins after the Fracture.

| Variant (slot) | Where | Terraria analog | Regular | Hardmode | Before |
|---|---|---|---|---|---|
| Sky layer (`sky`) | Above any surface biome: the upper 45% of the air column and at least 25 cells above the ground | Space: Harpy, Wyvern | stratos raptor (harpy), ion skate | stratos wyvern (wyvern), cirrus sentinel | ❌ |
| Underground junk (`junk-under`) | Junk Wasteland below the surface | Underground desert | pit charger (antlion charger), pit swarmer (antlion swarmer), tomb borer (tomb crawler) | dust ghoul (desert ghoul), rust basilisk (basilisk), grit seer (desert spirit) | ❌ (surface roster) |
| Underground frost (`frozen-under`) | Frozen Wasteland below the surface | Ice caverns | icicle bat (ice bat), frostbound delver (undead viking) | hoarfrost elemental (ice elemental), glacier tortoise (ice tortoise) | ❌ (surface roster) |
| Grave field (`grave`) | Surface with 6 or more memorial markers within ±30 × ±15 cells | Graveyard | grave wisp (ghost), carrion drone (raven), tomb husk (maggot zombie) | mourning shade | ❌ |
| Late dungeon (`dungeon-hard`) | Dungeon after `boss.mutated_flora.down` | Post-Plantera dungeon | — | crypt knight (armored bones), hex necrotech (necromancer, diabolist, ragged caster), crypt marksman (skeleton sniper and commando), crypt spirit (dungeon spirit), crypt paladin (paladin, mini-boss) | ❌ |

Night variants are spawn weights inside each roster (`DayWeight`/`NightWeight`), as before. Water variants are the
swimmer rule: swimmers only appear inside water or sea fuel (`BiomeSpawnTables.IsSwimmer`).

## 2. Events (every `WorldEventKind`)

Unique means a creature in no biome or variant roster. Mini-bosses are elite regular creatures, not bosses: no cache or boss bar. Each comes once per event, after the Fracture (`CreatureCoverage.EventMiniBosses`), once the event's progress passes its mark. The wave moons bring their mini-bosses by wave.

| Event [Terraria analog] | How it starts | Unique creatures (after the pass) | Mini-bosses | Boss | Before → after |
|---|---|---|---|---|---|
| Rainfall [rain] | Weather (rain > 0.35) | drizzle gel (umbrella slime), rain skipper (flying fish), slicker husk (raincoat zombie); hardmode thunderhead (angry nimbus) | — | — | ❌ no roster → ✅ |
| Acid rain [rain variant] | Weather | acid slime, acid bird, toxic cloud; hardmode corrupted survivor, scrap elemental | — | — | ✅ |
| Dust storm [sandstorm] | Weather (dust > 0.45) | dust devil, sand ray, scrap elemental, dust raider; hardmode **dune razorfin** (sand shark) | **grit elemental** (sand elemental) | — | ◐ no hardmode tier → ✅ |
| Blizzard [snow-biome rain] | Weather (snow > 0.5) | **whiteout wisp**, **drift stalker** (with snow-wolves and cryopterous) | **blizzard colossus** (ice golem) | — | ❌ biome mobs only → ✅ |
| Rust Moon [blood moon] | Dusk 12% (max HP 120+ or the Overseer down); **Corroded flare** (new, night only) | rust husk, rust wraith, rust hound, eye drone, corroded mech; hardmode lesser abomination, eclipse shade, **rust jester** (clown) | **corrosion nautilus** (Dreadnautilus) | Moon Eater (25-kill quota) | ◐ no starter, no hardmode native → ✅ |
| Dark Sun [solar eclipse] | Dawn 6% after the Fracture; **Umbral lens** (new, day only) | eclipse shade, eclipse reaper, eclipse moth-mech; after the Mutated Flora **eclipse slasher** (psycho, butcher), **eclipse sphere** (deadly sphere) | — | Eclipse Lord (Mothron) | ◐ no starter, no post-Plantera tier → ✅ |
| Swarm Rain [slime rain] | Rainy dawn 8% after the Overseer | swarmer, **armored swarmer**; hardmode **prism swarmer** (rainbow slime) | — | Alpha Skitter (King Slime) | ◐ one native → ✅ |
| Scavenger Raid [goblin army] | Dusk 10% after a heart; Scrap beacon | scavenger grunt, brute, sniper, rigger, raider scout; hardmode Neo-Terra reaver, scrap elemental | **scavenger hexer** (goblin summoner), hardmode | Scavenger Warlord | ◐ no hardmode leader → ✅ |
| Pirate Boarding [pirate invasion] | Dusk 5% after the Fracture; Pirate transmitter | pirate deckhand, gunner, parrot drone, boarding cannon | — | Pirate Dreadnought (Flying Dutchman) | ✅ |
| Defense Protocol [Old One's Army] | Signal core at the Station AI | protocol grunt, shieldbearer, wyvern, ogre, mage | ogre and mage play the wave-elite role | Protocol Dragon (Betsy) | ✅ |
| Precursor Probes [Martian Madness] | Dusk 5% after voidsteel ore | precursor probe, probe gunner, tesla walker, brain drone | — | Mothership Core (Martian Saucer) | ✅ |
| Hollow Moon [pumpkin moon] | Hollow lantern (night, after the Fracture) | **hollow effigy** (scarecrow), **ember splinter** (splinterling), **cinder hound** (hellhound), **static poltergeist** (poltergeist) | **ember rootwalker** (Mourning Wood, wave 4), **hollow lancer** (Headless Horseman, wave 6) | **Hollow Regent** (Pumpking, wave 8) | ❌ reused husks, Spore Monarch → ✅ |
| Rime Moon [frost moon] | Rime beacon (night, after the Fracture) | **frost sapper** (zombie elf, elf archer), **shellcracker** (nutcracker), **flurry flake** (flocko), **cargo mimic** (present mimic), **snow hulk** (yeti) | **frostspire pine** (Everscream, wave 4), **cryo tank** (Santa-NK1, wave 6) | **Rime Seraph** (Ice Queen, wave 8) | ❌ reused wolves, Prism Empress → ✅ |
| Cryo Brigade [Frost Legion] | Cryo orders (after the Fracture) | **brigade trooper** (snowman gangsta), **brigade shiv** (Mister Stabby), **brigade lobber** (snow balla) | — (none in Terraria either) | — | ❌ biome mobs → ✅ |
| Ember / Gale / Rift / Shard obelisk [Solar / Vortex / Nebula / Stardust pillar] | The Void Cantor's death | Ember: **cinder imp** (corite), **slag centipede** (crawltipede), **pyre lancer** (selenian, drakomire rider). Gale: **gale diver** (storm diver), **squall hornet** (alien hornet), **tempest larva** (alien larva). Rift: **rift seer** (predictor), **mind leech** (brain suckler), **rift floater** (nebula floater). Shard: **star-cell** (star cell), **lattice weaver** (milkyway weaver), **shard invader** (flow invader) | The obelisk itself (shielded until 20 kills) | Astro-God after all four | ❌ reused mobs → ✅ |
| Aurora Night [lantern night] | Clear dusk 10% | aurora wisp (passive; no hostile spawns) | — | — | ✅ |
| Colony Festival [party] | Dawn 8% with 3+ residents; Festival flare | festival balloon drone (passive) | — | — | ✅ |
| Windy day [windy day] | Dry dawn 10% | **thistledown drifter** (angry dandelion), **gust mite** | — | — | ❌ no creatures → ✅ |
| Meteor fall [meteor] | 50% after a heart; Starfall season | meteor head, meteor crawler | — | — | ✅ |
| **Lumen Tempest** [Torch God] (new) | 40 or more light fixtures within ±40 × ±25 cells of an underground player | **lumen flare**, **glare mote** (12-kill swarm) | — | — (none in Terraria); the first clear grants the **Lumen accord** (Torch God's Favor) | ❌ → ✅ |

## 3. Bosses (every boss archetype) and how each is summoned

Every boss now has an obtainable summon item. The one exception is the Abyss Leviathan, which is fished up with abyss bait (Bio Lab), like Duke Fishron. Summon refusals live in `GameSimulation.HardmodeSummonRefusal` and `CoverageSummonRefusal`.

| Boss | Tier | Terraria place | Phase | Summon item (station) or trigger | Gate |
|---|---|---|---|---|---|
| Overseer mech | Gate | Eye of Cthulhu | pre-Fracture | Reactor key (Nano-Fabricator); natural dusk patrol (life 140+, defence 6+) | — |
| Alpha Skitter | Easy | King Slime | pre | Swarm pheromone (Chem Lab); Swarm Rain finale | — |
| Damaged Mining Drone | Easy | (no twin) | pre | Faulty mining chip (Nano-Fabricator) | — |
| Plant Overgrowth | Easy | Brain of Cthulhu (overgrowth) | pre | Rotting seed (Bio Lab); every third spore heart | — |
| Fallout Hive-mind | Easy | Eater of Worlds (radiated) | pre | Fallout hive cell (Chem Lab); every third radiated heart before the Fracture | — |
| Scout Mech | Easy | Skeletron's role (dungeon gate) | pre | Scout beacon (Power Forge) | — |
| Spore Monarch | Easy | (no twin; glowshroom) | pre | Spore crown (Bio Lab) | — |
| **Hoarfrost Behemoth** (new) | Easy | Deerclops | pre | **Frostbitten lure** (Chem Lab: 12 frost crystals, 10 glacial ore, 4 bio-gel) | Frozen Wasteland only |
| Fission Tyrant | Gate | Wall of Flesh | pre → causes the Fracture | Core igniter (Power Forge Mk2) | In the Irradiated Core, after the Scout Mech |
| Scavenger Warlord | Event | (goblin army leader) | pre | Warlord banner (Loom); Scavenger Raid finale | — |
| Moon Eater | Event | (blood moon leader) | pre | Rusted lure (Chem Lab); Rust Moon finale | — |
| Solar Flare Entity, Void Stalker, Asteroid Miner Mech, Cyber-Pirate Captain | Event | (dusk incursions; Rimtera's own) | pre (after the Scout Mech) | Sunspot lens (Kiln), Void lure (Chem Lab), Claim marker (Power Forge), Bounty notice (Nano-Fabricator); random dusk incursion | Incursions wait for the Scout Mech |
| **Gel Sovereign** (new) | Hard | Queen Slime | post-Fracture | **Prism gel lattice** (Alloy Anvil: 10 prism shards, 20 bio-gel, 3 prism essence) | After the Fracture, in the Crystal Spires |
| Cyber-Swarm Leader, Renegade Overseer, Warship Core | Hard | the mechanical trio | post | Swarm resonator, Renegade credentials, Warship transponder (Alloy Anvil); natural dusk arrival | After the Fracture |
| Abomination | Hard | (no twin) | post | Tainted tissue (Bio Lab) | After the Fracture |
| Bloom Matriarch | Hard | Queen Bee (post-Fracture on purpose, G2) | post | Royal nectar (Bio Lab); bloom hive | After the Fracture |
| Prism Empress | Hard | Empress of Light | post | Prism catalyst (Nano-Fabricator Mk2); crystal altar | After the Fracture |
| Mutated Flora | Hard | Plantera | post | Canopy bulb; Mutagen bloom (Bio Lab) | After the hard trio |
| Vault Colossus | Hard | Golem | post | Vault core (Research Terminal) | Vault opened |
| Void Cantor | Hard | Lunatic Cultist | post | Dungeon ritual; Void hymnal (Void Forge) | After the Vault Colossus |
| Eclipse Lord | Event | Mothron | post | Eclipse orb (Nano-Fabricator Mk2); Dark Sun finale | — |
| Pirate Dreadnought | Event | Flying Dutchman | post | Dreadnought flag (Loom); Pirate Boarding finale | — |
| Protocol Dragon | Event | Betsy | post | Protocol key (Research Terminal); Defense Protocol finale | — |
| Mothership Core | Event | Martian Saucer | post | Mothership beacon (Void Forge); Precursor Probes finale | — |
| Abyss Leviathan | Event | Duke Fishron | post | Abyss bait fished in acid (bait from the Bio Lab) | — |
| **Hollow Regent** (new) | Event | Pumpking | post | Hollow Moon wave 8; **Regent's wick** (Nano-Fabricator Mk2: 20 hollow embers, 6 lumenite bars) | After the Fracture, at night |
| **Rime Seraph** (new) | Event | Ice Queen | post | Rime Moon wave 8; **Seraph frost-core** (Nano-Fabricator Mk2: 20 rime shards, 6 lumenite bars) | After the Fracture, at night |
| Astro-God | Final | Moon Lord | end | Countdown after the obelisks; Astral sigil (Void Forge) | Obelisks down |

**Event starters** (Terraria has an item for each of these): Corroded flare → Rust Moon (new; night only; Bloody Tear). Umbral lens → Dark Sun (new; day only; Solar Tablet). Scrap beacon → Scavenger Raid. Pirate transmitter → Pirate Boarding. Signal core → Defense Protocol. Hollow lantern → Hollow Moon. Rime beacon → Rime Moon. Cryo orders → Cryo Brigade. Festival flare → Colony Festival. Terraria has no item for slime rain, martians, pillars, weather, lantern night, the windy day or the Torch God, and Rimtera adds none for their analogs.

## 4. What the pass added

| Kind | Count | Ids |
|---|---|---|
| New `EnemyKind` values | 77: 64 regular creatures, 9 mini-bosses, 4 bosses | **1400–1476** (band reserved 1400–1599; banners need ids below 2000) |
| Mini-bosses | 9: crypt paladin, blizzard colossus, grit elemental, corrosion nautilus, scavenger hexer, ember rootwalker, hollow lancer, frostspire pine, cryo tank | in the band above |
| Bosses | 4: Hoarfrost Behemoth, Gel Sovereign, Hollow Regent, Rime Seraph (salvage cache, trophy, relic and core-chip slots 29–32 of 40) | 1473, 1474, 1447, 1455 |
| Summon and starter items | 6: frostbitten lure, prism gel lattice, regent's wick, seraph frost-core, corroded flare, umbral lens | `ItemId` **9400–9405** (band 9400–9499) |
| Unique loot weapons | 17: two per new boss, one per mini-boss (arsenal rows, `ArsenalItems.Coverage.cs`) | `ItemId` **6900–6916** (inside the arsenal band 6000–6999, which `ArsenalItems.Owns` requires) |
| Banners | 73 (dynamic, `20000 + kind`) | — |
| Events | 1: `WorldEventKind.LumenTempest` (appended) | — |
| Sprites | 189 kitbashed placeholders: 77 creatures, 73 banners, 16 boss items, 23 items (`tools/sprites/kitbash.py`, `_CREATURE_COVERAGE`) | no existing sprite overwritten |

Each creature has:
- an archetype in its tier band (pre-Fracture 25–150 HP; post-Fracture 150–800 HP; mini-bosses 900–1800 HP; bosses inside their tier)
- an existing AI family (default, hopper, skittering hopper, worm, caster, diving flyer or mimic)
- drops, with event materials (hollow embers, rime shards, astral ore) from event units
- a banner, a bestiary entry (automatic) and a spawn slot

`tools/comfy/anim_entities.tsv` has the 77 new sprites (77 added lines), so the paused art run picks them up.

## 5. Deliberate differences from Terraria

- **Moon bosses are full bosses with their own summon items.** Rimtera lets every boss be re-summoned; the Moon Eater and the Eclipse Lord already worked this way. They wait for the Fracture and for night.
- **Mini-bosses are elite regular creatures.** They have no boss bar or cache. An event brings each one once, and only after the Fracture, which matches Terraria's hardmode-only Ice Golem, Sand Elemental, Dreadnautilus and Goblin Summoner.
- **The Cryo Brigade and the Lumen Tempest have no leader**, as in Terraria (Frost Legion, Torch God).
- **The Torch God analog has creatures.** Terraria only throws projectiles, but Rimtera's events are creature-based. Its reward, the Lumen accord, is the Torch God's Favor, and it is granted once.
- **Graveyard detection** counts memorial markers (6 within ±30 × ±15) instead of Terraria's tombstone scan. **The sky layer** is the upper 45% of the air column, at least 25 cells up.
- **Underground variants exist only for junk and frost.** The other underground biomes (fungal, hives, mite warrens, vault) are underground zones in their own right. The canopy roster already mixes jungle surface and underground natives.
- **The late dungeon is one roster.** Terraria splits it by dungeon-wall colour.
- **Obelisk natives drop astral ore**, not a fragment per pillar. The obelisks' existing loot was kept.
- **Queen Bee stays post-Fracture** (the Bloom Matriarch, decision G2). **Skeletron's role stays with the Scout Mech** (the dungeon gate, G12).
- **The rain and blizzard rosters ignore time of day.** The blizzard keeps two frozen natives beside its own creatures.

## 6. Tests

`CreatureCoverageSelfTests` (11 checks; the suite now passes 587):
1. Every biome has at least 4 regular and 4 hardmode hostiles with 2 or more natives, and the dungeon has both rosters.
2. Each variant has its own rosters, and hardmode kinds wait for the Fracture.
3. The spawner picks the right variant for each spot. The grave field takes about 55% of the rolls.
4. Every event fields its own creatures (at least 3 for invasions and moons, 2 for weather, 1 for calm and party days) and its leader.
5. Special events have special leaders: two mini-bosses and a boss per moon, the storm and raid mini-bosses, and three natives per obelisk.
6. The blizzard colossus arrives once, mid-event, after the Fracture.
7. The late Dark Sun units wait for the Mutated Flora.
8. Every boss has an obtainable summon item or the abyss bait, and every event Terraria starts with an item has a starter.
9. The summon and starter gates hold: the frozen-only lure, the post-Fracture lattice, and the night-only wick and flare.
10. Every new creature has a band, a slot, drops, a banner, a sprite and loot.
11. The Lumen Tempest triggers on a light hoard and grants the accord once.
