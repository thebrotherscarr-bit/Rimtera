"""
Unpacks an Audacity 4 project (.aup4: SQLite holding binary XML plus float sample blocks) without Audacity.

    python tools/sounds/aup4_export.py Assets/Sounds/raw/take.aup4            # print the project tree
    python tools/sounds/aup4_export.py Assets/Sounds/raw/take.aup4 out/       # export each track as WAV
    python tools/sounds/aup4_export.py Assets/Sounds/raw/take.aup4 out/ --split   # ...and cut it into clips

--split cuts each track at gaps of silence into clip01.wav, clip02.wav, ... ready to be renamed into
Assets/Sounds (jump.wav, jump_2.wav, ...). Needs numpy.
"""
import sqlite3, struct, sys, wave
import numpy as np

def parse(d, doc):
    names = {}
    def run(buf, out):
        i, cs = 0, 2
        def s(n):
            return buf[i:i+n].decode('utf-16le' if cs == 2 else 'utf-8')
        while i < len(buf):
            t = buf[i]; i += 1
            if t == 0: cs = buf[i]; i += 1
            elif t == 15:
                nid, ln = struct.unpack_from('<HH', buf, i); i += 4
                names[nid] = buf[i:i+ln].decode('utf-16le' if cs == 2 else 'utf-8'); i += ln
            elif t in (1, 2):
                nid, = struct.unpack_from('<H', buf, i); i += 2
                out.append(('start' if t == 1 else 'end', names.get(nid)))
            elif t == 3:
                nid, ln = struct.unpack_from('<HI', buf, i); i += 6
                out.append(('attr', names.get(nid), buf[i:i+ln].decode('utf-16le' if cs == 2 else 'utf-8'))); i += ln
            elif t in (4, 6):
                nid, v = struct.unpack_from('<Hi', buf, i); i += 6; out.append(('attr', names.get(nid), v))
            elif t == 5:
                nid, v = struct.unpack_from('<HB', buf, i); i += 3; out.append(('attr', names.get(nid), v))
            elif t == 7:
                nid, v = struct.unpack_from('<Hq', buf, i); i += 10; out.append(('attr', names.get(nid), v))
            elif t == 8:
                nid, v = struct.unpack_from('<HI', buf, i); i += 6; out.append(('attr', names.get(nid), v))
            elif t == 9:
                nid, v, _ = struct.unpack_from('<Hfi', buf, i); i += 10; out.append(('attr', names.get(nid), v))
            elif t == 10:
                nid, v, _ = struct.unpack_from('<Hdi', buf, i); i += 14; out.append(('attr', names.get(nid), v))
            elif t in (11, 12):
                ln, = struct.unpack_from('<I', buf, i); i += 4 + ln
            elif t in (13, 14): pass
            elif t == 16:  # named binary blob (e.g. a PNG thumbnail): id, length, bytes
                nid, ln = struct.unpack_from('<HI', buf, i); i += 6 + ln
            else: raise ValueError(f'unknown field {t} at {i-1}')
    run(d, [])
    out = []
    run(doc, out)
    return out

def tree(events):
    root = {'tag': None, 'attrs': {}, 'kids': []}; stack = [root]
    for e in events:
        if e[0] == 'start':
            n = {'tag': e[1], 'attrs': {}, 'kids': []}; stack[-1]['kids'].append(n); stack.append(n)
        elif e[0] == 'end': stack.pop()
        else: stack[-1]['attrs'][e[1]] = e[2]
    return root

def walk(n, depth=0, show=True):
    if n['tag'] and show:
        a = {k: v for k, v in n['attrs'].items() if k in ('name', 'rate', 'offset', 'start', 'blockid', 'numsamples', 'channel', 'trimLeft', 'trimRight', 'sampleformat', 'linked', 'gain', 'pan', 'mute', 'colorindex', 'maxsamples')}
        print('  ' * depth + n['tag'], a)
    for k in n['kids']:
        walk(k, depth + 1, show and k['tag'] not in ('waveblock',) or show and depth < 0)

def split(x, rate, prefix, gap=0.35, floor=0.02):
    # Peak envelope on 10 ms windows; a clip runs until the level stays under the floor for longer than gap.
    win = rate // 100
    env = np.array([np.abs(x[i:i + win]).max() for i in range(0, len(x) - win, win)])
    clips, start, quiet = [], None, 0
    for i, level in enumerate(env):
        if level > floor:
            start = i if start is None else start
            quiet, end = 0, i
        elif start is not None:
            quiet += 1
            if quiet > gap * 100:
                clips.append((start, end)); start = None
    if start is not None:
        clips.append((start, end))
    clips = [(a, b) for a, b in clips if b - a >= 5]
    for k, (a, b) in enumerate(clips, 1):
        s, e = max(0, a * win - rate // 50), min(len(x), (b + 1) * win + rate * 8 // 100)
        clip = x[s:e].copy()
        fade_in, fade_out = rate * 4 // 1000, rate * 25 // 1000
        clip[:fade_in] *= np.linspace(0, 1, fade_in)
        clip[-fade_out:] *= np.linspace(1, 0, fade_out)
        clip *= 0.89 / max(1e-6, np.abs(clip).max())
        with wave.open(f'{prefix}_clip{k:02d}.wav', 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
            w.writeframes((np.clip(clip, -1, 1) * 32767).astype('<i2').tobytes())
        print(f'  clip{k:02d}  {s / rate:6.2f}s  {(e - s) / rate:.2f}s')

if __name__ == '__main__':
    path = sys.argv[1]
    c = sqlite3.connect(f'file:{path}?mode=ro', uri=True)
    d, doc = c.execute('select dict, doc from project').fetchone()
    root = tree(parse(d, doc))
    if len(sys.argv) == 2:
        walk(root); sys.exit()
    # export: one WAV per track, clips placed at their offsets
    outdir = sys.argv[2]
    blocks = {b: np.frombuffer(s, dtype='<f4') for b, s in c.execute('select blockid, samples from sampleblocks')}
    def find(n, tag):
        if n['tag'] == tag: yield n
        for k in n['kids']: yield from find(k, tag)
    for ti, track in enumerate(find(root, 'wavetrack')):
        rate = int(track['attrs'].get('rate', 44100))
        pieces = []
        for clip in find(track, 'waveclip'):
            seq = next(find(clip, 'sequence'))
            data = np.concatenate([blocks[b['attrs']['blockid']] for b in find(seq, 'waveblock') if b['attrs']['blockid'] in blocks] or [np.zeros(0, '<f4')])
            tl = int(round(clip['attrs'].get('trimLeft', 0) * rate)); tr = int(round(clip['attrs'].get('trimRight', 0) * rate))
            data = data[tl:len(data) - tr if tr else None]
            pieces.append((int(round(clip['attrs'].get('offset', 0) * rate)) + tl, data))
        end = max((o + len(x) for o, x in pieces), default=0)
        mix = np.zeros(end, dtype=np.float32)
        for o, x in pieces: mix[o:o+len(x)] += x
        name = f"{outdir}/track{ti + 1}.wav"
        with wave.open(name, 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
            w.writeframes((np.clip(mix, -1, 1) * 32767).astype('<i2').tobytes())
        print(name, rate, f'{end / rate:.2f}s', 'peak', float(np.abs(mix).max()) if end else 0)
        if '--split' in sys.argv:
            split(mix, rate, f'{outdir}/track{ti + 1}')
