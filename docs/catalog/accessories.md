# Accessories catalogue: Terraria v1.4.5.8 → Rimtera

This catalogue maps Terraria's accessories, grappling hooks, mounts and extra slots to Rimtera's equivalents by **role, tier and function**. Terraria is evidence only. Rimtera items keep their own names.

The row-by-row table is [`accessories.csv`](accessories.csv), with 445 rows. Plan the 0.2.0 → 0.2.3 integration from this page.

## Sources and method

**Terraria side**
- **Items:** every non-vanity row of `terraria-evidence/full/tables/accessories.csv` (352 functional accessories). Added to these are 25 grappling hooks, 34 mounts, 27 minecarts, the Cell Phone and the 5 Shellphone modes, and the Demon Heart.
- **Excluded:** the 159 vanity rows (music boxes, monoliths, capes, voices).
- **Recipes:** taken from `scripts/recipes.py` (2,475 recipes). Every "craft:" entry in `how_obtained` comes from this output.
- **Other sources:** the other sources come from a scratch scan of every IL method in `members-ALL.json` that loads the item's id. The scan covers `ItemDropDatabase::Register*`, `Chest::SetupShop`, `SetupTravelShop`, `OpenFishingCrate`, `OpenBossBag`, `AddBuriedChest` and `GetAnglerReward`.
  - NPC ids in drop rules were not resolved to names. Where a name is given, it is wiki-level knowledge.
  - The sources of 5 new 1.4.5 items (Heavy Sling, Timer Crit, Sharp Barb, Ammo Cycler) were **not located**. They are probably built from arrays.

**Rimtera side**
- **Accessory dump:** a scratch-only `--acc-dump` and `--acc-recipes` in a private build copy. The dump lists all 220 accessories, with every non-default field, every tag and every Tinker Bench recipe.
- **Rimtera sources:** taken from `--craft-audit --items`.
- **Hooks:** from `GrowthItems.HookLadder`.
- **Mounts:** from `TravelContent.Mounts`.
- **Slots:** from `PlayerProgression.EquipmentSlot` and `GrowthItems.AccessorySlot6Unlocked`.

**Match values**
- `exact-role`: same role, tier band and function.
- `partial`: same role, with a missing half, a different tier or a weaker mechanic.
- `merged-into`: several Terraria items collapse into one Rimtera item.
- `missing`: no Rimtera item does the job.

## 1. Coverage

### By category (all 445 rows)

| category | rows | exact | partial | merged | missing | covered |
|---|---|---|---|---|---|---|
| movement (boots, bottles, balloons, climbing, swimming) | 61 | 7 | 16 | 21 | 17 | 72% |
| wings | 51 | 6 | 9 | 15 | 21 | 58% |
| combat (emblems, gloves, quivers, scopes, on-hit) | 44 | 3 | 12 | 7 | 22 | 50% |
| defense (shields, i-frames, on-hurt, regen) | 28 | 4 | 11 | 1 | 12 | 57% |
| info (watches through the PDA, Cell Phone, Shellphone) | 28 | 4 | 4 | 6 | 14 | 50% |
| yo-yo (strings, counterweights, glove, bag) | 27 | 0 | 0 | 0 | 27 | 0% |
| minecarts | 27 | 0 | 1 | 0 | 26 | 3% |
| mounts | 34 | 2 | 4 | 22 | 6 | 82% |
| hooks | 25 | 3 | 1 | 21 | 0 | 100% |
| immunity (debuff and lava) | 23 | 2 | 5 | 3 | 13 | 43% |
| summoner (minion, sentry, whip tags) | 23 | 2 | 4 | 4 | 13 | 43% |
| novelty (golf balls) | 15 | 0 | 1 | 14 | 0 | 100% |
| building | 14 | 1 | 4 | 1 | 8 | 42% |
| fishing | 14 | 4 | 3 | 7 | 0 | 100% |
| utility (coins, voodoo, misc) | 13 | 1 | 2 | 0 | 10 | 23% |
| mana/charge | 11 | 1 | 3 | 0 | 7 | 36% |
| light | 3 | 0 | 2 | 0 | 1 | 66% |
| transformation | 3 | 0 | 1 | 0 | 2 | 33% |
| slot (Demon Heart) | 1 | 1 | 0 | 0 | 0 | 100% |
| **total** | **445** | **41** | **83** | **122** | **199** | **55%** |

### Gameplay-relevant subset

This subset leaves out the colour and cosmetic variants: 21 developer wings, 15 golf balls, 15 extra string colours, 5 extra counterweights, 7 glowing bobbers, 23 cosmetic minecarts and the Shellphone and Ammo-Cycler modes. That leaves **352 rows**:

| exact | partial | merged | missing |
|---|---|---|---|
| 41 (12%) | 82 (23%) | 97 (28%) | 132 (37%) |

Only **12%** are an exact-role match. Most "covered" rows are partial or merged: the Rimtera item exists but is weaker, sits at another tier, or lacks half the effect.

### Rimtera's 220 accessories

