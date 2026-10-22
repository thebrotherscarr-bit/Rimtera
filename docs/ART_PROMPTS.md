# Neo-Terra art prompts (Nano Banana)

Each prompt below is one asset sheet. Paste the **shared style block** first, then the sheet's own text. Save results into the project folder as `sheet_<number>_<name>.png` (e.g. `sheet_06_melee.png`). The cutter (`tools/sprites/extract_sprites.py`) slices sheets by grid cell, so **cell order matters**: cells are listed left→right, top→bottom.

If a sheet comes back with items drifting out of their cells, regenerate rather than fix by hand — tell me which ones and I'll adjust the cutter.

---

## Shared style block (prepend to every prompt)

> Pixel-art game asset sheet for a sci-fi colony survival sandbox called NEO-TERRA, in the same style as a crisp 16-bit retro pixel art game: hand-placed pixels, 1-pixel dark outlines, limited saturated palette, soft light from the top-left, light dithering, no anti-aliasing, no blur. Image 1536×1024 pixels. Layout: an exact grid of 6 columns × 4 rows of equal square cells (256×256 each), separated by 4-pixel dark gutters. Background is ONE flat solid colour, dark slate #23282F, everywhere — no CRT frame, no monitor bezel, no screen curvature, no vignette, no gradient, no panel texture, no rivets, no scanlines. Each cell contains exactly ONE subject, centred in the upper 80% of the cell with clear empty space around it, never touching the cell edge or another subject. The bottom 20% of each cell holds the subject's name in small clean white pixel capitals. Keep a consistent pixel scale across the whole sheet. No title text, no watermark, no extra decorations.

Add for **tile sheets**: "Every subject is a perfectly square, seamlessly tileable 32×32-pixel block texture scaled up 6× with nearest-neighbour, filling a 192×192 area centred in its cell."
Add for **creature / NPC sheets**: "Characters and creatures are shown in side view facing right, full body, standing on nothing."
Add for **item sheets**: "Weapons and tools are shown horizontally pointing right, at 45° only for swords and spears."

---

## Sheet 01 — Terrain blocks I (natural)
Tile sheet. Cells: 1 Mud (dark wet brown), 2 Clay (rust-orange), 3 Gravel (grey pebbles), 4 Silt (pale grey-brown fine), 5 Salt Crust (white crystalline flakes), 6 Acid Sand (yellow-green sand), 7 Shell Sand (cream with tiny shells), 8 Coral Reef Block (teal and pink crystal coral), 9 Ash (dark grey with embers), 10 Slate (blue-grey layered stone), 11 Alabaster (white marble-like stone with grey veins), 12 Basalt Column (dark hexagonal stone ends), 13 Hive Comb (amber hexagon cells), 14 Nectar Block (glossy golden translucent), 15 Pearl Stone (pale lilac stone, pearly speckles), 16 Prismatic Grass (rainbow-tipped grass on pearl soil), 17 Glowshroom Grass (cyan glowing mushroom turf on dark soil), 18 Thin Ice (cracked translucent blue), 19 Meteorite (dark red-black stone with molten veins), 20 Mite Silk (grey web strands over rock), 21 Moss Red, 22 Moss Blue, 23 Moss Violet, 24 Moss Gold (each: mossy rock with that colour of glowing moss).

## Sheet 02 — Terrain blocks II (crafted)
Tile sheet. Cells: 1 Ferrite Brick, 2 Cobalt Brick, 3 Viridium Brick, 4 Lumenite Brick (pale gold-white glowing), 5 Voidsteel Brick (black-violet), 6 Astral Brick (deep blue with stars), 7 Basalt Brick, 8 Alabaster Brick, 9 Slate Brick, 10 Glass (clear pane), 11 Reinforced Glass (pane with metal frame), 12 Hull Plating Grey, 13 Hull Plating Red, 14 Hull Plating Blue, 15 Hull Plating Green, 16 Hull Plating Yellow-Black Hazard, 17 Astro-timber Planks (warm sci-fi wood), 18 Charred Planks, 19 Crystal Block (geode cyan), 20 Pearl Brick, 21 Hive Brick, 22 Coral Brick, 23 Circuit Panel (green circuitry), 24 Conveyor Belt Block.

