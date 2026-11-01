# Rimtera style guide (draft)

Drafted 2026-11-01 from the owner's four reference sheets (`docs/style/`). This draft is used by every asset request
(`docs/ASSET_REQUESTS.md`) from now on, and is settled in 0.10.1 (`docs/VISION.md` §4.7). The **boss sheet** is the
tie-breaker whenever two references disagree.

| Sheet | File | Shows |
|---|---|---|
| Boss & Event Expansion (the boss sheet) | `docs/style/sheet_bosses_events.jpg` (local original: `art/sheets/sheet_00c_bosses.jpg`) | Easy, hardmode and event bosses; the Astro-God |
| NPCs & Wardrobe Expansion | `docs/style/sheet_npcs_wardrobe.jpg` | Town NPCs, wardrobe pieces, face bases, modular expressions and features |
| Biome Expansion | `docs/style/sheet_biomes.jpg` | Frozen Wasteland, Molten Chasm, Radiated Deeps, Junk Wasteland, plus grassland |
| Core: weapons, tiles, creatures | `docs/style/sheet_core_weapons_tiles.jpg` | Guns, floor and wall tiles, doors, ores and bars, flora, early creatures |

## 1. The look in one line
**Crisp 8/16-bit pixel art of a sci-fi frontier:** riveted gunmetal machinery with cyan glow, living things that are
part-organic and part-machine, and amber-gold for anything that is a title or a reward. This is Terraria's readability,
in Neo-Terra's materials.

## 2. Pixel rules
- **Hand-placed pixels.** No anti-aliasing, blur or soft gradients. Light dithering only, on large surfaces.
- **A one-pixel dark outline** around every sprite: near-black blue-grey, not pure black.
- **Light from the top left.** Each material has 3–4 tones: a highlight, a base and one or two shades.
  - Metal also gets a hard specular glint.
  - Glow sources (cores, eyes, screens, crystals) get a bright centre and one halo tone. They have no soft bloom.
- **Consistent pixel scale** across a set. In the game that is 40 px per tile (`docs/COMFYUI.md`):
  - item icons are 64 px, and weapons and tools 96 px;
  - bosses are big, about 3–6 tiles, and read as one shape.
- **Readable silhouettes first.** Every sprite reads as its subject at game size, before any detail.

## 3. Palette

### 3.1 Core materials

| Material | Look | Where |
|---|---|---|
| **Gunmetal** | Cool blue-grey steel, darker seams, rivets and panel lines | Mechs, drones, turrets, stations, the UI frame |
| **Tech glow** | Cyan and teal; bright core, one halo | Screens, eyes, energy, the station AI hologram, door lights |
| **Amber-gold** | Warm yellow-orange | Titles, rarity, rewards, hazard stripes (with black) |
| **Void and astral** | Purple and magenta crystal, violet glow | The Astro-God, void gear, astral and endgame items |
| **Organic green** | Moss to olive; neon green only for radiation | Flora, mutants, the Plant Overgrowth, Mutated Flora |
| **Fire** | Orange core into red, yellow highlights | Solar Flare Entity, molten things, explosions |
| **Wood, leather, cloth** | Warm browns, muted reds | Rifle stocks, packs, coats, the Trade Consul's robe |

### 3.2 Biome palettes (from the Biome Expansion sheet)

| Biome | Palette |
|---|---|
| Frozen Wasteland | Cold blues and whites; ice is translucent with white streaks |
| Molten Chasm | Deep reds, oranges and yellows; dark cooled rock with glowing cracks |
| Radiated Deeps | Neon greens and purples; mutant flora |
| Junk Wasteland | Rusty browns and greys; scrap, girders, salvaged electronics |
| Grassland | Fresh greens, mossy stone, warm timber |

Other biomes follow the same rule: one dominant pair of hues, plus the shared materials. Their pairs are set in
`docs/BIOME_THEMES.md`.

## 4. Subjects

### 4.1 Creatures and bosses
- **Terraria's role, a sci-fi body.** Each creature fills a Terraria niche: a mechanical-organic animal, a robot, a
  mutant. Examples: Alpha Skitter (a spider-bug), Overseer Mech, Cyber-Swarm Leader (a beetle with guns), Rock-Mite,
  Spore-Rat, Neo-Terra Deer.
