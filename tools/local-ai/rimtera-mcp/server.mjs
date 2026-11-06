#!/usr/bin/env node
// Rimtera MCP server (stdio, no dependencies).
// Gives a local model the same gate CI uses: build, the provers, the audits, and a few read-only helpers.
// It never pushes, merges, deletes or runs GPU jobs. Start it from a client with:  node tools/local-ai/rimtera-mcp/server.mjs
import { spawn } from 'node:child_process';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const MAX_OUT = 6000; // characters returned to the model: the tail, where failures are
const DOTNET = process.env.DOTNET_PATH || 'dotnet';

function run(cmd, args, timeoutMs) {
  return new Promise((resolve) => {
    const started = Date.now();
    let out = '';
    let timedOut = false;
    const child = spawn(cmd, args, { cwd: ROOT, shell: process.platform === 'win32', windowsHide: true });
    const timer = setTimeout(() => { timedOut = true; child.kill(); }, timeoutMs);
    const take = (d) => { out += d.toString(); if (out.length > 400000) out = out.slice(-200000); };
    child.stdout.on('data', take);
    child.stderr.on('data', take);
    child.on('error', (e) => { clearTimeout(timer); resolve({ code: -1, out: String(e), ms: Date.now() - started, timedOut }); });
    child.on('close', (code) => { clearTimeout(timer); resolve({ code: timedOut ? -2 : code, out, ms: Date.now() - started, timedOut }); });
  });
}

const tail = (s) => (s.length > MAX_OUT ? '...(truncated)\n' + s.slice(-MAX_OUT) : s);
const fmt = (label, r) =>
  `${label}: ${r.code === 0 ? 'PASS' : r.timedOut ? 'TIMEOUT' : 'FAIL (exit ' + r.code + ')'} in ${(r.ms / 1000).toFixed(0)}s\n${r.code === 0 ? tail(r.out.split('\n').slice(-15).join('\n')) : tail(r.out)}`;

const build = () => run(DOTNET, ['build', 'Rimtera.csproj', '-nologo', '-v', 'q', '-clp:ErrorsOnly;Summary'], 600000);
const runFlag = (flags) => run(DOTNET, ['run', '--no-build', '--project', 'Rimtera.csproj', '--', ...flags], 900000);


// ---- the play bridge (docs/AGENT_PLAY.md): talks to a running `--agent-play` game window on loopback --------------------------
const PLAY_URL = `http://127.0.0.1:${process.env.RIMTERA_AGENT_PORT || 8780}`;
async function play(route, body, wantImage = false) {
  let res;
  try {
    res = await fetch(PLAY_URL + route, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(120000),
    });
  } catch (e) {
    return 'The game is not running. Start it with: .\\tools\\local-ai\\Play-Agent.ps1 (or: dotnet run -- --agent-play). ' + e.message;
  }
  if (wantImage && res.ok) return { image: Buffer.from(await res.arrayBuffer()).toString('base64') };
  const text = await res.text();
  return res.ok ? text : `HTTP ${res.status}: ${text}`;
}
const noteDir = () => path.join(ROOT, 'docs', 'playtests');

