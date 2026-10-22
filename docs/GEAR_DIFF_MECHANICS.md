# Gear mechanics diff: stat formulas and tool rules

This is the formula side of the gear work. Item numbers (damage, use times, tool powers, defense) are tuned separately in `Systems/Content/GearCurve.cs`. This file covers only how stats combine and how tools work.

**Evidence:** `Terraria.exe` v1.4.5.8, read with REA (`docs/terraria-evidence/full/`). Citations use the form `Type.Method @IL offset` and point into `full/cil/*.il.txt`. The summary table is `full/tables/player_stats.md`. Terraria is evidence for rules and numbers only. The code, names and words below are Rimtera's own.

**Builds on:**
- `docs/MECHANICS_PARITY.md`
- `docs/parity/A_movement.md` to `D_world.md`
- `docs/PROGRESSION_DIFF.md`

Where an earlier parity pass chose a Rimtera-tuned shape, for example C1 (±8% spread), C5 (life regeneration) and C6 (charge regeneration), this pass replaces it with the reference formula. `docs/parity/C_combat.md` links here.

**Where the code is:**
- Formulas: `CombatTuning` in `Systems/Content/CombatModel.cs`
- Wiring:
  - `Systems/GameSimulation.Combat.cs`: damage, crit, strike, knockback, regeneration, charge
  - `GameSimulation.cs`: melee, projectiles, mining, reach, placement
  - `GameSimulation.Items.cs`: the hit accumulator, cutters, demolishers, wall placement
  - `GameSimulation.WeaponStyles.cs`: styled swings, thrown weapons, blasts, beams
  - `GameSimulation.Gear.cs`: melee-speed and velocity factors
  - `GameSimulation.Physics.cs`: the shaping hammer
  - `Entities/Actors.cs`: `EnemyArchetype.Defense`, `ProjectileState.Critical`
- Tests: `Systems/StatParitySelfTests.cs` (check names start with `stats`). It is hooked into `CoreSelfTests`.

**Status key:**
- **CHANGED**: Rimtera now follows the rule.
- **MATCHED**: the rule was already in place and was verified.
- **DIFF**: a deliberate difference, with the reason in §8.

