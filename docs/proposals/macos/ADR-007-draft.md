# ADR-007 (draft): Support macOS; protect credentials with the operating system's per-user secret store

Status: **Draft, not proposed.** It is not in the Host decision space yet. When proposed it supersedes ADR-004's credential clause only; ADR-004's JSON-store decision stays as it is.

## Context

SQL Investigator is a Windows tool: VISION.md says "local Windows developer tool", ADR-004 says "encrypt credentials separately with Windows DPAPI CurrentUser", and the bindings fix `os: windows-x64` and `windows-dpapi-currentuser`. Developers on macOS cannot use it. Everything except the credential protection and the owner-only file permissions is already cross-platform: the MCP stdio server, the ScriptDom allowlist, budgets, Microsoft.Data.SqlClient and the atomic JSON stores.

## Decision

1. Support macOS (Apple silicon and Intel) alongside Windows x64, as one product with one architecture and one acceptance suite.
2. Protect each alias's SQL credential with the operating system's per-user secret store:
   - **Windows:** DPAPI CurrentUser with per-alias entropy (unchanged).
   - **macOS:** a generic-password item in the user's login Keychain, one per alias, created by the local provisioning console only.
3. Restrict the data folder and its files to the current user on both systems: NTFS protected DACLs on Windows, owner-only `0700` folders and `0600` files on macOS.
4. Everything else in ADR-004 stands: System.Text.Json with atomic writes and single-writer discipline, and no SQLite.

## Alternatives

- **A portable encrypted file with a key derived from a passphrase.** Rejected: the developer would have to type a passphrase at every server start, and Claude Code starts the server unattended. A passphrase kept in an environment variable is plaintext with extra steps.
- **A cross-platform secret library** (for example a wrapper over the Keychain, DPAPI and libsecret). Rejected for now: it adds a dependency for two well-understood OS APIs, and Linux is out of scope.
- **Keep Windows only.** Rejected if macOS developers are to use the tool. It needs no change otherwise.

## Consequences

- **What does not move.** `CredentialVault` stays a secret-store that no service writes. Secrets still enter only at the local console, never through MCP or chat. `ReadSecret` is still the only service-side access. No ADL element, edge, owner or constraint changes; four model descriptions stop naming Windows and DPAPI (`macos-support.adl-change`).
- **Bindings.** A new technology, `os-user-secret-store`, replaces `windows-dpapi-currentuser` for `CredentialVault`. `LocalMCPServer` is bound for `windows-x64 osx-arm64 osx-x64`.
- **Acceptance.** B01's step `credential.dpapi-current-user` becomes `credential.user-protected`, checked per OS by the adapter.
- **Verification.** Every scenario and the safety challenge must pass on a real macOS machine before the transition is accepted. macOS CI needs a self-hosted Mac or GitHub macOS minutes, which count 10× against the plan's quota.
- **The Keychain trust boundary differs from DPAPI.** Another process running as the same user can ask the Keychain for an item, and macOS may prompt the user to allow it. DPAPI has no prompt. The item is created with access limited to the SqlInvestigator executable where the API allows it. Its access group and labels must not leak the server name or password.

## Expectations (for `decision propose`, written root-anchored)

- `::SQLInvestigator.Investigation.QueryExecution.CredentialVault` is a secret-store with no service `writes` edge.
- `::SQLInvestigator.Investigation.QueryExecution.ConnectionRegistry` `reads` `::SQLInvestigator.Investigation.QueryExecution.CredentialVault`.
- `::SQLInvestigator.LocalMCPServer` exists, and the three services are `deployedAs` it.