const tools = {
  build: {
    description: 'dotnet build Rimtera.csproj. Returns PASS or the compiler errors. Run after every edit.',
    schema: {},
    run: async () => fmt('build', await build()),
  },
  self_test: {
    description: 'Run the deterministic core suite (dotnet run -- --self-test). Needs a successful build first.',
    schema: {},
    run: async () => fmt('self-test', await runFlag(['--self-test'])),
  },
  prove: {
    description: 'The full local gate: build, --self-test, --batch-a-test, --demo. Stops at the first failure. Use before declaring a task done. (Clients prefix the server name: opencode shows it as rimtera_prove.)',
    schema: {},
    run: async () => {
      const steps = [['build', build], ['self-test', () => runFlag(['--self-test'])], ['batch-a-test', () => runFlag(['--batch-a-test'])], ['demo', () => runFlag(['--demo'])]];
      const lines = [];
      for (const [name, fn] of steps) {
        const r = await fn();
        lines.push(fmt(name, r));
        if (r.code !== 0) return lines.join('\n\n') + '\n\nSTOPPED: fix this before anything else.';
      }
      return lines.join('\n\n') + '\n\nALL PROVERS PASS';
    },
  },
  audit: {
    description: 'Run an audit that must stay clean: kind = "craft" (--craft-audit), "item" (--item-audit) or "assets" (--asset-check).',
    schema: { kind: { type: 'string', enum: ['craft', 'item', 'assets'] } },
    required: ['kind'],
    run: async ({ kind }) => {
      const flag = { craft: '--craft-audit', item: '--item-audit', assets: '--asset-check' }[kind];
      if (!flag) return 'kind must be craft, item or assets';
      return fmt(flag, await runFlag([flag]));
    },
  },
  next_step: {
    description: 'Read the "Where we are" section of docs/PLAN.md: the released version and the step to build next.',
    schema: {},
    run: async () => {
      const f = path.join(ROOT, 'docs', 'PLAN.md');
      if (!existsSync(f)) return 'docs/PLAN.md not found';
      const text = readFileSync(f, 'utf8');
      const i = text.indexOf('## Where we are');
      const j = text.indexOf('## Direction', i + 1);
      return text.slice(i, j > i ? j : i + 3000).trim();
    },
  },
  play_observe: {
    description: 'Look at the running game: tick, day and time, your tile and health, selected item, inventory, what you can craft, nearby creatures, and an ASCII map (@ you, ! hostile, & passive, G station AI; legend included). Row 0 / column 0 of the map are the tile coordinates printed in the "map:" line; y grows downward.',
    schema: { half_width: { type: 'number', description: 'tiles left/right of you, 8-60 (default 32)' }, half_height: { type: 'number', description: 'tiles up/down, 5-30 (default 14)' } },
    run: async (a) => play('/observe', a),
  },
  play_act: {
    description: 'Play for a few frames, then see the result. The world only moves while this runs (60 frames = 1 second). move: -1 left, 1 right. jump: press and hold jump. down: drop through platforms. action: none | mine | useselected | eat | replaceselected | interact | sleep. target_x/target_y: the tile to mine/place/interact with (absolute tile coordinates, within reach, a few tiles). Keep frames small (10-40) so you can react.',
    schema: {
      move: { type: 'number' }, jump: { type: 'boolean' }, down: { type: 'boolean' },
      action: { type: 'string', enum: ['none', 'mine', 'useselected', 'eat', 'replaceselected', 'interact', 'sleep'] },
      target_x: { type: 'integer' }, target_y: { type: 'integer' },
      frames: { type: 'integer', description: '1-600, default 15' },
    },
    run: async (a) => play('/act', a),
  },
  play_select: {
    description: 'Select an inventory slot (the hotbar item in hand) by its slot number from the inventory list.',
    schema: { slot: { type: 'integer' } }, required: ['slot'],
    run: async (a) => play('/select', a),
  },
  play_craft: {
    description: 'Craft a recipe by its id (see "craftable here" in play_observe). Needs the right station nearby.',
    schema: { id: { type: 'string' } }, required: ['id'],
    run: async (a) => play('/craft', a),
  },
  play_console: {
    description: 'Run a dev-console command (docs/DEV_CONSOLE.md), e.g. "help", "give <item> <n>", "time <hour>", "incident <id>". For setting up a test situation; say so in your notes when you use it, and do not use god mode unless testing it.',
    schema: { line: { type: 'string' } }, required: ['line'],
    run: async (a) => play('/console', a),
  },
  play_screenshot: {
    description: 'A screenshot of the game window (PNG). Only useful if your model can look at images; use it when the text view cannot tell you something (how it looks, UI problems).',
    schema: {},
    run: async () => play('/screenshot', undefined, true),
  },
  play_note: {
    description: 'Write a finding to docs/playtests/<date>-<session>.md. kind: bug | balance | ux | idea | progress. Include what happened, where (tile coords, tick) and how to reproduce it. Keep each note short and concrete.',
    schema: { session: { type: 'string', description: 'short slug for this play session' }, kind: { type: 'string', enum: ['bug', 'balance', 'ux', 'idea', 'progress'] }, text: { type: 'string' } },
    required: ['session', 'kind', 'text'],
    run: async ({ session, kind, text }) => {
      const { mkdirSync, appendFileSync } = await import('node:fs');
      const slug = String(session).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 40) || 'session';
      mkdirSync(noteDir(), { recursive: true });
      const file = path.join(noteDir(), `${new Date().toISOString().slice(0, 10)}-${slug}.md`);
      appendFileSync(file, `- **${kind}** ${String(text).replace(/\r?\n/g, ' ')}\n`);
      return `noted in docs/playtests/${path.basename(file)}`;
    },
  },
  git_status: {
    description: 'Current branch, short status and the last 8 commits. Read-only.',
    schema: {},
    run: async () => {
      const b = await run('git', ['rev-parse', '--abbrev-ref', 'HEAD'], 20000);
      const s = await run('git', ['status', '--short'], 20000);
      const l = await run('git', ['log', '--oneline', '-8'], 20000);
      return `branch: ${b.out.trim()}\n\nstatus:\n${s.out.trim() || '(clean)'}\n\nlog:\n${l.out.trim()}`;
    },
  },
};

const send = (msg) => process.stdout.write(JSON.stringify(msg) + '\n');
const ok = (id, result) => send({ jsonrpc: '2.0', id, result });
const err = (id, code, message) => send({ jsonrpc: '2.0', id, error: { code, message } });

async function handle(req) {
  const { id, method, params } = req;
  if (method === 'initialize') {
    return ok(id, {
      protocolVersion: params?.protocolVersion || '2024-11-05',
      capabilities: { tools: {} },
      serverInfo: { name: 'rimtera', version: '1.0.0' },
    });
  }
  if (method === 'ping') return ok(id, {});
  if (method === 'tools/list') {
    return ok(id, {
      tools: Object.entries(tools).map(([name, t]) => ({
        name,
        description: t.description,
        inputSchema: { type: 'object', properties: t.schema, required: t.required || [] },
      })),
    });
  }
  if (method === 'tools/call') {
    const t = tools[params?.name];
    if (!t) return err(id, -32602, `unknown tool ${params?.name}`);
    try {
      const out = await t.run(params.arguments || {});
      if (out && typeof out === 'object' && out.image)
        return ok(id, { content: [{ type: 'image', data: out.image, mimeType: 'image/png' }] });
      return ok(id, { content: [{ type: 'text', text: String(out) }] });
    } catch (e) {
      return ok(id, { content: [{ type: 'text', text: 'error: ' + e }], isError: true });
    }
  }
  if (id !== undefined && id !== null) return err(id, -32601, `method not found: ${method}`);
}

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buf += chunk;
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    try { handle(JSON.parse(line)); } catch (e) { err(null, -32700, 'parse error: ' + e); }
  }
});
