# System context

```mermaid
flowchart LR
  developer[Developer / Investigator]
  claude[Claude Code\nExternal agent host]
  subgraph workstation[Windows workstation]
    mcp[SQL Investigator\nLocal .NET 10 MCP stdio server]
    store[(Local JSON history and schema)]
    vault[(DPAPI CurrentUser credentials)]
  end
  sql[(Authorized SQL Server databases)]
  developer --> claude
  claude -. "MCP over stdio" .-> mcp
  mcp --> store
  mcp --> vault
  mcp -. "Scoped read-only queries" .-> sql
```

**Evidence:** Developer, Claude Code, SQL Server, LocalMCPServer, InvestigationHistory and CredentialVault are modeled. Connection routes are described in ADL/Vision, not explicitly declared as relationship edges.
