# Phase 2 — Terraria.ID / Item / placement

## 1. Equations and data variables

| Variable / relation | Observed value or rule | Units / scope |
|---|---|---|
| Item fields | `damage`, `knockBack`, `useTime`, `useAnimation`, `useStyle`, `shoot`, `shootSpeed`, `useAmmo`, `ammo`; legacy flags `melee`, `magic`, `ranged`, `summon`, `sentry` | per-item fields; no complete item table reproduced |
| Projectile creation | `Projectile.NewProjectile(source, position, velocity, projectileID, damage, knockBack, owner)` | common call shape; ammo/modifier/special paths vary |
| Mining accumulator | `hitTileDamage[n+1] = hitTileDamage[n] + GetPickaxeDamage(pickPower, tileState, worldState, ...)` | per hit; damage function is tile-/state-specific |
| Break gate | `hitTileDamage >= 100` | successful break branch; below threshold is failed-hit/effect path |
| Pick-power gates observed | `{50, 55, 65, 100, 110, 150, 200, 210}` | branch thresholds; not a single hardness multiplier |
| Item drop flow | `KillTile_GetItemDrops(tileState, worldState) -> (itemID, stack) -> Item.NewItem(...)` | conditional on tile rules, `noItem`, `stopDrops`, net mode, and special objects |
| Recipe inputs | `createItem`, ingredient entries/groups, crafting tile, liquid and biome conditions | environment gate via `PlayerMeetsEnvironmentConditions` |
| Placement replacement | replacement hit accumulator uses threshold `100` before `WorldGen.ReplaceTile` | allow/deny rules and object-specific checks apply |

## 2. Step-by-step state conditional logic array

```text
[0] Read held Item defaults and use state; verify reach, timing, use-style, and interaction gates.
[1] If ammo-bearing: select ammo; combine item/projectile defaults with ammo, player modifiers, and special-weapon branches.
[2] If firing branch valid: create projectile with source, position, velocity, projectile ID, damage, knockback, and owner.
[3] Mining entry is Player.PickTile (not PickAmmo); obtain hit buffer and tile-specific pick damage.
[4] If accumulated hit damage < 100: call failed-hit tile path; preserve tile and process applicable effects.
[5] If accumulated hit damage >= 100: enter successful break path; enforce breakability, special-object, and world-state checks.
[6] If ordinary drop path remains enabled: resolve tile-specific item IDs/stacks; instantiate item entities; clear tile state.
[7] Tile placement: check item tile ID, reach, cursor/interaction, timing, replacement eligibility; route multi-cell objects through TileObject.CanPlace/Place, otherwise legacy placement.
[8] After placement: frame affected cells and run post-placement/network hooks.
[9] Tile replacement: select pickaxe; accumulate replacement damage; require threshold and valid allow/deny, solidity, liquid, rope, frame-important, style, and drop checks.
[10] Wall placement: require reach/use timing, cardinal adjacent tile-or-wall support, and no duplicate; wall replacement adds friendly-wall gate.
[11] Recipe availability: resolve ingredients/groups, crafting station, liquid/biome conditions, and other recipe flags.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / thresholds | Tick-rate / coverage |
|---|---|---|---|
| `Terraria.Item` | item/projectile IDs; damage, knockback, use timing/style, ammo, velocity | item- and branch-specific; no full numeric table generated | event-driven use pipeline |
| Projectile spawn | projectile ID, source, position, velocity, damage, knockback, owner | derived through item + ammo + player/special modifiers | spawned on use path |
| Pickaxe mining | pick power, tile ID/state, world/depth flags, hit buffer | selected pick-power gates listed above; break threshold `100` | accumulated per hit |
| Tile drops | tile ID/state, item IDs, stack counts, source | `noItem` / `stopDrops` and special rules gate results | on successful break |
| Recipe | result ID, ingredient/group IDs, station, liquid/biome flags | recipe-definition-specific | availability recalculated by environment/state |
| Placement | tile/object/wall IDs, footprint, anchors, support, replacement eligibility | explicit rule sets; no universal multiplier | on placement attempt |
| `Terraria.ID` catalog | numeric ID namespace inventory | not present in available evidence | full ID/item/block/projectile catalogue intentionally not emitted |
| Input corpus | `docs/Decompiled_Terraria` | unavailable at generation time | based only on bounded observations in `docs/RESEARCH.md` |
