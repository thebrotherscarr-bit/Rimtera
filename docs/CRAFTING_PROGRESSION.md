# Crafting progression: Terraria vs Rimtera

A review of the whole crafting ladder from a fresh character, with the fixes that followed. Terraria is behavioural and
structural evidence only: no names, code or assets come from it.

- **Audit:** `dotnet run -- --craft-audit` (add `--items` for every item's stage and source). Code: `Systems/CraftingAudit.cs`.
- **Self-test:** `Systems/CraftingAuditSelfTests.cs` (hooked into `CoreSelfTests`).
- **Terraria evidence:** `Recipe.SetupRecipes`, parsed by `docs/terraria-evidence/full/scripts/recipes.py` into 2,475 recipes
  (324 by hand). Item ids come from `tables/items.csv`, and tile ids from its `createTile` column. Station inheritance
  comes from `Recipe.SetupTileInheritance`, and the "near a station" box from `Player.AdjTiles`.
  `(knowledge)` marks facts that are not in the dumps.

## 1. The blocker and the opening

**Before.** The Nano-Fabricator (`ItemId.WorkbenchBlock`) was the only first station. Its one recipe took 10 hull panels
(`ItemId.WoodBlock`) by hand. No recipe made hull panels: they came only from breaking `TileId.Wood` or `ShipDoor`, that
is, from salvaging the derelict ship. Felling a tree gave `ItemId.Timber`. The old kit hid the gap because it carried 30
hull panels and a fabricator. With the Terraria-style kit (stiletto, drill, hatchet, food), the audit's opening check
found that **the kit plus a felled tree reached no station, and neither did the kit plus surface digging** (9 and 17
items).

**The design chosen.** Terraria's 10 Wood → Work Bench by hand is mirrored in two small recipes
(`Systems/CraftingSystem.cs`):

1. **The Nano-Fabricator takes 10 timber or 10 hull panels** by hand (an `IngredientRequirement` with alternatives).
   The first craft after felling a tree is the station, just as in Terraria.
2. **Hull panel ← 1 timber, by hand** (`hull-panel`). Hull panels stay the building material that 105
   recipes use (Terraria's Wood), and timber is the felled log that becomes them. Biome logs already hand-craft into
   timber 1:1, so every tree species feeds the whole chain. This plays the part of Terraria's `RecipeGroups.Wood` with one
   visible hand recipe, instead of editing every wood recipe to accept timber.

Why not let every hull-panel recipe accept timber? That would change 105 recipes and make the hull panel
pointless. The station AI's guide (owned by the guidance UI) can now follow a short chain: *Nano-Fabricator ← timber ←
chop a tree*, and *hull panel ← timber*.

**After.** Kit plus one tree: Nano-Fabricator, Field Workbench, hull panels, platforms, ladder, plank furniture, door
and sign (21 items). Add surface digging (ferrite ore, basalt, sand, crystals): torches, the Ore Refinery, the Power
Forge, ferrite bars and then the Kiln, Sawmill, Cooking Station, Chem Lab, Ammo Press, Tinker Bench and Dye Vat
(211 items).

## 2. Station ladder

| Terraria station (how it is obtained, evidence) | What it unlocks in Terraria | Rimtera equivalent (recipe) | Status / gap |
|---|---|---|---|
| **Work Bench**: 10 Wood by hand | 530 recipes: wooden gear (sword 7, bow 10, hammer 8 wood), walls, door, chair, table, chest, sign, Furnace, Anvil, Sawmill | **Nano-Fabricator**: 10 timber or hull panels, by hand (108 recipes). **Field Workbench**: 6 hull panels by hand (34 recipes). The fabricator counts as a Field Workbench | ✅ Fixed (was unreachable). The old kit's shock baton, laser rifle and survey drill are now fabricator recipes, like Terraria's wooden sword, bow and cactus pickaxe |
| **Furnace**: 20 stone + 4 wood + 3 torches @ Work Bench | Bars (3 ore → 1), glass, bottles, clay pots (78) | **Ore Refinery**: 20 basalt + 5 hull panels @ Fabricator (13 recipes: bars) | ✅ |
| **Iron/Lead Anvil**: 5 iron/lead bars @ Work Bench | Tools, weapons and armour from bars (275) | **Power Forge**: 12 basalt + 8 ferrite ore @ Fabricator (168 recipes) | ◐ Takes ore rather than bars (no refinery first). Same place in the ladder |
| **Sawmill**: 10 wood + 2 iron bars + 1 chain @ Work Bench | Furniture (166), Loom, Keg, Bookcase | **Sawmill**: 12 hull panels + 2 ferrite bars @ Field Workbench (10 recipes). The Arborist also sells it | ✅ |
| **Loom**: 12 wood @ Sawmill | Silk, cloth armour, banners (73) | **Loom**: 10 hull panels + 2 rope coils @ Field Workbench (7) | ◐ Built at the bench, not the sawmill |
| **Placed Bottle**: 2 per glass @ Furnace. **Alchemy Table** (no recipe; dungeon drop, knowledge) counts as Bottle | Potions (62) | **Chem Lab**: 6 glass + 4 ferrite bars @ Fabricator (68). **Bio Lab**: 8 glass + 4 cobalt + 5 bio-gel (6) | ✅ The Bio Lab counts as a Chem Lab, as Terraria's Alchemy Table counts as a Bottle |
| **Cooking Pot**: 10 iron bars + 2 wood @ Anvil. Counts as a Campfire | Food (68) | **Cooking Station**: 10 basalt + 3 ferrite bars @ Fabricator (30). The Cook also sells it | ✅ |
| **Hellforge**: no recipe, found in the Underworld (absent from `SetupRecipes`). Counts as a Furnace | Hellstone bars, obsidian armour (9), Adamantite Forge | **Power Forge Mk2**: 12 cobalt bars + Overseer circuit + Power Forge @ Power Forge, **and found** as the Reactor forge in Irradiated Core ruins (134 recipes). Counts as a Power Forge | ✅ Same shape: found in the underworld analog or crafted after the gate boss. The Reactor forge can be mined before the Overseer (audit: "Built Power Forge Mk2 via mine Reactor forge", stage 6), as the Hellforge needs no boss (PC-21) |
| (none) | | **Ore Refinery Mk2**: Ore Refinery + 2 heart fragments + 10 vitrified shards @ Power Forge Mk2. Counts as an Ore Refinery | Rimtera only (viridium and lumenite smelting) |
| **Mythril/Orichalcum Anvil**: 10 mythril / 12 orichalcum bars @ Anvil. Counts as an Anvil | Hardmode gear (200) | **Alloy Anvil**: 10 viridium bars + heart fragment + 3 fission slag (Fission Tyrant drop) @ Power Forge Mk2 (71). Counts as both forges | ✅ After the Wall-of-Flesh analog |
| **Adamantite/Titanium Forge**: 30 ore + Hellforge @ Mythril Anvil. Counts as a Hellforge, so also a Furnace | Adamantite, titanium, chlorophyte and spectre bars (6) | **Nano-Fabricator Mk2**: 12 lumenite bars + fabricator + 6 prism shards @ Alloy Anvil (74). **Void Forge**: 10 lumenite bars + 6 void essence + 20 vitrified shards @ Fabricator Mk2 (71), counts as both refineries | ✅ Built from the previous station, as Terraria's forge is built from the Hellforge |
| **Tinkerer's Workshop**: no recipe, sold by the Goblin Tinkerer after the goblin army (knowledge) | Accessory combinations (146) | **Tinker Bench**: 6 ferrite bars + 10 hull panels @ Fabricator (152) | ◐ Deliberately early (see §6) |
| **Ancient Manipulator**: no recipe, dropped by the Lunatic Cultist (knowledge) | Lunar gear (57) | **Astral Assembler**: 12 voidsteel bars + 6 astral ore @ Void Forge (55). Astral ore comes from the obelisk sieges after the Void Cantor (the cultist analog). Counts as every forge and Fabricator Mk2 | ✅ Fixed: astral crates no longer give astral ore before the obelisks fall, so the assembler can no longer come right after the Fracture |
| **Dye Vat**: no recipe, sold by the Dye Trader | Dyes (120) | **Dye Vat**: 10 hull panels + 2 alloy @ Fabricator. The Pigment Trader also sells it (9) | ◐ Craftable as well as sold |
| **Keg / Bookcase / Heavy Work Bench / Glass Kiln / Crystal Ball / Imbuing Station / Solidifier / theme benches** | Small sets | **Kiln** 25 basalt + 5 hull panels @ Field Workbench (17); **Drone Bay** (35), **Ammo Press** (54), **Charge Reactor** (1), **Research Terminal** (14) | ◐ The Kiln is the Glass Kiln analog and counts as an Ore Refinery |

Order check (self-test): Nano-Fabricator → Ore Refinery → Power Forge, all before the Fracture. Then the Alloy Anvil
after the Fission Tyrant, then Nano-Fabricator Mk2, then the Void Forge after the lumenite gate (drill power 85), then the
Astral Assembler after the Void Cantor.

## 3. Hand recipes

| | Terraria (324 by hand) | Rimtera (25 by hand) |
|---|---|---|
| First station | Work Bench ← 10 wood | Nano-Fabricator ← 10 timber / hull panels; Field Workbench ← 6 hull panels |
| Light | Torch ×3 ← gel + wood (34 torch recipes) | Wall torch ×4 ← timber or hull panel + ferrite ore; standing torch ← torch + timber |
| Platforms | Platform ×2 ← 1 wood (65 platform recipes) | Plank platform ×2 ← 1 timber |
| Rope | Rope is sold or found; rope coil ← 10 rope (knowledge) | Rope coil ← 4 plant fibre; climbing rope ×10 ← rope coil |
| Campfire | Campfire ← 10 wood + 5 torches | none by hand (`TileId.Campfire` is world decor; it drops timber) |
| Explosives | Sticky and bouncy bombs ← bomb + gel | Bomb ← 3 explosive charges + scrap-iron ore; sticky bomb ← bomb + bio-gel |
| Wood types | Every wood is in `RecipeGroups.Wood` | Every biome log → timber by hand. Timber → hull panel, astro-timber planks, ladder |
| Other | Bottled water/honey (near liquid), critters in bottles, flares, arrows | Acid sand ← silica sand **near acid** (new). Recall capsule, bio-gel, nano-bananas and glow spores from plants |

## 4. Wood → everything early

In Terraria, Wood appears in 131 recipes: 24 at the Work Bench, 24 at the Sawmill, 32 at the Anvil and 21 by hand. It
also builds the first three stations. Rimtera's equivalent is the hull panel, which 105 recipes use (timber itself feeds 8).
Timber now feeds it 1:1, so the Terraria pattern holds: chop trees → bench → furniture, walls, platforms, torches,
wooden-tier weapons → furnace and forge.

## 5. "Near a station" and stations that count as others

- **Range.** Terraria's `Player.AdjTiles` scans a box around the player. The default is about 4 tiles across and 3 up
  (knowledge), plus 4 more with the Artisan Loaf (`ateArtisanBread`, evidence). Rimtera
  (`GameSimulation.CreateCraftingEnvironment`) uses a 6-tile radius, plus the Fabricator manual's bonus (the
  Artisan Loaf analog).
- **Inheritance** (Terraria `Recipe.SetupTileInheritance`, evidence):

| Terraria: tile counts as | Rimtera: `CraftingStationNames.Implied` (World/TileGrid.cs) |
|---|---|
| Cooking Pot → Campfire; Furnace → Campfire | (no campfire station) |
| Glass Kiln → Furnace | Kiln → Ore Refinery |
| Hellforge → Furnace; Adamantite Forge → Hellforge | Void Forge → Ore Refinery and Ore Refinery Mk2 |
| Mythril Anvil → Anvil | Power Forge Mk2 → Power Forge; Alloy Anvil → Power Forge and Power Forge Mk2 |
| Alchemy Table → Bottle | Bio Lab → Chem Lab |
| Living Loom → Loom | (none) |
| (none) | Nano-Fabricator → Field Workbench; Fabricator Mk2 → Fabricator and Field Workbench; Astral Assembler → every forge and Fabricator Mk2 |

Inheritance is **transitive** (PC-18, 0.2.2), as Terraria's chain is (the Adamantite Forge counts as a Hellforge, so also as
a Furnace): `CraftingStationInheritance.ImpliedClosure` (`Systems/CraftingSystem.cs`) follows the links to the end. A lone
Astral Assembler counts as the Void Forge, so also as the Ore Refinery Mk2 and the Ore Refinery, and as Fabricator Mk2, so
also as the Nano-Fabricator and the Field Workbench. `GetNearbyStations`, the crafting guide and the audit all use it
(`ProgressionGateSelfTests`).

