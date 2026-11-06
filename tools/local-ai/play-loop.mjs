// Rimtera playtest driver: no MCP and no tool-calling needed.
// Loop: observe the game -> ask the local Ollama model for ONE json action -> send it to the play bridge -> repeat.
// Usage: node tools/local-ai/play-loop.mjs --model steward:1 --minutes 20 --session play-123 --goal "..." [--port 8780] [--max 300]
import { existsSync, mkdirSync, appendFileSync, readFileSync } from 'node:fs';
const JSONL = (f, o) => appendFileSync(f, JSON.stringify(o) + '\n');
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, v, i, a) => (v.startsWith('--') ? [...acc, [v.slice(2), a[i + 1]]] : acc), []));
const MODEL = argv.model || 'steward:1';
const MINUTES = Number(argv.minutes || 20);
const MAX = Number(argv.max || 300);
const PORT = argv.port || 8780;
const ALLOW_CONSOLE = argv['allow-console'] !== undefined;
const SESSION = (argv.session || 'play').toLowerCase().replace(/[^a-z0-9]+/g, '-');
const GOAL = argv.goal || 'Play the early game: gather wood and stone, make a workbench and tools, survive the first night. Note anything wrong.';
const OLLAMA = process.env.OLLAMA_HOST_URL || 'http://127.0.0.1:11434';
const BASE = `http://127.0.0.1:${PORT}`;
const STOP = path.join(ROOT, '.agent-stop');
const noteFile = path.join(ROOT, 'docs', 'playtests', `${new Date().toISOString().slice(0, 10)}-${SESSION}.md`);
const stepFile = path.join(ROOT, 'logs', `${SESSION}-steps.jsonl`);
mkdirSync(path.dirname(stepFile), { recursive: true });
const log = (...a) => console.log(new Date().toLocaleTimeString(), ...a);

async function bridge(route, body) {
  const res = await fetch(BASE + route, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(120000),
  });
  const text = await res.text();
  return res.ok ? text : `HTTP ${res.status}: ${text}`;
}

const SYSTEM = `You are playing Rimtera, a 2D sandbox survival game, through a text interface. A human is watching the game window. Each turn you get the game state and must reply with ONE JSON object and nothing else.

Reply formats (pick exactly one per turn):
{"say":"short reason","do":"act","move":-1|0|1,"jump":false,"down":false,"action":"none|mine|useselected|eat|replaceselected|interact|sleep","target_x":int,"target_y":int,"frames":10-40}
{"say":"...","do":"select","slot":int}            pick an inventory slot (hotbar item in hand)
{"say":"...","do":"craft","id":"recipe-id"}       craft something listed under "craftable"
{"say":"...","do":"console","line":"give wood 10"}      dev console: OFF unless the session was started with --allow-console
{"say":"...","do":"note","kind":"bug|balance|ux|idea|progress","text":"one concrete sentence with tile coords and tick"}

Examples (copy the shape exactly; "do" must be one of: act, select, craft, console, note):
{"say":"walk right","do":"act","move":1,"jump":false,"down":false,"action":"none","frames":20}
{"say":"chop the tree","do":"act","move":0,"action":"mine","target_x":2104,"target_y":181,"frames":30}
{"say":"hold the hatchet","do":"select","slot":2}
{"say":"workbench","do":"craft","id":"workbench"}
{"say":"saw it","do":"note","kind":"progress","text":"gathered 5 wood at (2104,181) tick 240"}

Never repeat the game state back to me, and never put the state's fields (tick, day, map, legend, creatures) in your reply: reply with one action and nothing else. The game state's line "act: Landfall" names the run's act; it is not an action. "action" means one of: none, mine, useselected, eat, replaceselected, interact, sleep.

Map: the line starting "map:" tells the tile coordinates of the top-left of the ASCII map. x grows to the right, y grows DOWN. '@' is you. To mine or place, set target_x/target_y to a tile within a few tiles of you and use action "mine" (with a pickaxe or axe selected) or "useselected". To walk, use move -1 (left) or 1 (right) with frames 10-40. Jump to climb over 1-tile steps.
Mining blocks (action "mine") only works when a pickaxe/drill is the SELECTED item (see the "selected slot" line). Trees are cut with the hatchet selected and action "useselected". If the "message:" line says "Select a drill to mine", use {"do":"select","slot":N} with the slot number of your pickaxe or drill from the inventory list, then mine again. If mining the same tile twice changes nothing (no new items), stop repeating it: walk somewhere else, try another slot, or write a "bug" note describing exactly what you did and what the message said. Use the NEAREST line to pick targets and to know which way to walk (move 1 = right, -1 = left).
Work toward the goal. Write a "note" whenever something looks like a bug, is too slow/fast, is confusing, or when you finish a milestone. Only report things you actually saw in the game state; never invent results. Do not explain at length: just play.`;


