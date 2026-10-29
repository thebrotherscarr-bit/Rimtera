"""ComfyUI client for Rimtera music: ACE-Step 1.5 turbo (MIT licence), native ComfyUI nodes.

Builds the same graph as ComfyUI's "ACE-Step 1.5" template and returns the FLAC bytes of the track. Instrumental
only: the lyrics field is "[Instrumental]". Start the server with tools/comfy/start_comfy.ps1 first.

    from audio_client import AceStep
    flac = AceStep().track("sci-fi frontier, warm analog synth, orchestral strings", seconds=120, bpm=96, key="D minor")

Run directly for a test track:  python tools/audio/audio_client.py "tags" out.flac [seconds]
"""
import json
import random
import sys
import time
import urllib.parse
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "comfy"))
from comfy_client import Comfy  # noqa: E402

UNET = "acestep_v1.5_turbo.safetensors"
CLIP_SMALL = "qwen_0.6b_ace15.safetensors"
CLIP_LARGE = "qwen_1.7b_ace15.safetensors"
VAE = "ace_1.5_vae.safetensors"


class AceStep(Comfy):
    def music_workflow(self, tags, seconds=120.0, bpm=100, key="C minor", time_signature="4", seed=None, steps=8,
                       lyrics="[Instrumental]", prefix="rimtera/music"):
        seed = random.randint(0, 2**32) if seed is None else seed
        return {
            "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": UNET, "weight_dtype": "default"}},
            "shift": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["unet", 0], "shift": 3}},
            "clip": {"class_type": "DualCLIPLoader", "inputs": {"clip_name1": CLIP_SMALL, "clip_name2": CLIP_LARGE, "type": "ace", "device": "default"}},
            "vae": {"class_type": "VAELoader", "inputs": {"vae_name": VAE}},
            "encode": {"class_type": "TextEncodeAceStepAudio1.5", "inputs": {
                "clip": ["clip", 0], "tags": tags, "lyrics": lyrics, "seed": seed, "bpm": int(bpm), "duration": float(seconds),
                "timesignature": str(time_signature), "language": "en", "keyscale": key, "generate_audio_codes": True,
                "cfg_scale": 2.0, "temperature": 0.85, "top_p": 0.9, "top_k": 0, "min_p": 0.0}},
            "negative": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["encode", 0]}},
            "latent": {"class_type": "EmptyAceStep1.5LatentAudio", "inputs": {"seconds": float(seconds), "batch_size": 1}},
            "sample": {"class_type": "KSampler", "inputs": {
                "model": ["shift", 0], "positive": ["encode", 0], "negative": ["negative", 0], "latent_image": ["latent", 0],
                "seed": seed, "steps": steps, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
            # Tiled: a whole-track decode asks MIOpen for gigabytes of conv workspace and crashes on the RX 6800 XT.
            "decode": {"class_type": "VAEDecodeAudioTiled", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0], "tile_size": 256, "overlap": 32}},
            "save": {"class_type": "SaveAudio", "inputs": {"audio": ["decode", 0], "filename_prefix": prefix}},
        }

    def run_audio(self, graph, timeout=3600) -> bytes:
        """Queues a workflow and waits; returns the bytes of the first saved audio file."""
        prompt_id = self._post("/prompt", json.dumps({"prompt": graph, "client_id": self.client_id}).encode())["prompt_id"]
        deadline = time.time() + timeout
        while time.time() < deadline:
            history = json.loads(self._get(f"/history/{prompt_id}"))
            if prompt_id in history:
                entry = history[prompt_id]
                status = entry.get("status", {})
                if status.get("status_str") == "error":
                    raise RuntimeError(json.dumps(status.get("messages", []))[:2000])
                for output in entry.get("outputs", {}).values():
                    for audio in output.get("audio", []):
                        query = urllib.parse.urlencode({"filename": audio["filename"], "subfolder": audio["subfolder"], "type": audio["type"]})
                        return self._get(f"/view?{query}")
                raise RuntimeError("workflow finished without saving audio")
            time.sleep(2)
        raise TimeoutError(f"ComfyUI prompt {prompt_id} did not finish in {timeout}s")

    def track(self, tags, **kwargs) -> bytes:
        return self.run_audio(self.music_workflow(tags, **kwargs))


if __name__ == "__main__":
    out = Path(sys.argv[2] if len(sys.argv) > 2 else "ace_test.flac")
    started = time.time()
    out.write_bytes(AceStep().track(sys.argv[1] if len(sys.argv) > 1 else "ambient sci-fi, warm analog synth pads, strings",
                                    seconds=float(sys.argv[3]) if len(sys.argv) > 3 else 60))
    print(f"wrote {out} in {time.time() - started:.1f}s")
