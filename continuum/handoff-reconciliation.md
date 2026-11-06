# Handoff reconciliation

This matrix maps each agreement to where the package states it and where it is tested. It is a review aid, written by hand. It adds no claim. Where it disagrees with `model/`, the model governs.

**Sources:**
- `VISION.md`
- `CLAUDE.md`
- the six design-conversation ADRs (`decisions/`)
- the mission brief for this package, which named the approved stack

**State:**
- attestation `sha256:df885cefc7aba2905bf00c676755cd0ec8b3ae6170da605b600ee44c2c7d3a77`
- changeState `sha256:def71c2de556c87d6fcde76ced19b541be633f83f7a19d97098a90742d156a3a`

**Dispositions:**
- **represented**: stated in the right native artifact and covered by a traced scenario or check
- **deferred**: left open on purpose, with a named owner
- **blocked**: needs an act that is outside this package

No row is **missing**.

The ADL qualified names below drop the `::SQLInvestigator.` prefix. In them, `IDA` means `Investigation.InvestigateData` and `QE` means `Investigation.QueryExecution`.

## Product boundary

| # | Intent | Source | Authoritative artifact / element | Verification | Disposition |
|---|---|---|---|---|---|
| 1 | Single-user, local Windows developer tool, not hosted | Vision §Product boundary | ADL `LocalMCPServer`; binding `dotnet-10-mcp-stdio` (`os: windows-x64`) | B12 | represented |
| 2 | Claude Code is the only agent host and conversational interface | Vision; ADR-001 | ADL `ClaudeCode`, `IDA.SoleConversationalHost`, `LocalMCPServer satisfies` | B12 `mcp.tool-surface-exact` | represented. ADR-001 is accepted in the Host (see `decisions/README.md`) |
| 3 | .NET 10 MCP server over stdio; stdout carries protocol only; diagnostics go to stderr | Vision; CLAUDE.md | `SoleConversationalHost`; binding `dotnet-10-mcp-stdio` (`transport: mcp-stdio`) | B12 `mcp.stdout-protocol-only` | represented |
| 4 | Exactly 11 MCP tools | design conversation | the 11 `ClaudeCode sends` messages; IMPLEMENTATION-HANDOFF §3 | B12 `mcp.tool-surface-exact` | represented |
| 5 | SQL Server and T-SQL only | Vision; ADR-002 | ADL `SQLServer` API `Tsql` | B02, B06 | represented (ADR-002 accepted in Host) |
| 6 | Excluded: web UI, embedded LLM, provider abstraction, SQLite, linked servers, Continuum runtime dependency | Vision; CLAUDE.md; ADR-004, ADR-006 | `SoleConversationalHost` (no other surface); `SelectOnlyAllowlist` (linked servers); bindings name no SQLite and no model provider | B12 surface; B03 linked-server and OPENQUERY cases | represented as negative requirements |

## Connections and secrets

| # | Intent | Source | Authoritative artifact / element | Verification | Disposition |
|---|---|---|---|---|---|
| 7 | Connections are provisioned only through a local interactive command, never through chat or MCP | Vision; CLAUDE.md | `IDA.LocalOnlyProvisioning`; `QE.ProvisionConnection`; `QE.ConnectionRegistry`; binding `dotnet-console-provisioning` (`secretEntry: interactive-console-only`) | B01 | represented. ADL cannot declare the Actor → Command sender; the console route is stated in descriptions and the binding |
| 8 | Secrets use DPAPI CurrentUser; files are restricted to the current user | Vision; ADR-004 | `QE.CredentialVault [secret-store]`; binding `windows-dpapi-currentuser` (`protection: DPAPI-CurrentUser`, `acl: current-user-only`); `atomic-json-files` claims `access-controlled` | B01, B09 | represented. No service `write`s the vault (`adl/architectural/secret-store-write`) |
| 9 | Non-secret metadata lives in local JSON, with one writer per store | Vision; CLAUDE.md | `ConnectionCatalog ← ConnectionRegistry`, `InvestigationHistory ← QueryGateway`, `KnowledgeStore ← KnowledgeService`; binding `atomic-json-files` | B07, B08 | represented |
| 10 | No secret in output, errors, logs, history or knowledge | Vision | `IDA.NoSecretDisclosure`; `QE.SecretRedaction` on all three services | B09, B11 `output.excludes-secret` | represented |

## SQL safety