- **99** serve as the equivalent of at least one Terraria item.
- **121** have no Terraria analog (see §4), counting the Mk-ladder steps between mapped items.
- **33** of the 220 are core-chips.

## 2. Combination trees

Each node reads `Terraria item → Rimtera equivalent`. ✗ means nothing is there. Rimtera crafts its combos at the **Tinker Bench**, the Tinkerer's Workshop analog (D-5 makes it sold, not crafted).

### 2.1 Terraspark Boots (movement)
```
Terraspark Boots → Inferno treads (T5: +15% speed, water-walk, lava, fire-block; no flight, no ice)  [partial]
├─ Frostspark Boots → ✗
│  ├─ Lightning Boots → Jet boots (best available)  [partial]
│  │  ├─ Spectre Boots → Jet boots (speed+rocket in one item, bought from the Trade Consul)  [exact-role]
│  │  │  ├─ Hermes / Flurry / Sailfish / Sand Boots → Sprint servos Mk II (×1.10 multiplier, not accRunSpeed 6)  [merged]
│  │  │  └─ Rocket Boots → Jet boots  [merged]
│  │  ├─ Anklet of the Wind → Sprint servos Mk II  [exact-role]
│  │  └─ Aglet → Sprint servos Mk I  [exact-role]
│  └─ Ice Skates → ✗ (no ice-traction mechanic)
└─ Lava Waders → Core wader boots (fished in liquid uranium)  [exact-role]
   ├─ Obsidian Water Walking Boots → ✗ as one item (Hydro skimmers + Thermal sleeve exist separately)
   │  ├─ Water Walking Boots → Hydro skimmers  [exact-role]
   │  └─ Obsidian Skull → Thermal sleeve  [exact-role]
   ├─ Lava Charm → Core-plated skin (full lava immunity, T4; Terraria 7 s, pre-HM)  [partial]
   └─ Obsidian Rose → ✗ (no reduced-lava-damage tier)
Side branches: Fairy Boots (+Flower Boots), Hellfire Treads (+Flame Waker) → ✗
```
Rimtera's own boot tree is short and does not mirror this one:
- Pathfinder boots = Sprint servos Mk II + Hydro skimmers.
- Inferno treads = Pathfinder + Core-plated skin + Thermal sleeve.
- Rimtera has no boot that combines running and flight.

### 2.2 Ankh Shield (defense and immunity)
```
Ankh Shield → Paladin's ward (def 5, knockback immunity, 6% endurance)  [partial]
├─ Obsidian Shield → Bulwark core (knockback immunity + 2 def; no fire-block)  [partial]
│  ├─ Cobalt Shield → Grav anchor  [exact-role]
│  └─ Obsidian Skull → Thermal sleeve  [exact-role]
└─ Ankh Charm → Environmental suit (cold, heat, poison, toxic, breath)  [partial]
   ├─ Armor Bracing        ✗ ← Armor Polish ✗ + Vitamins ✗
   ├─ Medicated Bandage    → Filter implant (poison) [partial] ← Bezoar → Filter implant [exact] + Adhesive Bandage ✗
   ├─ The Plan             ✗ ← Fast Clock ✗ + Trifold Map ✗
   ├─ Countercurse Mantra  ✗ ← Megaphone ✗ + Nazar ✗
   └─ Reflective Shades    ✗ ← Blindfold → Night-vision goggles [partial] + Pocket Mirror ✗
```
Root cause: Rimtera's status system has only **Poison** and **Burning** that gear can block (`PlayerProgression.cs:159-160`). Bleeding, Broken Armor, Darkness, Slow, Silenced, Cursed, Weakness, Confusion, Chilled, Frozen and Stoned do not exist, so 9 of the 10 debuff-immunity items have nothing to block. IC-3 (creature debuffs) plans these.

Rimtera's own defense tree is Juggernaut core = Paladin's ward (Bulwark aegis + Ablative core) + Retribution mesh (Spike lattice + Barb mesh + Ablative core).

### 2.3 Cell Phone and Shellphone (info)
```
Shellphone (+ spawn / ocean / underworld modes) → ✗
└─ Cell Phone → ✗ (no info+recall item; Recall capsule is a consumable)
   ├─ Magic / Ice Mirror → (Recall capsule, consumable)  [partial, outside this catalogue]
   └─ PDA → Astral compass (4 readouts, T7 Astro-God drop, not crafted)  [partial]
      ├─ GPS → Astral compass  [partial]
      │  ├─ Gold / Platinum Watch → ✗ (no watch item)
      │  ├─ Depth Meter → Depth meter  [exact-role]
      │  └─ Compass → Compass chip  [exact-role]
      ├─ Fish Finder → Catch scanner  [partial]
      │  ├─ Fisherman's Pocket Guide → Catch scanner  [exact-role]
      │  ├─ Weather Radio → ✗
      │  └─ Sextant → ✗
      ├─ Goblin Tech → ✗
      │  ├─ Metal Detector → Ore scanner  [exact-role]
      │  ├─ Stopwatch → ✗
      │  └─ DPS Meter → ✗
      └─ R.E.K. 3000 → Life monitor  [partial]
         ├─ Radar → Life monitor  [merged]
         ├─ Lifeform Analyzer → Life monitor  [merged]
         └─ Tally Counter → ✗
```
Coverage is 5 of Terraria's 12 info lines. Rimtera has 4 readout tags (`position`, `depth`, `ore`, `life`). Rimtera has no info combination recipe at all; the Astral compass is a boss drop.

