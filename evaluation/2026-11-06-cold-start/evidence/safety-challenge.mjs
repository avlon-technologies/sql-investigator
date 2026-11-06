// Phase E safety challenge against the INSTALLED release (C:\sqlinv-eval\safety\bin), over real MCP stdio.
// Writes results.json next to this file. Secrets come from secrets.json and are only used to scan for leaks.
import { spawn, execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const EXE = 'C:\\sqlinv-eval\\safety\\bin\\SqlInvestigator.exe';
const HOME = 'C:\\sqlinv-eval\\safety\\home';
const secrets = JSON.parse(readFileSync(join(here, 'secrets.json'), 'utf8').replace(/^\uFEFF/, ''));
const SA = readFileSync(join(process.env.LOCALAPPDATA, 'SqlInvestigator', 'dev-fixture', 'sa-password.txt'), 'utf8').trim();
const results = [];
const transcript = [];   // every MCP response, for the leak scan
const stderrAll = [];
const nonJsonStdout = [];

function sa(sql, user = 'sa', pw = SA, db = 'SafetyProbe') {
  try {
    return { ok: true, out: execFileSync('docker', ['exec', '-e', 'SQLCMDPASSWORD', 'sqlinvestigator-mssql', '/opt/mssql-tools18/bin/sqlcmd', '-C', '-S', 'localhost', '-U', user, '-d', db, '-b', '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + sql],
      { env: { ...process.env, SQLCMDPASSWORD: pw, MSYS_NO_PATHCONV: '1' }, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim() };
  } catch (e) { return { ok: false, out: ((e.stdout ?? '') + (e.stderr ?? '')).trim() }; }
}

class Server {
  constructor(env = {}) {
    this.p = spawn(EXE, [], { env: { ...process.env, SQLINVESTIGATOR_HOME: HOME, ...env }, stdio: ['pipe', 'pipe', 'pipe'] });
    this.buf = ''; this.id = 0; this.waiting = new Map(); this.stdoutNonJson = [];
    this.p.stdout.on('data', (d) => { this.buf += d; let n; while ((n = this.buf.indexOf('\n')) >= 0) { const line = this.buf.slice(0, n); this.buf = this.buf.slice(n + 1); if (!line.trim()) continue; let m; try { m = JSON.parse(line); } catch { nonJsonStdout.push(line); continue; } transcript.push(line); this.waiting.get(m.id)?.(m); } });
    this.p.stderr.on('data', (d) => stderrAll.push(String(d)));
  }
  send(method, params) { const i = ++this.id; const pr = new Promise((res) => this.waiting.set(i, res)); this.p.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: i, method, params }) + '\n'); return { id: i, promise: pr }; }
  notify(method, params) { this.p.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n'); }
  async init() { await this.send('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'safety', version: '1' } }).promise; this.notify('notifications/initialized'); return this; }
  async call(name, args) { const r = await this.send('tools/call', { name, arguments: args }).promise; return r.result?.structuredContent ?? r.result ?? r.error; }
  close() { this.p.stdin.end(); this.p.kill(); }
}

const summarize = (r) => ({ ok: r?.ok ?? !(r?.error || r?.code), code: r?.error?.code ?? r?.code, accepted: r?.accepted, reasons: r?.reasons ?? r?.error?.reasons, message: r?.error?.message ?? r?.message, action: r?.error?.action ?? r?.action, rows: r?.rows?.length, truncated: r?.truncated, truncatedBy: r?.truncatedBy, queryId: r?.queryId });
function record(group, id, what, expectation, outcome, pass, detail) { results.push({ group, id, what, expectation, pass, outcome, detail }); console.log(`${pass ? 'PASS' : 'FAIL'} ${id} ${what} -> ${outcome}`); }

async function validatorCase(s, id, what, sql, alias = 'probe') {
  const v = await s.call('sql_validate', { alias, sql });
  const x = await s.call('sql_execute', { alias, sql });
  const vs = summarize(v), xs = summarize(x);
  const pass = v?.accepted === false && xs.code === 'sql.rejected';
  record('allowlist', id, what, 'rejected by sql_validate and sql_execute (sql.rejected)', `validate accepted=${v?.accepted}; execute ${xs.code ?? 'EXECUTED'}`, pass, { reasons: vs.reasons ?? xs.reasons });
}

const SLOW = 'SELECT COUNT_BIG(*) AS c FROM dbo.Big AS a CROSS JOIN dbo.Big AS b CROSS JOIN dbo.Big AS c WHERE a.n + b.n + c.n = -1';
const runningAs = (login) => sa(`SELECT COUNT(*) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id WHERE s.login_name = '${login}'`, 'sa', SA, 'master').out;

// ------------------------------------------------------------------------------------------------ main server
const s = await new Server().init();
const tools = (await s.send('tools/list', {}).promise).result.tools;
record('surface', 'T1', 'tools/list', 'exactly 11 tools, no credential parameters', tools.map((t) => t.name).join(','), tools.length === 11 && !tools.some((t) => /password|secret|credential|connectionstring/i.test(JSON.stringify(t.inputSchema))), null);

// control: an allowed query works
{ const r = summarize(await s.call('sql_execute', { alias: 'probe', sql: 'SELECT OrderId, Status FROM dbo.Orders WHERE OrderId < 3' })); record('control', 'C0', 'allowed SELECT', 'executes, 2 rows', `${r.code ?? 'ok'} rows=${r.rows}`, r.rows === 2, null); }

const writes = [
  ['W1', 'INSERT', "INSERT INTO dbo.Orders (OrderId, Status, CardToken) VALUES (999, N'x', N'y')"],
  ['W2', 'UPDATE', "UPDATE dbo.Orders SET Status = N'Hacked'"],
  ['W3', 'DELETE', 'DELETE FROM dbo.Orders'],
  ['W4', 'MERGE', "MERGE dbo.Orders AS t USING (SELECT 1 AS id) AS s ON t.OrderId = s.id WHEN MATCHED THEN DELETE;"],
  ['W5', 'DROP TABLE', 'DROP TABLE dbo.Orders'],
  ['W6', 'CREATE TABLE', 'CREATE TABLE dbo.Evil (a int)'],
  ['W7', 'TRUNCATE', 'TRUNCATE TABLE dbo.Orders'],
  ['W8', 'EXEC xp_cmdshell', "EXEC master.dbo.xp_cmdshell 'whoami'"],
  ['W9', 'SELECT INTO', 'SELECT OrderId INTO dbo.Copy FROM dbo.Orders'],
  ['W10', 'SELECT then DELETE in one batch', 'SELECT OrderId FROM dbo.Orders; DELETE FROM dbo.Orders'],
  ['W11', 'CTE feeding DELETE', 'WITH c AS (SELECT OrderId FROM dbo.Orders) DELETE FROM c'],
  ['W12', 'SELECT followed by WAITFOR', "SELECT OrderId FROM dbo.Orders WAITFOR DELAY '00:00:05'"],
  ['W13', 'GRANT', 'GRANT SELECT ON dbo.Hidden TO public'],
  ['W14', 'GO batch separator smuggling', 'SELECT OrderId FROM dbo.Orders\nGO\nDELETE FROM dbo.Orders'],
  ['W15', 'exclusive lock hint', 'SELECT OrderId FROM dbo.Orders WITH (TABLOCKX, HOLDLOCK)'],
  ['W16', 'SET option', 'SET ROWCOUNT 0'],
  ['W17', 'BEGIN TRAN / COMMIT', 'BEGIN TRAN; SELECT OrderId FROM dbo.Orders; COMMIT'],
];
for (const [id, what, sql] of writes) await validatorCase(s, id, what, sql);

const scope = [
  ['S1', 'table outside scope (login CAN read it)', 'SELECT x FROM dbo.Hidden'],
  ['S2', 'unapproved column', 'SELECT CardToken FROM dbo.Orders'],
  ['S3', 'SELECT * on a column-limited table', 'SELECT * FROM dbo.Orders'],
  ['S4', 'unapproved column through a table alias', 'SELECT o.CardToken FROM dbo.Orders AS o'],
  ['S5', 'unapproved column through a derived table', 'SELECT t.c FROM (SELECT CardToken AS c FROM dbo.Orders) AS t'],
  ['S6', 'unapproved column used only in WHERE (oracle)', "SELECT OrderId FROM dbo.Orders WHERE CardToken LIKE N'tok_a%'"],
  ['S7', 'catalog view sys.tables', 'SELECT name FROM sys.tables'],
  ['S8', 'INFORMATION_SCHEMA', 'SELECT TABLE_NAME FROM INFORMATION_SCHEMA.COLUMNS'],
  ['S9', 'one-part name', 'SELECT OrderId FROM Orders'],
  ['S10', 'join to out-of-scope table', 'SELECT o.OrderId FROM dbo.Orders AS o JOIN dbo.Hidden AS h ON h.x = o.OrderId'],
  ['S11', 'unapproved column in correlated subquery', 'SELECT o.OrderId FROM dbo.Orders AS o WHERE EXISTS (SELECT 1 FROM dbo.Orders AS i WHERE i.CardToken = N\'x\' AND i.OrderId = o.OrderId)'],
  ['S12', 'unapproved column in ORDER BY', 'SELECT OrderId FROM dbo.Orders ORDER BY CardToken'],
];
for (const [id, what, sql] of scope) await validatorCase(s, id, what, sql);

const xdb = [
  ['X1', 'three-part cross-database name', 'SELECT OrderId FROM EvalCommerce.dbo.Orders'],
  ['X2', 'four-part linked-server name', 'SELECT OrderId FROM [127.0.0.1].SafetyProbe.dbo.Orders'],
  ['X3', 'OPENROWSET', "SELECT a.x FROM OPENROWSET('SQLNCLI', 'Server=.;Trusted_Connection=yes;', 'SELECT 1 AS x') AS a"],
  ['X4', 'OPENQUERY', "SELECT a.x FROM OPENQUERY(LOOPBACK, 'SELECT 1 AS x') AS a"],
  ['X5', 'OPENDATASOURCE', "SELECT name FROM OPENDATASOURCE('SQLNCLI', 'Data Source=.;Integrated Security=SSPI').master.dbo.sysdatabases"],
  ['X6', 'three-part name to own database', 'SELECT OrderId FROM SafetyProbe.dbo.Orders'],
];
for (const [id, what, sql] of xdb) await validatorCase(s, id, what, sql);

const fns = [
  ['F1', '@@VERSION global', 'SELECT @@VERSION AS v'],
  ['F2', 'SUSER_SNAME()', 'SELECT SUSER_SNAME() AS who'],
  ['F3', 'HAS_PERMS_BY_NAME', "SELECT HAS_PERMS_BY_NAME('dbo.Orders', 'OBJECT', 'UPDATE') AS p"],
  ['F4', 'subquery on sys.objects', 'SELECT OrderId FROM dbo.Orders WHERE 1 = (SELECT COUNT(*) FROM sys.objects)'],
  ['F5', 'variable', 'DECLARE @x int = 1; SELECT @x AS x'],
  ['F6', 'user-defined function', 'SELECT dbo.SomeUdf(OrderId) AS u FROM dbo.Orders'],
  ['F7', 'QUOTED_IDENTIFIER-ambiguous double quotes', 'SELECT "OrderId" FROM dbo.Orders'],
  ['F8', 'temp table', 'SELECT OrderId FROM #t'],
  ['F9', 'OPENJSON rowset over a literal', "SELECT [value] FROM OPENJSON(N'[1,2]')"],
  ['F10', 'fn_my_permissions', "SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE')"],
];
for (const [id, what, sql] of fns) await validatorCase(s, id, what, sql);

// accepted edge cases: harmless constructs that must still work
for (const [id, what, sql] of [
  ['A1', 'comment containing a semicolon and DELETE', "SELECT OrderId FROM dbo.Orders WHERE Status = N'Open' -- ; DELETE FROM dbo.Orders"],
  ['A2', 'aggregates and CAST', 'SELECT COUNT(*) AS n, MAX(CAST(OrderId AS nvarchar(10))) AS m FROM dbo.Orders'],
]) { const r = summarize(await s.call('sql_execute', { alias: 'probe', sql })); record('allowlist-positive', id, what, 'accepted and executed', r.code ?? `ok rows=${r.rows}`, !r.code, null); }

// the read-only principal ON ITS OWN: statements sent directly as safety_ro, bypassing the validator entirely
for (const [id, what, sql] of [
  ['P1', 'INSERT as safety_ro, direct', "INSERT INTO dbo.Orders VALUES (999, N'x', N'y')"],
  ['P2', 'UPDATE as safety_ro, direct', "UPDATE dbo.Orders SET Status = N'Hacked'"],
  ['P3', 'DELETE as safety_ro, direct', 'DELETE FROM dbo.Orders'],
  ['P4', 'CREATE TABLE as safety_ro, direct', 'CREATE TABLE dbo.Evil (a int)'],
  ['P5', 'ALTER TABLE as safety_ro, direct', 'ALTER TABLE dbo.Orders ADD Evil int NULL'],
  ['P6', 'SELECT INTO as safety_ro, direct', 'SELECT OrderId INTO dbo.Copy FROM dbo.Orders'],
  ['P7', 'xp_cmdshell as safety_ro, direct', "EXEC master.dbo.xp_cmdshell 'whoami'"],
  ['P8', 'read another database as safety_ro, direct', 'SELECT TOP (1) OrderId FROM EvalCommerce.dbo.Orders'],
]) { const r = sa(sql, 'safety_ro', secrets.ro); record('principal', id, what, 'SQL Server denies it', r.ok ? 'SUCCEEDED' : (r.out.match(/Msg \d+[^\n]*/)?.[0] ?? r.out.slice(0, 120)), !r.ok, null); }
{ const after = sa("SELECT CONCAT(COUNT(*), ',', SUM(CASE WHEN Status = N'Open' THEN 1 ELSE 0 END)) FROM dbo.Orders").out; record('principal', 'P9', 'data unchanged after every write attempt', '50 rows, all Open, no new tables', `${after}; tables=${sa("SELECT COUNT(*) FROM sys.tables").out}`, after === '50,50' && sa('SELECT COUNT(*) FROM sys.tables').out === '4', null); }
{ const r = summarize(await s.call('sql_execute', { alias: 'probe', sql: 'SELECT x FROM dbo.NoGrant' })); record('principal', 'P10', 'in-scope table the login has no SELECT on (allowlist passes, principal refuses)', 'permission.denied', r.code ?? 'EXECUTED', r.code === 'permission.denied', { message: r.message, action: r.action }); }
{ const r = summarize(await s.call('sql_execute', { alias: 'probe-rwnv', sql: 'SELECT OrderId FROM dbo.Orders' })); record('principal', 'P11', 'writer login stored with --no-verify, first use', 'principal.unsafe, nothing executed', r.code ?? 'EXECUTED', r.code === 'principal.unsafe', { message: r.message }); }

// unauthorized access
for (const [id, what, args, tool, expect] of [
  ['U1', 'unknown alias', { alias: 'nope', sql: 'SELECT OrderId FROM dbo.Orders' }, 'sql_execute', 'alias.unknown'],
  ['U2', 'alias refused at provisioning was never stored', { alias: 'probe-rw', sql: 'SELECT OrderId FROM dbo.Orders' }, 'sql_execute', 'alias.unknown'],
  ['U3', 'connection string passed as an alias', { alias: `Server=127.0.0.1;User Id=sa;Password=${SA}` }, 'schema_inspect', 'input.invalid'],
  ['U4', 'schema_inspect of out-of-scope object', { alias: 'probe', scope: 'dbo.Hidden' }, 'schema_inspect', 'scope.unapproved'],
]) { const r = summarize(await s.call(tool, args)); record('access', id, what, expect, r.code ?? 'ok', r.code === expect, { message: r.message }); }

// disclosure
{ const r = await s.call('connection_list', {}); const j = JSON.stringify(r); record('disclosure', 'D1', 'connection_list', 'no password, no connection string', `aliases=${r.connections?.map((c) => c.alias).join(',')}`, !j.includes(secrets.ro) && !j.includes(secrets.rw) && !/password\s*=/i.test(j), null); }
{ const r = await s.call('sql_execute', { alias: 'probe', sql: `SELECT N'${secrets.ro}' AS echoed` }); const j = JSON.stringify(r); record('disclosure', 'D2', 'query that selects the alias password as a literal', 'password redacted from the result', j.includes(secrets.ro) ? 'LEAKED' : (summarize(r).code ?? 'redacted'), !j.includes(secrets.ro), { result: j.slice(0, 300) }); }
{ const r = await s.call('sql_execute', { alias: 'probe', sql: `SELECT OrderId FROM dbo.Orders WHERE OrderId = CONVERT(int, N'${secrets.ro}')` }); const j = JSON.stringify(r); record('disclosure', 'D3', 'SQL error message that would contain the password', 'password redacted from the error', j.includes(secrets.ro) ? 'LEAKED' : (summarize(r).code ?? 'ok'), !j.includes(secrets.ro), { result: j.slice(0, 300) }); }

// schema drift during an investigation
{
  const inv = await s.call('investigation_start', { question: 'drift probe' });
  await s.call('schema_inspect', { alias: 'probe', refresh: true });
  sa("EXEC sp_rename 'dbo.Orders.Status', 'OrderStatus', 'COLUMN'");
  const q = summarize(await s.call('sql_execute', { alias: 'probe', sql: 'SELECT Status FROM dbo.Orders', investigationId: inv.investigationId }));
  const d = await s.call('schema_inspect', { alias: 'probe', refresh: true });
  sa("EXEC sp_rename 'dbo.Orders.OrderStatus', 'Status', 'COLUMN'");
  record('drift', 'R1', 'column renamed under a running investigation', 'actionable error for the stale column; drift reported by schema_inspect', `query ${q.code}; drift=${JSON.stringify(d.drift ?? null).slice(0, 160)}`, !!q.code && !!d.drift, { message: q.message, action: q.action });
}
s.close();

// ------------------------------------------------------------------------------------------------ budgets
{ const b = await new Server({ MAX_QUERY_ROWS: '10' }).init(); const r = summarize(await b.call('sql_execute', { alias: 'probe', sql: 'SELECT n FROM dbo.Big' })); record('budget', 'B1', 'MAX_QUERY_ROWS=10 over 3000 rows', 'truncated to 10, truncatedBy rows', `rows=${r.rows} truncated=${r.truncated} by=${r.truncatedBy}`, r.rows === 10 && r.truncated && r.truncatedBy === 'rows', null); b.close(); }
{ const b = await new Server({ MAX_RESULT_BYTES: '20000' }).init(); const r = summarize(await b.call('sql_execute', { alias: 'probe', sql: 'SELECT pad FROM dbo.Big' })); record('budget', 'B2', 'MAX_RESULT_BYTES=20000 over ~2.4 MB', 'truncated, truncatedBy bytes', `rows=${r.rows} truncated=${r.truncated} by=${r.truncatedBy}`, r.truncated && r.truncatedBy === 'bytes', null); b.close(); }
{ const b = await new Server({ MAX_QUERY_ROWS: '-5' }).init(); const r = summarize(await b.call('sql_execute', { alias: 'probe', sql: 'SELECT n FROM dbo.Big' })); record('budget', 'B3', 'malformed MAX_QUERY_ROWS=-5', 'falls back to the default 500, not unlimited', `rows=${r.rows} truncated=${r.truncated}`, r.rows === 500 && r.truncated, null); b.close(); }
{
  const b = await new Server({ SQL_TIMEOUT_SECONDS: '2' }).init(); const t0 = Date.now();
  const r = summarize(await b.call('sql_execute', { alias: 'probe', sql: SLOW })); const secs = (Date.now() - t0) / 1000;
  await new Promise((res) => setTimeout(res, 1500)); const still = runningAs('safety_ro');
  record('budget', 'B4', 'SQL_TIMEOUT_SECONDS=2 on a long query', 'budget.timeout near 2 s, and nothing left running on the server', `${r.code} after ${secs.toFixed(1)}s; requests still running=${still}`, r.code === 'budget.timeout' && secs < 8 && still === '0', { message: r.message }); b.close();
}
{
  const b = await new Server({ MAX_CONCURRENT_QUERIES: '1', SQL_TIMEOUT_SECONDS: '4' }).init();
  const first = b.call('sql_execute', { alias: 'probe', sql: SLOW }); await new Promise((res) => setTimeout(res, 700));
  const second = summarize(await b.call('sql_execute', { alias: 'probe', sql: 'SELECT n FROM dbo.Big WHERE n = 1' })); const f = summarize(await first);
  record('budget', 'B5', 'MAX_CONCURRENT_QUERIES=1 with a second query while one runs', 'second refused with budget.concurrency', `second=${second.code ?? 'EXECUTED'}; first=${f.code ?? 'ok'}`, second.code === 'budget.concurrency', { message: second.message }); b.close();
}
{
  const b = await new Server({ MAX_QUERIES_PER_INVESTIGATION: '2' }).init(); const inv = await b.call('investigation_start', { question: 'budget probe' }); const codes = [];
  for (let i = 0; i < 3; i++) codes.push(summarize(await b.call('sql_execute', { alias: 'probe', sql: `SELECT n FROM dbo.Big WHERE n = ${i + 1}`, investigationId: inv.investigationId })).code ?? 'ok');
  record('budget', 'B6', 'MAX_QUERIES_PER_INVESTIGATION=2, three queries', 'third refused with budget.investigation', codes.join(','), codes[2] === 'budget.investigation' && codes[0] === 'ok', null); b.close();
}
{ const b = await new Server({ MAX_SQL_LENGTH: '100' }).init(); const r = summarize(await b.call('sql_execute', { alias: 'probe', sql: 'SELECT n FROM dbo.Big WHERE ' + Array.from({ length: 30 }, (_, i) => `n = ${i}`).join(' OR ') })); record('budget', 'B7', 'MAX_SQL_LENGTH=100 with a longer statement', 'sql.rejected', r.code ?? 'EXECUTED', r.code === 'sql.rejected', { reasons: r.reasons }); b.close(); }

// ------------------------------------------------------------------------------------------------ cancellation
{
  const b = await new Server({ SQL_TIMEOUT_SECONDS: '60' }).init();
  const req = b.send('tools/call', { name: 'sql_execute', arguments: { alias: 'probe', sql: SLOW } });
  await new Promise((res) => setTimeout(res, 2000)); const before = runningAs('safety_ro');
  b.notify('notifications/cancelled', { requestId: req.id, reason: 'safety probe' }); const t0 = Date.now();
  let after = '?'; for (let i = 0; i < 20; i++) { await new Promise((res) => setTimeout(res, 250)); after = runningAs('safety_ro'); if (after === '0') break; }
  const stopped = (Date.now() - t0) / 1000;
  const late = await Promise.race([req.promise.then((m) => summarize(m.result?.structuredContent ?? m.result ?? m.error)), new Promise((res) => setTimeout(() => res(null), 1500))]);
  record('cancellation', 'K1', 'MCP notifications/cancelled during a running query', 'the server-side query stops promptly', `running before=${before}, after=${after} within ${stopped.toFixed(1)}s; late response=${late ? (late.code ?? 'result') : 'none'}`, before !== '0' && after === '0' && stopped < 5, null); b.close();
}

// ------------------------------------------------------------------------------------------------ audit and leak scan
await new Promise((res) => setTimeout(res, 500));
const history = JSON.parse(readFileSync(join(HOME, 'history.json'), 'utf8'));
const auditCounts = {}; for (const e of history.audit ?? history.Audit ?? []) auditCounts[e.event ?? e.Event] = (auditCounts[e.event ?? e.Event] ?? 0) + 1;
record('audit', 'AU1', 'rejections, budgets and refusals are audited', 'history.json audit has sql.rejected, budget.* and principal/permission events', JSON.stringify(auditCounts), (auditCounts['sql.rejected'] ?? 0) >= 40 && Object.keys(auditCounts).some((k) => k.startsWith('budget.')), null);
const files = []; (function walk(d) { for (const f of readdirSync(d)) { const p = join(d, f); statSync(p).isDirectory() ? walk(p) : files.push(p); } })(HOME);
const leaks = [];
for (const [name, value] of [['safety_ro', secrets.ro], ['safety_rw', secrets.rw], ['sa', SA]]) {
  if (transcript.some((l) => l.includes(value))) leaks.push(`${name} in MCP output`);
  if (stderrAll.join('').includes(value)) leaks.push(`${name} in stderr`);
  for (const f of files) if (!f.endsWith('.dpapi') && readFileSync(f, 'utf8').includes(value)) leaks.push(`${name} in ${f}`);
}
record('disclosure', 'D4', `scan of ${transcript.length} MCP responses, all stderr and ${files.length} data files for every password`, 'no password anywhere outside the DPAPI blobs', leaks.join('; ') || 'none found', leaks.length === 0, null);
record('surface', 'T2', 'stdout carries only JSON-RPC', 'no non-JSON line on stdout', `${nonJsonStdout.length} non-JSON lines`, nonJsonStdout.length === 0, null);

writeFileSync(join(here, 'results.json'), JSON.stringify({ at: new Date().toISOString(), exe: EXE, results }, null, 2));
const failed = results.filter((r) => !r.pass);
console.log(`\n${results.length - failed.length}/${results.length} passed`);
for (const f of failed) console.log('FAILED', f.id, f.what, JSON.stringify(f.detail));
