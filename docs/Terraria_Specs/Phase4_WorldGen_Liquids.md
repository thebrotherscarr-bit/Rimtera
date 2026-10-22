# Phase 4 — Terraria.WorldGen / liquid worklist

## 1. Equations and data variables

| Variable / relation | Observed value or rule | Units / scope |
|---|---|---|
| World pass order | `GenerateWorld -> WorldGenerator -> enabled GenPass[0..n]` | ordered stages; each stage has progress weight |
| Pass random stream | `seed_i = deterministicSeed(pass_i)` | independent deterministic stream per pass |
| Liquid amount | `liquidAmount ∈ [0,255]` | byte per tile, plus liquid type |
| Active/buffer limits | `maxLiquidBuffer = 50,000`; `maxLiquid = 25,000`; `cycles = 10` | static initializer values; runtime settings may change limits |
| Downflow bound | `destinationAmount' <= 255` | compatible, nonblocking destination; affected cells re-enqueued |
| Horizontal relaxation | `newLocalAmount ≈ round(mean(compatible neighboring byte amounts))` | observed local averaging/rounding; neighborhood spans adjacent cells to two/three tiles, exact expression not present in notes |
| Work budget | active-list slice processed over `cycles`; `wetCounter` partitions work | bounded worklist, not full-grid sweep per tick |
| Quick settle sweep | visit rows from `maxY` toward `minY`; call `SettleWaterAt` for non-empty cells | generation/settling route |

## 2. Step-by-step state conditional logic array

```text
[0] GenerateWorld initializes generation state and delegates ordered enabled passes to WorldGenerator.
[1] Run each GenPass in sequence; weight contributes to progress; honor cancellation/pause; use pass-specific deterministic random stream.
[2] AddWater: reject out-of-range, empty, already-checked, or blocked cells.
[3] If active list has capacity: mark checked, create active record, increment count; else enqueue cell coordinates in LiquidBuffer.
[4] UpdateLiquid: process an active-list slice across bounded cycles; wetCounter partitions work; quick-fall may bypass selected delay/skip behavior.
[5] For each active cell: read current and cardinal neighbors; first attempt compatible downward transfer into a nonblocking cell, bounded by amount 255.
[6] Schedule changed source/destination cells again; process remaining compatible liquid with local horizontal redistribution and rounded averaging.
[7] On changed values: write byte amounts and re-enqueue affected cells for later passes; do not solve one global equilibrium equation.
[8] Remove settled/inactive records after a cycle; admit buffered coordinates as capacity becomes available.
[9] Under prolonged buffer pressure: quick-settle/panic path may activate.
[10] Check lava/honey/shimmer interactions separately through LiquidCheck; incompatible-fluid reactions may transform tiles.
[11] Generation QuickWater: sweep from maxY toward minY and call SettleWaterAt on nonempty cells.
```

## 3. Component property matrix

| Component | IDs / properties | Multipliers / limits | Tick-rate / coverage |
|---|---|---|---|
| `WorldGenerator` | ordered enabled `GenPass` list, per-pass weight, cancellation/pause state | progress weighted by pass weights | once per generation pass; exact pass catalog not included |
| `Liquid` | active coordinates/records, byte amount, liquid type, delay/kill/skip/quick-settle state, network-change set | amount max `255`; default max active `25,000`; cycles `10` | bounded active-list update |
| `LiquidBuffer` | queued coordinates and counters | default capacity `50,000` | drained as active-list slots free |
| Downflow | source/destination amount, compatibility, blocking flags | destination saturates at `255` | active cell update |
| Horizontal relaxation | same-type neighborhood, local volume, rounding, enqueue flags | local neighborhood extends about 2–3 tiles | repeated worklist passes |
| Liquid reactions | water/lava/honey/shimmer checks, `LiquidCheck` | tile/type-specific; no full reaction table emitted | conditional update |
| WorldGen liquid pass | `maxY`, `minY`, nonempty cells, `SettleWaterAt` | row-wise sweep | generation pass |
| Input corpus | `docs/Decompiled_Terraria` | unavailable at generation time | based only on bounded observations in `docs/RESEARCH.md`; not a complete WorldGen or liquid dump |
