# 06 · Items and combat: Rimtera vs Terraria

Area: weapons (melee, ranged, Tech = magic, Command = summon), projectiles and ammo, tools as weapons, armor and set bonuses, accessories and mobility gear, potions, food, buffs and debuffs, calibrations (prefixes), damage, crit, defense, knockback and immunity-frame formulas, player hurt, death and respawn, stacks, rarity, value and selling, minions, sentries, whips, wings, hooks, item sprites and use animations.

Method: I read the code (pointers are `file:line` on `main` as of 2026-10-28; another agent is editing input/UI code, so UI line numbers may drift by a few lines). I ran `--item-census`, `--item-audit` and `--balance` in a private build, plus two scratch probes (catalog statistics; regen, damage-over-time, buff duration and respawn timer measured over real ticks). Terraria evidence: `docs/terraria-evidence/full/tables/player_stats.md` (v1.4.5.8 IL), `items.csv`, `prefixes.csv`, `armor_sets.csv`, `accessories.csv`. "(knowledge)" marks Terraria behaviour that comes from general knowledge, not from the IL dump. Terraria is behavioural evidence only.

Status key: ✅ matches · ◐ partial · ❌ missing · ⚠ broken/bug · ➕ Rimtera-only (deliberate)

---

## Summary

**Counts (131 feature rows):** ✅ 65 · ◐ 30 · ❌ 14 · ⚠ 14 · ➕ 8. Every row is in the feature tables below.

The formula core is in good shape. Outgoing damage, spread, crit, NPC defense, armor penetration, knockback caps, use-time and attack-speed caps, charge (mana) regeneration, natural life regeneration and the player's defense rule all match the IL. Content breadth is also close: 429 weapons across 17 styles (Terraria has 462), 50 armor sets (64), 220 accessories (511), 51 calibrations (97 prefixes). The audit reports 0 items without a function and 0 without a sprite.

The problems sit in the layers around that core.

**Top 10 issues by player impact**

| # | Issue | Why it matters | Step |
|---|---|---|---|
| 1 | **Only one healing item exists**: Med stim, 50 HP (`ContentItems.cs:309`). There is one charge-restore item. Terraria has 4 healing tiers (50/100/150/200), restoration potions and healing food, plus 4 mana tiers. | Late-game sustain does not exist. A 300-HP player facing 90-damage hits has a 50-HP potion every 45 s. **Blocks hardmode play.** | IC-2 |
| 2 | **Regeneration, damage over time and buff durations run on the game-hour clock** (`PlayerProgression.cs` StatusDefinition, `GameSimulation.cs:660`). Probe: the Regeneration buff adds ≈0.03 HP/s (Terraria 2 HP/s), Rejuvenation ≈0.07 HP/s, Poison ≈0.12 HP/s (Terraria 2), Burning ≈0.3 HP/s (Terraria 4). A 4 h buff lasts 50 real seconds (Terraria buff potions last 2–8 min). | Regen potions and regen gear do almost nothing; buff potions expire within a minute; debuffs barely hurt. | IC-1 |
| 3 | **Creatures inflict no debuffs on the player.** Only lava (Burning) and toxic fallout (Poison) apply statuses (`GameSimulation.Player.cs` lava, `GameSimulation.cs:1980`). | A whole Terraria danger layer is missing: poison, fire, bleeding, chill, confusion, darkness, weakness, broken armor. So debuff-immunity accessories have nothing to block. | IC-3 |
| 4 | **The RimWorld body layer runs on the player.** Each hit wounds a body part (`Actors.cs:150`, ×0.5 scale). The player can lose limbs, be *downed* by pain shock, or die of blood loss or heart failure while HP > 0 (`HealthComponent.cs:805–836`). | Deaths and freezes that Terraria never has, and HP is no longer the single truth. Needs a decision. | IC-4 |
| 5 | **Minion lifecycle bugs.** Drone damage is frozen at summon time (`GameSimulation.Combat.cs:376`). Minions survive the player's death and slot loss, and are never despawned (no `_minions.Clear` anywhere). 4 of 18 minion kinds have no sprite and draw as a red *enemy* rectangle (`GraphicalSandbox.cs:2297`). | Summoner play is confusing and unbalanced. | IC-7 |
| 6 | **No reforging.** Calibrations roll only at the crafting bench (`GameSimulation.cs:789`). Loot, boss drops and chest weapons are always uncalibrated, and tools can never be calibrated. | One of Terraria's main gold sinks and gear-polish loops is missing. | IC-13 |
| 7 | **Every bullet, arrow, bolt, rocket and minion shot is drawn as a 1-px line** (`GraphicalSandbox.cs:2275`). Only yo-yos, boomerangs, flails and thrown weapons show sprites. | Weapons look and read alike. 429 weapons share one visual for their shots. | IC-11 |
| 8 | **Every creature has 0 defense** (`EnemyArchetype.Defense`, `Actors.cs:441`, never set). | The defense and armor-penetration formulas are live but inert. AP gear and "Ichor-like" shred do nothing on most targets. | IC-10 |
| 9 | **Death and respawn.** There is no bed spawn point: you always respawn at the landing site. The internal timer is 5 s for every difficulty (`GameSimulation.Player.cs:1025`), while the UI uses `GetRespawnDelay` (Explorer 3 s). Classic deaths **destroy** half of every material stack (`GameSimulation.cs:2557`); Terraria Softcore drops coins only. | Long walks back, and a harsh hidden penalty. | IC-5, IC-6 |
| 10 | **Item information and auto-swing.** The tooltip says "uses Energy cells" for every ranged weapon and shows no damage for Tech or Command weapons, and no calibration, crit, set bonus or accessory effect (`GraphicalSandbox.cs:3250`). The "auto-swing" accessory tag (3 items) has no effect (`Gear.cs:81` is never read; `ItemUseRules.AutoReuse` ignores it). | Players cannot compare gear. Feral-claw-type accessories are dead weight. | IC-17, IC-15 |

**Flags that block play:** IC-2 (no healing ladder: hardmode survival), IC-1 (regen and buffs useless), IC-4 (non-HP deaths and downed state), and the minion rendering part of IC-7 (allies drawn as enemies).

---

## Feature tables

### A. Damage pipeline and formulas

