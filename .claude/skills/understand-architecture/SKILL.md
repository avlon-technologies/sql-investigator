---
name: understand-architecture
description: Read, reason about, review and explain a Continuum ADL model - what the architecture is, how something works, who owns or handles what, what follows a message, what breaks if X is removed, what changed, whether a relationship is legal, or a C1/C2/C3-style view of it. Use when handed .adl / .adl.json / .adl.txt artifacts or an ADL package and asked to explain, answer over, review, critique, or visualize the architecture.
---

# Understand an architecture (read · answer · review · view)

**Load the `continuum` skill first** — it carries the task routing,
knowledge routing, tool routing and evidence discipline this workflow
assumes, and it is where the trust boundary and the declared / derived /
recommended / unknown rule live. This file adds only what is specific to
reading a model.

The reading rules themselves are `continuum://guides/reading-a-model`
(`docs/guides/READING-A-MODEL.md`): **read it before reasoning over any
model** — §3 how to answer, §4 what you must never infer, §5 the
asynchronous constructs. This skill enforces that guide; it does not
restate it.

## 1. Ground truth, before any answer

1. `continuum_find` when you do not know where the model is — or a glob for
   `continuum/model/*.adl-manifest.json` where it is not served (`continuum`
   skill §0). No model ⇒ do not answer architecture questions from code as
   if one existed: say the repository has no Continuum model and offer
   `recover-architecture`.
2. `continuum_compile` (MCP) or `continuum compile <manifest> --state`. Errors
   stop the reading — report them and offer to repair or rebuild; never summarize
   from a broken model, and never fall back to its `.adl.txt` (the
   projection is derived from the same broken state). Resolve every
   diagnostic id with `continuum_diagnostics`.
3. Note the **attestation** (and `changeState` when versions will be
   compared). Every answer below is pinned to it.
4. **If you are reading a committed `continuum/model/*.adl.txt` rather than
   generating one, check it is still the model's.** The projection header
   carries a `source sha256:…` line — the canonical-model attestation it
   was produced from. If that does not equal the attestation you just
   compiled, the projection describes a model that no longer exists:
   regenerate it, say that you did, and never answer from the stale one.

A model that is **valid and incomplete** is normal: warnings are
first-class context, not noise.

## 2. Read the smallest sufficient thing

| The question is | Read |
|---|---|
| about one element, one edge, one context | `continuum_query` — the slice, not the model |
| "what is this system" | `continuum_text_projection` — top levels first: system → domains → contexts, then descend only where the question goes |
| about a construct the projection drops (ids, authored order) | the authored `.adl` / `.adl.json` |
| the model on the other authored surface (a JSON model to read as text, or the reverse) | `continuum_emit` — both surfaces denote the same model at the same attestation |

`continuum_query` selects by `id`, `name`, `nameLike`, `kind`, `within` +
`depth`, or `relation` with `direction: out | in | both`, and `include`s
relationships / attributes / descriptions / children. Typical slices:

```
who owns capability X      select {relation:["owns"], direction:"both"}
what writes resource R     select {relation:["writes"], direction:"in"}, then filter to R
what is in context C       select {within:"::Sys.Dom.C", depth:1}
which services exist       select {kind:["Service"]}
what does this API expose  select {within:"::…Service.Api"}, include children
```

It returns what the model **declares**, plus the compiler's own reference
resolution — and nothing about importance, quality, layer or grouping.

## 3. Causal questions — the process graph, always

"What follows X?", "what does this Command lead to?", "how are these two
messages connected?", "does anything react to this Event?", "is this
process closed?" — **never answer these by collecting the edges around a
message**, and never from intuition: `continuum_process_graph` is the
only honest answer. Inbound and outbound edges say who *touches* a message; they do
not say what *follows* it.

```
trace X          everything X can lead to, hop by hop
impact X         what X can affect: every receiver of it and of what it reaches
dependencies Y   what can lead to Y, with the closed-world exclusions named
paths X,Y        the simple paths between two messages
explain …        the evidence one hop or one path rests on
```

