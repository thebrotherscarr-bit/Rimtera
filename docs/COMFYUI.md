# Generating art with ComfyUI

Rimtera's sprites can be generated locally with your Comfy Desktop install (`tools\comfy-app\`), using the Qwen-Image 2.1 model already downloaded into the Desktop app's shared models folder.

## Start the server
```powershell
tools\comfy\start_comfy.ps1
```
This starts ComfyUI headless on http://127.0.0.1:8188, using the Desktop app's model paths. The log is written to `%TEMP%\rimtera-comfy.log.err`. It is the same ComfyUI the Desktop app runs, so you can also open that address in a browser.

### Fixes this setup needs on your Radeon RX 6800 XT
| Problem | Fix |
|---|---|
| hipBLASLt does not support RDNA2 (gfx1030), so sampling failed with `HIPBLAS_STATUS_INVALID_VALUE`. | The script sets `TORCH_BLAS_PREFER_HIPBLASLT=0` so PyTorch uses rocBLAS. |
| The int8 ("convrot") model multiplies its weights with `torch._int_mm`, which only exists through hipBLASLt on ROCm. | `tools\comfy-app\ComfyUI\custom_nodes\rocm_int8_fallback.py` switches those multiplies to float32 (exact for int8 inputs; float32 is also the fastest path on this card). Delete that file to undo. |
| Dynamic VRAM streamed weights from RAM on every step, because the model (7.2 GB) and text encoder (8.9 GB) don't both fit in 16 GB. That made steps take 30–240 s. | The server runs with `--disable-dynamic-vram --disable-fast-disk`, so the text encoder is unloaded while the diffusion model samples fully resident: about 12 s per step. |

## Generate sprites
1. **Make batches.** This writes 16-item batch files to `tools/comfy/batches/` from `docs/ARSENAL_SPRITES.txt`:
   ```bash
   python tools/comfy/make_batches.py
   ```
   To make only some sections, pass their prefixes, e.g. `make_batches.py wings accessory`.
2. **Generate one sheet:**
   ```bash
   python tools/comfy/gen_sheet.py tools/comfy/batches/wings_1.json
   ```
3. **Generate everything** (resumable; finished sheets are skipped):
   ```bash
   python tools/comfy/run_all.py
   ```

Each batch becomes one 1024×1024 sheet with a 4×4 grid of distinct objects on flat magenta, in the shared Neo-Terra style. It takes about 4–5 minutes per sheet (16 sprites).
- Raw sheets are kept in `art/generated/`.
- Each cell is turned into a game sprite by `tools/comfy/sprite_post.py`:
  - the background is flood-filled out
  - the sprite is trimmed and box-downscaled to 48 px (64 px for long weapons and wings)
  - colours are reduced to a 24-colour palette and alpha is made hard
- The result is saved as `Assets/Sprites/<name>.png`. Any art it replaces is moved to `art/replaced/`, so it can be restored.

To write your own batch, make a JSON file like this:
```json
{"name": "my_batch", "size": 48, "items": [["sprite_name", "Display name: what it looks like, colours."]]}
```
Rebuild or publish the game afterwards, since sprites are copied to the output folder at build time.

## Animation frames
Every animated character (enemies, bosses, critters, town NPCs, crew, pets) can get generated frames.

1. **List the characters:**
   ```bash
   dotnet run -- --entity-sprites > tools/comfy/anim_entities.tsv
   ```
   This lists each character's base sprite, how it moves (walk, fly, hop, float) and its attack type (melee, ranged, none).
2. **Run the generator** (resumable):
   ```bash
   python tools/comfy/gen_anim.py
   ```
   Options: `--only enemy,boss`, `--limit N`, `--force`.

For each character, the base sprite is the reference image in a ComfyUI edit job that returns a 4×2 sheet of that same character:
- 2 idle frames
- 4 movement frames (walk, flap, hop or drift, by its movement type)
- 2 action frames: an attack (melee strike or ranged shot), or a gesture for NPCs and crew

