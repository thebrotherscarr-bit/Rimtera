# Terraria.NPC — Combat, Defaults, and AI Controllers

## Evidence identity and scope

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`; `Terraria` 1.4.5.8; SHA-256 `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- Evidence: static managed metadata and selected ILSpy 11.1.0.9782 decompilation; target was not run. The default matrix is generated from literal `type == n` branches in `NPC.SetDefaults`; conditional overrides remain explicit. It is not the post-difficulty/player-count/runtime-scaled state.

## 1. `StrikeNPC` damage and knockback equations

`NPC.StrikeNPC` resolves an omitted owner to `Main.myPlayer`, rejects inactive/dead NPCs, performs multiplayer/pending-strike checks, then calls `StrikeNPC_Inner`. `Main.CalculateDamageNPCsTake(Damage, defense)` is:

```text
d = max(1.0, double(Damage) - 0.5 * double(defense))
```

Inner damage order:

```text
num = int(max(1.0, d * (crit ? 2.0 : 1.0)))
if RedHatSkeletronAdjustmentsEnabled(): num = max(1, int(num * 0.7))
if takenDamageMultiplier > 1: num = int(num * takenDamageMultiplier)
num = max(1, num)
```

The casts to `int` truncate. If `immortal`, health is not reduced. If `realLife >= 0`, strike damage is credited/applied to `Main.npc[realLife]` and segment `life/lifeMax` mirrors the root; otherwise the local NPC receives it. Thus the generic defense term is `defense/2`, with further explicit multipliers and a hard one-damage floor.

Knockback starts only if `knockBack > 0 && knockBackResist > 0`:

```text
k = knockBack * knockBackResist
if onFire2: k *= 1.1
k = softCap(k):
    if k > 8:  k = 8  + 0.9 * (k - 8)
    if k > 10: k = 10 + 0.8 * (k - 10)
    if k > 12: k = 12 + 0.7 * (k - 12)
    if k > 14: k = 14 + 0.6 * (k - 14)
    if k > 16: k = 16
if crit: k *= 1.4
threshold = num * (Main.expertMode ? 15 : 10)
```

When `threshold > lifeMax`, horizontal velocity is pushed in the `hitDirection` with a magnitude cap at `k`; vertical response is `-0.5*k` for `noGravity`, otherwise `-0.75*k`, clamped against current velocity. NPC type `185` multiplies `k` by `1.5` in this branch. Otherwise, vertical velocity is set to `-0.5*k*knockBackResist` without gravity or `-0.75*k*knockBackResist` with gravity, and horizontal velocity becomes `k*hitDirection*knockBackResist`.

## 2. `NPC.SetDefaults` base property matrix

Global defaults observed before per-type branches include `damage=0`, `value=0f`, `knockBackResist=1f`, `npcSlots=1f`, `townNPC=false`, `friendly=false`, `boss=false`, `active=(Type != 0)`, `ai[0..maxAI)=0`, and `localAI[0..maxAI)=0`. Cells below show per-type branch assignments for `aiStyle`, `lifeMax`, `defense`, `damage`, `value` (NPC value units), `knockBackResist`, and selected role flags. `condition => expression` marks a nested branch/override preserved from the source; `—` means the type branch did not assign that property. IDs/names are joined against the `Terraria.ID.NPCID` literal map in Phase 14.

Coverage: 643 unique numeric type IDs from 1 through 696 were found in literal equality branches (648 rows before duplicate-ID merge). Grouped conditions map the same branch to multiple IDs. `SetDefaultsFromNetId`, transforms, seed adjustments, multiplayer scaling, and `ScaleStats_ByDifficulty` / `ScaleStats_ByPlayerCount` are separate pathways. Drop rules and happiness are separate systems and are not represented as fields in this matrix.

