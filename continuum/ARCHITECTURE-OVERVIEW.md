# Architecture inspection — avlon.sqlinvestigator draft-1

> **Baseline** changeState `sha256:def71c2de556c87d6fcde76ced19b541be633f83f7a19d97098a90742d156a3a` · attestation `sha256:df885cef…` · 0 error(s), 0 warning(s) · core adl/1.6
> **Sources** model `continuum/model/architecture.adl-manifest.json` · business `continuum/model/sql-investigator.bdl` · bindings `continuum/model/local-development.adl-binding.json` · decisions `continuum/decisions/decisions.jsonl` · brief `VISION.md`
>
> Each line says where it stands: **DECLARED** the compiled model says it · **DERIVED** the Host computed it from the model · **BOUND** a binding document says it · **STATED/ASSUMED** the brief or business model says it, with that standing · **PROPOSED** an open decision, not in the model · **ACCEPTED/REJECTED** the decision record says it · **INFERRED** this command reads it from the model's structure (a gap, not a defect) · **UNKNOWN** nothing here establishes it.
> **Decisions are Host-authorized — continuum-decision-record/1, EXPERIMENTAL; all 13 line(s) verified against the Host's published keys.**

## 1. Purpose
- **STATED** (VISION.md) *SQL Investigator — Vision* — A developer asks Claude Code a question about data in one or more SQL Server databases and receives a defensible answer: what was checked, which queries ran, what evidence supports the conclusion, what remains uncertain, and where the data came from. Investigation should feel conversational, iterative, and fast, without sacrificing control of database access.
- **DECLARED** `::SQLInvestigator` — A single-user conversational investigation tool. Claude Code uses a local MCP server to inspect and query independently authorized SQL Server databases.

## 2. Capabilities
- **DECLARED** `::SQLInvestigator.Investigation.InvestigateData` —  *(10 requirement(s); realized by `::SQLInvestigator.Investigation.QueryExecution`, `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry`, `::SQLInvestigator.Investigation.QueryExecution.KnowledgeService`, `::SQLInvestigator.Investigation.QueryExecution.QueryGateway`)*

## 3. Bounded contexts and ownership
- **DECLARED** Domain `::SQLInvestigator.Investigation` (core) —  **Owner:** `::SQLInvestigator.Developer`.
- **DECLARED** BoundedContext `::SQLInvestigator.Investigation.QueryExecution` —  **Owner:** `::SQLInvestigator.Developer`. Services: `ConnectionRegistry`, `KnowledgeService`, `QueryGateway`.
- **DECLARED** actor `::SQLInvestigator.Developer` [person] — The single local user. Converses with Claude Code and provisions connections at a local interactive console.

## 4. Components and interactions
- **DECLARED** service `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry` — Provisions connection aliases through the local interactive console, lists them without secrets, and removes them. The only writer of ConnectionCatalog.
  handles 3 command(s), publishes 0 event(s); writes `ConnectionCatalog`; reads `ConnectionCatalog`, `CredentialVault`; deployed as `::SQLInvestigator.LocalMCPServer`
- **DECLARED** service `::SQLInvestigator.Investigation.QueryExecution.KnowledgeService` — Maintains schema snapshots and proposed cross-database relationships. The only writer of KnowledgeStore.
  handles 4 command(s), publishes 0 event(s); writes `KnowledgeStore`; reads `ConnectionCatalog`, `KnowledgeStore`, `CredentialVault`; deployed as `::SQLInvestigator.LocalMCPServer`
- **DECLARED** service `::SQLInvestigator.Investigation.QueryExecution.QueryGateway` — Local .NET MCP stdio service validating SQL and enforcing budgets before execution. The only writer of InvestigationHistory.
  handles 5 command(s), publishes 0 event(s); writes `InvestigationHistory`; reads `InvestigationHistory`, `ConnectionCatalog`, `CredentialVault`; deployed as `::SQLInvestigator.LocalMCPServer`
- **DECLARED** deployable `::SQLInvestigator.LocalMCPServer` — Windows .NET 10 executable. Started by Claude Code, it speaks MCP over stdio; run by the developer with the provisioning subcommand, it offers the local interactive console. Hosts `ConnectionRegistry`, `KnowledgeService`, `QueryGateway`.
- **DECLARED** data-store `::SQLInvestigator.Investigation.QueryExecution.ConnectionCatalog` — written by `ConnectionRegistry`
- **DECLARED** secret-store `::SQLInvestigator.Investigation.QueryExecution.CredentialVault` — written by nothing
- **DECLARED** data-store `::SQLInvestigator.Investigation.QueryExecution.InvestigationHistory` — written by `QueryGateway`
- **DECLARED** data-store `::SQLInvestigator.Investigation.QueryExecution.KnowledgeStore` — written by `KnowledgeService`
- **DECLARED** 6 commands, 0 events, 0 read model(s), 20 type definitions

