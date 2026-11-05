# Bounded multi-database investigation (editorial sequence)

```mermaid
sequenceDiagram
  actor D as Developer
  participant C as Claude Code
  participant Q as QueryGateway
  participant K as KnowledgeService
  participant A as SQL Server alias A
  participant B as SQL Server alias B
  participant H as InvestigationHistory
  participant S as KnowledgeStore
  D->>C: Ask an investigation question
  C->>Q: investigation_start(question)
  Q->>H: save question
  C->>K: schema_inspect(A)
  K->>A: Tsql.ReadCatalog
  K->>S: save snapshot with source and capture time
  K-->>C: SchemaSnapshot
  C->>Q: sql_validate / sql_execute(A, SELECT)
  Q->>Q: SelectOnlyAllowlist + QueryBudgets
  Q->>A: Tsql.ExecuteSelect as read-only principal
  Q->>H: query id, exact SQL, alias, timestamps (no rows)
  Q-->>C: QueryEvidence (rows, truncation flag)
  C->>Q: sql_execute(B, SELECT)
  Q->>B: Tsql.ExecuteSelect
  Q-->>C: QueryEvidence
  C->>C: correlate; label unmatched and ambiguous records
  C->>K: knowledge_propose(relationship, evidence)
  K->>S: relationship stays proposed
  C->>Q: investigation_record(hypothesis, finding, uncertainty)
  Q->>H: atomic update
  C-->>D: evidence-backed findings with uncertainty
```

The model declares each **participant** and each **hop's endpoints** (`sends`, `handles`, `invokes`, `reads`, `writes`). It does **not** declare this ordering: there is no ADL Workflow, because Claude Code drives the investigation and the server only answers calls. `continuum preview` reports the declared causality as closed, with no underspecified findings. The order above is illustrative, taken from the Vision and scenarios B02, B06, B07 and B08. It is not a runtime trace.
