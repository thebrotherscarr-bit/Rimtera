"""Minimal ComfyUI API client for Rimtera art (Qwen-Image 2.1 on the local server).

Text-to-image, or edit with reference images (style sheets): the reference images are uploaded and passed to
TextEncodeQwenImage21, which conditions on them, so new art follows the look of existing Neo-Terra sheets.

    from comfy_client import Comfy
    comfy = Comfy()
    png_bytes = comfy.generate("a pixel-art sci-fi sword ...", width=1024, height=1024, references=["sheet.png"])
"""
import json
import os
import random
import time
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

UNET = "qwen_image_2.1_int8_convrot.safetensors"
CLIP = "qwen3vl_8b_int8_convrot.safetensors"
VAE = "qwen_image_2.1_vae_bf16.safetensors"


class Comfy:
    def __init__(self, url="http://127.0.0.1:8188"):
        self.url = url.rstrip("/")
        self.client_id = uuid.uuid4().hex

    def _post(self, path, payload: bytes, content_type="application/json"):
        request = urllib.request.Request(self.url + path, data=payload, headers={"Content-Type": content_type})
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read())

    def _get(self, path):
        with urllib.request.urlopen(self.url + path, timeout=60) as response:
            return response.read()

    def upload(self, path: Path) -> str:
        """Uploads a reference image to the input folder; returns the name ComfyUI knows it by."""
        boundary = uuid.uuid4().hex
        data = Path(path).read_bytes()
        body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{Path(path).name}\"\r\n"
                f"Content-Type: image/png\r\n\r\n").encode() + data + (
                f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n--{boundary}--\r\n").encode()
        return self._post("/upload/image", body, f"multipart/form-data; boundary={boundary}")["name"]

    def workflow(self, prompt, negative="", width=1024, height=1024, steps=25, cfg=1.0, seed=None, references=(), ref_resolution=384):
        seed = random.randint(0, 2**48) if seed is None else seed
        graph = {
            "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": UNET, "weight_dtype": "default"}},
            "cache": {"class_type": "QwenImage21Cache", "inputs": {"model": ["unet", 0], "device": "auto", "dtype": "default"}},
            # The text encoder stays on the GPU (on the CPU its int8 weights expand to ~18 GB of RAM); the server runs
            # with --disable-dynamic-vram so it is unloaded while the diffusion model samples fully resident.
            "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": CLIP, "type": "qwen_image", "device": "default"}},
            "vae": {"class_type": "VAELoader", "inputs": {"vae_name": VAE}},
            "encode": {"class_type": "TextEncodeQwenImage21", "inputs": {
                "clip": ["clip", 0], "vae": ["vae", 0], "prompt": prompt, "negative_prompt": negative,
                # Reference images are resized to about ref_resolution²; sprites need little, and every reference
                # token is paid again on each sampling step.
                "resolution": ref_resolution}},
            "latent": {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}},
            "sample": {"class_type": "KSampler", "inputs": {
                "model": ["cache", 0], "positive": ["encode", 0], "negative": ["encode", 1], "latent_image": ["latent", 0],
                "seed": seed, "steps": steps, "cfg": cfg, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
            "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
            "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": "rimtera/gen"}},
        }
        for index, name in enumerate(references, start=1):
            graph[f"ref{index}"] = {"class_type": "LoadImage", "inputs": {"image": name}}
            graph["encode"]["inputs"][f"images.image_{index}"] = [f"ref{index}", 0]
        return graph

    def run(self, graph, timeout=1800):
        """Queues a workflow and waits for it; returns the PNG bytes of every saved image."""
        prompt_id = self._post("/prompt", json.dumps({"prompt": graph, "client_id": self.client_id}).encode())["prompt_id"]
        deadline = time.time() + timeout
        while time.time() < deadline:
            history = json.loads(self._get(f"/history/{prompt_id}"))
            if prompt_id in history:
                # Optional rest between jobs (RIMTERA_COMFY_PAUSE seconds) so other programs get GPU time.
                time.sleep(float(os.environ.get("RIMTERA_COMFY_PAUSE", "0") or 0))
                entry = history[prompt_id]
                status = entry.get("status", {})
                if status.get("status_str") == "error":
                    raise RuntimeError(json.dumps(status.get("messages", []))[:2000])
                images = []
                for output in entry.get("outputs", {}).values():
                    for image in output.get("images", []):
                        query = urllib.parse.urlencode({"filename": image["filename"], "subfolder": image["subfolder"], "type": image["type"]})
                        images.append(self._get(f"/view?{query}"))
                return images
            time.sleep(1.5)
        raise TimeoutError(f"ComfyUI prompt {prompt_id} did not finish in {timeout}s")

    def generate(self, prompt, references=(), **kwargs) -> bytes:
        names = [self.upload(Path(path)) for path in references]
        return self.run(self.workflow(prompt, references=names, **kwargs))[0]


if __name__ == "__main__":
    import sys
    out = Path(sys.argv[2] if len(sys.argv) > 2 else "comfy_test.png")
    started = time.time()
    out.write_bytes(Comfy().generate(sys.argv[1] if len(sys.argv) > 1 else "a red apple on a table", width=1024, height=1024))
    print(f"wrote {out} in {time.time() - started:.1f}s")
