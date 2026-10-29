# Audio flow: expanding Rimtera's audioscape

The audio companion to `docs/COMFYUI.md`: one pipeline that turns the game's audio NEEDS into finished, levelled, named files the game loads. It covers weapon and tool sounds, character and creature sounds, UI, ambience and music.

Status: **generators built and tested; the full overnight run waits for the user's go.** The needs export (`--sound-cues`, 527 cues), fallback playback, the specific triggers, both models and every script in `tools/audio/` exist. The streaming music and ambience players (engine work below) are still to be built, so generated music and ambience files are made and promoted but not yet played; the synth music and rain cover them until then.

## What exists today
| Piece | Where | What it does |
|---|---|---|
| Sound effects | `UI/AudioEngine.cs` (`_effects[...]`) | 18 synthesised cues: `swing`, `shoot`, `zap`, `hit`, `hurt`, `death`, `dig`, `place`, `break`, `jump`, `land`, `pickup`, `click`, `hover`, `thunder`, `boss`, `event`, `bite`. Some others (`craft`, `door`, `splash`, `explosion`, `coin`) are synthesised but nothing plays them |
| Recorded takes | `Assets/Sounds/<cue>.wav`, `<cue>_2.wav`, … | A recorded file replaces the synth cue of the same name; numbered takes join it, and a random take plays each time. Levelled to −16 LUFS plus per-cue offsets (`hover` −12, `click` −8, footstep-type cues −2, `boss`/`thunder`/`explosion` +3) |
| Voice Booth | `tools/voice-recorder/index.html` | Browser recorder: record takes per cue, trim, level, save straight into `Assets/Sounds` |
| Audacity exporter | `tools/sounds/aup4_export.py` | Unpacks `.aup4` projects without Audacity; `--split` cuts a long take into clips at silences, ready to rename into cues. Sources live in `Assets/Sounds/raw/` |
| Music | `UI/AudioEngine.cs` `MusicState` | Procedural synth: 14 themes (title, 10 biomes, underground, core, boss), styled after Vivaldi, Bach and Tchaikovsky. One boss theme for all bosses, no event music, night only slows the tempo |
| Ambience | `AudioEngine.Mix` | Rain noise only |
| Triggers | `UI/AudioDirector.cs` | About 20 triggers, inferred from cooldowns and inventory changes (Terraria has about 1,700 sound call sites; see `docs/breakdown/09_presentation_engine.md` S1–S26) |

## The four source lanes
Every cue gets its sound from one of four lanes. The needs map (below) records which lane each cue uses.

1. **Record (the Voice Booth).** Character voices (grunts, hurt, death, effort), foley you can perform (eating, drinking, cloth, footsteps on real surfaces), and anything you want in your own voice. This is the highest-priority lane: the user's recordings always win over generated takes.
2. **Edit (Audacity, then `aup4_export.py`).** Longer sessions recorded or designed in Audacity, cut into clips with `--split`. Use it for layered or designed sounds, and for music stems you play.
3. **Generate (ComfyUI audio models).** Text-to-audio for sound effects and ambience loops, and text-to-music for tracks. The same server and GPU-sharing rules as the art runs (`docs/COMFYUI.md`, [art runs share the GPU]):
   - **Free, open-source models only.** Stable Audio Open is excluded: its licence is not open source.
   - **Sound effects and ambience: MOSS-SoundEffect v2.0** (Apache 2.0; DiT + DAC VAE + Qwen3 text encoder, 48 kHz mono, up to 30 s per clip). Weights in `models/diffusers/MOSS-SoundEffect-v2.0` of the shared ComfyUI models folder; code in `Rimtera/vendor/MOSS-TTS`. It pins transformers 4.57, which ComfyUI's environment cannot take, so it runs beside ComfyUI in `Rimtera/vendor/moss-venv`, which borrows ComfyUI's ROCm torch through a `.pth` file. `tools/audio/setup_audio.ps1` rebuilds that venv. Tested on the RX 6800 XT: a 2 s effect takes about 25 s (the denoise window is sized to the clip; the default 30 s window took 404 s).
   - **Music: ACE-Step 1.5 turbo** (MIT), native ComfyUI nodes: `diffusion_models/acestep_v1.5_turbo.safetensors`, `text_encoders/qwen_0.6b_ace15.safetensors` + `qwen_1.7b_ace15.safetensors`, `vae/ace_1.5_vae.safetensors`. Instrumental (`[Instrumental]` lyrics), 8 steps. Tested: a 60 s track in 100 s. The decode is tiled (`VAEDecodeAudioTiled`): a whole-track decode asks MIOpen for 5 GB of workspace and crashes the server on gfx1030.
   - Prompts follow the game's style card for sound: "sci-fi frontier, warm analog synth plus orchestral strings, clean and punchy, not harsh". Each category below has its own prompt template.
