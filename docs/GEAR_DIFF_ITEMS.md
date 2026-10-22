# Gear diff: Rimtera tools, weapons, armour and accessories vs Terraria's item numbers

This document compares Rimtera's gear, tier by tier, with the item numbers in `Terraria.exe` v1.4.5.8
(`docs/terraria-evidence/full/tables/`: `tools.csv`, `weapons.csv`, `armor.csv`, `armor_sets.csv`, `accessories.csv`).
It also records the retune that puts Rimtera's gear on Terraria's curve.

Terraria is used as numeric and behavioural evidence only. Every Rimtera item keeps its own name. Only the numbers and
the shape of the progression follow Terraria.

- **Before** means the values as they were before this pass. **After** means the values the catalog builds now.
- Rows marked *(knowledge)* use Terraria facts that are not in the extracted tables. NPC stats are not tabulated
  (see the evidence README, caveat 7).
- All tier numbers now come from one file, `Systems/Content/GearCurve.cs`. It is checked by
  `Systems/GearCurveSelfTests.cs`.

## 0. Tier mapping

The mapping in the brief holds in the code, with three refinements:

| Rimtera tier | Bar or ore (code) | Terraria tier it stands for | Evidence in Rimtera |
|---|---|---|---|
| 1 Ferrite | `FerriteBar`, `TileId.CopperOre` (gate 0) | Copper and tin | Starting kit: stiletto, drill and hatchet (`StarterKits.Standard`) |
| 2 Cobalt | `CobaltBar`, cobalt ore (gate 35) | Iron and lead | Power Forge recipes; the Creative kit |
| 3 Glacial, Pyrocite, Uranium, Meteorite | Biome bars (gate 35, meteorite 50) | Silver through platinum, meteor, demonite and crimtane, **and hellstone** (pyrocite) | Pyrocite is the Irradiated Core ore, Rimtera's underworld analog |
| 4 Viridium | `ViridiumBar`. The ore is mineable at 35, but the Alloy Anvil needs fission slag from the Fission Tyrant (the Wall of Flesh analog) | Cobalt and palladium, the first hardmode ore | The Core breaker (Pwnhammer analog) is tier 4 |
| 5 Lumenite | Ore gate **85** | Mythril and orichalcum (gate 110) | |
| 6 Voidsteel | Ore gate **100** | Adamantite and titanium (gate 150), plus hallowed | The vault and Mothership drops are tier 6 |
| 7 Astral | Astral ore, a drop from the Astro-God | Luminite, after Moon Lord | |

Refinements:
1. Pyrocite is the hellstone tier inside tier 3. The pyrocite drill is the Molten Pickaxe analog.
2. Viridium ore can be mined early, but viridium **gear** comes after the Fracture. Viridium therefore stands for
   cobalt and palladium, not for anything before hardmode.
3. Tier 6 also takes the post-mechanical-boss "hallowed" band. Rimtera has no separate chlorophyte or spectre tier.

## 1. Scale check: creature hit points and hits against the player

Two facts drive how the numbers are mapped.

1. **Rimtera's creatures have no defense**, while Terraria's NPCs subtract half their defense from each hit
   (`NPC.StrikeNPC`). A zombie with 6 defense turns a copper broadsword's 9 into 6.
2. **The hit-point curve differs.** Below are the medians of the non-passive spawn-table rosters
   (`BiomeSpawnTables`), set against Terraria's typical tier creatures *(knowledge)*:

| Tier | Rimtera roster median HP / hit (hit × `EnemyHitScale`) | Terraria typical HP / hit *(knowledge)* | Rimtera HP ÷ Terraria HP, normalised to tier 1 |
|---|---|---|---|
| 1 (surface) | 25 / 8 | ~35 (slimes 14-25, zombie 45, demon eye 60) / ~12 | 1.00 |
| 2 (caverns, frozen, coast) | 60 / 12 | ~60 (skeleton 60, undead miner 70) / ~20 | 1.40 |
| 3 (radiated, core, fungal, canopy, hives) | 65 / 14 | ~55 (hornet 46, man eater 130, angry bones 76, hellbat 35) / ~30 | 1.65 |
| 4 (post-Fracture tables) | 150 / 22 (the 150-400 HP band: 230 / 35) | ~220 (wraith 200, possessed armor 260, werewolf 400) / ~60 | 0.95 |
| 5 (crystal spires) | 190 / 32 | ~250 (same early-hardmode pool) / ~65 | 1.06 |
| 6 (vault and late) | 240 / 37 | ~300 (adamantite era) / ~75 | 1.12 |
| 7 | None (gear comes after the final boss); 400 used | ~800 (pillar enemies) | 0.70 |

