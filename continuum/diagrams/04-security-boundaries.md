# Security and trust boundaries (editorial)

```mermaid
flowchart LR
  subgraph cloud[Cloud agent context: untrusted for secrets]
    claude[Claude Code]
  end
  subgraph local[Windows user boundary: current-user-only files]
    console[provision console\nsecret typed here only]
    subgraph exe[LocalMCPServer]
      registry[ConnectionRegistry\nSecretRedaction, ApprovedEnvironmentsOnly]
      gateway[QueryGateway\nSelectOnlyAllowlist, QueryBudgets,\nSecretRedaction, ApprovedEnvironmentsOnly]
      knowledge[KnowledgeService\nSecretRedaction, ApprovedEnvironmentsOnly]
    end
    vault[(CredentialVault\nsecret-store, DPAPI)]
    stores[(ConnectionCatalog, InvestigationHistory,\nKnowledgeStore: no secrets)]
  end
  subgraph dbz[Database boundary]
    db[(SQL Server\nread-only principal per alias)]
  end
  claude -- "11 MCP operations, no credential in or out" --> exe
  console -. "local only" .-> registry
  vault -- read --> exe
  exe -- "writes, one writer each" --> stores
  gateway -- "one bounded SELECT, one alias" --> db
  knowledge -- "catalog read" --> db
```

Declared in the model:
- `constrainedBy` guardrails on every service: SelectOnlyAllowlist, QueryBudgets, SecretRedaction, ApprovedEnvironmentsOnly.
- `governedBy: ProductionDataApproval` on every service.
- `CredentialVault` as a `secret-store` that no service writes.
- Requirements `LocalOnlyProvisioning`, `NoSecretDisclosure` and `ReadOnly`.

**Not declared:** the boundary boxes themselves (ADL declares the system/outside divide, not zones) and the dashed console arrow.

**Trust caveat:** redaction and budgets do not make database content safe to send to a cloud model. The data owner must approve the data classifications and environment before any non-fixture database is connected (BDL `SensitiveDataPolicy` is open).

The verified, generated version of this view is `atlas/security-boundaries.svg`.
