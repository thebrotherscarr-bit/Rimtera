# Neo-Terra biome themes

One row per biome: its theme, block palette, flora and decorations, creatures (spawn roster) and loot. The theme
audit fixes are listed after the table. Terraria is a structural reference only: names, numbers and rosters are
Rimtera's own.

Zone ids that kept an old name for save compatibility:
- `BiomeZone.AcidCoast` is the **Fuel Sea** (the oceans at both edges).
- `BiomeZone.MoltenChasm` is the **Irradiated Core**.
- `LiquidKind.Lava` is **liquid uranium**.
- `LiquidKind.Fuel` (6) is the new amber **fuel** that fills the seas.

| Biome | Theme | Block palette | Flora / decorations | Creatures (roster) | Loot / materials |
|---|---|---|---|---|---|
| Surface (grassland) | Temperate regolith meadows around the landing site | Regolith, regolith moss, vine-mat meadows, surface ore and stone patches | Trees, saplings, sunpetal herbs, solar lilies, dye blossoms, wild gourds, fallen logs, ponds with lilypads and reeds | Ambient bands: moss slime, spore rat, sky-ray (day), cave bat and void wisp (night), astro-beast, cave moth. Wildlife: Neo-Terra deer, sky hawk. Families: gel/flea hoppers, dive kites | Surface supply crates (rope, torches, saplings), surface pots (sunpetal seeds), ferrite ore |
| Underground (regolith layer) | Packed soil over the bedrock | Regolith, basalt, clay, silt, gravel, root-laced walls | Speleothems, rubble piles, wild mushrooms | Ambient underground bands: rock mite, rock golem, spore rat, cave moth | Buried cabins (cabin chests), supply caches, salvage pods, ferrite and cobalt |
| Caverns | Deep basalt caves | Basalt, slate, gravel, gem-studded rock, moss (4 colours), layered and rough cave walls | Stone stalactites and stalagmites, long moss, crystal shrubs, underground lakes, water seeps | Caverns roster: rock mite, rock golem, cave bat, void wisp. Families: tunnel wyrm, rift caster, boulder hopper, cache mimic | Gem shards, cobalt (also near the surface since 0.3.3) and viridium veins, life cores, cavern urns, rigged chests, mining caches, statues |
| Frozen Wasteland (west) | Glacial tundra and ice caves | Ice (snow), slush, thin ice over ponds, glacial ore, ice walls | Pines, frostbell herbs, ice stalactites, frost crystals | Snow-wolf, cryopterous, snow mite, glacial golem | Glacial ore, frost crystals. Frozen pots drop frostbell seeds. Chests here gain frost crystals |
| Junk Wasteland and scrap dunes (east) | Rusting scrapyard desert | Dune sand, hardened dune sand, scrap-iron ore, scrap and sand walls | Dune-sand speleothems, burrower nests, coolant springs with palms | Junk skitter, scavenger crab, salvage drone, scrap mite, wasteland beast. Families: dune borer | Buried landers (lander strongboxes: power cells, drone parts), scrap-iron ore. Junk pots drop rainreed seeds. Chests gain oxidized shards |
| Verdant Canopy | Overgrown jungle | Mud, canopy grass, mud walls, living-wood giant trees, mud brick shrines | Canopy trees, giant leaves, pitcher plants, thorn bushes, bloom flowers, hanging vines, canopy pools | Thorn lurker, canopy ape, snapjaw plant, mud crawler, spitting orchid, jungle stalker, pollen drones. Critters: parrot, leaf frog, vine snake, pollen bee | Canopy shrines (shrine chests), canopy urns (moonglow seeds), vine-root blocks |
| Bloom Hives | Hive comb chambers under the canopy | Hive comb, nectar liquid, bloom hives, hive walls | Comb drips and mounds, brood pods | Hive guard, pollen drone swarm, mud crawler, pollen bee | Nectar, royal nectar, brood pods (summon the matriarch). Chests gain nectar |
| Fungal Hollows | Glowshroom caverns and spore groves | Glowshroom grass, mycelium stone, spore blocks, mushroom caps, spore mat, glowshroom walls | Giant glowshrooms, shelf fungus, spore puffballs, glow vines, biolum moss, wild mushrooms | Spore husk, fungal crawler, puff drifter, mycelid shaman, shroom stalker, cap turtle, lantern bat, spore golem | Glow spores, mushroom caps. Chests gain glow spores |
| Crystal Spires (post-Fracture) | Prismatic crystal growth | Prismatic grass, pearl stone, pearl sand, crystal spires, lumenite ore | Crystal trees, prism flowers, light reeds, singing crystals, halo bushes | Prism knight, shard strider, light mimic, crystal wraith, refractor turret, halo bat, pearl golem, spire glassfly | Prism shards, lumenite. Chests gain prism shards |
| Blight: Radiated | Fallout zones and deep pockets | Radiated soil, caustic crystal, irradiated sand, glowing ice, uranium ore, radiated walls | Radiant fungus, mutant vinelash, caustic rocks | Rad-skitter, mutant rat, geiger bug, glow-stalker, rad-leech | Uranium ore, caustic shards. Reactor-core hearts and rad altars |
| Blight: Overgrowth | Invasive root blight | Overgrown soil, mossy rock, root-choked sand, vine-veined ice, overgrowth walls | Root growths | Spore rat, cave bat, moss slime, astro-beast | Viridium ore. Spore hearts and root altars |
| Fuel Sea (both edges) | A sea of amber fuel capping the world, reached over a long coastal shelf | The land eases down over a shelf (~4% of width); then shell beach and salt crust on the shore; shell sand, sand and coral reef on a sloped sea floor; coral walls | Seaweed, coral fans, shell piles, coastal hab pod, beacon tower. Tide grottos (flooded caves) under the sea floor | Swimmers (only in fuel or water): saltjaw shark, glow jelly, tide leech, silver fish, glow fish. Shore: reef crawler, brine skitterer, spitting barnacle, shell crab, beach skitter, little crab, drowned diver, brine golem, coral sentinel. Air: storm ray, sea bird. Families: dive kites | Sunken chests (coral, tide fish, harpoon gun). Fuel-sea fishing: reef snapper and tide fish, slick eel, coastal crates, coral wand, coral trident, coral pendant. Chests gain coral shards |
| Irradiated Core (bottom band) | A severely irradiated core flooded with liquid uranium | Cracked core crust (ceiling and floor skin), irradiated core rock, vitrified core glass, glowing uranium veins, pyrocite ore. Liquid uranium lakes and falls. Irradiated core walls, lead-lined ruin walls | Core-crust stalactites and stalagmites, isotope canisters (core pots). Planned art: uranium crystals, rad lichen, glow reeds, core vents | Irradiated husk and fission wisp (new natives), glow-stalker, rad-leech, rad-skitter, geiger bug. Families: tunnel wyrm, rift caster | Reactor ruins: reactor forge (Power Forge Mk2 station), lead-lined reactor chest (pyrocite bars, vitrified shards, uranium, rad filter mask). Uranium fishing: glowscale koi, vitrified shards, reactor crates, core wader boots, fission rod. Drops: pyrocite, vitrified shards, uranium. Chests gain uranium |
| Sky islands | Floating regolith islets | Regolith, moss, basalt | Trees, island houses | Surface roster, sky-rays and sky hawks | Sky chests (glider wings, prism shards, ancient power cells) |
| Precursor dungeon | Sealed precursor fortress | Precursor dungeon plating, dungeon walls | Dungeon jars | Dungeon roster: void wisp, glyph drone, alabaster hoplite, vengeful wisp. The Dungeon Guardian wakes before the gate boss falls | Dungeon strongboxes (dynamite, precursor relic, lumenite, void essence) |
| Precursor temple (vault) | Glyph-carved precursor maze | Precursor brick and glyphs, vault doors, precursor walls | Precursor statues, energy pylons, glyph tablets, vault altar | Vault sentinel, glyph drone, ancient guardian, alabaster hoplite | Locked vault chests (vault key) |

