"""Rimtera's ComfyUI nodes, loaded by the shim tools/comfy/app_flows.py installs in tools/comfy-app/ComfyUI/custom_nodes.

Rimtera MOSS Sound Effect: MOSS-SoundEffect v2.0 (Apache 2.0) as a node, so the effect and ambience flows run from the
ComfyUI app. MOSS pins transformers 4.57, which conflicts with ComfyUI's, so the node does not import it: it unloads
ComfyUI's models, runs tools/audio/moss_worker.py in tools/moss-venv on one job, and returns the clip as AUDIO
for Save Audio or Preview Audio. The model loads for every run (about half a minute), then each clip takes 15-30 s.

Sprite nodes for the beauty and size pass (tools/comfy/beauty_core.py does the work, as in beauty_pass.py):
- Rimtera Sprite On Magenta: a sprite (Load Image's image and mask) scaled up by a whole factor onto a magenta canvas,
  the reference a redraw keeps the design of.
- Rimtera Animation Template: a finished sprite 8 times in a 4x2 grid, the reference an animation sheet is drawn over.
- Rimtera Finish Sprites: cuts a generated grid and saves each cell as a finished game sprite (exact size, palette,
  outline, transparent PNG) in output/<prefix>.
- Rimtera Finish Animation: cuts a generated 4x2 sheet into the 8 frames matched to the base sprite and saves them as
  <prefix>_idle_1 .. _attack_2.
- Rimtera Cut Labelled Pictures: cuts the labelled pictures out of an original art sheet (art/sheets), from the boxes
  the vision model read off it.

Rimtera Finish Audio: levels, trims or loops a clip for a cue, and optionally adds it to the game (finish_one.py).
"""
import json
import os
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np
import torch
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
MOSS_PYTHON = ROOT / "tools" / "moss-venv" / "Scripts" / "python.exe"
WORKER = ROOT / "tools" / "audio" / "moss_worker.py"
NEGATIVE = "speech, talking, voice, singing, music, low quality, distorted, muffled, noise"

sys.path.insert(0, str(ROOT / "tools" / "comfy"))
import beauty_core  # noqa: E402


def read_wav(path: Path):
    """16-bit PCM WAV (what moss_worker writes) as a [1, channels, samples] float tensor."""
    with wave.open(str(path), "rb") as handle:
        channels, rate = handle.getnchannels(), handle.getframerate()
        frames = np.frombuffer(handle.readframes(handle.getnframes()), dtype=np.int16)
    samples = torch.from_numpy(frames.reshape(-1, channels).T.astype(np.float32) / 32768.0)
    return {"waveform": samples.unsqueeze(0), "sample_rate": rate}


class RimteraMossSoundEffect:
    CATEGORY = "Rimtera/audio"
    RETURN_TYPES = ("AUDIO",)
    FUNCTION = "generate"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "prompt": ("STRING", {"multiline": True, "default": "a quick short blade stab, sharp thin swish. sci-fi frontier video game sound effect"}),
            "negative": ("STRING", {"multiline": True, "default": NEGATIVE}),
            "seconds": ("FLOAT", {"default": 1.0, "min": 0.25, "max": 30.0, "step": 0.25}),
            "seed": ("INT", {"default": 0, "min": 0, "max": 2**31 - 1, "control_after_generate": True}),
            "steps": ("INT", {"default": 50, "min": 10, "max": 100}),
            "cfg": ("FLOAT", {"default": 4.0, "min": 1.0, "max": 10.0, "step": 0.5}),
            "power": (["focused", "restricted"], {"default": "focused",
                                                  "tooltip": "focused: about 80% of the machine. restricted: leaves 4 GB of VRAM free."}),
        }}

    def generate(self, prompt, negative, seconds, seed, steps, cfg, power):
        if not MOSS_PYTHON.exists():
            raise RuntimeError(f"MOSS is not set up ({MOSS_PYTHON} missing): run tools/audio/setup_audio.ps1")
        import comfy.model_management as mm
        mm.unload_all_models()  # MOSS needs the VRAM ComfyUI's models are holding
        mm.soft_empty_cache()
        focused = power == "focused"
        env = dict(os.environ, RIMTERA_PRIORITY="normal" if focused else "below",
                   RIMTERA_VRAM_FREE_GB="1.5" if focused else "4", RIMTERA_CPU_FRACTION="0.8" if focused else "0.75")
        with tempfile.TemporaryDirectory(prefix="rimtera-moss-") as folder:
            out = Path(folder) / "clip.wav"
            jobs = Path(folder) / "job.jsonl"
            jobs.write_text(json.dumps({"prompt": prompt, "negative": negative, "seconds": seconds, "seed": seed, "out": str(out)}), encoding="utf-8")
            result = subprocess.run([str(MOSS_PYTHON), str(WORKER), str(jobs), "--steps", str(steps), "--cfg", str(cfg)],
                                    cwd=WORKER.parent, env=env, capture_output=True, text=True)
            print(result.stdout.strip(), flush=True)
            if not out.exists():
                raise RuntimeError(f"MOSS produced no clip:\n{(result.stdout + result.stderr)[-2000:]}")
            return (read_wav(out),)


