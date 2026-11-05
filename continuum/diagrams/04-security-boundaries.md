# Security and data boundaries

```mermaid
flowchart LR
  subgraph local[Windows user security boundary]
    provision[Local interactive credential provisioning]
    dpapi[(DPAPI CurrentUser secret store)]
    metadata[(Alias metadata and history JSON)]
    mcp[MCP server\nAST policy / limits / redaction]
    provision --> dpapi
    provision --> metadata
    dpapi --> mcp
    metadata --> mcp
  end
  db[(SQL Server\nLeast-privilege read-only principal)]
  cloud[Claude Code / cloud agent context]
  mcp -. "Read-only SQL / TLS as configured" .-> db
  db -. "Bounded results" .-> mcp
  mcp -. "Redacted bounded MCP output" .-> cloud
```

**Trust caveat:** Redaction and query limits do not automatically make database content safe to send to a cloud model. Obtain explicit employer/data-owner authorization for each dataset before connecting production systems. Credentials are provisioned locally, never through the chat or MCP tool arguments.
