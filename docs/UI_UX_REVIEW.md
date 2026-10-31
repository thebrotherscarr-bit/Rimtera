# UI/UX review: Rimtera against Terraria and RimWorld

Written 2026-10-31 against `main` at 0.1.7 plus the later merged work. It lists every screen and panel Rimtera has, then checks it against the player-facing UI of Terraria 1.4.4/1.4.5 and RimWorld 1.6. Status: **Have**, **Partial** (exists, but missing pieces) or **Missing**. The last section proposes how the two games' interfaces fit together; the plan is to settle that by iteration.

Terraria and RimWorld are references for what a player expects to find. Rimtera's layouts, art and words are its own.

## 1. What Rimtera has today

| Area | Screens and panels |
|---|---|
| **Title** | Title menu (Single Player, Settings, Exit) over a scrolling panorama; character select and world select as cards with Favorite / Rename / Delete; delete confirmation; loading, generating and saving screens with progress bars |
| **Character creation** | Name, difficulty, gender, age, appearance (skin, hair, hair colour, eyes, outfit) with a live preview, backstory, traits, skills and passions, focus, randomize, colonist sheet preview |
| **World creation** | Name, random name, size, difficulty, blight, seed, random seed, live preview |
| **Settings** | General (autosave, autopause, map, placement preview, item text, damage text), Interface (zoom, UI scale, health bars, guide hints), Video (window mode, resolution, frame skip, lighting), Volume (music, sound, ambient), Cursor (border, size, smart cursor), Controls (every action rebindable, defaults reset) |
| **In-game Settings menu** | Close, the settings groups, Save, Save & Exit to Title, Save & Quit |
| **HUD** | Hotbar (10) with item name; life, charge, food and rest bars; buff bar; status panel (biome, temperature, shelter, events); minimap, map overlay and full map with pylon travel; boss bar; event progress bars; message log; key-help hint; F10 frame breakdown; hide-UI key; damage numbers; enemy health bars; name tags on hover; smart-cursor and placement outlines; placement preview; wire overlay; guide hint line |
| **Inventory hub** | 50-slot grid with favorites; Sort, Quick stack, Cargo pod (bank), wire layer; trash slot; equipment (head, body, legs, accessories 1–6, vanity head/body/legs); loadouts 1–3; tabs for Gear, Housing, Crew, Bestiary, Feats; Settings button; click hints |
| **Crafting** | Crafting list for nearby stations, "close to" recipes, have/needed counts, station needed; recipe guide (used in / made from, browsable); station AI tips |
| **Containers** | Chest beside the inventory: Loot all, Deposit all, Quick stack, Restock, Sort, Rename |
| **Housing** | Check a room, valid/invalid reason |
| **People** | Colonist sheet: Overview, Skills, Health (body parts, capacities, vitals), Mood (needs, thoughts), Social (opinions), Crew |
| **NPCs** | Talk (dialogue, Shop, Sell held item, role actions such as healing, parties, quest fish; happiness and prices), Shop (stock and prices) |
| **World screens** | Bestiary (cards, unlock levels), Feats (achievements with toasts), Sign, Station AI screen, Creative powers (research, time, spawn rate, godmode, world difficulty, duplicate) |
| **Other** | Emote palette; death and respawn countdown; text prompts; key capture |

## 2. Against Terraria

### 2.1 Title and creation
| Item | Status | Notes |
|---|---|---|
| Title menu: Single Player, Settings, Exit | Have | |
| Title extras: Achievements, Credits, Workshop | Missing | Achievements and credits are cheap; Workshop does not apply |
| Character list: play, favorite, rename, delete, cloud | Partial | No cloud (does not apply) |
| Character creation: hair style and colour, eye colour, skin, clothing style | Partial | Rimtera has one outfit choice. Terraria colours each clothing piece |
| Difficulty on the character | Have | Explorer, Classic, Salvager, Ironman, Creative; Easy/Medium/Hard/Ironman per the design |
| World list: play, favorite, rename, delete | Have | |
| World creation: size, difficulty, evil (blight), seed | Have | |
| Secret and special seeds | Missing | Later; fits Rimtera's seeds |

