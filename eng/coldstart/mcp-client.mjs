#!/usr/bin/env node
// Minimal, generic MCP stdio client for the SQL Investigator cold-start evaluation.
//
// Written from the public Model Context Protocol specification only
// (JSON-RPC 2.0 messages, one per line, over the child's stdin/stdout:
// initialize -> notifications/initialized -> tools/list -> tools/call).
// It knows NOTHING about the server's implementation. Tool argument names are
// not documented in docs/USER-GUIDE.md, so arguments are derived at run time
// from each tool's advertised inputSchema using name heuristics; every
// argument object actually sent is written to the evidence so a reader can
// see what was guessed.
//
// Usage:
//   node mcp-client.mjs <mode> --exe <abs path> --out <dir> --state <file>
//        --alias <alias> [--scope <schema.object[(cols)]>]
// Modes:
//   first        initialize, tools/list, representative investigation
//   restart      new process: alias, history and a new query still work
//   conn-remove  call connection_remove on --alias (keychain item must stay)
//   removed      after console `remove`: alias gone, a query is refused
//
// Output: <out>/steps.tsv lines "STATUS<TAB>mode<TAB>title<TAB>evidence-file",
// one evidence file per step. Exit 0 if no step FAILED, 1 otherwise, 2 on crash.
// The client never sees the SQL password and strips SQLINV_SQL_PASSWORD from
// the server's environment.

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

// ---------------------------------------------------------------- arguments
function parseArgs(argv) {
  const o = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) { o[a.slice(2)] = argv[i + 1]; i++; } else o._.push(a);
  }
  return o;
}
const ARGS = parseArgs(process.argv.slice(2));
const MODE = ARGS._[0];
const OUT = ARGS.out;
const EXE = ARGS.exe;
const ALIAS = ARGS.alias;
const STATE = ARGS.state;
const SCOPE = ARGS.scope || '';
if (!MODE || !OUT || !EXE || !ALIAS || !STATE) {
  console.error('usage: mcp-client.mjs <mode> --exe P --out D --state F --alias A [--scope S]');
  process.exit(2);
}
fs.mkdirSync(OUT, { recursive: true });
const STEPS = path.join(OUT, 'steps.tsv');
const STDERR_FILE = path.join(OUT, `${MODE}-server-stderr.log`);

const EXPECTED_TOOLS = [
  'connection_list', 'schema_inspect', 'sql_validate', 'sql_execute',
  'investigation_start', 'investigation_record', 'investigation_get',
  'knowledge_search', 'knowledge_propose', 'knowledge_review', 'connection_remove',
];

