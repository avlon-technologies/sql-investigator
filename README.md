# SQL Investigator

A single-user, local Windows developer tool: ask Claude Code questions about data in one or more SQL Server databases and get evidence-backed answers. A local .NET 10 MCP server (stdio) provides authorized schema discovery, bounded read-only query execution, investigation history and reviewed relationship knowledge. See [`VISION.md`](VISION.md).

## Status

**Implemented.** The application is built against the accepted Continuum package and all 12 acceptance scenarios pass against the real executable. The evidence is in [`docs/IMPLEMENTATION-REPORT.md`](docs/IMPLEMENTATION-REPORT.md).

| Piece | State |
|---|---|
| MCP server `SqlInvestigator.exe` (`src/SqlInvestigator.McpServer`) | ConnectionRegistry, QueryGateway, KnowledgeService; exactly the 11 declared MCP tools; local provisioning console |
| Unit tests (`tests/SqlInvestigator.Tests`) | Allowlist positive and negative corpus, redaction, budgets settings, stores, vault, tool surface |
| Integration tests (`tests/SqlInvestigator.IntegrationTests`) | Disposable SQL Server fixtures: the read-only principal on its own, the principal guard, execution, timeout, drift |
| Acceptance (`continuum/acceptance`) | 12 / 12 scenarios pass; all 46 steps drive the real exe over MCP stdio and the console |
| Architecture model, bindings, package | Unchanged by the implementation; `continuum package check continuum` intact |
| ADRs (`continuum/decisions/`) | ADR-001..006 accepted in the Continuum Host |
| Initial accepted state (S0) | Recorded 2026-11-06 by mike@avlon.ca at `5fdd905` (suite 12/12); ledger checkpoint 0001, verified |

## Install

Prerequisites: Windows x64, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`), and for tests a SQL Server 2022 you can throw away (Developer edition, or the container below). Node.js 24+ runs the acceptance scenarios.

```powershell
dotnet publish src/SqlInvestigator.McpServer -c Release -o "$env:LOCALAPPDATA\SqlInvestigator\bin"
& "$env:LOCALAPPDATA\SqlInvestigator\bin\SqlInvestigator.exe" --version
```

### Register it with Claude Code

```powershell
claude mcp add sql-investigator -- "$env:LOCALAPPDATA\SqlInvestigator\bin\SqlInvestigator.exe"
```

or in a project's `.mcp.json`:

```json
{
  "mcpServers": {
    "sql-investigator": {
      "type": "stdio",
      "command": "C:\\Users\\<you>\\AppData\\Local\\SqlInvestigator\\bin\\SqlInvestigator.exe"
    }
  }
}
```

Recommended permissions (`.claude/settings.json`). The read-only tools can run freely. Ask before `connection_remove`, and before `knowledge_review`, which records *your* verdict on a relationship:

```json
{
  "permissions": {
    "allow": [
      "mcp__sql-investigator__connection_list", "mcp__sql-investigator__schema_inspect",
      "mcp__sql-investigator__sql_validate", "mcp__sql-investigator__sql_execute",
      "mcp__sql-investigator__investigation_start", "mcp__sql-investigator__investigation_record",
      "mcp__sql-investigator__investigation_get", "mcp__sql-investigator__knowledge_search",
      "mcp__sql-investigator__knowledge_propose"
    ],
    "ask": ["mcp__sql-investigator__connection_remove", "mcp__sql-investigator__knowledge_review"]
  }
}
```

## Provision a connection (local console only)

Connections and their passwords never pass through MCP or chat. You provision them yourself at a console:

```powershell
SqlInvestigator.exe provision --alias orders --server "sqldev01,1433" --database Orders `
  --user investigator_ro --environment development `
  --scope "dbo.Orders(OrderId,Status,CreatedAt)" --scope dbo.Shipments
