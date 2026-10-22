# Rimtera: Architectural Integration Plan
**Project Codename:** Rimtera  
**Target Platform:** Windows (.NET Core / C# Hybrid Engine)  
**Analysis Engine:** REA (Reverse Engineer Anything) via OpenCode MCP  

---

## 1. Core Vision & Engine Architecture
Rimtera is a hybrid application that integrates **RimWorld's high-fidelity system simulations** (Pawn AI, incident engines, component-based ticking) into **Terraria's real-time 2D sandbox environment** (tile manipulation, physics, fluid dynamics). 

The target execution uses a consolidated C# framework driven by static metadata extracted directly from the compiled game binaries using the local REA `.NET` toolchain.

---

## 2. Extraction & Reverse Engineering Strategy (via REA)
Because both source games rely entirely on the .NET framework, native decompilation through Ghidra is bypassed in favor of clean CIL (Common Intermediate Language) mapping.

```
                  ┌────────────────┐
                  │ Target Binaries│
                  └───────┬────────┘
                          │
            ┌─────────────┴─────────────┐
            ▼                           ▼
    [Terraria.exe]            [Assembly-CSharp.dll]
    (2D Tile Systems)          (RimWorld Core Simulation)
            │                           │
            └─────────────┬─────────────┘
                          │ npx rea-agents inspect_managed_members
                          ▼
            ┌───────────────────────────┐
            │   REA CIL Metadata Map    │
            └─────────────┬─────────────┘
                          │ OpenCode MCP Bridge
                          ▼
            ┌───────────────────────────┐
            │ OpenCode Local AI Engine  │
            └───────────────────────────┘
```

*   **Terraria Infrastructure:** Mapping the world generation loops, structural `Tile` multi-arrays, and lighting/fluid tick schedules inside `Terraria.exe`.
*   **RimWorld Simulation Matrix:** Cataloging entity behaviors (`Pawn` logic), work-givers, and the central incident management systems within `Assembly-CSharp.dll`.
*   **Unity/IL2CPP Boundary Handling:** Utilizing REA's static metadata decoder to extract structured schemas from any Unity instances utilizing `global-metadata.dat` (versions 29, 31, 39).

---

## 3. Subsystem Merging & Technical Boundaries

| Subsystem Matrix | Terraria Foundation | RimWorld Integration | Resolution / Bridge Layer |
| :--- | :--- | :--- | :--- |
| **World & Map Grid** | 2D tile arrays, rigid physics layers, block breaking. | Room definitions, temperature matrix, pathfinding cost maps. | Map tiles serve as physical frames injected with underlying `Room` data vectors. |
| **Entities / Pawns** | Physics-driven sprites, predictable path routines. | Mood variables, physical health capacities, AI behavior trees. | Entities retain Terraria physics hooks but derive behavior choices from RimWorld ticking logic. |
| **Simulation Ticker** | Frame-by-frame mechanical updates (60 Hz standard updates). | Long-term intervals (Ticks, Hours, Days) and threat scales. | Dual-rate tracking engine. Physics loop processes at high frequency; story tickers queue in background frames. |

---

## 4. Phase-by-Phase Execution Roadmap

### Phase 1: Environment Baseline & Isolation
*   Validate port alignment between the active OpenCode shell session (`ses_ea90f8ec2ffekvRgnCKc3Iv40u`) and the background Ollama daemon.
*   Establish strict separation workflows inside OpenCode using the `plan` agent configuration (**`Tab`** toggled) to outline structural class modifications safely without executing premature file writes.
*   Execute standard REA commands (`inspect_managed_members`) to output raw data signatures into text payloads.

### Phase 2: System Boundary Mapping
*   **Task 2.1:** Identify cross-referencing nodes between Terraria’s fluid tracking loops and RimWorld’s environmental metrics.
*   **Task 2.2:** Isolate RimWorld's Incident Worker pipelines to run standalone inside our target project.
*   **Task 2.3:** Reconstruct coordinate tracking layers to safely convert 2D float vectors (Terraria positions) into simulation cells (RimWorld regions).

### Phase 3: Consolidation & Testing
*   Compile extracted systems into individual modular C# classes within the `Rimtera` assembly.
*   Inject shell hooks (`!`\`cmd\` scripts) inside OpenCode to continuously monitor local builds and lint structural errors.
*   Run simulation validation test scripts to verify code execution parity.

---
*Document produced automatically via OpenCode Workspace Optimization Session.*
