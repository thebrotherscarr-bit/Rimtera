# Recipe and Item — crafting architecture

## 1. Equations and data variables

| Field / relation | Available observation | Exactness / scope |
|---|---|---|
| `Recipe.createItem` | Result item field | Schema-level observation from `docs/RESEARCH.md` |
| `requiredItem[]` | Ingredient entries, with recipe-group alternatives | Numeric ingredient rows not available |
| `requiredTile[]` | Required crafting tile/station entries | Exact station-ID arrays not available |
| Environment requirements | Liquid kinds (water/lava/honey), biome tags (snow/graveyard), and other condition flags | Exact per-recipe matrix not available |
| `PlayerMeetsEnvironmentConditions` | Environment eligibility gate | Method body/formula not captured in available report |
| `CreateRequiredItemAndObjectArrays` | Requested method | Body unavailable: source corpus absent; not asserted as inspected |
| Station-distance equation | `NOT_CAPTURED` | No radius, coordinate metric, or tile-distance formula in available evidence |
| Guide-NPC unlock matrix | `NOT_CAPTURED` | No raw item/recipe unlock rows available |
| Rimtera lookup design (not Terraria data) | `recipesByIngredient[itemId] -> recipeId[]`; `recipesByStation[stationId] -> recipeId[]` | New project architecture, not a claim about original internals |
| Rimtera station range (not Terraria data) | `dx = playerTile.X - stationTile.X`; `dy = playerTile.Y - stationTile.Y`; eligible iff `dx*dx + dy*dy <= stationRadius*stationRadius` | Configurable Euclidean tile-radius proposal; replaceable without affecting recipe data |
| Rimtera craft predicate (not Terraria data) | `craftable = allIngredientGroupsSatisfied && allStationsNearby && allEnvironmentConditionsSatisfied` | Project predicate; no original recipe rows embedded |

## 2. Step-by-step state conditional logic array

```text
[OBSERVED 0] Recipe definitions carry a result item, ingredient entries/groups, required crafting tile, environment requirements, and condition flags.
[OBSERVED 1] Recipe setup and recipe-group methods construct lookup structures for available recipes.
[OBSERVED 2] PlayerMeetsEnvironmentConditions gates environment-sensitive recipes.
[UNKNOWN 3] CreateRequiredItemAndObjectArrays control flow and exact lookup key order were not present in available evidence.
[UNKNOWN 4] Exact station-distance verification, guide-NPC unlock rules, and per-recipe ingredient matrices were not captured.
[RIMTERA DESIGN 5] Query candidates from ingredient/station indexes; deduplicate recipe IDs.
[RIMTERA DESIGN 6] Reject a candidate if any ingredient-group quantity is unmet, any required station is outside its configured radius, or any environment predicate fails.
[RIMTERA DESIGN 7] Consume ingredients atomically only after all predicates pass; then grant the result stack.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / numeric data | Coverage |
|---|---|---|---|
| Recipe | result ID, ingredient entries/groups, required station, liquid/biome/condition flags | Per-recipe quantities not available | Structural fields only |
| Ingredient group | alternatives and required counts | Raw group IDs/counts unavailable | No catalog dump |
| Station check | player position, station tile IDs, radius | Original distance formula/radius unavailable | Rimtera Euclidean formula above is a new configurable design |
| Guide unlock | guide-NPC state and recipe/material relationships | Full matrix unavailable | Not inferred from names or strings |
| Input corpus | `C:\Users\novad\Desktop\Rimtera\docs\Decompiled_Terraria` | `NOT_FOUND` at generation time | Existing `docs\RESEARCH.md` only; no file-by-file parse |