### 2.4 Celestial Shell (stat stones and transformations)
```
Celestial Shell → Astral halo (T7, +8% all classes, +4 def; no transformations)  [partial]
├─ Celestial Stone → Astral halo  [merged]
│  ├─ Sun Stone (Golem) → ✗ (Vault Colossus drops no accessory)
│  └─ Moon Stone (Eclipse) → ✗
└─ Moon Shell → ✗
   ├─ Moon Charm (werewolf) → ✗
   └─ Neptune's Shell (merfolk) → Gill implant  [partial]
```

### 2.5 Horseshoe balloons (jumping)
```
Horseshoe Bundle (quadruple jump) → Mobility rig (×1.16 speed, one double jump, featherfall)  [partial]
├─ Bundle of Balloons → ✗ (no multi-jump stacking)
│  ├─ Cloud / Blizzard / Sandstorm in a Balloon → Double-jump canister  [partial: no jump height]
│  │  ├─ Cloud / Blizzard / Sandstorm / Tsunami / Fart bottle → Double-jump canister  [merged]
│  │  └─ Shiny Red Balloon / Balloon Pufferfish → ✗ (no jump-height accessory)
└─ Lucky Horseshoe → Grav damper (featherfall)  [exact-role]
Horseshoe-balloon pairs (Blue, White, Yellow, Fart, Honey, Sharkron) → Mobility rig  [merged]
```

### 2.6 Master Ninja Gear, Frog Gear, diving gear
```
Master Ninja Gear → Ghost cloak (dodge 10% only)  [partial]
├─ Tiger Climbing Gear → Climbing claws  [merged] ← Climbing Claws [exact] + Shoe Spikes [merged]
├─ Tabi (dash) → ✗  (every Rimtera player can already dash: PawnMovementSettings DoubleTapDashWindow 0.25)
└─ Black Belt → Blink module  [exact-role]
Frog Gear → ✗ ← Frog Flipper ✗ / Frog Webbing ✗ ← Frog Leg ✗ + Flipper ✗ + Tiger Climbing Gear
Amphibian Boots → Sprint servos Mk II  [partial]
Arctic Diving Gear → Diver's kit  [merged] ← Jellyfish Diving Gear [merged] ← Diving Gear [partial] ← Flipper ✗ + Diving Helmet
```

### 2.7 Melee gloves and emblems
```
Fire Gauntlet → Servo grip  [partial: no on-hit fire]
├─ Magma Stone → ✗
└─ Mechanical Glove → Servo grip  [partial]
   ├─ Power Glove → Power glove (auto-swing + 8% speed)  [exact-role]
   │  ├─ Titan Glove → ✗ (no knockback or size accessory)
   │  └─ Feral Claws → Power glove  [merged]
   └─ Avenger Emblem → Eclipse sigil (+5% all classes, event boss drop)  [partial]
      └─ Warrior / Ranger / Sorcerer / Summoner Emblem → class emblem Mk II (12%, crafted)  [merged]
Berserker's Glove → Berserker rig  [partial]  (Power Glove + Flesh Knuckles ✗)
Destroyer Emblem → Fortune matrix  [partial]  (Avenger Emblem + Eye of the Golem → Targeting lens Mk III [exact])
Sniper Scope → Sniper scope rig  [exact-role]  (Rifle Scope [partial] + Destroyer Emblem)
Recon Scope → Sniper scope rig  [merged]  (Sniper Scope + Putrid Scent ✗)
```
Rimtera's emblem tree is a straight upgrade ladder: Mk I (6%) → Mk II (12%) → Mk III (15%) per class, then Omni-emblem (all four Mk III + astral bars). Terraria's emblems are a 15% Wall of Flesh drop and are never upgraded alone. Rimtera's Fission Tyrant has an empty accessory pool (G8).

### 2.8 Quivers, mana flowers, cuffs, cloaks (no Rimtera tree)
```
Molten / Stalker's / Phoenix Quiver ✗ ← Magic Quiver ✗ (+ Magma Stone ✗ / Putrid Scent ✗ / Harpy Charm ✗)
Mana Cloak / Magnet Flower / Arcane Flower ✗ ← Mana Flower ✗ ← Nature's Gift ✗ (+ Star Cloak ✗ / Celestial Magnet ✗ / Putrid Scent ✗)
Celestial Cuffs ✗ ← Magic Cuffs (Capacitor bank, partial) + Celestial Magnet ✗
Celestial Emblem → Arcanist's circlet [partial]
Mana Regeneration Band → Arc battery [partial, T5]  ← Band of Starpower → Capacitor bank [exact] + Band of Regeneration → Nano-mender [exact]
Star Veil / Bee Cloak / Sweetheart Necklace / Stinger Necklace / Honey Balloon ✗ ← Star Cloak ✗, Cross Necklace ✗, Honey Comb ✗, Panic Necklace ✗
Charm of Myths → Nano-mender [partial] ← Band of Regeneration [exact] + Philosopher's Stone ✗
```

