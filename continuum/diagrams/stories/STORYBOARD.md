# SQL Investigator in stories

The earlier diagrams in this folder are inventories: they list what exists. This folder tells what *happens*, and why the design is shaped the way it is.

Each **story** is one diagram with one tension and one resolution. Stories are composed into two **narratives**. Every story was handed to the Continuum Diagram composer as its question, against the pinned current model, so each picture is still graded against the ADL: solid lines are declared, dashed lines are inferred, and the companion brief for each run says which is which.

The running example comes from the acceptance scenarios: **"Why is order 42 delayed?"** Orders live in database `alpha` (`dbo.Orders`), shipments in database `beta` (`dbo.Shipments`).

---

## Prologue — Zooming in (C1 → C2 → C3)

The old `atlas/` C1, C2 and C3 views look nearly identical. They are deterministic projections, and this model has one system, one deployable and one bounded context, so each "zoom" only adds a single nested box. Zooming in should reveal something new at each level, so each level answers its own question:

1. **Who is in the room, and who is trusted with what?** (C1) SQL Investigator is one sealed box. The point is that the three relationships differ: Claude Code asks and gets bounded evidence; the developer talks to Claude and *separately* owns setup and review locally; only SQL Investigator touches the databases, read-only, one alias at a time.
2. **What runs on the developer's machine, and what does it keep?** (C2) One executable with two doors: MCP over stdio for Claude Code, and the local provisioning console for the developer. Four stores sit beside it, and the secret store is visibly set apart from the other three.
3. **Inside the executable: three keepers, one ledger each.** (C3) ConnectionRegistry, QueryGateway and KnowledgeService each solely write one store. All three read the vault, and none writes it. Each guardrail is attached to the keeper it binds.

---

## Narrative 1 — One afternoon: "Why is order 42 delayed?"

A developer wants an answer they can defend. Claude Code does the legwork. The five stories follow the investigation from the first setup step to the reviewed conclusion.

### 1. The password that never enters the conversation
*Scenarios B01, B12. Requirements LocalOnlyProvisioning, NoSecretDisclosure.*

- **Tension:** Claude Code needs to reach two databases, but Claude Code runs in the cloud, and it must never hold the key.
- **Beats:** The developer runs the provisioning command at a local console and types the password there. The developer's console seals the secret into the DPAPI-protected CredentialVault (no service ever writes the vault); ConnectionRegistry writes only the alias and its non-secret metadata into the ConnectionCatalog. When Claude asks `connection_list`, it gets back `alpha` and `beta`: names, not keys.
- **Resolution:** The key travels a separate road from the conversation. Services read it from the vault at the last moment, inside the local machine.
- **Takeaway:** Claude knows *which* databases exist, never *how to get in*.

### 2. Look before you query
*Scenario B10. Requirement SchemaFreshness.*

- **Tension:** Claude writes SQL from what it believes the schema is. A stale belief produces wrong queries and wrong conclusions.
- **Beats:** Claude calls `schema_inspect` on `alpha`. KnowledgeService reads the database catalog and stores a snapshot stamped with its source and capture time. Later a column is renamed; the next inspection notices the drift and refreshes.
- **Resolution:** Every schema fact says where it came from and when.
- **Takeaway:** Claude's map of the database carries a date.

### 3. Two databases, never joined
*Scenario B06. Requirement SeparatePerDatabaseQueries.* **The climax.**

- **Tension:** The answer needs orders from `alpha` and shipments from `beta`. The obvious SQL is a cross-database join, and that is exactly what the system forbids.
- **Beats:** Claude sends two separate `sql_execute` calls, each naming one alias. Each passes through the QueryGateway's allowlist and budgets and runs as that alias's read-only principal. Two bounded result sets come back. Claude lines them up on `OrderId` itself, and labels what does not match and what is ambiguous.
- **Resolution:** Correlation happens in the agent, over evidence, not in the database, over privileges.
- **Takeaway:** No single query ever spans two databases, so no single query can leak across them.

### 4. The paper trail
*Scenario B07, B02. Requirement EvidenceProvenance.*

- **Tension:** An answer from an AI is only as good as your ability to check it next week.
- **Beats:** Every executed query is recorded with its query id, exact SQL, alias and timestamps. Claude records its hypothesis ("shipment missing"), its finding and its uncertainty ("schema may be stale"). Raw rows are not kept. Next week, `investigation_get` brings the whole reasoning back.
- **Resolution:** The investigation is a reopenable record, not a chat scroll.
- **Takeaway:** Every claim points to the query that supports it.

### 5. A hunch is not a fact
*Scenario B08. Requirement ReviewedRelationships.*

- **Tension:** While correlating, Claude noticed that `alpha.Orders.OrderId` seems to match `beta.Shipments.OrderId`. If that guess silently became "knowledge", every later investigation would inherit it.
- **Beats:** Claude proposes the relationship with its evidence. It enters the KnowledgeStore as *proposed*. Only the developer can accept or reject it. A rejected relationship keeps its evidence and is never promoted.
- **Resolution:** The human decides what the system believes.
- **Takeaway:** Inference goes in as a question, and only a person turns it into a fact.

---

## Narrative 2 — What if Claude gets it wrong?

Claude is capable and well-meaning, and it will still sometimes write the wrong thing. Database content is also untrusted input that can steer it. This narrative is about the walls that hold regardless of what Claude intends.

### 6. Two walls against a write
*Scenarios B03, B04. Requirement ReadOnly; guardrail SelectOnlyAllowlist.*

- **Tension:** Claude sends `UPDATE dbo.Orders SET ...`. Maybe it misunderstood; maybe a value in the data told it to.
- **Beats:** Wall one: the QueryGateway parses the statement against a fail-closed allowlist that accepts exactly one bounded SELECT within the alias's approved scope. The UPDATE never reaches the database; the rejection is audited. Wall two: suppose wall one had a bug. The database login itself is a read-only principal, and the write would be denied there. The two walls are tested separately.
- **Resolution:** Safety does not depend on any one layer being perfect.
- **Takeaway:** To change data, an attacker would have to break two independent walls at once.

### 7. The query that wanted everything
*Scenario B05. Requirement BoundedExecution; guardrail QueryBudgets.*

- **Tension:** A valid SELECT can still scan a huge table, hold locks, or return more than anyone should send to a cloud model.
- **Beats:** Every query runs inside timeout, row, byte, concurrency and per-investigation budgets. When one is exceeded, the query is cancelled or truncated, and the result says so explicitly.
- **Resolution:** Read-only is not the same as harmless; budgets cover the gap.
- **Takeaway:** Claude always knows when it is looking at a partial answer.

### 8. When things break, nothing leaks
*Scenarios B09, B11. Requirements SafeFailure, NoSecretDisclosure; guardrail SecretRedaction.*

- **Tension:** Wrong password, server down, query cancelled. Error messages are where secrets and made-up results usually escape.
- **Beats:** Each failure comes back as an actionable, sanitized error. No password appears in the error, the log, the history or the knowledge store. No result is invented to fill the gap.
- **Resolution:** Failure is honest and quiet.
- **Takeaway:** A broken connection tells Claude what to fix, never what the password was.

---

## Rendered stories

Not yet rendered. Each scene has been sent to the Continuum Diagram composer against snapshot `snp_a5ef35dbb3cf9bf0fb0895c4babb5a65` (attestation `sha256:df885cef…3d77`). The rendered pictures, their run ids, and what in each is declared or inferred will be added here when the renders complete.
