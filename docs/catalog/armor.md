# Armor catalogue: Terraria 1.4.5.8 → Rimtera

This catalogue maps every Terraria armor piece, set bonus, vanity item and dye to its Rimtera equivalent by role, tier
and function. Terraria is evidence only. Rimtera keeps its own names and numbers. The row-level data is in
[`armor.csv`](armor.csv), with 280 rows:

- 256 functional armor pieces, one row each;
- 397 vanity pieces in 10 group rows;
- 133 dyes in 4 group rows;
- 10 system rows: slots, dye slots, the set-bonus key and loadouts.

**Sources**
- Terraria: `docs/terraria-evidence/full/tables/armor.csv` (653 pieces), `armor_sets.csv` (64 set bonuses), `items.csv`.
- Terraria recipes: `scripts/recipes.py` (2,475 recipes).
- Terraria item sources: an IL scan of `members-ALL.json` for constant item ids next to drop, shop, bag and chest
  calls (`Chest.SetupShop`, `ItemDropDatabase.*`, `Player.OpenBossBag/OpenGoodieBag/OpenPresent`,
  `TryGettingDevArmor`, `GetAnglerReward_MainReward`, `SetupTravelShop`, `WorldGen.AddBuriedChest`).
- Rimtera: `dotnet run -- --item-census` and `--craft-audit --items` (build of 2026-10-28); `Systems/Content/ContentItems.cs`
  `Armor()`; `ArsenalItems.Data.cs` `ArmorRows`; `ItemSystem.cs` (legacy pieces); `CombatModel.cs` `ArmorSets`;
  `GameSimulation.Gear.cs`.

**Caveats**
- The IL scan finds the method but not the NPC. For drops, the NPC named in `how_obtained` comes from general knowledge
  where the scan could not resolve it.
- The scan also flags small item ids (37-153) in unrelated drop tables. Those hits were ignored.
- The Eskimo set is not in the decoded 1.4.5 recipes.

**`match` values**
- `exact-role`: same role, tier and function.
- `partial`: the role exists but the bonus or the source differs.
- `merged-into`: several Terraria items map to one Rimtera item or set.
- `missing`: no Rimtera equivalent.

## 1. Coverage

### 1.1 By category