| ID | Feature | Rimtera (code) | Terraria | Status |
|---|---|---|---|---|
| A1 | Weapon damage = round(base × prefix dmg) + ammo, × class multiplier (1 + additive bonuses, × multiplicative penalties) | `CombatTuning.WeaponDamage` `Content/CombatModel.cs:420`; `ComputeWeaponDamage` `GameSimulation.Combat.cs:146`. Keeps the fraction before the roll (D2). | `GetWeaponDamage` truncates (player_stats §4) | ✅ |
| A2 | Colonist skill multiplies damage (Melee/Shooting/Intellectual/Social) | `CombatStatsCalculator.SkillMultiplier` `CombatModel.cs:384` | none | ➕ |
| A3 | ±15% spread, luck reroll keeps higher/lower | `RollVariance` `CombatModel.cs:432` | `Main.DamageVar` (§4.3) | ✅ |
| A4 | Crit: base 4 + weapon crit + bonuses; ×2; summons never crit | `WeaponCritChance` `CombatModel.cs:462`, `CritChanceFor` `Combat.cs:176`; probe: Command item crit 0 | §4.1, `GetWeaponCrit` returns 0 for summon | ✅ |
| A5 | Crit and spread rolled per hit | Rolled once per use, shared by every creature that use hits (`RollUse` `Combat.cs:191`; D5 in GEAR_DIFF_MECHANICS) | per NPC hit | ◐ |
| A6 | NPC damage taken = max(1, D + AP give-back − def/2), ×2 crit | `CreatureDamageTaken` `CombatModel.cs:452`, `StrikeEnemy` `Combat.cs:206` | `StrikeNPC_Inner`, `checkArmorPenetration` (§4.4–5) | ✅ |
| A7 | Creature defense values | `EnemyArchetype.Defense` exists (`Entities/Actors.cs:441`), but no archetype sets it: all 0 | NPC defense 0–~100 by tier (knowledge; `NPC.SetDefaults`) | ⚠ |
| A8 | Armor penetration from gear (`ap`, `ap-melee` tags) | `PlayerArmorPenetration` `Combat.cs:181` | `armorPenetration`, `meleeArmorPenetration` | ✅ |
| A9 | Knockback taken: resist share, soft caps 8/10/12/14, hard cap 16, crit ×1.4 | `KnockbackTaken` `CombatModel.cs:473`, `KnockbackResistance` `:610` | §5 | ✅ |
| A10 | Player-side knockback modifiers (kb glove ×2 melee, titan buff ×1.5, minionKB) | none (D6) | `GetWeaponKnockback` §5 | ❌ |
| A11 | Use time: prefix speed baked in (rounded), melee × 1/min(attack speed, 3) | `CalibratedUseTicks`, `MeleeUseTicks`, `UseTimeMultiplier` `CombatModel.cs:491–503`; `GearAttackSpeedFactor` `Gear.cs:147` | §6 | ✅ |
| A12 | Class damage bonuses from gear/buffs (dmg, dmg-melee…) are additive | `CombatStatsCalculator.Calculate` `CombatModel.cs:334`; `GearAdjust` `Gear.cs:123` | additive within a stat (§3) | ✅ |
| A13 | Ammo-family multipliers (arrowDamage, bulletDamage, rocketDamage) | none: ranged gear only adds `dmg-ranged` | `bowEffectiveDamage`, `gunEffectiveDamage` (§4) | ❌ |
| A14 | Charge (mana): delay, regen units, cost rounding, cap 400, sickness | `ChargeRegenDelayTicks`/`ChargeRegenUnits`/`ChargeCost` `CombatModel.cs:679–716`; `Combat.cs:107–122,472` | §10 | ✅ |
| A15 | Banner bonus on damage dealt and taken | `BannerDamageFactor`/`BannerDefenseFactor` `GameSimulation.Player.cs` (~1093) | `BannerEffect` (§4.2) | ✅ |
| A16 | NPC immunity after hits | Per-use hit sets for swings and pierce; yo-yo 0.22 s per enemy (`WeaponStyles.cs:31`); generic melee 0.05 s; tool swing 0.8 × swing time. No shared per-player NPC immunity, so stacked shots and drones are never throttled. | NPC.immune per player (~10 ticks), local/static immunity per projectile type (knowledge) | ◐ |
| A17 | Difficulty damage scaling for creatures (in/out) | `ScaleIncoming`/`ScaleOutgoing` `GameSimulation.cs:2548`; `DefenseBalance.EnemyHitScale` `Content/DefenseBalance.cs:37` | Expert/Master NPC stat scaling (knowledge) | ✅ |
| A18 | Boss hit ceiling | `CapBossHit` `GameSimulation.Hardmode.cs:124` | none | ➕ |

### B. Weapon classes and styles (429 weapons: Melee 141, Ranged 131, Tech 86, Command 71; every weapon has a style and a class)

| ID | Style | Rimtera (code, probe data) | Terraria | Status |
|---|---|---|---|---|
| B1 | Broadsword arc (57) | `StartSwing`/`UpdateSwing` `GameSimulation.WeaponStyles.cs:180,218`. 2.3 rad arc; size calibration scales reach. | useStyle 1 swing | ✅ |
| B2 | Sword beams (projectile-firing swords) | `StartSwing` never fires a projectile | Enchanted Sword, Night's Edge, Terra Blade… (knowledge) | ❌ |
| B3 | Shortsword thrust (13) | `UpdateSwing` sine extension | 1.4 shortsword stab | ✅ |
| B4 | Spear (22) | thrust 0.45/0.15/0.4 timing, pierce via the swing set | spear projectile AI | ✅ |
| B5 | Yo-yo (20) | `ThrowYoyo`/`StepYoyo` `WeaponStyles.cs:335,446`: tethered, held, 0.22 s re-hit | yo-yo AI, plus string, counterweight and yo-yo glove accessories | ◐ |
| B6 | Boomerang (12) | `ThrowReturning`/`StepReturning` `:347,481`, one out at a time | boomerang AI | ✅ |
| B7 | Flail (16) | Thrown and reeled like a boomerang (`StepReturning`) | 1.4 flails: spin while held, then launch (knowledge) | ◐ |
| B8 | Bow (34) | `FireStyledRanged` `:521`; arrows fly straight (gravity only for `Thrown`, `:566`) | arrows fall under gravity (knowledge) | ◐ |
| B9 | Gun (41) / Shotgun (9) | `FireStyledRanged`; a shotgun forces ≥4 pellets with spread | guns, shotguns | ✅ |
| B10 | Launcher (17) | Rockets explode on contact (`StepExplosive`/`Explode` `:571,588`); blasts never hurt the player or tiles | rockets and grenades hurt the player; some rockets break tiles | ➕ |
| B11 | Thrown consumables (30, Ranged class) | `ThrowConsumable` `:551`: consumed, gravity 16 | 1.4: thrown is ranged and consumed | ✅ |
| B12 | Staff / Tome (53/22) | `AttackTech` `Combat.cs:292` → straight bolts, count/spread/pierce | many staff projectile AIs | ✅ (shape) |
| B13 | Beam (channel) (11) | `StartBeam`/`UpdateBeam` `:605,617`: pulses, drains charge per pulse | channel magic (Last Prism-type) | ✅ |
| B14 | Auto-reuse per item kind | `ItemUseRules.AutoReuse` `GameSimulation.Controls.cs:16`, rules counted from items.csv | `Item.autoReuse` | ✅ |
| B15 | Melee hits cut plants | `CutPlantsAlong` in `UpdateSwing` | `CutTiles` | ✅ |
| B16 | Legacy paths: `AttackMelee` (miss chance from colonist skill), `AttackRanged` tier-0 energy guns (no spread roll, no class multiplier, `Critical` never set) | `GameSimulation.cs:1148,1197`. 8 tier-0 weapons; the melee miss path is reached only by non-styled melee. | n/a | ◐ |
| B17 | Solar flare disables ranged | `FireStyledRanged` `:523` | none | ➕ |