// Turn the ASCII map into a short "nearest things" list a small model can act on.
function nearest(obs) {
  try {
    const head = obs.match(/column 0 = tile x (-?\d+), row 0 = tile y (-?\d+)/);
    const you = obs.match(/you: tile \((-?\d+),(-?\d+)\)/);
    const leg = obs.match(/^legend: (.*)$/m);
    if (!head || !you || !leg) return '';
    const x0 = +head[1], y0 = +head[2], px = +you[1], py = +you[2];
    const names = {};
    for (const m of leg[1].matchAll(/(\S)=(.*?)(?=\s{2}\S=|\s*$)/g)) names[m[1]] = m[2].trim();
    const lines = obs.split(/\r?\n/);
    const start = lines.findIndex(l => l.startsWith('legend:')) + 1;
    const rows = lines.slice(start).filter(l => l.length > 0);
    const best = {};
    rows.forEach((row, ry) => [...row].forEach((ch, cx) => {
      const name = names[ch];
      if (!name || ch === '@' || /^(air|empty|you)/i.test(name)) return;
      const x = x0 + cx, y = y0 + ry, d = Math.abs(x - px) + Math.abs(y - py);
      if (!best[name] || d < best[name].d) best[name] = { x, y, d };
    }));
    const out = Object.entries(best).sort((a, b) => a[1].d - b[1].d).slice(0, 8)
      .map(([n, b]) => `${n} at (${b.x},${b.y}): ${Math.abs(b.x - px)} ${b.x >= px ? 'right' : 'left'}, ${Math.abs(b.y - py)} ${b.y >= py ? 'down' : 'up'}`);
    return out.length ? 'NEAREST: ' + out.join(' | ') + `  (you are at (${px},${py}); you can only mine within about 4 tiles)` : '';
  } catch { return ''; }
}

const PLAYBOOK = (() => { try { return readFileSync(path.join(ROOT, 'docs', 'AGENT_PLAYBOOK.md'), 'utf8'); } catch { return ''; } })();
const SYSTEM_FULL = SYSTEM + (PLAYBOOK ? '\n\nGAME REFERENCE:\n' + PLAYBOOK : '');

async function ask(messages) {
  const res = await fetch(OLLAMA + '/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ model: MODEL, messages, stream: false, think: false, format: 'json', options: { num_ctx: 32768, temperature: 0.4 } }),
    signal: AbortSignal.timeout(300000),
  });
  if (!res.ok) throw new Error(`Ollama HTTP ${res.status}: ${await res.text()}`);
  const content = (await res.json()).message?.content ?? '';
  return content.replace(/<think>[\s\S]*?<\/think>/g, '').trim();
}

function writeNote(kind, text) {
  mkdirSync(path.dirname(noteFile), { recursive: true });
  appendFileSync(noteFile, `- **${kind}** ${String(text).replace(/\r?\n/g, ' ')}\n`);
}

const ACTIONS = ['none', 'mine', 'useselected', 'eat', 'replaceselected', 'interact', 'sleep'];
const CORRECTION = 'Reply with ONE action object only, never a copy of the game state. ' +
  'For example {"do":"act","move":1,"action":"none","frames":20} or {"do":"select","slot":2}. ' +
  `"do" is one of act, select, craft, console, note; "action" is one of ${ACTIONS.join(', ')}. ` +
  'The line "act: Landfall" in the game state is the run act, not an action.';

function normalize(a) {
  // A small model sometimes answers with a copy of the game state (tick/day/map/legend) instead of an action.
  if (a && typeof a === 'object' && (a.tick !== undefined || a.map !== undefined || a.legend !== undefined))
    return { do: '_invalid', why: 'You replied with the game state instead of an action.' };
  const d = String(a.do ?? a.type ?? a.action_type ?? '').toLowerCase();
  const act = (extra) => ({ ...a, ...extra, do: 'act' });
  if (['move', 'walk', 'run', 'left', 'right', 'go'].includes(d)) {
    return act({ move: a.move ?? (d === 'left' ? -1 : d === 'right' ? 1 : a.direction === 'left' ? -1 : 1), action: a.action && a.action !== d ? a.action : 'none' });
  }
  if (['mine', 'dig', 'chop', 'break'].includes(d)) return act({ action: 'mine' });
  if (['place', 'use', 'useselected', 'build'].includes(d)) return act({ action: 'useselected' });
  if (['eat', 'interact', 'sleep'].includes(d)) return act({ action: d });
  if (['jump'].includes(d)) return act({ jump: true });
  if (['equip', 'hotbar', 'selectslot'].includes(d)) return { ...a, do: 'select' };
  if (['report', 'log', 'bug'].includes(d)) return { ...a, do: 'note', kind: a.kind ?? (d === 'bug' ? 'bug' : 'idea'), text: a.text ?? a.say };
  if (!d && (a.move !== undefined || a.action !== undefined)) return act({});
  return { ...a, do: d };
}

