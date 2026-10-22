# Terraria.Player — Equipment, Buffs, and Regeneration

## Evidence identity and scope

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`; assembly `Terraria` 1.4.5.8.
- SHA-256: `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- Static metadata/CIL plus selected type/member decompilation with ILSpy 11.1.0.9782. Target was not launched. Method names below refer to this exact build.

## 1. Stat variables and exact cap arithmetic

| State | Exact initialization / update | Gate / bound |
|---|---|---|
| `statLifeMax` | field default `100` | Persistent/base maximum; life crystal increases by `20` while `<400`; life fruit adds `5` only when `400 <= statLifeMax < 500`. |
| `statManaMax` | set to `20` during player initialization | Mana crystal adds `20` while `<200`. |
| `statLifeMax2` | each player update: `statLifeMax2 = statLifeMax`; buff/equipment effects add temporary capacity | Life fruit/crystal use paths also immediately add their increment to `statLifeMax2` and `statLife`. |
| `statManaMax2` | each player update: `statManaMax2 = statManaMax`; equipment/buff effects add temporary capacity | End-of-update clamp: `statManaMax2 = min(statManaMax2, 400)`. |
| `statDefense` | accumulated from effective armor, prefixes, buffs, and other effects | Final floor: `statDefense = max(statDefense, 0)`. |
| `lifeRegen`, `lifeRegenTime`, `lifeRegenCount` | signed rate accumulator, floating regen timer, and integer fractional-health accumulator | Health conversion thresholds use `120` accumulator units per HP. |
| `manaRegen`, `manaRegenDelay`, `manaRegenCount` | signed mana rate, floating delay, and integer accumulator | Mana conversion threshold is `120` accumulator units per mana. |

Cap item routes: `ItemCheck_UseLifeCrystal(Item)` checks item type `29`, positive animation, `statLifeMax < 400`, and `ItemTimeIsZero`; it adds `20`. `ItemCheck_UseLifeFruit(Item)` checks type `1291`, `400 <= statLifeMax < 500`, and `ItemTimeIsZero`; it adds `5`. `ItemCheck_UseManaCrystal(Item)` checks type `109`, positive animation, `statManaMax < 200`, and `ItemTimeIsZero`; it adds `20`.

## 2. Effective equipment and slot/property matrix

| Slot/index | Role observed in `UpdateEquips` | Benefits path |
|---:|---|---|
| `0` | head armor | `GrantArmorBenefits` when effective item is non-air, unlocked/usable, expert gate passes, and `headSlot > -1`. |
| `1` | body armor | same gate with `bodySlot > -1`. |
| `2` | leg armor | same gate with `legSlot > -1`. |
| `3..9` | functional accessory slots | `GrantArmorBenefits` requires `item.accessory`; `ApplyEquipFunctional` runs for unlocked usable slots. |
| `10..12` | vanity armor overrides for head/body/legs | `GetEffectiveArmor(10..12)` supplies vanity sprite IDs when present, otherwise visual code falls back to functional slots `0..2`. |
| `13..19` | vanity accessory/effect slots | `ApplyEquipVanity`; visual/effect flags and shaders, separate from the functional accessory pass. |
| inventory `0..57`, plus special slot `58` check | passive inventory scans for selected accessories and special items | selected inventory-wide feature scans; not a substitute for armor-slot eligibility. |

`GetEffectiveArmor(slot)` returns `armor[slot]` when non-air. If the slot is air, it walks `Loadouts` in array order and selects the first non-air, favorited candidate that passes `CanShareArmor`. Sharing checks `ItemSlot.CanEquipInArmorSlot`; accessory sharing also checks compatibility with favored items in preceding accessory slots through `ItemSlot.CanEquipBothAccessories`. If none qualifies, it returns the active armor slot and reports no source loadout.

`UpdateEquips(i)` order: scan inventory slots for selected passive accessory state; scan effective armor slots `0..9` and apply prefix + armor benefits; optionally scan bank4 if the matching void-bag-related flag is set; apply functional accessory effects over `3..9`; apply vanity effects over `13..19`; then apply aggregated accessory/environment/team modifiers and update mount effects. `ApplyEquipVanity` and `ApplyEquipFunctional` are distinct routines. Functional visual suppression checks `hideVisibleAccessory[itemSlot]` around cosmetic visuals; it does not replace the slot/type eligibility check used for stat benefits.

