# SQL Investigator — Vision

## The experience

A developer asks Claude Code a question about data in one or more SQL Server databases and receives a defensible answer: what was checked, which queries ran, what evidence supports the conclusion, what remains uncertain, and where the data came from. Investigation should feel conversational, iterative, and fast, without sacrificing control of database access.

## Product boundary

SQL Investigator is a single-user, local Windows developer tool, not a hosted product. Claude Code is the only agent host and user-facing conversational interface. A local .NET 10 MCP server communicates over stdio and supplies authorized schema discovery, SQL validation and execution, investigation history, and reviewed relationship knowledge. No web UI, embedded LLM, provider abstraction, linked server, SQLite, or Continuum runtime dependency.

## Core behaviors

- Register independently authorized SQL Server connections using local interactive credential provisioning, never by pasting secrets into chat.
- Inspect approved schema and execute separate, bounded read-only T-SQL queries per database.
- Let Claude Code autonomously investigate within enforced policy, correlate bounded results across databases, and identify ambiguous or unmatched records.
- Preserve query evidence, connection alias, timestamps, summaries, findings, confidence, and unresolved hypotheses without retaining raw sensitive results by default.
- Propose inferred relationships with provenance; do not treat them as verified until explicitly reviewed.
- Recover gracefully from SQL errors, schema drift, cancellation, and inaccessible systems.

## Non-negotiable safety boundaries

The MCP server independently enforces a fail-closed ScriptDom-based T-SQL allowlist, object/column scope, timeouts, row/byte limits, concurrency and query budgets, cancellation, redaction, and auditable rejection. Dedicated least-privilege SQL Server principals remain the final write-prevention boundary. Secrets use Windows DPAPI CurrentUser and restrictive filesystem permissions; local JSON stores non-secret metadata. Database results and imported documents are untrusted input to the agent.

No production data may be exposed to Claude Code without the employer's approval for the specific data classifications and environment. A read-only query can still leak data or consume server resources; both risks require independent controls.

## Technology choices

Windows x64; C#/.NET 10; MCP over stdio; Microsoft.Data.SqlClient; Microsoft.SqlServer.TransactSql.ScriptDom; System.Text.Json with atomic replacement and single-writer discipline; Windows DPAPI; structured redacted logs; xUnit and disposable SQL Server fixtures.

## Acceptance

A developer can provision a connection locally, investigate two independently authorized databases through Claude Code, receive evidence-backed conclusions, reopen investigation history, and review proposed relationships. Negative security tests must prove writes, bypasses, unsafe objects, secrets disclosure, and exceeded budgets are rejected. The Continuum ADL, BDL, technology bindings, diagrams, and governed ADRs must agree with this vision and pass their respective verification gates.

The Vision explains intent. The semantic ADL defines architectural meaning. If they conflict, the semantic model governs.
