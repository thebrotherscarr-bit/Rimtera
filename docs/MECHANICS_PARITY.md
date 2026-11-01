# Base mechanics parity: Terraria behaviour vs Rimtera

**Goal:** every base mechanic behaves the way Terraria's does: the same rules, the same cause and effect, the same feel.

**Constraint:** numbers stay Rimtera's own, tuned to Rimtera's scale. Terraria is behavioural evidence only, so no copied code, constants or tables.

> **Changed since this contract was written.** User decision 1 (`docs/ROADMAP_0.2.0.md` §1.2) now asks base mechanics,
> controls, tools, gear and stats to work like Terraria's, including its numbers where they are known (movement, falls,
> air, the clock, respawn, healing tiers). Terraria is still evidence only: no code or assets are copied. See
> "0.2.0 status" at the end for what moved.

**Evidence:** REA member names on `Terraria.Player`, `Projectile`, `NPC`, `WorldGen`, `Main` (`docs/terraria-evidence/*.json`). Examples:
- movement: `JumpMovement`, `DashMovement`
- health and charge: `HurtLifeRegen`, `ApplyManaRegenerationDelay`
- mining and placing: `PickTile_DetermineDamage`, `HasEnoughPickPowerToHurtTile`, `ClearMiningCacheAt`, `PlaceThing_Tiles_BlockPlacementIfOverPlayers`, `IsInTileInteractionRange`
- items and death: `GrabItems`, `GetItemGrabRange`, `DropCoins`, `DropTombstone`
- respawn: `GetRespawnTime`, `AnyBossHindersRespawnTime`
- item use: `ItemCheck_AutoReuseLogic`, `ItemCheck_UseBuckets`

The behaviours listed below are the parity contract.

**Method:** each area gets a deterministic probe self-test per behaviour (`Systems/Parity*Tests.cs`). Every behaviour is marked OK, FIXED or DIFF (an intentional difference, with its reason) in the table at the end.

## A. Movement and physics
1. Running accelerates up to a maximum speed; releasing input decelerates faster than accelerating; reversing brakes before turning.
2. Surfaces change it:
   - ice (frozen soil, glacial ice) lowers friction, so you slide
   - mud, honey (nectar) and webs slow you
   - running speed bonuses multiply
3. Jumping:
   - a jump gives an initial lift and keeps lifting while jump is held, up to a time cap
   - releasing early gives a shorter jump
   - no jumping in mid-air without a double jump or wings
   - a jump buffered on the landing frame still fires
4. Gravity accelerates to a terminal fall speed:
   - fall damage starts beyond a safe height and grows with the distance
   - featherfall and fall-immunity gear negate it
   - landing in liquid negates it
5. Running into a 1-tile step climbs it automatically:
   - slopes are walked smoothly
   - half blocks act as half steps
   - ceilings stop jumps without sticking
6. Platforms:
   - you stand on them
   - you jump up through them from below
   - holding down drops you through
7. Ropes, vines, ladders and chains:
   - up and down climb them
   - jumping leaves them
   - you can hang on them without falling
8. Water:
   - slows movement
   - jump held = swim upward
   - breath drains only with your head submerged
   - lava burns and slows
   - leaving liquid gives a small exit boost so you can climb out
9. Gear:
   - double jumps, rocket boots and wings work with their own fuel/time and refill on landing
   - dash needs a double tap or the dash key, then has a cooldown
   - a grappling hook pulls toward its anchor and lets you jump off
10. Hits:
   - a hit knocks you back away from the source
   - immunity frames block repeat hits for a short window and show as flashing
11. You can't leave the world's edges.

## B. Mining, building and tiles
1. Pick power:
   - every block has a pick-power requirement
   - a tool below it does no damage and posts "needs a stronger drill"
2. Mining damage:
   - hits accumulate damage on the block
   - damage per hit comes from tool power against block hardness
   - partial damage heals if the block isn't hit again for a few seconds
   - cracks show the progress
3. Tool use:
   - tools swing at their use speed and repeat while held (auto-swing)
   - reach is a fixed tile range from the player, plus bonuses
   - out of range nothing happens
4. Broken blocks drop as ground items: they pop out with a little velocity and are collected by walking near (magnet range), not added straight to the inventory.
5. Hammers and axes:
   - hammers remove background walls (only walls with an exposed side)
   - hammers cycle the slope and half-block shape of blocks
   - axes chop trees: the whole tree above falls and drops timber plus saplings
   - pickaxes can't cut trunks