`GrantArmorBenefits(armorPiece)` includes `statDefense += armorPiece.defense` and `lifeRegen += armorPiece.lifeRegen`, then type-specific effects. `GrantPrefixBenefits` applies prefix-conditioned stat deltas. Observed prefix defense entries: prefix `62` `+1`, `63` `+2`, `64` `+3`, `65` `+4`; prefix `66` adds `20` to `statManaMax2`. The observed global example modifiers in `UpdateEquips` include: `skyStoneEffects` => `lifeRegen +2`, defense `+4`, melee speed/damage `+0.1`, melee crit `+2`, ranged/magic damage `+0.1`, ranged/magic crit `+2`, pick speed `-0.15`, minion damage `+0.1`, minion knockback `+0.5`; the DD2 accessory flag gives minion damage `+0.1` and `maxTurrets +1`.

Armor-set dispatch occurs after equipment application: `ArmorSetBonuses.GetCompleteSet(new ArmorSetBonus.QueryContext(this))?.Effect(this)`, followed by the always-run Beetle, Solar, Stardust, Chlorophyte, and Vortex routines, then armor sound/dust changes. This is an effect/query registry, not one universal three-piece ID equality test in `UpdateArmorSets`.

## 3. Buff ticker and immunity state machine

### 3.1 Per-slot ticker

```text
for j in [0, maxBuffs):
    if buffType[j] <= 0 or buffTime[j] <= 0: continue
    if whoAmI == Main.myPlayer and not BuffID.Sets.TimeLeftDoesNotDecrease[buffType[j]]:
        buffTime[j] -= 1
    dispatch active effect by buffType[j]
    apply derived flags/stat deltas/status interactions
```

The decrement is one `buffTime` unit per player update and is authority-gated to `Main.myPlayer`; `BuffID.Sets.TimeLeftDoesNotDecrease[type]` exempts selected buffs. A valid mount buff resolves its mount through `BuffID.Sets.MountType[type]`, calls `mount.SetMount(...)`, and resets that slot's time to `10`. Several stack/visual buff groups refresh to short sentinels (e.g. `5`) rather than behaving as ordinary countdowns. After the loop, `UpdateHungerBuffs()` runs and derived visual state is updated.

Before `UpdateBuffs`, the player update clears `buffImmune[0..BuffID.Count)` to false. The ticker and equipment/status logic rebuild immunity flags; `AddBuff(type,time,...)` rejects immediately when `buffImmune[type]` is true. Example observed in the ticker: buff type `1` sets `lavaImmune=true` and `buffImmune[24]=true`. Thus immunity is an indexed, recomputed state gate, not a permanent property inferred only from the debuff ID. Environment-derived state is separately carried by flags such as `wet`, `lavaWet`, `honey`, `ZoneDesert`, `ZoneSnow`, `lavaImmune`, and status booleans.

### 3.2 Damage-over-time / regeneration interaction matrix

Before positive regeneration is calculated, each active harmful condition cancels positive `lifeRegen` (`if lifeRegen > 0: lifeRegen = 0`) and resets `lifeRegenTime=0`; the listed amount is then subtracted. Values are `lifeRegen` accumulator units per update, not HP per second.

| Status condition | Per-update `lifeRegen` delta | Additional rule |
|---|---:|---|
| `poisoned` | `-4` | resets timer; cancels positive regen |
| `venom` | `-30` | same |
| `chlorophyteSpore` | `-30` | same |
| `onFire` | `-8` normally; `-100` when `Main.vampireSeed` | `drippingSlime` applies the same amount again |
| `onFire3` | `-8` | `drippingSlime` repeats the penalty |
| `onFrostBurn`, `onFrostBurn2` | `-16` each | each status branch separately applies; `drippingSlime` repeats each |
| `onFire2` | `-24` | `drippingSlime` repeats the penalty |
| `burned` | `-60` | `drippingSlime` repeats the penalty |
| `suffocating` | `-40` | special damage conversion below |
| `electrified` | `-8`, plus `-32` if `controlLeft || controlRight` | special ordinary DoT conversion |
| `tongued && Main.expertMode` | `-100` | special damage conversion below |
| `honey && lifeRegen < 0` | `+4` toward zero | clamp result back to `<=0` |
| `nebulaLevelLife > 0 && lifeRegen < 0` | set to `0` | negates current negative regen accumulator |
| Shiny Stone active/eligible (`flag`) | negative `lifeRegen /= 2`; later `+4` | special timer acceleration and additional positive count |