## 5. Technology decisions
- **DECLARED** the model names no technology: an ADL model is semantic, and technology arrives only through binding documents
- **BOUND** `::SQLInvestigator.LocalMCPServer` → dotnet-10-mcp-stdio {"runtime":"net10.0","language":"csharp","transport":"mcp-stdio","os":"windows-x64"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.ConnectionCatalog` → atomic-json-files {"serializer":"System.Text.Json","writes":"atomic-replace-single-writer","retention":"metadata-only-no-secrets"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.CredentialVault` → windows-dpapi-currentuser {"protection":"DPAPI-CurrentUser","acl":"current-user-only"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.InvestigationHistory` → atomic-json-files {"serializer":"System.Text.Json","writes":"atomic-replace-single-writer","retention":"no-raw-results-default"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.KnowledgeStore` → atomic-json-files {"serializer":"System.Text.Json","writes":"atomic-replace-single-writer","retention":"metadata-only-no-row-values"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry` → dotnet-console-provisioning {"runtime":"net10.0","entry":"LocalMCPServer provision subcommand","secretEntry":"interactive-console-only","protection":"DPAPI-CurrentUser"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.KnowledgeService` → dotnet-sqlclient-service {"runtime":"net10.0","persistence":"System.Text.Json","driver":"Microsoft.Data.SqlClient"} *(continuum/model/local-development.adl-binding.json)*
- **BOUND** `::SQLInvestigator.Investigation.QueryExecution.QueryGateway` → sqlclient-scriptdom {"driver":"Microsoft.Data.SqlClient","validator":"Microsoft.SqlServer.TransactSql.ScriptDom","policy":"fail-closed-select-only"} *(continuum/model/local-development.adl-binding.json)*
- **DERIVED** bindings judgment: complete

## 6. Behavioural obligations
- **DECLARED** requirement `InvestigateData.BoundedExecution` — Every query runs within configured timeout, row, byte, concurrency and per-investigation query budgets, can be cancelled, and reports when its result was truncated. *(verified by `BudgetSuite`)*
- **DECLARED** requirement `InvestigateData.EvidenceProvenance` — Every executed query carries a query id, the exact SQL, the connection alias and timestamps; investigations persist questions, hypotheses, findings and uncertainty without raw result rows by default. *(verified by `ProvenanceSuite`)*
- **DECLARED** requirement `InvestigateData.LocalOnlyProvisioning` — Connections and their credentials are provisioned only through a local interactive command run by the developer; no MCP operation accepts or returns a credential. *(verified by `ConnectionProvisioningSuite`)*
- **DECLARED** requirement `InvestigateData.NoSecretDisclosure` — Credentials and connection secrets never appear in MCP output, diagnostics, investigation history or the knowledge store. *(verified by `SecretRedactionSuite`)*
- **DECLARED** requirement `InvestigateData.ReadOnly` — Only bounded, authorized read-only SQL queries may execute. The gateway accepts exactly one bounded SELECT per request, referencing only objects and columns within the alias's approved scope, through a fail-closed allowlist, and a dedicated read-only SQL principal denies writes independently of that validation. *(verified by `ReadOnlyEnforcement`)*
- **DECLARED** requirement `InvestigateData.ReviewedRelationships` — Inferred cross-database relationships stay proposed, with provenance, until explicitly reviewed; rejected relationships and their evidence are retained and never promoted. *(verified by `RelationshipReviewSuite`)*
- **DECLARED** requirement `InvestigateData.SafeFailure` — SQL errors, cancellation, schema drift and unreachable databases produce actionable, sanitized errors and never fabricated results. *(verified by `SafeFailureSuite`)*
- **DECLARED** requirement `InvestigateData.SchemaFreshness` — Schema snapshots record their source and capture time and are refreshed when drift is detected. *(verified by `SchemaDriftSuite`)*
- **DECLARED** requirement `InvestigateData.SeparatePerDatabaseQueries` — Each query targets exactly one connection alias. Cross-database correlation happens on the agent side over separate bounded results, never in cross-database or linked-server SQL. *(verified by `CorrelationSuite`)*
- **DECLARED** requirement `InvestigateData.SoleConversationalHost` — Claude Code is the only conversational interface. The server speaks MCP over stdio, keeps stdout for protocol messages only, and offers no other chat, web or model surface. *(verified by `ClaudeOnlySuite`)*
- **DECLARED** policy `ProductionDataApproval` — No production data reaches Claude Code until the data owner has approved the specific data classifications and environment.
- **DERIVED** behaviour: 1 starting message(s), 0 underspecified-causality finding(s), 0 workflow(s); every declared causal chain is closed

## 7. Assumptions
- **BDL** 46 business element(s): 1 stated, 45 assumed, 0 underspecified, 0 questioned
- **ASSUMED** 1 business domain(s) recorded as assumed, with no provenance — Investigation
- **ASSUMED** 5 business capability(s) recorded as assumed, with no provenance — ProvisionAuthorizedAccess, ExploreAuthorizedData, CorrelateEvidence, PreserveProvenance, CurateRelationshipKnowledge
- **ASSUMED** 6 business entity(s) recorded as assumed, with no provenance — ConnectionAlias, SchemaSnapshot, InvestigationCase, QueryEvidence, Finding, InferredRelationship
- **ASSUMED** 1 business state-model(s) recorded as assumed, with no provenance — RelationshipReviewLifecycle
- **ASSUMED** 2 business transition(s) recorded as assumed, with no provenance — Accept, Reject
- **ASSUMED** 3 business enum(s) recorded as assumed, with no provenance — EnvironmentClass, RelationshipState, Confidence
- **ASSUMED** 13 business action(s) recorded as assumed, with no provenance — ProvisionConnection, RemoveConnection, ApproveAgentAccess, InspectSchema, StartInvestigation, ExecuteReadQuery, …
- **ASSUMED** 4 business fact(s) recorded as assumed, with no provenance — ConnectionProvisioned, QueryExecuted, QueryRejected, RelationshipReviewed
- **ASSUMED** 6 business rule(s) recorded as assumed, with no provenance — ProductionRequiresApproval, OneDatabasePerQuery, BoundedQueries, ReviewBeforeTrust, NoFabricatedResults, NoRawResultRetention
- **ASSUMED** 1 business invariant(s) recorded as assumed, with no provenance — FindingStatesUncertainty
- **ASSUMED** 3 business actor(s) recorded as assumed, with no provenance — Investigator, InvestigationAgent, DataOwner

## 8. Risks and trade-offs
- none recorded

## 9. Unknowns
- none found

## 10. Decisions
### ADR-001 — Claude Code as sole agent host · **ACCEPTED IN HOST**
- Use Claude Code as the only conversational agent. SQL Investigator is a local .NET MCP stdio process that Claude Code launches; there is no separate web UI, chat application or embedded local model.
- Rationale: A standalone chat application, web service or embedded LLM adds operational and security surfaces without improving the investigation workflow.
- Alternative considered: Standalone chat application
- Alternative considered: Hosted web service
- Alternative considered: Embedded local LLM
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8
### ADR-002 — SQL Server and T-SQL only · **ACCEPTED IN HOST**
- Support only SQL Server and T-SQL, using Microsoft.Data.SqlClient for access and Microsoft.SqlServer.TransactSql.ScriptDom for validation.
- Rationale: A generic database provider abstraction would dilute the grammar-specific safety guarantees the validator relies on.
- Alternative considered: Generic multi-engine provider abstraction
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8
### ADR-003 — Bounded autonomous read-only investigation · **ACCEPTED IN HOST**
- Allow Claude Code to run multiple SELECT investigations without per-query human approval, only within independent, fail-closed MCP-side and SQL Server-side policy boundaries.
- Rationale: Per-SELECT human approval breaks the intended conversational workflow; trusting the model to police itself is unsafe, so enforcement must be independent of the agent.
- Alternative considered: Human approval of every SELECT
- Alternative considered: Trust the agent to self-police
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8
### ADR-004 — Local JSON and DPAPI, not SQLite · **ACCEPTED IN HOST**
- Use System.Text.Json files with atomic replacement for connection metadata, knowledge and history; protect credentials separately with Windows DPAPI CurrentUser.
- Rationale: SQLite adds a database dependency that a single-user local tool does not need; plaintext credentials are unacceptable.
- Alternative considered: SQLite
- Alternative considered: Plaintext or config-file credentials
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8
### ADR-005 — Separate queries and agent-side correlation · **ACCEPTED IN HOST**
- Query each authorized database independently through its own alias; Claude Code correlates the bounded observations and reports unmatched and ambiguous matches.
- Rationale: Linked servers and cross-database server-side joins broaden privileges and create hidden coupling between independently authorized databases.
- Alternative considered: Linked servers
- Alternative considered: Cross-database server-side joins
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8
### ADR-006 — Continuum at design time, optional knowledge at runtime · **ACCEPTED IN HOST**
- Use Continuum for architecture governance and the implementation handoff only. Optional exported context files may be imported as knowledge, but the executable has no Continuum runtime dependency.
- Rationale: A runtime dependency would complicate a small local investigation tool and tie it to a networked service.
- Alternative considered: Continuum Host or MCP as a runtime dependency
- Proposed by agent:claude-code acting for Michael Kang (Host-verified: https://login.microsoftonline.com/f7ef9e19-20fa-47ac-91dd-47890978afe8/v2.0 oid 2605c7a8-5120-4edb-98cc-76d563601c0f) under Host delegation 4686a9d1-45e0-425f-9574-fd68019a97c8


> Decision statuses above are synchronized from Continuum Host acceptance events 15–20. This overview is a human-readable snapshot; the Host is authoritative and the local signed decision export still requires refresh.