# Password for investigator_ro on sqldev01,1433/Orders: (typed, not echoed)
```

- **The login must be read-only.** Use a dedicated login with SELECT on the approved objects only. Before storing anything, the console connects and refuses a login that is sysadmin, db_owner, db_datawriter or db_ddladmin, or that can write, alter or execute (`principal.unsafe`). The same check runs again the first time each server process uses the alias.
- **The scope is the allowlist.** `--scope schema.object` approves every column of that object, and `schema.object(col,…)` approves only the listed columns. Queries naming anything else are rejected before they reach SQL Server.
- **Only `fixture` and `development` environments are accepted.** Production is refused until the data owner has approved the data classifications and environment (`ProductionDataApproval`).
- **The password is stored with DPAPI CurrentUser** in `%LOCALAPPDATA%\SqlInvestigator\data\vault\<alias>.dpapi`. The blob is bound to its alias, and the folder and files are restricted to your Windows account. It is never accepted as a command-line argument.
- Other options: `--windows-auth` (instead of `--user`), `--trust-server-certificate`, `--no-encrypt`, `--replace`, `--no-verify` (skip the read-only check, for offline set-up; the server still checks before first use).
- `SqlInvestigator.exe list` lists aliases; `SqlInvestigator.exe remove --alias orders` removes one and deletes its secret.

## What Claude Code can do with it

| Tool | What it does |
|---|---|
| `connection_list` | Aliases and non-secret metadata (server, database, environment, scope) |
| `connection_remove` | Remove an alias from the catalog |
| `schema_inspect` | Approved objects and columns, with source and capture time. Recaptured when absent, stale (15 min) or drifted, and drift is reported |
| `sql_validate` | Would this statement be accepted? Gives the reasons if not |
| `sql_execute` | One bounded SELECT on one alias. Returns a query id, the exact SQL, timestamps, rows and a truncation indicator |
| `investigation_start` / `_record` / `_get` | Questions, hypotheses, findings and uncertainty, citing query ids. Result rows are never stored |
| `knowledge_search` / `_propose` / `_review` | Schema snapshots and cross-database relationships. A proposed relationship stays proposed until you accept or reject it; a rejection is kept |

Cross-database questions are answered by separate per-alias queries that Claude Code correlates. Cross-database, linked-server and OPENROWSET SQL is rejected.

## Safety model

Two independent barriers stand between the agent and a write:

1. **A fail-closed ScriptDom allowlist** (`Queries/SqlValidator.cs`). A statement must parse, under both QUOTED_IDENTIFIER settings to the same meaning, as exactly one batch holding exactly one `SELECT`. Every parse-tree node must be on an allowlist of node types and every function on an allowlist of built-ins. Tables must be two-part names inside the alias's scope, and columns must be approved columns, including through aliases, derived tables, CTEs and correlated subqueries. Anything unrecognised is rejected and audited.
2. **A read-only SQL principal**, verified at provisioning and before first use. Each query also runs inside a transaction that is always rolled back.

Budgets (environment variables; recommended defaults, not architecture):

| Variable | Default | Effect |
|---|---|---|
| `SQL_TIMEOUT_SECONDS` | 15 | Command timeout; the query is cancelled and `budget.timeout` returned |
| `MAX_QUERY_ROWS` | 500 | Rows returned; beyond it the result is truncated, `truncatedBy: "rows"` |
| `MAX_RESULT_BYTES` | 1048576 | Result size; beyond it the result is truncated, `truncatedBy: "bytes"` |
| `MAX_CONCURRENT_QUERIES` | 2 | Concurrent queries; further ones are refused with `budget.concurrency` |
| `MAX_QUERIES_PER_INVESTIGATION` | 50 | Queries per investigation; further ones are refused with `budget.investigation` |
| `CONNECT_TIMEOUT_SECONDS`, `MAX_SQL_LENGTH`, `SCHEMA_MAX_AGE_SECONDS`, `HISTORY_RETENTION_DAYS`, `MAX_AUDIT_EVENTS` | 10, 20000, 900, 90, 10000 | |

A malformed or non-positive value falls back to the default, never to "unlimited". Every enforcement is written to the audit trail in `history.json`.

Other guarantees:

- stdout carries MCP protocol messages only. Diagnostics are structured JSON lines on stderr (`SQLINVESTIGATOR_LOG_LEVEL`, default Information).
- Every tool result and diagnostic passes through redaction of the stored secrets and anything shaped like a connection-string password.
- Errors are actionable (`code`, `message`, `action`) and never include a connection string or a fabricated result.

Data lives in `%LOCALAPPDATA%\SqlInvestigator\data` (override with `SQLINVESTIGATOR_HOME`): `catalog.json`, `history.json`, `knowledge.json` and `vault\`. Each store is written atomically, under a lock, by its one owning service.

## Develop and test

```powershell
dotnet build SqlInvestigator.slnx          # warnings are errors
dotnet test SqlInvestigator.slnx           # unit + integration (integration needs the test SQL Server)
continuum package run continuum            # the 12 acceptance scenarios against the real exe
continuum package check continuum          # is the contract intact?
```

The integration and acceptance tests create and drop disposable databases (`SqlInvFixture_*`) and logins (`sqlinv_fixture_*`) holding synthetic data. They read the test server from `SQLINVESTIGATOR_FIXTURE_SERVER` (default `127.0.0.1,1433`) and the admin password from `SQLINVESTIGATOR_FIXTURE_ADMIN_PASSWORD`, or from `%LOCALAPPDATA%\SqlInvestigator\dev-fixture\sa-password.txt`. A throwaway container:

```powershell
docker run -d --name sqlinvestigator-mssql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<throwaway> -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