### C. Projectiles and ammunition

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| C1 | Projectile behaviours | straight, pierce, homing (`UpdateWeaponStyles` `WeaponStyles.cs:661`), explode, gravity (thrown), tether, return, beam, wind drift | ~190 aiStyles: bounce, split, chain, sticky, spawn-on-hit, orbit… (knowledge) | ◐ |
| C2 | Ammo families | Arrow 11, Bullet 18, Rocket 9, Dart 7, Fuel 7, Cell 3 (`AmmoClass` `ItemSystem.cs:~1475`). Dart and Fuel each have only 1 weapon. | arrows, bullets, rockets, darts, gel, etc. | ◐ |
| C3 | Ammo effects: damage add, pierce, homing, blast, affliction | `FireStyledRanged` + `TagStyled` `:163`, `TagLastProjectiles` `PlayerSystems.cs:175` | per-ammo projectile | ✅ |
| C4 | Ammo choice order | First matching stack in inventory order (`FindAmmo` `WeaponStyles.cs:510`); no ammo slots | 4 ammo slots used first (knowledge) | ◐ |
| C5 | Ammo saving (set, potion, tags) | `AmmoSaveChance` `CombatModel.cs:378`, `Gear.cs:139` | ammo-cost reducers; per-weapon chances (e.g. 50%) | ◐ |
| C6 | Player projectile hit loop: pierce, lifesteal, on-hit gear | `UpdateProjectiles` `GameSimulation.cs:1844–1890` | `Projectile.Damage` | ✅ |
| C7 | Hostile projectile vs immune player | Still consumed, still knocks the player back, and the message shows the raw damage while 0 is applied (`GameSimulation.cs:1893–1899`) | hostile projectiles do not collide with an immune player (knowledge) | ⚠ |
| C8 | Projectile visuals | Lines only (`GraphicalSandbox.cs:2275`); `ProjectileState.SpriteName` is set but not drawn | per-projectile textures | ❌ |

### D. Command class: drones, sentries, whips

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| D1 | Base slots 1 minion + 1 sentry, gear and buffs add | `BaseDroneSlots`/`BaseSentrySlots` `CombatModel.cs:331`, `drone:`/`sentry:` tags | `maxMinions = 1`, `maxTurrets = 1` (§2) | ✅ |
| D2 | Summon replaces the oldest past the limit | `SummonMinion` `Combat.cs:355–379` | same | ✅ |
| D3 | 18 minion kinds, all used by 53 command items | `MinionCatalog` `CombatModel.cs:219` | ~60 summons (knowledge) | ✅ |
| D4 | Minion AI | Every drone orbits the player and fires bolts; sentries fire from position; no chase, no contact attack, no line-of-sight check (`UpdateMinions` `Combat.cs:383`) | minions chase and contact-hit, or shoot (varied AI) | ◐ |
| D5 | Minion damage follows current gear and buffs | Frozen at summon (`MinionState.Damage`, `Combat.cs:376`); colonist skill baked in | `minionDamage` read live each hit | ⚠ |
| D6 | Minions despawn on death and when slots shrink | Never: no clear on death, unequip or slot change; not saved (lost on reload). `MinionSpec.LifetimeSeconds` is unused. | killed on death and when over the slot cap (knowledge) | ⚠ |
| D7 | Minion and sentry sprites | 14 of 18 exist. `minion_mech_companion`, `minion_coral_jelly`, `minion_spore_familiar` and `minion_hive_familiar` are missing, so they draw as the red **enemy** box (`DrawSpriteActor` fallback, `GraphicalSandbox.cs:2297`). | own sprites | ⚠ |
| D8 | Right-click target selection for minions | none | summon staff alt-use targets an NPC | ❌ |
| D9 | Whip tag | Any whip hit marks the target: drones and sentries focus it and deal ×1.3 for 4 s, the same for all 19 whips (`StyleHit` `WeaponStyles.cs:313`, `UpdateMinions` `Combat.cs:424`). No flat tag damage, no crit tag, no whip attack-speed scaling. | per-whip flat `bonusTagDamage`, some crit tags, summon attack speed (§6, prefixes.csv tag) | ◐ |
| D10 | Whip class | 18 Command, 1 Melee (name-matched "lash", `WeaponStyleAssignments.cs:68`); the Melee one crits and lifesteals | all whips are summon class | ◐ |
| D11 | Repair drone heals the player | `MinionSpec.Heals` `Combat.cs:403` | none | ➕ |
| D12 | Minion slot cost | always 1 | 0.5–2 slots per minion type (knowledge) | ◐ |

### E. Tools as weapons

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| E1 | Tool swing hits creatures in an arc, once per swing (61 tools have damage) | `StrikeWithToolSwing` `GameSimulation.ToolCombat.cs:18`; damage from `ToolCombat.Apply` `Content/ToolCombat.cs:12` | pick/axe/hammer carry a melee hitbox | ✅ |
| E2 | Tools take prefixes | `Calibratable` excludes the Pickaxe/Cutter/Demolisher use kinds (`ItemSystem.cs` `Calibratable`) | tools roll melee prefixes; speed prefixes matter for mining | ◐ |

### F. Armor and set bonuses (152 pieces, 50 complete sets)

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| F1 | Player damage taken = max(1, D − def × 0.5/0.75/1.0) | `DefenseBalance.ApplyDefense` `Content/DefenseBalance.cs:28`, `GearHitPlayer` `Gear.cs:163` | `CalculateDamagePlayersTake` (§8) | ✅ |
| F2 | Endurance multiplies the post-defense damage | `Gear.cs:176`, capped at 60% (D7) | `(1−endurance)×dmg`, no cap | ✅ (cap ➕) |
| F3 | Set detection: head + body + legs of one set | `CombatStatsCalculator` `CombatModel.cs:354`, `PlayerStatsCalculator` `PlayerProgression.cs:~384` | `ArmorSetBonuses.GetCompleteSet` | ✅ |
| F4 | Set bonus content | Stat bonus (defense, class damage, crit, slots, ammo save, lifesteal, thorns) plus one of 8 generic specials (`SetSpecial` `ArsenalItems.cs:6`; `SetSpecials` `:74`). 49 of 50 sets have a class bonus. | 64 sets, many unique or active (double-tap shields, guardian, stealth, heal bolts) (`armor_sets.csv`) | ◐ |
| F5 | Per-piece bonuses (class damage, crit) | 135/152 pieces carry class damage, 18 carry crit | `GrantArmorBenefits` per-piece switch | ✅ |
| F6 | Armor defense curve per tier | `--balance`: T1 5 → T7 58; endgame + accessories 88 cuts 49% of the final boss hit | defense ~2 (copper) → ~60s (endgame sets) | ✅ |
| F7 | Vanity armor slots | `EquipmentLoadout` vanity slots exist; `--item-census` counts only 1 vanity item | ~400 vanity pieces | ◐ |

