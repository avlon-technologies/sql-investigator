# Threat model v0.3
Assets: SQL Server data, connection secrets, query history, schema metadata, investigation findings.
Trust boundaries: Claude model ↔ MCP tool; local MCP ↔ database; local process ↔ Windows secret store; imported docs ↔ agent.
Threats/mitigations:
- Prompt injection in database rows or documentation: treat data as untrusted, never grant policy-changing tools to agent.
- Write or privilege escalation via generated SQL: least-privilege SQL login plus strict AST allowlist and reject unknown forms.
- SELECT-based exfiltration: object/column allowlists, row/byte caps, data-classification rules; recognize that caps do not prevent all leakage.
- Resource exhaustion: SQL timeout, bounded concurrency, query budget, cancellation, server-side workload controls.
- Credential leakage: separate local interactive provisioning, DPAPI encryption, redacted errors/logs, restrictive file ACLs.
- Unapproved external transfer: Claude Code is cloud-hosted; verify employer policy for actual data categories before use.
- Inference poisoning: provenance, proposed status, explicit user confirmation before trust promotion.
- Misdiagnosis: attach SQL evidence and timestamps; distinguish hypothesis from verified cause.
Residual risks: read-only queries can lock or consume resources; a compromised local account may access protected credentials; cloud agent sees returned data.