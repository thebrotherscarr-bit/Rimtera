"""Installs Rimtera's flows into the ComfyUI app, so art and audio can be made by hand from its Workflows sidebar.

    Rimtera/standalone-env/python.exe tools/comfy/app_flows.py

Copies tools/comfy/workflows/*.json to Rimtera/ComfyUI/user/default/workflows/Rimtera/ (the app's Workflows sidebar,
folder "Rimtera") and installs the node shim (tools/comfy/nodes/custom_nodes_shim.py) as
Rimtera/ComfyUI/custom_nodes/rimtera_nodes.py, which adds the Rimtera MOSS Sound Effect node. Restart ComfyUI after the
first install so the node loads. The flows build the same graphs as the overnight scripts (comfy_client.py,
audio_client.py, moss_worker.py); a flow edited in the app stays in the app until it is copied back here.
"""
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMFY = ROOT / "Rimtera" / "ComfyUI"
FLOWS = Path(__file__).resolve().parent / "workflows"


def main():
    target = COMFY / "user" / "default" / "workflows" / "Rimtera"
    target.mkdir(parents=True, exist_ok=True)
    for flow in sorted(FLOWS.glob("*.json")):
        shutil.copy2(flow, target / flow.name)
        print(f"flow: {flow.name}")
    shutil.copy2(Path(__file__).resolve().parent / "nodes" / "custom_nodes_shim.py", COMFY / "custom_nodes" / "rimtera_nodes.py")
    print("node: Rimtera MOSS Sound Effect (restart ComfyUI if it was running)")


if __name__ == "__main__":
    main()
