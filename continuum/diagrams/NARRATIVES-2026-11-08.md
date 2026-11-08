# SQL Investigator — narrative-driven Continuum diagrams (2026-11-08)

This suite explains the *meaning* of SQL Investigator, rather than mechanically repeating C1/C2/C3. Each question was submitted to Continuum Diagram against the **compiled ADL** on 2026-11-08. The visuals must be interpreted as model-grounded architecture, **not proof of implementation**.

## Provenance and state

- Source: `continuum/model/architecture.adl-manifest.json` and `architecture.adl` from `main` at diagram-generation time.
- Imported model: `snp_4e7bbb575c7531ce071a5367ea533a24` (temporary server snapshot; re-import the source for future renders).
- Model attestation: `sha256:28a88f436b9ce5349783975e4a495199b25cebcd317d764ba6ce299d808c4005`
- Source package digest: `sha256:0474aa907194f947c8027e51053b028a6c195b34226524cd24b74a6d942619df`
- Model compiler: Continuum 0.19.0; core `adl/1.6`.
- The current `VISION.md` **does not fully match** this model: it requires in-conversation guided onboarding and OS-supported secrets on Windows and macOS; ADL still states console-only provisioning and describes the console in ConnectionRegistry. Do not present the future conversational workflow as declared or implemented.
- The model does not contain a formal investigation workflow. Any apparent sequence below is **illustrative editorial ordering**, not a model-proven transition graph.
- Visual convention: declared (solid), inferred (dashed), speculative/future (dotted). Do not turn an absence of a declared edge into a claim about network firewall enforcement.

## Narrative 1 — Who talks to whom, and where is trust placed?

**Audience:** new developers, architecture reviewers.

**Story:** A developer asks Claude Code a question. Claude Code calls the local .NET MCP process over stdio; SQL Investigator performs the authorized database accesses. The user owns the approval and review decisions. Password handling must be kept outside chat and model-visible tool outputs. The *present* ADL specifies a separate local provisioning console; the *Vision* requires guided conversational onboarding using safe credential-entry mechanisms.

**Question sent to Diagram:** Show the system context as a visually engaging, legible explanatory architecture diagram: developer converses with Claude Code, Claude Code uses SQL Investigator's local .NET MCP server, and SQL Investigator alone accesses independently authorized SQL Server databases. Distinguish human, cloud agent, local process and database trust boundaries. Include no fictional direct Claude-to-SQL edge. Highlight that a console-only provisioning journey remains declared in this ADL, whereas the current Vision asks for conversational onboarding; never portray the desired path as implemented.

**Selection result:** run `d045c994-b970-4729-bd67-dbd267c40891`; 9 declared, 3 inferred, 1 speculative; visually check the dotted future-state contrast before acceptance.

**Key question for reviewers:** Does the picture clearly communicate that the intended onboarding experience is *not yet represented* by the model?

## Narrative 2 — Why is order 42 delayed?

**Audience:** developers using the product; technical stakeholders.

**Story:** Orders in illustrative database `alpha` and shipments in illustrative `beta` each require a separately approved query. The gateway validates and bounds each SELECT independently. Claude Code correlates the bounded evidence *outside* SQL Server, marks missing or ambiguous matches, and preserves query provenance so conclusions can be reopened. Nothing here asserts that order 42 really is delayed or supplies real result rows.

**Question sent to Diagram:** Show how one investigation answers 'Why is order 42 delayed?' with separate bounded read-only queries against independently authorized alpha Orders and beta Shipments, agent-side correlation, and durable evidence/provenance. Explain the security boundary that forbids cross-database SQL; use narrative, left-to-right storytelling and explicitly label editorial ordering as illustrative rather than a declared workflow.

**Selection result:** run `51455e6b-ea57-4c8a-8419-7d5001e4ad04`; 8 declared, 5 inferred. `alpha`, `beta`, Orders and Shipments are acceptance-story examples, not declared ADL entities. Consult the BDL and live schema before making a business claim.