async function perform(raw) {
  const a = normalize(raw);
  switch (a.do) {
    case 'act': {
      const action = String(a.action ?? 'none').toLowerCase().replace(/[_\s-]/g, '');
      if (!ACTIONS.includes(action))
        return `"${a.action}" is not an action. ${CORRECTION}`;
      const body = {
        move: Number(a.move) || 0, jump: !!a.jump, down: !!a.down, action,
        frames: Math.min(600, Math.max(1, Math.round(Number(a.frames) || 15))),
      };
      if (a.target_x !== undefined && a.target_y !== undefined && Number.isFinite(Number(a.target_x)) && Number.isFinite(Number(a.target_y))) {
        body.target_x = Math.round(Number(a.target_x)); body.target_y = Math.round(Number(a.target_y));
      }
      return bridge('/act', body);
    }
    case 'select': return bridge('/select', { slot: Math.round(Number(a.slot)) });
    case 'craft': return bridge('/craft', { id: String(a.id) });
    case 'console':
      if (!ALLOW_CONSOLE) return 'The dev console is off in this session: play the game with your hands (act, select, craft).';
      return bridge('/console', { line: String(a.line) });
    case 'note': writeNote(String(a.kind || 'idea'), a.text || ''); return 'note saved';
    case '_invalid': return `${a.why} ${CORRECTION}`;
    default: return `unknown "do": "${a.do}". ${CORRECTION}`;
  }
}

if (PLAYBOOK) log('Loaded docs/AGENT_PLAYBOOK.md (' + PLAYBOOK.length + ' chars)');
log(`Playing as ${MODEL} for up to ${MINUTES} min / ${MAX} steps. Notes: ${path.relative(ROOT, noteFile)}`);
try { log('Bridge:', (await bridge('/health')).slice(0, 120)); } catch (e) { log('Cannot reach the game bridge at', BASE, '-', e.message); process.exit(2); }

const deadline = Date.now() + MINUTES * 60000;
const history = [];
let lastResult = '(none yet)';
let failures = 0;
let sameCount = 0, lastSig = '';
for (let step = 1; step <= MAX && Date.now() < deadline; step++) {
  if (existsSync(STOP)) { log('Stop file found. Done.'); break; }
  let obs;
  try { obs = await bridge('/observe', { half_width: 24, half_height: 10 }); failures = 0; }
  catch (e) { if (++failures >= 3) { log('Lost the game bridge:', e.message); break; } await new Promise(r => setTimeout(r, 2000)); continue; }
  if (obs.startsWith('no game yet')) { // the window is up but the world is still loading (the title menu is open)
    log(`step ${step}: waiting for the world to load...`);
    await new Promise(r => setTimeout(r, 3000));
    continue;
  }
  const sig = obs.split('\n').filter(l => /^(you:|inventory:|selected)/.test(l)).join('|');
  sameCount = sig === lastSig ? sameCount + 1 : 0; lastSig = sig;
  const stuck = sameCount >= 2 ? `WARNING: your last ${sameCount + 1} actions changed nothing (same position and inventory). Do something DIFFERENT now: read the "message:" line, select the right slot, walk to a different spot, or write a bug note.\n\n` : '';
  const user = `GOAL: ${GOAL}\n\n${stuck}Last result: ${lastResult}\n\nRecent steps:\n${history.slice(-6).join('\n') || '(none)'}\n\nGAME STATE:\n${obs}\n${nearest(obs)}\n\nReply with one JSON action.`;
  let raw, action;
  try {
    raw = await ask([{ role: 'system', content: SYSTEM_FULL }, { role: 'user', content: user }]);
    action = JSON.parse(raw);
  } catch (e) {
    log(`step ${step}: model reply unusable (${e.message}). ${raw ? raw.slice(0, 120) : ''}`);
    lastResult = 'Your last reply was not valid JSON. Reply with exactly one JSON object.';
    continue;
  }
  log(`step ${step}: ${action.do} ${JSON.stringify({ ...action, say: undefined, do: undefined })}  -- ${action.say || ''}`);
  try { lastResult = (await perform(action)).slice(0, 700); } catch (e) { lastResult = 'Error: ' + e.message; }
  log('   ->', lastResult.split('\n')[0].slice(0, 160));
  JSONL(stepFile, { step, t: new Date().toISOString(), say: action.say, action: { ...action, say: undefined }, result: lastResult.slice(0, 600), state: obs.split('\n').slice(0, 6).join(' | ').slice(0, 400), ...(step === 1 || step % 10 === 0 ? { fullObservation: obs } : {}) });
  history.push(`${step}. ${action.do} ${JSON.stringify({ ...action, say: undefined, do: undefined })} => ${lastResult.split('\n')[0].slice(0, 100)}`);
}
log('Finished. Step log: ' + path.relative(ROOT, stepFile) + (existsSync(noteFile) ? '  Notes: ' + path.relative(ROOT, noteFile) : '  (no notes were written)'));