`tests/SqlInvestigator.FixtureTool` is the fixture CLI the acceptance adapter drives (`drop-all` removes leftovers). CI (`.github/workflows/dotnet.yml`) runs the build, the tests and the acceptance scenarios on a self-hosted Windows runner on the developer's PC (label `sqlinv-local`, in `C:\actions-runner\sql-investigator`), against the local SQL Server container.

Implementation decisions (MCP SDK, store formats, allowlist design and the rest) are in [`docs/IMPLEMENTATION-NOTES.md`](docs/IMPLEMENTATION-NOTES.md).

## Repository map

```
VISION.md, CLAUDE.md                product intent; instructions for coding agents
src/SqlInvestigator.McpServer/      the executable
  Connections/                      ConnectionRegistry, catalog, DPAPI vault, SQL connections + principal guard, console
  Queries/                          QueryGateway, SqlValidator (allowlist), InvestigationHistory
  Knowledge/                        KnowledgeService, KnowledgeStore
  Mcp/                              the 11 MCP tools
  Infrastructure/                   paths, settings, secure atomic files, redaction, stderr diagnostics
tests/                              unit, integration, fixture library and fixture CLI
continuum/                          the development package (the implementation contract)
  model/                            semantic ADL (authority), BDL, bindings
  acceptance/                       12 scenarios and adapter.mjs (step bindings)
  decisions/                        ADR-001..006
docs/                               implementation notes and report
.githooks/, .github/workflows/      Continuum architecture guard; .NET CI
```

### If the architecture needs to change

Do not edit derived files (`IMPLEMENTATION.md`, `BEHAVIOUR.md`, `PACKAGE.json`, `continuum/README.md`, `acceptance/README.md`, `model/*.adl.txt`, `model/compile-proof.json`). If the code needs something the model does not say, record it in `continuum/gaps/<date>-<slug>.md` and propose an `.adl-change`; in Claude Code, use the `implement-against-architecture` skill. After any model edit: `continuum package bind refresh continuum`, then `continuum package build continuum`.

## Owner actions still open

1. **Configure the Continuum CI gate**: set the Actions variable `CONTINUUM_HOST` and the secret `CONTINUUM_HOST_API_KEY` (`continuum ci setup` checks them). Until then the `Continuum` workflow reports "architecture not evaluated".
2. **Get data-owner approval** before any non-fixture database is connected (the BDL `SensitiveDataPolicy` is still open).
3. **Read ADR alignment from the model.** The recorded alignment checks report "missing" only because the expectations lack the `::` root anchor (see `continuum/decisions/README.md`).
