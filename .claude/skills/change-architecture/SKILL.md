---
name: change-architecture
description: Evolve an existing Continuum ADL model as an architecture CHANGE rather than a rewrite - propose and author an .adl-change, compile it, apply it mechanically to prove apply(A, delta) = B, derive the verified ChangeSet between two states, and narrate a transition in identity terms. Use when asked to add, modify, rename, move or remove something in an existing model, to show what changed between two versions or across a PR, or to evaluate a proposed architectural change before adopting it.
---

# Change an architecture (propose · prove · explain)

**Load the `continuum` skill first** — knowledge routing, tool routing,
diagnostics and evidence discipline live there.

Two things are different in kind, and this skill keeps them apart:

```
mechanically applicable     the ChangeSet applies: apply(A, Δ) = B   — Continuum establishes this
architecturally desirable   the resulting architecture is better     — you argue this, with evidence
```

A change that applies is not thereby a good idea. **Never write files or
call a change adopted because it applied.** `continuum_apply` returns state
B; it does not write it, and whether it is adopted is the user's decision.

## 1. Establish state A

`continuum_compile` with `--state` semantics. You need three things from
it, and they are different digest domains — never substitute one for the
other:

- the **attestation** (canonical model) — what you pin answers to;
- the **`changeState`** digest — what a ChangeSet's `base … state` pin names;
- the manifest's **`core`** pin — what the change's header `core` must equal.

Then read enough of the model to express the change against what is
actually declared: `continuum_query` for the affected elements and their
edges, the projection when the change is broad.

### 1a. Assess A, so the change is proposed against a known state

Assess state A (`continuum` skill §5a) and record it. It tells you what you
are changing *from*: a level not attained, and why. Keep the two kinds apart
— what this change will fix, and what it will not — and say which findings
it deliberately leaves standing. An attained level is **not** permission to
change the model, and a level that was not attained is **not** a reason to
refuse: both are facts the proposal reports.

## 2. Decide the architecture, in concepts

What element is added, modified, renamed, moved, removed; which
relationships change; and what the change deliberately does **not** claim.

A change is authored from what the user established, exactly like a model:
**never invent an element, an edge or a consequence** to make the
transition tidy, to satisfy a diagnostic, or to make B look complete. A
change that declares less than you would like is honest; one that declares
something nobody asked for is a defect that now has a digest.

**Identity is the whole game.** A rename or a move is the *same element
continuing* — same id — and a ChangeSet says so. Remove+add is a different
claim: one element ended and another began. Getting this wrong silently
rewrites the architecture's history.

| Evidence | Verdict |
|---|---|
| renamed, same responsibility and surfaces | same entity — keep the id; the rename **is** the change |
| moved to another parent, responsibilities unchanged | same entity — keep the id; the move is the change |
| split in two | one keeps the id (say which and why); the other is new |
| merged | the survivor keeps one id; the other is **removed**, never silently absorbed |
| same name, entirely different responsibility | not the same entity — remove + add |

Mint ids (`continuum_mint_ids`) **only** for genuinely new elements: never
derive an id from a name, and never reuse a removed one — a removed id
names something that ended.

## 3. Author the change

`.adl-change` (`change-source/1`) is the surface to author when a person
will read the change; the `architecture-change/1` JSON is its lossless
peer. Retrieve before writing:

- `continuum://spec/change-source` — the grammar and the operation set;
- `continuum://examples/architecture-change/README.md` and the five worked
  `.adl-change` files — add, modify, rename-and-move, relationships, and a
  realistic multi-operation change;
- `continuum://spec/architecture-change` for the operation semantics, the
  pins and the refusal rules.

Pins are **copied, never guessed**: `core` from the manifest, `base … state`
from state A's `changeState`.

Order the operations so they apply — detach references into anything you
remove, then removals, then additions (parents first), then
rename/move/modify, then links. Sequential semantics: the first failure
stops the change.

## 4. Prove it

```
continuum_compile {artifactPath: "<change>.adl-change"}   → mode "change": does the proposal parse and type at all
continuum_apply   {manifestPath, changeSetPath|changeSet} → applied | stale | blocked | malformed | failed
```

