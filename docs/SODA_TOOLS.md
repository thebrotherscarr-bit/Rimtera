# The Soda tools: how Rimtera's art and audio are made

Rimtera's art and audio are made locally on the owner's PC with two sibling apps, **SodaPixel** (art and 3D models)
and **Sodaudio** (music, sound effects and ambience). They replace Comfy Desktop and Modly, and Rimtera's old
`tools/comfy` scripts (retired). Everything runs on one GPU (16 GB AMD, ROCm), on 127.0.0.1 only, with no cloud
service.

**The owner decides when the GPU runs.** The team plans, requests and integrates assets; it does not start art or
audio runs on its own (Sodaudio's MCP asks the owner for anything big, see below; SodaPixel jobs are queued only
when the owner asks).

## The machine

| Part | Where | Port |
|---|---|---|
| SodaPixel engine and UI | `C:\Users\novad\Desktop\SodaPixel` (`start.cmd`, or the installed desktop app) | 8771 |
| Sodaudio engine and UI | `C:\Users\novad\Desktop\Sodaudio` (`start.cmd`, or the installed desktop app) | 8770 |
| ComfyUI (shared install) | `Rimtera/tools/comfy-app` (not part of the game; never install nodes there) | 8188 |
| Ollama (local language models) | the owner's rack; also uses the GPU | 11434 |

- **One GPU at a time.** SodaPixel and Sodaudio share a lock (`%LOCALAPPDATA%\Soda\gpu.lock`): a night is art or
  audio, never both. The app holding the lock starts its own ComfyUI with its own start script and stops it when its
  run ends. A big local Ollama model loaded at the same time competes for VRAM.
- **Each app keeps its own things:**
  - SodaPixel: models in `SodaPixel\models` (Qwen-Image 2.1 int8 for images, Qwen3-VL 8B for text and the name
    check, and the 3D models), ComfyUI input/output in `SodaPixel\comfy\`, the old Rimtera art and UI workflows in
    `SodaPixel\comfy\workflows\`, its start script and model paths in `%APPDATA%\SodaPixel\comfy\`.
  - Sodaudio: models in `Sodaudio\models` (ACE-Step 1.5 turbo for music, MOSS-SoundEffect v2.0 for effects and
    ambience), ComfyUI input/output in `Sodaudio\comfy\`, the old Rimtera audio workflows in
    `Sodaudio\comfy\workflows\`, its start script and model paths in `%APPDATA%\Sodaudio\comfy\`.

## SodaPixel (art, 3D)

Rimtera's project: `SodaPixel/examples/rimtera/art-project.json` (style sheets, anchors, palette, pixel rules:
40 art px per tile, item icons 64 px, weapons and tools 96 px, one-pixel outline, magenta generation background;
importers for the game's asset lists). SodaPixel works on its own copy in `%APPDATA%\SodaPixel\projects\rimtera`;
its workspace (`art-work/`) holds every take, the replaced originals and the install log.

Screens and what they do:
- **Create:** any image from a prompt at full quality (1024², 20 steps), 1 to 4 per take, optional reference images.
- **Model:** a picture to a 3D mesh (`.glb`) with a recipe: Image to Model (Hunyuan3D Mini, remesh, smooth),
  TripoSplat baked, TripoSplat projection, GigaChad (Trellis.2, refine, optimise; downloads Trellis.2 the first
  time), or one model alone. Results are a gallery per project.
- **Explore:** quick small samples against the style sheets and palette; keep or promote to the style.
- **Assets:** the game's asset checklist (has art, refined, animated, name check) and the overnight plan.
- **Overnight lanes:** refine characters (4 per sheet), animate (8 frames: idle 2, move 4, attack 2), refine items
  (16 per sheet). Each sprite is cut, finished to the exact game size and palette, **name-checked** (described by
  Qwen3-VL, then judged against its name; three tries, then flagged), and **installed into `Assets/Sprites/`**
  automatically. The first original is kept in the workspace's `replaced/`, every install is logged, and any install
  can be undone. Hand-made art is never replaced.

Its HTTP API (`http://127.0.0.1:8771/api`): read freely (`/health`, `/assets`, `/overnight`, `/create`, `/model`,
`/model/kit`, `/jobs`, `/setup/check`); queue work (`POST /create`, `/explore`, `/model`, `/overnight`,
`/assets/{id}/regenerate`) only when the owner asks.

## Sodaudio (music, effects, ambience)

Rimtera's project: `Sodaudio/examples/rimtera/audio-project.json` (the house sound, the monk choir and the
Rim-te-ra chant shapes, the title and final-boss plans, output folders: effects `Assets/Sounds` as WAV, music and
ambience as OGG). Installs from it go into this repo's `Assets/`.

The pipeline: **import cues** (runs `dotnet run -- --sound-cues`, which writes `tools/audio/audio_needs.tsv`) →
**generate takes** (music with ACE-Step in ComfyUI; effects and ambience with MOSS) → **post-process** (trim, loop
crossfade, loudness to the project's LUFS) → **audition** (in the app) → **install** (never overwrites; the owner's
recordings always win; logged). For music: **map** a take (beats, bars, loudness, choir band, drums), then **cut a
loop** or **recompose** on bar lines. The game loops 30 to 40 s of each track.

**opencode can drive Sodaudio through MCP** (the `sodaudio` server in `opencode.json`). It forwards every call to the
running Sodaudio engine, so Sodaudio must be open. Tools: `list_cues`, `get_cue`, `edit_cue`, `add_cue`,
`edit_style`, `import_cues`, `queue_generate`, `queue_variations`, `list_jobs`, `cancel_job`, `get_takes`,
`map_track`, `cut_loop`, `recompose`, `promote`, `fill_gaps`.
- `promote` and `fill_gaps` (installs) always wait for the owner's **Allow** in the Sodaudio window, as do
  `edit_style`, `cancel_job`, `import_cues`, and any `queue_generate` over 5 music tracks or 30 clips.
- You cannot hear audio: judge a take only from `map_track` data, and say so. Use the measured tempo, never the
  cue's bpm. Map before cutting; cut on bar lines; no near-silent stretch in a loop. Sung parts use invented
  syllables only.

`tools/audio/` in this repo holds the scripts Sodaudio was ported from; `tools/audio/music_map.cmd` still maps any
track dropped on it. New audio work goes through Sodaudio.

## How a step gets its assets

1. The step's spec lists what it needs to see or hear.
2. `@art` and `@audio` add each item to `docs/ASSET_REQUESTS.md` (name, size or length, flow, prompt, style
   reference or voice) and to the catalogs (`UI/SoundCatalog.cs` for sound), with a kitbashed placeholder so
   `--asset-check` and `--sound-cues` stay clean.
3. The owner runs SodaPixel and Sodaudio. SodaPixel installs sprites itself after the name check; Sodaudio installs
   on the owner's Allow.
4. A later step wires the real asset in and removes the placeholder.
