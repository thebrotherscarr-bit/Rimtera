# 09: Presentation and engine (rendering, assets, audio, performance, packaging)

> **0.2.0 status column** (added 2026-11-01, for the 0.2.0 docs pass). ✅ = done, with the release that shipped it; ◐ = partly done; ❌ = open, with the milestone `docs/PLAN.md` now plans it for (parity steps 0.1.10 and 0.1.12 → 0.4.0, 0.1.11 and 0.1.13 → 0.5.0, 0.1.14 → 0.6.0). "As audited" means the row matched Terraria at audit time and was not re-checked. "Unchanged since the audit" means no shipped step targeted the row. The audit text itself is not updated.

This is a read-only audit, dated 2026-10-28. It changes no game code. Terraria is used only as behavioural evidence: v1.4.5.8 CIL, read with the scripts in `docs/terraria-evidence/full/scripts/`, or general knowledge where marked "(knowledge)".

Line numbers drift because another agent is editing `UI/GraphicalSandbox.cs` right now. Pointers name the function and give the line as seen on 2026-10-28, marked `~L`.

**How the evidence was gathered**

- Code was read under `UI/`, `Systems/EngineBenchmark.cs`, `Rimtera.csproj` and `.github/workflows/`.
- A private Debug build was made with `scratch_build.sh pe`. The published Release build in `publish/` is v0.1.6 from 2026-10-22.
- Dev flags run: `--engine-bench`, `--engine-bench --leak`, `--self-test`, `--item-audit`, `--tile-sprites` and `--flora-sprites`.
- Live captures used `shot.ps1` plus F10. A copy added the F10 key, because `shot.ps1` has no F10 entry.
- Terraria CIL was scanned for `SoundEngine.PlaySound` call sites, `UpdateAudio_DecideOnNewMusic`, `LightingEngine.UpdateLightDecay`, `DoDraw_UpdateCameraPosition`, `UpdateViewZoomKeys`, `CombatText.NewText`, the `PlayerDrawLayers` methods, and `DrawSurfaceBG_*` / `DrawBackground_*`.

---

## Summary

**Counts by status (100 features):** ✅ 44 · ◐ 32 · ❌ 13 · ⚠ 9 · ➕ 2

**Measured numbers**

| What | Value |
|---|---|
| Release frame, Large world, idle (v0.1.6) | 60 FPS (capped by the 60 Hz loop); drawing 8.2 ms: sky 1.6, tiles 1.0, light calc 2.0, light draw 2.1, HUD 1.3 |
| Debug frame, Large world, walking | 37 FPS; drawing 21.8 ms, of which light calc is 13.8 ms |
| Memory, Release v0.1.6 (4,379 sprites) | title screen 431 MB · Small quickstart 456 MB · Large 921 MB private |
| Memory, Debug with every current sprite (6,827) | title screen 807 MB · Large in game 1,215–1,299 MB private |
| Decoded sprite atlas | 674 MB for the current 6,827 PNGs; 325 MB for the 4,379 that shipped in v0.1.6 |
| World generation (Debug) | Tiny 2.1 s · Small 4.1 s · Medium 9.9 s · Large 13.7 s |
| Large world simulation tick | 3.26 ms; during a 3,000-cell flood 4.85 ms average with a **worst tick of 794 ms** |
| Large save / load | save 811 ms (10 MB cells + 242 KB JSON) · load 3.2 s · leak check clean |
| Startup to window | 3.2 s (Release v0.1.6) · 4.9 s (Debug, all sprites) |
| Self-test (Debug) | 615 checks, 0 FAIL, 52.9 s |

**Top 10 issues by player impact**

1. **Worn armor and vanity are invisible on the player** (R43, R44, A4). Equipping a helmet, chest or legs changes nothing on screen. Only a dye tints the base suit. Terraria draws the player in 72 `PlayerDrawLayers.DrawPlayer_*` layers. This is the biggest gap in how the game looks next to Terraria.
2. **The creature animation art will not ship** (K4, A5).
   - 2,448 frame PNGs (26.6 MB, 306 actors) in `Assets/Sprites` are untracked in git.
   - CI's Publish builds from git, so a release gets none of them. `publish/` also has only 4,379 sprites.
   - `docs/QUEUED_WORK.md` and the memory note still say "paused at 169/398". On disk, 489 of 495 actors have full 8-frame sets, and the other 6 have hand-made sets.
3. **Sprite memory doubles once the art ships** (A6, P2). `SpriteAtlas.Load` decodes every PNG at startup to 32-bit premultiplied ARGB. The title screen goes from 431 MB to 807 MB. The scaled-copy cache (`SpriteAtlas._scaled`) has no size limit. The user's "~490 MB, leave it" was measured on the old sprite set.
4. **Many actions are silent** (S10–S25). Breaking a block, swinging an axe or hammer, Tech weapons, summon staffs, placing walls, crafting, coins, potions, food, doors, chests and water splashes have no sound. `break`, `craft`, `coin`, `splash`, `door` and `explosion` are synthesised, and `break` is even recorded, but nothing plays them. Terraria has 1,704 `PlaySound` call sites in 566 methods and references 469 `SoundID` fields. Rimtera has about 20 triggers.
5. **Despawns sound and look like kills** (R37, S19). A creature that leaves the world off-screen plays `death` at 0.6 volume with no distance falloff, and bursts death particles where it was. Bug-net catches do the same.
6. **The music is one procedural generator with 14 themes** (S3–S7). There is no event music and one boss theme for every boss. Night only slows the tempo by 6%. Theme changes cut instantly. Underground music only plays under grassland and caverns. Terraria's `UpdateAudio_DecideOnNewMusic` picks from 63 distinct track ids.
7. **No block dust and no gore** (R35, R36). Mining and breaking blocks throw no material-coloured dust; Terraria uses `KillTile_MakeTileDust` and `GetTileDustAmount`. Deaths are a coloured particle puff.
8. **Tile seams are visible** (R28). Faint dark lines show on every tile edge, plus 480 px chunk seams in dirt (screenshot at 1280×720, 100% zoom). Terrain reads as a grid.
9. **Liquids** (P4, R33). A 3,000-cell flood causes a 794 ms tick hitch. Liquids draw as flat per-cell rectangles, with no surface smoothing, waves or waterfalls.
10. **Event skies and coloured sky light are missing** (R6, R10, R22). Rust Moon, Dark Sun and the pillars don't change the sky or the light. Sky light is neutral white at all hours. Only 4 biomes tint the sky, and toxic fallout and solar flare add full-screen overlays.

Nothing in this area **blocks play**. Item 2 blocks the art from reaching players. Item 3 becomes a risk on 8 GB machines once item 2 is fixed.

---

## Feature tables

### Rendering: frame, camera, sky and backgrounds