## Sheet 03 — Background walls
Tile sheet; walls are darker and flatter than blocks, meant to sit behind tiles. Cells: 1 Regolith Wall, 2 Rock Wall, 3 Ice Wall, 4 Scrap Wall, 5 Mud Wall, 6 Hive Wall, 7 Fungal Wall (dark with faint glow dots), 8 Pearl Wall, 9 Radiated Wall (purple with green cracks), 10 Overgrowth Wall (vines over dark rock), 11 Obsidian Wall, 12 Coral Wall, 13 Plank Wall, 14 Plating Wall Grey, 15 Plating Wall Blue, 16 Glass Wall, 17 Brick Wall Ferrite, 18 Brick Wall Alabaster, 19 Lattice Fence, 20 Circuit Wall, 21 Derelict Hull Wall (rusted), 22 Precursor Wall (ancient gold glyphs), 23 Crystal Wall, 24 Basalt Wall.

## Sheet 04 — Furniture and lighting
Item sheet, 3/4 front view. Cells: 1 Plank Door closed, 2 Plank Door open, 3 Hull Door closed, 4 Hull Door open, 5 Trapdoor, 6 Plank Chair, 7 Hull Chair, 8 Plank Table, 9 Hull Table, 10 Bunk Bed, 11 Cryo Pod Bed, 12 Storage Crate (chest), 13 Locked Vault Chest, 14 Shelf with jars, 15 Glow Lamp Cyan, 16 Glow Lamp Amber, 17 Glow Lamp Violet, 18 Wall Torch (sci-fi flare), 19 Standing Floodlight, 20 Plant Pot with sprout, 21 Herb Planter, 22 Colony Banner, 23 Sign Post, 24 Painting of a planet.

## Sheet 05 — Crafting stations (tiers)
Item sheet, 3/4 front view. Cells: 1 Field Workbench, 2 Nano-Fabricator Mk II, 3 Ore Refinery Mk II, 4 Power Forge Mk II, 5 Alloy Anvil (Lumenite), 6 Void Forge (Voidsteel), 7 Astral Assembler, 8 Chem Lab (potion station, flasks), 9 Bio Lab (seed splicer), 10 Loom (cloth/armor), 11 Sawmill (timber), 12 Kiln, 13 Cooking Station, 14 Tinker Bench (accessory combiner), 15 Drone Bay, 16 Ammo Press, 17 Charge Reactor (tech weapons), 18 Purifier Station, 19 Research Terminal, 20 Water Recycler, 21 Generator, 22 Battery Bank, 23 Wiring Panel, 24 Teleporter Pad.

## Sheet 06 — Close Quarters weapons (melee)
Item sheet. Cells: 1 Ferrite Shiv, 2 Scrap Cleaver, 3 Cobalt Saber, 4 Frostbite Edge (ice blue), 5 Pyrocite Maul (molten hammer), 6 Uranium Glaive (glowing green), 7 Viridium Katana, 8 Lumenite Greatsword, 9 Voidsteel Scythe, 10 Astral Blade, 11 Shock Spear, 12 Plasma Pike, 13 Tether Whip (electric cable), 14 Chain Flail, 15 Throwing Disc, 16 Return Boomerang (sci-fi), 17 Yo-Yo Drone, 18 Power Fist, 19 Chainsaw Sword, 20 Riot Shield Bash, 21 Bone Club (wasteland), 22 Coral Trident, 23 Mushroom Mace, 24 Hive Stinger Rapier.

## Sheet 07 — Ballistics weapons and ammo (ranged)
Item sheet. Cells: 1 Scrap Pistol, 2 Ferrite Revolver, 3 Cobalt Carbine, 4 Pump Scattergun, 5 Glacial Crossbow, 6 Pyrocite Flamer, 7 Uranium Railgun, 8 Viridium Minigun, 9 Lumenite Longbow (energy bow), 10 Voidsteel Sniper, 11 Astral Cannon, 12 Harpoon Gun, 13 Dart Blower (wasteland), 14 Nail Gun, 15 Mortar Tube, 16 Bundle of Energy Cells, 17 Slug Rounds box, 18 Shotgun Shells, 19 Mini Rockets, 20 Crossbow Bolts, 21 Fuel Canister, 22 Explosive Charge, 23 Cryo Rounds, 24 Toxic Darts.

## Sheet 08 — Tech weapons (magic, uses Charge)
Item sheet. Cells: 1 Spark Emitter (wand), 2 Arc Glove, 3 Frost Projector, 4 Ember Rod, 5 Rad Beam Rod, 6 Prism Lens Staff, 7 Void Orb Focus, 8 Astral Conductor, 9 Datapad: Chain Lightning, 10 Datapad: Gravity Well, 11 Datapad: Swarm Missiles, 12 Datapad: Nova, 13 Laser Scalpel, 14 Tesla Coil Staff, 15 Plasma Lance, 16 Sonic Disruptor, 17 Spore Censer, 18 Coral Wand, 19 Crystal Shard Wand, 20 Meteor Caller, 21 Charge Cell (small), 22 Charge Cell (large), 23 Overcharge Capacitor, 24 Focus Lens Crystal.

