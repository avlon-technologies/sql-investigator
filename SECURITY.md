# Security policy

SQL Investigator gives an AI agent read access to SQL Server databases, so security reports are welcome and taken
seriously.

## Reporting a vulnerability

**Please do not open a public issue for a vulnerability.** Report it privately through GitHub's
[private vulnerability reporting](https://github.com/avlon-technologies/sql-investigator/security/advisories/new)
for this repository.

Please include the version (`SqlInvestigator --version`), your operating system, and the smallest reproduction you
can. Never include real credentials or production data. We aim to acknowledge reports within five business days. This
is a best-effort open-source project, so there is no guaranteed fix timeline and no bug bounty.

## In scope

- A statement that gets past the SQL allowlist (`Queries/SqlValidator.cs`) and writes, alters or executes, or reads
  objects or columns outside an alias's approved scope.
- A secret (password or connection string) that reaches stdout, stderr, a tool result, a store file or the audit trail.
- A way to supply or read credentials through MCP tool arguments.
- A budget (timeout, rows, bytes, concurrency, per-investigation queries) that can be bypassed.
- Weak permissions on the data folder, vault files or keychain items.

## Out of scope

- An over-privileged SQL login. The read-only SQL principal is a required control that **you** configure. SQL
  Investigator checks it but cannot replace it.
- Data that an approved, read-only query legitimately returns to the agent. Deciding which data an AI agent may see is
  the operator's responsibility.
- Prompt injection that stays inside the enforced read-only, scoped and budgeted boundary.
- Vulnerabilities in Claude Code, SQL Server, .NET or other third-party components. Please report those upstream.

## Supported versions

Only the latest release receives security fixes.