| # | Feature | Where (Rimtera) | Terraria behaviour | Rimtera behaviour (verified) | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R1 | Fixed-step loop, frame skip | `GraphicalSandbox.cs` `OnIdle` ~L1477, `StepSeconds` 1/60, `MaximumCatchUpSteps` 4 | 60 Hz update; Frame Skip Off/On/Subtle (knowledge) | 60 Hz step, catch-up of 1, 2 or 4 steps by FrameSkip; Sleep(1) when idle; `timeBeginPeriod(1)` | ✅ | ✅ as audited |
| R2 | Camera follow | `DrawWorld` ~L2163; `CameraFollowX/Y` 14/9 ~L1733 | `DoDraw_UpdateCameraPosition`: `screenPosition` follows the player; `cameraLerp` is used only when set | Exponential ease toward the player (frame-rate independent); snaps on teleports (>24 tiles); sub-tile pixel scrolling | ➕ | ➕ kept |
| R3 | Zoom | `TileSize` ~L1540 (20 px × Zoom, clamped 12–40); Ctrl+wheel ~L192; Options entry `MenuSystem.cs` ~L1215 | `UpdateViewZoomKeys` step 0.02, range 1–2 | 0.6–2.0 in 0.1 steps, integer tile sizes. `GamePreferences.Normalize` clamps Zoom twice (0.5–2.5 at L134, then 0.6–2 at L149), which is redundant but harmless | ◐ | ◐ unchanged since the audit |
| R4 | Screen shake | none | Shake on boss roars, explosions and quakes (knowledge) | None | ❌ | ◐ PE-16 partly (0.1.14 camera smoothing; no screen shake) |
| R5 | FPS readout and frame profiler (F10) | `OnPaint` ~L1913; `FrameProfiler` in `GraphicalSandbox.Backdrop.cs` L88 | FPS readout only (knowledge) | FPS, window size, DPI, and rolling milliseconds per section (setup, sky, tiles, live, actors, light calc, light draw, effects, HUD, menus) | ✅ | ✅ as audited |
| R6 | Sky gradient, day and night | `DrawSky` ~L2100; `NightFactor` ~L2143 | `Main.ColorOfTheSkies` tints the sky and the light; `DrawSunAndMoon` (810 instructions) | Two-colour gradient blended by NightFactor; dusk 18–21 h, dawn 4–7 h. World light has no warm dawn or dusk tint (see R22) | ◐ | ❌ PE-10 → 0.4.0 |
| R7 | Sun and moon | `GraphicalSandbox.Sky.cs` `DrawSkyBodies` L39 | Arc across the sky, 8 moon phases (knowledge) | Sprite on an arc (day 5–19 h); the moon is shaded for `MoonPhase` 0–7 | ✅ | ✅ as audited |
| R8 | Stars | `DrawSky` | `DrawStarsInBackground`: stars twinkle and move; falling stars are visible (knowledge) | 110 fixed dots that fade in with night. No twinkle, no drift, no shooting stars | ◐ | ❌ PE-10 → 0.4.0 |
| R9 | Clouds | `Sky.cs` `DrawSpriteClouds` L83; fallback `DrawClouds` ~L2468 | Cloud layers with camera parallax, darker in rain (knowledge) | 6 sprite clouds; count follows cloud cover; drift with wind; darkened in storms. Screen-space, with no camera parallax | ◐ | ❌ PE-10 → 0.4.0 |
| R10 | Event and biome skies | `BiomeSkyTint` ~L2125; fallout and flare overlays in `DrawWorld` | Blood moon red sky and light, eclipse darkness, pillar and boss sky effects (`Terraria.Graphics.Effects`, 78 methods) | Tint for Frozen, Junk, Radiated and Molten only; green overlay for toxic fallout and amber for solar flare. Rust Moon, Dark Sun, pillars and bosses change nothing | ❌ | ❌ PE-10 → 0.4.0 |
| R11 | Surface parallax per biome | `Sky.cs` `DrawParallax` L109, `BackgroundTheme` L22 | `DrawSurfaceBG` plus 13 biome variants (`_Jungle`, `_Snow`, `_Desert`, `_Mushroom`, …); `DrawSurfaceBG_DrawChangeOverlay` cross-fades between biomes | 10 themes with far and near layers; horizon pinned to a smoothed regional ground line; fades out underground. Biome changes swap instantly. `BackgroundParallax` is forced to 1 and only used by the title screen | ◐ | ❌ PE-19 → 0.5.0 |
| R12 | Half-resolution backdrop cache | `Backdrop.cs` `DrawBackdrop` L25 | n/a | Sky, clouds and parallax render to a half-resolution bitmap at 30 Hz and blit as one opaque image; skipped when the view is fully underground | ➕ | ➕ kept |
| R13 | Underground backgrounds | `GraphicalSandbox.World.cs` `DrawDepthBackdrop` L422 | `DrawBackground_PickUndergroundBackgroundStyle` picks a per-biome style; `_DrawRockLayer`, `_DrawMagmaLayer` and `_DirtBackground` scroll with parallax | Three depth bands (regolith, rock, radiated) with an 8-row cross-fade. Locked to tiles (no parallax) and the same in every biome | ◐ | ❌ PE-19 → 0.5.0 |
| R14 | Title panorama | `DrawTitleBackdrop` ~L1987 | Animated title background (knowledge) | Scrolling biome strip with mountains and props | ✅ | ✅ as audited |

### Rendering: weather

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R15 | Rain and snow | `DrawWeather` ~L2410 | Rain drops are entities that hit tiles and splash (`Main.DrawRain`); snow particles (knowledge) | Up to 260 screen-space streaks slanted by wind; snow in Frozen. Gated on the biome's `Underground` flag only, so rain draws over caves and indoors near the surface. No collision or splash | ◐ | ❌ PE-13 → 0.4.0 |
| R16 | Fog, dust, lightning | same | Fog, graveyard mist, lightning flash (knowledge) | Fog and dust full-screen overlays; lightning bolt polyline plus flash for 0.35 s | ✅ | ✅ as audited |
| R17 | Wind sway and sandstorm or blizzard particles | none | Trees, grass and vines sway on windy days; sandstorm and blizzard particles (knowledge) | A dust overlay only; flora is static | ❌ | ❌ PE-13 → 0.4.0 |
| R18 | Acid rain, toxic fallout, solar flare visuals | `DrawWeather`; `DrawWorld` overlays | n/a (Rimtera events) | Acid-green rain, green and amber screen washes | ✅ | ✅ as audited |

