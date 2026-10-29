"""Rimtera's ComfyUI nodes, loaded by the shim tools/comfy/app_flows.py installs in tools/comfy-app/ComfyUI/custom_nodes.

Rimtera MOSS Sound Effect: MOSS-SoundEffect v2.0 (Apache 2.0) as a node, so the effect and ambience flows run from the
ComfyUI app. MOSS pins transformers 4.57, which conflicts with ComfyUI's, so the node does not import it: it unloads
ComfyUI's models, runs tools/audio/moss_worker.py in tools/moss-venv on one job, and returns the clip as AUDIO
for Save Audio or Preview Audio. The model loads for every run (about half a minute), then each clip takes 15-30 s.
"""
import json
import os
import subprocess
import tempfile
import wave
from pathlib import Path

import numpy as np
import torch

ROOT = Path(__file__).resolve().parents[3]
MOSS_PYTHON = ROOT / "tools" / "moss-venv" / "Scripts" / "python.exe"
WORKER = ROOT / "tools" / "audio" / "moss_worker.py"
NEGATIVE = "speech, talking, voice, singing, music, low quality, distorted, muffled, noise"


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


NODE_CLASS_MAPPINGS = {"RimteraMossSoundEffect": RimteraMossSoundEffect}
NODE_DISPLAY_NAME_MAPPINGS = {"RimteraMossSoundEffect": "Rimtera MOSS Sound Effect"}
