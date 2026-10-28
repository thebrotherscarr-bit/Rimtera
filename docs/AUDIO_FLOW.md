# Audio flow: expanding Rimtera's audioscape

The audio companion to `docs/COMFYUI.md`: one pipeline that turns the game's audio NEEDS into finished, levelled, named files the game loads. It covers weapon and tool sounds, character and creature sounds, UI, ambience and music.

Status: **planned.** The recording tools exist; the generation scripts, the needs export and the engine hooks below are still to be built. Nothing here runs until the user says go.

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
   - **Sound effects and ambience:** a text-to-audio model (Stable Audio Open 1.0 is natively supported by ComfyUI: up to about 47 s per clip, 44.1 kHz stereo). It needs `stable-audio-open-1.0.safetensors` in `models/checkpoints` and the `t5-base` text encoder in `models/text_encoders`.
   - **Music:** a text-to-music model (ACE-Step is natively supported by ComfyUI: minutes-long instrumental tracks from tags, plus a lyrics field to leave empty). It needs its checkpoint in `models/checkpoints`.
   - Neither model is installed yet (`models/audio_encoders` is empty). The user chooses which to download and accepts their licences. Verify both on the Radeon RX 6800 XT (ROCm) with one test clip before any batch, as the image models needed fixes on first run.
   - Prompts follow the game's style card for sound: "sci-fi frontier, warm analog synth plus orchestral strings, clean and punchy, not harsh". Each category below has its own prompt template.
4. **Synthesise (the existing procedural engine).** Always-available fallback for every cue, plus anything that should stay generative: the music engine's themes, short UI blips, stingers. A cue with no file still plays its synth version.

## The needs map (what the game needs to sound like)
One table, `tools/audio/audio_needs.tsv`, generated from the game, like `--tile-sprites` and `--entity-sprites` for art. Planned export: `dotnet run -- --sound-cues`. Columns: `cue`, `category`, `trigger` (what in the game plays it), `variants_wanted`, `lane`, `prompt`, `loudness_offset`, `loop` (yes or no), `status` (missing, synth, generated, recorded, approved), and `terraria_equivalent` (the Terraria SoundID or track it maps to, for the later audio diff).

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
Planned scripts in `tools/audio/`, mirroring `tools/comfy/`:

1. **Export needs:** `dotnet run -- --sound-cues > tools/audio/audio_needs.tsv`. It keeps any status and lane choices already in the file and adds new cues as `missing`.
2. **Record pass (lane 1):** the Voice Booth gets a cue list loaded from `audio_needs.tsv`, filtered to `lane=record`, and shows what's missing. Takes save to `Assets/Sounds/` with the existing naming. Status becomes `recorded`.
3. **Edit pass (lane 2):** `python tools/sounds/aup4_export.py <project.aup4> out/ --split`, then rename the clips to cues with a small mapping file (`tools/audio/assign.py out/ mapping.tsv`).
4. **Generate pass (lane 3):**
   - `python tools/audio/gen_sfx.py` handles sound effects and stingers. Batches of short clips per cue come from the category prompt plus the cue's own words. It makes 2× the wanted variants, so you can keep the best.
   - `python tools/audio/gen_ambience.py` handles ambience loops. It makes 45 s clips, crossfades the end into the start for a seamless loop, and keeps tone consistent per biome with a shared prompt seed.
   - `python tools/audio/gen_music.py` handles music. It makes a track per needs row from the biome or event style card (tempo, key, mood, instrumentation, and the composer-style flavour the synth themes already use). Loop points are found by beat alignment.
   - All three reuse the ComfyUI client pattern of `tools/comfy/comfy_client.py`: restart the server per batch, reserve VRAM, run at below-normal priority, pause between jobs, and retry once on a dropped connection.
   - `python tools/audio/overnight_audio.py` runs sound effects → ambience → music, skips anything already done, and resumes where it stopped.
5. **Post-process:** `python tools/audio/audio_post.py`, run by every lane.
   - Trim silence, short fades, and DC removal.
   - Sound effects become mono and ambience and music stay stereo, all at 44.1 kHz 16-bit WAV.
   - Loudness levelled to −16 LUFS plus the category offset (UI quieter, booms louder), matching the levelling the Voice Booth already does.
   - Peaks limited to −1 dBTP.
   - Loops checked for clicks at the seam.
   - Writes `Assets/Sounds/<cue>[_n].wav` for effects, `Assets/Music/<track>.wav` for music, and `Assets/Ambience/<loop>.wav` for loops.
6. **Audition:** `tools/audio/audition.html`, a sibling of the Voice Booth.
   - It plays every candidate take next to the current one, grouped by category.
   - Keep, reject or re-roll each take with one click.
   - Only kept takes stay; status becomes `approved`.
   - Your recorded takes always outrank generated ones for the same cue.
7. **Rebuild or publish** so the files are copied into the build. The `.csproj` already includes `Assets\Sounds\*.wav`; the new `Music/` and `Ambience/` folders need adding.

## Engine work the new sounds need
These are already planned in the roadmap (PE-4, PE-5, PE-6, S1–S26 in breakdown 09). The audio flow depends on them; it doesn't replace them.
- **Sound events from the simulation.** A `SoundEvents` queue with a position for each event, instead of inferring sounds from cooldowns and inventory changes. Every trigger in the needs map emits its cue.
- **Mixer.**
  - Pitch variation per play (about ±5–10%, as in Terraria).
  - Distance falloff and pan for every world sound.
  - Category buses that follow the Sound, Music and Ambient sliders.
  - Smaller output buffers so sounds land on time.
- **Fallback chain:** a specific cue (`use_bow`) falls back to its family (`shoot`) and then to the synth. Every new cue works before its file exists.
- **Ambience layer.** Crossfaded loops picked by biome, layer, time and weather, plus positional spot loops (waterfalls, uranium, machinery).
- **Music player.**
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
