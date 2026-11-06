# ADR-007 (draft): Support macOS; protect credentials with the operating system's per-user secret store

Status: **Draft, ready to propose.** When accepted it supersedes ADR-004. It restates ADR-004's JSON-store decision unchanged and replaces only its credential clause.

## Context

SQL Investigator is a Windows tool:
- VISION.md says "local Windows developer tool".
- ADR-004 says "protect credentials separately with Windows DPAPI CurrentUser".
- The bindings fix `os: windows-x64` and `windows-dpapi-currentuser`.

Developers on macOS cannot use it. Everything except credential protection, owner-only file permissions and the default data path is already cross-platform: the MCP stdio server, the ScriptDom allowlist, budgets, Microsoft.Data.SqlClient and the atomic JSON stores.

## Decision

1. **Platforms.** Support Windows x64 (`win-x64`), macOS on Apple silicon (`osx-arm64`) and macOS on Intel (`osx-x64`), as one product with one architecture and one acceptance suite. The supported minimum is the oldest macOS that .NET 10 supports: macOS 15.
2. **Stores (from ADR-004, unchanged).** Connection metadata, knowledge and history are System.Text.Json files with atomic replacement, each with exactly one writing service. No SQLite.
3. **Credentials.** Each alias's SQL credential is protected by the operating system's per-user secret store, never in plaintext:
   - **Windows:** DPAPI CurrentUser with per-alias entropy (unchanged; existing installations keep working).
   - **macOS:** one generic-password item per alias in the user's login (file-based) Keychain. It is created, replaced and deleted only by the local provisioning console. The item's access list trusts only the SqlInvestigator executable that created it. Any other application, including the `security` tool, must be approved by the user in a Keychain prompt. This is application-scoped access, stricter than DPAPI, which any process of the same user can call.
4. **Owner-only data folders.** NTFS protected DACLs on Windows. On macOS, owner-only `0700` folders and `0600` files under `~/Library/Application Support/SqlInvestigator`.
5. **No silent downgrade.** If the Keychain is locked, refuses access, or would need a prompt, the MCP server fails closed with `credential.unavailable` and tells the developer to re-provision at the console. It never blocks waiting for a prompt, and it never falls back to a weaker store.

## Alternatives

- **Data-protection keychain** (`kSecUseDataProtectionKeychain`) with an access group. Rejected for now: it needs a Developer ID Team ID, a provisioning profile and an app-like bundle, which an unsigned development build cannot have (Apple TN3137). It is the natural upgrade once production signing exists.
- **A portable encrypted file with a key derived from a passphrase.** Rejected: Claude Code starts the server unattended, so the passphrase would have to live in an environment variable, which is plaintext with extra steps.
- **A cross-platform secret library.** Rejected: it adds a dependency for two well-understood OS APIs, and Linux is out of scope.
- **Keep Windows only.** Rejected: macOS developers could not use the tool.

## Consequences

- **What does not move.** `CredentialVault` stays a secret-store that no service writes. Secrets still enter only at the local console, never through MCP or chat, and services still only read. No ADL element, edge, owner or constraint changes; four descriptions stop naming Windows and DPAPI (option `macos`, `macos-support.adl-change`).
- **Bindings.** A new technology, `os-user-secret-store`, replaces `windows-dpapi-currentuser` for `CredentialVault`. `LocalMCPServer` is bound for `windows-x64 osx-arm64 osx-x64`. Acceptance step `credential.dpapi-current-user` becomes `credential.user-protected`, checked per OS.
- **Development builds re-prompt after an update.** An unsigned or ad-hoc-signed binary is identified by its exact code hash, so a new build is a different application to the Keychain. After an upgrade the developer approves access once in a prompt, or re-provisions. The headless server never waits for that prompt.
- **Production distribution requires Developer ID signing and notarization:**
  - Apple Developer Program membership;
  - a Developer ID Application certificate;
  - Hardened Runtime with `com.apple.security.cs.allow-jit`;
  - `notarytool`.

  Until then, macOS builds are development builds and are not described as production-ready.
- **Verification.** Every scenario and the safety challenge must pass on macOS before the transition is accepted. GitHub macOS runners cannot run SQL Server, so SQL Server connectivity on macOS needs a server reachable from the runner.
- **Cross-process locking.** Store locks are machine-wide named mutexes (`Global\`), because on macOS each shell is its own session.

## Expectations (root-anchored)

- `not ::SQLInvestigator.Investigation.QueryExecution.QueryGateway writes ::SQLInvestigator.Investigation.QueryExecution.CredentialVault`
- `not ::SQLInvestigator.Investigation.QueryExecution.KnowledgeService writes ::SQLInvestigator.Investigation.QueryExecution.CredentialVault`
- `not ::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry writes ::SQLInvestigator.Investigation.QueryExecution.CredentialVault`
- `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry reads ::SQLInvestigator.Investigation.QueryExecution.CredentialVault`
- `::SQLInvestigator.Investigation.QueryExecution.QueryGateway writes ::SQLInvestigator.Investigation.QueryExecution.InvestigationHistory`
- `::SQLInvestigator.Investigation.QueryExecution.KnowledgeService writes ::SQLInvestigator.Investigation.QueryExecution.KnowledgeStore`
- `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry writes ::SQLInvestigator.Investigation.QueryExecution.ConnectionCatalog`
