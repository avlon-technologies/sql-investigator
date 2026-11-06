// acceptance/adapter.mjs — binds the 46 scenario steps to the REAL implementation.
//
// Every step drives the built SqlInvestigator.exe: as an MCP stdio child process (a JSON-RPC client below, the way
// Claude Code starts it) or as the local provisioning console (with the secret on stdin, never in argv). Fixtures are
// disposable SQL Server databases with synthetic data, created and dropped through tests/SqlInvestigator.FixtureTool
// on the local test server (SQLINVESTIGATOR_FIXTURE_SERVER, default 127.0.0.1,1433). Nothing here is an in-memory fake.
//
// THE CONTRACT (acceptance/run.mjs): createAdapter({ createFakeProvider, env }) → { steps, beforeScenario?, afterScenario? }

import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const clock = {
  now: (ctx) => ctx.state.now ?? new Date(),
  set: (ctx, when) => { ctx.state.now = new Date(when); return ctx.state.now; },
};

const here = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(here, '..', '..');
const CONFIG = process.env.SQLINVESTIGATOR_ACCEPTANCE_CONFIGURATION || 'Release';
const EXE = join(ROOT, 'src', 'SqlInvestigator.McpServer', 'bin', CONFIG, 'net10.0-windows', 'win-x64', 'SqlInvestigator.exe');
const FIXTURE_TOOL = join(ROOT, 'tests', 'SqlInvestigator.FixtureTool', 'bin', CONFIG, 'net10.0-windows', 'win-x64', 'SqlInvestigator.FixtureTool.exe');
const SECRET = 'fixture-secret';
const SERVER = process.env.SQLINVESTIGATOR_FIXTURE_SERVER || '127.0.0.1,1433';

const TOOLS = ['connection_list', 'connection_remove', 'schema_inspect', 'sql_validate', 'sql_execute', 'investigation_start',
  'investigation_record', 'investigation_get', 'knowledge_search', 'knowledge_propose', 'knowledge_review'];

// The narrowed scope a fixture alias gets: CustomerEmail is a real column the alias is NOT approved to read.
const ORDERS_SCOPE = 'dbo.Orders(Id,OrderId,Status,CreatedAt)';

// SQL Server's permission-denied errors: 229/230 object/column, 262 statement (CREATE), 297/300 server, 2760 schema.
const DENIED = new Set([229, 230, 262, 297, 300, 2760]);

let built = false;
function build() {
  if (built || process.env.SQLINVESTIGATOR_SKIP_BUILD === '1') { built = true; return; }
  const r = spawnSync('dotnet', ['build', join(ROOT, 'SqlInvestigator.slnx'), '-c', CONFIG, '-nologo', '-v', 'quiet'], { encoding: 'utf8' });
  if (r.status !== 0) throw new Error(`dotnet build failed:\n${r.stdout}\n${r.stderr}`);
  built = true;
}

// ---- the MCP stdio client --------------------------------------------------------------------------------------

class McpClient {
  constructor(env) {
    this.stdoutLines = [];
    this.stderr = '';
    this.pending = new Map();
    this.nextId = 1;
    this.child = spawn(EXE, [], { env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.exited = new Promise((res) => this.child.on('exit', (code) => res(code)));
    let buffer = '';
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (chunk) => {
      buffer += chunk;
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl).replace(/\r$/, '');
        buffer = buffer.slice(nl + 1);
        if (line.length) this.onLine(line);
      }
    });
    this.child.stderr.setEncoding('utf8');
    this.child.stderr.on('data', (chunk) => { this.stderr += chunk; });
  }

  onLine(line) {
    this.stdoutLines.push(line);
    let message;
    try { message = JSON.parse(line); } catch { return; } // recorded; mcp.stdout-protocol-only judges it
    if (message.id !== undefined && message.method) { // a request from the server (ping, …)
      this.send({ jsonrpc: '2.0', id: message.id, result: {} });
      return;
    }
    const waiter = this.pending.get(message.id);
    if (waiter) { this.pending.delete(message.id); waiter(message); }
  }

  send(message) { this.child.stdin.write(JSON.stringify(message) + '\n'); }

  request(method, params, { timeoutMs = 60000, id = this.nextId++ } = {}) {
    return new Promise((res, rej) => {
      const timer = setTimeout(() => { this.pending.delete(id); rej(new Error(`${method} (${id}) got no response in ${timeoutMs} ms`)); }, timeoutMs);
      this.pending.set(id, (m) => { clearTimeout(timer); res(m); });
      this.send({ jsonrpc: '2.0', id, method, params });
    });
  }

  async initialize() {
    const r = await this.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'continuum-acceptance', version: '1' } });
    if (r.error) throw new Error(`initialize failed: ${JSON.stringify(r.error)}`);
    this.send({ jsonrpc: '2.0', method: 'notifications/initialized' });
    return r.result;
  }

  async call(name, args = {}, options = {}) {
    const r = await this.request('tools/call', { name, arguments: args }, options);
    if (r.error) return { protocolError: r.error, isError: true, text: JSON.stringify(r.error) };
    const text = r.result?.content?.map((c) => c.text ?? '').join('') ?? '';
    const data = r.result?.structuredContent ?? (text ? JSON.parse(text) : null);
    return { isError: r.result?.isError === true, data, text };
  }

  async close() {
    try { this.child.stdin.end(); } catch { /* already closed */ }
    const timer = setTimeout(() => this.child.kill(), 5000);
    await this.exited;
    clearTimeout(timer);
  }
}

// ---- helpers ---------------------------------------------------------------------------------------------------

function fixtureTool(args) {
  const r = spawnSync(FIXTURE_TOOL, args, { encoding: 'utf8', windowsHide: true });
  let out;
  try { out = JSON.parse(r.stdout.trim().split('\n').pop()); } catch { throw new Error(`fixture tool ${args[0]} printed no JSON: ${r.stdout} ${r.stderr}`); }
  if (!out.ok) throw new Error(`fixture tool ${args[0]} failed: ${out.error}`);
  return out;
}

