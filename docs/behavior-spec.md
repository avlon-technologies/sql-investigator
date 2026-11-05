# BDL candidate — executable acceptance criteria v0.2
Native Continuum BDL compilation pending.

B01 Register: given an alias and SQL connection configuration, registering creates non-secret JSON metadata and separately encrypted credentials. No secret is emitted in MCP responses or logs.
B02 Read-only: given a SELECT within approved objects, execute returns exact SQL, query ID, capped rows and truncation indicator.
B03 Reject writes: INSERT, UPDATE, DELETE, MERGE, DDL, EXEC, SELECT INTO and multi-batches are rejected before DB execution.
B04 Reject bypass: parser ambiguity, unavailable policy service, unapproved tables/columns, and unauthorized connection aliases fail closed.
B05 Bounded resources: timeout, row, byte, concurrency and investigation budgets are enforced and auditable.
B06 Cross-database: Claude issues separate read-only queries to two connections, correlates on explicitly observed keys, and labels unmatched/ambiguous records.
B07 Investigation: record question, hypotheses, SQL, connection aliases, query IDs, summaries, findings and uncertainties; reopening returns history.
B08 Knowledge: newly inferred links remain proposed with evidence; explicit review accepts/rejects; rejected links remain available as negative knowledge.
B09 Secrets: logs, JSON metadata, model-visible MCP tool outputs and exception messages never contain passwords.
B10 Schema changes: cached schema has source/time; refresh detects changed columns; failed SQL due to drift triggers a refresh rather than fabricated results.
B11 Failure: unavailable SQL Server, cancellation, invalid credentials and malformed SQL produce actionable errors without exposing secrets.
B12 No UI: all routine operations are accessible through Claude Code/MCP; sensitive credential entry uses local protected provisioning, not chat.