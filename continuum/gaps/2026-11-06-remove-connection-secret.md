# Gap: what happens to an alias's secret when the alias is removed over MCP

**Date:** 2026-11-06 · **Raised by:** implementation (Claude Code) · **Status:** open, for the owner · **Blocks:** nothing

## The fact the model does not settle

`RemoveConnection` (MCP `connection_remove`) is handled by `ConnectionRegistry`, which **reads** `CredentialVault` and writes only `ConnectionCatalog`. ADL forbids a service `writes` edge to a `secret-store` (`adl/architectural/secret-store-write`), and the handoff says secrets reach the vault only through the local console. The model therefore says nothing about **deleting** a secret. It does not say whether removing an alias over MCP should leave its DPAPI blob in place, or who may delete that blob.

## Evidence

- `continuum/model/architecture.adl`: `ConnectionRegistry` `reads CredentialVault`, `writes ConnectionCatalog`, `on RemoveConnection -> none`.
- `continuum/IMPLEMENTATION-HANDOFF.md` §2: "The credential vault has no writing service."
- Scenario B01 and B09 check storage and output, not removal.

## What the implementation does meanwhile

- **MCP `connection_remove`** removes the alias from the catalog only. No tool can reach the alias again, because every request resolves the alias through the catalog first. The DPAPI blob stays in `vault\<alias>.dpapi`: still encrypted for the current Windows user, still bound to its alias by entropy, and in a current-user-only folder.
- **Console `SqlInvestigator remove --alias <name>`** (the developer, locally) removes the catalog entry and deletes the blob. Re-provisioning the alias overwrites the blob.
- `connection_remove`'s tool description says the secret file is deleted by the console's `remove` command.

No service writes the vault, so the declared structure is unchanged.

## Smallest change that would close it

Either:

1. **No model change; record a disposition.** The console is the only place a secret is created or destroyed, and an orphaned blob is acceptable. This is what the implementation does now.
2. **State it in the model.** Add to `RemoveConnection`'s description (or to the `CredentialVault` resource) that the secret is deleted only by the local console's `remove`, not by `connection_remove`. That is a descriptive `.adl-change` pinned to changeState `sha256:def71c2de556c87d6fcde76ced19b541be633f83f7a19d97098a90742d156a3a`.

Recommendation: option 1, unless the data owner wants removal over MCP to destroy the secret. That would need an architecture decision, because it is a write to a secret-store.