## 6. Audit results

| | Before | After |
|---|---|---|
| Kit + one felled tree reaches the first station | **no** (no station; 9 items) | yes (Fabricator, Field Workbench; 21 items) |
| Kit + surface digging | no station (17 items) | 11 stations, 211 items |
| Unreachable items | 8 (laser rifle, shock baton, survey drill, coral jelly familiar, acid sand, cyan and magenta geode blocks, prism moss) | 0 |
| Dead-end materials | 3 (fishing junk) | 0 (the junk is a listed deliberate dead end) |
| Stations nothing can build | 0 | 0 |
| Circular recipe chains | 0 | 0 |
| Recipes later than their tier | 0 (1 surfaced once lumenite leaks were closed: the teleport beacon, tier 1 but lumenite-built) | 0 |
| Blocked milestones | 0 | 0 |

The "before" column uses the final audit model on the content as it was before these fixes. An earlier, incomplete
model reported 383 unreachable items. Most were kill banners and seasonal or event sources the model did not yet cover,
not content gaps.

**Fixes** (all in `Systems/`):
- The opening: the fabricator takes timber, and timber makes hull panels by hand (`CraftingSystem.cs`).
- Fabricator recipes for the shock baton, laser rifle and survey drill; geode blocks from gem + basalt; acid sand by hand
  near acid (`Content/BatchAItems.cs`).