| # | Intent | Source | Authoritative artifact / element | Verification | Disposition |
|---|---|---|---|---|---|
| 11 | Fail-closed ScriptDom allowlist that accepts exactly one bounded SELECT | Vision; CLAUDE.md; ADR-003 | `IDA.ReadOnly`; `QE.SelectOnlyAllowlist`; binding `Microsoft.SqlServer.TransactSql.ScriptDom` | B03 (25-statement corpus), B04 | represented |
| 12 | Object and column scope | Vision §Safety | `SelectOnlyAllowlist`, `ReadOnly` and `ConnectionCatalog` text (added in this pass; a verified description-only diff) | B04 `forbidden-table`, `forbidden-column` | represented |
| 13 | The read-only SQL principal is the final write barrier, tested independently of validation | Vision; CLAUDE.md | `IDA.ReadOnly`; `Test ReadOnlyEnforcement` | B03 `sql.principal-denies-writes` (added in this pass) | represented |
| 14 | Auditable rejection | Vision | `SelectOnlyAllowlist` and `QueryBudgets` statements ("audited"); `atomic-json-files` claims `auditable` | B03, B04, B05 | represented |
| 15 | Timeouts, row and byte limits, concurrency and query budgets, cancellation | Vision | `IDA.BoundedExecution`; `QE.QueryBudgets` | B05, B11 | represented. The numbers are recommended defaults in `development-package.json` `controls`, not architecture |
| 16 | A read-only query can still leak data or consume resources, so independent controls are needed | Vision | `QueryBudgets` together with `ApprovedEnvironmentsOnly` / `ProductionDataApproval` | B04, B05 | represented |
| 17 | No production data without the employer's approval for the specific classifications and environment | Vision; CLAUDE.md | ADL `ProductionDataApproval` policy, realized by `ApprovedEnvironmentsOnly`, governing all services; BDL `SensitiveDataPolicy` | B04 `unauthorized-alias` | **deferred**: the approval mechanism and the classification semantics are open (BDL `SensitiveDataPolicy` unspecified); owner is the data owner |
| 18 | Database results and imported documents are untrusted input | Vision | IMPLEMENTATION-HANDOFF §5 "Untrusted content" | none at server level | **deferred**: how the agent handles untrusted content is Claude Code's behaviour, which this server cannot enforce. The server-side rule is in the handoff |

## Investigation and knowledge

| # | Intent | Source | Authoritative artifact / element | Verification | Disposition |
|---|---|---|---|---|---|
| 19 | Separate bounded queries per database; correlation happens on the agent side | Vision; ADR-005 | `IDA.SeparatePerDatabaseQueries`; `SelectOnlyAllowlist` rejects cross-database references | B06, B04 `cross-database-reference` | represented (ADR-005 accepted in Host) |
| 20 | Autonomous investigation within enforced policy; ambiguous and unmatched records are identified | Vision; ADR-003 | ADR-003; BDL actions and the `FindingStatesUncertainty` invariant | B06 | represented. The correlation algorithm is Claude Code's |
| 21 | Preserve query evidence, alias, timestamps, summaries, findings, confidence and hypotheses; no raw results by default | Vision | `IDA.EvidenceProvenance`; `QueryEvidence` and `Finding` records; BDL `QueryEvidence`, `Finding` | B02, B07 | represented |
| 22 | Inferred relationships stay proposed, with provenance, until reviewed | Vision | `IDA.ReviewedRelationships`; `ProposeRelationship` / `ReviewRelationship`; BDL `RelationshipReviewLifecycle` | B08 | represented |
| 23 | Graceful recovery from SQL errors, schema drift, cancellation and unreachable systems | Vision | `IDA.SafeFailure`, `IDA.SchemaFreshness` | B10, B11 | represented |
| 24 | Continuum is used at design time only; runtime knowledge is optional | ADR-006 | no runtime binding to Continuum | B12 surface | represented (ADR-006 accepted in Host) |

## Stack and delivery

| # | Intent | Source | Authoritative artifact / element | Verification | Disposition |
|---|---|---|---|---|---|
| 25 | Approved stack: Microsoft.Data.SqlClient, System.Text.Json atomic, DPAPI, structured redacted logs | Vision; mission | `local-development.adl-binding.json` (8 bindings, complete) | `package build` binding stage | represented. The logging format is left to the implementation |
| 26 | xUnit with disposable SQL Server fixtures; never production data in tests | Vision; CLAUDE.md | IMPLEMENTATION-HANDOFF §4 and §6; scenarios `connection.fixture` | all scenarios | represented (test tooling is not a bindable element) |
| 27 | End-to-end acceptance: provision, investigate two databases, evidence-backed conclusions, reopen history, review relationships | Vision §Acceptance | scenarios B01–B12, 46 steps | `package run`: 0/12 (stubs) | **blocked** on implementation, as expected before code exists |
| 28 | ADL, BDL, bindings, diagrams and governed ADRs agree and pass their gates | Vision §Acceptance | this package | compile, assess, business compile, build, check, Atlas checks | ADRs **blocked** on the owner: proposed, not accepted. S0 acceptance is not recorded |

## Owner acts that remain

1. Decide ADR-001…006: accept as proposed, or reject and re-propose with `::`-anchored expectations.
2. `continuum package accept continuum --as <you> --verified-against HEAD`, then `continuum ledger record continuum --verified-against HEAD`.
3. The data owner settles `SensitiveDataPolicy` before any non-fixture database is connected.

## Decision-status reconciliation

ADR-001 through ADR-006 were accepted by the Continuum Host under delegated `agent:remote-mcp` authority (events 15–20). Local signed export pending refresh with the Continuum CLI; the repository's model and package baseline have not been newly accepted by this documentation-only sync.