## Theme audit fixes

**Irradiated Core** (replaces the old Molten Chasm)
- Blocks: ember rock, ash and obsidian are replaced by new tiles: irradiated core rock, cracked core crust and vitrified core glass.
- Uranium veins are threaded through the band.
- The band is now one open cavern with:
  - a ragged ceiling;
  - flared rock pillars;
  - liquid-uranium lakes sunk into the floor;
  - uranium falls.
- Reactor ruins replace the slag foundries. They use hazard plating, a lead-lined wall, a vitrified floor, the reactor forge, a reactor chest and isotope canisters.
- Water and liquid uranium now harden into vitrified glass instead of obsidian. Uranium with acid or nectar leaves core crust.

**Core creatures**
- The core roster used fire creatures (pyro-skitter, magma turtle, lava-leech, ember rat, basalt golem).
- It now holds radiation creatures only. Two new natives were kitbashed: the irradiated husk and the fission wisp.
- The fire creatures' unique drops moved to core creatures:
  - fission brand (was magma brand) and furnace sentry kit: irradiated husk;
  - isotope flail: rad-leech;
  - reactor heart: fission wisp.

**Fuel seas** (replace the acid coasts)
- Seas of fuel (a new liquid, `LiquidKind.Fuel`) with sand beaches. The sea body is ~5.5% of world width on each side.
- Inland of each sea, a coastal shelf of ~4% of world width eases the land down to the shore. Mountains, valleys and scrap dunes stay inland, and the frozen and junk biomes run down to the beach.
- Fuel flows, settles and is swum, fished and scooped like water ("Fuel bucket"). Liquid uranium burns fuel into char slag; fuel and water do not mix. Inland lakes stay water.
- Tide grottos and sunken chests under the seas hold fuel.
- The ocean surface no longer ices over in the frozen west.
- The ocean biome is only read inside the edge bands, so inland lakes keep their own biome.
- Swimmers only spawn inside water, and shore life never spawns underwater.
- Ocean fishing has its own catch pool. Acid survives only in inland pools, which have a small caustic pool.