### 2.2 Settings
| Item | Status | Notes |
|---|---|---|
| General, Interface, Video, Volume, Cursor, Controls | Have | |
| Language | Missing | English only for now |
| Resource packs | Missing | Not planned |
| Health and mana style | Missing | Rimtera's vitals panel is its own style; a choice of styles is optional |
| Map border style, minimap scale | Partial | Zoom keys exist; no border style |
| Smart cursor walls, lock-on toggle/hold | Partial | Smart cursor toggle/hold only |
| Cursor colour | Partial | Removed from the menu in the settings pass; the setting still exists |
| Waves, blood and gore, heat distortion, storm effects | Missing on purpose | Removed with the effects cleanup; effects always on |

### 2.3 Inventory and equipment
| Item | Status | Notes |
|---|---|---|
| Hotbar 10, inventory 50 | Have | |
| Coin slots (4) | Missing | Credits live in normal slots |
| Ammo slots (4) | Missing | Ammo lives in normal slots |
| Trash slot | Have | |
| Favorites, sort, quick stack, deposit, loot all, restock, rename | Have | |
| Armor 3, vanity 3 | Have | |
| Accessories 5–7 | Have | 5, with a 6th unlocked |
| Vanity accessories | Missing | |
| Dye slots (armor, accessories, misc) | Missing | Dye exists in player systems but has no equipment slots |
| Misc equips: pet, light pet, minecart, mount, grappling hook | Missing | Pets, mounts and hooks exist but are used from the hotbar |
| Defense readout next to the armor | Partial | Defense is on the HUD line, not by the gear |
| Loadouts 1–3 | Have | |
| Info accessory toggles (eye icons by the minimap) | Missing | Info gear exists (`GameSimulation.InfoGear.cs`) without the toggles |
| Item tooltips: damage, crit, speed, knockback, defense, value, prefix lines | Partial | Damage, drill power, food, defense, speed; no crit, knockback, value or prefix |
| Tooltip comparison with equipped gear | Missing | |

### 2.4 Crafting
| Item | Status | Notes |
|---|---|---|
| Craftable list for nearby stations | Have | |
| Recipe materials shown when selected | Have | |
| Guide crafting lookup (put an item in, see recipes) | Have | Recipe guide: used in / made from |
| Craft many by holding | Missing | |
| Recipe search or filter | Missing | Terraria has none; worth having |

### 2.5 Town and NPCs
| Item | Status | Notes |
|---|---|---|
| NPC chat: Shop, Happiness, role actions | Have | |
| Shop grid with buy, sell and buyback | Partial | Rimtera shows a list; sells only the held item; no buyback panel (the system has buyback code) |
| Reforge screen | Missing | On the roadmap (0.1.13) |
| Housing page: NPC heads, query, assign rooms | Partial | Check-a-room only; no NPC list or assigning |
| Pylon travel from the map | Have | |

### 2.6 HUD, map and world
| Item | Status | Notes |
|---|---|---|
| Life and mana styles | Partial | One style |
| Buff bar with time left and right-click to cancel | Partial | No right-click cancel |
| Minimap styles, full map | Have | |
| Map pins and markers | Missing | |
| Boss bar, invasion progress | Have | |
| Info accessory text (time, weather, depth, compass, DPS, enemies nearby) | Partial | Status panel covers biome, temperature and events |
| Item pickup text | Have | |
| Emote bubbles | Have | |
| Achievement popups | Have | |
| Smart cursor, placement preview, wiring view | Have | |
| Ruler and mechanical ruler | Missing | Optional builder aids |
| Camera / capture mode | Missing | Low priority |

### 2.7 Journey mode
| Item | Status | Notes |
|---|---|---|
| Research and duplication | Have | |
| Time, freeze time | Have | |
| Weather, wind, rain | Missing | |
| Enemy spawn rate, difficulty slider, godmode | Have | |
| Infection spread, placement range | Missing | |

### 2.8 Bestiary
| Item | Status | Notes |
|---|---|---|
| Entries unlock by seeing and killing | Have | |
| Filters (biome, type), sort, search | Missing | |
| Entry page: stats, drops with rates, flavour text, portrait | Partial | Drops seen and biomes only |

## 3. Against RimWorld