**Key question for reviewers:** Can a reader tell that correlation is performed by the agent, not via a cross-database SQL join?

## Narrative 3 — What stops a dangerous query?

**Audience:** security reviewers, implementers, maintainers.

**Story:** Claude can accidentally propose an UPDATE or an enormously expensive SELECT. The gateway enforces approved environments and object/column scope, parses SQL against a fail-closed SELECT-only allowlist, applies budgets, and audits rejections. Independently, the SQL Server credential must lack write privileges. Secret redaction and evidence provenance frame the response. The order of these checks is not expressed as a formal ADL workflow; the architecture specifies responsibilities and guarantees.

**Question sent to Diagram:** Show the layered defense for a potentially dangerous or overbroad query from Claude Code: QueryGateway enforces approved environment and scope, fail-closed SELECT-only validation, query budgets and audit, SQL Server least-privilege read-only account independently prevents writes, sanitization and provenance protect the answer. Show no direct Claude-to-SQL access. Distinguish declared enforcement from implied sequencing.

**Selection result:** run `0fe991a6-4d17-40c1-ac37-33e66dcbadde`; 7 declared, 0 inferred. This is model-fidelity evidence, not proof of runtime enforcement.

**Key question for reviewers:** Do the independent gateway and database privilege controls remain distinct?

## Additional narratives worth rendering

### 4. One question, no terminal detour — desired user journey

A developer says "Connect to this database" and then "How many requests are there?" The system should guide secure credentials, discover scope, confirm consequential permissions, query immediately, show executed SQL and provenance, and allow a follow-up without restart. **Status: Vision-required, not presently grounded by the old console-only ADL.** This is a *target state*, not an as-built diagram. Revise the model by governed architecture decision before claiming semantic conformity.

### 5. A hunch is not accepted knowledge

An agent notices matching identifiers in separate result sets and proposes a cross-database relationship with evidence. KnowledgeService retains proposal and provenance; a developer explicitly accepts or rejects the relationship. Rejected evidence stays visible but does not become trusted knowledge. Model-grounded commands: ProposeRelationship, ReviewRelationship, SearchKnowledge; store: KnowledgeStore.

### 6. The first query fails — recovery without secret leakage

Show a disconnected alias, stale schema, cancelled query or wrong credential resulting in a sanitized, actionable error, no fabricated rows, and an opportunity to retry from Claude Code. **Caveat:** the new Vision mandates conversational repair and minimal manual steps; do not imply the current ADL establishes every recovery interaction.

### 7. What persists after the chat ends?

A subsequent investigation reopens exact executed SQL, aliases, timestamps, findings and uncertainty from InvestigationHistory, while raw sensitive result rows are not retained by default. Distinguish evidence from proposed, reviewed relationship knowledge in KnowledgeStore and nonsecret ConnectionCatalog metadata from CredentialVault secrets. Avoid depicting a single merged store.

### 8. Windows and macOS: identical user experience, OS-specific secret custody

Compare the same conversational investigation on Windows and macOS, with DPAPI CurrentUser versus macOS login keychain. This is a **Vision/ADR technology-binding** narrative, not a guarantee derived from ADL alone. Check platform implementation and bindings before illustrating mechanisms.

## Regeneration protocol

1. Import the repo's current ADL manifest **and every named source document** through `diagram_model_import`. Record the returned attestation and package digest; never assume a temporary snapshot remains available.
2. For each question, call `diagram_request` with the imported `snapshotId`, the full narrative question and the intended audience.
3. Review `selection-v1/brief.md` and evidence. Confirm absence of invented entities/edges; declared/inferred/speculative distinctions must be legible.
4. Render with `diagram_render`; inspect the PNG and mechanical check outputs, correct visual defects before publishing. Preserve editable SVG, preview PNG, run id, and checking notes together.
5. If the current Vision and model disagree, keep their boundary visible and update the model through an authorized architecture change before depicting the target experience as declared.