Every frame is scaled by one factor to the base sprite's height, with its feet on the bottom row, and saved as `<base>_idle_1..2`, `<base>_move_1..4` and `<base>_attack_1..2`. The game picks these up automatically (`SpriteCatalog.AnimatedFrame`); a character without frames keeps its still sprite.

**Cost:** about 4 minutes per character, including a server restart each time. All 225 take about 14–15 hours.

**Resuming:** characters that already have `_move_1` are skipped, so stopping and restarting continues where it left off. To redo one character, delete its `_idle_*`, `_move_*` and `_attack_*` files (or pass `--force`). The 7 characters with hand-drawn frames are never replaced. Raw sheets and references are kept in `art/generated/anim/`.

## Block textures (beauty pass)
`python tools/comfy/gen_textures.py` regenerates the block textures listed by `dotnet run -- --tile-sprites` (`tools/comfy/tile_textures.tsv`) as seamless pixel-art tiles:
- 16 per sheet
- each cell made to wrap seamlessly
- downscaled to 32 px, with a contrast lift
- ores are not generated: each is composed from the new rock texture plus the bright mineral pixels of its old texture, so ores match their rock

Done textures are listed in `art/generated/textures/done.txt`. The game draws them with shaped edges, rounded corners and soft-to-hard material transitions (`UI/TileArt.cs`).

## Environment art
`python tools/comfy/gen_env.py` generates:
- sky: `sky_cloud_1..6`, `sky_sun`, `sky_moon` (the moon is shaded to its phase in game)
- weather and ambient particles: `fx_*`
- parallax panoramas: `bg_<theme>_far` and `bg_<theme>_near` for ten surface themes, cut out so the game's sky shows through
- every flora decoration from `dotnet run -- --flora-sprites`

The renderer uses each piece once it exists and falls back to the procedural sky otherwise.

## Sharing the GPU
`start_comfy.ps1` keeps 4 GB of VRAM free for other programs (`--reserve-vram`) and runs the server at below-normal priority. This lets video and browsing stay smooth while art generates. Two more options:
- `overnight.py --pause SECONDS` rests the GPU after every image.
- For full speed: `start_comfy.ps1 -ReserveVramGB 0 -Priority Normal`, and no `--pause`.

## Overnight run
`python tools/comfy/overnight.py` refreshes the lists, then runs textures, then environment art, then animations, all resumable. Rebuild or publish afterwards.

