# CLAUDE.md

SQL Investigator: a local Windows .NET 10 MCP stdio server that lets Claude Code investigate SQL Server databases with bounded, read-only, auditable queries. Read `README.md` first, then `continuum/IMPLEMENTATION.md` and `continuum/IMPLEMENTATION-HANDOFF.md`.

## Authority order

1. `continuum/model/architecture.adl` (semantic ADL) governs.
2. `continuum/acceptance/scenarios/` define what must be true.
3. `continuum/model/local-development.adl-binding.json` is the only place technology choices live.
4. `VISION.md`, `continuum/GOAL.md` and derived docs explain intent; they never override the model.

## Rules

- Before building, changing or refactoring anything, use the `implement-against-architecture` skill to decide whether the request needs a model change. If the model is silent on something the code must decide, write `continuum/gaps/<YYYY-MM-DD>-<slug>.md` and stop for the owner before changing the model.
- Never hand-edit derived files: `continuum/IMPLEMENTATION.md`, `BEHAVIOUR.md`, `PACKAGE.json`, `continuum/README.md`, `continuum/acceptance/README.md`, `continuum/model/architecture.adl.txt`, `continuum/model/compile-proof.json`. Regenerate with `continuum package build continuum`.
- After a model edit: `continuum package bind refresh continuum`, then `continuum package build continuum`, then `continuum package check continuum`.
- Mint element and binding ids with the Continuum tools; never invent them.
- Acceptance: `continuum package run continuum`. Bind steps in `continuum/acceptance/adapter.mjs` against the real MCP process, not an in-memory fake.
- Each ordinary data store has exactly one writing service (`InvestigationHistory` ← QueryGateway, `KnowledgeStore` ← KnowledgeService, `ConnectionCatalog` ← ConnectionRegistry). `CredentialVault` is a secret-store that no service writes: secrets reach it only through the local interactive provision command. Keep it that way.

## Safety boundaries (non-negotiable)

- stdout is reserved for MCP protocol messages; diagnostics go to stderr, structured and redacted.
- SQL validation is a fail-closed ScriptDom allowlist of one bounded SELECT. Reject anything unrecognised. A read-only SQL principal is the final write barrier; test both independently.
- Credentials are provisioned through a local interactive command only, never via MCP tool arguments or chat. Encrypt with DPAPI CurrentUser; restrict files to the current user.
- Never connect to production data, and never use production data in tests. Fixtures are disposable SQL Server databases.
- No web UI, embedded LLM, provider abstraction, SQLite, linked servers, or runtime dependency on Continuum.
