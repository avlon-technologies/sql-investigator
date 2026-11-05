# Conversational SQL Investigator — revised architecture v0.2
Status: reviewed design; Continuum native compilation pending.

## Purpose and scope
Single-user, disposable developer debugging tool on a Windows work laptop. Claude Code is the only agent host. The tool has no UI, tenant management, hosted service, model abstraction, or commercial product requirements. SQL Server only. Multiple independently registered SQL Server connections, including cross-system investigation. No Continuum runtime dependency.

## Components
1. Claude Code: user conversation, hypothesis generation, T-SQL drafting, correlation of small query results, conclusions with SQL evidence.
2. Local .NET MCP server (stdio): connection registration/list/remove, schema discovery, SQL validation/execution, knowledge and investigation history.
3. SQL safety gateway: T-SQL AST allowlist (ScriptDom), approved connection and object scope, query timeout, result row/byte caps, concurrency and per-investigation budget. Fail closed on parser errors.
4. SQL Server access: Microsoft.Data.SqlClient; each connection uses a dedicated least-privilege read-only database login. Registration tests access but cannot guarantee privileges absent DB-side controls.
5. Local JSON store: connection aliases and non-secret metadata, schema cache, relationships with provenance and review status, investigations. Atomic file replace; no simultaneous writers.
6. Windows user-scoped secret storage: DPAPI-encrypted credentials stored separately from JSON metadata; never return credentials to the model or write them to logs.
7. Optional documentation: Claude reads Markdown or JSON architecture context. Imported facts have provenance and may be stale.

## MCP tools
`connection_register(alias, server, database, credentialReference)` — preferably interactive CLI provisioning outside model conversation; do not paste secrets into Claude.
`connection_list()` / `connection_remove(alias)`
`schema_inspect(alias, schemaFilter)` — allowlisted schema/table/column metadata, no sample rows by default.
`sql_validate(alias, sql)` — parse and report policy violations, no execution.
`sql_execute(alias, sql, investigationId)` — bounded result, exact SQL, query ID, truncation, duration.
`investigation_start(question)` / `investigation_record(id, queryIds, finding)` / `investigation_get(id)`
`knowledge_search(term)` / `knowledge_propose(relation, evidence)` / `knowledge_review(id, decision)` — review may occur conversationally through Claude; no admin UI.

## Investigation flow
User asks a business question -> Claude inspects relevant schema/context -> generates T-SQL -> MCP validates and executes against one authorized database at a time -> Claude correlates bounded results across databases -> reports findings, SQL, confidence and unresolved hypotheses -> investigation history saved.

## Mandatory safeguards
- Dedicated SQL Server read-only login with only necessary SELECT grants; no elevated roles, stored-procedure EXEC, or write grants.
- Deny all except single, parsed, approved SELECT forms; reject multi-batch, EXEC, SELECT INTO, DML/DDL, unsupported statements, dangerous functions, forbidden object references. Parsing alone is not a security proof; SQL principal permissions are the final enforcement boundary.
- Enforce timeout, max rows and bytes, query-count/concurrency limits, cancellation, and server workload safeguards. Even SELECT can cause load or reveal sensitive data.
- Scope access by connection and object allowlists; mask/deny sensitive columns as required by workplace policy.
- Stdio MCP transport and local filesystem permissions; never expose HTTP endpoints by default.
- No raw result retention by default. Save query text and summaries; configurable short retention for approved results.
- Record rejections and execution metadata without passwords or unapproved sensitive rows.
- Model receives query results through the employer-approved Claude Code account; confirm actual organizational data handling policy before real workplace use.

## Explicit exclusions
Web app, SQLite, linked servers, provider abstractions, tenant isolation, hosted deployment, built-in LLM, automatic promotion of inferred relationships, native ADL parsing in the tool.

## Developer-ready gates
Native ADL and BDL parse/compile; threat-model tests; safe-SQL negative tests; SQL principal permissions verified; two-database end-to-end tests; Claude Code MCP smoke test; organizational data handling approval.