## Original art sheets index
Double-click `tools\comfy\run_sheet_index.cmd` to index the original labelled art sheets in `art\sheets\`. It does this:
1. Starts ComfyUI.
2. Asks the Qwen3-VL vision model on each sheet for every labelled picture and where it is.
3. Cuts each picture out, without its label, to `art\generated\sheet_index\<sheet>\<label>.png`.
4. Lists them all in `tools\comfy\sheet_index.tsv` and stops ComfyUI.

To check the cut-outs, open `art\generated\sheet_index\<sheet>_review.png`, which shows each cut-out with its label.

It takes about a minute per sheet. Finished sheets are skipped, so you can close the window any time and carry on later. Pass `--restricted` to leave 4 GB of VRAM free, or `--redo` to index every sheet again. The beauty pass uses the index as the design and style reference for any sprite whose name matches a label.

## Flows in the ComfyUI app
To make art or audio by hand, open the Workflows sidebar (W) in the ComfyUI app and use the **Rimtera** folder. Install or refresh the flows with:
```bash
tools/comfy-app/standalone-env/python.exe tools/comfy/app_flows.py
```
| Flow | What it makes | Output (in ComfyUI's output folder) |
|---|---|---|
| Rimtera Art - Item sheet | 16 item icons in a 4×4 grid (the `gen_sheet.py` graph) | `rimtera/items` |
| Rimtera Art - Animation sheet | an 8-frame sheet of the character in Load Image (the `gen_anim.py` graph) | `rimtera/anim` |
| Rimtera Art - Background layer | a 1024×512 parallax layer (`gen_env.py`) | `rimtera/env` |
| Rimtera Art - Terrain textures | 16 seamless textures (`gen_textures.py`) | `rimtera/textures` |
| Rimtera Audio - Music track | one ACE-Step 1.5 track (`gen_music.py`) | `rimtera/music` |
| Rimtera Audio - Sound effect | one MOSS-SoundEffect clip (`gen_sfx.py`) | `rimtera/sfx` |
| Rimtera Audio - Ambience loop | one MOSS ambience loop, up to 30 s | `rimtera/ambience` |
| Rimtera Art - Redraw character | one character redrawn from its old sprite and an original sheet's style, finished at its game size | `rimtera/redrawn` |
| Rimtera Art - Redraw item | one item icon redrawn the same way (64 px, 96 for weapons and tools) | `rimtera/redrawn` |
| Rimtera Art - Re-animate character | 8 frames drawn over the finished sprite, matched to it | `rimtera/anim` |
| Rimtera Art - Index one sheet | every labelled picture on one original art sheet, cut out | `rimtera/sheet_index` |
| Rimtera Check - Sprite matches name | the model's blind description of a sprite, then YES or NO against its name | shown in the flow |
| Rimtera Audio - Finish clip | any clip levelled, trimmed or looped for its cue, optionally added to the game | shown in the flow; `Assets/` with install on |
| Rimtera UI - Panel frame | an empty riveted menu or window panel, 96×96, for 9-slice stretching | `rimtera/ui` |
| Rimtera UI - Button states | a menu button with hover, pressed and disabled versions, 160×32 | `rimtera/ui` |
| Rimtera UI - Slots | inventory, selected and equipment slots, 40×40 | `rimtera/ui` |
| Rimtera UI - Bar frame | an empty health, energy or boss bar frame, 200×20 | `rimtera/ui` |
| Rimtera UI - Icon sheet | 16 menu and HUD icons, 32×32 each | `rimtera/ui` |
| Rimtera UI - Title logo | the RIMTERA logo, up to 480×200 | `rimtera/ui` |
| Rimtera UI - Title backdrop | the title screen scene as a 480×270 pixel-art backdrop | `rimtera/ui` |
| Rimtera UI - Cursor | the mouse cursor, 24×24 | `rimtera/ui` |

Each flow has a "How to use" note. The prompts are filled in with real examples from the overnight runs, and the flows open with sample inputs (copied by `app_flows.py`), so they run as soon as they open. In the redraw flows, the name in the prompt decides what the sprite shows; the old sprite only lends its design where it fits that name.

The UI flows take their look from the original boss sheet, `sheet_00c_bosses` (riveted gunmetal panels with amber-gold headers and title). The game still draws its menus, panels, slots and bars in code, so UI art made here is not used until the renderer is changed to draw it.

The overnight runs are not flows: on this card ComfyUI has to restart between jobs (the second job on one server slows from 12 s to over 180 s per step), and a flow cannot restart its own server. They stay one-click launchers.

The sound flows use the **Rimtera MOSS Sound Effect** node (`tools/comfy/nodes/rimtera_nodes.py`), which `app_flows.py` installs as a shim in `custom_nodes`. MOSS cannot load inside ComfyUI because it pins a different transformers, so the node unloads ComfyUI's models and runs `tools/audio/moss_worker.py` in the MOSS venv. The model loads each run (about a minute for the first clip). The node's `power` setting is `focused` (about 80% of the machine) or `restricted` (4 GB of VRAM left free).

The sprite flows finish their output with the Rimtera sprite nodes (`tools/comfy/beauty_core.py`): cut from the grid by connected shapes, box-downscaled to 40 px per tile, hard alpha, a small palette and a dark outline. The older four art flows still make raw sheets only.

## Music preview
`dotnet run -- --music-preview art/music-preview` renders 20 s of every music theme to WAV (calm, then tense), so the generative score can be auditioned.

## Modly
Modly's models (Hunyuan3D, TRELLIS 2, TripoSplat in `Desktop\Archive\models`) turn images into 3D models. They aren't used for Rimtera's 2D sprites yet. One possible use is rendering a generated character in several poses to get consistent animation frames.