## Sheet 09 — Command weapons (summoner)
Item sheet. Cells: 1 Scout Drone Remote, 2 Hornet Drone Remote, 3 Repair Drone Remote, 4 Frost Drone Remote, 5 Ember Drone Remote, 6 Rad Swarm Hive, 7 Lumenite Sprite Beacon, 8 Void Familiar Core, 9 Astral Guardian Core, 10 Turret Kit: Autogun, 11 Turret Kit: Flame, 12 Turret Kit: Tesla, 13 Turret Kit: Mortar, 14 Signal Whip: Mark, 15 Signal Whip: Overdrive, 16 Signal Whip: Chain, 17 Drone Spare Parts, 18 Drone Battery, 19 Command Visor, 20 Swarm Controller, 21 Mech Companion Key, 22 Spore Familiar, 23 Coral Jelly Familiar, 24 Hive Familiar.
The drones themselves (side view, small): add a second image "Sheet 09b" with the creatures 1–9 and 10–13 as deployed robots/turrets.

## Sheet 10 — Armor sets
Item sheet; each armor piece shown alone, front view. Cells by set (helmet, chestpiece, leggings): 1–3 Scav Set (patched leather and scrap), 4–6 Cobalt Trooper Set, 7–9 Glacial Ranger Set (white fur and blue plates), 10–12 Pyrocite Bulwark Set (molten heavy plates), 13–15 Uranium Hazmat Set (yellow-green rad suit), 16–18 Viridium Stalker Set (sleek green), 19–21 Lumenite Paladin Set (white-gold), 22–24 Voidsteel Wraith Set (black-violet).

## Sheet 11 — Accessories
Item sheet. Cells: 1 Jet Boots, 2 Rocket Pack, 3 Glider Wings (mechanical), 4 Grapple Hook, 5 Double-Jump Canister, 6 Climbing Claws, 7 Energy Shield Emitter, 8 Life Monitor Bracelet, 9 Night Vision Goggles, 10 Ore Scanner, 11 Depth Meter, 12 Compass Chip, 13 Magnet Ring (item pickup), 14 Regen Implant, 15 Rebreather Mask, 16 Lava Wader Boots, 17 Frost Grip Gloves, 18 Melee Module (red emblem), 19 Ballistics Module (green emblem), 20 Tech Module (blue emblem), 21 Command Module (orange emblem), 22 Lucky Charm, 23 Thorn Plating, 24 Astral Halo.

## Sheet 12 — Consumables and boss summons
Item sheet. Cells: 1 Med Stim (red injector), 2 Charge Stim (blue injector), 3 Regen Patch, 4 Speed Stim, 5 Armor Stim, 6 Night Owl Drops, 7 Spelunker Spray (gold glow), 8 Hunter Lens, 9 Featherfall Pill, 10 Water-Breath Capsule, 11 Ration Bar, 12 Grilled Snow-wolf Steak, 13 Mushroom Stew, 14 Nectar Tea, 15 Coral Sushi, 16 Nano-banana Split, 17 Summon: Swarm Pheromone (calls Alpha Skitter), 18 Summon: Faulty Mining Chip (Damaged Mining Drone), 19 Summon: Rotting Seed (Plant Overgrowth), 20 Summon: Scout Beacon (Scout Mech), 21 Summon: Reactor Key (Cyber-Swarm Leader), 22 Summon: Signal Core (Defense Protocol), 23 Summon: Pirate Transmitter, 24 Summon: Astral Sigil (Astro-God).

## Sheet 13 — Ores, bars, gems, materials
Item sheet. Cells: 1 Lumenite Ore, 2 Voidsteel Ore, 3 Astral Ore, 4 Meteorite Ore, 5 Ferrite Bar, 6 Cobalt Bar, 7 Glacial Bar, 8 Pyrocite Bar, 9 Uranium Rod, 10 Viridium Bar, 11 Lumenite Bar, 12 Voidsteel Bar, 13 Astral Bar, 14 Ruby Shard, 15 Sapphire Shard, 16 Emerald Shard, 17 Amethyst Shard, 18 Topaz Shard, 19 Diamond Shard, 20 Credits (stack of hexagonal coins), 21 Cloth Roll, 22 Circuit Board, 23 Power Cell, 24 Bio Gel.

