"""Installs Rimtera's flows into the ComfyUI app, so art and audio can be made by hand from its Workflows sidebar.

    tools/comfy-app/standalone-env/python.exe tools/comfy/app_flows.py

Copies tools/comfy/workflows/*.json to tools/comfy-app/ComfyUI/user/default/workflows/Rimtera/ (the app's Workflows
sidebar, folder "Rimtera") and installs the node shim (tools/comfy/nodes/custom_nodes_shim.py) as
tools/comfy-app/ComfyUI/custom_nodes/rimtera_nodes.py, which adds the Rimtera nodes (MOSS sound effects, sprite
finishing, animation, sheet cutting, audio finishing). Restart ComfyUI after the first install so the nodes load. Also
copies the sample inputs the flows open with into ComfyUI's input folders. A flow edited in the app stays in the app
until it is copied back here.
"""
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMFY = ROOT / "tools" / "comfy-app" / "ComfyUI"
FLOWS = Path(__file__).resolve().parent / "workflows"
# What each flow's Load Image / Load Audio opens with, so it runs as soon as it is opened.
SAMPLES = {
    "rimtera_old_critter_shell_crab.png": ROOT / "Assets" / "Sprites" / "critter_shell_crab.png",
    "rimtera_old_ammo_mini_rocket.png": ROOT / "Assets" / "Sprites" / "ammo_mini_rocket.png",
    "rimtera_style_critters.jpg": ROOT / "art" / "sheets" / "sheet_23_critters.jpg",
    "rimtera_style_weapons.jpg": ROOT / "art" / "sheets" / "sheet_05_08_stations_weapons.jpg",
    "rimtera_sheet_bosses.jpg": ROOT / "art" / "sheets" / "sheet_00c_bosses.jpg",
    "rimtera_style_ui.jpg": ROOT / "art" / "sheets" / "sheet_00c_bosses.jpg",
    "rimtera_sample_clip.wav": ROOT / "Assets" / "Sounds" / "bite.wav",
}


def main():
    target = COMFY / "user" / "default" / "workflows" / "Rimtera"
    target.mkdir(parents=True, exist_ok=True)
    for flow in sorted(FLOWS.glob("*.json")):
        shutil.copy2(flow, target / flow.name)
        print(f"flow: {flow.name}")
    shutil.copy2(Path(__file__).resolve().parent / "nodes" / "custom_nodes_shim.py", COMFY / "custom_nodes" / "rimtera_nodes.py")
    print("nodes: Rimtera (restart ComfyUI if it was running)")
    for folder in (COMFY / "input", Path(os.environ.get("LOCALAPPDATA", "")) / "Comfy-Desktop" / "ComfyUI-Shared" / "input"):
        folder.mkdir(parents=True, exist_ok=True)
        for name, source in SAMPLES.items():
            if source.exists():
                shutil.copy2(source, folder / name)
    print("samples: copied to ComfyUI's input folders")


if __name__ == "__main__":
    main()
