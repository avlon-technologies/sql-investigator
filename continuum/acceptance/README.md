# SQL Investigator acceptance contract

Twelve authored Continuum scenario documents cover B01–B12. They reference compiled ADL elements and define executable step keys. The implementation must supply a real `createAdapter` binding to its .NET MCP process and SQL Server fixtures. **These scenarios have not been executed; no implementation or adapter exists.**

Use disposable SQL Server databases and a read-only principal. Never use production data for tests. A future package build must validate all scenario references, derive the runner, and generate the trace/coverage reports. A package run must execute every scenario against the implementation adapter.

The negative SQL corpus must be extended to cover nested queries, CTE variations, SQL comments, parser ambiguity, and SQL Server-specific escapes. Tests must demonstrate DB-level write denial independent of the AST validator.