## Sheet 14 — Tools and movement gear
Item sheet. Cells: 1 Ferrite Drill, 2 Cobalt Drill, 3 Glacial Drill, 4 Viridium Drill, 5 Lumenite Drill, 6 Voidsteel Drill, 7 Astral Drill, 8 Ferrite Cutter (axe), 9 Cobalt Cutter, 10 Viridium Cutter, 11 Lumenite Cutter, 12 Ferrite Demolisher (wall hammer), 13 Cobalt Demolisher, 14 Viridium Demolisher, 15 Lumenite Demolisher, 16 Paint Sprayer, 17 Wire Tool, 18 Bucket of Water, 19 Bucket of Lava, 20 Fishing Rod (sci-fi), 21 Bug Net, 22 Rope Coil, 23 Glow Stick bundle, 24 Teleport Beacon.

## Sheet 15 — Biome: Acid Coast
Mixed sheet. Cells 1–4 tile textures (rule: tileable): Acid Water surface, Salt Crust, Coral Reef, Shell Beach. Cells 5–10 flora/decor: Tube Coral, Kelp Strand, Salt Spire, Shell Pile, Tide Pool, Driftwood. Cells 11–14 critters: Shell Crab, Glow Jelly, Beach Skitter, Sea Bird. Cells 15–22 creatures: Acid Shark, Tide Leech, Reef Crawler, Brine Golem, Spitting Barnacle, Storm Ray, Drowned Diver (hostile scavenger in a broken diving suit), Coral Sentinel. Cells 23–24 structures: Coastal Hab Pod, Beacon Tower.

## Sheet 16 — Biome: Fungal Hollows
Mixed sheet. Cells 1–4 tiles: Glowshroom Grass, Mycelium Stone, Spore Block, Mushroom Cap Block. Cells 5–10 flora: Giant Glowshroom, Shelf Fungus, Spore Puffball, Glow Vine, Mycelium Tendrils, Bioluminescent Moss Clump. Cells 11–14 critters: Glow Snail, Spore Moth, Cave Frog, Truffle Mite. Cells 15–22 creatures: Spore Zombie (infected colonist), Fungal Crawler, Puff Drifter, Mycelid Shaman, Shroom Stalker, Cap Turtle, Lantern Bat, Spore Golem. Cells 23 Spore Monarch (mini-boss, large), 24 Fungal Hab Ruin.

## Sheet 17 — Biome: Crystal Spires (post-Fracture, bright and holy-looking)
Mixed sheet. Cells 1–4 tiles: Prismatic Grass, Pearl Stone, Crystal Spire Block, Pearl Sand. Cells 5–10 flora: Crystal Tree, Prism Flower, Light Reed, Singing Crystal, Halo Bush, Pearl Shell Plant. Cells 11–14 critters: Prism Butterfly, Light Wisp Bird, Crystal Bunny, Halo Snail. Cells 15–22 creatures: Prism Knight, Shard Unicorn (mechanical horse), Light Mimic, Crystal Wraith, Refractor Turret, Halo Bat, Pearl Golem, Spire Dragonfly. Cells 23 Prism Empress (boss), 24 Crystal Altar.

## Sheet 18 — Biome: Verdant Canopy and Bloom Hives
Mixed sheet. Cells 1–4 tiles: Canopy Grass, Mud, Hive Comb, Nectar. Cells 5–10 flora: Canopy Tree, Giant Leaf, Pitcher Plant, Thorn Bush, Bloom Flower, Hanging Vines. Cells 11–14 critters: Canopy Parrot, Leaf Frog, Pollen Bee (small robot-bee), Vine Snake. Cells 15–22 creatures: Pollen Drone Swarm, Thorn Lurker, Canopy Ape (mechanical-organic), Snapjaw Plant, Hive Guard, Mud Crawler, Spitting Orchid, Jungle Stalker. Cells 23 Bloom Matriarch (boss, giant hive-queen machine), 24 Precursor Vault Door (ancient gold door).

