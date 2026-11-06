# Rimtera playbook (read by the local playtest agent)

Facts here come from the game's source and docs. The agent loads this file at the start of every play session
(`tools/local-ai/play-loop.mjs`), so keep it short and correct. Add a line when the agent gets stuck on something.

## Your kit and the three tool kinds
- Start: slot 0 Ferrite stiletto (weapon), slot 1 Copper pickaxe (a "drill"), slot 2 Ferrite hatchet (a "cutter"), slot 3 12 berries.
- **Drill (pickaxe)**: select it, then `action: mine` on a block (dirt, stone, ore, sand). `mine` does nothing with any other item
  selected, and the game says "Select a drill to mine."
- **Hatchet (cutter)**: select it, then `action: useselected` on a tree trunk, cactus or log. It fells trees (gives timber).
  `mine` does NOT work for trees.
- **A tree takes several swings.** Keep swinging at the same trunk (45-60 frames a swing) until timber appears in your
  inventory; a swing that lands says nothing new. If the message says "Cutters only fell trees, cactus and logs." you are
  aiming at the wrong tile: pick a **TreeTrunk** tile beside you, never the tile you are standing in, and never a
  TreeCanopy.
- **Placing**: select a block, torch or furniture item, then `action: useselected` on an empty tile within reach.
- **Weapon**: select it, then `action: useselected` toward a creature.
- Select a slot first with `{"do":"select","slot":N}`. The "selected slot" line shows what is in hand.
- Targets must be close to you (a few tiles). If the message says "out of reach", walk closer first.

## Actions
`mine`, `useselected`, `eat` (eat food when hungry), `interact` (doors, containers, survivors, town NPCs),
`sleep` (in a bed at night, time runs faster), `replaceselected`, `none`. `move` -1 left / 1 right, `jump` to climb 1-tile steps,
`down` to drop through platforms. 60 frames = 1 second; the world only moves while you act.

## Reading the map
x grows right, y grows DOWN, so the ground is at larger y. The `NEAREST:` line lists the closest tile of each kind with
its tile coordinates. Use those coordinates as target_x/target_y. A tree has a TreeTrunk (cut this) and a TreeCanopy above it.

## Opening route (from the game's crafting notes)
1. Fell a tree with the hatchet: it gives timber.
2. Hand-craft: 10 timber (or 10 hull panels) makes the Nano-Fabricator, the first station. 1 timber makes 1 hull panel.
   With one tree you can also make the Field Workbench, platforms, a ladder, a door, a sign and plank furniture.
3. Dig with the drill (ferrite ore, basalt, sand, crystals) to unlock torches, the Ore Refinery, Power Forge, ferrite bars,
   then Kiln, Sawmill, Cooking Station, Chem Lab, Ammo Press, Tinker Bench.
4. Recipes need their station within about 6 tiles. "craftable here" in the state lists recipe ids you can craft right now:
   craft with `{"do":"craft","id":"<id>"}`. Place a station (select it, `useselected` on an empty tile) before crafting what needs it.
5. Eat when hungry (this game has hunger). Light torches before night and keep your health up.

## Good habits
- After an action, check the `message:` line and your inventory. If nothing changed after two tries, change something
  (different tool, different tile, walk) or write a bug note.
- Write a `progress` note for each milestone, and a `bug`/`ux` note for anything confusing (e.g. a message that doesn't match the item you hold).
