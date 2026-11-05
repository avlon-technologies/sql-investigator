# Local runtime — provisional component allocation

```mermaid
flowchart TB
  claude[Claude Code]
  subgraph exe[LocalMCPServer — .NET 10 MCP stdio executable]
    gateway[QueryGateway\nScriptDom validation / budgets]
    knowledge[KnowledgeService\nSchema snapshots / relationship review]
    history[(InvestigationHistory\nAtomic JSON)]
    secrets[(CredentialVault\nDPAPI CurrentUser)]
  end
  db[(SQL Server)]
  claude -. "MCP stdio" .-> gateway
  gateway --> history
  gateway --> secrets
  gateway -. "SqlClient / authorized SELECT" .-> db
  knowledge --> history
  knowledge -. "Schema inspection" .-> db
```

**Modeling gap:** QueryGateway and KnowledgeService are declared Services; LocalMCPServer is a declared Deployable. Their placement inside that deployable is a **design intent**, not yet supported by `deployedAs` relationships. Correct ADL before treating this as a mechanically proven C2/C3 diagram.