### G. Accessories and mobility (220 accessories, 30 wings)

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| G1 | Slots: 5 + a gated 6th | `EquipmentLoadout.AccessorySlots`/`Slot6Gate` `PlayerProgression.cs:60–72` | 5, +1 expert, +1 master (knowledge) | ✅ |
| G2 | Accessory effects via tags (~45 tag kinds, 0 unread per `--item-audit`) | `GearTagValue`/`GearHasTag` `Gear.cs:32,57` | `ApplyEquipFunctional` | ✅ |
| G3 | Same accessory can't be worn twice | `EquipmentSystem.TryEquip` `PlayerProgression.cs:~420` | same | ✅ |
| G4 | Auto-swing accessories (3 items, tag `auto-swing`) | `AutoSwingActive` `Gear.cs:81` is never read; `ItemUseRules.AutoReuse` ignores it | Feral Claws/Power Glove give melee autoReuse | ⚠ |
| G5 | Attack-speed accessories | `attackspeed:` tag → `GearAttackSpeedFactor` | `meleeSpeed +=` | ✅ |
| G6 | Missing accessory families | none: knockback glove, longer i-frames (Cross Necklace), on-hurt effects (Star/Bee Cloak, Panic Necklace), low-HP damage reduction (Frozen Turtle Shell), dash (Tabi/Shield of Cthulhu), auto-mana (Mana Flower), mana-on-hurt (Magic Cuffs), yo-yo accessories, arrow/bullet multipliers (Magic Quiver) | `accessories.csv` | ❌ |
| G7 | Wings: flight time (best wing counts), climb, glide | `GearWingFlightSeconds` `Gear.cs:83`; 0.7–4.2 s; `MaximumRocketFuel` `GameSimulation.Player.cs:145` | per-wing flight time, horizontal speed and acceleration | ◐ |
| G8 | Double jump, rocket boots, glide, climb, water walk, featherfall, lava/heat/cold | tags read in `PreparePlayerMovement`/`AfterPlayerMovement` `GameSimulation.Player.cs:~330–432` | cloud bottle, rocket boots, claws, water walking, lava charm… | ✅ |
| G9 | Grappling hooks | `TryFireGrapple`/`UpdateGrapple` `GameSimulation.Player.cs:528`; `HookSpec` (range, speed, count, instant retract) `GrowthItems.cs:32`. Needs the `grapple` tag on an *accessory*. | dedicated hook slot; 1–3 hooks | ◐ |
| G10 | Accessory defense cap 6 | `DefenseBalance.MaximumAccessoryDefense` `DefenseBalance.cs:13` | no cap | ➕ |
| G11 | 6th-slot gate respected everywhere | `GearTagValue` checks the gate (`Gear.cs:37`); `CombatStatsCalculator.Calculate` iterates every slot without it (`CombatModel.cs:338`) | n/a | ⚠ |
| G12 | Info accessories (readouts, ore/danger sense) | `GameSimulation.InfoGear.cs`, `OreDetectorActive` `Gear.cs:75` | info accessories | ✅ |