RimWorld's interface is built for a god's view of a colony; Rimtera is played through one character. Most of these are missing because the colony layer they serve is still to be built (see `docs/DESIGN_COLONY_ESCAPE.md` §4).

| RimWorld UI | Status | Notes |
|---|---|---|
| Colonist bar (portraits along the top, mood and state) | Missing | Wanted: the colony at a glance |
| Inspect pane (select a person or thing, see its tabs) | Partial | Colonist sheet; no click-to-inspect in the world |
| Colonist tabs: Needs, Health, Social, Character, Gear | Have | Overview, Skills, Health, Mood, Social |
| Work tab (priorities grid) | Missing | Core to skilled work |
| Schedule tab (sleep, work, recreation hours) | Missing | |
| Assign tab (outfits, food, drugs) | Missing | Later, if needed |
| Bills on stations | Missing | Core to station bills |
| Zones and stockpiles | Missing | Core to hauling and growing |
| Architect / build orders for colonists | Missing | Crew building from blueprints |
| Research tab | Missing | Ship parts and recipes |
| Alerts (stack on the right: starving, injured, idle colonist) | Missing | |
| Letters (incident notices with a "jump to" button) | Missing | The storyteller's warnings need these |
| History tab (story log, graphs) | Missing | Wanted: the narrative record of a run |
| Factions and relations | Missing | Traders, raiders, the zealots |
| Wildlife and animals tab | Missing | Pets and critters exist |
| Medical tab (who tends whom, priority) | Missing | |
| Mental break and inspiration notices | Missing | Breaks are a mood state only |
| Storyteller and difficulty pick at world creation | Missing | Decided: picked at world creation |
| Colony wealth readout | Missing | Drives threat points; worth showing |
| Game speed (pause, 1×–3×) | Missing on purpose | Rimtera is real-time like Terraria; sleep speeds time up |
| Quests tab | Missing | The Tide Warden's quest fish is the only quest |
| Ideoligion | Missing | Zealots are on hold |

## 4. Fitting the two together

These are starting proposals to try and refine.

1. **The inventory hub stays the home of everything.** Terraria keeps every panel behind one key; RimWorld puts its tabs on a bottom bar. Rimtera already has hub tabs (Gear, Housing, Crew, Bestiary, Feats). The colony tabs join them there: Work, Schedule, Research, History. One key opens them all, and the world keeps running behind them unless Autopause is on.
2. **The colonist bar is the one always-on colony element.** A thin row of crew portraits under the vitals panel: mood colour, injury and break markers, and a click to open that colonist's sheet. It is the colony at a glance without leaving the Terraria view.
3. **Letters and alerts sit on the right edge.** Storyteller incidents arrive as letters (a short title, the warning text, a "show me" that pans the map), and lasting problems show as alerts (a starving colonist, an undefended outpost). They are the storyteller's voice, and the queue's warnings need them.
4. **Orders come from the world, not from a god view.** You stand at a station to set its bills, paint a stockpile or grow zone with a zone tool held in the hotbar (like the housing check), and place build blueprints with the smart cursor. That keeps Terraria's "you are there" feel while giving RimWorld's control.
5. **Click to inspect.** Clicking a person, creature or station with an empty hand (or with a key held) opens a small inspect card. The full sheet is one more click.
6. **No game-speed buttons.** Time stays real-time, as in Terraria, with sleep to pass the night. The colony is managed while playing, not while paused.
7. **Terraria's missing basics fill in alongside.** Coin and ammo slots, misc equipment slots (pet, light pet, mount, hook) and dye slots, buff right-click cancel, tooltip comparison, the shop grid with buyback, the reforge screen, map pins, bestiary filters, and info accessory toggles.

## 5. Suggested order
1. **Terraria basics:** coin and ammo slots, misc equipment slots, buff cancel, tooltip comparison, the shop grid with buyback, the housing page with NPC heads. Small, and players notice them missing.
2. **The storyteller's voice:** letters and alerts, and the history log. These come with the storyteller work.
3. **The colony's controls:** the colonist bar, the work tab, station bills, zones. These come with the skilled work and hauling systems.
4. **Depth:** schedule, research (ship parts), inspect cards, bestiary filters, map pins, reforge, dyes, Journey mode's missing powers.