Bosses sit on a very different scale. The gate boss has 520 HP against Eye of Cthulhu's 2,800. The Fission Tyrant has
1,150 against Wall of Flesh's 8,000. The Astro-God has 6,000 against Moon Lord's ~145,000. Bosses were **not** used to
set weapon damage.

**Damage method (ratios).** Each tier's per-hit base is:

> Terraria damage at that tier × `DamageScale` × (Rimtera tier HP ÷ Terraria tier HP, normalised to tier 1)

This keeps Terraria's hits-to-kill progression against each tier's creatures. `DamageScale` (4/3) anchors tier 1: the
copper broadsword's 9 becomes the ferrite longblade's 12. A pure hits-to-kill match at tier 1, with no NPC defense,
would land near 6. Rimtera keeps 12 because `ArsenalSelfTests` pins the ferrite longblade at 12-22 and the tier-1
creatures were tuned around it. That is the lowest value the existing tests allow. See §6.

**Defense method.** Rimtera uses Terraria's rule: half of defense comes off a hit on Classic (`DefenseBalance`).
Rimtera's creatures hit for about 0.5-0.67 of Terraria's, so armour targets the same **share of a hit removed**, not the
same raw number. See §4.

## 2. Tools

### 2.1 Pickaxe power each ore needs

Terraria values come from `Player.GetPickaxeDamage` (`lite/Terraria.Player__GetPickaxeDamage.lite.txt`).

| Terraria tile | Needs | Rimtera tile | Needs | Gap |
|---|---|---|---|---|
| Copper through platinum ores | 0 | Ferrite ore / scrap | 0 | ✅ |
| (next ore after copper: iron, 0) | 0 | Cobalt ore, glacial, uranium, pyrocite, viridium, obsidian | 35 | Rimtera gates tier 2+ ores on the copper pick itself. Equivalent in effect: the starting drill opens them. |
| Meteorite (37) | 50 | Meteorite | 50 | ✅ 1:1 |
| Demonite / crimtane (22/204), obsidian (56) | 55 | none (obsidian 35) | — | No evil-ore gate |
| Ebonstone, crimstone, pearlstone, hellstone (25/203/117/58) | 65 | Pyrocite 35 | — | Pyrocite is not gated above 35 |
| Dungeon brick | 100 | Precursor dungeon brick | 70 | Mapped (Terraria 100 → Rimtera ≈80). Only the pyrocite drill (molten analog) opens it before hardmode. ✅ shape |
| Cobalt / palladium (107/221) | 100 | Viridium 35 | — | Gated by station (fission slag), not by pick power |
| Mythril / orichalcum (108/222) | 110 | Lumenite | **85** | Rimtera compresses the hardmode scale |
| Adamantite / titanium (111/223) | 150 | Voidsteel | **100** | Compressed |
| Chlorophyte (211) | 200 | none | — | |
| Lihzahrd (226/237) | 210 | Precursor altar | 85 | |

**Power mapping** (`GearCurve.DrillPowerFromTerraria`): Rimtera power equals Terraria power up to 55. Above that it is
piecewise linear, chosen so Terraria's gates land on Rimtera's gates: 110 → 85, 150 → 100, 225 → 128.

### 2.2 Pickaxes and drills (useTime in ticks; both games run 60 ticks a second)