6. Placing blocks:
   - needs an adjacent block or wall
   - can't overlap the player or creatures
   - placing onto an existing block swaps it when the tool is a block of a different type
7. Placing walls: they need an adjacent wall or block.
8. Furniture:
   - needs solid support of its full width underneath (or a wall, for wall-mounted items)
   - doors need 3 free cells
   - removing the support breaks the furniture and drops it as an item
9. Containers:
   - can't be mined while they hold items
   - opening one shows its contents
   - range-checked while open, closing when you walk away
10. Buckets:
   - an empty bucket picks up a full liquid cell
   - a filled bucket pours one cell
   - liquids then flow and settle
11. Loose blocks: sand and gravel fall when unsupported and land as blocks; falling sand hurts creatures it lands on.
12. Torches:
   - can be placed on walls or attached to blocks
   - give light
   - are knocked out by liquid

## C. Combat, health and spawning
1. Damage:
   - weapon damage varies a little (± a few percent) per hit
   - critical hits double damage
   - crit chance = weapon + class bonuses
2. Knockback:
   - weapon knockback pushes enemies away, reduced by their knockback resistance
   - bosses resist most or all of it
3. Enemy immunity:
   - each enemy has its own short immunity per hit source
   - one melee swing hits a given enemy once
   - piercing projectiles hit each enemy once
4. Defense (done): reduces hits by a fraction of defense, minimum 1. Damage over time ignores defense.
5. Life regeneration:
   - grows with time since you were last hurt
   - is faster while standing still
   - stops at once when you take damage
   - regen bonuses add on top
6. Charge (mana) regeneration:
   - waits a short delay after spending charge, then ramps up
   - is faster when not moving
   - charge potions cause charge sickness (reduced damage) for a while
7. Healing:
   - quick heal uses the smallest potion that heals enough
   - potion sickness blocks healing potions (done)
8. Death:
   - drops a share of carried credits where you died (Classic), or all of them (harder difficulties)
   - places a tombstone
   - respawn takes longer if a boss is alive (done)
9. Enemy spawning:
   - spawns happen just off-screen, never within a safe radius around the player
   - there is a spawn cap, and a spawn rate
   - both rise at night, underground, during events and in blight
   - both fall near town NPCs (a populated town)
   - creatures far off-screen despawn, except bosses and event units
10. Spawn locations:
   - surface spawns need open ground of the right biome
   - cave spawns need dark open space
   - flyers need air
   - water creatures need liquid
11. Contact damage: touching an enemy hurts you (then immunity frames). Passive critters never hurt.
12. Bosses:
   - a boss leaves if every player is dead
   - night bosses leave at dawn
   - while a boss is alive, regular spawns are reduced

## D. World systems, NPCs and inventory
1. Day and night:
   - a full cycle with day longer than night
   - the moon phase advances each night
   - a blood-moon-like event has a small chance at nightfall
   - sleeping speeds time
2. Town NPCs move in:
   - each needs its own unlock condition
   - and a free valid house: enclosed, within size limits, with background walls, a light, a table-like and a chair-like item, and a door or platform entrance
   - houses are checked periodically during the day; one NPC moves in at a time
   - a dead town NPC respawns after a delay if its house is still valid
3. Housing check (H):
   - reports why a room fails
   - an NPC claims a room, and two NPCs can't share one
4. Happiness: NPC happiness (biome, neighbours, crowding) changes shop prices (done; verify).
5. Inventory:
   - picked-up items stack onto existing stacks first, then fill the hotbar, then the main inventory
   - stacks have per-kind maximums
   - credits combine automatically
   - favourites aren't sorted or deposited (done)
6. Crafting:
   - a recipe is available only while you have the materials and are within range of every station it needs
   - crafting takes from inventory and chests in range? No: Rimtera uses inventory only. This is intentional; mark DIFF unless trivial.
   - output goes to the cursor or stacks into the inventory
7. Events:
   - invasions start on their trigger conditions
   - progress bars count kills
   - events end when their waves or timer finish
   - you can't sleep through an event
8. Weather:
   - rain starts and stops randomly
   - storms and wind affect it
   - rain reduces surface lighting
9. Fishing:
   - bait is consumed with a chance based on bait power
   - fishing power = rod + bait + bonuses
   - the catch depends on biome, depth and liquid
   - a daily quest fish