## Sheet 19 — Precursor Vault and mini-biomes
Mixed sheet. Cells 1–6 tiles: Precursor Brick, Precursor Glyph Block, Geode Crystal (cyan), Geode Crystal (magenta), Basalt Column top, Alabaster Pillar. Cells 7–12 decor: Precursor Statue, Glyph Tablet, Energy Pylon, Ancient Trap Dart, Geode Cluster, Mite Silk Cocoon. Cells 13–20 creatures: Vault Sentinel, Glyph Drone, Ancient Guardian, Geode Crab, Basalt Golem Mini, Alabaster Hoplite, Mite Broodmother, Silk Spider-Mite. Cells 21 Vault Colossus (boss), 22 Vault Key, 23 Ancient Power Cell, 24 Precursor Relic.

## Sheet 20 — Event enemies I (Rust Moon, Swarm Rain, Dust Storm, Acid Rain)
Creature sheet. Cells: 1 Husk (shambling rusted colonist), 2 Rust Wraith, 3 Blood-red Moth Swarm, 4 Rust Hound, 5 Eye Drone, 6 Corroded Mech, 7 Swarm Bug Falling (curled), 8 Swarm Bug Queen, 9 Dust Devil (whirlwind creature), 10 Sand Ray, 11 Scrap Elemental, 12 Dust Raider (masked scavenger), 13 Acid Slime, 14 Acid Bird, 15 Toxic Cloud Spirit, 16 Rust Moon Boss: Moon-Eater Mech, 17 Aurora Wisp (peaceful), 18 Festival Balloon Drone (friendly), 19 Meteor Head (flaming skull rock), 20 Meteor Crawler, 21 Eclipse Shade, 22 Eclipse Reaper, 23 Eclipse Mothron-like Moth Mech, 24 Eclipse Lord.

## Sheet 21 — Event enemies II (invasions)
Creature sheet. Cells: 1 Scavenger Grunt, 2 Scavenger Brute, 3 Scavenger Sniper, 4 Scavenger Rigger (builds turrets), 5 Scavenger Bike Rider, 6 Scavenger Warlord (invasion boss), 7 Pirate Deckhand, 8 Pirate Gunner, 9 Pirate Parrot Drone, 10 Pirate Cutlass Bot, 11 Pirate Cannon Turret, 12 Pirate Dreadnought (flying ship mini-boss), 13 Protocol Grunt (tower-defense enemy), 14 Protocol Shieldbearer, 15 Protocol Wyvern Drone, 16 Protocol Ogre Mech, 17 Protocol Dark Mage, 18 Protocol Betsy-style Dragon Mech (wave 3 boss), 19 Precursor Probe (saucer scout), 20 Probe Gunner, 21 Probe Tesla Walker, 22 Probe Brain Drone, 23 Mothership Core (invasion boss, wide), 24 Invasion Banner.

## Sheet 22 — Town NPCs and survivors
Creature sheet (people, side view, consistent with the colonist sprite scale). Cells: 1 Quartermaster, 2 Medic, 3 Armorer, 4 Demolitionist, 5 Tailor, 6 Cartographer, 7 Mechanic (wiring), 8 Tinkerer, 9 Pilot (trader ship), 10 Cook, 11 Arborist, 12 Archivist, 13–24 the same twelve NPCs shown **bound/trapped** (sitting, tied with glowing restraints) for rescue.

## Sheet 23 — Critters
Creature sheet, small animals. Cells: 1 Hop Bunny (sci-fi rabbit), 2 Glow Firefly, 3 Pebble Beetle, 4 Grass Snake, 5 Snow Hare, 6 Penguin Drone, 7 Scrap Rat, 8 Dune Lizard, 9 Ember Newt, 10 Cave Bat (small), 11 Worm, 12 Butterfly Blue, 13 Butterfly Gold, 14 Dragonfly, 15 Squirrel-Bot, 16 Owl, 17 Duck, 18 Fish Silver, 19 Fish Glow, 20 Crab Small, 21 Snail, 22 Ladybug-bot, 23 Frog, 24 Golden Critter (rare shiny).

## Sheet 24 — Colonist animation frames (most important for the animation pass)
Creature sheet, ONE colonist in an orange space suit with a clear helmet, identical design in every cell, side view facing right, same size and same feet line in every cell. Cells: 1–8 walk cycle frames 1–8 (contact, down, passing, up, repeating for the other leg), 9 idle stand, 10 idle breathe (chest slightly raised), 11 jump take-off, 12 jump rising, 13 falling, 14 landing crouch, 15 mining swing start (arm raised), 16 mining swing mid, 17 mining swing end, 18 melee slash start, 19 melee slash end, 20 aiming forward (arm extended, empty hand), 21 hurt (recoiling), 22 knocked down (lying), 23 climbing, 24 sitting.
Add: "Do not draw any held item; hands are empty so items can be layered in-game."
