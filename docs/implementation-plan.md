# Developer implementation plan — v0.3
## Vertical slice 1: secure one-database exploration
1. Scaffold .NET 10 console host with MCP stdio transport and structured logging to stderr (never stdout).
2. Provision connections with a local CLI command that prompts for credentials outside Claude Code; store DPAPI-encrypted secret separately and alias metadata as JSON.
3. Implement list_connections and inspect_schema with schema/table allowlists and size limits.
4. Implement validate_sql using ScriptDom with fail-closed, explicit AST allowlist. Do not rely on regex. Disallow nested constructs until separately tested.
5. Implement execute_sql with SqlClient, parameterless generated SQL constrained by policy; per-command timeout, cancellation, max returned rows/bytes, and SQL-side least-privilege login.
6. Register local MCP stdio server with Claude Code; smoke test generating, validating and executing a simple SELECT.
## Vertical slice 2: multi-database investigation
7. Support multiple connection aliases and independently scoped execution.
8. Add investigation start/append/get tools, atomic JSON persistence and SQL evidence references.
9. Add optional schema cache and architecture context files, freshness markers and proposed relationship review via tools.
10. Test correlation across two fixture databases with intentionally missing/mismatched keys.
## Security and acceptance gates
- A database login with no write privileges is mandatory, verified with controlled write attempts against disposable fixtures.
- Negative test corpus: INSERT, UPDATE, DELETE, MERGE, CREATE, DROP, ALTER, EXEC, SELECT INTO, multiple batches, comments, CTE variations, nested queries, OPENROWSET, cross-db identifiers, forbidden tables, unsupported syntax.
- Explicit SQL Server permission grants and data classification approved for each environment; app policy alone is insufficient.
- Validate sensitive-data handling for Claude Code with employer before connecting to workplace data.
- Test timeout, cancellation, row/byte truncation, concurrent invocations, malformed JSON, credential redaction and crash recovery.
- No production access until security review; development databases first.
## Definition of done
Builds cleanly; all acceptance scenarios automated; Claude Code end-to-end demo across two databases; documented runbook and threat model; native Continuum ADL/BDL compile successfully and ADRs formally recorded if Continuum conformance is required.