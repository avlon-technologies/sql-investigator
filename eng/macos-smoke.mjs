// eng/macos-smoke.mjs — runs the PUBLISHED macOS executable end to end, without a SQL Server:
//   1. --version and --help start, and print to stdout
//   2. the data home is created 0700, store files 0600, under SQLINVESTIGATOR_HOME
//   3. provision (secret on stdin, --no-verify) creates a login-keychain item; --replace updates it
//   4. another application (the published fixture tool) finds the item but cannot read its data without approval
//   5. the MCP server starts over stdio, lists 11 tools and the alias, and stdout carries only JSON-RPC
//   6. remove deletes the keychain item; a second remove reports nothing to delete
// Usage: node eng/macos-smoke.mjs <SqlInvestigator> <SqlInvestigator.FixtureTool>
// Exit 0 only when every check holds. Prints one line per check.

import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, statSync, existsSync, readdirSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [exe, fixtureTool] = process.argv.slice(2);
if (!exe || !fixtureTool) { console.error('usage: node eng/macos-smoke.mjs <SqlInvestigator> <SqlInvestigator.FixtureTool>'); process.exit(2); }

const SECRET = 'smoke-secret-' + Math.random().toString(36).slice(2);
const home = join(mkdtempSync(join(tmpdir(), 'sqlinv-smoke-')), 'data');
const env = { ...process.env, SQLINVESTIGATOR_HOME: home };
let failures = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`); if (!ok) failures++; };
const run = (file, args, input) => spawnSync(file, args, { env, input, encoding: 'utf8', timeout: 60000 });
const mode = (p) => (statSync(p).mode & 0o777).toString(8);
const tool = (args) => { const r = run(fixtureTool, args); try { return JSON.parse(r.stdout.trim().split('\n').pop()); } catch { return { ok: false, error: r.stdout + r.stderr }; } };

try {
  const v = run(exe, ['--version']);
  check('the executable starts (--version)', v.status === 0 && /\d+\.\d+\.\d+/.test(v.stdout), v.stdout.trim() || v.stderr.trim());

  const h = run(exe, ['--help']);
  check('--help prints the console usage', h.status === 0 && h.stdout.includes('provision --alias'));

  const provisionArgs = ['provision', '--alias', 'smoke', '--server', '127.0.0.1,1433', '--database', 'SmokeDb', '--user', 'smoke_reader',
    '--environment', 'fixture', '--scope', 'dbo.Orders', '--trust-server-certificate', '--no-verify'];
  const p = run(exe, provisionArgs, SECRET + '\n');
  check('provision stores the alias (secret on stdin)', p.status === 0, (p.stdout + p.stderr).trim().split('\n').pop());
  check('the console never prints the secret', !(p.stdout + p.stderr).includes(SECRET));

  check('the data home is 0700', existsSync(home) && mode(home) === '700', existsSync(home) ? mode(home) : 'missing');
  const catalog = join(home, 'catalog.json');
  check('catalog.json is 0600', existsSync(catalog) && mode(catalog) === '600', existsSync(catalog) ? mode(catalog) : 'missing');
  check('nothing under the data home holds the secret', !readdirSync(home, { recursive: true }).some((f) => {
    const full = join(home, String(f));
    return statSync(full).isFile() && readFileSync(full).includes(Buffer.from(SECRET));
  }));
  check('no vault folder: the secret is not a file on macOS', !existsSync(join(home, 'vault')));

  const c = tool(['vault-check', '--home', home, '--alias', 'smoke', '--expected', SECRET]);
  check('a login-keychain item exists for the alias', c.ok && c.present === true && c.mechanism === 'keychain-login', JSON.stringify(c));
  check('another application cannot read it without approval', c.ok && c.protectedForUser === true, c.detail ?? c.error);
  check('the data home is current-user-only (stat)', c.ok && c.storeCurrentUserOnly === true);

  const r = run(exe, [...provisionArgs, '--replace'], SECRET + '-2\n');
  check('provision --replace updates the item', r.status === 0, (r.stdout + r.stderr).trim().split('\n').pop());

  // MCP stdio: initialize, tools/list, connection_list. stdout must be JSON-RPC only.
  const mcp = await mcpSession();
  check('the MCP server initializes over stdio', mcp.init?.result?.serverInfo?.name === 'sql-investigator', JSON.stringify(mcp.init?.error ?? ''));
  check('it lists the 11 tools', (mcp.tools?.result?.tools ?? []).length === 11, String((mcp.tools?.result?.tools ?? []).length));
  const listed = mcp.list?.result?.structuredContent?.connections ?? [];
  check('connection_list shows the alias without a secret', listed.some((x) => x.alias === 'smoke') && !JSON.stringify(mcp.list).includes(SECRET));
  check('stdout carries only JSON-RPC', mcp.stray.length === 0, mcp.stray.slice(0, 2).join(' | '));
  check('no MCP output or diagnostic holds the secret', !mcp.raw.includes(SECRET) && !mcp.stderr.includes(SECRET));

  const d = run(exe, ['remove', '--alias', 'smoke']);
  check('remove deletes the keychain item', d.status === 0 && d.stdout.includes('and its stored secret'), d.stdout.trim());
  const after = tool(['vault-check', '--home', home, '--alias', 'smoke', '--expected', SECRET]);
  check('the item is gone', after.ok && after.present === false, JSON.stringify(after));
  const d2 = run(exe, ['remove', '--alias', 'smoke']);
  check('a second remove has nothing to delete', d2.status === 0 && d2.stdout.includes('was not provisioned'), d2.stdout.trim());
} finally {
  run(exe, ['remove', '--alias', 'smoke']);
  rmSync(join(home, '..'), { recursive: true, force: true });
}

console.log(failures === 0 ? 'smoke: all checks passed' : `smoke: ${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);

function mcpSession() {
  return new Promise((resolve) => {
    const child = spawn(exe, [], { env, stdio: ['pipe', 'pipe', 'pipe'] });
    const out = { raw: '', stderr: '', stray: [], init: null, tools: null, list: null };
    let buffer = '';
    const waiting = new Map();
    child.stdout.on('data', (d) => {
      out.raw += d; buffer += d;
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl).trim(); buffer = buffer.slice(nl + 1);
        if (!line) continue;
        let m; try { m = JSON.parse(line); } catch { out.stray.push(line.slice(0, 120)); continue; }
        if (m.jsonrpc !== '2.0') out.stray.push(line.slice(0, 120));
        waiting.get(m.id)?.(m); waiting.delete(m.id);
      }
    });
    child.stderr.on('data', (d) => { out.stderr += d; });
    let id = 0;
    const request = (method, params) => new Promise((res) => {
      const n = ++id; waiting.set(n, res);
      child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: n, method, params }) + '\n');
      setTimeout(() => { if (waiting.delete(n)) res({ error: `${method} timed out` }); }, 30000);
    });
    (async () => {
      out.init = await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'macos-smoke', version: '1' } });
      child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
      out.tools = await request('tools/list', {});
      out.list = await request('tools/call', { name: 'connection_list', arguments: {} });
      child.stdin.end();
      const timer = setTimeout(() => child.kill(), 5000);
      child.on('exit', () => { clearTimeout(timer); resolve(out); });
    })();
  });
}