- Prism moss sold by the Astro-Botanist after the Fracture, as the Dryad sells hallowed seeds in hardmode (`TownNpcs.cs`).
- Coastal hab pods can hold the coral jelly familiar, which was loot-only with no source (`GameSimulation.Items.cs`).
- Gate leaks closed:
  - Pearl golems (lumenite ore, the drill-85 ore) moved to the post-Fracture crystal-spire roster only (`BiomeSpawnTables.cs`).
  - Dungeon chests give tier-3 pyrocite bars instead of lumenite bars, because the dungeon opens before the Fracture (`GameSimulation.Physics.cs`).
  - Crates hold lumenite, voidsteel and astral material only after the Fracture, and astral ore only after the obelisks
    (`Fishing.cs`, `GameSimulation.Fishing.cs`). This is Terraria's rule that crates give hardmode ores only in hardmode.
- The teleport beacon's tier is set to 5, matching its lumenite recipe and its vault-gated shop line (`Content/ContentItems.cs`).

**Deliberate exceptions** (`CraftingAudit.ExceptionFor`): 100 items, all difficulty-only boss rewards.
- 33 salvage caches (Terraria's Expert treasure bags).
- Their 33 core-chips (Expert-only items).
- 33 relics (Master relics).
- The auxiliary socket, the Fission Tyrant's Brutal+ drop (Terraria's Demon Heart).

