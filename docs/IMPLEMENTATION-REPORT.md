# Implementation report

**Date:** 2026-11-06 · **Implementation revision:** `c4a9c31` (the code). Later commits change docs and diagrams only · **Implemented by:** Claude Code, against the Continuum package in `continuum/`

## Outcome

SQL Investigator is implemented: a .NET 10 MCP stdio server with three services, exactly the 11 declared MCP tools, and a local provisioning console. All 12 acceptance scenarios pass against the real executable, both locally and on CI. The architecture model, the bindings and the acceptance criteria were not changed.

## Evidence

Each result below was obtained by running the named command at `c4a9c31`.

| Check | Result | Where |
|---|---|---|
| `dotnet build` (warnings are errors) | 0 warnings, 0 errors | local and CI |
| Unit tests `SqlInvestigator.Tests` | 123 passed, 0 failed, 0 skipped | CI run [42200022040](https://github.com/avlon-technologies/sql-investigator/actions/runs/42200022040) (`.NET` workflow) and locally |
| Integration tests `SqlInvestigator.IntegrationTests` (disposable SQL Server fixtures) | 18 passed, 0 failed, 0 skipped | CI run 42200022040 and locally |
| Acceptance `continuum package run continuum` / `node continuum/acceptance/run.mjs` | 12 passed, 0 failed, 12 executed; all 46 steps bound to the real exe over MCP stdio and the console | CI run 42200022040 and locally |
| `continuum package check continuum` | intact, with every check evaluated over the attested state | local |
| `continuum guard ci` | lifecycle ok, architecture ok, implementation ok, hygiene ok on a clean checkout | local, clean tree at `c4a9c31` |
| Conformance assess | **L1 attained.** L2 undetermined (`precondition/no-correspondence-map`). See below | local |

The negative security tests run on their own, as `CLAUDE.md` requires:

- **The allowlist on its own.** Unit tests reject writes, DDL, `EXEC`, multiple statements, `SELECT INTO`, variables, `@@` globals, rowset functions (`OPENROWSET`, `OPENQUERY` and others), table-valued and user-defined functions, cross-database and linked-server names, out-of-scope tables and columns, `SELECT *` over restricted tables, and statements that `QUOTED_IDENTIFIER` would make ambiguous. Integration tests show that a rejected statement never reaches SQL Server.
- **The read-only principal on its own.** Integration tests run INSERT, UPDATE, DELETE and DDL directly as the fixture principal, bypassing the validator, and show that SQL Server denies each one. The principal guard refuses a login with an INSERT grant and a login in `db_owner`.

## What was not established

- **Conformance above L1.** `continuum conformance assess` cannot establish a source revision or a correspondence map for this repository, and `continuum conformance check` supports only JS/TS sources. So L2 is a verifier limit, not a failed check. **No level above L1 is claimed.** The test and acceptance results above are reported alongside the assessment and are not a conformance level.
- **The lifecycle state.** At the time of this report `continuum ledger current` reported `NOT VERIFIED`. The owner has since recorded S0: acceptance fact `sha256:fdbddf00…` by mike@avlon.ca at `5fdd905` (12/12), ledger checkpoint 0001 `initial`, verified.
- **The Continuum CI gate.** The `Continuum` workflow reports "architecture not evaluated" because the `CONTINUUM_HOST` variable and the `CONTINUUM_HOST_API_KEY` secret are not set. The `.NET` workflow is green.

## Open gap

[`continuum/gaps/2026-11-06-remove-connection-secret.md`](../continuum/gaps/2026-11-06-remove-connection-secret.md): the model does not say what removing an alias over MCP does to its DPAPI-protected secret. In the meantime:

- MCP `connection_remove` removes the catalog entry only.
- The console's `remove` command also deletes the secret.
- No service writes the vault.

The recommended disposition needs no model change. The gap waits for the owner and blocks nothing.

## Owner actions

1. ~~Record S0.~~ Done 2026-11-06 (checkpoint 0001).
2. Set the Actions variable `CONTINUUM_HOST` and the secret `CONTINUUM_HOST_API_KEY` (`continuum ci setup` checks them).
3. Get data-owner approval before connecting any non-fixture database. The BDL `SensitiveDataPolicy` is still open.
4. Dispose of the gap above.

## Where to look

- Implementation decisions and the guarantee-to-code map: [`IMPLEMENTATION-NOTES.md`](IMPLEMENTATION-NOTES.md).
- How it is used: [`continuum/diagrams/atlas/usage-view.png`](../continuum/diagrams/atlas/usage-view.png) and the top-level [`README.md`](../README.md).