| Category | Terraria items | exact-role | partial | merged-into | missing |
|---|---|---|---|---|---|
| Functional armor, all | **256** pieces / **64** set bonuses | 13 | 72 | 114 | 57 |
| · classless (wood, ore, Ninja, Gladiator, …) | 59 | 0 | 3 | 47 | 9 |
| · melee (Molten, Turtle, Beetle, Solar, DD2 Squire/Monk) | 35 | 3 | 16 | 4 | 12 |
| · ranged | 24 | 3 | 12 | 3 | 6 |
| · magic | 44 | 0 | 18 | 19 | 7 |
| · summon | 22 | 3 | 10 | 9 | 0 |
| · all-class (Shadow, Crimson, Palladium, Orichalcum, Titanium, Hallowed, Chlorophyte, Crystal Ninja) | 42 | 0 | 6 | 32 | 4 |
| · utility (Mining, Angler, Eskimo, single hats, 1.4.5 upgrades) | 30 | 4 | 7 | 0 | 19 |
| Vanity (social armor) | 397 | 0 | 0 | 92 (to Rimtera's 1 vanity hat) | 305 |
| Dyes (armor and hair) | 133 | 0 | 0 | 120 (to Rimtera's 8 dyes) | 13 (hair dyes) |

**Set level**
- 50 of the 64 Terraria set bonuses have a Rimtera set in the same role. Most are `partial`, because Rimtera's bonuses
  are passive stats.
- 14 sets have no equivalent:
  - Ash Wood, Pumpkin, Gladiator, Angler, Crystal Ninja and Spectre (healing);
  - the 8 Tavernkeep sentry sets (Squire, Monk, Huntress and Apprentice, at tiers 2 and 3).
- About 20 Terraria bonuses are active or proc abilities (§4). Rimtera has **none**: all 8 `SetSpecial` kinds are passive
  stats.

**Rimtera side**
- 152 armor pieces in 50 complete sets, plus 1 vanity item.
- Sets per tier:

  | Tier | T1 | T2 | T3 | T4 | T5 | T6 | T7 |
  |---|---|---|---|---|---|---|---|
  | Sets | 7 | 6 | 8 | 8 | 8 | 8 | 5 |

  T1 counts the trail set.
- Every class has a set at every tier from T1 (Melee, Ranged, Tech = magic, Command = summon). In Terraria, class sets
  only start around the T2-T3 biome sets.
- 147 of the 152 pieces are **crafted**. The 5 that are not:
  - Trailcoat: smash a Spore heart.
  - Trailboots: supply caches.
  - Rad-filter mask: a reactor chest.
  - Trailcap: crafted.
  - Insulated suit: crafted.

### 1.2 By tier (functional pieces)

| Terraria tier | Pieces | exact-role | partial | merged-into | missing | Rimtera tier and stage |
|---|---|---|---|---|---|---|
| Pre-hardmode (wood, ore, biome, evil, dungeon, Molten, Bee) | 130 | 10 | 39 | 60 | 21 | T1-T3; Power Forge (stage 4), Power Forge Mk2 (stage 6) |
| Early hardmode (Cobalt/Palladium, Mythril/Orichalcum, Frost, Forbidden, Spider, Crystal Ninja, DD2 T2) | 44 | 3 | 6 | 20 | 15 | T4 Alloy Anvil (stage 44), T5 Nano-Fabricator Mk2 (46) |
| Late hardmode (Adamantite/Titanium, Hallowed, Chlorophyte, Turtle/Beetle, Shroomite, Spectre, Spooky, Tiki, DD2 T3) | 69 | 0 | 15 | 34 | 20 | T6 Void Forge (stage 48) |
| Endgame (Solar, Vortex, Nebula, Stardust; Moon Lord legs) | 13 | 0 | 12 | 0 | 1 | T7 Astral Assembler (stage 71) |

**Where acquisition differs**
- Terraria crafts 189 of its 256 functional pieces. The other 67 are:
  - drops: King Slime, Queen Slime, marble Hoplite, Undead Miner, Shark and others;
  - shops: the Tavernkeep (24 pieces), Witch Doctor, Merchant, Clothier and Arms Dealer;
  - quest rewards: the Angler;
  - old-world variants (9 pieces).
- Rimtera has **no boss-dropped, shop-sold or quest-reward armor** (CA-5).

## 2. Upgrade and crafting loops

| Terraria loop | Terraria chain (evidence) | Rimtera chain | Status |
|---|---|---|---|
| **Starter wood set** | 20/30/25 wood @ Work Bench, 7 wood types, +1 def set | No equivalent. The first set (scav, ferrite-*) needs ferrite bars at the Power Forge (stage 4) | Missing step. The first armor arrives one station later (CA-9) |
| **Ore ladder** | Copper/Tin → Iron/Lead → Silver/Tungsten → Gold/Platinum: 12-30 bars @ Anvil, set +2/+3/+4 def | scav (T1 ferrite) → cobalt-trooper (T2 cobalt) at the Power Forge. No classless T3; class sets take over | `merged-into`. Each Terraria tier offers a choice of 2 ores; each Rimtera tier has 1 ore. Deliberate (one ore per tier, D-4) |
| **Biome and boss sets before hardmode** | Fossil, Jungle (spores), Meteor, Necro (dungeon bones), Shadow/Crimson (evil boss scales), Bee (wax), Obsidian (@Hellforge), Molten (hellstone) | T2-T3 class sets: ferrite-gunner, glacial-cryomancer, uranium-hazmat, glacial-ranger, meteorite-striker, glacial-handler, uranium-swarmlord, pyrocite-bulwark. All crafted from tier bars at the Power Forge or Power Forge Mk2. The themed material is used only by hive-sentinel, canopy-stalker, spore-mystic, coral-tidecaller (T4) and solar-adept (T5) | `partial`. Rimtera sets are bar-generic, so few T1-T3 sets need a biome or boss drop. Terraria's need for biome materials and boss drops before armor is mostly lost |
| **Hardmode ore sets** | Cobalt/Palladium @ Anvil → Mythril/Orichalcum @ Mythril Anvil → Adamantite/Titanium. 3 class heads (4 for Hallowed) share one body and legs | viridium-* (T4 @ Alloy Anvil) → lumenite-* (T5 @ Nano-Fabricator Mk2) → voidsteel-* (T6 @ Void Forge). One set per class, three pieces each | `merged-into`. Shared bodies are a deliberate difference (§5) |
| **Mech boss → Hallowed / Chlorophyte** | Hallowed bars (mech souls) and Chlorophyte ore (post-mech jungle) @ Mythril Anvil. Ancient Hallowed (1.4.4) @ Demon Altar | Folded into voidsteel-* (T6). Rimtera has no separate post-trio tier | `merged-into` |
| **Turtle → Beetle upgrade** | Turtle (Chlorophyte + Turtle Shell) + Beetle Husk (Golem) = Beetle Shell or Scale Mail | **None.** No Rimtera recipe takes an armor piece as an ingredient | Missing (CA-7) |
| **Mining and Angler → 1.4.5 upgraded sets** | Mining or Angler piece + 10 Mythril bars @ Mythril Anvil (new in 1.4.5) | None. scrap-salvager (mining:10) is crafted directly. No fishing set | Missing (CA-6, CA-7) |
| **Eskimo → Pink Eskimo** | Eskimo piece + 3 Pink Thread @ Work Bench | None | Missing (cosmetic) |
| **Gem robes** | Robe + 10 gems @ Loom; pairs with Magic Hat or Wizard Hat | None. Tech sets are full 3-piece sets | `merged-into` ferrite-tinker / cobalt-arcanist |
| **Post-Plantera bars** | Shroomite (mushroom + Chlorophyte @ Autohammer), Spectre (Ectoplasm @ Adamantite Forge), Spooky (Pumpkin Moon wood @ Work Bench) | Folded into T6: dreadnought-corsair, rift-oracle and mothership-overseer, crafted from voidsteel and essences @ Void Forge | `partial` |
| **Sentry sets (Old One's Army)** | Defender Medals from the event → the Tavernkeep shop. Tier 2 after a mech boss, tier 3 after Golem | None. The command sets add +1 sentry slot from T3/T4 | Missing (CA-5) |
| **Lunar** | Lunar fragments (pillars) + Luminite bars @ Ancient Manipulator | astral-* (T7 @ Astral Assembler). Astral ore comes from the obelisk sieges, the pillar analog | `partial`. Same loop, but the set abilities are missing (CA-1) |
| **Vanity** | Clothier, Merchant, Dye Trader, Tax Collector, Traveling Merchant, Goodie Bag, Present, boss bags (masks, dev sets), Angler, world chests, Loom crafts | The Tailor sells 1 head vanity. No masks, costumes, bags or crafted vanity | Missing (CA-4). Volume is deferred past 0.2.0 (X-TG-2) |
| **Dyes** | Dye materials (plants, strange plants) + bottle @ Dye Vat (75 colour dyes, 4 lunar). Strange plants traded to the Dye Trader for 41 shader dyes. Dye Vat sold by the Dye Trader | 8 flat-tint dyes and dye solvent, **bought** from the Pigment Trader. Dye petals are mined. The Dye Vat is bought or crafted (stage 21, +2 items). Dyes are not crafted from petals | `merged-into`. The loop is shallow (CA-10). X-CR-5 makes the Dye Vat shop-only in 0.1.13 |

## 3. Rimtera-only content (no Terraria analog)

| Rimtera item or set | Tier | What it does | Note |
|---|---|---|---|
| Trail set: `Trailcap` / `Trailcoat` / `Trailboots` ("Void-proof helmet", "Heavy astro-suit", "Scout boots") | early | Life regen, +8 max life, +8% move; set +2 def | The only set sourced partly from world loot (Spore heart, supply caches) |
| `RadFilterMask` (Rad-filter mask) | early | Poison and toxic immunity, head piece | Terraria grants these immunities only through accessories (Bezoar and others) |
| `InsulatedSuit` | early | Cold immunity, body piece | Closest to the Eskimo set (`partial`) |
| cobalt-vanguard, cobalt-sharpshooter, cobalt-conductor (T2); pyrocite-gunslinger (T3) | T2-T3 | Class sets at ore tiers where Terraria has only classless ore armor | A consequence of 4 classes at every tier |
| hive-sentinel (melee), canopy-stalker (ranged), spore-mystic (tech), coral-tidecaller (command, water-walk) | T4 | Biome-themed T4 class sets (comb, jungle root, glow spore, coral) | Terraria's biome sets sit before hardmode. Rimtera moves the theme to T4 |
| prism-knight, solar-adept, lumenite-handler | T5 | Extra T5 class sets (Endurance, BurstWhenHit) | |
| coral-diver | T2 | Classless, Endurance, gills on every piece | `partial` for the Diving Helmet |
| astral-paragon | T7 | Classless T7, Dodge 10%, +3% all-class on each piece | Terraria has no classless endgame set |
| The 8 passive `SetSpecial` kinds (Dodge, Thorns, HealOnHit, ChargeOnHit, BurstWhenHit, SpeedWhenLow, Endurance, Frenzy) | all | Conditional passive specials on the 27 arsenal sets | Rimtera's own design. Several stand in for Terraria procs (§4) |

## 4. Set abilities: Terraria vs Rimtera

| Terraria bonus (evidence: `Benefits.*`) | Kind | Rimtera stand-in |
|---|---|---|
| Solar Flare: solar shields, DR, dash, contact damage (`ApplySetBonus_Solar`) | active (set-bonus key) | astral-warden passive stats only |
| Stardust Guardian (`ApplySetBonus_Stardust`) | set minion | astral-handler drone slots only |
| Vortex stealth toggle (`setVortex`), Shroomite stand-still stealth (`shroomiteStealth`) | stealth mode | none |
| Nebula boosters (`setNebula`) | drops on hit | none |
| Forbidden storm (`setForbidden`) | active (set-bonus key) | storm-marshal SpeedWhenLow |
| Beetle Might / Beetle Endurance (`ApplySetBonus_BeetleDamage/Defense`) | orbiting stacks | Endurance / HealOnHit |
| Chlorophyte leaf crystal (`AddBuff 60`, `setChlorophyte`) | set turret | none |
| Spectre heal (`ghostHeal`) and splash (`ghostHurt`) | proc | rift-oracle ChargeOnHit. Healing is for colonists only (Decision 3) |
| Orichalcum petals, Titanium shards, Hallowed on-hit immunity, Palladium regen burst | on-hit procs | Dodge or HealOnHit as passive chances |
| Frost frostburn, Molten fire walk, Snow chill immunity, Gladiator knockback immunity, Ash Wood lava | status or immunity | none on sets (the tags exist only on accessories) |
| Meteor: Space Gun costs 0 mana | weapon synergy | none |
| DD2 tier 2/3: Ballista, Flameburst, Explosive Trap and Lightning Aura empowered | sentry empowerment | none |

## 5. Deliberate Rimtera design choices (do not "fix")

- **Settled decision 1**: mechanics follow Terraria by default. This is why the gaps below are treated as gaps.
- **Settled decision 3**: no free healing for the player. A Spectre-healing analog may heal colonists only
  (`0.1.14` IC-20 says so explicitly). The player-side heal is never added.
- **D-56**: armor visuals are one anchor-based worn sprite per piece for all 152 pieces in 0.2.0. Per-frame overlays are
  deferred (X-PE-1).
- **X-TG-2**: vanity volume (and furniture, mounts and so on) is **deferred past 0.2.0**. This is volume, not systems.
  The vanity *function* (slots, any piece in social slots, dyes per slot) is in scope; the 397-item catalogue is not.
- **X-CR-5**: the Dye Vat becomes shop-only (Pigment Trader) in 0.1.13.
- **D-4**: one ore per tier and no fourth pre-hardmode ore tier. This explains why the 2-ore choice per tier is
  `merged-into`.
- **Tech uses Charge, not mana.** Rimtera's Tech sets give max charge and charge regen instead of mana-cost cuts
  (GEAR_DIFF_ITEMS §4.2, ◐). CA-8 adds the cost-cut stat inside the Charge model. It does not add mana.
- **One set per class, three pieces each, from T1.** Terraria's shared bodies with class heads are folded on purpose.
  The equivalent choice is to pick a set.
- **Defense curve**: hardmode set defense is Terraria × 0.75, and class sets scale 0.72 / 0.65 / 0.55
  (`GearCurve.ArmorPieces`; GEAR_DIFF_ITEMS §4.1). This is balance, not a gap.

## 6. Gaps that matter, as integration items

Ordered by gameplay weight.

### CA-1 · Active set abilities for T5-T7 (set-bonus key)

- **Change**
  - Add Terraria's set-bonus input: double-tap down, rebindable.
  - Add a `SetAbility` field next to `SetSpecial` on `ArmorRow` / `ArmorSetBonus`.
  - Implement four abilities:
    - Solar-style charge shield with dash on astral-warden;
    - a guardian drone that follows and strikes on astral-handler;
    - toggle stealth (more ranged damage, less aggro, slower move) on astral-marksman, plus stand-still stealth on
      dreadnought-corsair;
    - booster pickups on astral-sage.
  - Then add a storm at the cursor for storm-marshal (the Forbidden analog).
- **Files**
  - `Systems/Content/ArsenalItems.cs` (enum and rows), `ArsenalItems.Data.cs`, `Systems/Content/ContentItems.cs`
    `Armor()` (the astral-* sets);
  - `Systems/Content/CombatModel.cs` (`ArmorSetBonus`), `Systems/GameSimulation.Gear.cs`;
  - `UI/GraphicalSandbox.cs` (input; the double-tap dash code is the model), `UI/PreferencesAndProfiles.cs` (binding).
- **Size:** L. Planned as 0.1.14 IC-20 / X-CP-4. This catalogue adds stealth and boosters to that list.
- **Acceptance**
  - One self-test per ability.
  - Double-tapping down in full astral-warden blocks the next hit and dashes.
  - Full astral-handler spawns exactly one guardian, which disappears when a piece is removed.
  - Stealth lowers aggro (`GearAggro`) while active.

### CA-2 · On-hit proc and status set bonuses

- **Change:** add proc kinds to `SetSpecial`, append-only:
  - `ProjectileOnHit` (petals or shards; the Orichalcum and Titanium analogs);
  - `ImmunityOnHit` (Hallowed);
  - `StatusOnHit` (frostburn; Frost);
  - `SetTurret` (a leaf-crystal-like turret that fires at nearby foes; Chlorophyte);
  - `SplashOnHit` (Spectre).

  Assign them to the T4-T6 sets that now merge those Terraria sets (viridium-*, lumenite-*, voidsteel-*,
  crystal-deadeye, rift-oracle).
- **Files:** `Systems/Content/ArsenalItems.cs`, `ArsenalItems.Data.cs`, `ContentItems.cs` `Armor()`,
  `Systems/GameSimulation.Gear.cs` (hit hooks next to HealOnHit and ChargeOnHit), `Systems/ItemAudit.cs` (known tags).
- **Size:** M.
- **Acceptance**
  - Each new kind has a test: a hit while wearing the set produces the proc, and no proc without the set.
  - `--item-audit` still reports "Equipment tags no system reads: 0".

### CA-3 · Social slots take any armor piece

- **Change:** the vanity slots accept every head, body or legs piece, not only `IsVanity` items. A piece in a social
  slot gives no defense or bonus.
- **Files:** `Systems/PlayerProgression.cs` (the `Valid(...)` check ~L125 and the equip check ~L434),
  `Systems/GameSimulation.EquipmentUi.cs`, `UI/InventoryUi.cs`.
- **Size:** S. Fits 0.1.14 PU-19 and PE-3.
- **Acceptance**
  - Equipping a functional T7 helmet in the vanity head slot leaves defense, set bonus and `CombatStats` unchanged.
  - The worn sprite (PE-3) shows the vanity piece.

### CA-4 · Vanity sources: boss masks and costume bags

- **Change:** a minimal vanity line per Terraria source, with original names:
  - one mask per progression boss (33 bosses; a mask drops 1/7 and comes in treasure bags);
  - 4-6 Tailor shop sets;
  - a seasonal bag (Festival or Aurora Night analog of the Goodie Bag and Present) with 3-piece costumes;
  - a rare "crew legacy" set in boss bags (the dev-set analog).
- **Files:** `Systems/ItemSystem.cs` / a new `Systems/Content/VanityItems.cs`, `Systems/NpcDropRegistry.cs`,
  `Systems/TownNpcs.cs` (Tailor stock), the art queue.
- **Size:** L, mostly art.
- **Acceptance**
  - `--item-census` shows vanity ≥ 40.
  - Every boss in `--craft-audit` lists a mask as a drop.
- **Deferred past 0.2.0 by X-TG-2.** Do the boss masks first if anything ships.

### CA-5 · Armor that is not crafted (boss drops, shops, event medals, quests)

- **Change**
  - A first-boss drop set with move speed (the Ninja analog).
  - A Gel Sovereign drop set with dash and +10% damage/crit (the Crystal Ninja analog).
  - A shop-sold command set (the Tiki analog).
  - An event-currency sentry line (the Old One's Army analog): 4 class-hybrid sets at T5 and T6 bought with a medal
    that drops in a defense event.
- **Files:** `Systems/Content/ArsenalItems.Data.cs` (rows with `Source = "drop:..."` / `"shop:..."`),
  `Systems/NpcDropRegistry.cs`, `Systems/TownNpcs.cs`, `Systems/WorldEvents.cs` (medal drops), `Systems/CraftingAudit.cs`.
- **Size:** M (L with the sentry line).
- **Acceptance**
  - `--craft-audit --items` lists ≥ 12 armor pieces whose source is not `craft`.
  - The Ninja analog is reachable at the first boss stage.

### CA-6 · Utility armor

- **Change**
  - A fishing set (fishing power, fewer spawns) as an Angler-quest reward. Today fishing quests yield 0 items.
  - A light-emitting mining helmet (the scrap-salvager mask gives light).
  - A night-vision helmet.
  - Make the Snow analog a full set (cold-immune head, body and legs).
  - A knockback-immunity set (the Gladiator analog).
  - A lava-contact reduction set (the Ash Wood analog).
- **Files:** `ArsenalItems.Data.cs`, `Systems/GameSimulation.Gear.cs` (light and night-vision tags already exist for
  accessories), `Systems/GameSimulation.Town.cs` or the fishing quest reward table.
- **Size:** M.
- **Acceptance**
  - Completing a fishing quest can award a fishing-set piece.
  - Wearing the mining helmet lights a radius in the dark (lighting test).
  - Each new immunity has a self-test.

### CA-7 · Armor upgrade recipes

- **Change:** recipes that consume an armor piece:
  - T6 tank set + boss material → T6+ defense/damage variants (the Beetle analog);
  - fishing and mining sets + T5 bar → upgraded versions (the 1.4.5 analog).
- **Files:** `Systems/Content/ArsenalRecipes.cs` (hint recipes), `ArsenalItems.Data.cs`.
- **Size:** S.
- **Acceptance**
  - The recipe audit shows each upgraded piece with its base piece as an ingredient.
  - `CraftingAuditSelfTests` still passes.

### CA-8 · Charge-cost reduction on Tech armor

- **Change:** add a `ChargeCostMultiplier` stat to pieces and sets, matching Terraria's mana-cost cuts (Jungle -16%,
  caster sets -14/-17/-19%, Spectre Mask, gem robes). Apply it in the charge spend path.
- **Files:** `Systems/ItemSystem.cs` (`ItemDefinition`), `Systems/Content/CombatModel.cs` (`ArmorSetBonus`,
  `CombatStats`), the Tech weapon use path in `Systems/GameSimulation.Combat.cs`.
- **Size:** S.
- **Acceptance**
  - A Tech weapon's charge cost drops by the set's percentage while the set is worn (self-test).
  - GEAR_DIFF_ITEMS §4.2 "Mana cost" becomes ✅.

### CA-9 · A first armor set before the Power Forge

- **Change:** a timber or hull-panel set at the Nano-Fabricator (the wood-armor analog): +1 def set, 1/1/0-1 pieces.
  This gives the opening (kit + one tree, stage 2) an armor craft.
- **Files:** `Systems/CraftingSystem.cs` or `Systems/Content/ContentRecipes.cs`, `ContentItems.cs` `Armor()`.
- **Size:** S.
- **Acceptance**
  - `--craft-audit` "kit + one tree" opening lists head, body and legs pieces.
  - The guide's "craft armor" tip (`GameSimulation.Guide.cs` ~L291) fires at stage 2.

### CA-10 · Dye depth

- **Change**
  - A dye slot per accessory slot (PU-19).
  - Dyes **crafted** at the Dye Vat from dye materials and petals; today they are only bought.
  - A strange-plant → Pigment Trader reward loop for shader dyes: animated, reflective, gradient.
  - Hair dyes at the Stylist analog.
- **Files:** `Systems/Content/PlayerSystemsContent.cs` (`DyeColors` → a dye table with shader kind),
  `Systems/GameSimulation.PlayerSystems.cs` (dye slots), `UI/GraphicalSandbox.PlayerSystems.cs` (`DyedLook` per piece),
  `Systems/TownNpcs.cs`.
- **Size:** M.
- **Acceptance**
  - Two accessories wear two different dyes.
  - A crafted dye recipe exists at the Dye Vat.
  - One animated dye changes tint over time in a render test.

### CA-11 · Visible worn armor

- **Change:** PE-3 as planned: anchor-based sprites per piece, vanity over armor, a dye per piece.
- **Files:** as in `docs/breakdown/09_presentation_engine.md` PE-3.
- **Size:** L.
- **Acceptance:** PE-3's own tests. Deliberate scope: D-56.

### CA-12 · Whip and weapon-synergy stats on sets

- **Change**
  - A whip-range and whip-speed stat on command sets (the Tiki, Obsidian and Stardust analogs).
  - A free-use synergy: one weapon costs no charge while its matching set is worn (the Meteor + Space Gun analog).
- **Files:** `Systems/Content/CombatModel.cs`, `Systems/GameSimulation.Gear.cs`, `ArsenalItems.Data.cs`.
- **Size:** S.
- **Acceptance**
  - A signal whip's reach grows by the set's percentage.
  - The synergy weapon spends 0 charge with the set and its normal cost without it.