**Generic spawns in special biomes**
- The precursor dungeon used the generic cavern rosters. It now has its own roster.
- Ambient AI-family spawns are themed in two biomes:
  - core: wyrms and casters;
  - ocean surface: dive kites only.

**Chest loot**
- Every generated chest gains a material from the biome it sits in. For example: frost crystals in ice chests, uranium in the core, coral by the sea.

**Names**

| Old name | New name |
|---|---|
| Lava bucket | Liquid uranium canister |
| Lava wader boots | Core wader boots |
| Lava lure | Uranium lure |
| Magma rod | Fission rod |
| Ember koi | Glowscale koi |
| Obsidian crate | Reactor crate |
| Obsidian shard | Vitrified shard |
| Obsidian heat ward | Vitrified heat ward |
| Magma rock | Irradiated core rock |
| Ash | Core crust |
| Magma skin | Core-plated skin |
| Slag forge | Reactor forge |
| Slag-iron chest | Lead-lined reactor chest |
| Basalt urn | Shielded isotope canister |
| Ash stalactite | Core-crust stalactite |
| Acid shark | Saltjaw shark |
| Acid skitterer | Brine skitterer |
| Acid eel | Slick eel |

- The "lava" immunity tag still shields from liquid uranium.
- Contact posts a radiation-burn message and keeps the existing burn damage and Burning status.

**Accepted as is**
- Emberroot, a heat-loving herb, now grows on core crust and irradiated rock: the core is hot from decay heat.
- Pyrocite stays the core's progression ore.