- **`applied`** with a result digest: `apply(A, Δ) = B` held mechanically.
  That is the proof — and the *only* thing it proves. The result carries
  **three verdicts**: `applicability` (the pin held), `validity` (no
  error in B) and `warnings` (B's design review) with `report` — the
  operations as applied, elements removed/added/modified/renamed/moved,
  relationship changes, `findings` by id. Report the findings (a dead
  Event, an unrealized policy) as what the *new* architecture declares;
  never read `applied` as "complete".
- **`failed` with `adl/semantic/deletion-blocked`**: the diagnostic's
  `detail.blockers` (also `blockers` on the result) enumerates every
  referrer with the exact `unlink #<referrer> <type> #<target>` that
  detaches it — prepend those operations; that list is also the cheapest
  impact analysis available. Nothing is detached for you.
- **`malformed` with `change/parse/reference-form`**: a name-form
  reference (`think`, `Orders.Client`, `optional(Foo)`) where the change
  needs `#<id>` — the `detail.spelling` is the token; look the element's
  id up in the state and write it.
- **`stale`**: the base pin no longer matches the state. Recompile A,
  re-pin, re-derive. Never hand-edit a digest.
- **`malformed` / `failed`**: resolve the `change/*` ids with
  `continuum_diagnostics`; they are spelling and shape defects, not
  architectural ones.

Application is atomic and fail-closed: nothing partial ever happened.

**Handing back state B as authored ADL.** `continuum_apply` with
`emitSource: true` returns `emittedSource` — a manifest and one `.adl`
document that IS state B (ids preserved, references in name form,
canonical layout), verified to recompile to the applied change-state
(`verification.digestsAgree`). Offer it when the user wants the new
state as files; writing them is still the host's act. Do not
hand-reconstruct B from the apply result.

## 5. Deriving a change between two states

When the ask is *what changed* — two recovered models, a PR's two sides,
two versions:

```
continuum_diff {manifestPathA, manifestPathB}
```

It compares by **stable id**, orders the operations, pins them, and
**verifies by applying** before delivering (`verified: true`). Fall back to
authoring by hand only when it refuses (an unresolvable ordering, a
fixed-frame change), or when the user wants the transition decomposed to
match authored intent.

Prerequisites: both states compile, and identity is continuous between
them. Where the producing side minted fresh ids for a continuing element,
the diff can only report remove+add — **say so**; name-based continuity
guessing is non-conforming.

Honesty limit (ARCHITECTURE-CHANGE §12): a mechanical diff recovers *what*
changed, never the authored intent or the order the author thought in.

## 6. Explain the result

Narrate the transition in **identity terms**: what is genuinely new, what
is the same element continuing under a new name or in a new place, what
ended. Then the consequences:

- what B declares that A did not, and what it stops declaring;
- causality — run `continuum_process_graph` over B's affected messages
  rather than reasoning about the change's intent;
- behaviour — `continuum preview <manifest> --change <file>` (or
  `continuum_preview` with `changePath`) renders state B's ordering and
  its underspecified causality before anyone adopts the change; quote
  its DECLARED / DERIVED / UNSPECIFIED labels as they stand;
- relevance — `continuum_applicable_guardrails` accepts a **candidate**
  tuple, so you can ask which Guardrails would bear on an edge the change
  introduces *before* it exists;
- **conformance of B, where it matters.** Assessment needs a manifest, so
  materialize B first (`--emit-source <dir>`, or `continuum icp materialize
  <transition> --analysis` for a transition) and assess *that*. `--assess`
  on an apply-mode compile reports the applied change and no assessment —
  the flag is accepted and nothing is assessed, so never read its silence as
  a level. Report A's level and B's as two facts, and never present a level
  as agreement that the change is desirable;
- what the change leaves undeclared.

Deliver: the `.adl-change` artifact, the apply status with both digests,
and the architectural explanation. Then stop and let the user decide.
Writing state B into the repository is a separate, explicit step.

## 7. Package the transition (a client repository)

When the model lives in a client's `continuum/` (a development package
with a ledger — `docs/guides/CLIENT-INSTALL.md` §4–§5), a change is
delivered as an **Incremental Change Package**, never as an edited
state:

```
continuum icp init transitions/<seq>-<slug> --base continuum --reason "…" --semantic changed [--bindings changed] [--acceptance changed]
   → put the .adl-change under changes/ (its base pins the checkpoint's changeState — copied from `continuum ledger current`),
     changed binding documents under bindings/, added or changed scenarios under acceptance/scenarios/, removed names in the manifest
continuum icp check transitions/<seq>-<slug> --base continuum          the tool derives "verified"; you never write it
continuum icp materialize transitions/<seq>-<slug> --base continuum --analysis   DP(n+1) for inspection: the complete suite executed, nothing sanctioned
continuum icp ready transitions/<seq>-<slug> --json                     from here on, readiness names every next step (`continuum` skill §1b)
```

The manifest carries no status; `transitionId` is written by the tool
over the authored content. With the MCP server connected,
`continuum_icp_check {icpDir, baseDir}` returns the same verdict
read-only, and `continuum_icp_state {icpDir}` the state the facts
derive — with `next`, the lawful acts from there. Which act is next, who
performs it and whether it would succeed is `continuum icp ready`'s answer,
not yours — drive the transition with the readiness loop (`continuum` skill
§1b). `continuum_workflow_verify {packageDir}` says whether the record those
facts live in verifies at all. If the checkpoint moved under you
(`base-drift` from the guard), `continuum icp rebase <icp> --onto continuum`
re-pins into a **new** package and reports the refused operations —
only those need a Δ'. Which acts remain, in what order and performed by whom,
is readiness's answer and is never restated from here; what holds regardless
is that **approval precedes implementation** and that nothing you ran — a
compile, an apply, a suite, an attained level — is any of those acts. Until a
person has performed them, everything you delivered is a proposal.