| NPCID key | Type | AI | LifeMax | Defense | Damage | Value | KB resist | Role flags |
| BlueSlime | 1 | 1 | 25 | 2 | 7 | 25f | 1f (global default) | defaults |
| DemonEye | 2 | 2 | 60 | 2 | 18 | 75f | 0.8f | defaults |
| Zombie | 3 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| EyeofCthulhu | 4 | 4 | 2800 | 12 | 15 | 30000f | 0f | boss=true; npcSlots=5f;  |
| ServantofCthulhu | 5 | 5 | 8 | 0 | 12 | 0f (global default) | 1f (global default) | defaults |
| EaterofSouls | 6 | 5 | 40<br>Main.remixWorld => (int)((float)lifeMax * scale) | 8<br>Main.remixWorld => (int)((float)defense * scale) | 22<br>Main.remixWorld => (int)((float)damage * scale) | 90f<br>Main.remixWorld => (int)(value * scale) | 0.5f | defaults |
| DevourerHead | 7 | 6 | 100<br>Main.remixWorld => (int)((float)lifeMax * scale) | 2<br>Main.remixWorld => (int)((float)defense * scale) | 31<br>Main.remixWorld => (int)((float)damage * scale) | 140f<br>Main.remixWorld => (int)(value * scale) | 0f | npcSlots=3.5f;  |
| DevourerBody | 8 | 6 | 100<br>Main.remixWorld => (int)((float)lifeMax * scale) | 6<br>Main.remixWorld => (int)((float)defense * scale) | 16<br>Main.remixWorld => (int)((float)damage * scale) | 140f<br>Main.remixWorld => (int)(value * scale) | 0f | defaults |
| DevourerTail | 9 | 6 | 100<br>Main.remixWorld => (int)((float)lifeMax * scale) | 10<br>Main.remixWorld => (int)((float)defense * scale) | 13<br>Main.remixWorld => (int)((float)damage * scale) | 140f<br>Main.remixWorld => (int)(value * scale) | 0f | defaults |
| GiantWormHead | 10 | 6 | 30 | 0 | 8 | 40f | 0f | defaults |
| GiantWormBody | 11 | 6 | 30 | 4 | 4 | 40f | 0f | defaults |
| GiantWormTail | 12 | 6 | 30 | 6 | 4 | 40f | 0f | defaults |
| EaterofWorldsHead | 13 | 6 | 150 | 2 | 22 | 800f | 0f | npcSlots=5f;  |
| EaterofWorldsBody | 14 | 6 | 150 | 4 | 13 | 800f | 0f | defaults |
| EaterofWorldsTail | 15 | 6 | 150 | 8 | 11 | 800f | 0f | defaults |
| MotherSlime | 16 | 1 | 90 | 7 | 20 | 75f | 0.6f | npcSlots=2f;  |
| Merchant | 17 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Nurse | 18 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| ArmsDealer | 19 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Dryad | 20 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Skeleton | 21 | 3 | 60 | 8 | 20 | 100f | 0.5f | defaults |
| Guide | 22 | 7 | 250 | 30 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| MeteorHead | 23 | 5 | 26 | 6 | 40 | 80f | 0.4f | defaults |
| FireImp | 24 | 8 | 70 | 16 | 30 | 350f | 0.5f | npcSlots=3f;  |
| BurningSphere | 25 | 9 | 1 | 0 | 30 | 0f (global default) | 0f | defaults |
| GoblinPeon | 26 | 3 | 60 | 4 | 12 | 100f | 0.8f | defaults |
| GoblinThief | 27 | 3 | 80 | 6 | 20 | 200f | 0.7f | defaults |
| GoblinWarrior | 28 | 3 | 110 | 8 | 25 | 150f | 0.5f | defaults |
| GoblinSorcerer | 29 | 8 | 40 | 2 | 20 | 200f | 0.6f | defaults |
| ChaosBall | 30 | 9 | 1 | 0 | 20 | 0f (global default) | 0f | defaults |
| AngryBones | 31 | 3 | 80 | 8 | 26 | 130f | 0.8f | defaults |
| DarkCaster | 32 | 8 | 50 | 2 | 20 | 140f | 0.6f | npcSlots=2f;  |
| WaterSphere | 33 | 9 | 1 | 0 | 20 | 0f (global default) | 0f | defaults |
| CursedSkull | 34 | 10 | 40 | 6 | 35 | 150f | 0.2f | npcSlots=0.75f;  |
| SkeletronHead | 35 | 11 | 4400 | 10 | 32 | 50000f | 0f | boss=true; npcSlots=6f;  |
| SkeletronHand | 36 | 12 | 600 | 14 | 20 | 0f (global default) | 0f | defaults |
| OldMan | 37 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Demolitionist | 38 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| BoneSerpentHead | 39 | 6 | 300 | 12 | 36 | 1200f | 0f | npcSlots=6f;  |
| BoneSerpentBody | 40 | 6 | 300 | 18 | 20 | 1200f | 0f | defaults |
| BoneSerpentTail | 41 | 6 | 300 | 18 | 16 | 1200f | 0f | defaults |
| Hornet | 42 | 5 | 48 | 12 | 26 | 200f | 0.5f | defaults |
| ManEater | 43 | 13 | 110 | 10 | 34 | 350f | 0f | defaults |
| UndeadMiner | 44 | 3 | 70 | 9 | 22 | 250f | 0.5f | defaults |
| Tim | 45 | 8 | 200 | 4 | 20 | 5000f | 0.6f | defaults |
| Bunny | 46 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| CorruptBunny | 47 | 3 | 70 | 4 | 20 | 500f | 1f (global default) | defaults |
| Harpy | 48 | 14 | 100 | 8 | 25 | 300f | 0.6f | defaults |
| CaveBat | 49 | 14 | 16 | 2 | 13 | 90f | 0.8f | npcSlots=0.5f;  |
| KingSlime | 50 | 15 | 2000 | 10 | 40 | 10000f | 0f | boss=true; npcSlots=5f;  |
| JungleBat | 51 | 14 | 34 | 4 | 20 | 80f | 0.8f | npcSlots=0.5f;  |
| DoctorBones | 52 | 3 | 500 | 10 | 20 | 1000f | 0.5f | defaults |
| TheGroom | 53 | 3 | 200 | 8 | 14 | 1000f | 0.5f | defaults |
| Clothier | 54 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Goldfish | 55 | 16 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| Snatcher | 56 | 13 | 60 | 10 | 25 | 90f | 0f | defaults |
| CorruptGoldfish | 57 | 16 | 100 | 6 | 30 | 500f | 1f (global default) | defaults |
| Piranha | 58 | 16 | 30 | 2 | 25 | 50f | 1f (global default) | npcSlots=0.5f;  |
| LavaSlime | 59 | 1 | 50<br>Main.remixWorld => 25 | 10<br>Main.remixWorld => 2 | 15<br>Main.remixWorld => 7 | 120f<br>Main.remixWorld => 25f | 1f (global default) | defaults |
| Hellbat | 60 | 14 | 46 | 8 | 35 | 120f | 0.8f | npcSlots=0.5f;  |
| Vulture | 61 | 17 | 40 | 4 | 15 | 60f | 0.8f | defaults |
| Demon | 62 | 14 | 120 | 8 | 32 | 300f | 0.8f | npcSlots=2f;  |
| BlueJellyfish | 63 | 18 | 34 | 4 | 25 | 100f | 1f (global default) | defaults |
| PinkJellyfish | 64 | 18 | 70 | 6 | 30 | 100f | 1f (global default) | defaults |
| Shark | 65 | 16 | 300 | 2 | 40 | 400f | 0.7f | defaults |
| VoodooDemon | 66 | 14 | 140 | 8 | 32 | 1000f | 0.8f | npcSlots=2f;  |
| Crab | 67 | 3 | 40 | 10 | 20 | 60f | 1f (global default) | defaults |
| DungeonGuardian | 68 | 11 | 9999 | 9999 | 9999 | 0f (global default) | 0f | defaults |
| Antlion | 69 | 19 | 45 | 6 | 10 | 60f | 0f | defaults |
| SpikeBall | 70 | 20 | 100 | 100 | 32 | 0f (global default) | 0f | npcSlots=0.3f;  |
| DungeonSlime | 71 | 1 | 150 | 7 | 30 | 150f | 0.6f | npcSlots=2f;  |
| BlazingWheel | 72 | 21 | 100 | 100 | 24 | 0f (global default) | 0f | npcSlots=0.3f;  |
| GoblinScout | 73 | 3 | 80 | 6 | 20 | 200f | 0.7f | defaults |
| Bird | 74 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| Pixie | 75 | 22 | 150 | 20 | 55 | 350f | 0.6f | defaults |
| ArmoredSkeleton | 77 | 3 | 260 | 28 | 40 | 400f | 0.4f | defaults |
| Mummy | 78 | 3 | 130 | 16 | 50 | 600f | 0.6f | defaults |
| DarkMummy | 79 | 3 | 180 | 18 | 60 | 700f | 0.5f | defaults |
| LightMummy | 80 | 3 | 200 | 18 | 55 | 700f | 0.55f | defaults |
| CorruptSlime | 81 | 1 | 170 | 20 | 55 | 400f | 1f (global default) | defaults |
| Wraith | 82 | 22 | 160<br>Main.remixWorld => (int)((float)lifeMax * scale) | 16<br>Main.remixWorld => (int)((float)defense * scale) | 65<br>Main.remixWorld => (int)((float)damage * scale) | 500f<br>Main.remixWorld => (int)(value * scale) | 0.7f | defaults |
| CursedHammer | 83 | 23 | 200 | 18 | 80 | 1000f | 0.4f | defaults |
| EnchantedSword | 84 | 23 | 200 | 18 | 80 | 1000f | 0.4f | defaults |
| Mimic | 85 | 25 | 500<br>!Main.hardMode => 300 | 30<br>!Main.hardMode => 12 | 80<br>!Main.hardMode => 30 | 100000f<br>!Main.hardMode => Item.buyPrice(0, 2) | 0.3f | defaults |
| Unicorn | 86 | 26 | 400 | 30 | 65 | 1000f | 0.3f | defaults |
| WyvernHead | 87 | 6 | 4000 | 10 | 80 | 10000f | 0f | npcSlots=5f;  |
| WyvernLegs | 88 | 6 | 4000 | 20 | 40 | 10000f | 0f | defaults |
| WyvernBody | 89 | 6 | 4000 | 20 | 40 | 2000f | 0f | defaults |
| WyvernBody2 | 90 | 6 | 4000 | 20 | 40 | 10000f | 0f | defaults |
| WyvernBody3 | 91 | 6 | 4000 | 20 | 40 | 10000f | 0f | defaults |
| WyvernTail | 92 | 6 | 4000 | 20 | 40 | 10000f | 0f | defaults |
| GiantBat | 93 | 14 | 100 | 16 | 45 | 400f | 0.75f | npcSlots=0.5f;  |
| Corruptor | 94 | 5 | 230 | 32 | 60 | 500f | 0.55f | defaults |
| DiggerHead | 95 | 6 | 200 | 10 | 45 | 300f | 0f | defaults |
| DiggerBody | 96 | 6 | 200 | 20 | 28 | 300f | 0f | defaults |
| DiggerTail | 97 | 6 | 200 | 30 | 26 | 300f | 0f | defaults |
| SeekerHead | 98 | 6 | 500 | 36 | 70 | 700f | 0f | npcSlots=3.5f;  |
| SeekerBody | 99 | 6 | 500 | 40 | 55 | 700f | 0f | defaults |
| SeekerTail | 100 | 6 | 500 | 44 | 40 | 700f | 0f | defaults |
| Clinger | 101 | 13 | 320 | 30 | 70 | 600f | 0.2f | defaults |
| AnglerFish | 102 | 16 | 90 | 22 | 80 | 500f | 1f (global default) | npcSlots=0.5f;  |
| GreenJellyfish | 103 | 18 | 120 | 30 | 80 | 800f | 1f (global default) | defaults |
| Werewolf | 104 | 3 | 350 | 38 | 70 | 1000f | 0.4f | defaults |
| BoundGoblin | 105 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| BoundWizard | 106 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| GoblinTinkerer | 107 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Wizard | 108 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Clown | 109 | 3 | 800 | 25 | 60 | 10000f | 0.2f | defaults |
| SkeletonArcher | 110 | 3 | 210 | 14 | 45 | 400f | 0.55f | defaults |
| GoblinArcher | 111 | 3 | 80 | 6 | 20 | 200f | 0.7f | defaults |
| VileSpit | 112 | 9 | 1 | 0 | 65 | 0f (global default) | 0f | defaults |
| WallofFlesh | 113 | 27 | 8000 | 12 | 50 | 80000f | 0f | boss=true; npcSlots=10f;  |
| WallofFleshEye | 114 | 28 | 8000 | 0 | 50 | 80000f | 0f | defaults |
| TheHungry | 115 | 29 | 240 | 10 | 30 | 0f (global default) | 1.1f | defaults |
| TheHungryII | 116 | 2 | 80 | 6 | 30 | 0f (global default) | 0.8f | defaults |
| LeechHead | 117 | 6 | 60 | 2 | 26 | 0f (global default) | 0f | defaults |
| LeechBody | 118 | 6 | 60 | 6 | 22 | 0f (global default) | 0f | defaults |
| LeechTail | 119 | 6 | 60 | 10 | 18 | 0f (global default) | 0f | defaults |
| ChaosElemental | 120 | 3 | 370 | 30 | 40 | 600f | 0.4f | defaults |
| Slimer | 121 | 14 | 60 | 20 | 45 | 0f (global default) | 0.8f | defaults |
| Gastropod | 122 | 22 | 220 | 22 | 60 | 600f | 0.8f | defaults |
| BoundMechanic | 123 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| Mechanic | 124 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Retinazer | 125 | 30 | 20000 | 10 | 45 | 120000f | 0f | boss=true; npcSlots=5f;  |
| Spazmatism | 126 | 31 | 23000 | 10 | 50 | 120000f | 0f | boss=true; npcSlots=5f;  |
| SkeletronPrime | 127 | 32 | 28000 | 24 | 47 | 120000f | 0f | boss=true; npcSlots=6f;  |
| PrimeCannon | 128 | 35 | 7000 | 23 | 30 | 0f (global default) | 0f | defaults |
| PrimeSaw | 129 | 33 | 9000 | 38 | 56 | 0f (global default) | 0f | defaults |
| PrimeVice | 130 | 34 | 9000 | 34 | 52 | 0f (global default) | 0f | defaults |
| PrimeLaser | 131 | 36 | 6000 | 20 | 29 | 0f (global default) | 0f | defaults |
| BaldZombie | 132 | 3 | 40 | 5 | 15 | 65f | 0.5f | defaults |
| WanderingEye | 133 | 2 | 300 | 20 | 40 | 500f | 0.8f | defaults |
| TheDestroyer | 134 | 37 | 80000 | 0 | 70 | 120000f | 0f | boss=true; npcSlots=5f;  |
| TheDestroyerBody | 135 | 37 | 80000 | 30 | 55 | 0f (global default) | 0f | npcSlots=5f;  |
| TheDestroyerTail | 136 | 37 | 80000 | 35 | 40 | 0f (global default) | 0f | npcSlots=5f;  |
| IlluminantBat | 137 | 14 | 200 | 30 | 75 | 500f | 0.75f | defaults |
| IlluminantSlime | 138 | 1 | 180 | 30 | 70 | 400f | 0.85f | defaults |
| Probe | 139 | 5 | 200 | 20 | 50 | 0f (global default) | 0.8f | defaults |
| PossessedArmor | 140 | 3 | 260 | 28 | 55 | 400f | 0.4f | defaults |
| ToxicSludge | 141 | 1 | 150 | 18 | 50 | 400f | 0.8f | defaults |
| SantaClaus | 142 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| SnowmanGangsta | 143 | 38 | 200 | 20 | 50 | 400f | 0.6f | defaults |
| MisterStabby | 144 | 38 | 240 | 26 | 65 | 400f | 0.6f | defaults |
| SnowBalla | 145 | 38 | 220 | 22 | 55 | 400f | 0.6f | defaults |
| IceSlime | 147 | 1 | 30 | 4 | 8 | 50f | 1f (global default) | defaults |
| Penguin | 148 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| PenguinBlack | 149 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| IceBat | 150 | 14 | 30 | 6 | 18 | 250f | 0.8f | npcSlots=0.5f;  |
| Lavabat | 151 | 14 | 160 | 16 | 50 | 400f | 0.6f | npcSlots=0.5f;  |
| GiantFlyingFox | 152 | 14 | 220 | 24 | 80 | 400f | 0.65f | npcSlots=0.5f;  |
| GiantTortoise | 153 | 39 | 470 | 30 | 80 | 500f | 0.3f | npcSlots=2f;  |
| IceTortoise | 154 | 39 | 400 | 28 | 55 | 450f | 0.3f | npcSlots=1.5f;  |
| Wolf | 155 | 26 | 300 | 30 | 65 | 1000f | 0.3f | defaults |
| RedDevil | 156 | 14 | 600 | 40 | 50 | 1200f | 0.5f | npcSlots=2f;  |
| Arapaima | 157 | 16 | 200 | 30 | 75 | 500f | 1f (global default) | npcSlots=0.5f;  |
| VampireBat | 158 | 14 | 750 | 32 | 60 | 5000f | 0.75f | npcSlots=2f;  |
| Vampire | 159 | 3 | 750 | 24 | 80 | 5000f | 0.4f | npcSlots=2f;  |
| Truffle | 160 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| ZombieEskimo | 161 | 3 | 50 | 8 | 16 | 80f | 0.45f | defaults |
| Frankenstein | 162 | 3 | 350 | 18 | 65 | 600f | 0.3f | defaults |
| BlackRecluse | 163 | 3 | 350 | 40 | 90 | 500f | 0.25f | npcSlots=0.5f;  |
| WallCreeper | 164 | 3 | 80 | 10 | 30 | 100f | 0.25f | npcSlots=0.5f;  |
| WallCreeperWall | 165 | 40 | 80 | 10 | 30 | 100f | 0.25f | npcSlots=0.5f;  |
| SwampThing | 166 | 3 | 450 | 26 | 70 | 1000f | 0.2f | defaults |
| UndeadViking | 167 | 3 | 70 | 10 | 24 | 200f | 0.5f | defaults |
| CorruptPenguin | 168 | 3 | 70 | 4 | 20 | 500f | 1f (global default) | defaults |
| IceElemental | 169 | 22 | 200 | 20 | 55 | 1500f | 0.6f | defaults |
| PigronCorruption | 170 | 2 | 210 | 16 | 70 | 2000f | 0.5f | defaults |
| PigronHallow | 171 | 2 | 210 | 16 | 70 | 2000f | 0.5f | defaults |
| RuneWizard | 172 | 8 | 600 | 30 | 200 | 5000f | 0.3f | defaults |
| Crimera | 173 | 5 | 40<br>Main.remixWorld => (int)((float)lifeMax * scale) | 8<br>Main.remixWorld => (int)((float)defense * scale) | 22<br>Main.remixWorld => (int)((float)damage * scale) | 90f<br>Main.remixWorld => (int)(value * scale) | 0.5f | defaults |
| Herpling | 174 | 41 | 220 | 26 | 65 | 450f | 0.3f | defaults |
| AngryTrapper | 175 | 13 | 300 | 30 | 100 | 650f | 0.25f | defaults |
| MossHornet | 176 | 5 | 220 | 22 | 70 | 600f | 0.5f | npcSlots=1.5f;  |
| Derpling | 177 | 41 | 300 | 26 | 80 | 500f | 0.5f | defaults |
| Steampunker | 178 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| CrimsonAxe | 179 | 23 | 200 | 18 | 80 | 1000f | 0.4f | defaults |
| PigronCrimson | 180 | 2 | 210 | 16 | 70 | 2000f | 0.5f | defaults |
| FaceMonster | 181 | 3 | 70<br>Main.remixWorld => (int)((double)((float)lifeMax * scale) * 1.2) | 10<br>Main.remixWorld => (int)((double)((float)defense * scale) * 1.2) | 25<br>Main.remixWorld => (int)((double)((float)damage * scale) * 1.2) | 200f<br>Main.remixWorld => (int)((double)(value * scale) * 1.2) | 0.4f | defaults |
| FloatyGross | 182 | 22 | 240 | 18 | 65 | 500f | 0.7f | defaults |
| Crimslime | 183 | 1 | 200 | 26 | 60 | 400f | 1f (global default) | defaults |
| SpikedIceSlime | 184 | 1 | 60 | 8 | 12 | 200f | 1f (global default) | defaults |
| SnowFlinx | 185 | 3 | 70 | 12 | 26 | 200f | 1.1f | defaults |
| PincushionZombie | 186 | 3 | 50 | 8 | 16 | 65f | 0.45f | defaults |
| SlimedZombie | 187 | 3 | 40 | 6 | 13 | 55f | 0.55f | defaults |
| SwampZombie | 188 | 3 | 45 | 8 | 13 | 80f | 0.45f | defaults |
| TwiggyZombie | 189 | 3 | 45 | 4 | 16 | 70f | 0.55f | defaults |
| CataractEye | 190 | 2 | 65 | 4 | 18 | 75f | 0.7f | defaults |
| SleepyEye | 191 | 2 | 60 | 2 | 16 | 75f | 0.85f | defaults |
| DialatedEye | 192 | 2 | 50 | 2 | 18 | 75f | 0.8f | defaults |
| GreenEye | 193 | 2 | 60 | 0 | 20 | 75f | 0.8f | defaults |
| PurpleEye | 194 | 2 | 60 | 4 | 14 | 75f | 0.8f | defaults |
| LostGirl | 195 | 42 | 250 | 30 | 10 | Item.buyPrice(0, 2) | 0.5f | defaults |
| Nymph | 196 | 3 | 300 | 16 | 35 | Item.buyPrice(0, 2) | 0.4f | defaults |
| ArmoredViking | 197 | 3 | 280 | 28 | 50 | 500f | 0.45f | defaults |
| Lihzahrd | 198 | 3 | 400 | 20 | 38 | 650f | 0.4f | defaults |
| LihzahrdCrawler | 199 | 3 | 400 | 30 | 60 | 650f | 0f | defaults |
| FemaleZombie | 200 | 3 | 38 | 4 | 12 | 65f | 0.6f | defaults |
| HeadacheSkeleton | 201 | 3 | 55 | 12 | 20 | 130f | 0.5f | defaults |
| MisassembledSkeleton | 202 | 3 | 65 | 8 | 18 | 120f | 0.5f | defaults |
| PantlessSkeleton | 203 | 3 | 60 | 8 | 22 | 110f | 0.5f | defaults |
| SpikedJungleSlime | 204 | 1 | 65 | 8 | 28 | 300f | 1f (global default) | defaults |
| Moth | 205 | 5 | 1000 | 28 | 70 | 600f | 0.4f | defaults |
| IcyMerman | 206 | 3 | 280 | 30 | 60 | 500f | 0.5f | defaults |
| DyeTrader | 207 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| PartyGirl | 208 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Cyborg | 209 | 7 | 500 | 30 | 10 | 0f (global default) | 0.25f | townNPC=true; friendly=true;  |
| Bee | 210 | 5 | 20 | 5 | 20 | 0f (global default) | 0.5f | defaults |
| BeeSmall | 211 | 5 | 10 | 2 | 15 | 0f (global default) | 0.5f | defaults |
| PirateDeckhand | 212 | 3 | 300 | 17 | 35 | 700f | 0.4f | defaults |
| PirateCorsair | 213 | 3 | 450 | 22 | 50 | 1000f | 0.2f | defaults |
| PirateDeadeye | 214 | 3 | 225 | 14 | 30 | 1000f | 0.3f | defaults |
| PirateCrossbower | 215 | 3 | 350 | 20 | 35 | 1500f | 0.35f | defaults |
| PirateCaptain | 216 | 3 | 3000 | 30 | 70 | 50000f | 0f | defaults |
| CochinealBeetle | 217 | 3 | 40 | 10 | 20 | 60f | 1f (global default) | defaults |
| CyanBeetle | 218 | 3 | 40 | 10 | 20 | 60f | 1f (global default) | defaults |
| LacBeetle | 219 | 3 | 40 | 10 | 20 | 60f | 1f (global default) | defaults |
| SeaSnail | 220 | 3 | 40 | 10 | 20 | 60f | 1f (global default) | defaults |
| Squid | 221 | 18 | 30 | 2 | 20 | 100f | 1f (global default) | defaults |
| QueenBee | 222 | 43 | 3400 | 8 | 30 | 50000f | 0f | boss=true; npcSlots=7f;  |
| ZombieRaincoat | 223 | 3 | 50 | 8 | 16 | 70f | 0.45f | defaults |
| FlyingFish | 224 | 44 | 20 | 4 | 9 | 300f | 0.8f | defaults |
| UmbrellaSlime | 225 | 1 | 35 | 5 | 10 | 200f | 0.75f | defaults |
| FlyingSnake | 226 | 14 | 260 | 28 | 85 | 400f | 0.65f | npcSlots=0.5f;  |
| Painter | 227 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| WitchDoctor | 228 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Pirate | 229 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| GoldfishWalker | 230 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| HornetFatty | 231 | 5 | 50 | 16 | 22 | 200f | 0.3f | defaults |
| HornetHoney | 232 | 5 | 42 | 12 | 28 | 200f | 0.6f | defaults |
| HornetLeafy | 233 | 5 | 38 | 14 | 30 | 200f | 0.45f | defaults |
| HornetSpikey | 234 | 5 | 42 | 6 | 32 | 200f | 0.55f | defaults |
| HornetStingy | 235 | 5 | 38 | 4 | 34 | 200f | 0.6f | defaults |
| JungleCreeper | 236 | 3 | 400 | 28 | 100 | 1000f | 0.25f | npcSlots=0.75f;  |
| JungleCreeperWall | 237 | 40 | 400 | 28 | 100 | 1000f | 0.25f | npcSlots=0.75f;  |
| BlackRecluseWall | 238 | 40 | 350 | 40 | 100 | 500f | 0.25f | npcSlots=0.5f;  |
| BloodCrawler | 239 | 3 | 60 | 8 | 30 | 130f | 0.5f | defaults |
| BloodCrawlerWall | 240 | 40 | 60 | 8 | 30 | 130f | 0.5f | defaults |
| BloodFeeder | 241 | 16 | 150 | 20 | 50 | 500f | 1f (global default) | npcSlots=0.5f;  |
| BloodJelly | 242 | 18 | 150 | 20 | 75 | 800f | 1f (global default) | defaults |
| IceGolem | 243 | 3 | 4000 | 32 | 60 | Item.buyPrice(0, 1, 50) | 0.05f | defaults |
| RainbowSlime | 244 | 1 | 400 | 26 | 85 | Item.buyPrice(0, 0, 20) | 0.3f | defaults |
| Golem | 245 | 45 | 15000 | 26 | 72 | Item.buyPrice(0, 15) | 0f | boss=true; npcSlots=5f;  |
| GolemHead | 246 | 46 | 25000 | 20 | 64 | 0f (global default) | 0f | defaults |
| GolemFistLeft | 247 | 47 | 10000 | 28 | 59 | 0f (global default) | 0f | defaults |
| GolemFistRight | 248 | 47 | 10000 | 28 | 59 | 0f (global default) | 0f | defaults |
| GolemHeadFree | 249 | 48 | 16000 | 32 | 80 | 0f (global default) | 0f | defaults |
| AngryNimbus | 250 | 49 | 300 | 24 | 50 | 300f | 0.3f | defaults |
| Eyezor | 251 | 3 | 1000 | 30 | 50 | Item.buyPrice(0, 0, 50) | 0.3f | defaults |
| Parrot | 252 | 5 | 100 | 12 | 80 | 500f | 0.7f | npcSlots=0.5f;  |
| Reaper | 253 | 22 | 700 | 22 | 80 | 1500f | 0.6f | defaults |
| ZombieMushroom | 254 | 3 | 180 | 10 | 40 | 1000f | 0.4f | defaults |
| ZombieMushroomHat | 255 | 3 | 220 | 16 | 38 | 1200f | 0.3f | defaults |
| FungoFish | 256 | 18 | 140 | 20 | 90 | 1200f | 1f (global default) | npcSlots=0.3f;  |
| AnomuraFungus | 257 | 3 | 230 | 24 | 38 | 1300f | 0.3f | npcSlots=0.3f;  |
| MushiLadybug | 258 | 3 | 220 | 16 | 60 | 1500f | 0.3f | npcSlots=0.3f;  |
| FungiBulb | 259 | 13 | 90 | 4 | 24 | 350f | 0f | npcSlots=0.3f;  |
| GiantFungiBulb | 260 | 13 | 300 | 20 | 70 | 1250f | 0f | npcSlots=0.3f;  |
| FungiSpore | 261 | 50 | 1 | 0 | 80 | 0f (global default) | 0f | npcSlots=0f;  |
| Plantera | 262 | 51 | 30000 | 14 | 50 | Item.buyPrice(0, 15) | 0f | boss=true; npcSlots=16f;  |
| PlanterasHook | 263 | 52 | 4000 | 24 | 60 | 0f (global default) | 1f (global default) | defaults |
| PlanterasTentacle | 264 | 53 | 1000 | 20 | 60 | 0f (global default) | 1f (global default) | defaults |
| Spore | 265 | 50 | 1 | 0 | 70 | 0f (global default) | 0f | npcSlots=0f;  |
| BrainofCthulhu | 266 | 54 | 1250 | 14 | 30 | 50000f | 0.45f | boss=true; npcSlots=6f;  |
| Creeper | 267 | 55 | 100 | 10 | 20 | 0f (global default) | 0.8f | defaults |
| IchorSticker | 268 | 22 | 340 | 20 | 55 | 450f | 0.6f | defaults |
| RustyArmoredBonesAxe | 269 | 3 | 550 | 34 | 70 | 1000f | 0.3f | defaults |
| RustyArmoredBonesFlail | 270 | 3 | 400 | 50 | 55 | 1000f | 0.2f | defaults |
| RustyArmoredBonesSword | 271 | 3 | 450 | 40 | 70 | 1000f | 0.25f | defaults |
| RustyArmoredBonesSwordNoArmor | 272 | 3 | 400 | 28 | 75 | 1000f | 0.35f | defaults |
| BlueArmoredBones | 273 | 3 | 500 | 50 | 45 | 1000f | 0.15f | defaults |
| BlueArmoredBonesMace | 274 | 3 | 350 | 34 | 65 | 1000f | 0.4f | defaults |
| BlueArmoredBonesNoPants | 275 | 3 | 550 | 50 | 45 | 1000f | 0.15f | defaults |
| BlueArmoredBonesSword | 276 | 3 | 500 | 54 | 85 | 1000f | 0.2f | defaults |
| HellArmoredBones | 277 | 3 | 400 | 32 | 70 | 1000f | 0.4f | defaults |
| HellArmoredBonesSpikeShield | 278 | 3 | 450 | 48 | 65 | 1000f | 0.3f | defaults |
| HellArmoredBonesMace | 279 | 3 | 500 | 54 | 40 | 1000f | 0.2f | defaults |
| HellArmoredBonesSword | 280 | 3 | 500 | 34 | 75 | 1000f | 0.4f | defaults |
| RaggedCaster | 281 | 8 | 400 | 20 | 40 | 1500f | 0.6f | npcSlots=2f;  |
| RaggedCasterOpenCoat | 282 | 8 | 450 | 28 | 35 | 1500f | 0.5f | npcSlots=2f;  |
| Necromancer | 283 | 8 | 300 | 18 | 50 | 1500f | 0.55f | npcSlots=2f;  |
| NecromancerArmored | 284 | 8 | 450 | 24 | 35 | 1500f | 0.5f | npcSlots=2f;  |
| DiabolistRed | 285 | 8 | 200 | 12 | 50 | 1500f | 0.7f | npcSlots=2f;  |
| DiabolistWhite | 286 | 8 | 250 | 10 | 60 | 1500f | 0.65f | npcSlots=2f;  |
| BoneLee | 287 | 3 | 1000 | 42 | 90 | 2000f | 0.3f | defaults |
| DungeonSpirit | 288 | 56 | 200 | 30 | 70 | 500f | 0.2f | defaults |
| GiantCursedSkull | 289 | 10 | 400 | 20 | 60 | 150f | 0.2f | npcSlots=0.75f;  |
| Paladin | 290 | 3 | 5000 | 50 | 100 | 50000f | 0f | defaults |
| SkeletonSniper | 291 | 3 | 400 | 28 | 60 | 1000f | 0.4f | defaults |
| TacticalSkeleton | 292 | 3 | 400 | 28 | 60 | 1000f | 0.4f | defaults |
| SkeletonCommando | 293 | 3 | 400 | 28 | 60 | 1000f | 0.4f | defaults |
| AngryBonesBig | 294 | 3 | 70 | 6 | 34 | 130f | 0.9f | defaults |
| AngryBonesBigMuscle | 295 | 3 | 70 | 12 | 28 | 130f | 0.7f | defaults |
| AngryBonesBigHelmet | 296 | 3 | 120 | 14 | 24 | 130f | 0.6f | defaults |
| BirdBlue | 297 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| BirdRed | 298 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| Squirrel | 299 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Mouse | 300 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| Raven | 301 | 17 | 35 | 2 | 12 | 50f | 0.85f | defaults |
| SlimeMasked | 302 | 1 | 25 | 2 | 7 | 25f | 1f (global default) | defaults |
| BunnySlimed | 303 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| HoppinJack | 304 | 1 | 175 | 20 | 80 | 500f | 0.5f | npcSlots=0.3f;  |
| Scarecrow1 | 305 | — | 500 | 18 | 60 | 0f (global default) | 0.4f | defaults |
| Scarecrow2 | 306 | — | 400 | 14 | 52 | 0f (global default) | 0.2f | defaults |
| Scarecrow3 | 307 | — | 600 | 16 | 78 | 0f (global default) | 0.25f | defaults |
| Scarecrow4 | 308 | — | 650 | 14 | 66 | 0f (global default) | 0.35f | defaults |
| Scarecrow5 | 309 | — | 450 | 26 | 52 | 0f (global default) | 0.5f | defaults |
| Scarecrow6 | 310 | — | 500 | 18 | 60 | 0f (global default) | 0.4f | defaults |
| Scarecrow7 | 311 | — | 400 | 14 | 52 | 0f (global default) | 0.2f | defaults |
| Scarecrow8 | 312 | — | 600 | 16 | 78 | 0f (global default) | 0.25f | defaults |
| Scarecrow9 | 313 | — | 650 | 14 | 66 | 0f (global default) | 0.35f | defaults |
| Scarecrow10 | 314 | — | 450 | 26 | 52 | 0f (global default) | 0.5f | defaults |
| HeadlessHorseman | 315 | 26 | 5000 | 40 | 130 | 10000f | 0f | defaults |
| Ghost | 316 | 22 | 50 | 4 | 15 | 90f | 0.5f | defaults |
| DemonEyeOwl | 317 | 2 | 75 | 6 | 16 | 100f | 0.7f | defaults |
| DemonEyeSpaceship | 318 | 2 | 60 | 4 | 20 | 100f | 0.65f | defaults |
| ZombieDoctor | 319 | 3 | 40 | 6 | 20 | 85f | 0.6f | defaults |
| ZombieSuperman | 320 | 3 | 60 | 8 | 15 | 105f | 0.5f | defaults |
| ZombiePixie | 321 | 3 | 34 | 14 | 20 | 120f | 0.3f | defaults |
| SkeletonTopHat | 322 | 3 | 115 | 0 | 23 | 130f | 0.65f | defaults |
| SkeletonAstonaut | 323 | 3 | 65 | 10 | 18 | 120f | 0.5f | defaults |
| SkeletonAlien | 324 | 3 | 70 | 10 | 22 | 130f | 0.4f | defaults |
| MourningWood | 325 | 57 | 14000 | 34 | 120 | 10000f | 0f | npcSlots=3f;  |
| Splinterling | 326 | 3 | 1200 | 32 | 100 | 2000f | 0.2f | defaults |
| Pumpking | 327 | 58 | 26000 | 40 | 50 | 50000f | 0f | npcSlots=5f;  |
| PumpkingBlade | 328 | 59 | 5000 | 14 | 65 | 0f (global default) | 0f | npcSlots=0f;  |
| Hellhound | 329 | 26 | 1800 | 38 | 80 | 3000f | 0.3f | defaults |
| Poltergeist | 330 | 22 | 1250 | 44 | 90 | 4500f | 0.4f | defaults |
| ZombieXmas | 331 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| ZombieSweater | 332 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| SlimeRibbonWhite | 333 | 1 | 25 | 2 | 7 | 25f | 1f (global default) | defaults |
| SlimeRibbonYellow | 334 | 1 | 23 | 2 | 6 | 25f | 1f (global default) | defaults |
| SlimeRibbonGreen | 335 | 1 | 29 | 3 | 8 | 25f | 1f (global default) | defaults |
| SlimeRibbonRed | 336 | 1 | 22 | 1 | 5 | 25f | 1f (global default) | defaults |
| BunnyXmas | 337 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| ZombieElf | 338 | — | 600 | 18 | 65 | 0f (global default) | 0.4f | defaults |
| ZombieElfBeard | 339 | — | 700 | 24 | 52 | 0f (global default) | 0.2f | defaults |
| ZombieElfGirl | 340 | — | 500 | 14 | 78 | 0f (global default) | 0.25f | defaults |
| PresentMimic | 341 | 25 | 900 | 32 | 100 | Item.buyPrice(0, 0, 20) | 0.25f | defaults |
| GingerbreadMan | 342 | 3 | 750 | 26 | 90 | 1800f | 0.2f | defaults |
| Yeti | 343 | 3 | 3500 | 50 | 140 | 3000f | 0f | npcSlots=2f;  |
| Everscream | 344 | 57 | 13000 | 38 | 110 | 10000f | 0f | npcSlots=3f;  |
| IceQueen | 345 | 60 | 34000 | 38 | 120 | 50000f | 0f | npcSlots=5f;  |
| SantaNK1 | 346 | 61 | 18000 | 56 | 120 | 10000f | 0f | npcSlots=4f;  |
| ElfCopter | 347 | 62 | 1200 | 28 | 60 | 1000f | 0.4f | npcSlots=1.5f;  |
| Nutcracker | 348 | 3 | 1800 | 26 | 80 | 1500f | 0.4f | npcSlots=1.5f;  |
| NutcrackerSpinning | 349 | 3 | 1800 | 42 | 100 | 1500f | 0.1f | npcSlots=1.5f;  |
| ElfArcher | 350 | 3 | 900 | 30 | 70 | 900f | 0.45f | defaults |
| Krampus | 351 | 3 | 2500 | 40 | 100 | 3000f | 0.1f | npcSlots=1.75f;  |
| Flocko | 352 | 63 | 450 | 8 | 75 | 500f | 0.4f | npcSlots=2f;  |
| Stylist | 353 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| WebbedStylist | 354 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| Firefly | 355 | 64 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.2f;  |
| Butterfly | 356 | 65 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| Worm | 357 | 66 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | friendly=true; npcSlots=0.1f;  |
| LightningBug | 358 | 64 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.2f;  |
| Snail | 359 | 67 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.5f;  |
| GlowingSnail | 360 | 67 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.5f;  |
| Frog | 361 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Duck | 362 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Duck2 | 363 | 68 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| DuckWhite | 364 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| DuckWhite2 | 365 | 68 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| ScorpionBlack | 366 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Scorpion | 367 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| TravellingMerchant | 368 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| Angler | 369 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| DukeFishron | 370 | 69 | 60000 | 50 | 100 | 250000f | 0f | boss=true; npcSlots=10f;  |
| DetonatingBubble | 371 | 70 | 1 | 0 | 100 | 0f (global default) | 0f | defaults |
| Sharkron | 372 | 71 | 100 | 100 | 100 | 0f (global default) | 0f | defaults |
| Sharkron2 | 373 | 71 | 100 | 100 | 120 | 0f (global default) | 0f | defaults |
| TruffleWorm | 374 | 66 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.15f;  |
| TruffleWormDigger | 375 | 6 | 5 | 0 | 0 | 0f (global default) | 0f | npcSlots=0.15f;  |
| SleepingAngler | 376 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| Grasshopper | 377 | 1 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | friendly=true; npcSlots=0.1f;  |
| ChatteringTeethBomb | 378 | 41 | 200 | 30 | 120 | 450f | 0.5f | defaults |
| MartianOfficer | 383 | 3 | 300 | 50 | 75 | 1200f | 0.25f | defaults |
| ForceBubble | 384 | 72 | 1000 | 20 | 0 (global default) | 0f (global default) | 1f (global default) | npcSlots=0f;  |
| GrayGrunt | 385 | 3 | 750 | 30 | 80 | 600f | 0f | npcSlots=0.75f;  |
| MartianEngineer | 386 | 3 | 400 | 34 | 40 | 1200f | 0.4f | defaults |
| MartianTurret | 387 | 73 | 200 | 40 | 10 | 0f (global default) | 0f | defaults |
| MartianDrone | 388 | 74 | 300 | 16 | 60 | 1000f | 0.4f | npcSlots=1.5f;  |
| GigaZapper | 389 | 3 | 600 | 38 | 75 | 1200f | 0.3f | npcSlots=0.5f;  |
| ScutlixRider | 390 | 75 | 350 | 30 | 65 | 1200f | 1f (global default) | npcSlots=0.5f;  |
| Scutlix | 391 | 3 | 600 | 30 | 85 | 1200f | 0.1f | npcSlots=0.5f;  |
| MartianSaucer | 392 | 75 | 100 | 100 | 50 | 0f | 0f | npcSlots=0f;  |
| MartianSaucerTurret | 393 | 75 | 5000 | 20 | 60 | 0f | 0f | defaults |
| MartianSaucerCannon | 394 | 75 | 3500 | 20 | 60 | 0f | 0f | defaults |
| MartianSaucerCore | 395 | 76 | 10000 | 0 | 80 | 0f | 0f | boss=true; npcSlots=6f;  |
| MoonLordHead | 396 | 79 | 45000 | 50 | 0 | 0f | 0f | boss=true; npcSlots=6f;  |
| MoonLordHand | 397 | 78 | 25000 | 40 | 0 | 0f | 0f | boss=true; npcSlots=6f;  |
| MoonLordCore | 398 | 77 | 50000 | 70 | 0 | 1000000f | 0f | boss=true; npcSlots=6f;  |
| MartianProbe | 399 | 80 | 500 | 5 | 5 | 0f | 0f | npcSlots=0f;  |
| MoonLordFreeEye | 400 | 81 | 100 | 0 | 60 | 0f | 0f | npcSlots=0f;  |
| MoonLordLeechBlob | 401 | 82 | 400 | 0 | 0 | 0f | 0f | npcSlots=0f;  |
| StardustWormHead | 402 | 6 | 1200 | 10 | 80 | 0f | 0f | defaults |
| StardustCellBig | 405 | 85 | 300 | 50 | 120 | 0f (global default) | 0.3f | npcSlots=2f;  |
| StardustCellSmall | 406 | 95 | 300 | 0 | 70 | 0f (global default) | 0.4f | defaults |
| StardustJellyfishBig | 407 | 96 | 1500 | 38 | 70 | 0f (global default) | 0.03f | npcSlots=3f;  |
| StardustSpiderBig | 409 | 3 | 800 | 40 | 70 | 0f (global default) | 0.4f | npcSlots=3f;  |
| StardustSpiderSmall | 410 | 26 | 200 | 10 | 80 | 0f (global default) | 0.3f | npcSlots=0.5f;  |
| StardustSoldier | 411 | 3 | 700 | 34 | 80 | 0f (global default) | 0.4f | defaults |
| SolarCrawltipedeHead | 412 | 6 | 10000 | 1000 | 120 | 0f (global default) | 0f | npcSlots=2f;  |
| SolarCrawltipedeBody | 413 | 6 | 10000 | 1000 | 80 | 0f (global default) | 0f | npcSlots=0f;  |
| SolarCrawltipedeTail | 414 | 6 | 10000 | 0 | 50 | 0f (global default) | 0f | npcSlots=0f;  |
| SolarDrakomire | 415 | 3 | 800 | 32 | 55 | 0f (global default) | 0.2f | defaults |
| SolarDrakomireRider | 416 | 75 | 800 | 28 | 80 | 0f (global default) | 0.2f | defaults |
| SolarSroller | 417 | 39 | 700 | 34 | 80 | 0f (global default) | 0f | npcSlots=2f;  |
| SolarCorite | 418 | 74 | 600 | 26 | 70 | 0f (global default) | 0.2f | defaults |
| SolarSolenian | 419 | 3 | 800 | 30 | 90 | 0f (global default) | 0.4f | defaults |
| NebulaBrain | 420 | 97 | 1300 | 20 | 75 | 0f (global default) | 0.5f | npcSlots=3f;  |
| NebulaHeadcrab | 421 | 85 | 330 | 34 | 70 | 0f (global default) | 0.5f | defaults |
| LunarTowerVortex | 422 | 94 | 20000 | 20 | 0 | 0f | 0f | npcSlots=0f;  |
| NebulaBeast | 423 | 26 | 850 | 46 | 90 | 0f (global default) | 0.2f | defaults |
| NebulaSoldier | 424 | 3 | 700 | 30 | 80 | 0f (global default) | 0.6f | npcSlots=2f;  |
| VortexRifleman | 425 | 3 | 800 | 40 | 100 | 0f (global default) | 0.4f | defaults |
| VortexHornetQueen | 426 | 3 | 1000 | 44 | 100 | 0f (global default) | 0.3f | defaults |
| VortexHornet | 427 | 3 | 500 | 20 | 75 | 0f (global default) | 0.5f | npcSlots=0.5f;  |
| VortexLarva | 428 | 3 | 200 | 6 | 50 | 0f (global default) | 1f (global default) | defaults |
| VortexSoldier | 429 | 3 | 700 | 34 | 90 | 0f (global default) | 0.6f | npcSlots=2f;  |
| ArmedZombie | 430 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| ArmedZombieEskimo | 431 | 3 | 50 | 8 | 16 | 80f | 0.45f | defaults |
| ArmedZombiePincussion | 432 | 3 | 50 | 8 | 16 | 65f | 0.45f | defaults |
| ArmedZombieSlimed | 433 | 3 | 40 | 6 | 13 | 55f | 0.55f | defaults |
| ArmedZombieSwamp | 434 | 3 | 45 | 8 | 13 | 80f | 0.45f | defaults |
| ArmedZombieTwiggy | 435 | 3 | 45 | 4 | 16 | 70f | 0.55f | defaults |
| ArmedZombieCenx | 436 | 3 | 38 | 4 | 12 | 65f | 0.6f | defaults |
| CultistTablet | 437 | 83 | 400 | 0 | 0 | 0f | 0f | npcSlots=0f;  |
| CultistDevote | 438 | 83 | 400 | 0 | 0 | 0f | 0f | npcSlots=0f;  |
| CultistBoss | 439 | 84 | 32000 | 42 | 50 | 100000f | 0f | boss=true; npcSlots=10f;  |
| CultistBossClone | 440 | 84 | 10000 | 35 | 0 | 0f (global default) | 0f | npcSlots=0f;  |
| TaxCollector | 441 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| GoldBird | 442 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| GoldBunny | 443 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| GoldButterfly | 444 | 65 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| GoldFrog | 445 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| GoldGrasshopper | 446 | 1 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | friendly=true; npcSlots=0.1f;  |
| GoldMouse | 447 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| GoldWorm | 448 | 66 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | friendly=true; npcSlots=0.1f;  |
| BoneThrowingSkeleton | 449 | 3 | 60 | 8 | 20 | 100f | 0.5f | defaults |
| BoneThrowingSkeleton2 | 450 | 3 | 55 | 12 | 20 | 130f | 0.5f | defaults |
| BoneThrowingSkeleton3 | 451 | 3 | 65 | 8 | 18 | 120f | 0.5f | defaults |
| BoneThrowingSkeleton4 | 452 | 3 | 60 | 8 | 22 | 110f | 0.5f | defaults |
| SkeletonMerchant | 453 | 7 | 250 | 30 | 10 | 0f (global default) | 0.5f | friendly=true; npcSlots=7f;  |
| CultistDragonHead | 454 | 6 | 10000 | 15 | 100 | 0f | 0f | npcSlots=5f;  |
| CultistDragonBody1 | 455 | 6 | 10000 | 30 | 50 | 0f | 0f | defaults |
| CultistDragonBody2 | 456 | 6 | 10000 | 30 | 50 | 0f | 0f | defaults |
| CultistDragonBody3 | 457 | 6 | 10000 | 30 | 50 | 0f | 0f | defaults |
| CultistDragonBody4 | 458 | 6 | 10000 | 30 | 50 | 0f | 0f | defaults |
| CultistDragonTail | 459 | 6 | 10000 | 30 | 50 | 0f | 0f | defaults |
| Butcher | 460 | 3 | 700 | 30 | 70 | 1000f | 0.25f | defaults |
| CreatureFromTheDeep | 461 | 3 | 400 | 22 | 60 | 1000f | 0.3f | defaults |
| Fritz | 462 | 3 | 270 | 14 | 70 | 600f | 0.7f | npcSlots=0.5f;  |
| Nailhead | 463 | 3 | 4000 | 34 | 100 | 3000f | 0.1f | defaults |
| CrimsonBunny | 464 | 3 | 75 | 5 | 21 | 500f | 1f (global default) | defaults |
| CrimsonGoldfish | 465 | 16 | 110 | 7 | 31 | 500f | 1f (global default) | defaults |
| Psycho | 466 | 3 | 550 | 40 | 70 | 1500f | 0.5f | defaults |
| DeadlySphere | 467 | 85 | 350 | 80 | 100 | 900f | 0.7f | defaults |
| DrManFly | 468 | 3 | 500 | 24 | 65 | 1300f | 0.6f | defaults |
| ThePossessed | 469 | 3 | 600 | 28 | 68 | 1300f | 0.35f | defaults |
| CrimsonPenguin | 470 | 3 | 75 | 5 | 21 | 500f | 1f (global default) | defaults |
| GoblinSummoner | 471 | 3 | 2000 | 26 | 80 | 5000f | 0.15f | defaults |
| ShadowFlameApparition | 472 | 86 | 180 | 18 | 50 | 0f | 0f | npcSlots=0.1f;  |
| BigMimicCorruption | 473 | 87 | 3500 | 34 | 90 | 30000f | 0.1f | defaults |
| BigMimicCrimson | 474 | 87 | 3500 | 34 | 90 | 30000f | 0.1f | defaults |
| BigMimicHallow | 475 | 87 | 3500 | 34 | 90 | 30000f | 0.1f | defaults |
| BigMimicJungle | 476 | 87 | 3500 | 34 | 90 | 30000f | 0.1f | defaults |
| Mothron | 477 | 88 | 6000 | 30 | 80 | 50000f | 0.2f | defaults |
| MothronEgg | 478 | 89 | 200 | 30 | 0 | 0f | 0.7f | npcSlots=0f;  |
| MothronSpawn | 479 | 90 | 700 | 14 | 50 | 0f | 0.3f | npcSlots=0.1f;  |
| Medusa | 480 | 3 | 400 | 20 | 30 | 1000f | 0.15f | defaults |
| GreekSkeleton | 481 | 3 | 70 | 10 | 22 | 300f | 0.4f | defaults |
| GraniteGolem | 482 | 3 | 110 | 18 | 30 | 500f | 0.35f | defaults |
| GraniteFlyer | 483 | 91 | 40 | 8 | 24 | 1000f | 0.6f | npcSlots=0.5f;  |
| TargetDummy | 488 | 92 | 1000 | 0 | 0 | 0f | 0f | defaults |
| BloodZombie | 489 | 3 | 75 | 8 | 20 | 150f | 0.4f | defaults |
| Drippler | 490 | 22 | 50 | 14 | 28 | 150f | 0.6f | defaults |
| PirateShip | 491 | 93 | 50 | 100 | 0 | 0f | 0f | defaults |
| PirateShipCannon | 492 | 75 | 2000 | 20 | 30 | 0f | 0f | defaults |
| LunarTowerStardust | 493 | 94 | 20000 | 20 | 0 | 0f | 0f | npcSlots=0f;  |
| LunarTowerNebula | 507 | 94 | 20000 | 20 | 0 | 0f | 0f | npcSlots=0f;  |
| GiantWalkingAntlion | 508 | 3 | 110 | 16 | 38 | 175f | 0.4f | defaults |
| GiantFlyingAntlion | 509 | 44 | 90 | 12 | 34 | 195f | 0.3f | defaults |
| DuneSplicerHead | 510 | 6 | 500 | 18 | 58 | 700f | 0f | npcSlots=5f;  |
| DuneSplicerBody | 511 | 6 | 500 | 28 | 54 | 700f | 0f | npcSlots=0f;  |
| DuneSplicerTail | 512 | 6 | 500 | 34 | 50 | 700f | 0f | npcSlots=0f;  |
| TombCrawlerHead | 513 | 6 | 60 | 0 | 18 | 130f | 0f | npcSlots=4f;  |
| TombCrawlerBody | 514 | 6 | 60 | 12 | 7 | 130f | 0f | npcSlots=0f;  |
| TombCrawlerTail | 515 | 6 | 60 | 14 | 7 | 130f | 0f | npcSlots=0f;  |
| SolarFlare | 516 | 9 | 1 | 0 | 100 | 0f (global default) | 0f | defaults |
| LunarTowerSolar | 517 | 94 | 20000 | 20 | 0 | 0f | 0f | npcSlots=0f;  |
| SolarSpearman | 518 | 3 | 1000 | 28 | 80 | 0f (global default) | 0.4f | defaults |
| SolarGoop | 519 | 99 | 1 | 0 | 120 | 0f (global default) | 0f | defaults |
| MartianWalker | 520 | 3 | 2000 | 40 | 60 | 0f (global default) | 0f | defaults |
| AncientCultistSquidhead | 521 | 86 | 2000 | 30 | 90 | 0f (global default) | 0f | defaults |
| AncientLight | 522 | 100 | 400 | 0 | 120 | 0f (global default) | 0f | defaults |
| AncientDoom | 523 | 101 | 500 | 0 | 30 | 0f (global default) | 0f | defaults |
| DesertScorpionWalk | 530 | 3 | 320 | 24 | 66 | 600f | 0.5f | defaults |
| DesertScorpionWall | 531 | 40 | 320 | 24 | 66 | 600f | 0.5f | defaults |
| DesertBeast | 532 | 3 | 270 | 34 | 65 | 800f | 0.3f | npcSlots=0.75f;  |
| DesertDjinn | 533 | 8 | 220 | 20 | 40 | 1200f | 0f | npcSlots=2f;  |
| DemonTaxCollector | 534 | 3 | 400 | 20 | 14 | 0f | 0.3f | defaults |
| SlimeSpiked | 535 | 1 | 50 | 5 | 14 | 40f | 1f (global default) | defaults |
| TheBride | 536 | 3 | 200 | 8 | 14 | 1000f | 0.5f | defaults |
| SandSlime | 537 | 1 | 50 | 5 | 15 | 75f | 0.7f | npcSlots=2f;  |
| SquirrelRed | 538 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| SquirrelGold | 539 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| PartyBunny | 540 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| SandElemental | 541 | 102 | 5000 | 30 | 40 | Item.buyPrice(0, 1, 50) | 0.05f | defaults |
| SandShark | 542 | 103 | 360 | 20 | 50 | 400f | 0.9f | defaults |
| SandsharkCorrupt | 543 | 103 | 380 | 24 | 60 | 400f | 0.8f | defaults |
| SandsharkCrimson | 544 | 103 | 400 | 22 | 64 | 400f | 0.8f | defaults |
| SandsharkHallow | 545 | 103 | 450 | 26 | 54 | 400f | 0.7f | defaults |
| Tumbleweed | 546 | 26 | 50 | 6 | 30 | 100f | 0.8f | defaults |
| DD2AttackerTest | 547 | 104 | 10 | 10 | 0 (global default) | 0f (global default) | 1f | defaults |
| DD2EterniaCrystal | 548 | 105 | 1000<br>Main.hardMode && downedMechBossAny => 3000<br>downedGolemBoss && Main.hardMode => 5000<br>Main.masterMode => 3<br>Main.masterMode && Main.expertMode => 2 | 14<br>Main.hardMode && downedMechBossAny => 18<br>downedGolemBoss && Main.hardMode => 20 | 0 (global default) | 0f (global default) | 0f | friendly=true; npcSlots=0f;  |
| DD2LanePortal | 549 | 106 | 5 | 20 | 0 | 0f | 0f | npcSlots=0f;  |
| DD2Bartender | 550 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| DD2Betsy | 551 | 110 | 50000 | 38 | 80 | 0f | 0f | npcSlots=0f;  |
| DD2GoblinT1 | 552 | 107 | 30 | 14 | 18 | 0f | 0.2f | npcSlots=0f;  |
| DD2GoblinT2 | 553 | 107 | 170 | 20 | 46 | 0f | 0.2f | npcSlots=0f;  |
| DD2GoblinT3 | 554 | 107 | 560 | 28 | 70 | 0f | 0.15f | npcSlots=0f;  |
| DD2GoblinBomberT1 | 555 | 107 | 50 | 16 | 26 | 0f | 0.2f | npcSlots=0f;  |
| DD2GoblinBomberT2 | 556 | 107 | 200 | 26 | 55 | 0f | 0.2f | npcSlots=0f;  |
| DD2GoblinBomberT3 | 557 | 107 | 700 | 34 | 75 | 0f | 0.15f | npcSlots=0f;  |
| DD2WyvernT1 | 558 | 108 | 60 | 4 | 30 | 0f (global default) | 0.2f | npcSlots=0f;  |
| DD2WyvernT2 | 559 | 108 | 180 | 16 | 75 | 0f (global default) | 0.2f | npcSlots=0f;  |
| DD2WyvernT3 | 560 | 108 | 600 | 30 | 100 | 0f (global default) | 0.05f | npcSlots=0f;  |
| DD2JavelinstT1 | 561 | 107 | 60 | 18 | 30 | 0f | 0.1f | npcSlots=0f;  |
| DD2JavelinstT2 | 562 | 107 | 300 | 28 | 60 | 0f | 0.1f | npcSlots=0f;  |
| DD2JavelinstT3 | 563 | 107 | 1000 | 38 | 80 | 0f | 0.05f | npcSlots=0f;  |
| DD2DarkMageT1 | 564 | 109 | 800 | 18 | 40 | 0f | 0f | npcSlots=0f;  |
| DD2DarkMageT3 | 565 | 109 | 4000 | 38 | 90 | 0f | 0f | npcSlots=0f;  |
| DD2SkeletonT1 | 566 | 107 | 25 | 12 | 18 | 0f | 0.3f | npcSlots=0f;  |
| DD2SkeletonT3 | 567 | 107 | 480 | 22 | 70 | 0f | 0.2f | npcSlots=0f;  |
| DD2WitherBeastT2 | 568 | 107 | 500 | 30 | 50 | 0f | 0.15f | npcSlots=0f;  |
| DD2WitherBeastT3 | 569 | 107 | 1400 | 40 | 80 | 0f | 0.05f | npcSlots=0f;  |
| DD2DrakinT2 | 570 | 107 | 900 | 30 | 60 | 0f | 0f | npcSlots=0f;  |
| DD2DrakinT3 | 571 | 107 | 3000 | 40 | 90 | 0f | 0f | npcSlots=0f;  |
| DD2KoboldWalkerT2 | 572 | 107 | 260 | 26 | 60 | 0f | 0.2f | npcSlots=0f;  |
| DD2KoboldWalkerT3 | 573 | 107 | 800 | 32 | 80 | 0f | 0.1f | npcSlots=0f;  |
| DD2KoboldFlyerT2 | 574 | 108 | 170 | 16 | 50 | 0f (global default) | 0.4f | npcSlots=0f;  |
| DD2KoboldFlyerT3 | 575 | 108 | 580 | 32 | 80 | 0f (global default) | 0.25f | npcSlots=0f;  |
| DD2OgreT2 | 576 | 107 | 5000 | 34 | 70 | 0f | 0f | npcSlots=0f;  |
| DD2OgreT3 | 577 | 107 | 13000 | 40 | 90 | 0f | 0f | npcSlots=0f;  |
| DD2LightningBugT3 | 578 | 111 | 500 | 36 | 80 | 0f (global default) | 0.8f | npcSlots=0f;  |
| BartenderUnconscious | 579 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| WalkingAntlion | 580 | 3 | 80 | 10 | 25 | 80f | 0.5f | npcSlots=0.8f;  |
| FlyingAntlion | 581 | 44 | 60 | 8 | 29 | 90f | 0.5f | npcSlots=0.8f;  |
| LarvaeAntlion | 582 | 3 | 30 | 2 | 10 | 40f | 0.75f | npcSlots=0.4f;  |
| FairyCritterPink | 583 | 112 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| FairyCritterGreen | 584 | 112 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| FairyCritterBlue | 585 | 112 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| ZombieMerman | 586 | 3 | 400 | 20 | 40 | 1000f | 0f | defaults |
| EyeballFlyingFish | 587 | 44 | 300 | 18 | 35 | 1000f | 0f | defaults |
| Golfer | 588 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| GolferRescue | 589 | 0 | 250 | 15 | 10 | 0f (global default) | 0.5f | friendly=true;  |
| TorchZombie | 590 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| ArmedTorchZombie | 591 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| GoldGoldfish | 592 | 16 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| GoldGoldfishWalker | 593 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| WindyBalloon | 594 | 113 | 1 | 0 | 0 | 0f | 0.3f | defaults |
| Seagull | 602 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Seagull2 | 603 | 68 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| LadyBug | 604 | 115 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| GoldLadyBug | 605 | 115 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| Maggot | 606 | 66 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | friendly=true; npcSlots=0.1f;  |
| Pupfish | 607 | 16 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| Grebe | 608 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Grebe2 | 609 | 68 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Rat | 610 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| Owl | 611 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| WaterStrider | 612 | 116 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| GoldWaterStrider | 613 | 116 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| ExplosiveBunny | 614 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Dolphin | 615 | 16 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| Turtle | 616 | 7 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| TurtleJungle | 617 | 7 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| BloodNautilus | 618 | 117 | 7000 | 24 | 55 | Item.buyPrice(0, 2) | 0f | defaults |
| BloodSquid | 619 | 5 | 750 | 16 | 60 | 500f | 0f | defaults |
| GoblinShark | 620 | 107 | 5000 | 30 | 70 | Item.buyPrice(0, 0, 75) | 0f | npcSlots=0f;  |
| BloodEelHead | 621 | 6 | 6000 | 0 | 90 | Item.buyPrice(0, 0, 75) | 0f | npcSlots=5f;  |
| BloodEelBody | 622 | 6 | 6000 | 30 | 60 | 0f (global default) | 0f | npcSlots=0f;  |
| BloodEelTail | 623 | 6 | 6000 | 40 | 50 | 0f (global default) | 0f | npcSlots=0f;  |
| Gnome | 624 | 3 | 25 | 0 | 10 | 0f (global default) | 1f | defaults |
| SeaTurtle | 625 | 7 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| Seahorse | 626 | 118 | 5 | 0 | 0 | 0f (global default) | 1f | defaults |
| GoldSeahorse | 627 | 118 | 5 | 0 | 0 | 0f (global default) | 1f | defaults |
| Dandelion | 628 | 119 | 50 | 0 | 15 | 50f | 0f | defaults |
| IceMimic | 629 | 25 | 500<br>!Main.hardMode => 300 | 30<br>!Main.hardMode => 12 | 80<br>!Main.hardMode => 30 | 100000f<br>!Main.hardMode => Item.buyPrice(0, 2) | 0.3f | defaults |
| BloodMummy | 630 | 3 | 180 | 18 | 60 | 700f | 0.5f | defaults |
| RockGolem | 631 | 3 | 1000 | 35 | 85 | 25000f | 0.1f | defaults |
| MaggotZombie | 632 | 3 | 45 | 6 | 14 | 60f | 0.5f | defaults |
| BestiaryGirl | 633 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| SporeBat | 634 | 14 | 16 | 2 | 13 | 90f | 0.8f | npcSlots=0.5f;  |
| SporeSkeleton | 635 | 3 | 60 | 8 | 20 | 100f | 0.5f | defaults |
| HallowBoss | 636 | 120 | 70000 | 50 | 80 | 250000f | 0f | boss=true; npcSlots=16f;  |
| TownCat | 637 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownDog | 638 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| HellButterfly | 653 | 65 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| Lavafly | 654 | 64 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.2f;  |
| MagmaSnail | 655 | 67 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.5f;  |
| TownBunny | 656 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| QueenSlimeBoss | 657 | 121 | 18000 | 26 | 60 | 60000f | 0f | boss=true; npcSlots=5f;  |
| QueenSlimeMinionBlue | 658 | 1 | 150 | 35 | 40 | 0f (global default) | 1f (global default) | defaults |
| QueenSlimeMinionPink | 659 | 1 | 150 | 35 | 40 | 0f (global default) | 1f (global default) | defaults |
| QueenSlimeMinionPurple | 660 | 14 | 120 | 30 | 50 | 0f (global default) | 0.8f | defaults |
| EmpressButterfly | 661 | 65 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| PirateGhost | 662 | 122 | 500 | 22 | 75 | 0f | 0.2f | defaults |
| Princess | 663 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TorchGod | 664 | — | 20 | — | 0 (global default) | 0f (global default) | 1f (global default) | boss=true;  |
| ChaosBallTim | 665 | 9 | 1 | 0 | 20 | 0f (global default) | 0f | defaults |
| VileSpitEaterOfWorlds | 666 | 9 | 1 | 0 | 65 | 0f (global default) | 0f | defaults |
| GoldenSlime | 667 | 1 | 300 | 5 | 5 | Item.buyPrice(0, 15) | 1.4f | defaults |
| Deerclops | 668 | 123 | 7000 | 10 | 20 | Item.buyPrice(0, 5) | 0f | boss=true; npcSlots=10f;  |
| Stinkbug | 669 | 115 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.25f;  |
| TownSlimeBlue | 670 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| ScarletMacaw | 671 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | defaults |
| BlueMacaw | 672 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | defaults |
| Toucan | 673 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | defaults |
| YellowCockatiel | 674 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | defaults |
| GrayCockatiel | 675 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | defaults |
| ShimmerSlime | 676 | 1 | 80 | 5 | 20 | Item.buyPrice(0, 0, 1, 50) | 1f (global default) | defaults |
| Shimmerfly | 677 | 64 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | npcSlots=0.2f;  |
| TownSlimeGreen | 678 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimeOld | 679 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimePurple | 680 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimeRainbow | 681 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimeRed | 682 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimeYellow | 683 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| TownSlimeCopper | 684 | 7 | 250 | 15 | 10 | 0f (global default) | 0.5f | townNPC=true; friendly=true;  |
| BoundTownSlimeOld | 685 | 1 | 250 | 10 | 0 | 0f | 0.3f | friendly=true;  |
| BoundTownSlimePurple | 686 | 125 | 1 | 0 | 0 | 0f | 0.3f | defaults |
| BoundTownSlimeYellow | 687 | 7 | 5 | 0 | 0 | 0f (global default) | 1f (global default) | defaults |
| Pufferfish | 688 | 16 | 5 | 0 | 0 | 0f (global default) | 0.5f | defaults |
| OwlMimic | 689 | 24 | 5 | 0 | 0 | 0f (global default) | 0.8f | npcSlots=0.4f;  |
| StatueMimic | 690 | 126 | 400 | 10 | 40 | 0f (global default) | 0f | npcSlots=0f;  |
| MossZombie | 691 | 3 | 60 | 10 | 18 | 80f | 0.45f | defaults |
| Orca | 692 | 16 | 400 | 20 | 50 | 10000f | 0.7f | defaults |
| LibrarianSkeleton | 693 | 8 | 70 | 2 | 18 | 160f | 0.6f | npcSlots=2f;  |
| WaterBoltMimic | 694 | 10 | 60 | 4 | 20 | 150f | 0.2f | defaults |
| PalworldCattivaDistressed | 695 | 127 | 250 | 15 | 0 | 0f (global default) | 0.5f | friendly=true;  |
| PalworldFoxsparksDistressed | 696 | 127 | 250 | 15 | 0 | 0f (global default) | 0.5f | friendly=true;  |
## 3. AI dispatch and controller state arrays