### H. Potions, food, buffs and debuffs (51 statuses; 79 buff/heal items)

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| H1 | Healing potions | **1 item**: Med stim 50 HP (`ContentItems.cs:309`) | Lesser 50, Healing 100, Greater 150, Super 200, Restoration 90, plus healing foods (items.csv `healLife`) | ❌ |
| H2 | Charge (mana) restore items | 1 item | 50/100/200/300 (items.csv `healMana`) | ❌ |
| H3 | Potion sickness | 45 s, blocks QuickHeal and direct use (`PotionSicknessSeconds` `GameSimulation.Player.cs:83`, `ConsumeBatchBItem`) | 60 s (45 s restoration / Philosopher's Stone) (knowledge) | ◐ |
| H4 | Quick heal / quick charge / quick buff | `QuickHeal`/`QuickCharge`/`QuickBuff` `GameSimulation.Player.cs:610–672` | QuickHeal/QuickMana/QuickBuff | ✅ |
| H5 | Buff durations | Game-hours; 1 h = 12.5 real s (`GameHoursPerRealSecond = 0.08`). Potions 3–6 h ≈ 37–75 s; probe: Fortified 4 h → 49 s left after 1 s. Sleep fast-forward also drains them. | Regeneration/Ironskin/Swiftness 28800 ticks = 8 min (items.csv `buffTime`) | ⚠ |
| H6 | Regen from buffs and gear | `LifeRegenerationPerHour` × game hours (`GameSimulation.cs:660`). Probe: Regeneration +0.13 HP in 4 s, Rejuvenation +0.29. | Regeneration +4 units = 2 HP/s; Band of Regen +1 HP/s (§9) | ⚠ |
| H7 | Damage-over-time strength | Poison 1.25 per 0.5 h (probe 0.12 HP/s), Burning 1 per 0.25 h (0.3 HP/s) (`StatusEffectSystem` `PlayerProgression.cs:~157`) | poisoned −4 units (2 HP/s), onFire −8 (4 HP/s) (§9.1) | ⚠ |
| H8 | Creatures inflict debuffs on the player | none (only lava → Burning, fallout → Poison) | dozens of NPCs inflict Poisoned, OnFire, Bleeding, Confused, Slow, Weak, Darkness, Broken Armor, Chilled, Frozen, Venom, Cursed, Silenced… | ❌ |
| H9 | Movement and stat debuffs on the player | none (no slow, chilled, confused, darkness, weak, broken armor, silenced) | strongest move-speed debuff, `statDefense/=2`, `meleeDamage*=0.5` (§1) | ❌ |
| H10 | Buff effects through tags (dmg, crit, speed, mining, light, dodge, thorns, slots, ammo save…) | `StatusEffectSystem` `PlayerProgression.cs:157–205`; read by `GearTagValue` | buff effects in `UpdateBuffs` | ✅ |
| H11 | Well Fed tiers (3), food replaces lower tiers | `GearOnAte` `Gear.cs:249` | Well Fed / Plenty Satisfied / Exquisitely Stuffed | ✅ |
| H12 | Hunger need from food | `Player.Needs.Restore` `GameSimulation.cs:~1250` | none | ➕ |
| H13 | Charge sickness after a restore | `ChargeSicknessSeconds`/`ChargeSicknessPenalty` `CombatModel.cs:670` | mana sickness | ✅ |
| H14 | Debuff immunities (poison, heat, cold, toxic, acid, lava, fire-block) | `BlockedByImmunityTag` `PlayerProgression.cs`; `GearTick` `Gear.cs:209` | debuff-immunity accessories | ✅ (little to block, see H8) |
| H15 | Buff/debuff count | 51 statuses (8 have no item: FrostGuard, Phased, PotionSickness, PhaseStrain + 4 creature afflictions) | ~400 buff ids | ◐ |
| H16 | Afflictions on creatures (burning, poison, frostbite, armor shred, confused, marked) | `AfflictEnemy`/`WeaponAffliction` `GameSimulation.PlayerSystems.cs:97,119` | NPC debuffs | ✅ |

### I. Calibrations (prefixes)

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| I1 | Table: 51 calibrations in 6 groups (universal, melee, ranged, tech, command, accessory) with weights | `CalibrationCatalog` `CombatModel.cs:63–125` | 97 prefixes (`prefixes.csv`) | ✅ |
| I2 | Effects: damage, crit, speed, knockback, cost, velocity, size, accessory defense/crit/damage/move | `CalibrationDefinition` `CombatModel.cs:46`; applied in `WeaponDamage`, `CalibratedUseTicks`, `ChargeCost`, `GearProjectileSpeedFactor`, `CalibratedSize` | `Item.Prefix` (§11) | ✅ |
| I3 | Roll when crafted | Always rolls (no "none" outcome), luck-weighted (`LuckyCalibration` `PlayerSystems.cs:279`; `GameSimulation.cs:789`) | `Prefix(-1)`: 1 in 4 none | ◐ |
| I4 | Prefixes on loot and drops | Never: drops, chests and boss loot are always uncalibrated | dropped weapons roll prefixes (knowledge) | ❌ |
| I5 | Reforging | none | Goblin Tinkerer reforge for coins | ❌ |
| I6 | Value and rarity from prefix | Sell value × ValueMultiplier, linear (`GetSellPrice` `GameSimulation.Town.cs:389`); rarity unchanged | value × valueMult²; rarity ±1/±2 (§11) | ◐ |
| I7 | Accessory prefixes | defense up to +3, crit up to +3, damage up to +4%, move speed up to +4% | also +1–4% melee speed, +20 mana | ◐ |
| I8 | Calibrated stackables | Thrown consumables (30) calibrate, and stacks only merge with the same calibration (`Inventory.Add` `ItemSystem.cs:1989`), so knife stacks fragment | consumable thrown items take no prefix | ⚠ |
| I9 | Calibration kept on drop, pickup, equip and save | `GroundItems` snapshot, `CaptureEquipmentWithCalibrations` `Gear.cs:274` | prefix persists | ✅ |

### J. Player hurt, death and respawn

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| J1 | Hit immunity 0.67 s; a dodge grants immunity | `HitImmunitySeconds` `Entities/Actors.cs:130`, `TakeHit` `:139`; `Gear.cs:168` | `immuneTime = 40` ticks (knowledge) | ✅ |
| J2 | Dodge chance (tags + set special, cap 60%) | `GearHitPlayer` `Gear.cs:166` | Black Belt, Master Ninja Gear 10% (§8) | ✅ |
| J3 | Contact damage variance | none: creature hits are exact (`HitPlayerFromEnemy` `GameSimulation.Player.cs:1153`, `GameSimulation.cs:1673`) | `Main.DamageVar(npc.damage, −luck)` on contact (knowledge) | ◐ |
| J4 | Contact hit test | Creature centre within 1.15 tiles of the player centre (`GameSimulation.cs:~1671`), not hitbox overlap | hitbox intersection | ◐ |
| J5 | Knockback on the player | Fixed push (3, −2) on contact, projectile's own on shots; knockback-immune tag cancels it | fixed ~(4.5, −3.5) push (knowledge); Cobalt Shield immunity | ✅ |
| J6 | Thorns, burst-when-hit, lifesteal, heal-on-hit, charge-on-hit | `Gear.cs:177–205`, `GameSimulation.cs:1680` | thorns, beetle, spectre, etc. | ✅ |
| J7 | RimWorld body layer on the player | Hits wound body parts (×0.5); limb loss reported (`ReportPartLoss`); downed by pain, unconsciousness or legs (`HealthComponent.cs:816–836`); death by blood loss or heart failure independent of HP (`:805–813`) | HP is the only death condition; no downed state | ⚠ |
| J8 | Natural life regeneration | `NaturalLifeRegenUnits` `CombatModel.cs:642`, `UpdateLifeRegen` `Combat.cs:88` | §9.3 exactly | ✅ |
| J9 | Max life progression | 100 + 10 Life cores × 10 + 20 Lifeblooms × 5 = 300 (`Actors.cs:91`, `GrowthItems.cs:43`) | 100 + 15 × 20 + 20 × 5 = 500 (§2) | ◐ |
| J10 | Death drops by difficulty | Explorer none; Classic drops 40% credits **and destroys half of every material stack** (`RespawnPlayer` `GameSimulation.cs:2557`); Salvager drops the whole inventory with a long life; Ironman deletes the character | Softcore 50% coins; Mediumcore all items; Hardcore deletion | ◐ |
| J11 | Tombstone with epitaph | `PlaceTombstone` `GameSimulation.Player.cs:1044` | tombstone | ✅ |
| J12 | Respawn delay | `UpdateWhileDead` `GameSimulation.Player.cs:1025` sets 5 s (20 s with a boss) for every difficulty; `GetRespawnDelay` (`:1021`, used by the UI) says Explorer 3 s. Probe: timer 5.0 vs Explorer 3. | ~10 s base, longer while a boss lives (knowledge) | ⚠ |
| J13 | Respawn point | Always the landing site column (`RespawnPlayer`); no bed spawn | bed spawn point (knowledge) | ❌ |
| J14 | Bosses leave when the player dies | `BossesLeaveOnPlayerDeath` `GameSimulation.Spawning.cs:253` | bosses despawn | ✅ |
| J15 | Medic heals for credits | `HealAtMedic` `GameSimulation.Town.cs:~413` | Nurse | ✅ |

### K. Stacks, rarity, value, selling, consumables

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| K1 | Max stack | 1:884, 20:144, 30:54, 99:623, 999:261 (ammo 999) | 9999 for nearly all stackables (1.4.4+) (knowledge) | ◐ |
| K2 | Rarity | 7 tiers from progression tier (`RarityFor` `ContentItems.cs:35`); never shown (no tooltip colour), not changed by calibration | −1…11 + expert/master, shown as name colour | ◐ |
| K3 | Value | `CreditValue` = Value → ShopPrice → tier² × 10 (`ItemSystem.cs`); 45 items use the fallback | `value` in copper | ✅ |
| K4 | Selling | 1/5 of value × calibration × happiness (`GetSellPrice` `GameSimulation.Town.cs:389`) | sell at value/5 | ✅ |
| K5 | Buy-back of sold items | none | shop buy-back row (knowledge) | ❌ |
| K6 | Currency | single "Credits" item, 99999 stack | copper/silver/gold/platinum | ➕ |
| K7 | Boss summons and event starters | `ConsumeContentItem` `GameSimulation.Items.cs:77–120` | summon items | ✅ |
| K8 | Crates and treasure bags open | `OpenCrate`, `OpenTreasureBag` `PlayerSystems.cs:422` | same | ✅ |
| K9 | Life/charge capacity items | Life core, Lifebloom, Charge crystal (`ConsumeBatchBItem` `GameSimulation.Player.cs:~678`) | Life Crystal, Life Fruit, Mana Crystal | ✅ |

### L. Sprites, use animations, item UI

| ID | Feature | Rimtera | Terraria | Status |
|---|---|---|---|---|
| L1 | Own sprite for every item | `--item-audit`: 0 missing, 0 shared | per-item textures | ✅ |
| L2 | Use animations by style | `UseAnimation` (21 kinds) `UI/GraphicalSandbox.HeldItems.cs:14–125`: swing, heavy swing, stab, aim, bow, staff, tome, beam, hold-up, eat, drink, throw, tether, whip, place, pour, spray, cast, drill, net | useStyle 1–14 | ✅ |
| L3 | Melee swing/thrust art at the simulation reach | `DrawSimSwing` `HeldItems.cs:~677` | item drawn on the arc | ✅ |
| L4 | Spinning sprites for yo-yos, boomerangs, flails and thrown | `DrawStyledProjectiles` `UI/GraphicalSandbox.Weapons.cs:131` | projectile textures | ✅ |
| L5 | Item tooltip | `DrawItemTooltip` `GraphicalSandbox.cs:3250`: "uses Energy cells" for every ranged item; no damage line for Tech or Command; no crit, knockback, charge cost, calibration name, set bonus, accessory effect, rarity colour or value | full stat tooltip, prefix name, rarity colour, sell price | ⚠ |
| L6 | Explosion visuals | `RecentExplosions` `WeaponStyles.cs:82` | explosion dust | ✅ |

---

## Integration steps

Size: S ≈ under a day, M ≈ 1–3 days, L ≈ a week or more. Each step names the self-test that should prove it. Add checks to the existing suites (`ParityCombatTests`, `WeaponStyleSelfTests`, `ArsenalSelfTests`, `PlayerSystemsSelfTests`) unless noted.

**IC-1 · Put status timing and regeneration on real time** (fixes H5, H6, H7) · **blocks play**
- Change: give `StatusDefinition` real-second durations and Terraria-unit regen (half-HP/s units) and damage over time. `StatusEffectController.Advance` should run on real seconds, not game hours, so sleep fast-forward stops draining buffs. Fold gear `LifeRegenerationPerHour` into the same unit (resolves GEAR_DIFF D10), and add it in `UpdateLifeRegen` as `lifeRegen` units. Debuffs zero out positive regen, as in Terraria §9.1. Re-author potion durations: 2–8 min for utility potions, 4 min for class tonics. Keep the save format by converting old hour values on load.
- Files: `Systems/PlayerProgression.cs` (StatusDefinition, StatusEffectController, PlayerStatsCalculator base regen), `Systems/GameSimulation.cs:655–661`, `Systems/GameSimulation.Combat.cs:88`, item data in `Content/ArsenalItems.Data.cs`, `ContentItems.cs`, `PlayerItems.cs`, save restore in `GameSaveData.cs`.
- Size: M. Dependencies: none (food and hunger layer stays).
- Acceptance: Regeneration buff adds ≥1.5 HP/s on top of natural regen; Poison takes 2 ± 0.2 HP/s and Burning 4 ± 0.4 HP/s; a 4-min buff still has ≥3:50 left after 10 s of sleeping; a regen accessory gives ≥0.5 HP/s; old saves load with their buffs.

**IC-2 · Healing and charge potion ladder** (fixes H1, H2, H3) · **blocks play**
- Change: append ItemIds for 3 more healing tiers (100/150/200), a restoration-type potion (90 HP, 45 s sickness) and 3 more charge tiers (100/200/300). Gate the recipes by tier stations and materials (Chem lab → hardmode lab → endgame). Add them to the Medic and merchant shops. Set potion sickness to 60 s, with an accessory that shortens it (×0.75). Let some foods heal a small amount.
- Files: `Systems/ItemSystem.cs` (ItemId, append-only), `Content/ContentItems.cs` or `ArsenalItems.Data.cs`, `Content/ContentRecipes.cs`, `Systems/TownNpcs.cs:216` shops, `GameSimulation.Player.cs:83`, sprites.
- Size: M. Dependencies: crafting/progression area (gates), art (sprites; reuse recolours until the paused ComfyUI run resumes on the user's go).
- Acceptance: each tier exists, can be crafted only after its gate (`--craft-audit` clean), and QuickHeal picks the smallest tier covering missing HP. Balance check: at each tier band, the median creature hit ≤ 50% of that tier's potion.

**IC-3 · Creature-inflicted debuffs on the player** (fixes H8, H9; gives H14 a purpose)
- Change: append player statuses Bleeding (no regen), Chilled/Slow (move ×0.75/0.5, strongest only), Confused (inverted input), Darkness (light −), Weak (melee damage ×0.8, defense −4), Shattered/Broken Armor (defense ÷2), Silenced (no Tech use), Venom (−30 units), Frostburn (−16) and Cursed fire (−24). Add `EnemyArchetype.Inflicts` (status, chance, seconds) and apply it on contact and projectile hits. Add immunity tags to matching accessories (an Ankh-like combiner later).
- Files: `PlayerProgression.cs` (StatusId append, definitions, `IsDebuff`), `Entities/Actors.cs` (EnemyArchetype), creature data (`Content/CreatureCoverageData.cs`, `ContentFillCreatures.cs`, `Entities/ContentCreatures.cs`), `GameSimulation.cs:1673/1893`, movement multiplier in `GameSimulation.cs:675`, `Gear.cs` immunity.
- Size: L. Dependencies: enemies/creatures area (which creatures inflict what), UI buff bar (red icons), input area (confused inversion).
- Acceptance: a poisonous creature applies Poison at about its listed chance over 200 contacts; an immunity accessory blocks it; Slow and Chilled together give ×0.5, not ×0.375; Broken Armor halves `TotalDefense`.

**IC-4 · Decide the player's death model** (J7) · **blocks play until decided**
- Change (recommended: HP-only, like Terraria): for the player, skip `HealthComponent.Kill` causes other than HP ≤ 0, never set `IsDowned`, and make body-part wounds cosmetic or limited to explicit sources (traps, specific bosses). Keep the full body layer for colonists.
- Files: `Entities/Actors.cs:150–167` (TakeDamage, SyncHealthDeath), `Entities/HealthComponent.cs:805–836`, `GameSimulation.cs` (`ReportPartLoss` calls at 1674/1894).
- Size: M. Dependencies: colonist/RimWorld area (shared HealthComponent); needs the user's call.
- Acceptance: 10,000 random creature hits that leave HP > 0 never kill or down the player; the player dies exactly when HP reaches 0.

**IC-5 · Classic death penalty**
- Change: Classic drops 50% of carried credits at the death spot and deletes nothing. Only if the user wants the harsher rule: drop half of each material stack as recoverable ground items instead of destroying it.
- Files: `GameSimulation.cs:2557–2581`, `GameSimulation.Combat.cs:563` (`ClassicDeathCreditShare`).
- Size: S. Dependencies: user decision.
- Acceptance: a Classic death keeps every item stack and leaves a credit pile worth 50% at the death site.

**IC-6 · Respawn point and timer**
- Change: interacting with a bed in a valid room sets the spawn point (saved). Respawn there, or at the landing site if the bed is gone. Set `_respawnSeconds = GetRespawnDelay(difficulty)` in `UpdateWhileDead`. Consider a 10 s base (5 s Explorer), with a boss alive doubling it.
- Files: `GameSimulation.Player.cs:1021–1040`, `GameSimulation.cs:2557`, a bed interaction in `GameSimulation.Town.cs` or `Leisure.cs`, the save meta (`GameSaveData.cs`).
- Size: M. Dependencies: housing/town area (valid-room check), world save.
- Acceptance: after setting a bed and dying, the player stands beside that bed; `RespawnSecondsRemaining` equals the UI countdown for every difficulty.

**IC-7 · Minion lifecycle, live damage and sprites** (D5, D6, D7) · rendering part **blocks play**
- Change: store the source item and calibration on `MinionState` and compute damage per shot from current `CombatStats` (whip tag included). Despawn drones on death and trim them when `DroneSlots`/`SentrySlots` drop. Save minions or clear them on load, and drop the unused `LifetimeSeconds` or use it for sentries. For the 4 missing `minion_*` sprites, resolve to the summoning item's `cmd_*` art (aliases already exist: `cmd_mech_companion_key`, `cmd_coral_jelly_familiar`, `cmd_hive_familiar`, `cmd_spore_familiar`), and never fall back to the enemy brush for allies.
- Files: `Systems/GameSimulation.Combat.cs:355–440`, `Content/CombatModel.cs:248`, `UI/GraphicalSandbox.cs:2297`, `UI/SpriteAtlas.cs` (aliases).
- Size: M. Dependencies: art (proper sprites in the next run).
- Acceptance: summon, then equip a +16% set → the next shot is 16% stronger; dying leaves 0 drones; removing a slot item trims to the cap; `--entity-sprites` lists all 18 minion kinds as resolved.

**IC-8 · Minion behaviour variety and targeting** (D4, D8, D12)
- Change: add 3 minion behaviours: chase-and-contact, orbit-shooter (current) and dash-striker. Add a line-of-sight check for shooters, right-click (alt-use) on a creature to set the minion target, and per-kind slot cost.
- Files: `Content/CombatModel.cs` (MinionSpec fields), `GameSimulation.Combat.cs:383`, the input hook for alt-use.
- Size: L. Dependencies: controls area (alt-use binding), enemy AI helpers (`EnemyAiFamilies.cs`).
- Acceptance: a contact minion damages a creature only when touching it; shooters do not fire through solid tiles; an alt-use target is preferred over the nearest one.

**IC-9 · Whip tags per whip** (D9, D10)
- Change: add `ItemDefinition.TagDamage` (flat, added to minion hits on the tagged target) and an optional `TagCrit`. Scale whip use time with a summon attack-speed stat. Make the one Melee "lash" a Command whip, or rename it to a melee weapon.
- Files: `Systems/ItemSystem.cs` (field), `Content/WeaponStyleAssignments.cs:68`, `GameSimulation.WeaponStyles.cs:313`, `GameSimulation.Combat.cs:424`.
- Size: S–M. Dependencies: IC-7 (live minion damage).
- Acceptance: a whip with TagDamage 5 adds exactly 5 (before defense) to each minion hit on the tagged creature for 4 s; all whips report the Command class.

**IC-10 · Creature defense data** (A7)
- Change: give archetypes tiered defense (roughly 0–8 pre-boss, 10–30 mid, 30–60 late), then retune `GearCurve` so time-to-kill stays on target.
- Files: creature data files, `Content/GearCurve.cs`, `--balance` report.
- Size: M. Dependencies: enemies area (owner of creature stats).
- Acceptance: `--balance` prints a defense column; an AP accessory measurably raises damage on armored creatures; the GearCurve time-to-kill self-tests still pass.

**IC-11 · Projectile sprites** (C8)
- Change: draw `ProjectileState.SpriteName` (already set per item, minion and enemy) as a rotated sprite. Fall back to an ammo-family sprite (arrow, bullet, rocket, dart, flame, cell bolt, magic bolt), then to the current line.
- Files: `UI/GraphicalSandbox.cs:2275`, `UI/SpriteAtlas.cs`, new `proj_*` assets.
- Size: M. Dependencies: art pipeline (the ComfyUI run is paused; resume only on the user's go), the UI area.
- Acceptance: a render self-test (or `--entity-sprites`) shows every ammo family and the 18 minion shot types resolving to a sprite.

**IC-12 · Projectile behaviour library** (B2, B7, B8, C1)
- Change: gravity for arrows (lighter than thrown), bounce, split-on-hit, chain-to-nearest, sticky/explode-later and sword beams (broadswords with `ProjectileSpeed > 0` fire one projectile per swing). Flails: hold to spin, release to launch.
- Files: `GameSimulation.WeaponStyles.cs`, `ProjectileState` (flags), item data.
- Size: L. Dependencies: IC-11 (visuals).
- Acceptance: one test per behaviour (an arrow's apex lies below its straight line; a bouncer reflects off a wall; a splitter spawns N children; a chain hits 3 creatures).

**IC-13 · Reforging and calibrations on loot** (I3, I4, I5, E2)
- Change: a town NPC service (the Tinkerer-analog, or a "calibration bench" station) rerolls the calibration for credits, with cost ∝ value. Drops and chest weapons roll a calibration (25% none). Tools become calibratable (melee group: speed matters for mining). Thrown consumables stop calibrating (fixes I8).
- Files: `Content/CombatModel.cs` (`Roll`, `ValidFor`), `ItemSystem.cs` (`Calibratable`), `GameSimulation.Town.cs` (service), loot drop paths (`DropLoot`, chest seeding), UI shop panel.
- Size: M. Dependencies: town area (which NPC), UI.
- Acceptance: reforging changes the calibration and costs credits; 1,000 boss-weapon drops show about 25% uncalibrated; a calibrated pickaxe with a speed calibration mines faster; thrown stacks always merge.

**IC-14 · Calibration value and rarity** (I6, K2)
- Change: value × valueMult², rarity ±1/±2 from valueMult, as in §11. Show rarity as the tooltip name colour.
- Files: `GameSimulation.Town.cs:389`, `ContentItems.RarityFor`, UI tooltip.
- Size: S. Dependencies: IC-17.
- Acceptance: a Masterwork weapon sells for more than 2× a plain one, and its tooltip name colour moves up one tier.

**IC-15 · Auto-swing accessories work** (G4)
- Change: `ItemUseRules.AutoReuse(item)` returns true for melee and whips when `AutoSwingActive`. The rule needs the simulation, so pass a flag or move the check into `GameSimulation`.
- Files: `Systems/GameSimulation.Controls.cs:16`, `UI/GraphicalSandbox.cs:945`.
- Size: S. Dependencies: the controls agent currently editing input; land this after their change.
- Acceptance: holding use with a shortsword plus an auto-swing glove produces repeated swings; without the glove, one swing.

**IC-16 · Missing accessory families** (G6, A10, A13, J-adjacent)
- Change: add tags and items for a knockback glove (`kb-melee:100`), longer immunity (`immune:+0.67`), on-hurt star or bee burst, low-HP damage reduction, a dash (double-tap), auto-charge (quick charge when short), charge-on-hurt, yo-yo string and counterweight, and arrow/bullet/rocket multipliers. Each tag must be read by `GearHitPlayer`, `KnockEnemy`, `FireStyledRanged` or movement code. Extend the item-audit known-tag list.
- Files: `Gear.cs`, `Combat.cs`, `WeaponStyles.cs`, `GameSimulation.Player.cs` (dash), content files, `ItemAudit.cs:26`.
- Size: L. Dependencies: art, crafting area (recipes), movement area (dash).
- Acceptance: `--item-audit` shows 0 unread tags; a self-test per tag shows its effect.

**IC-17 · Item tooltip** (L5, I-related)
- Change: build tooltip lines from the item and stack: class-correct damage ("42 tech damage", "drone damage"), ammo family, crit, speed words from use ticks, knockback words, charge cost, calibration name and its deltas, defense, set name and bonus, accessory tag effects in plain words, rarity colour, sell value at the open shop.
- Files: `UI/GraphicalSandbox.cs:3250` (pass the `ItemStack`), maybe a `Systems/ItemTooltip.cs` builder so it can be self-tested.
- Size: M. Dependencies: UI area.
- Acceptance: a self-test over all 1,981 items shows no ranged item claiming "Energy cells" unless it uses cells, and every weapon has a damage line.

**IC-18 · Hurt-path polish** (C7, J3, J4)
- Change: apply ±15% `RollVariance` (luck inverted) to contact and projectile hits on the player. Hostile projectiles skip an immune player (not consumed, no push). Messages show the applied damage. Use hitbox overlap for contact.
- Files: `GameSimulation.cs:1671–1700, 1890–1900`, `GameSimulation.Player.cs:1153`.
- Size: S. Dependencies: enemies area (contact hitboxes).
- Acceptance: 1,000 contact hits on a still player spread over ±15%; a projectile passing through during immunity continues and damages nothing.

**IC-19 · Stack sizes** (K1)
- Change: 9999 for blocks, materials, ammo, potions, bait and critters (keep weapons, armor and tools at 1).
- Files: content registration helpers (`ContentItems`, `ArsenalItems`, `BatchAItems`, …) or one catalog pass after registration, like `DefenseBalance.Apply`.
- Size: S. Dependencies: inventory UI (count text width), save (counts already int).
- Acceptance: catalog test where every non-equipment stackable has MaxStack 9999.

**IC-20 · Signature set abilities** (F4)
- Change: give T5–T7 sets one active or visible ability each (double-tap to dash or shield, a guardian minion, stealth while still, heal bolts for colonists), alongside the existing 8 specials.
- Files: `Content/ArsenalItems.cs` (SetSpecial append), `Gear.cs`, input hook.
- Size: L. Dependencies: controls (double-tap), art.
- Acceptance: one test per ability.

**IC-21 · Consistency fixes** (G11, B16)
- Change: `CombatStatsCalculator.Calculate` respects `Slot6Open`. Route tier-0 energy guns through `FireProjectiles` (spread, crit, class multiplier).
- Files: `Content/CombatModel.cs:338`, `GameSimulation.cs:1197–1240`.
- Size: S. Dependencies: none.
- Acceptance: a locked 6th slot item adds no class damage; a tier-0 gun shot varies by ±15% and can crit.

**IC-22 · Ammo slots** (C4)
- Change: 4 ammo slots (ammo only) checked first by `FindAmmo`; coin slots are optional.
- Files: `ItemSystem.cs` Inventory, `GameSimulation.Player.cs` (`PlayerInventorySlots`), `UI/InventoryUi.cs`, save.
- Size: M. Dependencies: UI/inventory area.
- Acceptance: with arrows in an ammo slot and other arrows in the hotbar, the ammo-slot stack is spent first.

**IC-23 · Max life to Terraria's ceiling** (J9)
- Change: Life cores +20 each, up to 15 (400), and Lifeblooms +5 up to 500. Retune `EnemyHitScale` and potion tiers to match.
- Files: `Entities/Actors.cs:91–92`, `Content/GrowthItems.cs:43–44`, `Content/DefenseBalance.cs`.
- Size: S (code) + balance pass. Dependencies: progression area (crystal counts in worldgen), IC-2.
- Acceptance: a fully upgraded player has 500 max HP; `--balance` survival margins hold per tier.

---

## Cross-area dependencies

| Other area | What this area needs from it |
|---|---|
| Enemies / creatures | Defense values (IC-10), debuff-inflicting creatures (IC-3), contact hitboxes (IC-18), AI helpers for minions (IC-8) |
| Progression / crafting | Potion and charge tier gates and recipes (IC-2), life-crystal counts (IC-23), recipes for new accessories (IC-16) |
| Town NPCs / housing | Reforge service owner (IC-13), potion shops (IC-2), bed spawn and valid rooms (IC-6), Medic pricing |
| Controls / input (agent active now) | Auto-swing hook (IC-15), alt-use minion targeting (IC-8), double-tap set abilities and dash (IC-16, IC-20), confused input inversion (IC-3) |
| UI / HUD | Tooltip (IC-17), rarity colours (IC-14), buff bar debuffs (IC-3), ammo slots (IC-22), projectile drawing (IC-11), minion fallback art (IC-7) |
| Colonists / RimWorld layer | Shared `HealthComponent`: the player-only HP death model (IC-4) |
| Art pipeline | Projectile sprites, 4 minion sprites, new potions and accessories (IC-2, IC-7, IC-11, IC-16). The ComfyUI run is paused at 169/398 and resumes only on the user's go. |
| Save / world | Spawn point, minion persistence, buff duration unit migration (IC-1, IC-6, IC-7) |

---

## Open questions for the user

1. **Player body layer:** should the player keep RimWorld wounds (downed state, lost limbs, bleed-out or heart-failure death at HP > 0), or be HP-only like Terraria while colonists keep the full model? (IC-4)
2. **Classic death:** keep destroying half of each material stack on death, or switch to Terraria Softcore (drop 50% of credits, keep items)? (IC-5)
3. **Buff time:** should buffs run on real time (Terraria minutes, unaffected by sleeping) rather than the game clock? (IC-1)
4. **Explosives:** keep "blasts never hurt the player or break tiles" (B10), or add Terraria-style self-damage and tile-breaking rockets?
5. **Colonist skill effects in combat:** keep the skill damage multiplier and the melee miss chance (A2, B16)?
6. **Max life ceiling:** 300 (current) or Terraria's 500? (IC-23)
7. **Reforging:** which existing town NPC should offer it, or should it be a craftable bench? (IC-13)
8. **Art:** may the projectile and minion sprite work (IC-7, IC-11) join the paused ComfyUI run when you give the go?