| Tier | Terraria (pick power / useTime / tileBoost) | Rimtera before | Rimtera after | Gap closed |
|---|---|---|---|---|
| Start | Copper Pickaxe 35 / 15 / −1 | Ferrite drill (`CopperPickaxe`) 38 / 6 | **35 / 15** | Now the copper pickaxe |
| 1 | Copper 35/15, Tin 35/14, Cactus 35/16 | Ferrite rock drill 42/6, Ferrite pick 40/7 | 35 / 15 (both) | Drills were 15-20% too strong and 2.5× too fast |
| 2 | Iron 40/13, Lead 43/12 | Cobalt drill 55/5, Cobalt pick 53/7 | 41 / 13 | Before, cobalt mined meteorite (50). Iron and lead cannot. |
| 3 | Silver 45/11, Tungsten 50/19, Gold 55/17, Platinum 59/15, Nightmare 65/15, Deathbringer 70/14, **Molten 100/18** | Every tier-3 pick 68/7, glacial drill 70/5 | Glacial and uranium 55/15, meteorite 60/15 (evil picks), **pyrocite 80/18 (molten)** | The band is spread out again. Only pyrocite opens dungeon plating, as only molten opens dungeon brick. |
| 4 | Cobalt 110/13, Palladium 130/12 | Viridium drill 85/4, **Viridium pick 83/7 (could not mine lumenite, gate 85)** | 88 / 13 | Bug fixed: the pick and the drill now both mine lumenite |
| 5 | Mythril 150/10, Orichalcum 165/9 | Lumenite drill 100/4, **Lumenite pick 98/5 (could not mine voidsteel, gate 100)** | 103 / 10 | Bug fixed |
| 6 | Adamantite 180/8, Titanium 190/7, Pickaxe Axe 200/7, Chlorophyte 200/7 (+1) | Voidsteel 120/3, 118/5 | 114 / 8 | |
| 7 | Lunar pickaxes 225/6 (+4), drills 225/4 (+3) | Astral 150/3, 148/5 | 128 / 6 | |
| Unique | Reaver Shark 59, Bone 55, Fossil 55 (above their tier) | +5..+7 over the tier | 40% of the way to the next tier (prospector 37, drone-core 47, claim-jumper 107, void auger 120) | Formula |
| Core breaker | Pwnhammer: hammer 80, useTime 14 | Pick 75 / 7 | 75 / **14** | |
| Survey drill | (none; wood-tier tools are ~20-25 ticks) | 20 / 8 (faster than copper) | 20 / 20 | |

Mining now follows Terraria's formula: a block takes 100 damage, and each hit does pick × 2 on soft ground, × 1 on
stone, or ÷ 2/3/4 on hardmode ores (`CombatTuning.DrillDamage`, owned by the mechanics pass). So use times are 1:1
(`ToolUseTimeScale = 1`). Hardmode drill power is compressed (§2.1), which makes hardmode mining about 25% slower than
Terraria's.

### 2.3 Axes (cutters) and hammers (demolishers)

**Axe mapping:** Rimtera cutter power = Terraria axe **percent** = the `axe` field × 5. The copper axe (7) is 35. The
chop formula reads power ÷ 5 back as the axe value: (int)(axe × 1.2) per chop, toward 100.

| Tier | Terraria axe % / useTime | Rimtera before (cutters, hatchets) | After | Terraria hammer / useTime | Rimtera demolisher before | After |
|---|---|---|---|---|---|---|
| 1 | Copper 35/21, Tin 40/20 | 45 / 7-8 | **35 / 21** | Copper 35/23, Tin 38/21 | 45 / 8 | 35 / 23 |
| 2 | Iron 45/19, Lead 50/19 | 60 / 7-8 | 45 / 19 | Iron 40/20, Lead 43/19 | 60 / 8 | 42 / 20 |
| 3 | Silver 50, Tungsten/Gold 55, Platinum 60 (17-18); war axe 75; meteor hamaxe 100; molten hamaxe 150 | 75 / 8 | 55 / 18 | Gold 55/23, Platinum 59/21, The Breaker 55/19, Meteor hamaxe 60 | 75 / 8 | 55 / 20 |
| 4 | Cobalt 70/13, Palladium 75/12 | 90 / 7-8 | 70 / 13 | Pwnhammer 80/14 | 90 / 8 | 80 / 14 |
| 5 | Mythril 85/10, Orichalcum 90/9 | 105 / 7-8 | 85 / 10 | Hammush 85/14 | 105 / 8 | 85 / 14 |
| 6 | Adamantite 100/8, Titanium 105/7, Chlorophyte 115/7 | 120 / 7-8 | 105 / 8 | Chlorophyte warhammer 90/14, jackhammer 90/4 | 120 / 8 | 90 / 12 |
| 7 | Lunar axes 135/7, hamaxes 150 | 135 / 7-8 | 135 / 7 | Lunar hammers 100/7 | 135 / 8 | 100 / 7 |