"In what order does this happen?", "what runs concurrently?", "where
does the model leave behaviour open?" — `continuum_preview` (CLI:
`continuum preview <manifest | package>`), the behavioural projection.
It draws each trace as a tree, expands every Workflow into its steps
with the order its transitions establish, marks the steps they leave
*unordered* (potentially concurrent, statically — the specification's
own relation), and lists the underspecified-causality findings. Repeat
its three labels — DECLARED, DERIVED, UNSPECIFIED — and never promote
"unordered" to "runs in parallel": the model is silent there and the
implementation decides. A finding is a place to ask the owner, not a
defect to report.

`detail:"summary"` first. Report each hop's classification **verbatim**
(topological = *causal relation undeclared*; reactive; correlated), a path
is as strong as its weakest hop, and `closed` means declared causal
closure — never that anything ran or succeeded. Never upgrade a hop, never
fill an open consumption from code or intuition, and never propose
declaring a Reaction to close a gap nobody established.

## 4. Recipes

- **Traceability** — capability ← `realizes`; requirement ← `satisfies`
  (implementation claim) *and* ← `verifies` from a Test (verification
  claim). Neither ⇒ uncovered; `satisfies` without `verifies` ⇒
  unverified. Report both directions.
- **Who touches a message** — a Command/Query has exactly one `handles`;
  `publishes` / `subscribesTo` / `triggeredBy` / `projectsFrom` list the
  declared participants. That is *structural*; the moment the question
  becomes "what follows", go to §3.
- **Who produces a view** (`adl/1.4`) — a ReadModel's `projectsFrom` is its
  **provenance** (and its consumption of those Events); the Service that
  `projects` it is its **declared producer** — responsibility and effect,
  never a message emission or consumption. Report both facts by name and
  never merge them; a view with no `projects` edge is "no producer is
  declared" (a warning), not "nothing produces it". The process graph shows
  the declared path *Event → producer → view* as a `production` record,
  beside the hops. Nothing in the model says how the view is computed,
  which row a presentation affects, or whether duplicates are absorbed —
  say the model does not declare it.
- **Impact of removing X** — collect every inbound relationship and
  attribute reference to X and its subtree; for a mechanical answer author
  a one-op `remove` change and apply it: the
  `adl/semantic/deletion-blocked` refusal enumerates exactly the
  dependents that must be detached first (fail-closed analysis, for free).
- **Workflow walkthrough** — narrate the graph: trigger surface and
  uniqueness rule, StateContract, entry fan-out, each step's kind and
  policy, guarded transitions (a DecisionStep selects **exclusively** —
  retrieve WORKFLOW-SEMANTICS §3.1.1 before describing overlapping
  guards), WaitStep alternatives and deadlines, compensation targets.
- **Guardrail relevance** — `continuum_applicable_guardrails` over tuples
  of **resolved ids** answers which Guardrails are applicable to a
  relationship, including a **candidate** one a change would introduce.
  Applicability is relevance, never satisfaction: no verdict exists.
- **Comparing versions** — equal `changeState` digests ⇔ the same
  architecture state. For *what changed*, `change-architecture`.

## 5. Reviewing a model

Use this flow when the ask is a review. The verdict is yours — Continuum
has no review tool and must not acquire one.

**Conformance assessment is DERIVED, and is not your verdict.** Ask for it
(`continuum` skill §5a) and report the attained level, the identities it
rests on, the findings with their element ids, the `UNKNOWN` reasons and the
blind spots — all as DERIVED, kept apart from the judgment you are forming.
Never present a level as a review verdict, or a review verdict as a level.
For L3 and L4 the answer is a *verifier capability* limit, not something a
reviewer's evidence can remove.

Gather evidence first: compile + every diagnostic resolved;
the shape (projection or scoped queries); ownership and intent coverage
(`owns`, `realizes`, `satisfies`, `verifies`, `dependsOn`, `reads`,
`writes` — `direction:"both"`); causality for the significant messages;
guardrail relevance on the edges that matter. Then judge, across whatever
the model actually supports: business intent and capability realization,
ownership and authoritative-versus-projected state, bounded-context
boundaries and what crosses them, API and message boundaries, workflow
intent versus declared causality, deployment and verification coverage,
and the decisions the model leaves open.

