# Item audit

Every tool, weapon, accessory, armour piece, consumable, placeable and usable item must **do something** and have
**a sprite of its own**. `dotnet run -- --item-audit` checks this. `Systems/ItemAuditSelfTests.cs` (part of
`--self-test`) keeps it true.

## How the audit decides

`Systems/ItemAudit.cs` walks the whole `ItemCatalog`, including the dynamic ids for kill banners and the per-boss
cache, trophy, relic and core-chip.

**Function**, by use kind:

| Kind | Counts as working when |
|---|---|
| Weapon (melee, ranged, tech) | damage > 0 or a weapon style; a ranged weapon's ammo family has at least one ammo item |
| Command weapon | it summons a drone or sentry (`Summons`) |
| Drill, cutter, demolisher | tool power > 0 (bug net: always) |
| Placeable / wall | places a tile / a wall id |
| Ammo | a weapon fires its family, or a weapon names it as `AmmoType` |
| Food | restores food, or the live probe reaches a handler |
| Consumable, utility | the **live probe** reaches a handler (see below) |
| Critter | releases a creature or is bait |
| Armour / accessory | some stat, set bonus, set special, luck, aggro, dye or a tag that some system reads; a tag nothing reads fails the item |
| Vanity | has a slot |
| Material | used in a recipe, sold by a town NPC, has a credit value, is bait or a quest fish, or is a boss trophy or relic |

**Live probe.** Every consumable, food, utility and critter item is used once in a small live
`GameSimulation`. The fallthroughs where a use does nothing ("That item has no direct use." in `UseSelected` and
`ConsumeSelected`, and the end of `UseUtility`) now run through `NoDirectUse()`, which counts
`GameSimulation.UnhandledUses`. Between items, a plain material is used as a control. If that control stops reaching the
fallthrough, the probe starts a fresh game and tries the item again.

**Equipment tags.** A tag counts only if some system reads it:
- the numeric `key:value` tags summed by `GearTagValue`;
- the flags tested with `GearHasTag`, `ImmunityTags.Contains`, the movement layer, fishing, temperature and status
  blocking.

The lists are `ItemAudit.KnownFlagTags` and `ItemAudit.KnownNumericTags`. Add to them only when code reads the new tag.

**Sprite.** `SpriteCatalog.ForItem` must resolve to a png in `Assets/Sprites`, using the same sheet-prefix rule as
`SpriteAtlas.Resolve`. No other item may resolve to the same file. The tier-tinted `ItemArt` fallback does not count.

## Counts

|  | Before | After |
|---|---:|---:|
| Items audited | 1869 | 1869 |
| Items with no function | 6 | 0 |
| Items carrying a tag no system read | 11 (6 of them not among the 6 above) | 0 |
| Items without a sprite of their own | 427 (411 missing, 16 sharing) | 0 |

## What was fixed

### Functions

- **Info accessories** (life monitor, ore scanner, depth meter, compass chip; the astral compass has all four).
  - **Before:** their `*-readout` tags were read by nothing.
  - **Now:** `Systems/GameSimulation.InfoGear.cs` gives them Terraria's info-accessory behaviour. Like Terraria's
    1.4 info accessories, they work while worn or carried.
  - `GameSimulation.InfoReadouts` gives one line per item, for a HUD:
    - compass: metres east or west of the landing site;
    - depth meter: metres above or below the surface, and the layer;
    - ore scanner: the hardest ore within 20 tiles (the metal detector);
    - life monitor: creatures nearby and the strongest one (the lifeform analyzer).
  - The half-second pass also posts a message when the reading changes:
    - the depth meter when you enter a new layer;
    - the ore scanner when it finds a better ore;
    - the life monitor when a boss or a creature with 300+ HP comes within 40 tiles;
    - the compass every 250 m.
- **Braided fishing line** (Terraria's High Test Fishing Line).
  - **Before:** its `fishing-line` tag was unread.
  - **Now:** a hooked fish stays on the line twice as long before it gets away.
- **Companion permit.** Using it used to do nothing; the permit only filed itself while carried. Using it now files it
  at once, or says why it cannot.
- **Thorn plating.** Its bare `thorns` flag was unread. It is now `thorns:33`, so it reflects a third of contact damage,
  like Terraria's thorns.
- **`toxic` gear** (rad-filter mask, rebreather mask, filter implant, environmental suit, rad battery). Toxic fallout
  no longer adds radiation sickness to a wearer. Poison is still blocked by `poison`, as before.
- **Food.** A duplicated `GearOnAte(food)` call was removed. It was harmless, but it applied the meal's buff twice.

### Sprites

All new sprites come from recipes in `tools/sprites/kitbash.py`. The recipes recolour, crop, overlay, glow or scale
existing art. No existing sprite was overwritten and ComfyUI was not run. Two recipe operations were added:
- `crop`, a fractional crop;
- `fit`, an overlay scaled relative to the base sprite.

**Kill banners (257).** Each `item_banner_<creature>` is the kill banner with its cloth hue-shifted per creature and the
creature's sprite on it.

**Boss items (29 bosses × 4).**
- `item_salvage_cache_<boss>`: the cache, hue-shifted, with the boss on it.
- `item_trophy_<boss>`: the trophy banner with the boss.
- `item_relic_<boss>`: the boss as a gilded statue.
- `item_core_chip_<boss>`: a chip, hue-shifted, with the boss.

These are drawn because:
- `SpriteCatalog.ForItem` names dynamic ids (`DynamicItemSprite`);
- an existing sprite file now always wins over the renderer's tinted fallback.

`--item-audit --kitbash` prints the creature-sprite tables that the recipes loop over.

**Missing sprites, given kitbashes (34):**
- voidsteel reaver helmet, chestplate and greaves;
- moonglow, rainreed, frostbell, emberroot and sporecap herbs and seeds;
- plank and metal platforms, climbing rope, ladder, living wood, cactus chunk;
- shaping hammer, wire cutter, actuator, switch, lever, pressure plate;
- 1/3/5-second timers, OR/NOT/XOR gates;
- dynamite, sticky bomb, shrine blade.

**Mapped to existing art no item used (4):**
- moss clump → `terrain_moss`;
- precursor dungeon brick → `tile_dungeon_brick_precursor`;
- dart trap → `tile_dart_trap`;
- explosive trap → `tile_explosive_trap`.

**Shared pictures split (8 new):**

| Item | Shared with | Now |
|---|---|---|
| Hull panel | panel wall | `item_hull_panel` |
| Survey drill | ferrite drill | `tool_survey_drill` |
| Shock baton | energy cutlass | `weapon_shock_baton` |
| Energy cell | energy crystal | `ammo_energy_cell` |
| Crystal tonic | life core | `item_crystal_tonic` |
| Insulated suit | heavy astro-suit | `armor_insulated_suit` |
| Vitrified heat ward | vitrified shard | `item_heat_ward` |
| Rad-filter mask | void-proof helmet | `armor_rad_filter_mask` |

## Deliberate exceptions

None. `ItemAuditSelfTests.FunctionExceptions` and `SpriteExceptions` are empty. An item added there needs a written
reason.

Three tags are descriptive markers on items that already work through their stats: `wings` and `wing-climb` (wing
flight time; climbing is "not a `glide-rig`") and `fishing-lure` (the lure's fishing power). They are listed as known
tags.