// ---------------------------------------------------------------- steps
let stepNo = 0;
let failures = 0;
const trunc = (s, n = 6000) => (s && s.length > n ? s.slice(0, n) + `\n... (${s.length} chars, truncated)` : s || '');
function step(status, title, evidence) {
  stepNo++;
  const f = path.join(OUT, `${MODE}-${String(stepNo).padStart(2, '0')}.txt`);
  fs.writeFileSync(f, typeof evidence === 'string' ? evidence : JSON.stringify(evidence, null, 2));
  fs.appendFileSync(STEPS, `${status}\t${MODE}\t${String(title).replace(/[\t\r\n]+/g, ' ')}\t${f}\n`);
  if (status === 'FAIL') failures++;
  console.error(`[${status}] ${MODE}: ${title}`);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------------------------------------------------------- transport
class McpProcess {
  constructor(exe, stderrFile) {
    const env = { ...process.env };
    delete env.SQLINV_SQL_PASSWORD;
    this.nextId = 1;
    this.pending = new Map();
    this.nonJson = [];
    this.notifications = [];
    this.transcript = [];
    this.exitInfo = null;
    this.spawnError = null;
    this.startedAt = Date.now();
    this.errStream = fs.createWriteStream(stderrFile, { flags: 'a' });
    // Launched exactly as `claude mcp add ... -- <abs path>` registers it: no arguments.
    this.proc = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'], env });
    this.proc.on('error', (e) => { this.spawnError = e; });
    this.proc.stdin.on('error', () => {});
    this.proc.stderr.pipe(this.errStream);
    let buf = '';
    this.proc.stdout.setEncoding('utf8');
    this.proc.stdout.on('data', (d) => {
      buf += d;
      let i;
      while ((i = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, i).replace(/\r$/, '');
        buf = buf.slice(i + 1);
        if (line.trim()) this.onLine(line);
      }
    });
    this.exited = new Promise((resolve) => {
      this.proc.on('close', (code, signal) => {
        if (buf.trim()) this.onLine(buf);
        this.exitInfo = { code, signal, afterMs: Date.now() - this.startedAt };
        for (const [, p] of this.pending) { clearTimeout(p.t); p.reject(new Error('server exited before answering')); }
        this.pending.clear();
        resolve(this.exitInfo);
      });
    });
  }
  onLine(line) {
    let msg;
    try { msg = JSON.parse(line); } catch { this.nonJson.push(line); return; }
    this.transcript.push({ dir: 'server->client', msg });
    if (msg.id !== undefined && msg.method === undefined && this.pending.has(msg.id)) {
      const p = this.pending.get(msg.id);
      this.pending.delete(msg.id);
      clearTimeout(p.t);
      p.resolve(msg);
      return;
    }
    if (msg.method && msg.id !== undefined) {
      // A server-to-client request. We advertise no client capabilities, so only ping is answered.
      const reply = msg.method === 'ping'
        ? { jsonrpc: '2.0', id: msg.id, result: {} }
        : { jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'Method not found' } };
      this.send(reply);
      return;
    }
    if (msg.method) this.notifications.push(msg);
  }
  send(obj) {
    this.transcript.push({ dir: 'client->server', msg: obj });
    this.proc.stdin.write(JSON.stringify(obj) + '\n');
  }
  request(method, params, timeoutMs = 90000) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      if (this.exitInfo) { reject(new Error('server already exited')); return; }
      const t = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`no response to ${method} within ${timeoutMs} ms`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, t });
      try { this.send({ jsonrpc: '2.0', id, method, params }); } catch (e) { clearTimeout(t); this.pending.delete(id); reject(e); }
    });
  }
  notify(method, params) {
    const m = { jsonrpc: '2.0', method };
    if (params) m.params = params;
    this.send(m);
  }
  // MCP stdio shutdown: close the server's stdin, wait for exit, then SIGTERM, then SIGKILL.
  async close(ms = 20000) {
    try { this.proc.stdin.end(); } catch {}
    let r = await Promise.race([this.exited, sleep(ms).then(() => null)]);
    if (r) return { ...r, how: 'exited after stdin closed' };
    this.proc.kill('SIGTERM');
    r = await Promise.race([this.exited, sleep(5000).then(() => null)]);
    if (r) return { ...r, how: `did not exit within ${ms} ms of stdin close; exited after SIGTERM` };
    this.proc.kill('SIGKILL');
    r = await this.exited;
    return { ...r, how: 'needed SIGKILL' };
  }
}