### Rendering: lighting

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R19 | Light propagation | `UI/LightMap.cs` `Compute` L60, `Spread` L248 | `LightingEngine` scans, blurs and presents; `UpdateLightDecay` sets decay through air 0.91, solid 0.56, and water 0.88–0.94 by water style | Seeds, then 2 × 4 directional sweeps; decay through air 0.90, liquid 0.82, solid 0.62 | ✅ | ✅ as audited |
| R20 | Smooth lighting and quality modes | `LightMap.Subdivision`; `DrawWorld` sets Low = 1, Standard and High = 4 | Color, White, Retro and Trippy modes (knowledge) | Off, Low (blocky), Standard (smooth), High (smooth plus coloured glow) | ✅ | ✅ as audited |
| R21 | Coloured light | `LightMap.Present` L310 | Light is per channel and multiplies tile colour (knowledge) | A monochrome darkness layer (alpha 245 × (1 − max channel)) plus an additive tint where one channel dominates, capped at alpha 90. Lights don't truly colour the scene | ◐ | ✅ PE-11 0.2.0 |
| R22 | Sky light | `LightMap.Compute` sky seeding; `GameSimulation.SkyLightLevel` (`GameSimulation.World.cs` L122) | The surface takes the sky colour: warm at dawn and dusk, red in a blood moon, dark in an eclipse (`Main.ColorOfTheSkies`) | Sky light fills each column down to the first solid tile, only where there is no wall; dimmed by night, moon phase, cloud, rain and storm. Always neutral white; no event colour | ◐ | ❌ PE-10 → 0.4.0 |
| R23 | Light sources | `LightMap.Emission` L177; `DrawWorld` lamp list | Tiles, walls, liquids, `AddLight` from projectiles, NPCs, items and buffs (knowledge; `Lighting.AddLight`) | Tile lights (wiring-aware), name-based glow, glowing liquids, player, crew and guide lamps, `PlayerLightSources`. Projectiles, bosses and glowing creatures give no light | ◐ | ◐ unchanged since the audit |
| R24 | Off-screen light reach | `LightMap.Padding` = 10 | `LightMap.NonVisiblePadding` (knowledge: a wider margin) | Lights more than 10 tiles off-screen don't reach the view; a slight pop-in at the edges | ◐ | ◐ unchanged since the audit |
| R25 | Underground darkness and map reveal | `Present`; `Map.RevealFromLight` in `DrawWorld` | Black where unlit; the map reveals lit tiles (`LightingEngine.ExportToMiniMap`) | Near-black (alpha 245); the map reveals from the light map | ✅ | ✅ as audited |
| R26 | Lighting cost | F10 | n/a | Release: 2.0 ms calc + 2.1 ms draw at 1280×720. Debug: 13.8 ms calc, the largest Debug cost | ✅ | ✅ as audited |

### Rendering: tiles, walls, liquids and particles

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R27 | Static tile cache | `GraphicalSandbox.Chunks.cs` (24×24 chunks, `CellChanged` dirtying, at most 40 kept) | `TileDrawing` caches render targets (`ClearCachedTileDraws`) | Chunk bitmaps redrawn on change; live cells drawn every frame | ✅ | ✅ as audited |
| R28 | Tile seams | screenshot `large_f10_run.png` | No visible grid | Faint dark lines on every tile edge in dirt, plus chunk seams every 480 px at 100% zoom | ⚠ | ❌ PE-8 → 0.5.0 |
| R29 | Tile framing | `UI/TileArt.cs`; `DrawShapedTile` `GraphicalSandbox.World.cs` L196 | Frame atlas per tile (edges, corners, 3 random variants) chosen from neighbours (knowledge; `TileDrawing`, 111 methods) | One texture per tile plus procedural outlines on exposed sides, a top rim, corner chamfers, soft-into-hard bleed and a grass fringe. No per-tile variants | ◐ | ❌ WT-17 → 0.5.0 |
| R30 | Slopes and half blocks | `ShapePolygon` `World.cs` L249 | Drawn sloped | Clipped polygon of the texture | ✅ | ✅ as audited |
| R31 | Walls | `RenderChunk` wall pass | Wall frames and edges, lit (knowledge) | One texture plus a flat shade; no edges or variants | ◐ | ◐ unchanged since the audit |
| R32 | Mining cracks | `DrawTileCracks` ~L1528 | `HitTile.DrawFreshAnimations` | Crack lines grow with damage | ✅ | ✅ as audited |
| R33 | Liquids | `DrawWorld` live pass | `DrawWaters`, `DoDraw_Waterfalls`, smoothed surface, translucent (knowledge) | Per-cell rectangle sized by depth with the liquid texture; no surface smoothing, no waves, no waterfalls | ◐ | ❌ PE-9 → 0.5.0 |
| R34 | Particles | `UI/ActorAnimator.cs` (pool of 700), `Gear.cs` and `HeldItems.cs` emitters | `Dust.NewDust` (693 instructions), dust everywhere | Hit and death bursts, landing and jump dust, double-jump puff, gear trails, tool sparks; density follows the Particles setting | ✅ | ✅ as audited |
| R35 | Block dust on hit and break | none | `WorldGen.KillTile_MakeTileDust` and `KillTile_GetTileDustAmount`: dust coloured by tile type on every hit and on break | None. Drills spark at the tip, but the material throws nothing | ❌ | ✅ PE-7 0.1.15 |
| R36 | Gore and death visuals | `ActorAnimator.Update` | `Main.DrawGore`: body-part gore per NPC (knowledge) | Coloured particle burst, 18 + 8 particles | ◐ | ✅ PE-7 0.1.15 |
| R37 | Despawn vs death | `ActorAnimator.Update` L94 | Despawns are silent and invisible | Any enemy id that vanishes gets a death burst, including despawns and bug-net catches | ⚠ | ✅ PE-5 0.1.15 |
| R38 | Damage numbers | `ActorAnimator.AddText` / `DrawEffects` | `CombatText.NewText`, 11 call sites: crits are larger and orange (`StrikeNPC_Inner`), heals green, mana blue, damage over time (`HurtLifeRegen`) | Enemy damage in amber, player damage in red, heals in green; can be turned off. No crit styling, no mana text, damage over time looks like a hit | ◐ | ✅ PE-14 0.1.15 |
| R39 | Enemy health bars | `DrawHealthBar` ~L2538 | `DrawInterface_14_EntityHealthBars` | Above or below or off; boss colour | ✅ | ✅ as audited |
| R40 | Hit flash and telegraph blink | `DrawSpriteActor` ~L2494 | Hurt flash (knowledge) | White flash on hit; blink while telegraphing | ✅ | ✅ as audited |

### Rendering: player, held items, armor and dyes

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R41 | Player body animation | `ActorAnimator.AnimatedPose` L207; `DrawPlayer` ~L2549 | Body frames for walk, jump, use and so on (knowledge) | 8-frame walk timed to speed; idle, jump, fall, land, hurt, down. `anim_climb_1-2`, `anim_sit_1-3`, `anim_mine_*`, `anim_slash_*` and `anim_aim` exist on disk but no code uses them | ◐ | ❌ PE-12 → 0.4.0 |
| R42 | Appearance recolour | `UI/CharacterLook.cs` | Skin, hair and clothes colours (knowledge) | HSV recolour of the head and skin from the colonist's appearance | ✅ | ✅ as audited |
| R43 | Worn armor drawn | none | `PlayerDrawLayers`: 72 `DrawPlayer_*` layers (head, body, legs, arms, back, front, and so on) | **Not drawn.** `UpdateGear` (`Gear.cs` L130) reads the slots only for effects such as wings and speed trails. Armor sprites are item icons only | ❌ | ❌ PE-3 → 0.6.0 |
| R44 | Vanity slots shown | `InventoryUi.cs` ~L530 (slots exist) | Vanity overrides armor looks | The slots work in the inventory but change nothing on screen | ❌ | ❌ PE-3 → 0.6.0 |
| R45 | Dyes | `GraphicalSandbox.PlayerSystems.cs` `DyedLook` L209 | A per-slot shader dye on each armor, vanity and accessory piece | One tint, from the body dye, else head, else legs, over the whole suit | ◐ | ◐ unchanged since the audit |
| R46 | Held items and use styles | `GraphicalSandbox.HeldItems.cs` (890 lines) | `ItemCheck` use styles: swing, thrust, hold-up, eat, shoot, and so on | Every use style, arm drawn from the shoulder, smears, muzzle flashes, rune rings, eat and drink, bows, tethers | ✅ | ✅ as audited |
| R47 | Wings, rocket boots, accessory visuals | `GraphicalSandbox.Gear.cs` | Wing and boot effect layers | Wings flap, jets, speed streaks, fire, frost and water trails, buff motes, detector pings | ✅ | ✅ as audited |
| R48 | Immunity blink and dash ghosts | `AnimatedPose`; `DrawDashGhosts` | Immunity blink (knowledge) | Blink while immune, dash after-images | ✅ | ✅ as audited |
| R49 | Mounts, minions, pets, crew, town NPCs | `DrawTravelMount`, `DrawCompanions`, `DrawCrew` ~L2560 | Drawn with frames | Drawn with generated frames (`AnimatedFrame`) and name tags | ✅ | ✅ as audited |