**Deliberate dead ends:** rusted can, tangled wire and soggy datapad. These are fishing junk that is only sold, like
Terraria's tin can, seaweed and old shoe, which no recipe uses.

**Guide-taught recipes** (`RequiresGuideUnlock`, `_knownGuideRecipes`): unchanged. Every reveal material is also an
ingredient of its recipe, so the audit treats a guide recipe as known once its ingredients are in hand.

**Bosses before the Fracture are dated honestly (PC-7, 0.2.2).** The audit used to take milestones strictly by order, so
the Fission Tyrant's Fracture came before every optional boss of a higher order, and their loot was dated after the
Fracture even when nothing gated them. The run now holds the Fracture back until every other ready milestone has been
taken. `--craft-audit` prints **"Bosses reachable before the Fracture"** (life, tier, the stage it became fightable),
flags any boss above the gate boss's life, and marks each as intended or under review:
- **Intended** (`CraftingAudit.IntendedPreFractureBosses`, Terraria's pre-hardmode set): Overseer mech, Fallout
  Hive-mind, Damaged Mining Drone, Scout Mech, Alpha Skitter, Hoarfrost Behemoth, Plant Overgrowth, Spore Monarch.
- **Under review** (`CraftingAudit.PreFractureBossReview`, each with its reason): Moon Eater (PC-1), Protocol Dragon
  (PC-2; D-3 says it waits for the Vault Colossus), the dusk incursion bosses and their summons (Scavenger Warlord, Void
  Stalker, Cyber-Pirate Captain, Solar Flare Entity, Asteroid Miner Mech; PC-4) and the Pirate Dreadnought's flag (owner's
  call). None is above the Fission Tyrant's life since the 0.2.0 re-curve.

