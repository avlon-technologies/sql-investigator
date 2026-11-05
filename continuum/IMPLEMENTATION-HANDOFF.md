# SQL Investigator implementation handoff (hand-authored; complements the generated IMPLEMENTATION.md)

**Status:** the development package builds and checks clean (attestation `sha256:cc76d530…`, changeState `sha256:78a0887b…`, 0 compiler warnings, LocalDevelopment binding complete, 12 scenarios resolved). The acceptance adapter is scaffolded and every step is a stub, so 0 / 12 scenarios pass. The initial accepted state (S0) has not been recorded by the owner yet.

Authority: `model/architecture.adl` governs; `IMPLEMENTATION.md` is its generated contract. This file adds delivery detail the model does not carry. If they disagree, the model wins.

## Required delivery

1. A .NET 10 Windows console executable (`LocalMCPServer`) speaking MCP over stdio, with stdout reserved for protocol messages and structured redacted diagnostics on stderr. Claude Code is the sole conversational interface.
2. `QueryGateway` and `KnowledgeService` both run inside that executable.
3. Local interactive connection provisioning (never via MCP), DPAPI CurrentUser encryption in `CredentialVault`, ACL-protected alias metadata, atomic single-writer JSON persistence.
4. MCP operations: `connection_list`, `connection_remove`, `schema_inspect`, `sql_validate`, `sql_execute`, `investigation_start`, `investigation_record`, `investigation_get`, `knowledge_search`, `knowledge_propose`, `knowledge_review`. Keep names identical in code and in `acceptance/adapter.mjs`.
5. Microsoft.Data.SqlClient with independently authorized connection aliases and read-only SQL Server principals. ScriptDom strict allowlist of a single bounded SELECT. Reject any unsupported AST node, dynamic execution, linked server, OPENROWSET, cross-database reference, DDL/DML, SELECT INTO or multiple batches.
6. Configurable timeout, row/byte, concurrency and per-investigation query limits (recommended defaults in `development-package.json`: `MAX_QUERY_ROWS=500`, `SQL_TIMEOUT_SECONDS=15`); cancellation; truncation flags; exact SQL and query-id provenance; redaction; audit events. Never assume a SELECT is safe because it parses as read-only.
7. Persist question, hypotheses, query ids, exact SQL, aliases, findings and uncertainty in `InvestigationHistory` (written only by QueryGateway). Do not retain raw result rows by default.
8. Persist schema snapshots (with source and freshness) and inferred relationships (with provenance and review state) in `KnowledgeStore` (written only by KnowledgeService). Keep inferred relationships proposed until explicitly reviewed; keep rejection evidence. Refresh snapshots on drift.

## Obligations from the bindings

Both JSON stores are bound to `atomic-json-files`, which claims `retention-honored`, `erasure-capable`, `access-controlled` and `auditable`. The implementation must deliver all four: a retention rule that is enforced, a way to delete an investigation or knowledge entry, current-user-only file ACLs, and audit events for writes.

## Tests

- xUnit unit tests and disposable SQL Server integration tests.
- Bind all 12 scenarios in `acceptance/adapter.mjs` against the real MCP process; test the Claude Code journey across two databases with ambiguous and missing keys.
- The negative SQL corpus (scenarios B03/B04, test `ReadOnlyEnforcement`) must cover nested queries, CTE variations, SQL comments, parser ambiguity and SQL Server-specific escapes.
- Prove DB-level write denial independently of the AST validator.
- Use disposable databases and a read-only principal. Never production data.

## Hard gates

- Data-owner approval for what may be sent to cloud-hosted Claude Code. **No production connection until approved.**
- All 12 acceptance scenarios passing; `continuum package check continuum` intact; the CI architecture gate green.
- No separate chat UI, generic database provider layer, SQLite, linked servers, hosted service, or Continuum runtime dependency.

## What the model leaves to the implementation

Recorded here so no one mistakes it for architecture: MCP tool schemas, the exact allowlist grammar, budget numbers, retention period, how SQL Server is reached (the model declares `SQLServer` and `ClaudeCode` as external systems but no edges to them), and the sensitive-data policy (open in the BDL as `SensitiveDataPolicy`). If any of these needs to become architecture, record a gap in `continuum/gaps/` and propose an `.adl-change`.
