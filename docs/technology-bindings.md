# Proposed technology bindings v0.2
Runtime: .NET 10 C#, Windows x64.
Protocol: MCP over stdio, using compatible .NET MCP SDK.
SQL driver: Microsoft.Data.SqlClient.
SQL grammar validation: Microsoft.SqlServer.TransactSql.ScriptDom, SQL Server dialect version pinned and tested.
Secrets: Windows DPAPI CurrentUser; protect file ACLs; no secret in Claude Code conversation.
Persistence: System.Text.Json; atomic file replacement and single-process locking.
Logging: Microsoft.Extensions.Logging with redaction; local JSON-lines audit metadata.
Tests: xUnit, SQL Server test containers or approved disposable test databases; Claude Code MCP smoke tests.
Deployment: local CLI executable configured in Claude Code MCP settings.
No HTTP server, Razor Pages, EF Core or SQLite.