`NPC.AI()` dispatches by `aiStyle`. In this build, **style 1 is `AI_001_Slimes`; fighters are style 3 (`AI_003_Fighters`)**, not style 1. Style 2 dispatches `AI_002_FloatingEye`; style 6 dispatches `AI_006_Worms`; styles 8 and 22 are large type-specialized inline branches in `NPC.AI`.

| Style / controller | State variables and observed transition structure |
|---|---|
| `1` — Slimes (label correction) | Specialized slime variants; `ai[1]` also gates one-time contained-item generation. The item-content branch checks `NPCID.Sets.SlimeCanContainItems[type]`, `ai[1]==0`, non-client net mode, and positive `value`; it sets sentinel `ai[1]=-1` before rolls. World/secret-seed/slime-rain gates change roll denominators and generation paths. This is not fighter AI. |
| `2` — Floating eye | Set `noGravity=true`. Unless `noTileCollide`, `collideX/Y` reflect corresponding `oldVelocity` component at `-0.5×` and apply minimum escape speeds. Select/retain closest target; type `170/171/180` use `ai[0]` timer and `ai[1]` phase: lost sight increments timer, `>=300` enters phase 1, changes alpha to 200 and enables no-tile-collision; reacquired sight in open space resets. Horizontal steering uses `0.08` acceleration, correction `0.2`/`0.04`, cap `±4`; vertical steering uses `0.1`, correction `0.15`/`0.05`, cap `±2.5`. Other types override speeds/behavior. |
| `3` — Fighters (`AI_003_Fighters`) | Target player and update horizontal/vertical facing; direct steering and ground/tile probes decide approach, jump, obstacle handling, attack, and retreat. NPC-type-specific branches override movement and attacks, so there is no single fighter velocity equation. Gnomes (type `624`) have a special grounded transformation/placement path. |
| `6` — Worms | `ai[]` and `realLife` represent root/segment links and segment ordinal/count. A head initializes its root reference, creates configured body/tail NPCs with `NewNPC`, writes root into each child’s `ai[3]` and `realLife`, links adjacent indices via `ai[0]`/`ai[1]`, and stores the segment index/order. Updates validate the referenced neighbor/root, couple body motion to the chain, and handle orphan/invalid links by transforming/removing appropriate segments. Particular worm types have separate initialization/count rules. |
| `8` — Casters | Target closest player; horizontal velocity damping `velocity.X *= 0.93`, then snap `|velocity.X|<0.1` to zero. `ai[0]` initializes to `500` and advances as a timer; type branches select distinct firing thresholds and projectile patterns. Teleport flag requires `ai[2] != 0 && ai[3] != 0`: interpret `(ai[2],ai[3])` as tile coordinates, set pixel position to `(16*x - width/2 + 8, 16*y - height)`, zero velocity, clear AI flags, and emit type-specific effects. Firing is gated by type-specific timer plus global firing-distance/line-of-sight rules; individual caster types alter timers and projectile output. |
| `22` — Hovering | Event-gated variants (e.g. type `330` requires Pumpkin Moon, `253` Eclipse, `490` night). A hit resets `ai[2]=0`. `ai[0:2]` hold an idle/hover target position; X/Y alignment windows are `±16` pixels and `±40` pixels. While aligned, `ai[2]` increments; at `>=60`, it becomes `-200`, direction and horizontal velocity flip, collision X resets, and a network update is requested. Misalignment stores current position in `ai[0:2]` and resets timer. Other branches increment the hover timer by `+2` (type 253), `+0.1` (330), or `+1`; direction faces the player. Ranged subtypes add separate `ai[3]` and `localAI` cooldowns plus firing-distance/`Collision.CanHit` gates. |