Separate **modelling gaps** (the model does not declare something it
should) from **implementation recommendations** (the architecture itself
should change). Cite evidence for every finding — qualified names,
diagnostic ids, hop classifications. Never invent an element or an edge to
close a gap, and never report a warning as a defect without saying what
the architecture would have to declare instead.

## 6. Views

A view is **deterministic selection + your presentation**, and the two
must stay visibly apart.

1. Select with `continuum_query` — never from memory of the projection.
2. Group and render (C1 context / C2 container-deployable / C3
   component-service, domain, ownership, public-vs-internal, data-flow,
   messaging).
3. State which parts are the model's claims and which are your grouping.
   Continuum has no C4 levels, no importance and no layout: if you draw a
   boundary the model does not declare, say that you drew it.
4. Name what the model leaves undeclared rather than drawing it in
   (technology, deployment topology, runtime behaviour).

Every node and edge must trace to a query result. A diagram with a box
nobody declared is a fabrication with better graphics.

## 7. Beyond the semantic model — route, never guess

- **"What technology realizes X?"** → binding documents (LANGUAGE-SPEC
  §16), standalone artifacts with their own base attestation; a stale
  binding answers nothing about today's architecture. No bindings present
  ⇒ the honest answer is that the model declares the requirement and no
  realization. A `Resource [data-store]` is not "Postgres".
- **"Where is this in the diagram?"** → presentation documents (§17);
  presentation never affects semantics.
- **"What happened at runtime?"** → nothing here. Models declare; runtimes
  run.
- **"Is this deployable?"** → binding satisfaction and target admission
  (§16.5–§16.6, §18), computed per Environment, not readable off the
  semantic model.

## 8. The shape to aim for

> **Q:** Does anything depend on the orders store, and what breaks if we
> drop it?
>
> **A:** Two declared dependents, both in `Sales.Orders`:
> `::OrderPlatform.Sales.Orders.OrderService` **writes** it and
> `::OrderPlatform.Sales.Orders.ReviewProjection` **reads** it
> (`continuum_query`, `relation: writes|reads`, `direction: both`). The
> `FulfillOrder` workflow shows a **derived** `writes` through its
> `persist` step — derived from the step, not authored at workflow level.
>
> Mechanically: a one-op `remove` refuses with
> `adl/semantic/deletion-blocked`, naming exactly those inbound
> statements — they must be removed or retargeted first.
>
> What the model does **not** say: which database product this is (no
> binding document is present), or whether anything reads it outside the
> declared surfaces.
>
> *(Pinned to `acme.orderplatform` @ `example-1`, attestation
> `sha256:12371b55…`.)*

Exact citations; declared / derived / absent separated in words; a
mechanical check where one exists; the limits stated; the state pinned.

## 9. Ledger questions (a client repository)

"Which checkpoint introduced this?", "what changed between DP002 and
DP003?", "is the live package recorded?", "is this transition
verified?" are ledger questions (`docs/guides/CLIENT-INSTALL.md` §5),
answered mechanically and never from memory:

```
continuum ledger history continuum                       the checkpoints, their transitions, the verified-against commits
continuum ledger show <n>                                one checkpoint's digests and transition
continuum diff continuum/history/000<n>-*/<m> continuum/history/000<n+1>-*/<m>   the VERIFIED change between two checkpoints
continuum ledger verify continuum                       lineage; whether the live package is the last checkpoint
continuum icp check <transition> --base continuum       whether a transition is verified against the current state
```

With the MCP server connected, `continuum_ledger {packageDir, op:
current|history|show|verify, sequence?}` answers the same questions
read-only. Cite a checkpoint by its `changeState` digest and its label;
the sequence number is a label, never the identity. A transition's
`transitionId` is the tool's; a `REASON.md` is the author's rationale,
not evidence.