4. **Synthesise (the existing procedural engine).** Always-available fallback for every cue, plus anything that should stay generative: the music engine's themes, short UI blips, stingers. A cue with no file still plays its synth version.

## The needs map (what the game needs to sound like)
One table, `tools/audio/audio_needs.tsv`, generated from the game by `dotnet run -- --sound-cues` (`UI/SoundCatalog.cs`), like `--tile-sprites` and `--entity-sprites` for art. Columns: `cue`, `category`, `trigger` (what in the game plays it), `variants`, `seconds`, `loop`, `offset` (loudness, dB from −16 LUFS), `lane`, `fallback`, `bpm`, `key`, `meter` (the music style card) and `prompt`. Status is not stored: the scripts read it from the files (a file in `Assets/` is in the game; one in `tools/audio/candidates/` is waiting for audition). The `terraria_equivalent` column is added by the later audio diff.

**Everything that needs a sound has one.** Every cue names a fallback cue, and every chain ends at one of the 23 synthesised effects (`AudioEngine.SynthesisedCues`). So `use_bow_heavy` → `use_bow` → `shoot` plays from day one and upgrades itself as files arrive. Only music, ambience, footsteps, idle calls and the heartbeat wait silently for their files. The self-test (`SoundCatalogSelfTests`) checks three things: every weapon style, tool, tile material, creature, boss, event and music theme has a cue; every chain ends at a real sound; and every music slot has a style card. Counts today: weapons 40, impacts 11, tools 16, tiles 36, interactions 41, UI 14, player 30, creatures 54, bosses 133, events 48, ambience 40, music 64.

**What the game plays now** (`UI/AudioDirector.cs`):
- the weapon style cue (`use_<style>`, with `_heavy` at tier 4 and up)
- the tool or item cue (`tool_drill`, `tool_chop`, `tool_hammer_wall`, `tool_net`, `eat`, `potion`, `equip`, `place` …)
- the creature's material impact, plus its family voice (`creature_<family>_<size>_hurt` and `_death`)
- each boss's own roar, hurt and death
- each event's start and end stinger

Recorded or promoted files load from `Assets/Sounds` and are levelled with the cue's catalog offset.

Categories, and how cues are derived from game data so new content gets sounds automatically:

| Category | Cues (pattern) | Derived from | Variants |
|---|---|---|---|
| **Weapons** | `use_<style>` for broadsword, shortsword, spear, whip, yo-yo, flail, boomerang, bow, gun, shotgun, launcher, beam, staff, tome, summon, sentry and thrown; heavy versions for tier 4 and up (`use_<style>_heavy`) | `WeaponStyle`, `ItemUseKind` and tier | 3–4 |
| **Impacts** | `impact_<material>` for flesh, carapace, metal, crystal, slime, stone, energy and spirit | creature archetype material and AI family | 3 |
| **Tools** | `tool_drill`, `tool_drill_loop` (channel drills), `tool_chop`, `tool_hammer`, `tool_hammer_wall` | `ItemUseKind` Pickaxe, Cutter and Demolisher; channel tools | 3 |
| **Tiles** | `break_<class>`, `place_<class>` for soil, stone, metal/hull, glass, wood/timber, plant, crystal, ice and sand | the tile class from `TileGrid` (hardness, organic, glass) | 3 |
| **Player** | `step_<surface>` per tile class, `jump`, `jump_double`, `land`, `hurt`, `death`, `breath_out`, `drown`, `eat`, `drink`, `grapple_fire`, `grapple_latch`, `wings_flap`, `rocket_boots`, `dash`, `mount_<kind>` | player systems and movement | 3–5 (voice cues 5+) |
| **Creatures** | `<family>_idle`, `<family>_hurt`, `<family>_death` per AI family and size (small, medium, large); `<boss>_roar`, `<boss>_attack`, `<boss>_death` for each of the 33 bosses; critter chirps by type | `EnemyKind` archetypes, AI families, boss list | 3 |
| **Items and world** | `pickup`, `coin`, `craft`, `potion`, `door_open`, `door_close`, `chest_open`, `chest_close`, `splash_<liquid>` (water, uranium, acid, fuel), `explosion`, `teleport`, `pylon`, `trap_<kind>`, `wire_switch` | use kinds and tile interactions | 2–3 |
| **UI** | `hover`, `click`, `open_inventory`, `close_inventory`, `tab`, `error`, `craft_ok`, `achievement`, `chat_tick`, `map_open` | the menu system and inventory hub | 2 |
| **Ambience (loops)** | `amb_<biome>_<day/night>`, `amb_<biome>_underground`, `amb_core` (irradiated core), `amb_space`; weather `amb_rain_light`, `amb_rain_heavy`, `amb_wind`, `amb_blizzard`, `amb_sandstorm`, `amb_acid_rain`; spot loops `amb_waterfall`, `amb_uranium_flow`, `amb_machinery` | `BiomeZone`, layers, weather, liquids | 1–2 loops of 30–60 s |
| **Event stingers** | `event_<kind>_start`, `event_<kind>_end` per `WorldEventKind`, `boss_spawn`, `fracture`, `level_up` | world events and progression | 1–2 |
| **Music** | `mus_<biome>_day`, `mus_<biome>_night`, `mus_<biome>_underground`, `mus_event_<kind>`, `mus_boss_<boss>` (or one per boss tier), `mus_title`, `mus_town`, `mus_space`, `mus_core` | biomes, events, bosses | 1 track each, 90–180 s, loopable |

The music list mirrors Terraria's structure: it picks from 63 tracks, by biome, layer, time of day, event and boss. The audio diff (roadmap step 4 of `docs/HANDOFF_WORKFLOW.md`) fills in the `terraria_equivalent` column and finds anything still missing.

## The pipeline
Scripts in `tools/audio/`, mirroring `tools/comfy/`.

**Overnight:** `python tools/audio/overnight_audio.py [--only effects|music|ambience] [--pause S] [--limit N] [--no-fill] [--restricted]`. **A night has one focus, art or audio, never both.** An audio night is resumable and works through:
1. effects: weapons, impacts, tools, tiles, interactions and UI first, then player, creatures, bosses and events
2. music: every biome, event and boss-tier slot
3. ambience: every biome, weather and spot loop
4. post-processing and the audition page
5. `promote.py --fill-gaps`, which gives every cue that has no file at all its first takes, never replacing anything

**Power:** a focused night (the default) gets the machine: normal priority, no rests, all but 1.5 GB of the VRAM (MOSS caps itself; ComfyUI starts with 1.5 GB reserved) and 80% of the CPU threads. A **restricted** run (`--restricted`) leaves 4 GB of VRAM free (below-normal priority, 10 s rests) so the PC stays usable. ComfyUI unloads its models (`/free`) before MOSS loads.

Rough timing at full power: about 1,300 effect takes at 15–25 s each, 64 tracks at about 4 minutes each and 40 ambience loops at a few minutes each, so everything takes two or three nights. `--only` runs a single lane, and each night continues where the last one stopped.

The steps as built:

1. **Export needs:** `dotnet run -- --sound-cues > tools/audio/audio_needs.tsv`. It keeps any status and lane choices already in the file and adds new cues as `missing`.
2. **Record pass (lane 1):** the Voice Booth gets a cue list loaded from `audio_needs.tsv`, filtered to `lane=record`, and shows what's missing. Takes save to `Assets/Sounds/` with the existing naming. Status becomes `recorded`.
3. **Edit pass (lane 2):** `python tools/sounds/aup4_export.py <project.aup4> out/ --split`, then rename the clips to cues with a small mapping file (`tools/audio/assign.py out/ mapping.tsv`).
4. **Generate pass (lane 3):**
   - `gen_sfx.py` (MOSS venv) handles sound effects, stingers and ambience loops (20 s).
     - It makes `variants` takes per cue (`--takes` to change), each with a stable seed, into `tools/audio/raw/<category>/<cue>__c<n>.wav`.
     - It runs through `moss_worker.py`, which loads the model once, runs at below-normal priority and carries on past a failed clip.
     - Cues that already have enough takes in the game are skipped.
   - `gen_music.py` (ComfyUI venv) handles music through `audio_client.py`.
     - It makes one 150 s track per slot from its style card (prompt, tempo, key, meter), into `raw/music/<slot>__c<n>.flac`.
     - It starts ComfyUI if needed (`tools/comfy/start_comfy.ps1`: 4 GB VRAM reserved, below-normal priority) and restarts it once on a dropped connection.
   - `RIMTERA_AUDIO_PAUSE` (set by `--pause`) rests the GPU between clips and tracks.