Unique cutters and demolishers sit 40% of the way to the next tier: overgrowth reaper 49, chain cutter 61, plasma saw
93, wrecker's maul 65, seismic hammer 82, gravity breaker 94, astral sledge 104.

**Reach (tileBoost):** Terraria varies tool reach: copper −1, chlorophyte +1, spectre +3, lunar +3/+4. Rimtera has no
per-tool reach. Reach comes only from gear tags (`reach:N`) and `CombatTuning.TileReachX/Y`. This gap is open; it
would need a field read by the mining path, which belongs to another owner.

## 3. Weapons

Per-hit base per class (`GearCurve.*Base`). Each style multiplies the base by its Terraria ratio
(`StyleDamageFactor`), uses Terraria's use time (`StyleUseTime`) and knockback × 0.6 (`StyleKnockback`).

### 3.1 Base per tier

| Tier | Terraria broadsword (dmg / useTime) | Melee base before → **after** | Terraria bow / repeater | Ranged base | Terraria staff (dmg / useTime / mana) | Tech base, charge | Terraria minion | Command base |
|---|---|---|---|---|---|---|---|---|
| 1 | Copper 9/20, Tin 10/20 | 16 → **12** | Copper 6/29, Tin 7/28 | 10 → 8 | Amethyst 15/37/7, Topaz 16/36/7 | 12 → 20, charge 4 → 7 | Finch 7, Slime 8 | 8 → 10 |
| 2 | Iron 12, Lead 13 (20) | 25 → 23 | Iron 8/28, Lead 9/27 | 16 → 16 | Sapphire 18/34/8, Emerald 19/32/8 | 19 → 27, 5 → 8 | — | 13 → 15 |
| 3 | Silver/Tungsten 14, Gold 15, Platinum 16, Light's Bane 16, Blood Butcherer 22, Muramasa 24, Volcano 40 | 36 → 40 | Gold 11/26, Platinum 13/25, Demon 14, Tendon 19, Molten Fury 31/22 | 27 → 27 | Ruby 18/28/9, Diamond 23/26/9, Space Gun 20/17/6, Demon Scythe 35/20/14 | 28 → 40, 7 → 9 | Hornet 12, Imp 17 | 20 → 29 |
| 4 | Cobalt 40/19, Palladium 49/22 | 50 → **57** | Cobalt 35/23, Palladium 37/22 | 36 → 46 | Crystal Storm 35/7/5, Golden Shower 30/6/7, Cursed Flames 55/15/9, Sky Fracture 38/4/17 | 38 → 50, 9 → 10 | Spider 26, Sanguine 35 | 30 → 38 |
| 5 | Mythril 50/20, Orichalcum 59/22 | 74 → 78 | Mythril 39/20, Orichalcum 40/19 | 48 → 57 | Frost Staff 46/12/12, Rainbow Rod 50/25/21 | 50 → 62, 10 → 12 | Pirate 40, Optic 24 | 40 → 45 |
| 6 | Adamantite/Titanium 61/20-21, Excalibur 72/20 | 105 → 105 | Adamantite 42/18, Titanium 43/17, Hallowed 53/16 | 68 → 69 | Venom 44/30/25, Spectre 65/24/15, Inferno Fork 70/30/18 | 68 → 82, 12 → 14 | Pygmy 40, Deadly Sphere 55, Raven 55 | 56 → 70 |
| 7 | Star Wrath 170/16, Meowmere 200/14 | 150 → **173** | Phantasm 50/12 (multi-arrow) | 92 → 95 | Nebula Blaze 130/12/12, Last Prism 100/10/12 | 100 → 120, 14 → 15 | Stardust Cell 60, Terraprisma 90 | 85 → 86 |