No generic A* pathfinder is used by these selected controllers; common movement is target-relative steering, collision response, tile probes, and per-type state transitions.

## 4. Bestiary entry schema and population order

`BestiaryDatabaseNPCsPopulator.Populate(database)` applies, in order: automated enemy/critter entries; 40 manually registered TownNPC entries; automated NPC-biome relationships; manual NPC-biome relationships; automated biome decoration tags; special entry modifications; filters; sorting steps. Automated entries iterate `ContentSamples.NpcsByNetId`, skip IDs whose `NPCID.Sets.NPCBestiaryDrawOffset[id].Hide` is true and NPCs with `isLikeATownNPC`; `CountsAsACritter` selects Critter versus Enemy.

`BestiaryEntry` structural fields are `Icon`, `UIInfoProvider`, and ordered `Info: List<IBestiaryInfoElement>`. Enemy construction adds net ID, localized name plate, portrait rarity, kill counter, and stats report; optionally adds rare-spawn and boss elements; adds localized flavor text only when the localization key exists. Bosses and `ShouldBeCountedAsBossForBestiary[type]` get the boss element and `CommonEnemyUICollectionInfoProvider(..., quickUnlock:true)`. TownNPC and Critter constructors omit the stats report and use their respective collection-unlock providers. Manual TownNPC net-ID vector, in registration order: `[22,17,18,19,20,37,54,38,107,108,124,142,160,178,207,208,209,227,228,229,353,369,441,550,588,368,453,633,663,638,637,656,670,678,679,680,681,682,683,684]`. A separate manual multi-entry example registers Mimic net ID `85` with a custom unlock icon.