function env(ctx, extra = {}) {
  return { ...process.env, SQLINVESTIGATOR_HOME: ctx.state.home, SQLINVESTIGATOR_LOG_LEVEL: 'Debug', ...extra };
}

/** Runs the local console, with any secret on stdin. Its output joins what the secret scans look at. */
function runConsole(ctx, args, stdin = '') {
  const r = spawnSync(EXE, args, { env: env(ctx), input: stdin, encoding: 'utf8', windowsHide: true, timeout: 120000 });
  ctx.state.consoleOutput.push(r.stdout ?? '', r.stderr ?? '');
  for (const a of args) if (a.includes(SECRET)) throw new Error('a secret reached the console argv');
  return r;
}

async function server(ctx, name = 'default', extraEnv = {}) {
  if (ctx.state.servers[name]) return ctx.state.servers[name];
  build();
  const client = new McpClient(env(ctx, extraEnv));
  ctx.state.servers[name] = client;
  ctx.state.clients.push(client);
  client.initResult = await client.initialize();
  return client;
}

/** Calls a tool and remembers the output (every MCP output is checked by the secret scans anyway, via stdout). */
async function call(ctx, name, args, { server: which = 'default', ...options } = {}) {
  const client = await server(ctx, which);
  const result = await client.call(name, args, options);
  ctx.state.outputs.push(result.text);
  return result;
}

function createFixture(ctx, alias, kind, grant) {
  build();
  const f = fixtureTool(['create', '--name', alias.replace(/-/g, '_'), '--kind', kind, ...(grant ? ['--grant', grant] : [])]);
  ctx.state.fixtures.push(f);
  return f;
}

function provision(ctx, alias, fixture, scope, { secret = SECRET, serverName = fixture.server, verify = true, replace = false } = {}) {
  const args = ['provision', '--alias', alias, '--server', serverName, '--database', fixture.database, '--user', fixture.login,
    '--environment', 'fixture', '--trust-server-certificate'];
  for (const s of scope) args.push('--scope', s);
  if (!verify) args.push('--no-verify');
  if (replace) args.push('--replace');
  const r = runConsole(ctx, args, secret + '\n');
  if (r.status !== 0) throw new Error(`provision ${alias} failed (${r.status}): ${r.stderr}`);
  ctx.state.aliases[alias] = { fixture, scope };
  return r;
}

/** connection.fixture: a disposable database, a SELECT-only principal and an alias provisioned at the console. */
function ensureFixture(ctx, alias, scopeArg = alias === 'beta' ? 'dbo.Shipments' : 'dbo.Orders', { fullScope = false } = {}) {
  if (ctx.state.aliases[alias]) return ctx.state.aliases[alias];
  if (/Shipments/i.test(scopeArg)) {
    const f = createFixture(ctx, alias, 'shipments', 'dbo.Shipments');
    provision(ctx, alias, f, ['dbo.Shipments']);
  } else {
    const scope = fullScope ? 'dbo.Orders' : ORDERS_SCOPE;
    const f = createFixture(ctx, alias, 'orders', scope);
    provision(ctx, alias, f, [scope]);
  }
  return ctx.state.aliases[alias];
}

function readJson(ctx, file) {
  const path = join(ctx.state.home, file);
  return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : null;
}

const history = (ctx) => readJson(ctx, 'history.json') ?? { queries: [], investigations: [], audit: [] };

function allStdout(ctx) { return ctx.state.clients.flatMap((c) => c.stdoutLines); }
function allStderr(ctx) { return ctx.state.clients.map((c) => c.stderr).join('\n') + '\n' + ctx.state.consoleOutput.join('\n'); }

function expectError(result, codes, label) {
  if (!result.isError) throw new Error(`${label}: expected a refusal (${codes.join('|')}) but the tool succeeded: ${result.text.slice(0, 300)}`);
  const code = result.data?.error?.code;
  if (!codes.includes(code)) throw new Error(`${label}: expected ${codes.join("|")}, got ${code}: ${result.data?.error?.message ?? result.text.slice(0, 300)}`);
  return result.data.error;
}

function expectOk(result, label) {
  if (result.isError) throw new Error(`${label}: ${result.text.slice(0, 500)}`);
  return result.data;
}