The biggest gaps closed here:
- **Tier 1 was a third too strong.** The longblade went from 16 to 12, and the starting stiletto from 12 dmg with
  10% crit to 7 dmg with 4%.
- **The hardmode jump was missing.** Terraria's gold-to-cobalt step is ×2.5. Rimtera's was ×1.4 and is now
  ×1.43 (40 → 57), which is what Rimtera's flatter creature HP supports.
- **Tier 7 was too low** compared with tier 6. It went from 150 to 173.
- **Staves cast as fast as swords swing** (16 ticks) at 0.6 of the damage. Terraria's gem staves hit harder and
  slower: 15-19 damage at 32-37 ticks. Rimtera's staves now take 36 → 14 ticks across the tiers, and charge per cast
  equals Terraria's mana per cast.

### 3.2 Styles (after; Terraria ratio to the tier base, use time, knockback)

| Style | Terraria evidence | Damage factor | Use time | Knockback (Terraria × 0.6) | Before |
|---|---|---|---|---|---|
| Broadsword | Swords 17-22 ticks, kb 5.5-6.5 | 1.0 | 20 | 3.6 | 1.0 / 16 / 3.5 |
| Shortsword | Copper 5 vs broadsword 9; 13-10 ticks; no bonus crit | 0.6 | 12 | 2.4 | 0.72 / 9 / 1.5, +6 crit |
| Spear | Spear 8/31, Dark Lance 34/22, hardmode 44-49/23-28 | 0.88 | 28 | 3.3 | 0.95 / 20 / 3 |
| Yo-yo | 9-60 per hit at 25 (channelled) | 0.7 (kept) | 12 re-hit (kept) | 2.1 | 0.7 / 12 / 1.5 |
| Boomerang | Wooden 10/20, Enchanted 17, Light Disc 60/14 | 1.0 | 20 | 4.2 | 0.85 / 20 / 2 |
| Flail | 15-65 at 40-45, spinning multi-hit | 1.1 (kept) | 24 (kept) | 3.6 | 1.1 / 24 / 5 |
| Whip | 1.4-2.6× the tier's minion; 27-35 ticks | 1.6 × command | 30 | 0.9 | 1.15 / 18 / 1.2 |
| Bow | Tier useTime 29 → 12 | 1.0 | 28, 27, 25, 22, 19, 17, 14 | 1.2 | 1.0 / 20 / 2 |
| Gun | Flintlock 13/16, Handgun 26/15, Phoenix 30/14 | 0.85 | 16 | 1.2 | 0.8 / 12 / 1.5 |
| Shotgun | Boomstick 14×3-4 at 40, Shotgun 24×4-5 at 45 | 0.55 per pellet | 42 | 3.6 | 0.42 / 32 / 2.5 |
| Launcher | Rocket Launcher 55/30 | 1.8 | 30 | 2.4 | 1.8 / 42 / 7 |
| Thrown | Knife 12/15, Shuriken 10/15; Grenade 60/45 | 1.4; explosive 3.0 | 15; 45 | 1.2; 4.8 | 0.85 / 14; 1.6 / 30 |
| Staff | See §3.1 | 1.0 | 36, 33, 27, 20, 18, 18, 14 | 3.0 | 1.0 / 16 / 1.5 |
| Tome | Faster than staves (Water Bolt 17, Golden Shower 6-18) | 0.55 × 3 bolts | 0.75 × staff | 3.0 | 0.55 / 20 |
| Beam | Heat Ray 90/10, Last Prism 100/10 | 0.3 (kept) | 4 | 0.3 | same |
| Summon / sentry | 36 ticks, **no mana in 1.4.5** (README caveat 6) | 1.0 / 1.1 | 36 | — | 30 ticks; no charge cost (already matched) |

**Crit:** Terraria's player base crit is 4% and most weapons add 0. Rimtera's base is 4, plus unique flavour. The
shortsword's +6 was removed.

**Velocity (not retuned):** Terraria's bows and guns fire at shootSpeed 6.6-14 px/tick. With ammo, a bullet or arrow
travels about 36-68 tiles/s. Rimtera's are 22-36 tiles/s, roughly 0.55×. Raising them is risky for projectile
collision, which another owner handles. Listed as an open gap.