Sort-step order is `ByUnlockState`, `ByBestiarySortingId`, `Alphabetical`, `ByNetId`, `ByAttack`, `ByDefense`, `ByCoins`, `ByHP`, `ByBestiaryRarity`. Filters are unlock state, boss, rare creature, then one `ByInfoElement` for each common filter tag. Bestiary unlock tracking, portrait/background metadata, tags, and presentation are separate from combat defaults and loot resolution.
## 5. Spawn, drop, bestiary, and happiness data ownership

- Natural spawn path is separate from per-NPC defaults: `NPC.Spawner.SpawnNPC -> TrySpawnAnNPC -> GetSpawnRate/GetSpawnArea/FindSpawnTile -> off-screen validation -> regional flags -> SpawnAnNPC`. Observed search samples at most `50` tiles; ordinary candidate/safe extents are about `0.70×`/`0.52×` screen tile dimensions around a player, with zoom/scope and multi-player checks. Rate/cap modifiers depend on depth, time, biome, town density, weather, progression, and event flags. These are documented in `Phase3_NPC_AI.md`.
- Drop tables are separate from `NPC.SetDefaults`. `Phase18_NPC_Drops_Happiness_Registries.md` enumerates the 425 direct NPC rule registrations, 186 multi-NPC registrations, 19 global registrations, and captured condition/success/failure graph statements from `ItemDropDatabase`; `NPC.NPCLoot()` also applies legacy type/world-gated Skyblock helpers, money, and heal drops. These are source-declared registrations, not a fully resolved runtime `NPCID -> item[]` result.
- Bestiary presentation is a separate `Terraria.GameContent.Bestiary` registration/entry model; it does not own combat `lifeMax`, defense, or strike math.
- Happiness/biome price modifiers are computed by the shop/personality subsystem rather than stored in `NPC.SetDefaults`. `Phase17_GameContent_Environment_Biomes.md` records the price curves and `Phase18_NPC_Drops_Happiness_Registries.md` records the observed 25-entry NPC-to-biome preference matrix and affection priorities.
- The matrix above retains direct base assignments only. Do not treat it as a fully resolved drop table, bestiary text, or final world-mode difficulty-scaled combat stats.