10. Saving: autosave on a timer and on exit; the world and the character are saved separately (done).

## Results
Each audit fills in its table: behaviour, status (OK / FIXED / DIFF), test name, notes. The tables are in
`docs/parity/A_movement.md`, `B_building.md`, `C_combat.md` and `D_world.md`. They record the first audit and are not
rewritten; the section below says what has changed since.

## 0.2.0 status (2026-11-01)

Every behaviour in sections A to D still holds unless listed here. "Superseded" means the behaviour holds but with new
rules or numbers.

| Item | 0.2.0 status | What changed |
|---|---|---|
| A1 Running | ✅ superseded (0.1.14) | Terraria's numbers: top run about 11 tiles/s, reached in about half a second and lost in a quarter |
| A3 Jumping | ✅ superseded (0.1.14) | A held jump is about 6.5 tiles high, a tap about 2 |
| A4 Gravity and falls | ✅ superseded (0.1.12, 0.1.14) | Gravity 90 tiles/s² to a 37.5 tiles/s fall. Fall damage is 10 per tile beyond 25, from the highest point. Each mount has its own fall rule (the hover-bike, rotor skiff and spring hopper take none, the others half) |
| A8 Water and breath | ✅ superseded (0.1.12, 0.1.14) | About 23 s of air, drowning at about 17 HP/s, refilled within a second; the rebreather gives six times the air. Swimming needs fins (the Diver's kit); without them the player only paddles up at 2 tiles/s |
| A9 Dash | DIFF kept | Every player still has a dash key; Terraria needs dash gear |
| C1 Damage spread and crits | ✅ superseded | ±15% spread and a 4% base crit since the gear mechanics pass (`docs/GEAR_DIFF_MECHANICS.md`); since 0.1.15, hits on the player vary ±15% too |
| C4 Defense | ✅ extended (0.1.15) | Creatures now have tiered defense too (2-6 early, up to 30-50 late), explicit immunities and lava damage |
| C7 Healing | ✅ extended (0.1.15) | A four-tier healing and charge ladder, a restoration stim, 60 s potion sickness, boss potions and hearts, heart and charge-orb pickups |
| C8 Death | ✅ superseded (0.1.8, 0.1.11, 0.1.12) | Drops by character difficulty: Easy part of the money, Medium money and items, Hard everything but tools, Ironman the impossible run (the character dies for good). Terraria's respawn timer: 10 s, 15 s on Brutal and Annihilation, 3 s for Explorer, no boss extension. The bed sets the spawn point. The old "Classic halves material stacks" DIFF is gone |
| C9 Spawning | ◐ 0.3.4 | Terraria's base rate (1 in 600 ticks), cap (5), night, underground and hardmode factors, the ramp-down, and battle and calming stims are done. Terraria's spawn area above and below the player, and the placeable candles, are not |
| C10 Spawn locations | ◐ unchanged | Event creatures still spawn anywhere, not only on the surface or near their front (EV-3 → 0.4.0) |
| C11 Contact damage | ✅ superseded (0.1.15) | Contact uses box overlap; immune players are passed by projectiles |
| C12 Bosses leave | ◐ unchanged | Per-boss leave rules (dawn, target lost, biome; BO-6) were planned for parity step 0.1.7 and are not done |
| D1 Day and night | ✅ superseded (0.1.11) | Terraria's 24-minute day: 15 minutes of day and 9 of night. Buffs, regeneration and needs run on real time; sleeping speeds the clock without draining buffs |
| D2 Dead town NPCs respawn | ✗ replaced (0.1.8) | People die for good on every setting with stakes. A storyteller arrival stream brings new people into free houses and fills the empty roles. On Explorer and Creative, slain residents still come back |
| D5 Inventory | ✅ extended (0.1.10, 0.1.15) | Four coin and four ammo slots beside the bag; credits and ammunition go there first. Stacks of 9999 |
| D7 Events end | ✅ superseded (0.1.11) | Moons end at dawn and day events at dusk. Outlasting an event is not clearing it |
| D8 Weather | ◐ unchanged | Weather is rolled from the zone the player stands in, not from the world's climate (EV-9 → 0.4.0) |
| D10 Saving | ✅ superseded (0.1.13) | Autosave every 2, 5, 10 (default) or 15 minutes, or off, and on exit |

