# Items, weapons and armor: Terraria vs Rimtera

Evidence (REA metadata of `Terraria.exe` 1.4.5, saved in `docs/terraria-evidence/items-*.json`):
- `ItemID`: 6,247 constants, about 5,400 real items once legacy and duplicate entries are dropped.
- `ItemID.Sets`, plus the other `Sets` nested types, give category flags: Yoyo, IsAWhip, IsAKite, IsDrill, IsChainsaw, IsFood, SummonerWeaponThatScalesWithAttackSpeed, StaffMinionSlotsRequired, BossBag, OpenableBag, ShootsOnUseRelease, LightPet, IsFishingCrate, Torches, CanGetPrefixes, and more.
- `ArmorIDs.Head/Body/Legs`: 294 / 205 / 233 equippable looks, vanity included.
- `ItemUseStyleID`: 17 use animations, including Swing, Thrust, Shoot, HoldUp, Rapier and the drink and eat styles.
- `AmmoID`: 18 ammo families, among them Arrow, Bullet, Rocket, Dart, Gel, Coin, Snowball, Flare, Sand and Solution.
- `PrefixID`: 98 prefixes across melee, ranged, magic, universal and accessory groups.
- `BuffID`: 402 buffs and debuffs.

Only names and structure are used. Every Rimtera item, stat, name and recipe is original. Item counts come from classifying `ItemID` names (`--item-census` prints Rimtera's side).

## Counts

> **Status (2026-10-21):** this document is the original diff. The count table and “Missing mechanics” list below predate the arsenal pass. The authoritative current state is “Status after the arsenal pass” at the end: every target was met and the catalogue is 1,459 items.

Terraria's figures are approximate, from name patterns.

| Category | Terraria | Rimtera now | Target |
|---|---|---|---|
| Broadswords / blades | ~55 | 32 melee, all one generic swing | 40 |
| Shortswords | ~10 | 0 | 10 |
| Spears / lances | ~15 | 0 | 15 |
| Yoyos | ~26 | 0 | 18 |
| Boomerangs / chakrams | ~10 | 0 | 10 |
| Flails | ~12 | 0 | 12 |
| Whips (summoner) | ~19 | a few signal whips | 15 |
| Bows / repeaters | ~35 | 24 ranged, all one generic shot | 30 |
| Guns / shotguns | ~36 | (in the 24) | 30 |
| Launchers | ~17 | 0 | 12 |
| Thrown | ~44 | a few | 30 |
| Magic staves / wands / tomes / beams | ~70 | 21 Tech | 60 |
| Summon staves / sentries | ~35 | 18 | 35 |
| Pickaxes / drills | ~40 | 9 | 25 |
| Axes / saws | ~34 | 7 cutters | 20 |
| Hammers | ~25 | 7 demolishers | 15 |
| Armor sets (combat) | ~60 sets | 24 sets | 50 sets |
| Accessories (non-wing) | ~200 | 36 | 150 |
| Wings | 44 | 1 | 30 |
| Accessory slots | 5 (+2 unlocks) | 3 → **now 5** | 5 |
| Ammo items | ~62 | 9 | 50 |
| Potions with buffs | ~60 | 11 (14 statuses) | 50 potions, ~45 statuses |
| Food | ~50 | 22 | 40 |
| Prefixes | 98 | 12 calibrations | 50 calibrations |

Furniture, blocks, banners and vanity, about 4,000 items in Terraria, belong to building and world content, not this pass.

## Missing mechanics

1. **Weapon styles:** Rimtera has one melee hit check and one projectile shot. `WeaponStyle` (new, in `ItemSystem.cs`) names 17 styles:
   - **Melee:** broadsword arc, shortsword stab, spear thrust, yoyo on a tether, returning boomerang, launched-and-reeled flail.
   - **Summoner:** whip lash that marks a target for bonus minion damage.
   - **Ranged:** bow, gun, shotgun spread, launcher with an explosive radius, thrown and consumed.
   - **Tech:** staff bolt, tome burst, continuous beam.
   - **Summons:** drone staff, sentry staff.
2. **Ammo families:** `AmmoClass` (new) on weapons (`UsesAmmo`) and ammo items (`AmmoKind`). A weapon fires the first ammo of its family in the inventory. Ammo adds damage and can change the projectile: piercing, homing, exploding, inflicting a status.
3. **Armor set bonuses** beyond extra defense: class damage, crit, drone and sentry slots, charge regen, and one special effect per set (dodge, thorns, healing on hit, burst on damage, bonus speed).
4. **Accessory effects:** the 12 existing tags cover jumps and flight. Missing: knockback immunity, fire-block immunity, lava immunity, night vision, auto-swing, extra reach, run speed, water walking, step-up, dodge, thorns, life on hit, charge on hit, ore detector, danger sense, plus combined accessories that merge two or three effects.
5. **Buffs:** ~45 statuses, so potions cover what Terraria's do: more regen, defense, damage per class, crit, speed, mining speed, light, fishing, water breathing, gravity, thorns, endurance, life force, ammo saving, plus well-fed tiers from food.
6. **Prefixes:** calibrations per class (melee size and speed, ranged velocity, tech charge cost, accessory defense, crit and speed), so reforging has about 50 outcomes.

## Implementation plan
- **Mechanics** (`GameSimulation.WeaponStyles.cs`): the 17 styles, ammo families, renderer-facing views of swings, tethers, chains, whip arcs and beams, plus self-tests.
- **Content** (`Content/ArsenalItems.cs` and friends):
  - A tier ladder × style generator for all weapons and tools, armor sets with bonuses, accessories and wings, ammo, potions, food and calibrations.
  - Every new item is obtainable through a recipe, loot, a shop or a drop.
  - Existing weapons get a `Style`.
- **Renderer and art:** drawing per style, kitbashed tier sprites, and an equipment panel with five accessory slots.

## Status after the arsenal pass
Every target above is met (`--item-census`). The catalog grew from 777 to 1,459 items: 682 new, including 317 new weapons, 118 of them themed uniques. The suite passed 233 checks at the time of this pass; the current suite is 550.

**Weapons**
- All 17 styles have their own attack logic (`GameSimulation.WeaponStyles.cs`), and every existing weapon now has a style.
- Ranged weapons fire any ammo of their family. Ammo can pierce, home, explode or apply a status.

**Gear and buffs**
- 50 armor sets, each with a set bonus and one special: dodge, thorns, heal or charge on hit, burst when hit, speed when low, endurance or frenzy.
- 150 accessories and 30 wings, with 20 new gear tags.
- 45 statuses, 50 potions, 40 foods and 51 class-specific calibrations.
- Five accessory slots, plus an equipment panel in the inventory: right-click an item to equip it, click a worn item to take it off.

**Art**
- `tools/sprites/arsenal.py` kitbashes all 682 new sprites from `docs/ARSENAL_SPRITES.txt`, tinted by material colour.
- Items that still have no art fall back at runtime to a tier-tinted base sprite for their style (`UI/ItemArt.cs`).

**Play-testing:** `--quickstart --kit arsenal` starts with one weapon of every style, ammo, a full armor set and wings.

**Defense and balance** (`Systems/Content/DefenseBalance.cs`, checked with `--balance`)
- **How defense works:** every creature and projectile hit loses half the player's total defense (three quarters on Brutal), but always does at least 1. Total defense is armor, accessories, buffs, set bonus and accessory calibrations. Endurance then reduces what's left. Damage over time and hazards ignore defense.
- **Enemy scaling:**
  - Creatures above 150 HP hit harder, by +35% per doubling of hit points, up to ×2.2.
  - Bosses scale by tier: Easy ×1.15, Hard and Event ×1.6, Final ×2.
  - Early creatures, the gate boss and the Dungeon Guardian keep their authored damage.
  - The result: each armor tier removes about 25–50% of its band's typical hit.
- **Accessory cap:** accessories give at most 6 defense each.
- **Calibrations:** worn accessories' calibrations now survive saves, loadout switches and character files.
