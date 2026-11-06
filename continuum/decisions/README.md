# SQL Investigator decisions

The six ADRs agreed in the design conversation live in a **Host-governed Continuum decision space** in this folder. The Host authorizes every act from the credential presented, signs every line, and keeps the record. The Markdown files are readable copies; the record is authoritative.

| File | What it is |
|---|---|
| `decision-authority.json` | the authority policy the space was created with (sent once; changing it now is a govern act) |
| `decision-space.json` | the pin: which Host space this folder is |
| `decisions.jsonl` | the signed export of the record, refreshed by every command and verifiable offline |
| `ADR-001.md` … `ADR-006.md` | readable copies of each decision's text and current status |

The space is registered as the default in `../../continuum.json`.

## Current state

| ID | Decision | Status |
|---|---|---|
| ADR-001 | Claude Code as sole agent host | proposed |
| ADR-002 | SQL Server and T-SQL only | proposed |
| ADR-003 | Bounded autonomous read-only investigation | proposed |
| ADR-004 | Local JSON and DPAPI, not SQLite | proposed |
| ADR-005 | Separate queries and agent-side correlation | proposed |
| ADR-006 | Continuum at design time, optional knowledge at runtime | proposed |

None is accepted. Always read the live status rather than this table:

```
continuum decision list
continuum decision show ADR-003 --verbose
continuum decision integrity            # every line intact, chained and Host-signed
continuum decision verify               # the Host confirms the export matches its record
```

## Authority policy

The policy in force is the latest `governed` event in the Host record, not `decision-authority.json`. The file mirrors it. To change the policy, edit the file and run `continuum decision govern --policy continuum/decisions/decision-authority.json --reason "<why>"` (CLI 0.28.8 or later), signed in as the owner. Editing the file alone changes nothing.

In force since event 14 (2026-11-06, policy `sha256:a97d5f9fbc5f9c44e8b7daa446833040bdad848a9cd2a58aebe464aa60f361eb`; the genesis policy was `sha256:8de00d45…`):

- **Owner:** Michael Kang (Entra object id `2605c7a8-5120-4edb-98cc-76d563601c0f`), with every action on every decision.
- **`agent:claude-code`:** may propose, amend, check and record evidence, only through a short-lived delegation the owner mints. It may never accept, reject, revoke or supersede.
- **`agent:remote-mcp`** (ChatGPT and other Remote MCP connectors, recognized by the Host from the relay's client): may propose, check, record evidence, accept and reject decisions scoped to `SQLInvestigator`, for the owner, **until 2026-11-12T23:59:59Z**. After that the grant is dead; withdraw it sooner with another `govern`.
- **Separation of duties:** accept, revoke and supersede are performed by a person or a delegated workload that did **not** propose or amend the decision (`distinct-party`). A workload approves only where its own grant includes the action.

The six proposals were made by `agent:claude-code` under a delegation, so the owner or `agent:remote-mcp` may accept them, and `agent:claude-code` may not. None has been accepted yet.

## Known issue: alignment checks report "missing"

Each proposal carries expectations (for example `QueryGateway constrainedBy SelectOnlyAllowlist`) that the Host checks against the compiled model. The expectations were written without the leading `::` of a root-anchored name. The checker resolves only root-anchored names (`::SQLInvestigator.…`), so the recorded checks (events 8–13) report every expectation as "missing", even though every element and relationship exists in the model. A local test confirmed that the same expectations hold once written with `::`.

This is a tooling gap: `decision propose` accepts names it cannot resolve. Expectations cannot be changed with `amend`. The owner's options:

1. **Accept as proposed**, and read alignment from the model rather than from these checks.
2. **Reject and re-propose**: the owner rejects ADR-001…006 with that reason, and the agent re-proposes them with root-anchored expectations, which become new IDs (ADR-007…012).

## Owner commands

```
continuum decision show ADR-001 --verbose
continuum decision accept ADR-001 --reason "<why>"      # repeat for ADR-002 … ADR-006
continuum decision integrity
```