### 2.9 Summoner trees
```
Papyrus Scarab → Hivemind processor  [partial]
├─ Necromantic Scroll → Warden's uplink  [exact-role]
└─ Hercules Beetle → Command emblem Mk II  [partial]
Pygmy Necklace → Uplink antenna [exact]; Apprentice / Squire / Huntress / Monk sentry pieces → Sentry beacon [merged]
1.4.5 whip-tag tree, all ✗:
  Ouroboros Ring ← Snake Band + Möbius Strip;  Twilight Grasp ← Silver Bracer + Wicked Armlet;
  Wicked Claws, Armlet of Ruin, Druidic Serpent Cloak, Silver Shield;  Scout / Templar Sling, Royal Guard Harness ← Heavy Sling
```

### 2.10 Building, fishing, coins, yo-yo
```
Hand of Creation → Architect's frame [partial]
├─ Architect Gizmo Pack → Architect's frame [partial]
│  └─ Brick Layer ✗, Extendo Grip → Telescopic arm [exact], Paint Sprayer ✗, Portable Cement Mixer ✗
├─ Ancient Chisel → Miner's gauntlet [partial: 15% at T1; Excavator rig 25% at T4]
├─ Treasure Magnet → Magnet ring [exact]
└─ Portable Stool ✗
Lavaproof Tackle Bag → Master tackle kit [partial]
├─ Angler Tackle Bag → Master tackle kit [partial] ← High Test Line → Braided fishing line [exact], Tackle Box → Tackle kit [partial], Angler Earring → Angler's visor [exact]
└─ Lavaproof Fishing Hook → Uranium lure [exact]
Greedy Ring ✗ ← Coin Ring ✗ (Gold Ring → Magnet ring [partial] + Lucky Coin ✗) + Discount Card ✗
Magic Yoyo Bag ✗ ← Yoyo Bag ✗ ← Yo-Yo Glove ✗ + Strung Counterweight ✗ ← String ✗ + Counterweight ✗
```

## 3. Hooks, mounts and slots

**Hooks.** All 25 Terraria hooks map onto Rimtera's 4-step `HookLadder`:

| Rimtera hook | reach | notes |
|---|---|---|
| Scrap tether | 10 tiles | |
| Mag-grapple | 15 tiles | |
| Twin lash | 20 tiles | 2 hooks |
| Phase tether | 28 tiles | instant retract |

Gaps:
- **Rimtera hooks are worn in an accessory slot.** Terraria has a dedicated hook slot, so in Rimtera a hook costs a real accessory.
- Missing hook kinds: multi-hook counts above 2 (Web Slinger 8, Lunar Hook 4), anti-gravity free movement, and the Hook of Dissonance teleport.