### Rendering: creature animation

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| R50 | Creature frame sets on disk | `SpriteCatalog.AnimatedFrame` (`SpriteAtlas.cs` L580); `tools/comfy/anim_entities.tsv` | Every NPC has a frame sheet | **489 of 495 actors have all 8 frames** (idle 1–2, move 1–4, attack 1–2): enemies 327/328, bosses 28/33, critters 101/101, NPCs 24/24, crew 5/5, pets 4/4. The other 6 (AlphaSkitter, VoidStalker, CyberSwarmLeader, AstroGod, Abomination, RadLeech) use hand-made sets in `EnemyAnimations` (L556). No actor is a static placeholder | ✅ (on disk; see K4) | ✅ as audited |
| R51 | Frame selection | `EnemyFrame` L594; `AnimatedFrame` | `NPC.FindFrame` per AI: hurt, death and special frames; frame rate follows speed (knowledge) | Three states only: idle, move (speed > 0.3 or flying) and attack (telegraphing). Fixed rates of 2.5, 9 and 8 fps. No hurt or death frames | ◐ | ◐ unchanged since the audit |
| R52 | Draw scale and feet placement | `DrawSpriteActor`; `EnemyDrawScale` | n/a | Scaled once from the reference frame so bodies don't change size; feet on the bottom of the box | ✅ | ✅ as audited |
| R53 | Worm bodies, mimic disguise | `DrawWormBody`; disguised chest | Segmented worms, mimics | Present | ✅ | ✅ as audited |

### Assets

| # | Feature | Evidence | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| A1 | Item sprites | `--item-audit`: 1,981 items, "Without their own sprite: 0" | Every item has its own sprite | Every item has its own sprite | ✅ | ✅ as audited |
| A2 | Tile and flora sprites | `--tile-sprites` 110/110; `--flora-sprites` 59/59 on disk | n/a | Complete | ✅ | ✅ as audited |
| A3 | Sky and background art | `bg_<theme>_far/near` × 10; `sky_sun`, `sky_moon`, `sky_cloud_1-6` | n/a | Complete | ✅ | ✅ as audited |
| A4 | Worn-armor and vanity art | none (the 77 `armor_*` sheets in `art/generated` are icons) | Armor sheets per body frame | Missing; it has to match the 20 player body frames | ❌ | ❌ PE-3 → 0.6.0 |
| A5 | ComfyUI run status | `art/generated/overnight.log` ends at `[169/398] creature_rift_obelisk`; sheets in `art/generated/anim` dated to 2026-10-23 (490 sheet + ref pairs); frames for 489/495 actors present | n/a | **`docs/QUEUED_WORK.md` §1 and the memory note are stale.** The animation pass is effectively done on disk. The texture run's sheet 6 of 6 was not re-checked here | ⚠ (docs) | ✅ PE-1 0.1.7 |
| A6 | Sprite loading and memory | `SpriteAtlas.Load` L22 decodes every PNG to 32bppPArgb at startup; `_scaled` cache (L19) has no size limit | Textures load on demand (`Main.instance.LoadNPC`, `Asset<T>` async) (knowledge) | 6,827 PNGs decode to **674 MB**. The largest frames are 1078×561 (`boss_gel_sovereign_*`, 2.4 MB each). Creature frames take 253 MB, event creatures 127 MB, critters 72 MB. Every zoom level adds another scaled copy | ⚠ | ✅ PE-2 0.1.7 |

### Audio

| # | Feature | Where | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| S1 | Mixer and output | `UI/AudioEngine.cs` L13–184 | XACT/XNA engine; most sounds get a random pitch variation (knowledge) | waveOut 44.1 kHz stereo, 4 × 2048-frame buffers (up to about 186 ms queued, so sounds land late), 24-voice cap that drops the oldest, ±10% volume variation, no pitch variation | ◐ | ❌ PE-15 not scheduled |
| S2 | Recorded clips | `LoadRecordedSounds` L247; `Assets/Sounds/*.wav` | n/a | 67 takes for 18 names, levelled to −16 LUFS plus per-name offsets; a random take per play. `Assets/Sounds/raw/boss.mp3` is untracked and not shipped (only `*.wav` is in the csproj) | ✅ | ✅ as audited |
| S3 | Music per biome | `AudioDirector.Update` theme switch; `MusicState.CurrentStyle` L637 | `UpdateAudio_DecideOnNewMusic` (1,777 instructions) assigns **63 distinct music ids**, with separate day and night, underground per biome, space, ocean, rain, desert, jungle, snow, hallow, evil, mushroom, dungeon and temple tracks | Procedural synth, 14 themes plus title (grassland, canopy, coast or ocean, frozen, crystal, fungal, junk, forest, overgrowth, radiated, underground, molten or core, boss) | ◐ | ◐ PE-6 partly (after 0.1.7: biome, event and boss-tier music) |
| S4 | Day and night music | `MusicState.Render` L661 | Distinct night tracks (knowledge) | Night slows the tempo by 6%; same theme | ◐ | ◐ PE-6 partly (after 0.1.7: biome, event and boss-tier music) |
| S5 | Event music | `AudioDirector` tension | Eclipse, invasions, moon events, pillars and others have their own tracks (`DecideOnNewMusic`) | None; any active event only raises tension to 0.7 | ❌ | ◐ after 0.1.7: event music (AudioScene); EV-8 → 0.4.0 |
| S6 | Boss music | theme "boss" | Several boss tracks; each boss has its own (knowledge) | One "boss" style for every boss | ◐ | ◐ BO-14 partly (after 0.1.7: music per boss tier) |
| S7 | Underground music | `AudioDirector` | Underground track per biome (knowledge) | Only when underground in Grassland or Caverns | ◐ | ◐ PE-6 partly (after 0.1.7: biome, event and boss-tier music) |
| S8 | Music transitions | `SetMusicMood` | Old track fades out, new one fades in (`Main.musicFade`, knowledge) | Theme changes cut instantly in the next buffer; tension eases | ◐ | ◐ PE-6 partly (after 0.1.7: biome, event and boss-tier music) |
| S9 | Ambient sound | `AudioEngine.Mix` rain noise | `Ambience` plus `LegacyAudioSystem.UpdateAmbientCueState`: wind, rain, lava, waterfalls, birds by day, crickets at night | Rain noise only | ◐ | ◐ PE-17 partly (after 0.1.7: biome and weather ambience beds) |
| S10 | Music boxes and audio decks | `MusicThemeOverride` | Music boxes | Decks override the theme | ✅ | ✅ as audited |
| S11 | Volume channels | `GamePreferences` sound, music and ambient volume | Sound, music and ambient sliders | Same three | ✅ | ✅ as audited |

