# Implementation notes

Decisions the architecture deliberately leaves to the implementation (`continuum/IMPLEMENTATION-HANDOFF.md` §8), and how the code meets the package's claims. The model governs; nothing here adds an architectural element.

## Choices

| Concern | Choice | Why |
|---|---|---|
| MCP SDK | [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) 2.3.1, the official C# SDK, stdio transport | Maintained with the MCP specification, attribute-based tools, structured content, cancellation flows into the tool's `CancellationToken` |
| Hosting | `Microsoft.Extensions.Hosting` empty application builder; one process, three singleton services | No web host, no hosted service beyond the stdio transport |
| SQL | `Microsoft.Data.SqlClient` 7.1.1 | Bound by `dotnet-sqlclient-service` / `sqlclient-scriptdom` |
| Validator | `Microsoft.SqlServer.TransactSql.ScriptDom` 180, `TSql160Parser` | Bound by `sqlclient-scriptdom` |
| Secrets | `System.Security.Cryptography.ProtectedData`, DPAPI `CurrentUser`, per-alias entropy | Bound by `windows-dpapi-currentuser` |
| Persistence | `System.Text.Json` files, atomic replace (temp file, flush with write-through, `File.Move` overwrite), one named mutex per store | Bound by `atomic-json-files` |
| Target | `net10.0-windows`, `win-x64`; framework-dependent for development, self-contained for the release zip (`eng/release.ps1`) | Bound by `dotnet-10-mcp-stdio` (runtime, OS, transport); the deployment mode is not bound |
| Tests | xUnit 2.9; fixtures are disposable SQL Server databases with synthetic data | Handoff §4 |

`System.Security.Cryptography.Pkcs` is pinned to 10.0.14 because the version SqlClient brings in transitively carries known advisories (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx).

## Where each guarantee lives

| Model element | Code |
|---|---|
| `ConnectionRegistry` (only writer of `ConnectionCatalog`) | `Connections/ConnectionRegistry.cs`; the console flow is in `Connections/ProvisioningConsole.cs` |
| `QueryGateway` (only writer of `InvestigationHistory`) | `Queries/QueryGateway.cs`, `Queries/InvestigationHistory.cs` |
| `KnowledgeService` (only writer of `KnowledgeStore`) | `Knowledge/KnowledgeService.cs`, `Knowledge/KnowledgeStore.cs` |
| `CredentialVault` (no writing service) | `Connections/CredentialVault.cs`: services use `CredentialVault.ReadSecret`. `VaultProvisioner` is reached only from the console |
| `SelectOnlyAllowlist` | `Queries/SqlValidator.cs` |
| `QueryBudgets` | `QueryGateway.ExecuteAsync` / `RunAsync`; `Infrastructure/Settings.cs` |
| `SecretRedaction` | `Infrastructure/Redaction.cs`, applied in `Mcp/ToolResults.cs`, `Infrastructure/Diagnostics.cs`, store writes and errors |
| `ApprovedEnvironmentsOnly` | `ConnectionCatalogReader.Require` (query time), `ConnectionRegistry.RecordProvisioned` and the console (provisioning time) |
| `SoleConversationalHost` | `Program.cs`: no arguments means MCP stdio, and `Console.Out` is pointed at stderr so only the transport writes stdout |
| `ReadOnly` (second barrier) | `Connections/SqlConnections.cs` `PrincipalGuard`, plus each query in a transaction that is always rolled back |

## The allowlist, in detail

A statement is accepted only if every one of these holds:

1. Non-empty and at most `MAX_SQL_LENGTH` characters.
2. It parses with `TSql160Parser` with `QUOTED_IDENTIFIER ON` (the session setting the gateway uses) and with no errors.
3. It parses to the same script with `QUOTED_IDENTIFIER OFF`. Otherwise it is rejected as **ambiguous**: `"OrderId"` would be a column under one setting and a string under the other.
4. Exactly one batch holding exactly one statement, of type exactly `SelectStatement`, with no `INTO`.
5. **Every node** of the parse tree is one of an explicit list of node types: query expressions, joins, predicates, scalar expressions, literals and built-in calls. Everything else is rejected by name, including variables, `@@` globals, rowset functions (OPENROWSET/OPENQUERY/OPENDATASOURCE/OPENJSON/OPENXML), table-valued functions, table and query hints, `FOR XML/JSON/BROWSE`, `TABLESAMPLE`, `NEXT VALUE FOR`, `COMPUTE` and `WITH XMLNAMESPACES`.
6. **Every function** is on a list of built-ins (aggregates, ranking and offset functions, string, math and date functions) and is not called on a target. So there are no user-defined, metadata or security functions. Date-part keywords are allowed only as the first argument of a date function.
7. **Every table** is a two-part `schema.object` in the alias's scope, or the name of a CTE of the statement. One-part names are rejected as ambiguous, three-part names as cross-database and four-part names as linked-server. `FOR SYSTEM_TIME` is rejected.
8. **Every column** is approved:
   - A qualified column is checked against the table its qualifier resolves to.
   - An unqualified column must be approved on every restricted table it could resolve to, in its own query and every enclosing query, because T-SQL resolves names outward.
   - `*` is rejected over a table with restricted columns. `COUNT(*)` is allowed.
   - `ORDER BY` may name the query's own output aliases.

The validator returns every reason it found. A rejection at validation or at catalog resolution (unknown alias, unreadable policy) is written to the history audit before anything is executed.

## Budgets, cancellation and failures

- **Concurrency.** A semaphore taken with a zero wait. A query over the limit is refused immediately and audited.
- **Per-investigation count.** Checked and reserved in the same locked store write, so two concurrent queries cannot both take the last slot.
- **Timeout.** The server-side `CommandTimeout` (`SQL_TIMEOUT_SECONDS`) plus a client-side backstop token. The session also sets `LOCK_TIMEOUT`.
- **Rows and bytes.** The reader stops at `MAX_QUERY_ROWS` rows or `MAX_RESULT_BYTES` (UTF-8 size of the values). The result carries `truncated` and `truncatedBy` (`rows` | `bytes`), the command is cancelled, and a `budget.rows` / `budget.bytes` audit event is written.
- **Cancellation.** An MCP `notifications/cancelled` cancels the tool's token. The command is cancelled, the query is recorded as failed with `errorCode: cancelled`, and no result is returned.
- **Error mapping.** Every failure maps to `{code, message, action}` (`Connections/SqlConnections.cs` `SqlFailures`): `login.failed`, `server.unreachable`, `database.unavailable`, `permission.denied`, `budget.timeout`, `cancelled`, `sql.error` (for 207/208/4104 it advises `schema_inspect`), `sql.rejected`, `policy.unavailable`, `alias.unknown`, `environment.unapproved`, `principal.unsafe`, `credential.unavailable`. Messages are redacted and never include a connection string.
- **Schema reads that fail** are reported as failures. A stored snapshot is never returned as if it were current.

## The JSON stores' claims (`atomic-json-files`)

| Claim | ConnectionCatalog | InvestigationHistory | KnowledgeStore |
|---|---|---|---|
| retention-honored | metadata only, no secrets | no result rows; entries older than `HISTORY_RETENTION_DAYS` (90) pruned on write | metadata only, no row values |
| erasure-capable | `connection_remove`, console `remove` | console `erase-investigation --id` (QueryGateway) | console `erase-relationship --id`, `erase-snapshots --alias` (KnowledgeService) |
| access-controlled | protected DACL, current user only (folder and every file, including the vault) | same | same |
| auditable | `audit` array in the document, written by its owning service, capped at `MAX_AUDIT_EVENTS` | same | same |

A store that cannot be read fails closed (`store.unreadable`; for the catalog, `policy.unavailable`). It is never replaced by an empty document.

## Result fields beyond the declared records

The model's TypeDefinitions are the minimum field set (handoff §3). The tools add fields, and remove none:

- `connection_list`: `authentication`, `environment`, `scope`.
- `sql_execute`: `investigationId`, `truncatedBy`, `rowLimit`, `byteLimit`.
- `schema_inspect`: `status` (`captured` | `current` | `refreshed` | `stale-refreshed` | `drifted`), `drift`, `verifiedAt`, and in the snapshot `detail`, `missing`, `fingerprint` and `covers`.
- `investigation_get`: `queries` (provenance of the cited and owned queries).
- Relationships: `proposedAt`, `review`.

## Open gaps

- [`continuum/gaps/2026-11-06-remove-connection-secret.md`](../continuum/gaps/2026-11-06-remove-connection-secret.md): what removing an alias over MCP does to its secret. The implementation leaves the DPAPI blob for the console's `remove`, and awaits the owner's disposition.
