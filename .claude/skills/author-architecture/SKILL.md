---
name: author-architecture
description: Author a NEW Continuum ADL model from a description of a system, a business process, or a set of requirements - inventory the elements, mint identities, write adl-source (.adl) with its manifest, compile it, repair the diagnostics, and deliver it with the compiler's attestation as proof. Use when asked to model a system in ADL, design an architecture in Continuum, or turn a description or requirements document into a Continuum model. For an existing model use change-architecture; for a repository use recover-architecture.
---

# Author an architecture (description → compiled model)

**Load the `continuum` skill first** — knowledge routing, tool routing,
diagnostics and evidence discipline live there. This file is the authoring
flow only.

The deliverable is an **attested model**, never "a file that looks right":
`<system>.adl` + `<system>.adl-manifest.json`, compiled clean, delivered
with the attestation the compiler returned.

## 1. Understand the request before choosing constructs

Model what the system **is** — not how it runs, not its code structure,
not its technology. Reason in architectural concepts first; ADL is the
representation of that thinking, not a substitute for it.

**Bounded clarification** (the contract's rule, applied here): ask at most
three questions, and spend them only where the answer changes the
architecture — which capability the system owns, where a boundary falls,
who is authoritative for a store. Detail that can be recorded as an
assumption is not worth a question. Every assumption goes in the report,
never inside the model: an absent claim is recoverable, an invented one is
a defect.

## 2. Inventory before source

Before writing any ADL, list every element you will declare: **kind, name,
parent, intended relationships** — and check each against what the request
actually establishes. Anything you cannot point at a source for comes out
of the inventory and goes into your open-questions list.

This step is not ceremony. It is where fabrication gets caught, while it
is still cheap.

## 3. Mint identities

```
continuum_mint_ids {count: <at least one per element, steps and transitions included>}
```

One call, up front. Every element declaration carries a `#<uuid>`;
**parsing never mints identity**. Use each id exactly once, never derive
one from a name, and never reuse a removed one. Without MCP:
`node -e "for(let i=0;i<60;i++)console.log(crypto.randomUUID())"`.

## 4. Retrieve what you are about to write

Do not author from memory of the syntax. Before writing, fetch what this
model actually needs (see the `continuum` skill §2):

- the authoring reference and the compile loop —
  `continuum://guides/repository-to-source` (its §4 is the element/
  relationship/clause reference, its §5 the manifest);
- a **worked example** to mirror — `continuum://examples/order-platform.adl`
  for the general shape, `continuum://examples/async-order-fulfillment.adl`
  for reactions, waits, channels and correlation;
- METAMODEL §5–§6 before any edge you are not certain is legal;
- SYNTAX or the grammar for a construct the example does not cover.

## 5. Author, in containment order

`system` → actors and external systems → domains (capability / requirement
tree, policies) → bounded contexts (type definitions, business rules,
resources, services with their api/operations, commands/events, workflows,
agents/tools) → deployables, environments, tests → the relationship
clauses that connect them, with `desc` throughout.

The manifest is always JSON: package name, an opaque version token, the
core pin the authoring guide names, empty imports/extensions, the document
list.

Two standing rules while writing: **preserve what the user established**
(names, ids, facts), and **declare only what they established** — no
consumer, publisher, owner, causal consequence or contract invented to
make the model feel complete.

## 6. Compile — the only thing that makes it valid

```
continuum_compile {manifestPath | packageJson}
```

Iterate to **zero errors**. For each diagnostic: resolve the id
(`continuum_diagnostics`), understand what the *architecture* is missing
or mis-stating, fix the model, recompile. Six iterations is a reasonable
ceiling; past that, deliver what you have, labelled, with the remaining
diagnostics explained.

Read warnings as **design review**, not noise. `unrealized-intent`
enumerates where the intent layer is unrealized — add the edge where the
request supports it, otherwise report it as a gap. `dead-event` /
`phantom-event` mark a message whose other side nobody established;
`unproduced-read-model` (`adl/1.4`) marks a view with provenance but no
declared producer — declare `projects` on the Service the brief makes
responsible for the view (it must `subscribesTo` every projected Event;
one producer per view; same context), and where the brief names none,
report the gap. Never add an element or an edge to silence one.

## 7. Deliver with proof

- the `.adl` and the manifest;
- the **attestation** — and say "compiles clean" only because
  `continuum_compile` returned it. If no compiler was reachable, label the
  output **UNCOMPILED** and say so plainly;
- a short architecture summary (3–8 lines): intent, then structure, then
  behaviour;
- the remaining warnings, explained as claims about the architecture;
- assumptions and open questions — outside the model.

Offer, do not assume: writing files into the repository (`continuum/model/`
at its root is the convention — the package's semantic state), the dense
projection for review
(`continuum_text_projection`), and the CI gate that keeps the model
compiling (`recover-architecture` §7).

## 8. Scope limits — state them

One package per model unless the user asks otherwise. The semantic model
carries no technology, no diagrams and no runtime: bindings and
presentation are separate artifacts, and a `Resource [data-store]` is not
"Postgres". If the request needs something the language does not have, say
so — **ADL 1.x is frozen**, and the answer is to model it with the
concepts that exist or to record it as an open question, never to invent a
construct.