**Sound effects by action.** Terraria: 1,704 `SoundEngine.PlaySound` call sites. By type: Projectile 457, Main 271, NPC 270, Player 210, WorldGen 78, ItemSlot 45, and so on. `WorldGen.KillTile_PlaySounds` alone has 25 `PlaySound` calls that depend on the tile type. Rimtera's triggers are all in `UI/AudioDirector.cs`, plus menu hover and click and the achievement chime.

| # | Action | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|
| S12 | Melee swing | Swing sound per item (`Item.UseSound`) | `swing` when UseKind is Melee | ✅ | ✅ as audited |
| S13 | Gun and bow fire | Per-item `UseSound` | `shoot`, or `zap` for Tech-class Ranged items | ✅ | ✅ as audited |
| S14 | Tech weapons (UseKind.Tech), summon and command staffs | Per-item `UseSound` | **Silent.** The switch only covers Pickaxe, Melee, Ranged and PlaceTile | ⚠ | ✅ PE-4 0.1.15 |
| S15 | Mining hit (pickaxe) | Dig sound on each hit | `dig` when the use cooldown restarts | ✅ | ✅ as audited |
| S16 | Axe (Cutter), hammer (Demolisher), wall placement, wire tools | Dig or hammer sounds | Silent | ❌ | ✅ PE-4 0.1.15 |
| S17 | Block broken | `KillTile_PlaySounds`: dig, glass shatter (Item27), metal, grass and others by tile | `break` exists (recorded, 3 takes) but nothing plays it | ❌ | ✅ PE-4 0.1.15 |
| S18 | Place block | Dig/place sound | `place` | ✅ | ✅ as audited |
| S19 | Enemy hit and death | `NPC.HitSound` and `DeathSound` per type | Generic `hit` and `death` with distance volume and pan. **Despawns and bug-net catches play `death` at 0.6 with no distance or pan** | ⚠ | ✅ PE-4 0.1.15 |
| S20 | Player hurt and death | Player hurt (with variants), death sound | `hurt`; death has no sound of its own | ◐ | ✅ PE-4 0.1.15 |
| S21 | Jump, double jump, land | No sound for a normal jump or landing; double jumps have sounds (knowledge) | `jump` and `land` on every jump and landing; no double-jump sound | ➕/◐ (counted ◐) | ➕ kept |
| S22 | Item pickup and coins | Grab sound; coin sound for coins | `pickup` whenever the inventory count rises, **including after crafting or buying**; `coin` never plays | ⚠ | ✅ PE-4 0.1.15 |
| S23 | Crafting, potions, food, doors, chests, NPC talk, splash, explosion | Each has its own sound (Grab, Item2, Item3, DoorOpen and DoorClosed, chest, MenuOpen, Splash, Item14) | `craft`, `door`, `splash` and `explosion` are synthesised but never played; the others have no sound | ❌ | ✅ PE-4 0.1.15 |
| S24 | UI hover and click | `MenuTick` and `MenuOpen` sounds | `hover` and `click` in menus | ✅ | ✅ as audited |
| S25 | Thunder, fishing bite, boss spawn, event start, achievement | Thunder, bite and roar sounds | All present (`thunder` panned) | ✅ | ✅ as audited |
| S26 | Voice sounds (player grunts, NPC voices) | Player hurt varies by character; NPC hit and death sounds per type (knowledge) | None. The only "voice" is the user's own recorded takes, used as effects | ❌ | ❌ PE-18 → 0.4.0 |

### Performance

| # | Feature | Evidence | Terraria | Rimtera | Status | 0.2.0 status |
|---|---|---|---|---|---|---|
| P1 | Frame time | F10 | Steady 60 FPS (knowledge) | Release, Large world, 1280×720: 60 FPS, 8.2 ms. Release, Small world with the inventory open: 76 FPS, 11.8 ms. Debug, Large, walking: 37 FPS, 21.8 ms (light calc 13.8 ms) | ✅ (Release) | ✅ as audited |
| P2 | Memory | `shot.ps1` `mem` | About 1–2 GB for a large world (knowledge) | Release v0.1.6: 431 MB title, 456 MB Small, 921 MB Large. With today's sprites (Debug): 807 MB title, about 1.2–1.3 GB Large. **Left as is at the user's request; documented here.** | ◐ | ✅ PE-2 0.1.7 |
| P3 | World generation time | `--engine-bench` (Debug) | Large world takes minutes (knowledge) | Tiny 2.1 s, Small 4.1 s, Medium 9.9 s, Large 13.7 s. Slowest passes on Tiny: "Irradiating the core" 178 ms, "Raising background walls" 110 ms | ✅ | ✅ as audited |
| P4 | Simulation tick | `--engine-bench` | n/a | Large: 3.26 ms per tick. During a flood of 3,000 cells: 4.85 ms average, **794 ms worst** (peak active 6,071). A visible freeze | ⚠ | ⚠ unchanged since the audit |
| P5 | Save and load | `--engine-bench --leak` (Large) | n/a | Save 811 ms (`.cells` 10.1 MB + `.json` 242 KB); load 3.2 s without regeneration; old world collected after save and after load (3 MiB left). The plain bench reports "old world collected: False", but that is the Debug JIT keeping locals alive; the `--leak` path is clean | ✅ | ✅ as audited |
| P6 | Startup | timed launch | n/a | 3.2 s to window (Release v0.1.6); 4.9 s Debug with all 6,827 sprites (eager decode) | ◐ | ◐ unchanged since the audit |
| P7 | Self-test time | `--self-test` | n/a | 615 checks, 0 FAIL, 52.9 s (Debug) | ✅ | ✅ as audited |
| P8 | Render caches | chunk cache (bounded to 40), tinted-image cache (cleared above 160), TileArt (cleared on zoom), `SpriteAtlas._scaled` (**unbounded**) | n/a | One cache grows without limit across zoom levels and frame sizes | ◐ | ◐ unchanged since the audit |

### Packaging

| # | Feature | Where | Status and notes | 0.2.0 status |
|---|---|---|---|---|
| K1 | Prove workflow | `.github/workflows/prove.yml` | Release build; matrix runs `--self-test`, `--batch-a-test` and `--demo`; a determinism job compares two demo hashes; logs uploaded. ✅ | ✅ as audited; 0.2.0 adds an assets prover (PE-20) |
| K2 | Version workflow | `version.yml` | SemVer check of `<Version>` (now 0.1.6); a manual dispatch on `main` creates the `vX.Y.Z` tag. ✅ | ✅ as audited |
| K3 | Publish workflow | `publish.yml` | A tag is checked against `<Version>`; the self-test runs; then a self-contained single-file win-x64 build, a zip and `SHA256SUMS.txt`, and a GitHub release with generated notes. ✅ | ✅ as audited |
| K4 | What a published build contains | `Rimtera.csproj` copies `Assets\Sprites\**\*.png` and `Assets\Sounds\*.wav` | CI checks out git, so **the 2,448 untracked frame PNGs (306 actors) are missing from every release**. `publish/` (local, 2026-10-22) has 4,379 sprites, against 6,827 on disk. `raw/` is correctly left out. ⚠ | ✅ PE-1 0.1.7 |
| K5 | Version shown in game | `GameVersion` ~L3623, drawn on the title screen | ✅ | ✅ as audited |
| K6 | Performance and asset gates in CI | none | No `--engine-bench` budget, no check that every sprite the game references is tracked in git, no screenshot smoke test. ◐ | ✅ PE-20 0.2.0 |
| K7 | Code signing | none | Unsigned exe, so Windows SmartScreen warns on first run (knowledge). ❌ (optional) | ❌ PE-21 deferred |

