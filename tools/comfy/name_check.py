"""Checks that a sprite shows what its name says, with the Qwen3-VL 8B model ComfyUI already has (the Qwen-Image text
encoder, run through ComfyUI's TextGenerate node). Free and local; about 5-10 s per sprite.

Two questions, so the name cannot sway what the model sees:
1. Shown only the picture: what does it show, in a few words?
2. Text only: does that description fit "<name>" (<category>)? Only the kind of thing counts, not colour or material,
   since many names are made-up sci-fi words.

    from name_check import NameCheck
    seen, fits = NameCheck().check(Path("Assets/Sprites/ammo_mini_rocket.png"), "Mini rocket", "ammo")
    # ("a three-pronged trident", False)

Needs the ComfyUI server (tools/comfy/start_comfy.ps1).
"""
import json
import time
from pathlib import Path

from PIL import Image

from comfy_client import Comfy

ENCODER = "qwen3vl_8b_int8_convrot.safetensors"
DESCRIBE = ("What single object or creature does this pixel art video game sprite show? If it shows several separate "
            "things, or is broken into fragments, say so. Answer in at most ten words.")


class NameCheck(Comfy):
    def _text(self, prompt: str, image: str | None = None, length: int = 40) -> str:
        graph = {
            "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": ENCODER, "type": "qwen_image", "device": "default"}},
            "gen": {"class_type": "TextGenerate", "inputs": {"clip": ["clip", 0], "prompt": prompt, "max_length": length, "sampling_mode": "off"}},
            "show": {"class_type": "PreviewAny", "inputs": {"source": ["gen", 0]}},
        }
        if image is not None:
            graph["img"] = {"class_type": "LoadImage", "inputs": {"image": image}}
            graph["gen"]["inputs"]["image"] = ["img", 0]
        prompt_id = self._post("/prompt", json.dumps({"prompt": graph, "client_id": self.client_id}).encode())["prompt_id"]
        deadline = time.time() + 300
        while time.time() < deadline:
            history = json.loads(self._get(f"/history/{prompt_id}"))
            if prompt_id in history:
                entry = history[prompt_id]
                if entry.get("status", {}).get("status_str") == "error":
                    raise RuntimeError(json.dumps(entry["status"].get("messages", []))[-1500:])
                return " ".join(entry["outputs"]["show"]["text"]).strip()
            time.sleep(0.5)
        raise TimeoutError("name check did not finish in 5 minutes")

    def describe(self, sprite: Path | Image.Image, scratch: Path = Path.cwd()) -> str:
        """What the picture shows, judged on a grey backdrop at a readable size."""
        image = sprite if isinstance(sprite, Image.Image) else Image.open(sprite)
        image = image.convert("RGBA")
        k = max(1, 384 // max(image.size))
        flat = Image.new("RGBA", (image.width * k + 32, image.height * k + 32), (128, 128, 128, 255))
        flat.alpha_composite(image.resize((image.width * k, image.height * k), Image.NEAREST), (16, 16))
        path = Path(scratch) / "name_check.png"
        flat.convert("RGB").save(path)
        return " ".join(self._text(DESCRIBE, self.upload(path)).split())

    def fits(self, seen: str, name: str, category: str) -> bool:
        answer = self._text(
            f"A video game {category} is named \"{name}\". Its sprite was described as: \"{seen}\". Does that description "
            f"show the kind of thing the name says (for example a rocket, a sword, a helmet, a crab)? Ignore colour, "
            f"material and made-up sci-fi words. It does not fit if it is a different kind of thing, several things, or "
            f"broken fragments. Answer only YES or NO.", length=4)
        return answer.upper().lstrip(" .\"'").startswith("YES")

    def check(self, sprite, name: str, category: str, scratch: Path = Path.cwd()):
        seen = self.describe(sprite, scratch)
        return seen, self.fits(seen, name, category)