- **Machines:**
  - gunmetal bodies with panel seams;
  - cyan or green sensor eyes;
  - visible guns and pistons;
  - wear and damage marks on early, "damaged" or "rogue" machines.
- **Organics:** chunky, rounded shapes; glowing eyes; spines and crystals where the biome calls for them.
- **Bosses:**
  - one strong silhouette with a symmetric core;
  - glowing weak points (crystals, cores, eyes);
  - the Astro-God sets the endgame look: tentacles, many eyes, purple crystal and cyan orbs.

### 4.2 Town NPCs and people
- **Proportions:** chunky and slightly chibi, with a big head, about 2.5 heads tall, a readable face and the role's
  props. Examples: the Chief Engineer's goggles and wrenches; the Head Researcher's lab coat and tablet; the
  Astro-Botanist's plants.
- **The station AI** is a cyan hologram with a soft scanline glow.
- **Faces are modular** (the NPCs & Wardrobe sheet):
  - a base face;
  - skin and feature variations;
  - expressions;
  - add-ons: scars, a cybernetic eye, glasses, eye patches, stubble, beards.

  This is how people get their variety (0.5.1 backgrounds) and how injuries and **bionics** (0.5.3–0.5.4) show on them.
- **Wardrobe:** armour and clothing read as separate wearable pieces (helmet, suit, boots, gloves, packs). Equipped
  gear shows on people (0.4.0).

### 4.3 Items
- **Weapons:** side view, pointing right; 45° only for swords and spears. Gunmetal with coloured accents by tier or
  element.
- **Ores:** grey rock with coloured mineral flecks.
- **Bars:** stacked ingots.
- **Crystals:** faceted, with an inner glow.
- **Consumables:** vials and injectors in gunmetal casings, with a coloured serum and an amber-gold cap.

### 4.4 Tiles, walls and stations
- **Natural tiles:** seamless textured squares in the biome's palette, readable at 1×.
- **Built tiles:**
  - riveted steel floor plates and grates;
  - hazard-striped plates (amber and black);
  - vents;
  - wall panels, windows with glass, and cracked or damaged variants;
  - sliding doors with cyan light strips.
- **Stations:** gunmetal machines with screens and status lights, so each tier reads as a step up (more glow, more
  parts).

## 5. The UI frame
From the boss sheet:
- riveted gunmetal panels on a dark slate background;
- inset cells with corner rivets;
- an **amber-gold pixel title**;
- white pixel-capital labels;
- a faint CRT-screen frame.

Panels, slots, bars, buttons and the cursor follow it (0.10.4).

## 6. Sound (draft, to match the look)
- **Weapons and machines:** punchy, crunchy and synthetic: servo whine, metallic clank, energy zaps, chiptune-flavoured
  impacts.
- **Organics:** wet, chittering and growling, with a slight metallic edge on the mechanical-organic creatures.
- **Music:**
  - retro-synth layered with orchestral weight for bosses;
  - each biome takes its mood from its palette (cold and airy for Frozen, low and heavy for Molten, buzzing and uneasy
    for Radiated, clanky and rhythmic for Junk).
- **The station AI:** a clean, slightly vocoded voice, with interface blips.
- `docs/AUDIO_FLOW.md` holds the pipeline. The sound rules are settled with the owner in 0.10.5–0.10.6.

## 7. Using this guide
- **Every request** in `docs/ASSET_REQUESTS.md` names the sheet to match and the rules above it relies on (palette row,
  subject rules).
- **Prompts** keep the shared style block in `docs/ART_PROMPTS.md`, plus these lines:
  - "riveted gunmetal and cyan glow";
  - "amber-gold accents for rewards";
  - "Neo-Terra 8-bit pixel art, one-pixel dark outline, top-left light".
- **Checks:**
  - the "Sprite matches name" flow confirms the subject;
  - the owner confirms the style against the sheets;
  - 0.10.2 adds a style check (palette and outline) for every sprite.
