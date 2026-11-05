# Local runtime — component allocation

```mermaid
flowchart TB
  claude[Claude Code]
  subgraph exe[LocalMCPServer — .NET 10 MCP stdio executable]
    gateway[QueryGateway\nScriptDom validation / budgets]
    knowledge[KnowledgeService\nSchema snapshots / relationship review]
  end
  history[(InvestigationHistory\nAtomic JSON)]
  kstore[(KnowledgeStore\nAtomic JSON)]
  secrets[(CredentialVault\nDPAPI CurrentUser)]
  db[(SQL Server)]
  claude -. "MCP stdio" .-> gateway
  claude -. "MCP stdio" .-> knowledge
  gateway -- writes --> history
  gateway -- reads --> secrets
  gateway -. "SqlClient / authorized SELECT" .-> db
  knowledge -- writes --> kstore
  knowledge -. "Schema inspection" .-> db
```

Solid arrows and the placement of both services inside `LocalMCPServer` are declared in the ADL (`writes`, `reads`, `deployedAs`). Dashed arrows are intended integrations the model does not declare.