## 1. Outgoing damage

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 1.1 | Class damage bonuses are **additive within a class**: `meleeDamage += x` (`ldfld; ldc.r4; add; stfld` throughout `UpdateEquips` and `GrantArmorBenefits`). A few multiplicative penalties exist (`magicDamage *= manaSick…`, `Update @12219/@12250`). | `CombatStatsCalculator` summed gear and set bonuses per class, and `GearAdjust` added the all-class tags and accessory calibrations. | Unchanged. Charge sickness, the mana-sickness analog, multiplies the Tech class multiplier, as the reference does. | MATCHED |
| 1.2 | Weapon damage = `(int)(item.damage × classMult + 5e-6)` (`Player.GetWeaponDamage @0–@30`). The prefix is baked into the base first: `damage = round(damage × dmg)` (`Item.Prefix @305+`). | `(base + ammo) × skill × gear × calibration × sickness`, all floats and all multiplicative. | `CombatTuning.WeaponDamage`: `(round(base × calibration) + ammo) × classMult`, then × the colonist's combat skill. The fraction is kept: no `(int)` before the hit roll. | CHANGED, partly DIFF (D1, D2) |
| 1.3 | Spread: `round(dmg × (1 + rand.Next(-15, 16) × 0.01))`. Positive luck rerolls with chance = luck and keeps the higher result; negative luck keeps the lower (`Main.DamageVar @10–@165`). | ±8%, continuous, no rounding, no luck. | `CombatTuning.RollVariance`: whole-percent roll in [−15, +15], banker's rounding to whole points, luck reroll. Rimtera `Luck` is clamped to ±1 for the reroll chance. | CHANGED |
| 1.4 | Target defense: `max(1, D − def × 0.5)` (`Main.CalculateDamageNPCsTake @0–@39`, `ldc.r8 0.5`). NPC defense is not scaled by game mode (`NPC.StrikeNPC_Inner @27–@64`). | Creatures had **no defense**. | `CombatTuning.CreatureDamageTaken`. The new `EnemyArchetype.Defense` is read by `StrikeEnemy` on every player hit: melee, projectiles, styled swings, thrown weapons, blasts and beams. All creatures default to 0 (D3). | CHANGED (mechanism), values D3 |
| 1.5 | Armor penetration adds back `min(AP, def) / 2` (integer) before defense (`NPC.checkArmorPenetration @0–@92`). The player's AP is `armorPenetration + meleeArmorPenetration` for melee (`Player.GetArmorPenetration`). | None. | `CombatTuning.ArmorPenetrationGiveBack`. Player AP comes from the gear/status tags `ap:N` (any hit) and `ap-melee:N` (close quarters): `GameSimulation.PlayerArmorPenetration`. No item carries them yet. | CHANGED |
| 1.6 | Crit doubles **after** defense: `(int)Max(1, CalculateDamageNPCsTake(D, def) × (crit ? 2 : 1))` (`StrikeNPC_Inner @27–@64`). | Doubled before (there was no defense). | `CreatureDamageTaken(..., critical)`: defense first, then ×2, at least 1. | CHANGED |
| 1.7 | Crit chance: class crit = **4 base** (`ResetEffects @493/@500/@507 ldc.i4.4`) + the held weapon's `crit` (`Update @8466–@8528`) + bonuses. Summon and classless weapons return 0 (`Player.GetWeaponCrit @0–@46`). Roll: `rand.Next(1, 101) <= crit` (`ProcessHitAgainstNPC @432–@449`). | Weapon crit + calibration + gear + luck. No base. A weapon with 0 crit never rolled. Command weapons could crit. | `CombatTuning.WeaponCritChance` = 4 + weapon + calibration + gear + luck, and 0 for Command. `RollCrit` uses the 1–100 roll. | CHANGED (+ luck crit: D4) |
| 1.8 | Order on hit: banner multiplier → spread → AP → defense → crit ×2 → `takenDamageMultiplier` (`ProcessHitAgainstNPC`, `StrikeNPC_Inner`). | Spread and crit at fire time, then banner and affliction factors. | Spread and crit roll once per use (`RollUse`). Each creature hit then goes banner × rolled → AP → defense → crit ×2 → toughness scaling → afflictions (`StrikeEnemy`). | CHANGED, roll timing D5 |
| 1.9 | Knockback on the NPC: `k = kb × knockBackResist`. Soft caps: the part above 8 is kept at 0.9, above 10 at 0.8, above 12 at 0.7, above 14 at 0.6. Hard cap 16. Crit ×1.4 after the caps (`NPC.StrikeNPC_Inner @1413–@1652`). | `push × (1 − resistance)`. No caps; crits had no effect. | `CombatTuning.KnockbackTaken`, used by `KnockEnemy(enemy, push, critical)` for every player hit. Resistance is Rimtera's `KnockbackResistance` (1 − knockBackResist). | CHANGED |
| 1.10 | Player-side knockback bonuses: melee ×2 with the knockback glove, ×1.5 with the titan buff, summon `+= minionKB` (`Player.GetWeaponKnockback @0–@149`). | Calibration factor only. | Unchanged. No Rimtera gear grants a knockback bonus yet. | DIFF (D6) |