def to_pil(image, mask=None, index=0) -> Image.Image:
    """A ComfyUI IMAGE (and Load Image's MASK, 1 where transparent) as a PIL image."""
    rgb = (image[index].cpu().numpy() * 255).clip(0, 255).astype(np.uint8)
    picture = Image.fromarray(rgb, "RGB").convert("RGBA")
    if mask is not None:
        alpha = ((1.0 - mask[min(index, mask.shape[0] - 1)].cpu().numpy()) * 255).clip(0, 255).astype(np.uint8)
        if alpha.shape == rgb.shape[:2]:
            picture.putalpha(Image.fromarray(alpha, "L"))
    return picture


def to_tensor(picture: Image.Image):
    return torch.from_numpy(np.array(picture.convert("RGB")).astype(np.float32) / 255.0).unsqueeze(0)


def save_sprites(named, prefix):
    """Saves (name, RGBA image) pairs under ComfyUI's output folder; returns the UI preview list."""
    import folder_paths
    folder = Path(folder_paths.get_output_directory()) / prefix
    folder.mkdir(parents=True, exist_ok=True)
    previews = []
    for name, sprite in named:
        sprite.save(folder / f"{name}.png")
        previews.append({"filename": f"{name}.png", "subfolder": prefix.replace("\\", "/"), "type": "output"})
    return previews


class RimteraSpriteOnMagenta:
    CATEGORY = "Rimtera/art"
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "place"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"image": ("IMAGE",), "mask": ("MASK",),
                             "width": ("INT", {"default": 1024, "min": 256, "max": 2048, "step": 64}),
                             "height": ("INT", {"default": 1024, "min": 256, "max": 2048, "step": 64})}}

    def place(self, image, mask, width, height):
        return (to_tensor(beauty_core.place_on_magenta(to_pil(image, mask), width, height)),)


class RimteraAnimationTemplate:
    CATEGORY = "Rimtera/art"
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "template"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"image": ("IMAGE",), "mask": ("MASK",)}}

    def template(self, image, mask):
        return (to_tensor(beauty_core.animation_template(to_pil(image, mask))),)


class RimteraFinishSprites:
    CATEGORY = "Rimtera/art"
    RETURN_TYPES = ()
    OUTPUT_NODE = True
    FUNCTION = "finish"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "images": ("IMAGE",),
            "columns": ("INT", {"default": 1, "min": 1, "max": 8}),
            "rows": ("INT", {"default": 1, "min": 1, "max": 8}),
            "max_width": ("INT", {"default": 64, "min": 4, "max": 1024,
                                  "tooltip": "Pixels. Characters: box width in tiles x 1.6 x 40. Items: 96 for weapons and tools, else 64."}),
            "max_height": ("INT", {"default": 105, "min": 4, "max": 1024,
                                   "tooltip": "Pixels. Characters: box height in tiles x 40 (a person is 105). Items: same as the width."}),
            "colours": ("INT", {"default": 32, "min": 4, "max": 256}),
            "outline": ("BOOLEAN", {"default": True}),
            "filename_prefix": ("STRING", {"default": "rimtera/sprites/sprite"}),
        }}

    def finish(self, images, columns, rows, max_width, max_height, colours, outline, filename_prefix):
        folder, _, stem = filename_prefix.replace("\\", "/").rpartition("/")
        named = []
        for index in range(images.shape[0]):
            for number, cell in enumerate(beauty_core.cut_sheet(to_pil(images, index=index).convert("RGB"), columns, rows), start=1):
                if cell is not None:
                    named.append((f"{stem}_{index + 1}_{number}", beauty_core.finish(cell, max_width, max_height, colours, outline)))
        return {"ui": {"images": save_sprites(named, folder or "rimtera")}}


class RimteraFinishAnimation:
    CATEGORY = "Rimtera/art"
    RETURN_TYPES = ()
    OUTPUT_NODE = True
    FUNCTION = "finish"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "sheet": ("IMAGE",),
            "base": ("IMAGE",),
            "base_mask": ("MASK",),
            "outline": ("BOOLEAN", {"default": True}),
            "filename_prefix": ("STRING", {"default": "rimtera/anim/creature"}),
        }}

    def finish(self, sheet, base, base_mask, outline, filename_prefix):
        folder, _, stem = filename_prefix.replace("\\", "/").rpartition("/")
        frames = beauty_core.frames_from_sheet(to_pil(sheet).convert("RGB"), beauty_core.trim(to_pil(base, base_mask)), outline)
        if any(frame is None for frame in frames):
            raise RuntimeError(f"only {sum(f is not None for f in frames)} of 8 frames came out of the sheet; run it again")
        names = [f"{state}_{n}" for state, count in (("idle", 2), ("move", 4), ("attack", 2)) for n in range(1, count + 1)]
        named = [(f"{stem}_{name}", frame) for name, frame in zip(names, beauty_core.centre_on_body(frames))]
        return {"ui": {"images": save_sprites(named, folder or "rimtera")}}