// ---------------------------------------------------------------- schema-driven arguments
function resolveSchema(s, root) {
  if (!s || typeof s !== 'object') return {};
  if (s.$ref && typeof s.$ref === 'string') {
    let x = root;
    for (const k of s.$ref.replace(/^#\//, '').split('/')) x = x ? x[k] : undefined;
    return resolveSchema(x, root);
  }
  const alts = s.anyOf || s.oneOf;
  if (Array.isArray(alts)) {
    const opts = alts.map((o) => resolveSchema(o, root));
    return { ...s, ...(opts.find((o) => o.type !== 'null') || opts[0] || {}), anyOf: undefined, oneOf: undefined };
  }
  return s;
}
function typeOf(s) {
  const t = s.type;
  if (Array.isArray(t)) return t.find((x) => x !== 'null');
  if (!t && s.properties) return 'object';
  if (!t && s.items) return 'array';
  return t;
}
// Names we fill whenever they appear (required or not).
const STRONG = [
  [/confirm|acknowledg|approved/i, () => true],
  [/alias|^connection(name)?$/i, (c) => c.alias],
  [/quer(y|ies)_?ids?$|citation|^cites?$|evidence_?ids?/i, (c) => c.qid],
  [/investigation_?id|^id$/i, (c) => c.invId],
  [/^sql$|sql_?text|^statement$|^query$|^query_?text$/i, (c) => c.sql],
  [/question/i, (c) => c.question],
];
// Content-like names filled even when optional, so a "record" call carries something.
const CONTENT = [
  [/hypothes/i, (c) => c.hypothesis],
  [/finding/i, (c) => c.finding],
  [/uncertain/i, (c) => c.uncertainty],
  [/title|summary|conclusion|note|statement|claim|text|description|content|body|detail/i, (c) => c.finding],
];
function pickEnum(e, prefer = []) {
  for (const p of prefer) {
    const m = e.find((x) => String(x).toLowerCase().includes(p));
    if (m !== undefined) return m;
  }
  return e[0];
}
function scalarFor(name, s, ctx, content) {
  const t = typeOf(s);
  if (s.enum) return pickEnum(s.enum, ctx.prefer);
  if (t === 'integer') return 1;
  if (t === 'number') return /confid/i.test(name) ? 0.5 : 1;
  if (t === 'boolean') return false;
  return content ? content[1](ctx) : ctx.finding;
}
function valueFor(name, s, root, ctx, required, depth) {
  const t = typeOf(s);
  if (s.const !== undefined) return s.const;
  for (const [re, f] of STRONG) {
    if (re.test(name)) {
      const v = f(ctx);
      if (v !== undefined && v !== null) {
        if (t === 'array') return [v];
        if (t === 'integer' || t === 'number') return Number(v);
        return v;
      }
      if (!required) return undefined;
      break;
    }
  }
  const content = CONTENT.find(([re]) => re.test(name));
  if (!required && !content) return undefined;
  if (depth > 4) return undefined;
  if (s.enum) return pickEnum(s.enum, ctx.prefer);
  if (t === 'object') return fillArgs(s, root, ctx, depth + 1);
  if (t === 'array') {
    const it = resolveSchema(s.items || {}, root);
    if (typeOf(it) === 'object') return [fillArgs(it, root, ctx, depth + 1)];
    return [scalarFor(name, it, ctx, content)];
  }
  return scalarFor(name, s, ctx, content);
}
function fillArgs(schema, root, ctx, depth = 0) {
  const s = resolveSchema(schema, root);
  const req = new Set(s.required || []);
  const out = {};
  for (const [name, ps] of Object.entries(s.properties || {})) {
    const v = valueFor(name, resolveSchema(ps, root), root, ctx, req.has(name), depth);
    if (v !== undefined) out[name] = v;
  }
  return out;
}

// ---------------------------------------------------------------- result helpers
function deepFind(obj, re, seen = new Set()) {
  if (!obj || typeof obj !== 'object' || seen.has(obj)) return undefined;
  seen.add(obj);
  const q = [obj];
  while (q.length) {
    const o = q.shift();
    for (const [k, v] of Object.entries(o)) {
      if (re.test(k) && (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean')) return v;
    }
    for (const v of Object.values(o)) if (v && typeof v === 'object' && !seen.has(v)) { seen.add(v); q.push(v); }
  }
  return undefined;
}
function hasExactString(obj, s) {
  if (obj === s) return true;
  if (!obj || typeof obj !== 'object') return false;
  return Object.values(obj).some((v) => hasExactString(v, s));
}
function mentionsAlias(c, alias) {
  if (c.data !== undefined && hasExactString(c.data, alias)) return true;
  const esc = alias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(^|[^A-Za-z0-9_-])${esc}([^A-Za-z0-9_-]|$)`).test(c.text || '');
}
function ok(c) { return !c.err && !c.rpcError && !c.isError && !c.missing; }
const KNOWN_CODE = /\b(alias\.unknown|login\.failed|server\.unreachable|principal\.unsafe|scope\.unapproved|sql\.rejected|permission\.denied|database\.unavailable|environment\.unapproved|credential\.[a-z-]+|budget\.[a-z-]+|store\.[a-z-]+|platform\.unsupported)\b/;
function errorCode(c) {
  const d = c.data !== undefined ? deepFind(c.data, /^(code|errorCode|error_code)$/i) : undefined;
  if (typeof d === 'string') return d;
  const m = KNOWN_CODE.exec((c.text || '') + ' ' + JSON.stringify(c.rpcError || ''));
  return m ? m[1] : undefined;
}
function validateVerdict(c) {
  if (!ok(c)) return 'rejected';
  const v = c.data !== undefined ? deepFind(c.data, /^(valid|isValid|accepted|isAccepted|ok|allowed)$/i) : undefined;
  if (v === true) return 'accepted';
  if (v === false) return 'rejected';
  if (/sql\.rejected/.test(c.text)) return 'rejected';
  return 'accepted (no explicit verdict field)';
}

let TOOLS = new Map();
async function callTool(srv, name, ctx) {
  const tool = TOOLS.get(name);
  if (!tool) return { name, missing: true, args: {}, text: '', isError: true };
  const schema = tool.inputSchema || {};
  const args = fillArgs(schema, schema, ctx);
  const c = { name, args };
  try { c.resp = await srv.request('tools/call', { name, arguments: args }); } catch (e) { c.err = e.message; }
  const r = c.resp && c.resp.result;
  c.rpcError = c.resp && c.resp.error;
  c.isError = !!(r && r.isError);
  c.text = ((r && r.content) || []).filter((x) => x.type === 'text').map((x) => x.text).join('\n');
  c.data = r && r.structuredContent;
  if (c.data === undefined) { try { c.data = JSON.parse(c.text); } catch {} }
  return c;
}
function ev(c, extra = '') {
  if (c.missing) return `tool ${c.name} is not in tools/list\n`;
  let s = `tools/call ${c.name}\narguments sent (derived from the advertised inputSchema):\n${JSON.stringify(c.args, null, 2)}\n`;
  if (c.err) s += `client error: ${c.err}\n`;
  if (c.rpcError) s += `JSON-RPC error: ${JSON.stringify(c.rpcError)}\n`;
  s += `isError: ${c.isError}\n`;
  const code = errorCode(c);
  if (code) s += `error/result code seen: ${code}\n`;
  s += `result text:\n${trunc(c.text)}\n`;
  const sc = c.resp && c.resp.result && c.resp.result.structuredContent;
  if (sc !== undefined) s += `structuredContent:\n${trunc(JSON.stringify(sc, null, 2))}\n`;
  return s + extra;
}

// ---------------------------------------------------------------- scope / SQL
function parseScope(s) {
  const m = /^\s*([A-Za-z_][\w]*)\.([A-Za-z_][\w]*)\s*(?:\(([^)]*)\))?\s*$/.exec(s || '');
  if (!m) return null;
  return { table: `${m[1]}.${m[2]}`, object: m[2], cols: m[3] ? m[3].split(',').map((x) => x.trim()).filter(Boolean) : [] };
}
function columnsFromSchema(data, object) {
  // Find arrays under a "columns"-like key; prefer the one near the scoped object's name.
  const found = [];
  (function walk(o) {
    if (!o || typeof o !== 'object') return;
    for (const [k, v] of Object.entries(o)) {
      if (/columns?/i.test(k) && Array.isArray(v)) found.push({ owner: o, cols: v });
      if (v && typeof v === 'object') walk(v);
    }
  })(data);
  const pick = found.find((f) => JSON.stringify(f.owner).includes(object)) || found[0];
  if (!pick) return [];
  return pick.cols
    .map((x) => (typeof x === 'string' ? x : x && (x.name || x.column || x.columnName)))
    .filter((x) => typeof x === 'string' && /^[A-Za-z_][A-Za-z0-9_]*$/.test(x))
    .slice(0, 3);
}
function sqlCandidates(scope, schemaCols) {
  if (!scope) return [];
  const c = [];
  if (scope.cols.length) c.push(`SELECT TOP (5) ${scope.cols.join(', ')} FROM ${scope.table}`);
  else {
    if (schemaCols.length) c.push(`SELECT TOP (5) ${schemaCols.join(', ')} FROM ${scope.table}`);
    c.push(`SELECT TOP (5) * FROM ${scope.table}`);
  }
  c.push(`SELECT COUNT(*) AS row_count FROM ${scope.table}`);
  return c;
}

// ---------------------------------------------------------------- session pieces
async function openSession() {
  const srv = new McpProcess(EXE, STDERR_FILE);
  await sleep(200);
  if (srv.spawnError) {
    step('FAIL', 'start the server process', `spawn ${EXE} failed: ${srv.spawnError.message}`);
    return null;
  }
  let init;
  try {
    init = await srv.request('initialize', {
      protocolVersion: '2025-06-18',
      capabilities: {},
      clientInfo: { name: 'sqlinv-coldstart-client', version: '1.0.0' },
    }, 60000);
  } catch (e) {
    step('FAIL', 'initialize handshake', `initialize failed: ${e.message}\nexit: ${JSON.stringify(srv.exitInfo)}\nnon-JSON stdout: ${srv.nonJson.join('\n')}`);
    await srv.close(5000);
    return null;
  }
  const r = init.result || {};
  step(init.error || !r.serverInfo ? 'FAIL' : 'PASS',
    'initialize handshake (server started with no arguments, as the Claude Code registration does)',
    { response: init, note: 'client sent protocolVersion 2025-06-18' });
  srv.notify('notifications/initialized');

  // tools/list, following nextCursor pagination.
  const tools = [];
  let cursor;
  try {
    do {
      const resp = await srv.request('tools/list', cursor ? { cursor } : {}, 60000);
      if (resp.error) throw new Error(JSON.stringify(resp.error));
      tools.push(...((resp.result && resp.result.tools) || []));
      cursor = resp.result && resp.result.nextCursor;
    } while (cursor);
  } catch (e) {
    step('FAIL', 'tools/list', `tools/list failed: ${e.message}`);
  }
  TOOLS = new Map(tools.map((t) => [t.name, t]));
  const names = tools.map((t) => t.name);
  const missing = EXPECTED_TOOLS.filter((n) => !names.includes(n));
  step(tools.length === 11 && missing.length === 0 ? 'PASS' : 'FAIL',
    `tools/list returns the 11 tools the user guide lists (got ${tools.length})`,
    `tools: ${names.join(', ')}\nmissing vs user guide: ${missing.join(', ') || '(none)'}\n\n` +
    `input schemas (the user guide does not document tool arguments; this is what the client had to work from):\n` +
    tools.map((t) => `--- ${t.name}\n${JSON.stringify(t.inputSchema, null, 2)}`).join('\n'));
  return srv;
}

async function closeSession(srv) {
  const exit = await srv.close(20000);
  step(exit.how === 'exited after stdin closed' ? 'PASS' : 'FAIL',
    'server stops cleanly when its stdin is closed',
    JSON.stringify(exit, null, 2));
  step(srv.nonJson.length === 0 ? 'PASS' : 'FAIL',
    'stdout carried only JSON-RPC messages',
    srv.nonJson.length ? `non-JSON stdout lines:\n${srv.nonJson.slice(0, 50).join('\n')}` : `${srv.transcript.filter((t) => t.dir === 'server->client').length} JSON messages from server, 0 other lines`);
  // stderr: user guide says diagnostics go to stderr as JSON lines.
  let errText = '';
  try { errText = fs.readFileSync(STDERR_FILE, 'utf8'); } catch {}
  const lines = errText.split(/\r?\n/).filter((l) => l.trim());
  const bad = lines.filter((l) => { try { JSON.parse(l); return false; } catch { return true; } });
  step(bad.length === 0 ? 'PASS' : 'FAIL',
    `server stderr is JSON lines (${lines.length} lines)`,
    (bad.length ? `non-JSON stderr lines:\n${bad.slice(0, 30).join('\n')}\n\n` : '') + `first lines:\n${lines.slice(0, 20).join('\n')}`);
  step('INFO', 'JSON-RPC transcript of this session', trunc(srv.transcript.map((t) => `${t.dir}: ${JSON.stringify(t.msg)}`).join('\n'), 40000));
}

function loadState() { try { return JSON.parse(fs.readFileSync(STATE, 'utf8')); } catch { return {}; } }
function saveState(s) { fs.writeFileSync(STATE, JSON.stringify(s, null, 2)); }

// ---------------------------------------------------------------- modes
async function modeFirst() {
  const srv = await openSession();
  if (!srv) return;
  const scope = parseScope(SCOPE);
  const question = `Cold-start evaluation ${new Date().toISOString()}: how many rows does ${scope ? scope.table : 'the scoped table'} hold, and what do they look like?`;
  const ctx = {
    alias: ALIAS, question,
    hypothesis: 'The scoped table is reachable through the read-only alias and holds synthetic rows.',
    finding: 'A bounded SELECT over the scoped table returned rows through the read-only alias.',
    uncertainty: 'Synthetic fixture data; row counts say nothing about any real system.',
    prefer: ['finding', 'hypothes', 'conclu', 'note'],
  };
  const state = { alias: ALIAS, question };

  // 1. connection_list
  let c = await callTool(srv, 'connection_list', ctx);
  step(ok(c) && mentionsAlias(c, ALIAS) ? 'PASS' : 'FAIL', `connection_list shows alias ${ALIAS}`, ev(c));

  // 2. schema_inspect
  c = await callTool(srv, 'schema_inspect', ctx);
  const okSchema = ok(c) && scope && (c.text || '').toLowerCase().includes(scope.object.toLowerCase());
  step(okSchema ? 'PASS' : 'FAIL', `schema_inspect on ${ALIAS} lists ${scope ? scope.table : '(no scope)'}`, ev(c));
  const schemaCols = scope ? columnsFromSchema(c.data, scope.object) : [];

  // 3. sql_validate: find a bounded SELECT the server accepts; then one it must reject.
  let sql;
  const tried = [];
  for (const cand of sqlCandidates(scope, schemaCols)) {
    const v = await callTool(srv, 'sql_validate', { ...ctx, sql: cand });
    const verdict = validateVerdict(v);
    tried.push(`SQL: ${cand}\nverdict: ${verdict}\n${ev(v)}`);
    if (verdict.startsWith('accepted')) { sql = cand; break; }
  }
  step(sql ? 'PASS' : 'FAIL', `sql_validate accepts a bounded SELECT${sql ? `: ${sql}` : ''}`, tried.join('\n=====\n'));
  if (scope) {
    const bad = `DELETE FROM ${scope.table}`;
    const v = await callTool(srv, 'sql_validate', { ...ctx, sql: bad });
    const verdict = validateVerdict(v);
    step(verdict === 'rejected' ? 'PASS' : 'FAIL', `sql_validate rejects "${bad}"`, `verdict: ${verdict}\n${ev(v)}`);
  }

  // 4. investigation_start
  c = await callTool(srv, 'investigation_start', ctx);
  const invId = c.data !== undefined ? deepFind(c.data, /^(investigation_?id|id)$/i) : undefined;
  step(ok(c) && invId ? 'PASS' : 'FAIL', `investigation_start returns an investigation id${invId ? ` (${invId})` : ''}`, ev(c));
  if (invId) { ctx.invId = String(invId); state.invId = String(invId); }

  // 5. sql_execute
  if (sql) {
    c = await callTool(srv, 'sql_execute', { ...ctx, sql });
    const qid = c.data !== undefined ? deepFind(c.data, /^(query_?id)$/i) : undefined;
    const rows = c.data !== undefined ? (Array.isArray(c.data.rows) ? c.data.rows.length : deepFind(c.data, /row_?count/i)) : undefined;
    step(ok(c) && qid ? 'PASS' : 'FAIL', `sql_execute runs the SELECT and returns a query id${qid ? ` (${qid}), rows: ${rows}` : ''}`, ev(c));
    if (qid) { ctx.qid = String(qid); state.qid = String(qid); state.sql = sql; }
  } else {
    step('BLOCKED', 'sql_execute', 'no SELECT was accepted by sql_validate, so nothing to execute');
  }

  // 6. investigation_record
  if (ctx.invId) {
    c = await callTool(srv, 'investigation_record', ctx);
    step(ok(c) ? 'PASS' : 'FAIL', 'investigation_record saves a finding citing the query id', ev(c));
    // 7. investigation_get
    c = await callTool(srv, 'investigation_get', ctx);
    const blob = (c.text || '') + JSON.stringify(c.data || '');
    const hasQ = blob.includes(question);
    const hasQid = !ctx.qid || blob.includes(ctx.qid);
    step(ok(c) && hasQ && hasQid ? 'PASS' : 'FAIL',
      `investigation_get reads it back (question: ${hasQ}, cites query id: ${hasQid})`, ev(c));
  } else {
    step('BLOCKED', 'investigation_record / investigation_get', 'no investigation id from investigation_start');
  }
  saveState(state);
  await closeSession(srv);
}

async function modeRestart() {
  const state = loadState();
  const srv = await openSession();
  if (!srv) return;
  const ctx = {
    alias: ALIAS, invId: state.invId, question: state.question,
    finding: 'After a server restart the same alias answered a new bounded SELECT.',
    hypothesis: 'History and the alias survive a restart.',
    uncertainty: 'Synthetic fixture data.',
    prefer: ['finding', 'hypothes', 'conclu', 'note'],
  };
  let c = await callTool(srv, 'connection_list', ctx);
  step(ok(c) && mentionsAlias(c, ALIAS) ? 'PASS' : 'FAIL', `after restart, connection_list still shows ${ALIAS}`, ev(c));

  if (state.invId) {
    c = await callTool(srv, 'investigation_get', ctx);
    const blob = (c.text || '') + JSON.stringify(c.data || '');
    const hasQ = blob.includes(state.question || '\u0000');
    const hasQid = !state.qid || blob.includes(state.qid);
    step(ok(c) && hasQ && hasQid ? 'PASS' : 'FAIL',
      `after restart, investigation ${state.invId} is still readable (question: ${hasQ}, first query id: ${hasQid})`, ev(c));
  } else {
    step('BLOCKED', 'after restart, investigation_get', 'the first session produced no investigation id');
  }

  if (state.sql) {
    c = await callTool(srv, 'sql_execute', { ...ctx, sql: state.sql });
    const qid2 = c.data !== undefined ? deepFind(c.data, /^(query_?id)$/i) : undefined;
    step(ok(c) && qid2 && String(qid2) !== state.qid ? 'PASS' : 'FAIL',
      `after restart, a new sql_execute works (new query id ${qid2})`, ev(c));
    if (qid2 && state.invId) {
      ctx.qid = String(qid2);
      c = await callTool(srv, 'investigation_record', ctx);
      step(ok(c) ? 'PASS' : 'FAIL', 'after restart, investigation_record appends a finding citing the new query', ev(c));
      c = await callTool(srv, 'investigation_get', ctx);
      const blob = (c.text || '') + JSON.stringify(c.data || '');
      step(ok(c) && blob.includes(String(qid2)) && (!state.qid || blob.includes(state.qid)) ? 'PASS' : 'FAIL',
        'investigation_get shows both the pre-restart and the post-restart query ids', ev(c));
    }
  } else {
    step('BLOCKED', 'after restart, sql_execute', 'the first session found no accepted SELECT');
  }
  await closeSession(srv);
}

async function modeConnRemove() {
  const srv = await openSession();
  if (!srv) return;
  const ctx = { alias: ALIAS, finding: 'cold-start evaluation: remove this alias', prefer: [] };
  let c = await callTool(srv, 'connection_list', ctx);
  step(ok(c) && mentionsAlias(c, ALIAS) ? 'PASS' : 'FAIL', `connection_list shows ${ALIAS} before connection_remove`, ev(c));
  c = await callTool(srv, 'connection_remove', ctx);
  step(ok(c) ? 'PASS' : 'FAIL', `connection_remove (the MCP tool) removes ${ALIAS}`, ev(c));
  c = await callTool(srv, 'connection_list', ctx);
  step(ok(c) && !mentionsAlias(c, ALIAS) ? 'PASS' : 'FAIL', `connection_list no longer shows ${ALIAS}`, ev(c));
  await closeSession(srv);
}

async function modeRemoved() {
  const state = loadState();
  const srv = await openSession();
  if (!srv) return;
  const ctx = { alias: ALIAS, invId: state.invId, sql: state.sql || 'SELECT 1 AS one', prefer: [] };
  let c = await callTool(srv, 'connection_list', ctx);
  step(ok(c) && !mentionsAlias(c, ALIAS) ? 'PASS' : 'FAIL', `after console remove, connection_list no longer shows ${ALIAS}`, ev(c));
  c = await callTool(srv, 'sql_execute', ctx);
  const code = errorCode(c);
  const refused = !ok(c);
  step(refused ? 'PASS' : 'FAIL',
    `after console remove, sql_execute on ${ALIAS} is refused (code: ${code || 'none found'})`,
    ev(c, `\nThe docs predict alias.unknown (catalog entry gone) or credential.unavailable (keychain item gone).\n`));
  if (state.invId) {
    c = await callTool(srv, 'investigation_get', ctx);
    step(ok(c) ? 'PASS' : 'INFO', 'investigation history is still readable after the alias was removed', ev(c));
  }
  await closeSession(srv);
}

const MODES = { first: modeFirst, restart: modeRestart, 'conn-remove': modeConnRemove, removed: modeRemoved };
if (!MODES[MODE]) { console.error(`unknown mode ${MODE}`); process.exit(2); }
try {
  await MODES[MODE]();
} catch (e) {
  step('FAIL', 'MCP client crashed', String(e && e.stack || e));
  process.exit(2);
}
process.exit(failures ? 1 : 0);