The whitelist and the tier 6+ count are checked on a second, **gated** run (`CraftingAudit.RunGated`) that holds the
bosses under review back until the Fracture, so what only they leak is printed apart ("Pre-Fracture only through a boss
under review": 37 items, among them the Asteroid Miner Mech's drill-power-107 drill and two tier-6 boss upgrades, the
nanite marrow and the servo grease) instead of being whitelisted. When a boss's gate lands, model it in `BossGateOpen`
and drop its review entry: `CraftingAuditSelfTests` fails on an entry that is no longer early.

**Tier 4+ before the Fracture: closed with a whitelist (X-CR-1, 0.2.0).** With the bosses under review held, the audit
finds 92 items of tier 4 or higher that a Classic character can obtain before the Fracture (98 in 0.2.0). They are all on
`CraftingAudit.EarlyGearWhitelist`, each with a reason, and `CraftingAuditSelfTests` fails if any other item turns up or
an entry is no longer early. `--craft-audit` prints the list with each reason, any item outside it, and any entry that
is no longer early.

| Group | Items | Reason | Permanent? |
|---|---|---|---|
| Tinker Bench combinations (35) | Arcanist's Circlet, Architect's Frame, Ballistics Emblem Mk II, Ballistics Module, Berserker Rig, Bio-weave, Bulwark Core, Close-quarters emblem Mk II, Command Emblem Mk II, Command Module, Drone Controller, Duelist Harness, Energy Shield Emitter, Environmental Suit, Excavation Suite (T5), Excavator Rig, Fortune Matrix, Gunner's Harness, Heart Pump, Inferno Treads (T5), Core-plated skin, Master Tackle Kit, Close-quarters module, Reinforced Armor Suit, Retribution Mesh (T5), Rocket Pack, Sanguine Reactor (T5), Servo Grip, Spike Lattice, Targeting Lens Mk II, Tech Emblem Mk II, Tech Module, Telescopic Arm, Vital Core (T5), Warden's Uplink | Combinations of pre-Fracture parts. In Terraria, hardmode combinations need hardmode parts. | No: until PC-16 gives each a tier-matched input |
| Ammo Press ammunition (11) | Cryo Rocket, Crystal Darts (T5), Flechette Rounds, Incendiary Rocket, Overcharged Cell, Plasma Fuel (T5), Prism Cell (T5), Seeker Bolts, Shard Bolts (T5), Viridium Bolts, Viridium Rounds | Pressed from pre-Fracture materials: viridium bars and the void essence that coastal hab pods hold. | No: until PC-6 re-tiers or re-gates them |
| Tonics and food (13) | Greater ballistics, close-quarters, command and tech tonics (T5), Heavy Fortify Stim, Hunter's Focus, Lifeforce Serum, Wrath Draught, Astro-burger, Colony Feast, Koi flambe, Prism Sorbet (T5), Void Noodles (T5) | Consumable buffs brewed or cooked from pre-Fracture ingredients. | No: until PC-6 |
| Optional boss summons without a gate (7) | Bounty Notice, Claim Marker, Dreadnought Flag, Rusted lure (Moon Eater), Protocol Key, Sunspot Lens, Void Lure | These bosses have no Fracture gate. Summoning one early is the player's risk. PC-1 (parity 0.1.7) meant to gate the Moon Eater and its lure behind the Fracture; that is not done. | Reviewed with the boss framework (parity 0.1.10) |
| Gated boss summons (2) | Mutagen Bloom, Tainted Tissue | The item can be made early, but the boss refuses until its gate (`HardmodeSummonRefusal`: the trio, the Fracture, the opened vault). | Yes |
| Defense Protocol (2) | Signal Core, Protocol Shard | The event's starter and its drop. The Defense Protocol is a pre-Fracture event (Terraria: the Old One's Army, tier 1). | Yes |
| Station (1) | Ore Refinery Mk2 | The viridium refinery, a pre-Fracture station (Terraria: the Hellforge). Its recipes keep their own tiers. | Yes |
| Fishing finds, crates and quest rewards (7) | Hive Familiar, Hive Stinger Rapier, Reactor crate, Core wader boots, Fission rod, Uranium lure, Royal Nectar | Terraria's fishing also gives high-tier gear before hardmode. Crates hold hardmode ores only after the Fracture. | Yes |
| Rare creature and event drops (8) | Aurora Ring, Canopy Longbow, Codex: Tidal surge, Hive Lash, Splice Needle, Sky-ray Wings, Storm Repeater, Wisp Staff | Rare drops, as Terraria's dungeon and jungle drops are before hardmode. | Yes |
| Crystal Spires drops (4) | Codex: Shard storm, Prismatic crate, Halo Gyre, Prism Ribbon | Their creatures also spawn in the Crystal Spires before the Fracture. | No: until PC-6 moves them to the post-Fracture roster |
| Town shops (2) | Mini Rocket (Demolitionist), Mortar turret kit (the Mechanic, after the hearts) | Sold behind the seller's own unlock. | Yes |