**Hand-authored content weapons** (`ContentItems.Weapons`: shiv, katana, flamer, datapads, remotes, about 70 rows) are
not hand-edited. The helpers rescale their damage by (new base ÷ old base) for their class and tier, and their use
time by the new style time ÷ the old one (sword 16 → 20, gun 12 → 16, staff 16 → the tier's staff time, whip
18 → 30, remote 30 → 36). Rapid-fire items of 8 ticks or less keep their rate. Each item keeps its place inside its
tier.

## 4. Armour

### 4.1 Set defense (three pieces plus the set bonus)

Terraria evidence comes from `armor.csv` and `armor_sets.csv`. The "% cut" columns show the share of a typical
same-tier hit that defense removes, at half of defense.

| Tier | Terraria sets (pieces + set bonus) | Terraria % cut | Rimtera heavy set before | After (melee / ballistics / tech / command; classless) | Rimtera % cut after |
|---|---|---|---|---|---|
| 1 | Copper 1/2/1 + 2 = 6, Tin 2/2/1 + 2 = 7 | ~27% of 12 | 9-10 (2/3/2 + 2-3) | **5** (1/2/1 + 1) / 4 / 4 / 4; 5 | 31% of 8 |
| 2 | Iron 2/3/2 + 2 = 9, Lead 3/3/2 + 3 = 11 | ~25% of 20 | 15 | **7** (2/2/1 + 2) / 6 / 6 / 6; 7 | 29% of 12 |
| 3 | Silver 13, Tungsten 15, Gold 16, Platinum 5/6/5 + 4 = 20, Shadow 19, Molten 25 | ~30% of 30 | 20 | **11** (2/3/3 + 3) / 9 / 9 / 9 | 39% of 14 |
| 4 | Cobalt melee 14/10/8 = 32, ranged 23, caster 21; Palladium 32/23/21 | ~27% of 60 | 26 | **24** (7/9/6 + 2) / 17 / 16 / 13 | 34% of the 150-400 HP band hit (35) |
| 5 | Mythril 37/27/24, Orichalcum 42/30/27 | ~29% of 65 | 31 | **30** / 22 / 20 / 16 | 32% of 47 (250-400 band) |
| 6 | Adamantite 50/36/32, Titanium 49/34/30, Hallowed 50 (melee) / 31 / 27 | ~31% of 80 | 37 | **38** / 27 / 25 / 21 | 32% of 60 |
| 7 | Solar 24/34/20 = 78, Vortex 62, Nebula 46, Stardust 38 | ~33% of ~120 | 42 | **58** (16/21/16 + 5) / 42 / 38 / 32; paragon 49 | 32% of the Astro-God's 90 |

Gaps closed:
- Pre-hardmode armour was 1.5-2× too generous for Rimtera's soft early hits. Before, a tier-3 set took 70% off a
  tier-3 hit.
- All classes used to get the same pieces. Terraria splits sets by class from hardmode on: ranged 0.72, magic 0.65,
  summoner 0.55 of melee. Rimtera now does the same. Before hardmode, class sets sit at 0.85, like jungle, necro and bee.
- Tier 7 was capped at 42 against Terraria's 78. It is now 58.
- `DefenseSelfTests` keeps tier 4 at a 30-60% cut, while Terraria's own cut is about 27%. That is why the hardmode
  curve is Terraria × 0.75 rather than a straight hit-ratio match.