### 3.3 Life-regeneration equation and conversion

Let `t = lifeRegenTime`. The base positive regeneration score `r` is the sum of threshold crossings: `+1` for each of `t >= {300,600,900,1200,1500,1800,2400,3000}`. With the Shiny Stone `flag`, add `clamp((t-3000)/300, 0, 30)` for `t>3000`; without it, `t>=3600` contributes one more and clamps `t=3600`.

Multiplier order observed:

1. Sitting or sleeping: add `3` to `lifeRegenTime`, multiply `r` by `1.3`.
2. Horizontal movement with no grappling hook (`velocity.X != 0 && grappling[0] < 0`): `r *= 0.5`; otherwise `r *= 1.25`.
3. Crimson regeneration `×1.5`; Shiny Stone `×1.1`; local-player campfire `×1.1`.
4. Expert mode and not `wellFed`: `÷2`, or `×0.75` if Shiny Stone. `rabid` applies the same branch.
5. Maximum-life scale: `r *= (statLifeMax2 / 400f) * 0.85f + 0.15f`.
6. Add `(int)Math.Round(r)` to `lifeRegen`; then `lifeRegenCount += lifeRegen`. Palladium regeneration adds `4` to the count; eligible Shiny Stone positive regen adds another `1` when below max life.

Each `lifeRegenCount >= 120` converts to `+1` HP and subtracts `120`, bounded at `statLifeMax2`. Ordinary negative conversion consumes `120` per damage event, with `num14=min(lifeRegenCount / -120, 4)` damage units per pass. `burned`, `suffocating`, or expert `tongued` instead consume `600` per `5`-damage event. Starvation sets `num2=max(4, 120*statLifeMax2/3000)`, then its direct damage size is `max(2,statLifeMax2/50)` (doubled in desert or snow) every `120*num12` negative count units. Bleeding forces `lifeRegenTime=0`. Soul Drain adds `(5+soulDrain)/2` to both `lifeRegenTime` and `lifeRegen`; Honey adds `2` to each; Hearty Meal adds `3*120/60 = 6` to `lifeRegen`. Local-player campfire and Heart Lantern add `1` and `2` respectively.

## 4. Mana-regeneration equation

If `manaRegenDelay > 0`, per update subtract `1 + manaRegenDelayBonus`; subtract one extra when standing still, grappling, or `manaRegenBuff`; subtract `0.05` for a used Arcane Crystal. Regeneration activates when the resulting delay is `<=0` (with a debug-only ManaV2 near-delay override).

```text
base = statManaMax2 / 3 + manaRegenBonus + 1            // integer arithmetic
if standingStill || grappling[0] >= 0 || manaRegenBuff:
    base += base
if usedArcaneCrystal:
    base += statManaMax2 / 50
q = statMana / statManaMax2
f = (manaRegenBuff ? 1.0 : 0.5) + (1.0 - (manaRegenBuff ? 1.0 : 0.5)) * q
manaRegen = max(2, int(base * f))                        // truncating cast
```

When inactive, `manaRegen=0`. Each update adds it to `manaRegenCount`; while `manaRegenCount >= 120`, subtract `120` and add one mana up to `statManaMax2`. Full-mana state is clamped to `statManaMax2`. A debug-only ManaV2 use/delay modifier is not part of the default path.

## 5. Structural update order and conditional logic array

```text
[0] Reset temporary caps: statLifeMax2 <- statLifeMax; statManaMax2 <- statManaMax; reset transient equipment/status flags.
[1] Recompute nearby/environment statuses and clear buffImmune[0..BuffID.Count).
[2] UpdateBuffs(i): per-slot validity test -> authority-aware decrement exception -> buff-type effect dispatch.
[3] Recompute mount, water/lava, hunger, debuff and dynamic immunity state; remove expired local-player buff slots.
[4] UpdateEquips(i): resolve GetEffectiveArmor/loadout-sharing; apply prefix and armor stats in slots 0..9; functional accessory pass 3..9; vanity pass 13..19.
[5] Apply aggregate equipment/environment/team modifiers; clamp mana max to 400 and defense to >=0.
[6] UpdateArmorSets(i): complete-set query effect, five always-routines, armor sound/dust.
[7] UpdateLifeRegen and UpdateManaRegen accumulate signed regen counters and convert each 120 units to one point, with status-specific damage paths.
```
