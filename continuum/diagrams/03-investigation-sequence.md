# Bounded multi-database investigation — intended behavior

```mermaid
sequenceDiagram
  actor D as Developer
  participant C as Claude Code
  participant M as Local MCP server
  participant A as SQL Server A
  participant B as SQL Server B
  participant H as JSON investigation history
  D->>C: Ask investigation question
  C->>M: investigation_start(question)
  M->>H: Save question and hypotheses
  C->>M: schema_inspect(alias A)
  M->>A: Read authorized metadata
  A-->>M: Bounded schema snapshot
  M-->>C: Schema with freshness/provenance
  C->>M: sql_validate / sql_execute(alias A, SELECT)
  M->>M: Fail-closed ScriptDom AST + policy + budgets
  M->>A: Bounded SELECT as read-only principal
  A-->>M: Bounded result
  M-->>C: Rows, query ID, exact SQL, truncation
  C->>M: sql_validate / sql_execute(alias B, SELECT)
  M->>M: Independent authorization and budgets
  M->>B: Bounded SELECT
  B-->>M: Bounded result
  M-->>C: Evidence with provenance
  C->>C: Correlate; retain ambiguous/unmatched cases
  C->>M: investigation_record(findings, uncertainty)
  M->>H: Atomic history update
  C-->>D: Evidence-backed findings
```

**Status:** Behavior design derived from Vision and B02/B06/B07, not a declared ADL Workflow or verified runtime trace.