Piece split: 30% head, 40% body, 30% legs, with the body at least the head. Terraria hardmode melee helmets carry
more than this. Set bonus defense is a quarter of the total before hardmode (Terraria's ore sets add +2..+4) and
8% after.

### 4.2 Set-bonus kinds

| Terraria set-bonus kind (`armor_sets.csv`) | Examples | Rimtera equivalent |
|---|---|---|
| Flat defense | Wood +1, MetalTier1 +2, MetalTier2 +3, Platinum +4 | `SetBonusDefense` on classless and melee sets ✅ |
| Class damage / crit | Pumpkin +10% all, Frost +10% melee/ranged, Spooky +25% summon, Bone +10% ranged crit, Mythril melee +10 crit | `ClassDamageBonus`, `CritBonus` per class set ✅ |
| Ammo saving | Fossil, Cobalt/Mythril ranged 20%, Adamantite 25% | `AmmoSaveChance` 15-36% ✅ |
| Mana cost / max mana | Jungle −16%, Cobalt/Mythril/Adamantite caster −14/−17/−19%, Magic Hat +60 | `ChargeMaxBonus`, `ChargeRegenBonus` (no cost reduction) ◐ |
| Minion and sentry slots | Chlorophyte summon +2, Hallowed summoner +2, Tiki +1, DD2 sentries +1 | `DroneSlots`, `SentrySlots` ✅ |
| Speed | Ninja +20% move, Cobalt melee +15% melee speed, Adamantite melee +20% | `SetSpecial.SpeedWhenLow` (conditional) ◐ |
| Damage reduction | Turtle 15%, Chlorophyte melee 5%, Solar 12% | `SetSpecial.Endurance` ✅ |
| Thorns | Cactus, Turtle | `SetSpecial.Thorns`, melee `Thorns` ✅ |
| On-hit heal | Palladium regen burst, Spectre healing, Crimson regen | `SetSpecial.HealOnHit`, melee `Lifesteal` ✅ |
| On-hit dodge / immunity | Hallowed, Titanium shards | `SetSpecial.Dodge` ✅ |
| Summoned helper or burst | Chlorophyte leaf crystal, Spectre damage, Orichalcum petals, Stardust guardian, Beetles | `SetSpecial.BurstWhenHit`, `ChargeOnHit` ◐ |
| Immunities | Molten fire walk, Snow chill/freeze, Gladiator knockback | Only via tags on a few sets (gills, water-walk) ◐ |
| Stealth / special modes | Shroomite, Vortex, Forbidden storm, Nebula boosters | none ❌ (out of scope) |

## 5. Accessories

### 5.1 Categories (counts of non-vanity accessories)

Terraria's counts come from `accessories.csv`, by effect statements (heuristic). Rimtera's come from the catalog,
counting `EquipmentSlot.Accessory` items by field or tag.

| Category | Terraria (352 non-vanity) | Rimtera (216) | Notes |
|---|---|---|---|
| Move speed / run | 27 | 15 | |
| Jump, flight, glide, dash, hooks | 38 (+ 44 wings with stats outside these tables) | 40 | |
| Defense | 16 | 23 | |
| Damage % | 32 | 40 | |
| Crit | 12 | 21 | |
| Attack speed / auto-swing | 10 | 4 | ◐ few |
| Life regen | 5 | 8 | Units differ (Terraria `lifeRegen` 2 = +1 HP/s; Rimtera per hour). Not retuned. |
| Max mana / mana cost | 13 | 16 (max charge, charge regen) | No cost reduction (mana flower) ◐ |
| Minion / sentry slots | 7 | 10 | |
| Damage reduction / dodge | 10 | 10 | |
| Knockback immunity | 9 | 5 | |
| Immunities (fire, debuffs, lava) | 30 | 15 | Fewer debuffs to be immune to |
| Water / breath | 13 | 10 | |
| Info and utility | 78 | 24 | Rimtera has fewer info displays ◐ |
| Mining / building | 6 | 8 | |
| Luck / fishing | 26 | 4 | ◐ |
| Armor penetration | 2 | 0 | No NPC defense, so not needed |
| Thorns, lifesteal, charge on hit | 13 | 17 | |
| Max life | 0 (only buffs) | 12 | Rimtera-only; its life cap is 200-300, not 500 |

### 5.2 Magnitudes (before → after)

| Effect | Terraria evidence | Rimtera before | After |
|---|---|---|---|
| Class damage, single item | Warrior/Ranger/Sorcerer/Summoner emblem +15% (hardmode); Avenger +12% all; Destroyer +10% all, +8 crit | Emblems Mk I/II/III 6/9/12%; Omni-emblem 10% | Mk I 6% (no pre-hardmode Terraria analog), **Mk II 12%, Mk III 15%**, Omni 12%. Harnesses, circlet, uplinks, processors and the sniper rig follow the same steps. |
| Crit | Putrid Scent +5, Eye of the Golem +10, Recon/Sniper Scope +10 | Lenses 3/5/8 | 3/5/**10**; sniper rig 10 |
| Defense | Obsidian Skull 1, Cobalt Shield 1, Obsidian Shield 2, Ankh 5, Paladin's 6, Hero 10 | Kinetic plates 2/4/6; aegis 7, ward 8 and juggernaut 10 (capped at 6); armour suit 6, shield emitter 4 | ×0.75 like armour: plates **1/2/4**, bulwark core 2, aegis 5, ward 5, juggernaut 6, retribution mesh 3, armour suit 4, shield emitter 3 |
| Move speed | Aglet +5%, Anklet +10%, Hermes run 6 | Servos 8/12/16% | **5/10/15%** |
| Max mana | Band of Starpower +40 (1.4.5), Celestial Cuffs +40 | Capacitor bank +20 | **+40** |
| Endurance | Worm Scarf 17%, Frozen Turtle Shell (below 50%) | Ablative core 5%, shock absorber 8% | Unchanged (tags read by combat code) |
| Minion slots | Pygmy Necklace +1, Necromantic Scroll +1 and 10% | Uplink +1, swarm processor +1 and 6% | Unchanged ✅ |

The core-chips in `PlayerSystemsContent` (defense 2 + tier, capped at 6) are outside this pass.
`DefenseBalance.MaximumAccessoryDefense` (6) is kept.

## 6. Deliberate differences

1. **Damage scale.** Tier 1 is Terraria × 1.33, held by `ArsenalSelfTests` (ferrite longblade 12-22). With Rimtera
   creatures having no defense, a strict tier-1 match would be about 6. **Recommendation for the combat and creature
   owners:** either give creatures a small defense value, or allow the longblade floor to drop to 8.
2. **Hardmode pick power is compressed** so Terraria's 110 and 150 gates fall on the existing 85 and 100 gates.
3. **Yo-yo and flail timings were kept.** Rimtera yo-yos re-hit on a fixed cooldown, and flails hit once per launch.
   Terraria's channelled and spinning models are different mechanics.
4. **No per-tool reach (tileBoost)** and **no velocity retune.** Both depend on code paths owned elsewhere.
5. **Max-life accessories** stay. They are Rimtera's own, and the life cap is lower.

## 7. What changed and where

- `Systems/Content/GearCurve.cs` (new): every per-tier table and formula. Pick, axe and hammer power and use time;
  the Terraria → Rimtera pick mapping; class damage bases; charge; style factors, use times and knockback; armour
  totals, class split and piece split; rescalers for hand-authored weapons.
- `Systems/Content/ArsenalItems.cs`: `BuildWeapon` reads the curve. Tool rows with power or cooldown 0 take the curve.
  Armour pieces and set bonus come from `GearCurve.ArmorPieces`.
- `Systems/Content/ArsenalItems.Data.cs`: tool rows set to 0 (curve). About 25 accessory rows retuned (§5.2).
- `Systems/Content/ContentItems.cs`: drills, cutters and demolishers from the curve. Armour from the curve. Weapon
  helpers rescale damage and use time. Two accessory defense values.
- `Systems/ItemSystem.cs`: the starting `CopperPickaxe` is 35 / 15. The survey drill's use time is 20.
- `Systems/Content/HardmodeContent.cs`: the Core breaker's use time is 14 (the Pwnhammer's).
- `Systems/GearCurveSelfTests.cs` (new, hooked into `CoreSelfTests`): 15 checks covering drill power and rate per
  tier, ore gates, the unique band, cutter and demolisher ladders, the copper-tier starting kit, the armour curve and
  shape, the class order, weapon base climbs and charge, and the sword and spear use times.

**Known test interaction:** `BatchASelfTests` "saplings grow into trees…" uses the ferrite cutter with a fixed 90-tick
window. Under Terraria's chop formula, a copper-tier axe (35%, 21 ticks) needs 13 chops, about 273 ticks. The parity
version of the same test (`ParityBuildTests` B5) already sizes its window from the item's numbers. The BatchA test
needs the same change, or a higher-tier cutter.
