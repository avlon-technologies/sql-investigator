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
| ADR-001 | Claude Code as sole agent host | accepted |
| ADR-002 | SQL Server and T-SQL only | accepted |
| ADR-003 | Bounded autonomous read-only investigation | accepted |
| ADR-004 | Local JSON and DPAPI, not SQLite | accepted |
| ADR-005 | Separate queries and agent-side correlation | accepted |
| ADR-006 | Continuum at design time, optional knowledge at runtime | accepted |

All six were accepted by the Host under the delegated `agent:remote-mcp` identity (acceptance events ADR-001 #15, ADR-002 #16, ADR-003 #20, ADR-004 #17, ADR-005 #18, ADR-006 #19). Always read the live Host status rather than this table:

```
continuum decision list
continuum decision show ADR-003 --verbose
continuum decision integrity            # every line intact, chained and Host-signed
continuum decision verify               # the Host confirms the export matches its record
```

## Authority policy

- **Owner:** Michael Kang (Entra object id `2605c7a8-5120-4edb-98cc-76d563601c0f`), with every action on every decision.
- **`agent:claude-code`:** may propose, amend, check and record evidence, only through a short-lived delegation the owner mints. It may never accept, reject, revoke or supersede.
- **Separation of duties:** accept, revoke and supersede must be performed by a human who is not the author.

The six proposals were made by `agent:claude-code` under a delegation. The Host subsequently authorized `agent:remote-mcp` to accept them through the `chatgpt-delegate` grant while enforcing its non-author-approver rule.

## Known issue: alignment checks report "missing"

Each proposal carries expectations (for example `QueryGateway constrainedBy SelectOnlyAllowlist`) that the Host checks against the compiled model. The expectations were written without the leading `::` of a root-anchored name. The checker resolves only root-anchored names (`::SQLInvestigator.…`), so the recorded checks (events 8–13) report every expectation as "missing", even though every element and relationship exists in the model. A local test confirmed that the same expectations hold once written with `::`.

This is a tooling gap: `decision propose` accepts names it cannot resolve. Expectations cannot be changed with `amend`. At acceptance time the owner chose to accept the proposals as written. The alternatives considered before acceptance were:

1. **Accepted as proposed**; interpret prior alignment checks against the canonical model rather than treating the unanchored expectation results as proof of missing model elements.
2. **Not pursued:** reject and re-propose with root-anchored expectations under new decision IDs.

## Owner commands

```
continuum decision show ADR-001 --verbose
continuum decision list                              # accepted in Host; confirm live status
continuum decision verify                            # after refreshing the signed export
continuum decision integrity
```

## Local signed-export synchronization

The Host is authoritative. The checked-in `decisions.jsonl` export must be refreshed with `continuum decision` before offline verification; do not manually edit signed lines.
