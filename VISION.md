# SQL Investigator — Vision

## The experience

A developer installs SQL Investigator, opens Claude Code, connects to a SQL Server database through conversation, and starts asking questions about the data in ordinary business language.

The intended experience is simple:

1. Install SQL Investigator and register its MCP server with Claude Code. The only database prerequisite for the normal journey is a reachable, authorized SQL Server.
2. Open Claude Code and see SQL Investigator connected.
3. Say, "Connect to this database," providing a connection string or ordinary connection details. Claude Code guides the remaining setup in the conversation. Any secret is entered through a secure mechanism that does not expose it to the model, chat transcript, logs, or command arguments.
4. Ask, "How many requests are there?" Claude Code discovers relevant schema, constructs and runs authorized read-only SQL, and returns an answer with the executed SQL and evidence.
5. Continue asking follow-up questions, including questions involving multiple independently authorized databases.

This must feel conversational, iterative, fast, and dependable. Users should not need to understand the product's internal governance model, manually enumerate tables, translate connection strings into flags, edit configuration files, restart the MCP server, or open another console to complete the ordinary connection-and-question journey.

**Usability is a product requirement, not a secondary convenience or documentation problem.** A technically secure implementation that repeatedly tells users it cannot help and sends them away to perform technical repair steps has failed this vision, even if its architecture and tests conform.

## Product boundary

SQL Investigator is a single-user, local developer tool for Windows and macOS (ADR-007), not a hosted product. Claude Code is the only agent host and user-facing conversational interface. A local .NET 10 MCP server communicates over stdio and supplies guided connection onboarding, authorized schema discovery, SQL validation and execution, investigation history, and reviewed relationship knowledge. No web UI, embedded LLM, provider abstraction, linked server, SQLite, or Continuum runtime dependency.

A CLI may exist for diagnostics, automation, and advanced administration, but must not be a required second user interface for normal onboarding or investigation.

## User-centered operating principles

- **Start with intent, not administration.** "Connect to this database" begins an actionable workflow, not an instruction to go run a provisioning command.
- **Progress in the same conversation.** Connection setup, discovery, scope confirmation, querying, and ordinary recovery should not require an MCP restart or unrelated manual commands.
- **Ask only for information the user actually knows.** Accept common SQL Server connection strings and discrete connection details; do not require prior knowledge of schemas, table names, or security terminology.
- **Secure by design, usable by default.** The product owns the complexity of permissions, limits, secret handling, and auditability. Never solve usability by silently dropping security controls.
- **Actionable failure recovery.** If a step cannot be completed, explain what failed, why, and the smallest safe next action. Whenever authorized and technically possible, offer to perform that action rather than delegating it to the user. Empty states must explain how to proceed.
- **Minimize setup ceremony.** A routine local development connection should not require a chain of undocumented flags, manual configuration changes, or repeated credential entry.
- **Prove outcomes, not just mechanisms.** Passing unit, security, architecture, and protocol tests is not sufficient when a real user cannot ask and answer the first question.

## Core behaviors

### Connection onboarding

- Add, list, inspect, test, refresh, and remove connections through MCP tools callable from Claude Code, with human confirmation for consequential grants or changes.
- Accept SQL Server connection strings as well as separate server, port, database, authentication, encryption, and certificate-trust fields. Never echo or persist secrets in conversational responses, command-line arguments, or non-secret metadata.
- Securely collect or reference credentials without exposing them to the model, using supported client elicitation and/or OS-mediated secret entry or secret references. If a client capability is unavailable, provide a clear safe alternative; never ask a user to paste passwords into chat.
- Discover accessible schemas and tables before requiring table-specific configuration. Present a comprehensible scope summary for approval; permit inclusion and exclusion of tables and sensitive columns, and safe later changes to scope.
- Permit exploration within explicit authorization and actual database permissions. A discoverable object is not automatically authorized for data extraction.
- Make newly provisioned connections immediately available to the running MCP server without restart.
- Provide a guided path to a dedicated read-only principal for local development and fixture databases when one does not exist. Any administrative setup must be explicitly authorized, use a transient secret channel, and never retain an admin credential. If setup cannot be performed safely, explain the precise blocker and next action.
- Identify environment and policy requirements clearly. Never infer that a database is non-production merely because it is on localhost or in Docker.
- Make empty states, missing aliases, mismatched data homes, expired credentials, and connectivity failures distinguishable and recoverable. Provide diagnostics for registration, credential storage, the data directory, and connection health.

### Investigation