function assertActionable(error, label) {
  for (const k of ['code', 'message', 'action']) {
    if (typeof error?.[k] !== 'string' || !error[k].trim()) throw new Error(`${label}: the error has no ${k}`);
  }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// The slow, valid query used for timeout, concurrency and cancellation: billions of rows to count.
const SLOW_SQL = 'SELECT COUNT_BIG(a.OrderId) AS n FROM dbo.Orders AS a CROSS JOIN dbo.Orders AS b CROSS JOIN dbo.Orders AS c WHERE (a.OrderId * b.OrderId + c.OrderId) % 7919 = 3';

// ---- the adapter -----------------------------------------------------------------------------------------------

export function createAdapter() {
  return {
    beforeScenario(ctx) {
      ctx.state = {
        home: mkdtempSync(join(tmpdir(), 'sqlinv-acceptance-')),
        servers: {}, clients: [], fixtures: [], aliases: {}, outputs: [], consoleOutput: [], rejections: [], labels: {},
      };
    },

    async afterScenario(ctx) {
      for (const c of ctx.state.clients ?? []) await c.close();
      for (const f of ctx.state.fixtures ?? []) {
        try { fixtureTool(['drop', '--server', f.server, '--database', f.database, '--login', f.login]); } catch (e) { ctx.log(`drop: ${e.message}`); }
      }
      if (ctx.state.home) rmSync(ctx.state.home, { recursive: true, force: true });
    },

    steps: {
      // ---- connections ----------------------------------------------------------------------------------------

      'connection.fixture': async (ctx, { alias, scope }) => {
        ensureFixture(ctx, alias, scope);
        await server(ctx);
      },

      'connection.provision': async (ctx, { alias, credential }) => {
        const f = createFixture(ctx, alias, 'orders', ORDERS_SCOPE);
        provision(ctx, alias, f, [ORDERS_SCOPE], { secret: credential });
        ctx.state.credential = credential;
      },

      'connection.list': async (ctx) => {
        ctx.state.listed = expectOk(await call(ctx, 'connection_list', {}), 'connection_list');
      },

      'connection.metadata-has-no-secret': async (ctx, { alias }) => {
        const entry = ctx.state.listed.connections.find((c) => c.alias === alias);
        if (!entry) throw new Error(`connection_list does not list ${alias}`);
        const allowed = new Set(['alias', 'server', 'database', 'provisionedAt', 'authentication', 'environment', 'scope']);
        for (const key of Object.keys(entry)) if (!allowed.has(key)) throw new Error(`unexpected metadata field ${key}`);
        const text = JSON.stringify(ctx.state.listed);
        if (text.includes(ctx.state.credential ?? SECRET)) throw new Error('the listed metadata contains the secret');
        if (/password|pwd|secret|credential/i.test(text)) throw new Error('the listed metadata names a credential field');
      },

      'credential.dpapi-current-user': async (ctx, { alias }) => {
        const file = join(ctx.state.home, 'vault', `${alias}.dpapi`);
        if (!existsSync(file)) throw new Error(`no vault file for ${alias}`);
        const v = fixtureTool(['vault-check', '--file', file, '--alias', alias, '--expected', ctx.state.credential ?? SECRET]);
        if (!v.dpapiCurrentUser) throw new Error('the vault file is not a DPAPI CurrentUser blob');
        if (!v.decryptsForAlias) throw new Error('the vault blob does not decrypt (only) with its alias entropy for this user');
        if (!v.fileCurrentUserOnly || !v.directoryCurrentUserOnly) throw new Error('the vault file or directory is not restricted to the current user');
        for (const f of ['catalog.json']) {
          if (!fixtureTool(['acl-check', '--path', join(ctx.state.home, f)]).currentUserOnly) throw new Error(`${f} is not current-user-only`);
        }
        const catalog = readFileSync(join(ctx.state.home, 'catalog.json'), 'utf8');
        if (catalog.includes(ctx.state.credential ?? SECRET)) throw new Error('the catalog holds the secret');
      },

      'connection.verify-local-only-provisioning': async (ctx) => {
        const client = await server(ctx);
        const list = await client.request('tools/list', {});
        for (const tool of list.result.tools) {
          if (/provision|credential|secret|password/i.test(tool.name)) throw new Error(`tool ${tool.name} looks like provisioning`);
          const props = Object.keys(tool.inputSchema?.properties ?? {});
          const bad = props.filter((p) => /password|pwd|secret|credential|connection.?string|user(name)?$/i.test(p));
          if (bad.length) throw new Error(`tool ${tool.name} accepts ${bad.join(', ')}`);
        }
        const r = await client.request('tools/call', { name: 'provision', arguments: { alias: 'x', password: 'p' } });
        const refused = r.error || r.result?.isError;
        if (!refused) throw new Error('a "provision" tool call was not refused');
        if (allStdout(ctx).some((l) => l.includes(SECRET))) throw new Error('an MCP output carried the fixture secret');
      },

      'connection.exercise-error-paths': async (ctx) => {
        const base = ctx.state.aliases.alpha.fixture;
        // wrong password, checked at the console: refused, nothing stored
        const wrong = runConsole(ctx, ['provision', '--alias', 'alpha-bad', '--server', base.server, '--database', base.database, '--user', base.login,
          '--environment', 'fixture', '--scope', ORDERS_SCOPE, '--trust-server-certificate'], 'not-the-password\n');
        if (wrong.status === 0) throw new Error('the console accepted a wrong password');
        // wrong password stored without verification, then used over MCP
        provision(ctx, 'alpha-wrongpw', base, [ORDERS_SCOPE], { secret: 'not-the-password', verify: false });
        expectError(await call(ctx, 'sql_execute', { alias: 'alpha-wrongpw', sql: 'SELECT OrderId FROM dbo.Orders' }), ['login.failed'], 'wrong password');
        // the REAL secret against an unreachable server: the secret is in play on the failure path
        provision(ctx, 'alpha-down', base, [ORDERS_SCOPE], { serverName: '127.0.0.1,1', verify: false });
        expectError(await call(ctx, 'sql_execute', { alias: 'alpha-down', sql: 'SELECT OrderId FROM dbo.Orders' }), ['server.unreachable'], 'unreachable');
        expectError(await call(ctx, 'schema_inspect', { alias: 'alpha-down' }), ['server.unreachable'], 'unreachable schema');
        // a working alias, a server-side error
        expectError(await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT CAST(Status AS int) AS s FROM dbo.Orders' }), ['sql.error'], 'conversion');
        expectOk(await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT TOP (2) OrderId FROM dbo.Orders' }), 'working alias');
        expectOk(await call(ctx, 'connection_list', {}), 'list');
        // removing an alias through MCP, and at the console (which also deletes its secret)
        expectOk(await call(ctx, 'connection_remove', { alias: 'alpha-down' }), 'remove');
        runConsole(ctx, ['remove', '--alias', 'alpha-wrongpw']);
      },

      // ---- one bounded SELECT --------------------------------------------------------------------------------

      'sql.execute': async (ctx, { alias, sql }) => {
        ctx.state.submitted = sql;
        ctx.state.evidence = expectOk(await call(ctx, 'sql_execute', { alias, sql }), 'sql_execute');
      },

      'sql.has-query-id': async (ctx) => {
        if (!/^q-[0-9a-f]{12}$/.test(ctx.state.evidence.queryId ?? '')) throw new Error(`no query id: ${ctx.state.evidence.queryId}`);
        const recorded = history(ctx).queries.find((q) => q.queryId === ctx.state.evidence.queryId);
        if (!recorded || recorded.status !== 'succeeded') throw new Error('the query id is not recorded as evidence in the history');
      },

      'sql.exact-statement': async (ctx) => {
        const e = ctx.state.evidence;
        if (e.sql !== ctx.state.submitted) throw new Error(`the result carries ${JSON.stringify(e.sql)}, not the submitted statement`);
        const recorded = history(ctx).queries.find((q) => q.queryId === e.queryId);
        if (recorded.sql !== ctx.state.submitted) throw new Error('the history records a different statement');
        if (!e.startedAt || !e.completedAt || e.alias !== 'alpha') throw new Error('the evidence lacks its alias or timestamps');
      },

      'sql.within-result-budget': async (ctx) => {
        const e = ctx.state.evidence;
        if (e.rows.length !== e.rowCount || e.rowCount > e.rowLimit) throw new Error(`${e.rowCount} rows exceed the ${e.rowLimit}-row budget`);
        const bytes = Buffer.byteLength(JSON.stringify(e.rows));
        if (bytes > e.byteLimit) throw new Error(`${bytes} bytes exceed the ${e.byteLimit}-byte budget`);
        if (e.rowLimit !== 500) throw new Error(`the default row budget is ${e.rowLimit}, not 500`);
      },

      'sql.has-truncation-indicator': async (ctx) => {
        const e = ctx.state.evidence;
        if (typeof e.truncated !== 'boolean') throw new Error('no truncation indicator');
        // the fixture holds 2000 orders, so the 500-row budget must have cut this result
        if (!e.truncated || e.truncatedBy !== 'rows') throw new Error(`2000 rows were not reported truncated (truncated=${e.truncated}, by=${e.truncatedBy})`);
      },

      // ---- rejection before execution ------------------------------------------------------------------------

      'sql.reject-corpus': async (ctx, { statements }) => {
        for (const sql of statements) {
          const verdict = expectOk(await call(ctx, 'sql_validate', { alias: 'alpha', sql }), `sql_validate ${sql}`);
          if (verdict.accepted) throw new Error(`sql_validate accepted: ${sql}`);
          const r = await call(ctx, 'sql_execute', { alias: 'alpha', sql });
          ctx.state.rejections.push({ label: sql, error: expectError(r, ['sql.rejected'], sql) });
        }
      },

      'sql.reject-policy-corpus': async (ctx, { cases }) => {
        const statements = {
          'parser-ambiguity': ['alpha', 'SELECT "OrderId" FROM dbo.Orders'],
          'forbidden-table': ['alpha', 'SELECT Id FROM dbo.Customers'],
          'forbidden-column': ['alpha', 'SELECT CustomerEmail FROM dbo.Orders'],
          'unauthorized-alias': ['gamma', 'SELECT OrderId FROM dbo.Orders'],
          openrowset: ['alpha', "SELECT a.x FROM OPENROWSET('SQLNCLI', 'Server=(local);Trusted_Connection=yes;', 'SELECT 1 AS x') AS a"],
          'cross-database-reference': ['alpha', 'SELECT name FROM master.dbo.sysdatabases'],
          'policy-unavailable': ['alpha', 'SELECT OrderId FROM dbo.Orders'],
        };
        const expected = { 'unauthorized-alias': ['alias.unknown'], 'policy-unavailable': ['policy.unavailable'] };
        for (const name of cases) {
          const [alias, sql] = statements[name] ?? (() => { throw new Error(`unknown policy case ${name}`); })();
          let r;
          if (name === 'policy-unavailable') {
            const file = join(ctx.state.home, 'catalog.json');
            const saved = readFileSync(file);
            writeFileSync(file, '{ "connections": [ this is not a policy');
            try { r = await call(ctx, 'sql_execute', { alias, sql }); } finally { writeFileSync(file, saved); }
          } else {
            r = await call(ctx, 'sql_execute', { alias, sql });
          }
          ctx.state.rejections.push({ label: name, error: expectError(r, expected[name] ?? ['sql.rejected'], name) });
        }
      },

      'sql.all-rejected-before-execution': async (ctx) => {
        if (ctx.state.rejections.length === 0) throw new Error('nothing was submitted');
        for (const { label, error } of ctx.state.rejections) assertActionable(error, label);
        const h = history(ctx);
        if (h.queries.length !== 0) throw new Error(`${h.queries.length} statement(s) reached execution`);
        if (h.audit.some((a) => a.event === 'sql.started')) throw new Error('an execution was started');
        const audited = h.audit.filter((a) => a.event === 'sql.rejected' && a.reasons?.length);
        if (audited.length < ctx.state.rejections.length) throw new Error(`${audited.length} audited rejections for ${ctx.state.rejections.length} statements`);
        // and SQL Server agrees: the fixture is exactly as it was created
        const f = ctx.state.aliases.alpha.fixture;
        const at = ['--server', f.server, '--database', f.database, '--login', f.login];
        if (fixtureTool(['row-count', ...at, '--table', 'dbo.Orders']).rows !== 2000) throw new Error('dbo.Orders changed');
        for (const t of ['dbo.Evil', 'dbo.Copy']) if (fixtureTool(['table-exists', ...at, '--table', t]).exists) throw new Error(`${t} was created`);
      },

      'sql.principal-denies-writes': async (ctx, { alias, statements }) => {
        const f = ctx.state.aliases[alias].fixture;
        const at = ['--server', f.server, '--database', f.database, '--login', f.login];
        for (const sql of statements) {
          const r = fixtureTool(['exec-as', ...at, '--sql', sql]);
          if (r.succeeded) throw new Error(`the principal was allowed: ${sql}`);
          if (!DENIED.has(r.error)) throw new Error(`"${sql}" failed with error ${r.error}, not a permission denial: ${r.message}`);
        }
        if (fixtureTool(['row-count', ...at, '--table', 'dbo.Orders']).rows !== 2000) throw new Error('dbo.Orders changed');
        if (fixtureTool(['table-exists', ...at, '--table', 'dbo.Probe']).exists) throw new Error('dbo.Probe was created');
      },

      // ---- budgets -------------------------------------------------------------------------------------------

      'sql.exercise-budgets': async (ctx, { kinds }) => {
        const budgets = { SQL_TIMEOUT_SECONDS: '2', MAX_QUERY_ROWS: '10', MAX_RESULT_BYTES: '300', MAX_CONCURRENT_QUERIES: '1', MAX_QUERIES_PER_INVESTIGATION: '2' };
        await server(ctx, 'budgets', budgets);
        const on = { server: 'budgets' };
        const outcomes = {};
        for (const kind of kinds) {
          if (kind === 'timeout') {
            outcomes.timeout = await call(ctx, 'sql_execute', { alias: 'alpha', sql: SLOW_SQL }, on);
          } else if (kind === 'row') {
            outcomes.row = await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT OrderId FROM dbo.Orders' }, on);
          } else if (kind === 'byte') {
            outcomes.byte = await call(ctx, 'sql_execute', { alias: 'alpha', sql: "SELECT REPLICATE('x', 100) AS Pad, OrderId FROM dbo.Orders" }, on);
          } else if (kind === 'concurrency') {
            const slow = call(ctx, 'sql_execute', { alias: 'alpha', sql: SLOW_SQL }, on);
            await sleep(700);
            outcomes.concurrency = await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT TOP (1) OrderId FROM dbo.Orders' }, on);
            outcomes.concurrencyFirst = await slow;
          } else if (kind === 'investigation') {
            const inv = expectOk(await call(ctx, 'investigation_start', { question: 'budget probe' }, on), 'investigation_start');
            const runs = [];
            for (let i = 0; i < 3; i++) runs.push(await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT TOP (1) OrderId FROM dbo.Orders', investigationId: inv.investigationId }, on));
            outcomes.investigation = runs;
          } else {
            throw new Error(`unknown budget kind ${kind}`);
          }
        }
        ctx.state.budgets = outcomes;
      },

      'sql.budgets-enforced': async (ctx) => {
        const o = ctx.state.budgets;
        assertActionable(expectError(o.timeout, ['budget.timeout'], 'timeout'), 'timeout');
        const row = expectOk(o.row, 'row budget');
        if (!row.truncated || row.truncatedBy !== 'rows' || row.rowCount !== 10) throw new Error(`row budget: ${JSON.stringify({ t: row.truncated, by: row.truncatedBy, n: row.rowCount })}`);
        const byte = expectOk(o.byte, 'byte budget');
        if (!byte.truncated || byte.truncatedBy !== 'bytes' || Buffer.byteLength(JSON.stringify(byte.rows)) > 300) throw new Error(`byte budget: ${JSON.stringify({ t: byte.truncated, by: byte.truncatedBy, n: byte.rowCount })}`);
        assertActionable(expectError(o.concurrency, ['budget.concurrency'], 'concurrency'), 'concurrency');
        expectError(o.concurrencyFirst, ['budget.timeout'], 'the query that held the slot');
        const [a, b, c] = o.investigation;
        expectOk(a, 'investigation query 1');
        expectOk(b, 'investigation query 2');
        assertActionable(expectError(c, ['budget.investigation'], 'investigation budget'), 'investigation');
      },

      'audit.has-budget-events': async (ctx) => {
        const events = new Set(history(ctx).audit.map((a) => a.event));
        for (const e of ['budget.timeout', 'budget.rows', 'budget.bytes', 'budget.concurrency', 'budget.investigation']) {
          if (!events.has(e)) throw new Error(`no audit event ${e}`);
        }
      },

      // ---- correlation across databases ----------------------------------------------------------------------

      'investigation.correlate-separate': async (ctx, { aliases, key }) => {
        const [left, right] = aliases;
        const inv = expectOk(await call(ctx, 'investigation_start', { question: `Do ${left} orders and ${right} shipments agree on ${key}?` }), 'start');
        const shipments = expectOk(await call(ctx, 'sql_execute', { alias: right, sql: `SELECT ShipmentId, ${key} FROM dbo.Shipments`, investigationId: inv.investigationId }), right);
        const keys = [...new Set(shipments.rows.map((r) => r[1]))];
        const orders = expectOk(await call(ctx, 'sql_execute', { alias: left, sql: `SELECT ${key}, Status FROM dbo.Orders WHERE ${key} IN (${keys.join(', ')}) OR ${key} BETWEEN 1001 AND 1015`, investigationId: inv.investigationId }), left);

        // agent-side correlation over two separate bounded results
        const shipped = new Map();
        for (const [id, k] of shipments.rows) shipped.set(k, [...(shipped.get(k) ?? []), id]);
        const ordered = new Set(orders.rows.map((r) => r[0]));
        const labels = {};
        for (const k of ordered) labels[k] = !shipped.has(k) ? `unmatched: no ${right} shipment` : shipped.get(k).length > 1 ? `ambiguous: ${shipped.get(k).length} ${right} shipments` : 'matched';
        for (const k of shipped.keys()) if (!ordered.has(k)) labels[k] = `unmatched: no ${left} order`;
        ctx.state.correlation = labels;
        ctx.state.correlationQueries = [shipments.queryId, orders.queryId];
        const counts = Object.values(labels).reduce((m, l) => ({ ...m, [l.split(':')[0]]: (m[l.split(':')[0]] ?? 0) + 1 }), {});
        expectOk(await call(ctx, 'investigation_record', {
          investigationId: inv.investigationId, hypothesis: `every ${left} order has exactly one ${right} shipment`,
          queryIds: ctx.state.correlationQueries, finding: JSON.stringify(counts), uncertainty: 'fixture data only',
        }), 'record');
        // the one-statement form a careless agent might write is refused
        const crossDb = `SELECT o.${key} FROM ${ctx.state.aliases[left].fixture.database}.dbo.Orders AS o JOIN ${ctx.state.aliases[right].fixture.database}.dbo.Shipments AS s ON s.${key} = o.${key}`;
        expectError(await call(ctx, 'sql_execute', { alias: left, sql: crossDb }), ['sql.rejected'], 'cross-database join');
      },

      'investigation.no-cross-database-sql': async (ctx) => {
        const h = history(ctx);
        if (h.queries.length < 2) throw new Error('the correlation ran fewer than two queries');
        const databases = Object.values(ctx.state.aliases).map((a) => a.fixture.database.toLowerCase());
        for (const q of h.queries) {
          const named = databases.filter((d) => q.sql.toLowerCase().includes(d));
          if (named.length) throw new Error(`executed SQL names a database: ${q.sql}`);
          if (/\b\w+\.\w+\.\w+\.?\w*\s*(AS|$|\s|,)/i.test(q.sql.replace(/'[^']*'/g, ''))) throw new Error(`executed SQL uses a three-part name: ${q.sql}`);
          if (q.status !== 'succeeded') throw new Error(`query ${q.queryId} did not succeed`);
        }
        const perAlias = new Set(h.queries.map((q) => q.alias));
        if (!perAlias.has('alpha') || !perAlias.has('beta')) throw new Error('both aliases were not queried separately');
      },

      'investigation.labels-unmatched-and-ambiguous': async (ctx) => {
        const l = ctx.state.correlation;
        if (l[1001] !== 'matched') throw new Error(`1001 is ${l[1001]}`);
        if (!String(l[1011]).startsWith('ambiguous')) throw new Error(`1011 (shipped twice) is ${l[1011]}`);
        if (!String(l[9999]).startsWith('unmatched')) throw new Error(`9999 (no order) is ${l[9999]}`);
        if (!String(l[1015]).startsWith('unmatched')) throw new Error(`1015 (no shipment) is ${l[1015]}`);
      },

      // ---- history -------------------------------------------------------------------------------------------

      'investigation.start': async (ctx, { question }) => {
        ctx.state.question = question;
        ctx.state.investigation = expectOk(await call(ctx, 'investigation_start', { question }), 'investigation_start');
      },

      'investigation.record': async (ctx, args) => {
        ensureFixture(ctx, args.alias);
        const id = ctx.state.investigation.investigationId;
        const evidence = expectOk(await call(ctx, 'sql_execute', { alias: args.alias, sql: args.sql, investigationId: id }), 'evidence query');
        ctx.state.labels[args.queryId] = evidence.queryId;
        ctx.state.recorded = { ...args, queryId: evidence.queryId };
        expectOk(await call(ctx, 'investigation_record', {
          investigationId: id, hypothesis: args.hypothesis, queryIds: [evidence.queryId], aliases: [args.alias],
          finding: args.finding, uncertainty: args.uncertainty,
        }), 'investigation_record');
        // citing a query that never ran is refused
        expectError(await call(ctx, 'investigation_record', { investigationId: id, finding: 'x', queryIds: ['q-000000000000'] }), ['query.unknown'], 'invented query id');
      },

      'investigation.reopen': async (ctx) => {
        // a new server process: the history is read back from disk, not from memory
        for (const c of ctx.state.clients) await c.close();
        ctx.state.servers = {};
        ctx.state.reopened = await call(ctx, 'investigation_get', { investigationId: ctx.state.investigation.investigationId });
      },

      'investigation.history-complete': async (ctx) => {
        const view = expectOk(ctx.state.reopened, 'investigation_get');
        const r = ctx.state.recorded;
        if (view.investigation.question !== ctx.state.question) throw new Error('the question is missing');
        const entry = view.investigation.entries.find((e) => e.queryIds.includes(r.queryId));
        if (!entry) throw new Error('no entry cites the query');
        if (entry.hypothesis !== r.hypothesis || entry.finding !== r.finding || entry.uncertainty !== r.uncertainty) throw new Error('the hypothesis, finding or uncertainty differs');
        const q = view.queries.find((x) => x.queryId === r.queryId);
        if (!q || q.sql !== r.sql || q.alias !== r.alias || !q.startedAt || !q.completedAt) throw new Error('the query provenance is incomplete');
        const text = ctx.state.reopened.text + readFileSync(join(ctx.state.home, 'history.json'), 'utf8');
        if (/"rows"\s*:/.test(text) || /"columns"\s*:/.test(text)) throw new Error('raw result rows were stored or returned');
        if (/\b10\d\d\b/.test(JSON.stringify(view.investigation.entries) + JSON.stringify(view.queries.map(({ queryId, ...rest }) => rest)))) throw new Error('an OrderId value from the result appears in the history');
      },

      // ---- relationship review -------------------------------------------------------------------------------

      'knowledge.empty': async (ctx) => {
        const k = expectOk(await call(ctx, 'knowledge_search', { text: '' }), 'knowledge_search');
        if (k.relationships.length !== 0) throw new Error('the knowledge store is not empty');
      },

      'knowledge.propose': async (ctx, { source, target, evidence }) => {
        ctx.state.proposal = { source, target, evidence };
        const rel = expectOk(await call(ctx, 'knowledge_propose', { source, target, evidence, provenance: 'inferred from matching OrderId values' }), 'propose');
        if (rel.state !== 'proposed') throw new Error(`a new relationship is ${rel.state}`);
        ctx.state.relationshipId = rel.relationshipId;
        const found = expectOk(await call(ctx, 'knowledge_search', { text: 'OrderId' }), 'search');
        if (found.relationships.find((x) => x.relationshipId === rel.relationshipId)?.state !== 'proposed') throw new Error('the proposal is not stored as proposed');
      },

      'knowledge.reject': async (ctx) => {
        ctx.state.rationale = 'The developer rejects it: the OrderId overlap in the fixture is coincidental.';
        const rel = expectOk(await call(ctx, 'knowledge_review', { relationshipId: ctx.state.relationshipId, verdict: 'rejected', rationale: ctx.state.rationale }), 'review');
        if (rel.state !== 'rejected') throw new Error(`the reviewed relationship is ${rel.state}`);
      },

      'knowledge.rejected-not-promoted': async (ctx) => {
        expectError(await call(ctx, 'knowledge_review', { relationshipId: ctx.state.relationshipId, verdict: 'accepted', rationale: 'promote it' }), ['relationship.closed'], 'promotion');
        // a fresh process reads the store back
        for (const c of ctx.state.clients) await c.close();
        ctx.state.servers = {};
        const found = expectOk(await call(ctx, 'knowledge_search', { text: '' }), 'search');
        const rel = found.relationships.find((x) => x.relationshipId === ctx.state.relationshipId);
        if (rel?.state !== 'rejected') throw new Error(`the relationship is ${rel?.state}`);
        if (found.relationships.some((x) => x.state === 'accepted' || x.state === 'verified')) throw new Error('a relationship is reported as accepted or verified');
        ctx.state.retained = rel;
      },

      'knowledge.negative-knowledge-retained': async (ctx) => {
        const rel = ctx.state.retained;
        const p = ctx.state.proposal;
        if (rel.source !== p.source || rel.target !== p.target || rel.evidence !== p.evidence || !rel.provenance) throw new Error('the evidence was not kept');
        if (rel.review?.verdict !== 'rejected' || rel.review?.rationale !== ctx.state.rationale || !rel.review?.reviewedAt) throw new Error('the rejection was not kept');
      },

      // ---- secrets -------------------------------------------------------------------------------------------

      'output.excludes-secret': async (ctx, { value }) => {
        const lines = allStdout(ctx);
        if (lines.length === 0) throw new Error('no MCP output was produced to check');
        const hit = lines.find((l) => l.includes(value));
        if (hit) throw new Error(`an MCP output contains the secret: ${hit.slice(0, 200).replaceAll(value, '***')}`);
        if (ctx.state.consoleOutput.some((o) => o.includes(value))) throw new Error('the console printed the secret');
      },

      'logs.exclude-secret': async (ctx, { value }) => {
        const logs = allStderr(ctx);
        if (!logs.trim()) throw new Error('no diagnostics were produced to check');
        if (logs.includes(value)) throw new Error('a stderr diagnostic contains the secret');
      },

      'storage.excludes-plaintext-secret': async (ctx, { value }) => {
        const files = fixtureTool(['scan', '--root', ctx.state.home, '--value', value]).files;
        if (files.length) throw new Error(`plaintext secret in ${files.join(', ')}`);
        if (!existsSync(join(ctx.state.home, 'vault', 'alpha.dpapi'))) throw new Error('the scan saw no vault file');
      },

      // ---- schema drift --------------------------------------------------------------------------------------

      'schema.snapshot': async (ctx, { alias }) => {
        ensureFixture(ctx, alias, 'dbo.Orders', { fullScope: true });
        const first = expectOk(await call(ctx, 'schema_inspect', { alias }), 'schema_inspect');
        if (first.status !== 'captured') throw new Error(`the first inspection is ${first.status}`);
        const again = expectOk(await call(ctx, 'schema_inspect', { alias }), 'schema_inspect again');
        if (again.status !== 'current' || again.snapshot.capturedAt !== first.snapshot.capturedAt) throw new Error('an unchanged, fresh snapshot was not reused');
        ctx.state.before = first.snapshot;
      },

      'schema.fixture-change': async (ctx, { alias, change }) => {
        if (change !== 'rename-column') throw new Error(`unknown change ${change}`);
        const f = ctx.state.aliases[alias].fixture;
        fixtureTool(['rename-column', '--server', f.server, '--database', f.database, '--login', f.login, '--table', 'dbo.Orders', '--from', 'Status', '--to', 'OrderStatus']);
      },

      'schema.refresh-after-drift': async (ctx, { alias }) => {
        await sleep(20);
        ctx.state.after = expectOk(await call(ctx, 'schema_inspect', { alias }), 'schema_inspect after drift');
        if (ctx.state.after.status !== 'drifted' || !ctx.state.after.drift) throw new Error(`the drift was not detected (${ctx.state.after.status})`);
      },

      'schema.has-source-and-time': async (ctx) => {
        const s = ctx.state.after.snapshot;
        const db = ctx.state.aliases.alpha.fixture.database;
        if (!s.source.includes('sqlserver://') || !s.source.includes(db)) throw new Error(`the source is ${s.source}`);
        if (Number.isNaN(Date.parse(s.capturedAt)) || Date.parse(s.capturedAt) <= Date.parse(ctx.state.before.capturedAt)) throw new Error('the capture time did not advance');
        if (ctx.state.after.drift.previousCapturedAt !== ctx.state.before.capturedAt) throw new Error('the drift does not name the previous capture');
      },

      'schema.changed-column-visible': async (ctx) => {
        const columns = ctx.state.after.snapshot.detail.flatMap((o) => o.columns.map((c) => c.name));
        if (!columns.includes('OrderStatus')) throw new Error(`OrderStatus is not visible: ${columns.join(', ')}`);
        // and a query over the new name works while the old one fails with a pointer to schema_inspect
        expectOk(await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT TOP (1) OrderStatus FROM dbo.Orders' }), 'new column');
        const old = expectError(await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT TOP (1) Status FROM dbo.Orders' }), ['sql.error'], 'old column');
        if (!/schema_inspect/.test(old.action)) throw new Error('the error does not point at schema_inspect');
      },

      'schema.no-fabricated-results': async (ctx) => {
        const s = ctx.state.after.snapshot;
        const columns = s.detail.flatMap((o) => o.columns.map((c) => c.name));
        if (columns.includes('Status')) throw new Error('the renamed-away column is still reported');
        if (s.detail.some((o) => o.name !== 'dbo.Orders')) throw new Error('an object outside the scope is reported');
        if (s.missing.length) throw new Error(`objects reported missing: ${s.missing.join(', ')}`);
        if (!ctx.state.after.drift.removed.some((x) => x.includes('Status nvarchar'))) throw new Error('the drift does not report the removed column');
      },

      // ---- safe failures -------------------------------------------------------------------------------------

      'sql.exercise-failures': async (ctx, { kinds }) => {
        const f = ctx.state.aliases.alpha.fixture;
        const failures = [];
        for (const kind of kinds) {
          if (kind === 'unavailable-server') {
            provision(ctx, 'down', f, [ORDERS_SCOPE], { serverName: '127.0.0.1,1', verify: false });
            failures.push([kind, await call(ctx, 'sql_execute', { alias: 'down', sql: 'SELECT OrderId FROM dbo.Orders' }), ['server.unreachable']]);
          } else if (kind === 'invalid-credentials') {
            provision(ctx, 'wrongpw', f, [ORDERS_SCOPE], { secret: 'not-the-password', verify: false });
            failures.push([kind, await call(ctx, 'sql_execute', { alias: 'wrongpw', sql: 'SELECT OrderId FROM dbo.Orders' }), ['login.failed']]);
          } else if (kind === 'malformed-sql') {
            failures.push([kind, await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELEC OrderId FRM dbo.Orders' }), ['sql.rejected']]);
            failures.push([kind + ' (server error)', await call(ctx, 'sql_execute', { alias: 'alpha', sql: 'SELECT CAST(Status AS int) AS s FROM dbo.Orders' }), ['sql.error']]);
          } else if (kind === 'cancelled') {
            const client = await server(ctx);
            const id = 900001;
            const pending = client.request('tools/call', { name: 'sql_execute', arguments: { alias: 'alpha', sql: SLOW_SQL } }, { id, timeoutMs: 8000 });
            await sleep(1000);
            client.send({ jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId: id, reason: 'the developer cancelled it' } });
            const reply = await pending.catch(() => null); // a cancelled request need not be answered
            if (reply?.result && !reply.result.isError) throw new Error('a cancelled query returned a result');
            // the server records the outcome once SQL Server acknowledges the cancel; a slow runner can take a while
            let q;
            for (let i = 0; i < 60; i++) {
              q = history(ctx).queries.find((x) => x.sql === SLOW_SQL);
              if (q && q.status !== 'running') break;
              await sleep(500);
            }
            if (!q || q.status !== 'failed' || q.errorCode !== 'cancelled') throw new Error(`the cancelled query is recorded as ${JSON.stringify(q)}`);
            if (reply?.result) failures.push([kind, { isError: true, data: reply.result.structuredContent, text: JSON.stringify(reply.result) }, ['cancelled']]);
          } else {
            throw new Error(`unknown failure kind ${kind}`);
          }
        }
        ctx.state.failures = failures;
      },

      'sql.actionable-sanitized-errors': async (ctx) => {
        for (const [label, result, codes] of ctx.state.failures) {
          const error = expectError(result, codes, label);
          assertActionable(error, label);
          if (result.data.rows || result.data.columns) throw new Error(`${label}: a failure returned rows`);
          if (/data source|user id|password\s*=|pwd\s*=|initial catalog/i.test(result.text)) throw new Error(`${label}: the error carries connection details`);
        }
        if (history(ctx).queries.some((q) => q.status === 'failed' && q.rowCount !== 0)) throw new Error('a failed query recorded rows');
      },

      // ---- the MCP surface -----------------------------------------------------------------------------------

      'mcp.start-stdio': async (ctx) => {
        const client = await server(ctx);
        if (client.child.exitCode !== null) throw new Error('the server exited');
      },

      'mcp.exercise-routine-tools': async (ctx) => {
        ensureFixture(ctx, 'alpha');
        const exercised = [];
        const run = async (name, args) => { exercised.push(name); return call(ctx, name, args); };
        expectOk(await run('connection_list', {}), 'connection_list');
        expectOk(await run('schema_inspect', { alias: 'alpha' }), 'schema_inspect');
        expectOk(await run('sql_validate', { alias: 'alpha', sql: 'SELECT OrderId FROM dbo.Orders' }), 'sql_validate');
        const inv = expectOk(await run('investigation_start', { question: 'routine' }), 'investigation_start');
        const q = expectOk(await run('sql_execute', { alias: 'alpha', sql: 'SELECT TOP (3) OrderId FROM dbo.Orders', investigationId: inv.investigationId }), 'sql_execute');
        expectOk(await run('investigation_record', { investigationId: inv.investigationId, finding: 'three orders', queryIds: [q.queryId] }), 'investigation_record');
        expectOk(await run('investigation_get', { investigationId: inv.investigationId }), 'investigation_get');
        const rel = expectOk(await run('knowledge_propose', { source: 'alpha.Orders.OrderId', target: 'beta.Shipments.OrderId', evidence: q.queryId }), 'knowledge_propose');
        expectOk(await run('knowledge_search', { text: 'OrderId' }), 'knowledge_search');
        expectOk(await run('knowledge_review', { relationshipId: rel.relationshipId, verdict: 'accepted', rationale: 'routine check' }), 'knowledge_review');
        expectOk(await run('connection_remove', { alias: 'no-such-alias' }), 'connection_remove');
        if (new Set(exercised).size !== TOOLS.length) throw new Error('not every tool was exercised');
      },

      'mcp.stdout-protocol-only': async (ctx) => {
        const lines = allStdout(ctx);
        if (lines.length < 5) throw new Error('too little stdout to judge');
        for (const line of lines) {
          let m;
          try { m = JSON.parse(line); } catch { throw new Error(`stdout carries a non-JSON line: ${line.slice(0, 120)}`); }
          if (m.jsonrpc !== '2.0' || (!('id' in m) && !m.method)) throw new Error(`stdout carries a non-protocol message: ${line.slice(0, 120)}`);
        }
        if (!allStderr(ctx).includes('"level"')) throw new Error('no structured diagnostics reached stderr');
      },

      'mcp.tool-surface-exact': async (ctx) => {
        const client = await server(ctx);
        const list = await client.request('tools/list', {});
        const names = list.result.tools.map((t) => t.name).sort();
        if (JSON.stringify(names) !== JSON.stringify([...TOOLS].sort())) throw new Error(`advertised tools: ${names.join(', ')}`);
        const offered = Object.keys(client.initResult.capabilities ?? {}).filter((c) => !["tools", "logging"].includes(c));
        if (offered.length) throw new Error(`the server offers more than tools: ${offered.join(", ")}`);
      },
    },
  };
}