**Mounts.** Rimtera has 6 mounts: Hover-bike, Dune strider, Rotor skiff, Tide ray, Burrower and Spring hopper. They cover:
- the ground-speed role (Unicorn, horses, wolf, raptor and rat: 13 items);
- jump (Slime, Bunny, Pogo);
- desert (Basilisk);
- swim (Turtle, Cute Fishron);
- limited flight (Bee, UFO, Witch's Broom, Bat, Pillion, Rudolph);
- dig (Drill).

Missing:
- the shooting mount (Scutlix);
- infinite flight (UFO, Cute Fishron);
- the 4 Master-mode boss mounts;
- the camera drone.

Mounts are summoned from a **carried** key (`FirstCarriedMount`, R key), not from a mount slot.

**Minecarts.** These are missing by design: the ore cart was retired (`MountKind.RetiredCart`) and glide-walk conveyors replaced it. The 23 cosmetic carts follow from that choice. The open question is whether rails ever come back (see CX-16).

**Slots**

| | Terraria | Rimtera today | Planned |
|---|---|---|---|
| Accessory slots | 5 + Demon Heart (Expert) + 1 in Master = 7 | 5 + slot 6 (Auxiliary socket on Brutal+, or **any** Annihilation world) = 6 max | D-8 / PC-11 (0.1.14): socket opens slot 6 on Brutal+ only, Annihilation adds slot 7 |
| Misc equipment | hook, mount, minecart, pet, light pet | none: hooks use accessory slots, mounts come from a carried key, pets are used items | PU-19 (0.1.14) equipment page with misc slots |
| Vanity and dye per accessory | yes | vanity only for head, body and legs | PU-19 |

## 4. Rimtera-only accessories (no Terraria analog)

These 121 accessories exist only in Rimtera. Most come from the sci-fi or RimWorld layer, or from Rimtera's Mk I/II/III ladders:

- **Max-life accessories** (Terraria has none): Vital booster (+20), Heart pump (+40), Vital core (+50), Nova heart (+60), Energy shield emitter, Bio-weave, and the life core-chips.
- **Lifesteal:** Leech coil, Vampiric chip, Sanguine reactor, Moon-eater fang.
- **Charge on hit:** Siphon coil, Capacitor tap, Storm capacitor, Overload matrix, Protocol shard.
- **Thorns.** Terraria has thorns only on armor or a potion. Rimtera has Barb mesh, Spike lattice, Thorn plating, Retribution mesh and Juggernaut core.
- **Flat damage reduction ladder:** Ablative core (5%) and Shock absorber (8%). Terraria has only the Worm Scarf (Expert).
- **Temperature and atmosphere** (the RimWorld layer): Vitrified heat ward, Cyber-swarm gloves (cold), Frost-grip gloves, Thermal regulator, Rebreather mask, Oxygen tank, Rad battery, Acid wader.
- **Light, mining and sensing ladders.** These replace Terraria's Mining Helmet, Spelunker and Hunter potions: Headlamp → Flood visor → Lumen halo; Miner's gauntlet → Excavator rig; Geo-scanner, Threat radar, Survey suite, Excavation suite, Miner's helmet rig, Hunter's eye, Void eye.
- **Mk ladders:**
  - class emblems Mk I and Mk III;
  - Targeting lens Mk I and II;
  - Kinetic plate Mk II and III;
  - Sprint servos Mk III;
  - the 4 class modules;
  - the 4 hybrid harnesses (Duelist, Gunner's, Arcanist's, Warden's);
  - Omni-emblem.
- **Ore wingpacks** (Ferrite → Astral, 10 tiers) and pre-Fracture glide rigs from drops: Scrap, Spore and Salvaged thruster fins.
- **Event-boss trinkets.** Terraria event bosses drop no accessories; Rimtera's do: Warlord's medal, Pirate's spyglass, Overseer's monocle, Prism brooch, Empress tiara, Leviathan scale, Mothership chip, Hivecomb amulet, Cryo heart, Reactor heart.
- **Core-chips for 22 bosses with no Terraria Expert accessory:** the hard trio, Spore Monarch, Abomination, Fallout Hive-mind, Void Cantor and all event bosses. All 33 chips are flat-stat pieces today.

## 5. Effect categories missing entirely

Nothing in Rimtera reads a tag or stat for the following. Each line names the Terraria items that use the effect.

1. **Jump height and jump speed:** Shiny Red Balloon, Frog Leg, the balloon half of every combo.
2. **Multi-jump stacking:** Bundle of Balloons, Horseshoe Bundle. Rimtera has one `double-jump` flag.
3. **Max-run-speed boots (`accRunSpeed`).** Rimtera only multiplies speed. There is also no ice traction (Ice Skates) and no sand speed (Sand Boots).
4. **Swimming:** Flipper and the Frog / Diving swim half. Only the Tide ray mount swims.
5. **Debuff immunity beyond poison and fire.** 11 Terraria debuffs do not exist (IC-3).
6. **On-hurt reactions:**
   - longer invincibility (Cross Necklace);
   - star, bee and gel bursts;
   - a speed burst (Panic Necklace);
   - charge on hurt (Magic Cuffs);
   - a low-HP shell (Frozen Turtle Shell).
7. **On-hit inflicts and penetration:** fire (Magma Stone), poison arrows (Poison Barb), armor penetration (Shark Tooth Necklace), crossbones (Bone Glove).
8. **Melee knockback and weapon size:** Titan Glove.
9. **Ammo-class multipliers:** arrow damage and speed (quivers), ammo rotation (Ammo Cycler).
10. **Aggro manipulation:** Putrid Scent, Flesh Knuckles. There is no aggro stat.
11. **Charge (mana) economy:** cost reduction (Nature's Gift), auto charge-cell use (Mana Flower), charge-pickup range (Celestial Magnet).
12. **Yo-yo modifiers:** string reach, counterweights, two yo-yos, offstring tricks.
13. **Whip-tag slots and sentry carrying** (1.4.5 summoner tree), and minion knockback.
14. **Coins and shops:** coin pickup range, coins on hit, shop discount.
15. **Info readouts:** time, weather, moon phase, kill tally, DPS, speed. There are also no info combos.
16. **Building speed and automation:** tile and wall placement speed, auto-paint, auto-actuator, placement ruler, wire view, stool.
17. **Transformations:** werewolf and merfolk.
18. **Gravity flip, infinite flight, downward slam, shimmer phasing.**
19. **Potion-sickness reduction:** Philosopher's Stone, Charm of Myths.

## 6. Deliberate Rimtera choices (keep unless the user overrides)

- **Pre-Fracture wings are glide rigs (G3).** They never climb (`glide-rig` tag, `ArsenalItems.WingClimbs`). Only flight-essence wings and tier 5+ drops climb. This matches Terraria's "flight is hardmode" while allowing early gliding (Terraria's Fledgling Wings → Glider wings).
- **Everyone can dash.** Base movement has a double-tap dash (`GameSimulation.cs:55-67`).
  - Terraria gates the dash behind Tabi, the EoC Shield, Master Ninja Gear and the Solar set.
  - Under decision 1 (§1.2: match Terraria), this should move to accessories. IC-16 already lists "a dash (double-tap)" as a missing family. CX-5 asks the user to confirm.
- **Class emblems are crafted Mk ladders,** not a Wall of Flesh drop. G8 notes that the Fission Tyrant pool is empty.
- **Boss summons need no sacrifice.** The Core igniter and Scout beacon replace the Guide and Clothier Voodoo Dolls.
- **Minecarts are retired** in favour of glide-walks (see CX-16).
- **No developer-item pool.** The 21 developer wings stay out of scope.
- **The RimWorld temperature and toxicity layer** adds immunity tags Terraria lacks (`cold`, `heat`, `toxic`, `breath`, `acid`).
- **Roadmap defaults that already touch this area:**
  - D-5: the Tinker Bench is sold by the Mechanic.
  - D-8: accessory slots 6 and 7.
  - D-17: core-chips get unique effects.
  - IC-15: auto-swing works (0.1.9).
  - IC-16: missing accessory families (0.1.14).
  - PU-6: mount physics (0.1.8).
  - PU-19: misc slots (0.1.14).
  - X-TG-2: mount volume deferred past 0.2.0.

## 7. Integration items

Sizes: S ≈ ≤1 agent-day, M ≈ 2-4, L ≈ 5+.

**Shared files** (named per item below only when they differ):
- content rows: `Systems/Content/ArsenalItems.Data.cs` (`AccessoryRows`), `ArsenalItems.cs` (`BuildAccessory`);
- recipes: `ArsenalRecipes.cs`;
- tag readers: `Systems/GameSimulation.Gear.cs`;
- known tags: `Systems/ItemAudit.cs:26-32`;
- the `ItemId` enum: `Systems/ItemSystem.cs`, append-only.

**Every item must pass the same three checks:**
- `--item-audit` reports 0 unread tags.
- `--craft-audit` reports 0/0/0, with no tier-4+ item before the Fracture.
- The catalogue row's `match` moves up.

**CX-1 · Run-speed boot chain (the Terraspark tree)**
- **Change:**
  - Add a `runspeed:X` tag that raises the maximum run speed and acceleration, as Terraria's `accRunSpeed` does. Today only the speed multiplier exists.
  - Add an `ice-traction` tag.
  - Build the chain: sprint boots → sprint+rocket → +anklet/aglet → +ice → +Core wader boots = the Terraspark analog. Jet boots become the Spectre step.
  - Re-point Inferno treads, or add a new top tier.
- **Files:** `GameSimulation.Player.cs` (movement prep, around line 266), `PawnMovementSystem` settings, the content rows and recipes.
- **Size:** M.
- **Acceptance:**
  - A ParityMovementTests case shows the boots reach 6/3 × base top speed after the Terraria acceleration time.
  - A craft test builds the top boot from 7 base pieces at the Tinker Bench.
  - Ice does not slow a wearer of the ice step.

**CX-2 · Jump family and balloon combos**
- **Change:**
  - Add `jump-height:X` and `jump-speed:X` tags.
  - Make each `double-jump:<kind>` item an extra jump with its own height and duration, so several can stack.
  - Add the balloon, horseshoe-balloon and bundle (4 jumps) combos, and frog-leg auto-jump.
- **Files:** `GameSimulation.Player.cs` (jump handling, around line 364), Gear.cs, the content rows and recipes.
- **Size:** M.
- **Acceptance:**
  - Self-test: balloon worn → held-jump apex is about 1.33× base.
  - 3 bottle items worn → 3 mid-air jumps.
  - Bundle + horseshoe → 4 jumps and no fall damage.

**CX-3 · Debuff immunity ladder and the Ankh chain**
- **Depends on:** IC-3 (creature debuffs).
- **Change:**
  - Add 10 single-immunity accessories, 5 pair combos, the Ankh-charm analog, and the Ankh-shield analog (kb + fire-block + all debuffs).
  - Drop them through a status-immunity table on matching creatures, as Terraria's `RegisterStatusImmunityItems` does (1/100).
- **Files:** `Systems/PlayerProgression.cs` (`StatusId` and `BlockedByImmunityTag`), `NpcDropRegistry.cs`, the content rows and recipes.
- **Size:** L.
- **Acceptance:**
  - For each debuff, a creature applies it, and the matching item blocks it in a self-test.
  - The Ankh analog blocks all of them.
  - The craft audit reaches the Ankh shield after the Fracture.

**CX-4 · Info accessories and the PDA / Cell Phone chain**
- **Change:**
  - Add readouts for time (3 watch tiers), weather, moon phase, fishing, kill tally, DPS and speed.
  - Add the combos (GPS, Goblin Tech, R.E.K., Fish Finder), the PDA, and a reusable recall + all-info item.
  - Optionally add a Shellphone-style mode switch: home, landing site, ocean, molten chasm.
  - Lower the Astral compass to a real crafted path, or keep it as the endgame bonus.
- **Files:** `Systems/GameSimulation.InfoGear.cs`, the HUD info panel in `UI/`, the content rows and recipes, the shops in `TownNpcs.cs` (Travelling Merchant, the Angler analog).
- **Size:** M.
- **Acceptance:**
  - `InfoReadouts` returns 12 lines with the PDA analog carried, from the inventory as well as worn.
  - The Cell Phone analog recalls home with no item consumed.

**CX-5 · Dash accessories (decision needed)**
- **Change:** the user decides whether to keep the free base dash. If gated, remove the base dash and add:
  - the Tabi analog (dash);
  - the EoC-shield analog (dash that hits; also part of CX-12);
  - the Master Ninja Gear combo (climb + dash + dodge).
- **Files:** `Systems/GameSimulation.cs:55-87` (`DashSpeed`), `GameSimulation.Player.cs`, the content rows and recipes.
- **Size:** S.
- **Acceptance:**
  - With no gear, a double-tap gives no dash.
  - With the dash item, the parity movement test's dash passes.
  - The ninja combo gives all three effects.

**CX-6 · On-hurt and invincibility family**
- **Change:** add `immune:+0.67` (longer i-frames), on-hurt star, bee and gel bursts, `panic` (speed on hurt), `charge-on-hurt`, and `lowhp-dr` (25% under 50% HP). Then add the combos: Star Veil, Bee Cloak, Sweetheart, Mana Cloak, Magic Cuffs, Frozen and Hero shield. These are part of IC-16.
- **Files:** `GameSimulation.cs` (`GearHitPlayer` / hurt path), Gear.cs, the content rows and recipes.
- **Size:** M.
- **Acceptance:**
  - Self-test: the i-frames after a hit last 1.67× as long.
  - A hit spawns N star projectiles.
  - Under 50% HP, damage taken is ×0.75.

**CX-7 · Ranged and melee modifiers (quivers, scopes, gloves, on-hit)**
- **Change:**
  - Add `ammo-dmg:<class>:X` and `ammo-speed` (quivers), a gun scope zoom input, `armorpen:X`, and `kb-melee` + `melee-size` (Titan Glove).
  - Add on-hit inflict tags: fire, poison arrows.
  - Rebuild the Fire Gauntlet and quiver chains as combos.
- **Files:** `GameSimulation.WeaponStyles.cs` (`FireStyledRanged`), `GameSimulation.Combat.cs` (`KnockEnemy`, strike), Gear.cs, the content rows and recipes.
- **Size:** M.
- **Acceptance:**
  - An arrow fired with the quiver analog deals ×1.10 damage and flies faster.
  - Armor penetration 5 removes 5 creature defense.
  - The glove raises knockback and swing hitbox size.

**CX-8 · Charge (mana) economy accessories**
- **Change:** add `charge-cost:-X`, an auto charge-cell use when short (Mana Flower), and charge-pickup range. Then add the flower, cloak, cuffs and Celestial chains, and a pre-Fracture "star + regen" band. Arc battery is T5 today.
- **Files:** `GameSimulation.PlayerSystems.cs` (charge spend), Gear.cs, the content rows and recipes.
- **Size:** S-M.
- **Acceptance:**
  - A Tech weapon costs 8% less charge with the flower analog.
  - At 0 charge with a cell carried, the cell is consumed automatically.

**CX-9 · Summoner accessories (whip tags, sentries, minion knockback)**
- **Change:**
  - Add a whip-tag count stat. Signal whips stack up to N distinct tags (Silver Bracer ladder).
  - Add the second-whip strike (Snake Band), sentry carrying (Heavy Sling), and `minion-kb`.
  - Build the Ouroboros / Twilight / sling combos and the Papyrus Scarab combo.
- **Files:** the minion and whip code in `GameSimulation.WeaponStyles.cs` and `GameSimulation.Combat.cs`, the content rows and recipes.
- **Size:** M.
- **Acceptance:** two different whips' tags both apply with the bracer analog and only one without it.

**CX-10 · Yo-yo accessories**
- **Change:** add string reach (+3 tiles), counterweights (thrown on hit), the two-yo-yo glove, the bag combos, and offstring tricks (Magic String). Rimtera has 20 yo-yo weapons and none of these.
- **Files:** `GameSimulation.WeaponStyles.cs` (the yo-yo style), the content rows and recipes, the Skeleton-merchant analog shop.
- **Size:** M.
- **Acceptance:** with the bag analog, a yo-yo reaches +3 tiles, fires counterweights on hit and spawns a second yo-yo.

**CX-11 · Hook and mount slots, slot 7**
- **Change:**
  - Add a hook slot and a mount slot. Moving hooks out of the accessory slots frees one slot for every player who uses a hook.
  - Make the mount key equippable as well as carried.
  - Bring `AccessorySlot6Unlocked` into line with D-8, and add `Accessory7`.
  - This shares the 0.1.14 PU-19 / PC-11 save bump.
- **Files:** `Systems/PlayerProgression.cs` (`EquipmentSlot`, append-only), `Content/GrowthItems.cs` (`ActiveHook` reads the hook slot), `GameSimulation.Travel.cs` (`FirstCarriedMount`), `CharacterFile.cs`, `UI/InventoryUi.cs`.
- **Size:** M.
- **Acceptance:**
  - A hook in the hook slot works and is refused in an accessory slot.
  - Slot counts: Classic 5, Brutal + socket 6, Annihilation + socket 7.
  - An old save loads with no item loss.

**CX-12 · Core-chips with Expert-item effects (D-17)**
- **Change:** give each chip whose boss maps to a Terraria Expert boss that boss's Expert effect:

  | chip | Terraria Expert item | effect |
  |---|---|---|
  | Alpha Skitter | Royal Gel | slime-likes are friendly |
  | Overseer | EoC Shield | dash that hits |
  | Damaged Mining Drone | Worm Scarf | 17% damage reduction |
  | Plant Overgrowth | Brain of Confusion | dodge + confuse |
  | Scout Mech | Bone Glove | crossbones |
  | Hoarfrost Behemoth | Bone Helm | shadow hands |
  | Bloom Matriarch | Hive Pack | bee strength |
  | Gel Sovereign | Volatile Gelatin | gel bursts |
  | Mutated Flora | Spore Sac | spore minions |
  | Vault Colossus | Shiny Stone | regen when still |
  | Prism Empress | Flight Booster | infinite wing time |
  | Astro-God | Gravity Globe | gravity flip |

  The other 21 chips get original effects.
- **Files:** content (chip rows), Gear.cs (chip effect methods, owned by stream B per 0.1.14), `GameSimulation.Player.cs` (gravity, flight).
- **Size:** L.
- **Acceptance:** one self-test per chip effect. No two chips have the same effect.

**CX-13 · Building accessories**
- **Change:** add tile and wall placement speed (`place-speed`, `wall-speed`), auto-paint (needs D-34 paint), auto-actuator (needs 0.1.14 wiring), a placement ruler, wire view and a stool (+1 reach upward). Then add the Gizmo-pack and Hand-of-Creation combos. The current Architect's frame becomes a step.
- **Files:** placement code (`GameSimulation.Items.cs`), Gear.cs, `UI/` overlays, the content rows and recipes, the Travelling Merchant stock.
- **Size:** M.
- **Acceptance:**
  - Holding place with the speed analog places 1 tile per 1/3 of the normal delay.
  - Auto-paint coats a placed tile with the selected paint.

**CX-14 · Swimming and water family**
- **Change:** add a `swim` tag (Flipper: free swimming in water), the Frog Leg analog, the frog-gear combos, a merfolk form in water (Neptune's Shell), and the Diving Gear → Arctic chain with swim + light + ice.
- **Files:** `GameSimulation.Player.cs` (liquid movement), Gear.cs, the content rows and recipes, fishing crates.
- **Size:** M.
- **Acceptance:** with the flipper analog, holding jump in water rises at the Terraria swim speed, and without it the player sinks.

**CX-15 · Small gaps: coins, lava timing, fishing, potion sickness**
- **Change:**
  - Coins: coin pickup range, coin on hit, shop discount, and the Greedy Ring chain (pirate-event analog drop).
  - Lava: a timed lava immunity (7 s) and an obsidian-rose damage reduction before the Fracture, then the molten-skull-rose chain into the Core wader boots.
  - Fishing: a bait-saving Tackle Box and the lava tackle-bag combo.
  - Potion sickness: a reducer and the Charm-of-Myths combo.
- **Files:** Gear.cs, the shops in `TownNpcs.cs`, the fishing code (`GameSimulation.Fishing.cs`), the content rows and recipes.
- **Size:** M (several S parts).
- **Acceptance:** one self-test per tag. The discount lowers a shop price by 20%.

**CX-16 · Decision record: minecarts, transformations, developer items**
- **Change:** record the user's choice on rails and minecarts (Rimtera retired its cart), werewolf and merfolk forms, and the developer-wing pool. Today all three are absent. If rails return, size them separately (L, world and wiring).
- **Files:** `docs/ROADMAP_0.2.0.md` §6.
- **Size:** S.
- **Acceptance:** a D-row exists for each, and this catalogue's `notes` cite it.

### Suggested order across 0.2.0 → 0.2.3
- **0.2.0 (gear foundations):** CX-11 (slots), CX-1, CX-2, CX-5.
- **0.2.1 (combat accessories):** CX-6, CX-7, CX-8, CX-12.
- **0.2.2 (class and statuses):** CX-3 (after IC-3), CX-9, CX-10.
- **0.2.3 (utility and polish):** CX-4, CX-13, CX-14, CX-15, CX-16.
