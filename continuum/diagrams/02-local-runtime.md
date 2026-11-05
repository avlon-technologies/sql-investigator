# Local runtime: component allocation (editorial)

```mermaid
flowchart TB
  claude[Claude Code]
  dev[Developer at local console]
  subgraph exe[LocalMCPServer: .NET 10 MCP stdio executable]
    registry[ConnectionRegistry\nconnection_list / connection_remove\nprovision subcommand]
    gateway[QueryGateway\nsql_validate / sql_execute\ninvestigation_start / _record / _get]
    knowledge[KnowledgeService\nschema_inspect / knowledge_search\nknowledge_propose / _review]
  end
  catalog[(ConnectionCatalog\nJSON, alias metadata)]
  vault[(CredentialVault\nDPAPI CurrentUser)]
  history[(InvestigationHistory\nJSON)]
  kstore[(KnowledgeStore\nJSON)]
  db[(SQL Server)]
  claude -- sends --> registry
  claude -- sends --> gateway
  claude -- sends --> knowledge
  dev -. "provision: secret typed at console" .-> registry
  registry -- writes --> catalog
  gateway -- writes --> history
  knowledge -- writes --> kstore
  registry -- reads --> vault
  gateway -- reads --> vault
  knowledge -- reads --> vault
  gateway -- reads --> catalog
  knowledge -- reads --> catalog
  gateway -- "invokes Tsql.ExecuteSelect" --> db
  knowledge -- "invokes Tsql.ReadCatalog" --> db
```

Every solid arrow and the placement of all three services inside `LocalMCPServer` (`deployedAs`) is declared in the ADL. Each JSON store has exactly one writer.

The dashed arrow is **not** a declared relationship. `ProvisionConnection` has no declared sender: ADL has no edge from an Actor to a Command. The provisioning console is also how the DPAPI secret reaches `CredentialVault`, which ADL forbids any service to `write` (`adl/architectural/secret-store-write`).

The verified, generated views are `atlas/c2-containers.svg` and `atlas/c3-components.svg`.
