"""Shared paths and helpers for the tools/audio scripts (docs/AUDIO_FLOW.md)."""
import csv
import hashlib
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools" / "audio"
NEEDS = TOOLS / "audio_needs.tsv"
RAW = TOOLS / "raw"
CANDIDATES = TOOLS / "candidates"
MOSS_PYTHON = ROOT / "Rimtera" / "vendor" / "moss-venv" / "Scripts" / "python.exe"
COMFY_PYTHON = ROOT / "Rimtera" / "ComfyUI" / ".venv" / "Scripts" / "python.exe"

# Generation order: the game's most-heard sounds first, so a short night still covers the essentials.
ORDER = ["weapons", "impacts", "tools", "tiles", "interactions", "ui", "music", "player", "creatures", "bosses", "events", "ambience"]


def asset_folder(category: str) -> Path:
    """Where a promoted take of this category lives in the game."""
    return ROOT / "Assets" / {"music": "Music", "ambience": "Ambience"}.get(category, "Sounds")


def export_needs() -> None:
    """Refreshes audio_needs.tsv from the game (dotnet run -- --sound-cues); keeps the old list if that fails."""
    result = subprocess.run(["dotnet", "run", "--project", str(ROOT), "--", "--sound-cues"], capture_output=True, text=True, cwd=ROOT)
    if result.returncode == 0 and result.stdout.startswith("cue\t"):
        NEEDS.write_text(result.stdout, encoding="utf-8")
    else:
        print("could not refresh audio_needs.tsv; using the existing list", flush=True)


def load_needs(categories=None):
    with NEEDS.open(encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE))
    if categories:
        rows = [row for row in rows if row["category"] in categories]
    return sorted(rows, key=lambda row: ORDER.index(row["category"]) if row["category"] in ORDER else len(ORDER))


def existing_takes(cue: str, category: str) -> list:
    """Files the game already loads for this cue (cue.wav, cue_2.wav …): recordings and promoted takes."""
    folder = asset_folder(category)
    return sorted(path for path in folder.glob(f"{cue}*.wav")
                  if path.stem == cue or (path.stem.startswith(cue + "_") and path.stem[len(cue) + 1:].isdigit()))


def seed(cue: str, take: int) -> int:
    return int(hashlib.sha1(f"{cue}#{take}".encode()).hexdigest()[:8], 16)


def raw_path(row, take: int, ext: str = "wav") -> Path:
    return RAW / row["category"] / f"{row['cue']}__c{take}.{ext}"
