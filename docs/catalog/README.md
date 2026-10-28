# Content catalogue: Terraria → Rimtera

An item-by-item map of Terraria's content to Rimtera's equivalents. It feeds the 0.2.0 → 0.2.3 plan. Each map is a `.csv` (one row per Terraria entry) plus a `.md` (coverage numbers, upgrade and crafting loops, and numbered gap items).

Status as of 2026-10-28:

| Catalogue | Files | Status | Gap IDs |
|---|---|---|---|
| Armor and vanity | `armor.csv`, `armor.md` | ✅ done | CA-1 … CA-12 |
| Accessories | `accessories.csv`, `accessories.md` | ✅ done | CX-1 … CX-16 |
| Stations, recipe graph, materials, tools, upgrades | `stations.csv`, `materials_tools_upgrades.csv` | ◐ CSVs generated, `recipe_graph.md` not written | CR-* (pending) |
| NPCs (town, critters, enemies, events, bosses) | `npcs.csv` | ◐ CSV generated, `npcs.md` not written | CN-* (pending) |
| Placeables and building | `placeables.csv` | ◐ CSV generated, `placeables.md` not written | CP-* (pending) |
| Weapons | — | ◐ data gathered, nothing generated yet | CW-* (pending) |
| Consumables, ammo, buffs | — | ◐ builder script written, not run | CC-* (pending) |

Every row was meant to be checked by the agent that wrote it, but the ◐ CSVs were written in a final pass just before their agents stopped at the usage limit. Treat them as drafts until their `.md` is written.

The working scripts and extracted inputs live in `docs/terraria-evidence/catalog-wip/`. That folder is gitignored because it contains Terraria-derived data; it stays on this machine and never ships. The workflow for finishing all of this is in `docs/HANDOFF_WORKFLOW.md`.