class RimteraCutLabelled:
    """Cuts every labelled picture out of an original art sheet, from the boxes the vision model gave (Text Generate
    with the sheet-index question), as index_sheets.py does for all sheets."""
    CATEGORY = "Rimtera/art"
    RETURN_TYPES = ()
    OUTPUT_NODE = True
    FUNCTION = "cut"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"sheet": ("IMAGE",), "boxes": ("STRING", {"forceInput": True}),
                             "filename_prefix": ("STRING", {"default": "rimtera/sheet_index/sheet"})}}

    def cut(self, sheet, boxes, filename_prefix):
        import index_sheets
        folder, _, stem = filename_prefix.replace("\\", "/").rpartition("/")
        picture = to_pil(sheet).convert("RGB")
        named, seen = [], {}
        for label, box in index_sheets.boxes(boxes, picture.width, picture.height):
            name = f"{stem}_{index_sheets.safe(label)}"
            seen[name] = seen.get(name, 0) + 1
            named.append((name if seen[name] == 1 else f"{name}_{seen[name]}", picture.crop(box).convert("RGBA")))
        if not named:
            raise RuntimeError(f"no labelled pictures found in the model's answer:\n{boxes[:500]}")
        return {"ui": {"images": save_sprites(named, folder or "rimtera")}}


def write_wav(audio, path: Path):
    """A ComfyUI AUDIO (first item) as a 16-bit PCM WAV."""
    samples = audio["waveform"][0].cpu().numpy().clip(-1, 1)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(samples.shape[0])
        handle.setsampwidth(2)
        handle.setframerate(int(audio["sample_rate"]))
        handle.writeframes((samples.T * 32767).astype(np.int16).tobytes())


class RimteraFinishAudio:
    """Finishes a clip for a cue as the overnight run does (tools/audio/finish_one.py in the MOSS venv): levelled to
    the cue's loudness, trimmed and faded, or cut into a seamless loop for music and ambience. With install on, it
    also goes into Assets/ as the cue's next take (never replacing an existing file); rebuild the game to hear it."""
    CATEGORY = "Rimtera/audio"
    RETURN_TYPES = ("AUDIO",)
    OUTPUT_NODE = True
    FUNCTION = "finish"

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "audio": ("AUDIO",),
            "cue": ("STRING", {"default": "use_shortsword", "tooltip": "A cue from tools/audio/audio_needs.tsv (dotnet run -- --sound-cues)."}),
            "install": ("BOOLEAN", {"default": False, "tooltip": "Also add it to Assets/ as the cue's next take."}),
        }}

    def finish(self, audio, cue, install):
        if not MOSS_PYTHON.exists():
            raise RuntimeError(f"MOSS is not set up ({MOSS_PYTHON} missing): run tools/audio/setup_audio.ps1")
        with tempfile.TemporaryDirectory(prefix="rimtera-finish-") as folder:
            source, out = Path(folder) / "in.wav", Path(folder) / "out.wav"
            write_wav(audio, source)
            command = [str(MOSS_PYTHON), str(ROOT / "tools" / "audio" / "finish_one.py"), str(source), cue.strip(), str(out)]
            result = subprocess.run(command + (["--install"] if install else []), cwd=ROOT / "tools" / "audio",
                                    capture_output=True, text=True)
            print(result.stdout.strip(), flush=True)
            if not out.exists():
                raise RuntimeError(f"finishing failed:\n{(result.stdout + result.stderr)[-2000:]}")
            return {"ui": {"text": [result.stdout.strip() or "finished"]}, "result": (read_wav(out),)}


NODE_CLASS_MAPPINGS = {
    "RimteraCutLabelled": RimteraCutLabelled,
    "RimteraFinishAudio": RimteraFinishAudio,
    "RimteraMossSoundEffect": RimteraMossSoundEffect,
    "RimteraSpriteOnMagenta": RimteraSpriteOnMagenta,
    "RimteraAnimationTemplate": RimteraAnimationTemplate,
    "RimteraFinishSprites": RimteraFinishSprites,
    "RimteraFinishAnimation": RimteraFinishAnimation,
}
NODE_DISPLAY_NAME_MAPPINGS = {
    "RimteraCutLabelled": "Rimtera Cut Labelled Pictures",
    "RimteraFinishAudio": "Rimtera Finish Audio",
    "RimteraMossSoundEffect": "Rimtera MOSS Sound Effect",
    "RimteraSpriteOnMagenta": "Rimtera Sprite On Magenta",
    "RimteraAnimationTemplate": "Rimtera Animation Template",
    "RimteraFinishSprites": "Rimtera Finish Sprites",
    "RimteraFinishAnimation": "Rimtera Finish Animation",
}
