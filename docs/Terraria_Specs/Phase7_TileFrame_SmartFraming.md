# TileFrame and WorldGen framing — neighborhood masks

## 1. Equations and data variables

| Field / relation | Available observation | Exactness / scope |
|---|---|---|
| `Terraria.TileFrame` / `WorldGen.TileFrame` source | No method body or frame table in `docs/RESEARCH.md`; decompiled source directory is absent | Exact Terraria bitmask and atlas mapping unavailable |
| Neighbor order, original bit values | `NOT_CAPTURED` | No claim that Rimtera bit assignments match Terraria |
| Original mask-to-atlas coordinate table | `NOT_CAPTURED` | Texture coordinates and tile-specific tables not available |
| Rimtera neighbor bits (new design) | `N=1`, `NE=2`, `E=4`, `SE=8`, `S=16`, `SW=32`, `W=64`, `NW=128` | Project-owned 8-neighbor layout |
| Rimtera cardinal connectivity (new design) | `connected(d) = sameFrameGroup(neighbor[d], center)` | Project-owned material/blend grouping |
| Rimtera diagonal suppression (new design) | `NE' = NE && N && E`; `SE' = SE && S && E`; `SW' = SW && S && W`; `NW' = NW && N && W` | Avoids diagonal-only corner joins |
| Rimtera effective mask (new design) | `mask = N + 2*NE' + 4*E + 8*SE' + 16*S + 32*SW' + 64*W + 128*NW'` | Bitwise OR of corresponding set bits is equivalent |
| Rimtera 16×16 atlas address (new design) | `atlasX = mask & 0x0F`; `atlasY = (mask >> 4) & 0x0F`; `sourceX = atlasX*frameWidth`; `sourceY = atlasY*frameHeight` | Simple project convention; atlas must author/populate matching cells |

## 2. Step-by-step state conditional logic array

```text
[OBSERVED 0] Placement/death operations report nearby cells for later framing in Rimtera; no original TileFrame behavior was captured in the available report.
[UNKNOWN 1] Original eight-neighbor bit order, diagonal rules, mask normalization, random/style variation, and frame-coordinate lookup remain unverified.
[RIMTERA DESIGN 2] Read center tile and its eight neighbors in N, NE, E, SE, S, SW, W, NW order.
[RIMTERA DESIGN 3] Set a connection bit only when the neighbor is compatible with the center's configured frame group.
[RIMTERA DESIGN 4] Keep a diagonal connection only when its two adjoining cardinal connections are also present.
[RIMTERA DESIGN 5] Compose the 8-bit mask using the project bit assignments in the data-variable table.
[RIMTERA DESIGN 6] Resolve the mask through a project-owned frame table or the documented 16x16 atlas address formula.
[RIMTERA DESIGN 7] Reframe only cells in the dirty region after placement, mining, liquid reaction, or tile replacement.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / numeric data | Coverage |
|---|---|---|---|
| Neighbor sample | center tile and 8 adjacent tile IDs/frame groups | Project bit values listed above | New Rimtera contract only |
| Diagonal rule | diagonal plus its two orthogonal neighbors | Both orthogonals required | New Rimtera contract only |
| Frame output | atlas cell X/Y and pixel source X/Y | `frameWidth`, `frameHeight` are asset-defined | 16×16 address scheme is a project proposal, not original data |
| Dirty region | changed cells and bounded neighbors | Existing Rimtera placement reports 3×3 regions | Does not encode original texture frames |
| Terraria TileFrame table | tile IDs, mask normalization, frame coordinates | `NOT_CAPTURED` | Cannot be reconstructed from absent source without fabricating data |
| Input corpus | `C:\Users\novad\Desktop\Rimtera\docs\Decompiled_Terraria` | `NOT_FOUND` at generation time | Existing `docs\RESEARCH.md` only |
