# ADR review — agreed conversationally, not formally accepted in Continuum
ADR-001 Claude Code exclusively as agent host; local stdio MCP; no agent abstraction and no web UI. Accepted in discussion.
ADR-002 SQL Server and T-SQL only; no provider abstraction. Accepted in discussion.
ADR-003 Claude autonomously generates and executes queries inside enforced read-only and resource boundaries; SQL validation and database least privilege are independent of model. Accepted in discussion.
ADR-004 JSON files for metadata, knowledge and investigation history; DPAPI-protected credentials separately; no SQLite. Accepted in discussion.
ADR-005 Query databases separately and let Claude correlate bounded results; no linked servers. Accepted in discussion.
ADR-006 Optional architecture documentation/JSON supplied to Claude; Continuum used to build the application, not required at runtime. Accepted in discussion.