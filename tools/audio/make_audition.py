"""Builds tools/audio/candidates/audition.html: every candidate take next to what the game plays now, grouped by
category. Tick the takes to keep, press "Copy keep list", paste it into a file and run promote.py --keep <file>.

    python tools/audio/make_audition.py
"""
import html
import json
import os

from audio_common import CANDIDATES, ROOT, asset_folder, load_needs

PAGE = """<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Rimtera audition</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
:root{--bg:#14171c;--panel:#1d222a;--text:#e6e9ee;--muted:#8d96a3;--accent:#6fc3ff;--keep:#3fbf7f}
body{margin:0;padding:16px;background:var(--bg);color:var(--text);font:14px system-ui,sans-serif}
h1{font-size:20px;margin:0 0 4px} h2{font-size:16px;margin:24px 0 8px;color:var(--accent);text-transform:capitalize}
.bar{position:sticky;top:0;background:var(--bg);padding:8px 0;display:flex;gap:8px;align-items:center;flex-wrap:wrap;z-index:1}
button{background:var(--accent);color:#000;border:0;border-radius:6px;padding:6px 12px;font-weight:600;cursor:pointer}
.cue{background:var(--panel);border-radius:8px;padding:10px;margin:6px 0}
.cue b{font-family:ui-monospace,monospace} .muted{color:var(--muted);font-size:12px}
.takes{display:flex;flex-wrap:wrap;gap:8px;margin-top:6px}
.take{display:flex;align-items:center;gap:6px;background:#0002;border-radius:6px;padding:4px 6px}
.take.kept{outline:2px solid var(--keep)} audio{height:32px;max-width:260px}
textarea{width:100%;height:120px;background:var(--panel);color:var(--text);border:0;border-radius:6px}
</style></head><body>
<h1>Rimtera audition</h1><div class="muted">Candidates from the overnight audio run. Tick takes to keep; the current game sound is shown first.</div>
<div class="bar"><button onclick="copyList()">Copy keep list</button><span id="count" class="muted">0 kept</span></div>
<textarea id="list" placeholder="Kept takes appear here; save as keep.txt and run promote.py --keep keep.txt"></textarea>
__BODY__
<script>
const kept=new Set();
document.querySelectorAll('input[type=checkbox]').forEach(box=>box.addEventListener('change',()=>{
  box.checked?kept.add(box.value):kept.delete(box.value);box.closest('.take').classList.toggle('kept',box.checked);
  document.getElementById('count').textContent=kept.size+' kept';document.getElementById('list').value=[...kept].join('\\n');}));
function copyList(){const area=document.getElementById('list');area.select();navigator.clipboard?.writeText(area.value);}
</script></body></html>"""


def main():
    rows = load_needs()
    sections, current = [], None
    count = 0
    for row in rows:
        folder = CANDIDATES / row["category"]
        takes = sorted(folder.glob(f"{row['cue']}__c*.wav")) if folder.exists() else []
        if not takes:
            continue
        if row["category"] != current:
            current = row["category"]
            sections.append(f"<h2>{html.escape(current)}</h2>")
        game = sorted(asset_folder(row["category"]).glob(f"{row['cue']}*.*"))
        game_html = "".join(f'<div class="take"><span class="muted">now</span><audio controls preload="none" src="{html.escape(os.path.relpath(path, CANDIDATES).replace(os.sep, "/"))}"></audio></div>'
                            for path in game[:2]) or '<div class="take muted">now: synth fallback</div>'
        take_html = "".join(f'<label class="take"><input type="checkbox" value="{html.escape(take.name)}"><audio controls preload="none" '
                            f'src="{html.escape(take.relative_to(CANDIDATES).as_posix())}"></audio></label>' for take in takes)
        sections.append(f'<div class="cue"><b>{html.escape(row["cue"])}</b> <span class="muted">{html.escape(row["trigger"])}</span>'
                        f'<div class="muted">{html.escape(row["prompt"])}</div><div class="takes">{game_html}{take_html}</div></div>')
        count += len(takes)
    page = CANDIDATES / "audition.html"
    page.parent.mkdir(parents=True, exist_ok=True)
    page.write_text(PAGE.replace("__BODY__", "\n".join(sections) or "<p>No candidates yet.</p>"), encoding="utf-8")
    print(f"audition page: {page} ({count} takes)", flush=True)


if __name__ == "__main__":
    main()