5. **Post-process:** `audio_post.py` (MOSS venv) writes into `tools/audio/candidates/<category>/`.
   - Trim silence, short fades, and DC removal.
   - Sound effects become mono and ambience and music stay stereo, all at 44.1 kHz 16-bit WAV.
   - Loudness levelled to −16 LUFS plus the category offset (UI quieter, booms louder), matching the levelling the Voice Booth already does.
   - Peaks limited to −1 dBTP.
   - Loops checked for clicks at the seam.
6. **Audition:** `make_audition.py` builds `tools/audio/candidates/audition.html`, which shows every candidate next to what the game plays now, grouped by category.
   - Tick the keepers, copy the list into a file, then run `promote.py --keep keep.txt`.
   - Add `--replace-generated` to swap out earlier generated takes. Recordings are never touched.
   - `promote.py` writes effects to `Assets/Sounds/<cue>[_n].wav`, music to `Assets/Music/<slot>.ogg` and ambience to `Assets/Ambience/<loop>.ogg` (Vorbis, because WAV music would add gigabytes).
   - Every promoted file is logged in `tools/audio/promoted.tsv`.
7. **Rebuild or publish** so the files are copied into the build. The `.csproj` copies `Assets\Sounds\*.wav`, `Assets\Music\*.ogg` and `Assets\Ambience\*.ogg`.

## Engine work the new sounds need
These are already planned in the roadmap (PE-4, PE-5, PE-6, S1–S26 in breakdown 09). The audio flow depends on them; it doesn't replace them.
- **Sound events from the simulation.** A `SoundEvents` queue with a position for each event, instead of inferring sounds from cooldowns and inventory changes. Every trigger in the needs map emits its cue.
- **Mixer.**
  - Pitch variation per play (about ±5–10%, as in Terraria).
  - Distance falloff and pan for every world sound.
  - Category buses that follow the Sound, Music and Ambient sliders.
  - Smaller output buffers so sounds land on time.
- **Fallback chain:** done. A specific cue (`use_bow`) falls back to its family (`shoot`) and then to the synth (`AudioEngine.Play` walks `SoundCatalog.Fallback`).
- **Ambience layer.** Crossfaded loops picked by biome, layer, time and weather, plus positional spot loops (waterfalls, uranium, machinery).
- **Music player** (to build next; it needs an OGG decoder such as NVorbis, MIT licence).
  - Streams recorded or generated tracks with crossfades, and picks the track by Terraria's priority order: boss, then event, then biome and layer, then time of day.
  - The procedural `MusicState` stays as the fallback for any slot without a file, and as an option in settings.
- **Despawns stay silent;** catches get their own sound (PE-5).

## Quality rules
- Your own recordings and assets always take precedence; the generator fills gaps and offers alternatives, never overwrites.
- No sound or music is taken from Terraria or any other game. Terraria's sound list is used only as a checklist of *which moments* need a sound.
- Consistent style per category: one prompt template per category, a seed family per biome, and the same reverb character per layer (dry on the surface, roomier underground, metallic in the core).
- Loudness and peak targets checked automatically in `audio_post.py`. A planned `--audio-audit` dev flag lists cues still on synth fallback, cues with fewer takes than wanted, and files off target.
- Attribution: none. All audio is the user's game content.

## Order of work (on the user's go)
1. Build `--sound-cues` and the needs export. Review the list with the user: cue names, lanes and variant counts.
2. Install and test the two ComfyUI audio models with one sound effect, one loop and one track.
3. Build `audio_post.py` and the audition page. Re-level the existing takes through them.
4. Engine hooks: the sound-event queue, mixer, fallback chain, ambience layer and music player. These are roadmap items and land in their version step.
5. Record pass for voices and foley; generation passes for the rest (overnight, GPU-sharing).
6. Audition, approve, rebuild. Then the audio diff against Terraria's cue list fills any remaining holes.
