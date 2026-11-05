# SQL Investigator

A single-user, local Windows developer tool: ask Claude Code questions about data in one or more SQL Server databases and get evidence-backed answers. A local .NET 10 MCP server (stdio) provides authorized schema discovery, bounded read-only query execution, investigation history and reviewed relationship knowledge. See [`VISION.md`](VISION.md).

## Status

**Ready for implementation. No application code exists yet.**

| Piece | State |
|---|---|
| Architecture model (`continuum/model/architecture.adl`) | Compiles, 0 errors, 0 warnings |
| Technology bindings (LocalDevelopment) | Complete; `continuum package build` succeeds |
| Development package (`continuum/`) | Built; `continuum package check continuum` reports it intact |
| Acceptance scenarios (12) | All resolve against the model; adapter scaffolded; **0 / 12 passing** (every step is a stub) |
| Business model (`continuum/business/*.bdl`) | Compiles; covers initial capabilities only |
| ADRs (`continuum/decisions/`) | Candidates, not yet recorded in a governed decision ledger |
| Initial accepted state (S0) | Not recorded yet; the owner records it (see below) |

## Prerequisites

| Tool | Why | Check |
|---|---|---|
| Windows x64 | DPAPI CurrentUser and the target runtime | |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Build the MCP server and tests (pinned in `global.json`) | `dotnet --version` |
| SQL Server Developer edition or LocalDB | Disposable fixture databases for integration and acceptance tests | `sqllocaldb info` |
| Node.js 24+ | Runs the Continuum acceptance runner | `node --version` |
| Continuum CLI, signed in | Package build/check/run, git hooks, CI gate | `continuum auth status`, `continuum doctor` |
| Claude Code | The only conversational interface | |

Never point tests or local development at production data. Production access needs the data owner's approval for the specific data classifications and environment first.

## Repository map

```
VISION.md                         product intent
CLAUDE.md                         instructions for coding agents working in this repo
continuum/                        the development package (the implementation contract)
  GOAL.md                         why
  IMPLEMENTATION.md               generated build contract: start here
  IMPLEMENTATION-HANDOFF.md       hand-written delivery notes: MCP tool names, hard gates, test guidance
  BEHAVIOUR.md                    generated behavioural projection
  model/                          semantic ADL (authority), bindings, vocabulary claims
  business/                       BDL business model
  acceptance/scenarios/           12 executable Given/When/Then scenarios (B01-B12)
  acceptance/adapter.mjs          step bindings: replace each stub with a call into the implementation
  decisions/                      ADR candidates
  diagrams/                       hand-authored Mermaid diagrams
.githooks/, .github/workflows/    Continuum architecture guard (pre-commit, pre-push, CI)
.claude/                          Continuum skills and agents for Claude Code
```

Suggested source layout (a recommendation, not part of the model):

```
src/SqlInvestigator.McpServer/    .NET 10 console exe: MCP stdio, QueryGateway, KnowledgeService
tests/SqlInvestigator.Tests/      xUnit unit tests (ScriptDom allowlist, budgets, redaction, JSON stores)
tests/SqlInvestigator.IntegrationTests/  disposable SQL Server fixtures
```

## Daily workflow

```powershell
continuum package check continuum      # is the contract current?
continuum package run continuum        # run the 12 acceptance scenarios against acceptance/adapter.mjs
continuum package run continuum --only b02-bounded-select
dotnet test                            # once the solution exists
```

Work test-first: pick a scenario, implement the behaviour in .NET, bind its steps in `continuum/acceptance/adapter.mjs` (drive the real MCP process over stdio), and re-run until it passes.

### If the architecture needs to change

Do not edit derived files (`IMPLEMENTATION.md`, `BEHAVIOUR.md`, `PACKAGE.json`, `continuum/README.md`, `acceptance/README.md`, `model/*.adl.txt`, `model/compile-proof.json`). If the code needs something the model does not say, record it in `continuum/gaps/<date>-<slug>.md` and propose an `.adl-change`; in Claude Code, use the `implement-against-architecture` skill. After any model edit: `continuum package bind refresh continuum`, then `continuum package build continuum`.

## Owner actions still open

1. **Record the initial accepted state** once you have reviewed the model: `continuum package accept continuum --as <you> --verified-against HEAD`, then `continuum ledger record continuum --verified-against HEAD`.
2. **Govern the six ADR candidates** in a Continuum decision space with an authority policy.
3. **Get data-owner approval** before any non-fixture database is connected.
