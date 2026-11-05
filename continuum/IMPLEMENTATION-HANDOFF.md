# SQL Investigator implementation handoff (authored; not generated)

**Status: not a verified Continuum package.** The authoritative intent is `VISION.md`; semantic architecture is `model/architecture.adl`; business meaning is `business/sql-investigator.bdl`; technology choices are `model/local-development.adl-binding.json`; scenarios are `acceptance/scenarios/`; decisions are in `decisions/` (candidate only).

## Required delivery

1. Build a .NET 10 Windows console executable speaking MCP over stdio, with stdout reserved for protocol messages and structured redacted diagnostics on stderr. Claude Code is the sole conversational interface.
2. Implement local interactive connection provisioning (never via MCP), DPAPI CurrentUser secret encryption, ACL-protected alias metadata, and atomic single-writer JSON persistence.
3. Implement `connection_list`, `connection_remove`, `schema_inspect`, `sql_validate`, `sql_execute`, `investigation_start`, `investigation_record`, `investigation_get`, `knowledge_search`, `knowledge_propose`, and `knowledge_review` MCP operations. Keep names consistent in code and adapter.
4. Use Microsoft.Data.SqlClient with independently authorized connection aliases and SQL Server read-only principals. Use ScriptDom for a strict allowlist of a single bounded SELECT statement. Reject any unsupported AST node, dynamic execution, linked server, OPENROWSET, cross-database reference, DDL/DML, or multiple batch.
5. Apply configurable timeout, returned row/byte, concurrency and investigation limits; cancellation; truncation; exact SQL/query ID provenance; redaction; audit events. Never assume a SELECT is safe solely because it is syntactically read-only.
6. Persist investigation question, hypotheses, query IDs, exact SQL, aliases, findings and uncertainty. Keep inferred relationships proposed until explicit review; preserve rejection evidence. Cache schema with source and freshness; refresh on drift.
7. Add xUnit unit and disposable SQL Server integration tests. Bind all twelve acceptance scenarios through a real adapter; test the Claude Code MCP journey across two databases with ambiguous and missing keys.

## Hard gates

- Workplace data-owner approval for what may be sent to cloud-hosted Claude Code. **No production connection until approved.**
- Compiler-clean ADL and BDL, successful technology binding realization, generated package build/check, all executable acceptance scenarios passing, governed ADR decisions recorded with verified authority.
- No separate chat UI, generic database provider layer, SQLite, linked servers, hosted service, or Continuum runtime dependency.

## Known architectural gaps

The current ADL compiles with six warnings and is a partial semantic model. The native BDL compiles but covers only initial business capabilities. Binding realization has not been verified. This file is a transparent implementation guide, **not** a substitute for the generated `IMPLEMENTATION.md`.