## 2. Damage the player takes

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 2.1 | `D − def × 0.5` Classic, `× 0.75` Expert, `× 1.0` Master, minimum 1 (`Main.CalculateDamagePlayersTake @0–@76`). | `DefenseBalance.ApplyDefense`: Explorer and Classic ×0.5, Brutal ×0.75, Annihilation ×1.0, minimum 1. | Unchanged. **Mapping:** Explorer → Classic (×0.5; Explorer is Rimtera's easier, Journey-like world), Classic → Classic (×0.5), Brutal → Expert (×0.75), Annihilation → Master (×1.0). | MATCHED |
| 2.2 | Dodges first (mystic sash, black belt, Brain of Confusion, shadow dodge) (`Player.Hurt @159–@327`). Then defense. Then `(int)((1 − endurance) × dmg)` (`Hurt @986–@1003`). | `GearHitPlayer`: dodge roll, then defense, then endurance. | Unchanged. | MATCHED (caps: D7) |
| 2.3 | Endurance stacks additively and has no cap inside `Hurt`. | Endurance and dodge are both clamped at 60%. | Unchanged. | DIFF (D7) |
| 2.4 | Damage is an integer (`conv.i4`). | Floats. | Unchanged. | DIFF (D8) |

## 3. Speeds

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 3.1 | `meleeSpeed` (attack speed, base 1, additive bonuses) becomes `1 / min(x, 3)` (`Player.CapAttackSpeeds`, `TurnAttackSpeedToUseTimeMultiplier @0–@33`). Melee animation = `max(1, (int)(useAnimation × meleeSpeed))` (`SetItemAnimation[1] @0–@20`, `ApplyItemAnimation @52–@289`). Other classes ignore melee speed. | `round(ticks × calibration × 1/(1 + attackspeed%))`. No cap. | `ScaledCooldown`: `CalibratedUseTicks` (rounded), then for Melee `MeleeUseTicks(ticks, UseTimeMultiplier(1 + attackspeed%))`. Attack speed is capped at 3×, with at least 1 tick. Other classes use the calibrated ticks. | CHANGED |
| 3.2 | Prefix speed is baked in: `useAnimation`/`useTime`/`reuseDelay = round(x × spd)` (`Item.Prefix @327–@391`). | Float product, rounded once at the end. | Rounded into the item first (`CalibratedUseTicks`). | CHANGED |
| 3.3 | Pickaxe swing: `itemTime = (int)(useTime × pickSpeed)` (`ActuallyUseMiningTool @1041–@1059`). `pickSpeed` = 1 − bonuses, floored at 0.3 (`Update @12820`). | The `mining:N` tag and colonist mining skill multiplied **per-hit damage**. The swing used the raw cooldown. | `PickSpeed = CombatTuning.PickSpeed(mining%)` (1 − N/100, floor 0.3). Drill swing = `DrillUseTicks(useTicks, PickSpeed)`, then divided by colonist mining skill speed (D1). Per-hit damage no longer depends on speed. | CHANGED |
| 3.4 | Tile and wall placement: `useAnimation × tileSpeed` (or `× wallSpeed`), where the speeds are capped at 3 and then inverted (`Update @12344–@12412`, `ApplyItemAnimation`). | Fixed 6 ticks per block and 4 per wall. | `CombatTuning.PlacementTicks(base, build%)` = `(int)(ticks / min(1 + build%, 3))`, at least 1. It is read from the gear/status tag `build:N` for blocks and walls. No item or status carries `build:N` yet. The Builder status gives reach only. | CHANGED (hook) |

## 4. Mining and tools

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 4.1 | Hits accumulate per tile and the tile breaks at **100** (`ActuallyUseMiningTool @288/@642`, `HitTile.AddDamage >= 100`). | Each tile had its own hit points (16–120). Damage per hit was `ceil(power × skill × gear / (10 × resistance))`, so a weak drill needed up to 40 hits on basalt. | `AddTileHitDamage`: hit damage is kept per cell in `_tileHitDamage` (0–99) and the block breaks at 100. The cell's crack damage is set to the same share of its hit points, so the crack drawing still shows progress. Damage from other sources (blasts, old saves) is read back as a share. Healing after 5 s clears both. | CHANGED |
| 4.2 | Per-hit pick damage by tile (`Player.GetPickaxeDamage @0–@1039`, integer division): ×2 on dirt, clay, sand, ash, mud, silt, slush, snow and hardened sand. ×1 by default. ½ on dungeon brick, hellstone, ebonstone, pearlstone and crimstone. ½ / ⅓ / ¼ on the three hardmode ore pairs. ⅕ on chlorophyte. ¼ on lihzahrd. 100 on rope-like and "no-fail" tiles. | Continuous: power ÷ (10 × resistance) against per-tile HP. | `CombatTuning.DrillHardness` + `DrillDamage`. The mapping is by role in Rimtera's progression (table in §4a). Soft ground is any other tile with resistance ≤ 1.0. Fragile tiles (thin ice, mite silk) break in one hit. | CHANGED (mapping D9) |
| 4.3 | Minimum pick power gates: 0 damage below the gate (`GetPickaxeDamage @339–@865`). Mythril 110, adamantite 150, and so on. | `TileDefinition.MinimumToolPower`, checked in `Mine` ("needs a stronger drill"). | Unchanged. `DrillDamage` also returns 0 below the gate. **The progression gates are kept: lumenite 85, voidsteel 100** (they line up with the reference's 110 and 150 through `GearCurve.DrillPowerFromTerraria`). | MATCHED |
| 4.4 | Axes: `tileDamage += (int)(item.axe × 1.2)`, ×3 on cactus. The `axe` field is tooltip power ÷ 5 (`ActuallyUseMiningTool @437–@505`). Trees break at 100. | `ceil(power × 2 / (10 × resistance))` against tile HP. | `CombatTuning.CutterDamage(power, cactus)` = `(int)(power/5 × 1.2)` (×3 cactus), accumulated to 100 in `Cut`. Cutter power reads like the axe tooltip percentage. | CHANGED |
| 4.5 | Hammers on walls: `(int)(hammer × 1.5)` per hit through `PickWall`, which breaks at 100. The wall-hit swing time is `useTime / 2` (`ItemCheck_UseMiningTools_TryHittingWall`, raw body in `members-Terraria.Player.json`: `ldfld hammer; conv.r4; ldc.r4 1.5; mul; conv.i4 … ldfld useTime; ldc.i4.2; div`). The wall must have an exposed side. | Any hit on an exposed wall removed it at once. | `HammerWall(point, power)` adds `WallHammerDamage(power)` toward 100 (`_wallHits`, which heals after 5 s). Demolisher wall hits use half the use time. The exposed-side rule is unchanged. The shaping hammer uses its own power. **Superseded by `TOOLS_PARITY.md` 5.7–5.9:** built walls need no open side, natural walls count diagonal neighbours, and wall damage mends through the hit ledger, not a timer. | CHANGED |
| 4.6 | Altars (tile 26) need `hammer >= 80` (`ActuallyUseMiningTool @222`). | Smashed by tools with the altar-breaker capability after the Fracture (`CanSmashAltars`). | Unchanged. | DIFF (progression rule, `PROGRESSION_DIFF.md` G4) |
| 4.7 | Tile reach is a **box**: `[leftTile − (tileRangeX + tileBoost), rightTile + (…)]`, and the same vertically. `tileRangeX = 5`, `tileRangeY = 3` (`Player..cctor @158/@164`; `TileReachCheckSettings.GetTileRegion`). | A circle of radius 5 from the body centre, plus the `reach` tag. | `CombatTuning.InTileReach`: 5 cells sideways and 3 cells up or down past the cells the body touches, each plus `floor(reach tag)`. Used by `IsInReach` for mining, cutting, hammering and placing. The placement system receives a radius that circumscribes the box, so the box decides. | CHANGED |
| 4.8 | Partly mined tiles heal (`HitTile` decay). | Healed after 5 s (parity B2). | Also clears the 0–100 accumulator, and wall damage heals too. **Superseded by `TOOLS_PARITY.md` 3.8:** the reference only mends damage as later hits age it (`HitTile.Prune` per damaging hit), never with time; Rimtera now does the same. | CHANGED (see TOOLS_PARITY) |

### 4a. Drill hardness mapping (Rimtera tile → reference role)

| Hardness | Per-hit damage | Rimtera tiles | Reference role |
|---|---|---|---|
| fragile | 100 (one hit) | thin ice, mite silk (`TileTraits.Fragile`) | ropes and no-fail tiles |
| soft | power × 2 | every tile with mining resistance ≤ 1.0: regolith, moss, sands, ice, mud, clay, silt, ash, hull panel, glass, nectar, furniture… | dirt, sand, mud, clay, ash, silt, snow, slush |
| 1 | power | everything else: basalt, ferrite, scrap iron, cobalt, glacial, uranium, meteorite, slate, bricks… | stone and pre-hardmode ores |
| 2 | power / 2 | void rock (CorruptRock), pearl stone, derelict plating (DungeonBrick), pyrocite ore, **viridium ore** | ebonstone/pearlstone, dungeon brick, hellstone, cobalt/palladium |
| 3 | power / 3 | **lumenite ore** | mythril/orichalcum |
| 4 | power / 4 | **voidsteel ore**, precursor brick and glyph | adamantite/titanium, lihzahrd brick |

With the ferrite drill (power 35), basalt takes 3 hits and regolith 2, the same counts as a copper pickaxe on stone and dirt. A lumenite drill (103) on voidsteel ore (÷4) does 25 per hit, so 4 hits. The reference's adamantite pick on titanium ore also takes 4.

## 5. Life regeneration

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 5.1 | `lifeRegen` is in half-HP-per-second units: `lifeRegenCount += lifeRegen` per tick, and +1 HP per 120 (`UpdateLifeRegen @2025–@2462`). | HP per second from a Rimtera curve. | `CombatTuning.NaturalLifeRegenUnits`, converted with `RegenUnitsPerSecond` (units × 0.5). | CHANGED |
| 5.2 | The natural term counts the thresholds reached by `lifeRegenTime`: ≥300, 600, 900, 1200, 1500, 1800, 2400, 3000 ticks, +1 at 3600, where the timer caps (`@1433–@1693`). The timer grows 1 per tick (`@956`), plus 3 more while sitting or sleeping (`@1724–@1737`). It resets on a hurt and on damage-over-time debuffs (`stfld lifeRegenTime 0` at `@58…@688` and in `Hurt`). | A 4 s delay, then a linear 26 s ramp. Reset on any HP loss. | The same thresholds, from `SecondsSinceHurt` × 60. The timer runs 4× while asleep and resets on any HP loss, which includes damage over time. | CHANGED |
| 5.3 | Multipliers: ×1.3 sitting or asleep; ×1.25 when `velocity.X == 0` or grappling, otherwise ×0.5; Expert without well-fed ÷2; then `× (statLifeMax2/400 × 0.85 + 0.15)`; `lifeRegen += round(r)` (`@1698–@2020`). | ×1.75 when grounded and still. | ×1.3 asleep. ×1.25 with no sideways speed, else ×0.5. ÷2 on **Brutal and Annihilation** unless a well-fed tier is active. × max-life scaling. Rounded with banker's rounding. | CHANGED |
| 5.4 | Flat regen from items (`lifeRegen += item.lifeRegen`, `GrantArmorBenefits`) is added in the same units, and debuffs subtract. | Gear and status `LifeRegenerationPerHour` (per game hour). | Unchanged, applied on top. | DIFF (D10) |

Examples (100 max life, standing still): 0 HP/s for the first 5–10 s, 0.5 HP/s at 10 s, 1.5 HP/s at 30 s, 2 HP/s after a minute. At 400 max life the full rate is 5.5 HP/s.

## 6. Charge (mana)

| # | Terraria rule (IL) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 6.1 | Delay after spending: `(int)(((1 − mana/max) × 60 × 4 + 45) × 0.7)` ticks, at most 20 with the mana-regeneration buff (`Player.ApplyManaRegenerationDelay @0–@89`). | A flat 1.1 s. | `CombatTuning.ChargeRegenDelayTicks`, set by every spend in the `Charge` setter. The buff is Rimtera's charge stim (`StatusId.ChargeSurge`). | CHANGED |
| 6.2 | The delay counts down 1 per tick, plus 1 more when standing still, grappling or buffed (`UpdateManaRegen @41–@144`). There is no regeneration during it (`@498`). | — | `ChargeDelayCountdownPerTick`. | CHANGED |
| 6.3 | After the delay: `statManaMax2/3 + manaRegenBonus + 1`, doubled when still or buffed, `× (0.5 + 0.5 × mana/max)` (×1 when buffed), `conv.i4`, minimum 2. Count per tick, +1 mana per 120 (`@224–@491`, `@933–@998`). | 3/s + bonuses, a 20%→100% ramp over 1.6 s, ×1.5 still, ×2 with the stim. | `CombatTuning.ChargeRegenUnits`, converted with units × 0.5 per second. Rimtera's per-second gear and status bonuses become units ×2 (D10). `ChargeRegenPerSecond` reports the current post-delay rate. | CHANGED |
| 6.4 | Mana cost: `round(mana × mcst)` is baked in by the prefix (`Item.Prefix @396–@414`), then × the player's `manaCost` (`CheckMana`, lite view). | `ChargeCost × calibration`, as a float. | `CombatTuning.ChargeCost`: `(int)(round(cost × calibration) × wearer multiplier)`. The wearer multiplier comes from the tag `chargecost:N` (−8 = 8% cheaper). Used by Tech attacks, staves and beams. | CHANGED |
| 6.5 | Max mana is capped at 400 (`Update @12428`). | No cap. | `MaximumCharge` is capped at `CombatTuning.MaximumChargeCap` = 400. | CHANGED |
| 6.6 | Mana sickness: `magicDamage *= (1 − manaSickReduction)`, fading out (`Update @12219`). | Charge sickness: up to −25% Tech damage, fading over 6 s. | Unchanged; it now multiplies the class multiplier (1.1). | MATCHED |

## 7. Calibrations (the prefix analog)

| # | Terraria rule (IL, `Item.Prefix @305–@698`, `prefixes.csv`) | Rimtera before | Rimtera now | Status |
|---|---|---|---|---|
| 7.1 | `damage = round(damage × dmg)` | Float multiply at use time. | Rounded into the base (`WeaponDamage`). | CHANGED |
| 7.2 | `useAnimation/useTime/reuseDelay = round(x × spd)` | Float multiply. | `CalibratedUseTicks` (rounded) before melee speed. | CHANGED |
| 7.3 | `mana = round(mana × mcst)` | Float multiply. | `ChargeCost` (rounded, then × the wearer multiplier). | CHANGED |
| 7.4 | `knockBack *= kb` | Multiplied. | Unchanged. | MATCHED |
| 7.5 | `scale *= size` | `CalibratedSize` scales melee reach and arc. | Unchanged. | MATCHED |
| 7.6 | `shootSpeed *= vel` on any item that shoots. | Applied only to Ranged weapons. | Applied to every class (`GearProjectileSpeedFactor`). | CHANGED |
| 7.7 | `crit += crt` | Added. | Added on top of the 4% base (1.7). | MATCHED |
| 7.8 | Accessory prefixes: +1–4 defense, +2/+4 crit, +1–4% damage, +1–4% move speed, +1–4% melee speed, +20 mana (`GrantPrefixBenefits @19–@772`). | Defense, crit, damage and move-speed accessory calibrations. | Unchanged. There are no melee-speed or +charge accessory calibrations. | DIFF (D11) |
| 7.9 | `Prefix(-1)` gives no prefix 1 time in 4 (`@165–@179`). | Every calibratable craft rolls a calibration. | Unchanged. | DIFF (D11) |

## 8. Deliberate differences

- **D1. Colonist skills (RimWorld layer).**
  - Melee, shooting, Intellectual (Tech) and Social (Command) skills multiply weapon damage after the reference formula: `CombatStatsCalculator.SkillMultiplier`.
  - Melee swings can miss on the colonist's melee hit chance.
  - The colonist's mining skill (0.65–1.35) divides drill swing time. Before, it multiplied damage per hit.
  - Manipulation and the extreme-mood penalty scale every action cooldown (`SetPlayerCooldown`).
  - These are what makes the player a colonist, so they stay.
- **D2. No truncation before the roll.** The reference truncates weapon damage to an integer before the spread. Rimtera keeps the fraction until the spread, which rounds to whole points. The difference is under one point. `ContentSelfTests` asserts exact multiplicative class scaling ("Tech module = ×1.12"), so truncating would break that contract.
- **D3. Creature defense values are all 0.** The mechanism is live: `EnemyArchetype.Defense` → `StrikeEnemy` → `CreatureDamageTaken`, with armor penetration. Giving the roster defense numbers is a content-tuning task. It changes time-to-kill for every weapon, so it belongs with the item and creature retune (`GearCurve`), not with the formulas.
- **D4. Luck adds crit.** Rimtera's `Luck` also gives `(int)(luck × 4)` crit (`LuckCritBonus`), on top of the reference's luck reroll of the spread, which is now in.
- **D5. One roll per use.** The spread and crit are rolled once per swing, shot, throw or beam pulse, and every creature that use hits shares the roll. Defense and the crit doubling still apply per creature. The reference rolls per creature hit. The per-hit distribution is the same. Existing contracts expect two creatures hit by one broadsword sweep to lose the same amount (`WeaponStyleSelfTests`).
- **D6. No player-side knockback bonuses.** No Rimtera gear or status grants them. A future glove or titan-like buff would multiply the weapon's knockback before `KnockbackTaken`.
- **D7. Dodge and endurance caps (60%).** These are kept as safety rails in `GearHitPlayer`. Rimtera stacks set specials, accessory tags and statuses, and the reference has no cap. No current loadout reaches the caps.
- **D8. Float hit points.** Player and creature hit points are floats. Damage over time, regeneration and difficulty scaling stay continuous. Weapon hits are whole numbers after the spread.
- **D9. Hardness by role.** Rimtera's tile table has its own hit points and resistance, and the reference has fixed per-tile cases. Each ore and special block is mapped to its progression role (§4a), and the rest are classified by resistance. Hit points now only drive the crack drawing.
- **D10. Gear regeneration units.** Gear and status life regeneration stays in Rimtera's per-game-hour stat (`LifeRegenerationPerHour`) and is added on top. Gear charge regeneration is per second and is converted ×2 into regen units. The reference's campfire, heart lantern, shiny stone and similar are not modelled.
- **D11. Calibration table and rolls.** The calibration rows (names, weights, magnitudes) are Rimtera's own. Their crit bonuses go up to 8 (the reference's largest is 8, `Legendary2`). The 25% "no prefix" roll is not used, because crafted gear always has a calibration in Rimtera's crafting loop. Accessory melee-speed and +charge calibrations do not exist yet.
- **Difficulty mapping.** Explorer and Classic → reference Classic. Brutal → Expert. Annihilation → Master. This covers defense (§2.1) and regeneration halving (§5.3). Enemy damage and health scaling by mode is `WorldSettings.EnemyScale` (unchanged, outside this pass).
- **Summon specifics.** Whip attack speed (`summonerWeaponSpeedBonus`), whip tag damage and sentry/minion knockback (`minionKB`) are not modelled. Command hits get the spread, never crit, and use the strike pipeline.

## 9. Tests

`Systems/StatParitySelfTests.cs`, 38 checks, all passing. They cover:
- the additive class bonus
- the weapon formula, in pure form and in game
- spread range, rounding and luck
- defense, armor penetration and crit order, in pure form and in game
- the base crit chance and Command exclusion
- crit frequency
- knockback caps and crit push
- player defense by difficulty
- attack-speed and calibration rounding, and the melee-speed gear in game
- pick speed and the mining buff in game
- placement time
- the hardness mapping and drill damage
- that the ore gates are kept
- in-game hits to break basalt and regolith, crack display and healing
- box reach, in pure form and in game at exactly 5 cells
- cutter and hammer formulas, and in-game wall hits
- regeneration steps and multipliers, and in-game timing plus Brutal/well-fed
- charge delay, rate, cost and cap, and the in-game delay and recovery
- calibration scaling of damage, time, crit and cost

Existing parity probes updated for the new contracts (these files are not `*SelfTests.cs`):
- `ParityCombatTests` C1: ±15% spread, crit range 170–230, 4% base crit.
- `ParityCombatTests` C3: tolerance for the spread.
- `ParityCombatTests` C5: timings at 2 s, 11 s and 41 s.
- `ParityCombatTests` C6: the delay and rate formulas.
- `ParityBuildTests` B5: two demolisher hits per wall, and the cutter held for enough chops.

**Existing `*SelfTests.cs` checks that now fail because of the tool rules.** I did not edit these files, per instructions. Each needs a small change by their owner:

1. `ContentSelfTests` "…demolishers refund them" uses the ferrite demolisher once on a wall. Its power is 35, which gives (int)(35 × 1.5) = 52 per hit, so the wall needs 2 hits. **Fix:** use it twice, or loop until the wall is gone.
2. `BatchASelfTests` "saplings grow into trees, and chopping the trunk fells the whole tree…" holds the ferrite cutter for 90 ticks. The cutter has power 35 and a 21-tick use time in `GearCurve`. It adds (int)(7 × 1.2) = 8 per chop, so felling needs 13 chops ≈ 273 ticks. **Fix:** hold it for about 300 ticks.
3. `BatchASelfTests` "using a rope on a rope extends it downward" fails only because the tree from check 2 is still standing in the rope's cell ("Cannot place: occupied"). It passes once check 2 passes.

`CoreSelfTests` stops at the first failure, so check 1 hides every later suite in a normal run. With checks that fail but do not stop the run, everything else passes: 497 checks.