**Tally**

| Table | Rows | ✅ | ◐ | ❌ | ⚠ | ➕ |
|---|---|---|---|---|---|---|
| R (rendering) | 53 | 24 | 19 | 6 | 2 | 2 |
| A (assets) | 6 | 3 | 0 | 1 | 2 | 0 |
| S (audio) | 26 | 9 | 9 | 5 | 3 | 0 |
| P (performance) | 8 | 4 | 3 | 0 | 1 | 0 |
| K (packaging) | 7 | 4 | 1 | 1 | 1 | 0 |
| **Total** | **100** | **44** | **32** | **13** | **9** | **2** |

The two ➕ are R2 (eased camera) and R12 (half-resolution backdrop cache). S21 (jump and land sounds) is Rimtera-only but counts as ◐ because there is no double-jump sound. R50 counts as ✅ for what is on disk; the shipping problem is counted at K4.

---

## Integration steps

### PE-1: Ship the generated creature art, and fix the stale run notes (blocks the art from reaching players)
- **Change:**
  - Commit the 2,448 untracked `Assets/Sprites/*_{idle,move,attack}_N.png` files (26.6 MB) after a visual pass.
  - Update `docs/QUEUED_WORK.md` §1, since the animation pass is complete on disk.
  - Re-check the texture run log for sheet 6 of 6.
  - Add a self-test check that every actor listed in `anim_entities.tsv` has either 8 frames in `git ls-files` or a hand-made set.
- **Files:** `Assets/Sprites/`, `docs/QUEUED_WORK.md`, `docs/COMFYUI.md`, a new self-test (for example in `Systems/CreatureCoverageSelfTests.cs`).
- **Size:** S
- **Depends on:** the user's go (memory note: "resume only on the user's go"; this commits finished output, it does not restart the run), and PE-2 so memory doesn't jump.
- **Acceptance:**
  - `git status --porcelain Assets/Sprites` is empty.
  - A Publish dry run (workflow_dispatch) has 6,827 or more PNGs in the zip.
  - The self-test reports "495/495 actors animated".

### PE-2: Load sprites lazily, and downscale oversized frames
- **Change:**
  - `SpriteAtlas.Load` indexes file paths only. `Get` decodes on first use and holds an LRU of decoded bitmaps.
  - Downscale generated frames at import (`sprite_post.py`) to their largest draw size. Boss frames are drawn at about 1.25× the box, so 1078×561 is roughly 4× too big.
  - Bound `_scaled` with an LRU, and clear it on zoom changes, as `TileArt.UseSize` already does.
- **Files:** `UI/SpriteAtlas.cs`, `tools/comfy/sprite_post.py`, `UI/GraphicalSandbox.cs` (call sites of `_sprites.Get` used for existence checks should use `Find` on the index).
- **Size:** M
- **Depends on:** none. Do it before or together with PE-1.
- **Acceptance:**
  - Title-screen private memory is at most 450 MB with all 6,827 sprites (now 807 MB, Debug).
  - Startup to window is at most 3.5 s.
  - F10 "actors" stays under 1 ms in a boss fight.
  - A self-test asserts that no sprite larger than 512 px on its long side is in `anim_entities` frames.

### PE-3: Draw worn armor and vanity on the player
- **Change:**
  - Add a paper-doll layer for the head, body and legs pieces, using vanity when it is set, otherwise armor.
  - Order: back accessories, legs, body, arm or held item, head, front accessories. Each layer is a sprite per body frame; the 20 `anim_*` frames share a common skeleton, so per-piece overlays can be keyed by frame name.
  - Interim option: a "worn" sprite per piece with an anchor point per frame (head, torso, hips), offset by the rig in `RigFor`.
  - Apply a dye to each piece, not to the whole suit (`DyedLook`).
- **Files:** `UI/GraphicalSandbox.cs` `DrawPlayer`, `UI/GraphicalSandbox.PlayerSystems.cs` `DyedLook`, `UI/GraphicalSandbox.HeldItems.cs` (rig anchors), a new `UI/GraphicalSandbox.Armor.cs`, the art pipeline (`tools/comfy/gen_anim.py` or a new `gen_armor.py`).
- **Size:** L (art 152 armor items × 3 slots; code M)
- **Depends on:** the items and gear area (equip slots, the vanity set list); PE-2 (the memory budget for the new sheets).
- **Acceptance:**
  - A play-test with `--kit arsenal` shows a visible change on the player when each armor piece goes on.
  - Vanity overrides armor.
  - A dye in the head slot tints only the helmet.
  - A self-test asserts that every `EquipmentSlot.Head/Body/Legs` item resolves to a worn-layer sprite or an explicit "no-visual" flag.

### PE-4: Fill in the missing sound triggers
- **Change:** move from inferring sounds from cooldown and inventory deltas to explicit simulation sound events. Add a `game.SoundEvents` queue that the simulation fills and `AudioDirector` drains, each with a position. Emit:
  - `break` with a material variant (stone, soil, plant, glass, metal) when a block is destroyed.
  - `dig` on every pick, drill, axe or hammer hit, including channelled drills.
  - `place` for walls.
  - Use sounds for the Tech and Command use kinds.
  - `craft` on a successful craft, and stop `pickup` firing for it.
  - `coin` for credit pickups.
  - Drink and eat sounds for potions and food.
  - `door` for doors, open and close sounds for chests.
  - `splash` on entering or leaving liquid.
  - `explosion` for explosives.
  - Player death.
  - Double jump.
- **Files:** `UI/AudioDirector.cs`, `UI/AudioEngine.cs` (new synth fallbacks: eat, drink, chest, grab, death sting), `Systems/GameSimulation.Items.cs`, `Systems/GameSimulation.ToolCombat.cs`, `Systems/CraftingSystem.cs`, `Systems/GroundItems.cs`, `Systems/GameSimulation.Physics.cs`.
- **Size:** M
- **Depends on:** the mining and tools, crafting and items areas (hook points); the controls and UI agent (inventory and chest UI events).
- **Acceptance:** a self-test drives a headless simulation through mine-to-break, wall place, craft, drink, door, splash and the Tech and summon uses, and asserts one event of each kind. A play-test confirms each is audible.

### PE-5: Stop treating despawns as kills
- **Change:**
  - The simulation exposes a `Died` and `Despawned` reason, or a `RecentDeaths` list with positions, for enemies leaving `game.Enemies`.
  - The animator bursts and the director plays `death` only for real kills, with `Near` and `Pan`.
  - Bug-net catches get a soft catch sound.
- **Files:** `UI/ActorAnimator.cs` `Update`, `UI/AudioDirector.cs`, `Systems/GameSimulation.Spawning.cs` and `GameSimulation.Combat.cs` (death or despawn records).
- **Size:** S
- **Depends on:** the creatures and spawning area.
- **Acceptance:** a self-test despawns 20 off-screen enemies and asserts no death events. A play-test standing still at night hears no stray death sounds.

