# Installed into Rimtera/ComfyUI/custom_nodes/rimtera_nodes.py by tools/comfy/app_flows.py. It loads Rimtera's nodes
# from the repo (tools/comfy/nodes/rimtera_nodes.py), so edits there take effect when ComfyUI restarts. Delete to undo.
import importlib.util
from pathlib import Path

_source = Path(__file__).resolve().parents[3] / "tools" / "comfy" / "nodes" / "rimtera_nodes.py"
_spec = importlib.util.spec_from_file_location("rimtera_repo_nodes", _source)
_module = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_module)
NODE_CLASS_MAPPINGS = _module.NODE_CLASS_MAPPINGS
NODE_DISPLAY_NAME_MAPPINGS = _module.NODE_DISPLAY_NAME_MAPPINGS