**Tier 6+ before the Fracture: 0 (0.2.2).** The six items 0.2.0 left early are gated in the game, not whitelisted:

| Item | Gate | Where |
|---|---|---|
| Astral crate (T6) | A legendary water catch bites only after the Fracture (an alloy crate before it). It also drops from the Abyss Leviathan and the Mothership Core and rewards the Precursor Probes, all post-Fracture | `FishingContext.Fractured`, `FishingTables.Pick` |
| Astral fishing rod (T7) | The Tide-Warden's 30-quest reward is paid on the first quest turned in at or after 30 once the Fracture has happened | `GameSimulation.TurnInQuestFish` |
| Phase dirk (T6) | The vengeful wisp's rare drop is marked `mob:VengefulWisp:5:fracture` and waits for the Fracture | `ArsenalItems.MobDropWaitsForFracture` |
| Vault core (T6) | The recipe adds 5 lumenite bars (post-Fracture) | `ContentRecipes.cs` |
| Void darts, void fuel (T6) | The recipes add 1 blight essence (post-Fracture) to the void essence | `ArsenalItems.Data.cs` |

The live run still finds two tier-6 items early, both late-boss upgrades of bosses under review (the Void Stalker's
nanite marrow, the Protocol Dragon's servo grease); the gated run finds none.

The "until PC-6" and "until PC-16" rows belong to parity steps 0.1.11 and 0.1.13, which `docs/PLAN.md` moved to
milestone 0.5.0 ("Pressure"). When those land, their rows leave the whitelist.

## 7. What the audit models (and does not)

- **World:** two generated 4200×1200 worlds, one radiated and one overgrowth. A few cells of every tile kind are broken for
  real (`GameSimulation.ForceBreakTile`), then again after the Fracture with altars smashed first. Each tile is gated by
  its minimum drill power. Trees need a cutter, hearts a demolisher, and altars the Fracture plus an altar breaker. The
  vault's chests need its key, and the dungeon's chests need its gate boss. Every container also contributes its whole
  loot table. Herbs, wild plants and sapling-grown trees count on any ground the world has.
- **Creatures:**
  - Guaranteed and arsenal drops, kill banners and Fracture essences.
  - Post-Fracture rosters wait for the Fracture, and event-only creatures wait for their event. Dungeon creatures wait
    for the gate boss, vault creatures for the key, and obelisks for the Void Cantor.
  - Boss loot waits for the boss. A boss is reachable by its summon item or its natural trigger (patrol, hearts,
    altars, dusk incursions, events, the vault altar, abyss bait after the Fracture), under the same gates as
    `HardmodeSummonRefusal`.
- **Other sources:**
  - Shops: each resident's move-in condition and each stock line's flag.
  - Fishing: every catch table, rod-gated liquids, quest fish and Tide-Warden rewards.
  - Crates, the sifter, the phase pool, audio decks, events (eligibility under clear, rain and snow, by day and by
    night) and their rewards.
- **Stages:** the run takes milestones in order: drill powers, stations built, world flags, events cleared and bosses
  defeated. The Fracture (the gate boss's defeat) is held back until no other milestone is ready (PC-7). Each item
  records the first milestone after which it became obtainable. A milestone that never becomes ready is reported as
  blocked, then forced so the items behind it are still listed. The drill-power gates raise an item's progress level
  (tiers 5 and 6) only when passed after the Fracture.
- **Two runs:** the live run (`CraftingAudit.Run`, what the game allows today; `--boss-balance` builds its kits from it)
  and the gated run (`RunGated`, the bosses under review held for the Fracture; the whitelist checks).
- **Limits:**
  - Spawn rarity, drop chance and difficulty of the fight are ignored. "Obtainable" means some route exists.
  - Recipe liquid and biome conditions are assumed satisfiable.
  - Survivors and colony-growth residents count as available from the start.
  - Terraria parsing skips a handful of recipes built inside loops.