### PE-6: Music variety, events and crossfade
- **Change:**
  - Add an 800 ms crossfade between styles (render both and blend).
  - Give night its own variant per theme (key or mode change, sparser groove), not just −6% tempo.
  - Add event themes: Rust Moon, Dark Sun, Scavenger Raid, pirates, Precursor Probes, pillars, Colony Festival.
  - Give each boss tier its own style (at least easy, hard and final).
  - Add underground variants for every biome, and themes for space or sky islands, the ocean and rain.
  - Optionally load recorded tracks (`Assets/Music/<theme>.wav`) the way `LoadRecordedSounds` loads effects.
- **Files:** `UI/AudioEngine.cs` `MusicState`, `UI/AudioDirector.cs` (theme choice from `game.EventProgress` and biome or depth), `Systems/GameSimulation.Leisure.cs` (deck theme list).
- **Size:** L
- **Depends on:** the events area (event ids); the user's music direction (open question 3).
- **Acceptance:**
  - `--music-preview` renders every theme, including the new event, boss-tier and night variants.
  - A self-test maps each event id and each boss tier to a theme name.
  - In play, walking from grassland into frozen fades over at least 0.5 s with no click.

### PE-7: Block dust and death gore
- **Change:**
  - Add a material-coloured dust emitter on every mining hit (2–4 particles) and on break (8–12), with colour sampled from the tile sprite's average. Cache it per tile the way `FootColor` does.
  - Add per-creature death chunks: 3–5 sprite fragments cut from the creature's idle frame, with gravity and a fade.
- **Files:** `UI/ActorAnimator.cs` (a new `TileDust` and gibs), `UI/GraphicalSandbox.cs` (subscribe to the sound and dust events from PE-4).
- **Size:** M
- **Depends on:** PE-4 (the event queue).
- **Acceptance:** a play-test shows dust when mining dirt, stone and ore in their distinct colours. The particle cap of 700 holds under a drill held for 10 s (F10 "effects" under 1 ms).

### PE-8: Remove tile seams
- **Change:**
  - Find the source. Candidates: (a) generated tile textures with dark 1 px borders, so check `tile_*` edge rows; (b) `TileArt.ShapeBody` outlines on sides it wrongly marks as exposed; (c) `PixelOffsetMode.Half` with the lightmap blit, which gives chunk-boundary lines.
  - Fix it, either by trimming or wrapping the texture edges in `gen_textures.py`, or by drawing chunks with `PixelOffsetMode.None` and integer offsets.
- **Files:** `UI/TileArt.cs`, `UI/GraphicalSandbox.Chunks.cs`, `UI/LightMap.cs` `Draw`, `tools/comfy/gen_textures.py`.
- **Size:** S–M
- **Depends on:** the world and tiles area (texture regeneration).
- **Acceptance:** a screenshot at 100% and 150% zoom over flat dirt and stone shows no line of pixels darker than 15% below its neighbours at tile or 480 px boundaries. A scripted pixel probe on a capture is enough.

### PE-9: Liquid hitch and liquid look
- **Change:**
  - Cap liquid work per tick: spread the active set over ticks with a budget of about 2 ms, and carry the rest over.
  - Draw the liquid surface with a smoothed top edge (average neighbour depths), translucency over blocks, a slow surface shimmer, and falling-liquid columns as waterfalls.
- **Files:** `World/ActiveLiquidSimulator.cs`, `UI/GraphicalSandbox.cs` live liquid pass (move it to a new `GraphicalSandbox.Liquids.cs`).
- **Size:** M
- **Depends on:** the world and tiles area (liquid simulator owner).
- **Acceptance:** `--engine-bench` reports a flood worst tick of at most 33 ms (now 794 ms) with the same final settled state. A play-test of a lake surface shows no stair-steps.

### PE-10: Event skies and coloured sky light
- **Change:**
  - `SkyLightLevel` becomes `SkyLight` (an RGB value): warm at dawn and dusk, cool at night, red tint in Rust Moon, near-black with a corona in Dark Sun, and pillar tints.
  - `LightMap.Compute` takes the colour.
  - `DrawSky` blends event gradients.
  - The sun sprite gets an eclipse overlay.
  - Biome background changes cross-fade over about 1 s.
- **Files:** `Systems/GameSimulation.World.cs` `SkyLightLevel`, `UI/LightMap.cs`, `UI/GraphicalSandbox.cs` `DrawSky`, `UI/GraphicalSandbox.Sky.cs`.
- **Size:** M
- **Depends on:** the events area (event flags).
- **Acceptance:** screenshots at 06:00, 12:00, 19:00, during Rust Moon and during Dark Sun show distinct sky and terrain tints. A self-test asserts that `SkyLight` is not grey during Rust Moon.

### PE-11: Truly coloured lighting (High quality)
- **Change:** in High, present the light as a multiply layer (per-channel tint of the scene) instead of darkness plus a capped glow tint. Keep Standard as it is for speed.
- **Files:** `UI/LightMap.cs` `Present` and `Draw`.
- **Size:** M
- **Depends on:** PE-10.
- **Acceptance:** a blue torch beside a red torch in a dark cave shows blue and red pools on the stone in a screenshot. The High light draw is at most 3 ms in Release at 1280×720.

### PE-12: Use the unused player frames, and add creature hurt frames
- **Change:**
  - Wire up `anim_climb_*` (ropes and ladders), `anim_sit_*` (chairs and mounts), `anim_mine_*` (pick, drill and axe use), `anim_slash_*` (melee) and `anim_aim` (guns and bows) in `AnimatedPose`.
  - For creatures, add a hurt pose: one frame of `attack_1` with the flash.
  - Scale the move frame rate with speed.
- **Files:** `UI/ActorAnimator.cs`, `UI/SpriteAtlas.cs` `AnimatedFrame` and `EnemyFrame`.
- **Size:** S
- **Depends on:** the controls area (climb and sit states).
- **Acceptance:** play-tests on a rope, sitting in a chair and mining show the matching frames. A self-test asserts that `AnimatedPose` returns `anim_climb_*` when `motion.Climbing`.

### PE-13: Weather polish
- **Change:**
  - Rain and snow skip columns whose sky stop is above the camera (reuse `LightMap.SkyStop`). Draw splashes on surfaces.
  - Wind sway for trees, grass fringes and vines: a shear by wind × sin(time + x).
  - Particles for sandstorms and blizzards.
  - Twinkling stars with slow rotation, and shooting stars during Starfall.
  - Cloud parallax with the camera.
- **Files:** `UI/GraphicalSandbox.cs` `DrawWeather` and `DrawSky`, `UI/GraphicalSandbox.Sky.cs`, `UI/GraphicalSandbox.Chunks.cs` (sway drawn live for flora).
- **Size:** M
- **Depends on:** the world and flora area.
- **Acceptance:** a screenshot inside a roofed house during rain shows no streaks indoors. Windy-day trees visibly sway.

### PE-14: Combat text parity
- **Change:**
  - The simulation exposes hit records `(target, amount, crit, kind: direct/dot/heal/charge)`.
  - Crits draw larger and orange. Damage over time draws smaller and dimmer, with no hit sound. Charge (mana) gains show blue.
  - Text stacks for rapid hits.