- Inspect approved schema and execute bounded, separately authorized read-only T-SQL queries per database.
- Let Claude Code autonomously investigate within enforced policy, correlate bounded results across databases, and identify ambiguous or unmatched records.
- Translate business language into an investigation plan and relevant SQL; show the actual executed SQL, result provenance, and meaningful uncertainty, without inventing results.
- Preserve query evidence, connection alias, timestamps, summaries, findings, confidence, and unresolved hypotheses without retaining raw sensitive results by default.
- Propose inferred relationships with provenance; do not treat them as verified until explicitly reviewed.
- Recover gracefully from SQL errors, schema drift, cancellation, inaccessible systems, and partial results.

## Non-negotiable safety boundaries

The MCP server independently enforces a fail-closed ScriptDom-based T-SQL allowlist, authorized object/column scope, timeouts, row/byte limits, concurrency and query budgets, cancellation, redaction, and auditable rejection. Dedicated least-privilege SQL Server principals remain the final write-prevention boundary. ApplicationIntent=ReadOnly, query validation, and rolled-back transactions are not substitutes for database permissions.

Secrets use the operating system's per-user secret store (Windows DPAPI CurrentUser; the macOS login keychain, application-scoped) and restrictive filesystem permissions; local JSON stores non-secret metadata. Database results and imported documents are untrusted input to the agent.

No production data may be exposed to Claude Code without the employer's approval for the specific data classifications and environment. A read-only query can still leak data or consume server resources; both risks require independent controls. Production access must not be silently enabled by an onboarding shortcut.

If safety requires a confirmation or an exceptional manual step, communicate the reason in user terms. This is an explicit exception to the normal no-terminal journey, not permission to make every user perform technical provisioning.

## Technology choices

Windows x64 and macOS 15+ (arm64, x64); C#/.NET 10; MCP over stdio; Microsoft.Data.SqlClient; Microsoft.SqlServer.TransactSql.ScriptDom; System.Text.Json with atomic replacement and single-writer discipline; Windows DPAPI and the macOS keychain; structured redacted logs; xUnit and disposable SQL Server fixtures.

Implementation details may evolve through governed architectural decisions when needed to achieve the user experience while preserving the safety boundaries.

## Acceptance

### First-run experience — release-blocking

On a clean supported machine with a reachable, authorized local SQL Server development or fixture database:

1. Install the published executable and register the MCP server with Claude Code using one documented installation/registration step.
2. Open Claude Code and verify that the MCP server is connected.
3. Tell Claude Code to connect using a standard connection string or equivalent connection details. Complete the workflow conversationally, with at most one secure out-of-chat credential entry where needed, a readable schema summary, and any necessary authorization confirmation.
4. Ask "How many requests are there?" and receive a correct count, the actual SQL executed by SQL Investigator, and source identification.
5. Ask a follow-up question, still within Claude Code, without re-provisioning or restarting.

For an already-authorized local development database, **no additional manual terminal commands, hand-written SQL, preselected table names, configuration edits, or MCP restarts are allowed after installation**. If a dedicated read-only principal needs to be created, the tool must provide a safe guided setup experience rather than merely directing the user to write administrative SQL; any unavoidable external approval is identified as an explicit prerequisite and tested separately.

The first query must run **through SQL Investigator**, not via a fallback to docker exec, sqlcmd, or another query client. A run that cannot answer the first question fails acceptance regardless of passing lower-level tests.

### Other acceptance

- A developer can investigate two independently authorized databases, receive evidence-backed conclusions, reopen investigation history, and review proposed relationships.
- Negative security tests prove writes, scope bypasses, unsafe objects, secrets disclosure, unauthorized production access, and exceeded budgets are rejected.
- Recovery tests cover empty connection lists, missing/wrong aliases, credential failures, environment mismatch, unsupported secure-prompt mechanisms, schema drift, and disconnected databases with actionable in-conversation responses.
- A cold-start usability evaluation is performed from the published package and README in a fresh Claude Code session, without source-tree assistance. Record the actual steps, interventions, failures, and completion of the first real question. Fix release-blocking defects before shipping.
- The Continuum ADL, BDL, technology bindings, diagrams, governed ADRs, and implementation must agree with the user outcome, safety requirements, and acceptance scenarios in this vision.

## Vision and architectural authority

This Vision defines product intent and required user outcomes. The semantic ADL defines architectural meaning; ADRs govern consequential design tradeoffs and revisions. Neither a model nor an ADR may silently redefine away an explicit user outcome.

When the Vision, ADL, BDL, technology bindings, or ADRs conflict, record the conflict and resolve it through an authorized, traceable decision and corresponding model/vision updates before claiming conformance. Architectural fidelity is necessary but not sufficient: **successful, usable delivery of the intended experience is an independent acceptance requirement**.