- **Files:** `UI/ActorAnimator.cs`, `Systems/GameSimulation.Combat.cs`.
- **Size:** S
- **Depends on:** the combat area.
- **Acceptance:** a self-test triggers a forced crit and asserts the crit record. A play-test shows orange crit numbers.

### PE-15: Audio latency and pitch variance
- **Change:**
  - Cut the queued audio: 3 × 1024-frame buffers (about 70 ms), or move to WASAPI shared mode.
  - Add ±5% resampled pitch variation for repeated sounds (hit, dig, swing).
  - Add voice stealing by priority, not oldest-first.
- **Files:** `UI/AudioEngine.cs`.
- **Size:** S–M
- **Depends on:** none.
- **Acceptance:** a loopback measurement of `Play("click")` to output start is at most 80 ms. No dropouts at 60 FPS under `--quickstart --size Large`.

### PE-16: Screen shake and camera options
- **Change:**
  - Add a decaying screen shake for boss roars, explosions and heavy landings, with an Options toggle (accessibility).
  - Keep the eased camera, and expose a "Camera smoothing" slider where 0 snaps like Terraria.
- **Files:** `UI/GraphicalSandbox.cs` `DrawWorld` camera block, `UI/PreferencesAndProfiles.cs`, `UI/MenuSystem.cs`.
- **Size:** S
- **Depends on:** the controls and UI agent (who is editing these files now).
- **Acceptance:** a play-test with an explosive shows a short shake that the toggle turns off. With the slider at 0, the camera centre equals the player centre every frame (assertion in a probe).

### PE-17: Ambient sound beds
- **Change:**
  - Synthesised loops per context: wind on the surface (scaled by `Weather.Wind`), birds by day, crickets at night, cave drips underground, a lava rumble near uranium, and a waterfall near falling liquid.
  - All on the Ambient volume.
- **Files:** `UI/AudioEngine.cs` (ambient voices), `UI/AudioDirector.cs`.
- **Size:** M
- **Depends on:** PE-15 (mixer headroom).
- **Acceptance:** `--music-preview`, or a new `--ambient-preview`, renders each bed to WAV. A play-test hears them change from day to night and from surface to cave.

### PE-18: Per-creature and voice sounds
- **Change:**
  - Give each creature archetype a sound family for hit and death (chitin, flesh, metal or drone, gel, crystal, boss), each with a synth fallback and optional recorded takes `hit_metal_2.wav` and so on.
  - Player hurt and death grunts with variants. Optional town-NPC talk blips.
- **Files:** `Entities/Actors.cs` (archetype sound family), `UI/AudioDirector.cs`, `UI/AudioEngine.cs`, `Assets/Sounds/`.
- **Size:** M
- **Depends on:** the creatures area; open question 5 (voice).
- **Acceptance:** a self-test asserts that every `EnemyKind` maps to a sound family. A play-test hears a drone and a gel sound different when hit.

### PE-19: Per-biome underground backdrops with parallax
- **Change:**
  - Pick the cave-band wall art by biome (frozen ice, junk, canopy mud, fungal, crystal), not only by depth.
  - Scroll the backdrop at about 0.85 of the camera instead of locking it to tiles.
- **Files:** `UI/GraphicalSandbox.World.cs` `DrawDepthBackdrop`, `UI/GraphicalSandbox.Chunks.cs` (move the backdrop out of the chunks into its own parallax pass).
- **Size:** M
- **Depends on:** the world area (biome at depth); art (wall textures exist).
- **Acceptance:** screenshots underground in Frozen and Fungal show different backdrops. The backdrop visibly lags the terrain when walking.

### PE-20: CI asset and performance gates
- **Change:** add a Prove matrix entry, `--asset-check`. It asserts that every sprite the game can request (items, tiles, flora, actors and their frames, backgrounds) exists in `git ls-files`, and that no generated frame is over its size budget. Add an `--engine-bench --only Small` job with soft budgets: Small generation at most 8 s, tick at most 5 ms, flood worst at most 50 ms. Upload the output.
- **Files:** `.github/workflows/prove.yml`, `Systems/EngineBenchmark.cs`, a new `Systems/AssetCheck.cs`, `Program.cs` (flag).
- **Size:** S
- **Depends on:** PE-1, PE-2, PE-9.
- **Acceptance:** the CI run fails when a frame PNG is deleted from git, and shows bench numbers in the step summary.

### PE-21 (optional): Sign the release
- **Change:** add Authenticode signing in `publish.yml`, with the certificate in repository secrets.
- **Size:** S
- **Depends on:** the user obtaining a certificate.
- **Acceptance:** the downloaded zip's exe shows a publisher in its properties.

---

## Cross-area dependencies

| Other area | What this area needs from it | Steps |
|---|---|---|
| Items, gear and equipment | Worn-layer flags per armor item; the vanity set list; dye slot semantics per piece | PE-3 |
| Mining, tools and tiles | A block-broken event with material; hit events for axe, hammer and drill; texture regeneration for the seam fix; liquid simulator budget | PE-4, PE-7, PE-8, PE-9 |
| Combat | Hit records with crit and kind; kill vs despawn reason | PE-5, PE-14 |
| Creatures and spawning | Despawn records; an archetype sound family; hurt-state frames | PE-5, PE-12, PE-18 |
| Events and weather | Event ids for music and sky; wind and storm values for sway and particles | PE-6, PE-10, PE-13 |
| Controls and UI (being edited now) | Climb and sit states exposed; inventory, chest and door UI events for sounds; camera and shake options in menus. `UI/GraphicalSandbox.cs`, `UI/MenuSystem.cs` and `UI/PreferencesAndProfiles.cs` are shared, so sequence the edits after that agent lands | PE-4, PE-12, PE-16 |
| Art pipeline (ComfyUI) | Worn-armor sheets; downscaled frames; tile textures without borders | PE-2, PE-3, PE-8 |
| Crafting | A successful-craft event (and stop `pickup` firing for it) | PE-4 |

## Open questions for the user

1. **Commit the generated animation frames now?** That is 2,448 PNGs (26.6 MB) for 306 creatures, already on disk, finished after the 2026-10-22 pause note. Doing it ships them in the next release. Without it, releases keep static creatures. Should `docs/QUEUED_WORK.md` be marked done?
2. **Memory budget.** "Leave ~490 MB" was measured on the old sprite set. With all the art loaded eagerly, the title screen alone is about 800 MB. Is a lazy-loading pass (PE-2) acceptable now, or should it still wait?
3. **Music direction.** Keep the procedural synthesiser and add event, night and boss variants (PE-6)? Or add recorded or composed tracks per theme (loaded like the recorded effects)?
4. **Armor visuals.** Generated per-frame overlays (large art job, best fidelity), or a simpler anchor-based "helmet, chest plate, boots" sprite per piece (fast, less faithful)?
5. **Voice sounds.** Do you want player grunts and creature voices? Should they come from your recorder (`tools/voice-recorder`, as for the current effects) or be synthesised?
6. **Camera.** Is the eased follow camera a deliberate Rimtera choice? Terraria's camera snaps to the player. Should a "snap" option and screen shake (with an off switch) be added?
7. **Jump and land sounds.** Terraria has none for a normal jump or landing. Keep Rimtera's?
8. **Code signing** for releases: wanted, and is a certificate available?
