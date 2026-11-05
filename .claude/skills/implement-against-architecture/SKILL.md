---
name: implement-against-architecture
description: Gate implementation on an explicit model decision - classify a requested feature, change or refactor as needing an ADL change, a BDL change, both or neither; for anything but neither, inspect the authoritative model, propose the delta, prove what can be proved, and STOP for approval before writing code; then implement against the accepted model and check conformance. Use when asked to build, change, extend or refactor something in a repository that carries a continuum/ package, when a request might move the architecture or the business model, or when implementation has revealed that the model does not answer a question the code needs answered.
---

# Implement against the architecture (classify · gate · implement)

**Load the `continuum` skill first** — knowledge routing, tool routing,
diagnostics and evidence discipline live there. This skill adds one thing to
it: **a decision boundary that code must not cross unasked.**

It orchestrates and delegates: it authors no change format, runs no compiler
and defines no rule of its own. Reading and explaining the model is
`understand-architecture`; authoring an `.adl-change` and proving
`apply(A, Δ) = B` is `change-architecture`; turning an accepted architecture
into an implementable handoff is `development-package`; drift is
`reconcile-architecture`. What is left here — whether the model must move, and
when to stop — is this skill's, and the implementation and the architecture
judgment are yours.

## 0. The rule this exists for

> **An unrecorded architectural decision is still a decision. It is just one
> nobody approved, nobody can find, and the next reader has to reverse-engineer
> from code.**

Implementation that quietly assumes a new service, message, owner, dependency
or business rule has moved the architecture without moving the model. That is
the failure this skill prevents — not by forbidding change, but by making it
explicit and approved *before* it is encoded in code. Two absolute
prohibitions: **never edit the authoritative model to unblock yourself** (not
`continuum/model/`, not a `.bdl` source, not "just to make it compile" — the
model moves by an approved change and by nothing else), and **never treat "it
compiles", "it applies" or "it attained a level" as approval** — each is a
mechanical fact, and whether the change is a good idea is the user's judgment.

## 1. Classify the request — four outcomes

Do this **before** planning implementation, and state the outcome in one line
so the user can disagree with it cheaply.

| Outcome | What it means | Go to |
|---|---|---|
| **neither** | the model already answers every question the code needs | §2 |
| **ADL** | the structure changes: what exists, who owns it, what talks to what | §3 |
| **BDL** | what the business MEANS changes: a rule, a policy, a term, an outcome | §4 |
| **both** | the business meaning changes and its realization changes with it | §5 |

### How to decide, concretely

Read the model before answering — `continuum_query` for one element,
`continuum_text_projection` for a bounded slice, `continuum_process_graph` for
anything causal. Do **not** classify from the repository's code, its layout or
its README: those are the implementation, and the question is whether the
*intent* covers what is being asked.

- **ADL** if the request adds, removes, renames, moves or re-owns an element
  (service, actor, resource, capability, message, workflow, bounded context,
  domain), an edge (`dependsOn`, `publishes`, `consumes`, `reads`, `writes`,
  `owns`, `realizes`, `deployedAs`), a constraint, a requirement or a policy —
  or changes *who* is responsible for something already declared.
- **BDL** if it changes what the business means: a capability's purpose, a
  policy or rule, a value definition, a term, an outcome, a modality
  (*stated*, *required*, *observed*?), or an assertion's provenance. A business
  model answers what the organization does and means, independent of any
  system realizing it.
- **neither** if it is entirely inside something already declared: an
  algorithm, a data structure, error handling, performance, logging, a
  refactor preserving every declared element and edge, a test, or a fix making
  code match what the model *already* says.

**When the answer is genuinely unclear, say so and ask.** An honest "this could
be read two ways, and they lead to different work" costs one question. Guessing
costs a wrong model or a silent divergence.

### 1a. Assess the starting model — before the work, never instead of it

Whatever the classification, assess the **authoritative** model the work will
be built against — `continuum conformance assess <package dir> [--baseline
<file>] --json`, or `continuum_assess_model {manifestPath}` — and record what
the `continuum` skill §5a lists. Then split what came back, because the halves
lead to different work:

- **Blocking** — a level not attained for a reason this work will not fix: a
  `FAIL` carrying an `error` diagnostic, an unresolved reference,
  `policy/unresolved` from an unreadable baseline, a warning class the
  baseline in force makes policy-relevant. Say so *before* building; some are
  themselves an architecture change, which is §3.
- **Obligations this work is meant to satisfy** — the `UNKNOWN`s whose
  evidence does not exist yet because the implementation does not.
  `precondition/no-correspondence-map` in a repository with no citations is
  the work, not a defect in the model.

**No minimum level, no new approval.** The level is a fact you report;
whether it is good enough is the owner's judgment. Do not gate on it.

## 2. Neither — implement

Say which declared elements the work sits inside, then build it. Nothing here
is ceremonial: naming them is what makes it checkable later that you stayed
inside them.

**If building it turns out to need something the model does not declare, that
classification was wrong — stop and go to §8.** "Neither" is a finding about the
model, not a commitment to finish without one.

At the end, confirm conformance rather than assuming it — §7 step 3, then §7a.

## 3. ADL — the change artifact path

`change-architecture` does this work — establishing state A, deciding the
architecture in concepts, authoring the `.adl-change` against the repository's
change record, and proving `apply(A, Δ) = B`. Delegate to it rather than
re-deriving its steps here. What this skill adds is the **stop**, and three
rules it will not relax:

- a population or correction is a **change**, never a rewrite — that is what
  keeps element identity stable, so a later `continuum_diff` reports a rename
  as a rename;
- **adding an element, edge, owner or reaction to silence a warning is not a
  change and is never permitted** — it is a lie with a digest behind it. An
  unrealized-intent warning can be the honest finding; explain it instead;
- where the change records claims, only `VERIFIED` and `INTENDED` may be
  declared, and an `INTENDED` claim says so in its `desc`. `PROPOSED`,
  `UNKNOWN` and `CONFLICT` are not declared at all — they are gaps.

Then **§6 — present and stop.**

## 4. BDL — the source-diff path

**There is no `.bdl-change`, and you must not invent one.** BDL change
semantics are deliberately ABSENT rather than stubbed — no diff, no apply, no
change artifact, no change source — and the MCP contract's `BUSINESS_ABSENT`
names each capability that does not exist and why. Read that record rather
than guessing, never emulate what it says is absent, and never describe a BDL
change as if it were proved the way an ADL change is: the two paths are
asymmetric because the languages are. The authority, the interfaces and what
BDL does not do: `docs/guides/BDL-CONSUMER.md`.

So the proposal is a **source diff plus a compile result**:

1. **Read the current model** — `continuum_business_inspect` (`view` is the
   closed set `elements` | `worklist`; there is no filter, because BDL has
   fixed projections rather than a query language) and
   `continuum_business_text_projection` for the readable form.
2. **Write the proposed `.bdl` as a candidate** beside the authoritative
   document, never over it; `continuum_business_parse` says whether it reads
   as a BDL model at all.
3. **Compile it** — `continuum_business_compile`. Diagnostics carry `rule`
   and `jurisdiction`, and nothing resolves a BDL rule id to a registry:
   quote the rendered diagnostic, never invent a lookup.
4. **Diff the CANONICAL source, not your draft** — `continuum_business_emit`
   returns each model as authored `.bdl`, so emit both and compare those.
   Diffing a draft shows your formatting as change and hides a real change
   that reformats.
5. **Show the behavioural delta**, which no tool derives: the canonical
   diff; the `elements` and `worklist` views before and after; valid/invalid
   with every diagnostic that appeared or disappeared; and in words, which
   rule, policy, term or outcome now means something different and what
   follows for anyone relying on the old meaning.
6. **BDL never mints identity** (R3): a new element's id is authored by a
   human, deliberately. Do not generate one; do not reuse one.
7. Where both languages are present, `continuum-bdl`'s
   business-to-architecture correspondence checkers say whether the two still
   agree. Use them; never assert correspondence by reading.

Then **§6 — present and stop.**

## 5. Both — coordinate, then present once

Order matters, and it follows from what the two languages are for: **BDL says
what the business means; ADL says how a system realizes it.** So:

1. Settle the **business** delta first (§4). Meaning is upstream of
   realization, and an architecture built to realize a rule nobody has agreed
   on is work done twice.
2. Derive the **architecture** delta from it (§3), and say explicitly which
   business change each architectural change serves.
3. Check **correspondence** between the two before presenting — a pair that
   does not correspond is not two approvals, it is one incoherent proposal.
4. Present **one** delta with both halves, and take **one** approval. Do not
   implement the ADL half while the BDL half is still open: the halves are not
   independently adoptable, and half a coordinated change is a divergence with
   a paper trail.

Then **§6 — present and stop.**

## 6. The stop

**Present, then stop. Do not continue into implementation in the same breath.**

What a reviewer needs, and nothing more: **what changes**, in architecture or
business concepts rather than syntax; **why**, and what it makes possible that
is not possible now; **the proof, labelled for what it is** — for ADL,
`apply(A, Δ) = B` with the state digests; for BDL, the compile result and the
projection comparison, and say which is which, because a BDL proposal has no
`apply` proof and must not appear to; the **assessment of the proposed
target** as an observation, never an endorsement (§7 step 1); **what it does
not settle**, as gaps rather than omissions; and **what you will implement**
once it is accepted.

Then ask for approval, explicitly, and wait. Approval of the *model change* is
not approval of an implementation plan you have not shown, and approval of one
half of a coordinated change is not approval of the other.

## 7. After approval — implement, then check

For ADL in a client repository the change is a **transition**, and neither its
order nor who performs each act is yours to reconstruct or restate:
`continuum icp ready <transition>` is the authority, driven with the readiness
loop (`continuum` skill §1b). Two rules hold independently of any sequence:
**implementation begins only once the approval fact exists** — an approval
asked for after the code is written does not authorize that code (§7.2 of the
lifecycle) — and **nothing you ran is a lifecycle fact**: a passing suite, a
clean guard and an attained level are evidence a person weighs, never an act
they performed.

1. **Prepare the approved target.** For ADL, read readiness and perform the
   agent steps it awaits; the `.adl-change` stays as the transition's
   explanation. Assess the target you will build against — the resulting
   package's model, not state A — and carry its actionable findings into the
   work: the reason code verbatim, the element ids from `warnings[].elements`,
   what evidence would remove it, and which implementation or verification
   task produces that evidence (`docs/guides/CONFORMANCE-ASSESSMENT.md` §4.2,
   §5). Keep **observed**, **proposed** and **approved** apart in words: an
   assessment of a proposed target is an observation about a proposal, and
   approval stays the person's. For BDL, edit the authoritative `.bdl` source,
   then recompile and re-inspect to confirm it is the source that was
   approved.
2. **Implement against the accepted model**, not your memory of the proposal.
   Re-read it if the work is long. **The moment the work needs an answer the
   accepted model does not carry, stop and go to §8** — including when the
   missing piece looks like a small extension of what was just approved. That
   is the case this skill exists for, and the one most easily walked past
   while there is momentum.
3. **Check conformance rather than assuming it.** `continuum_guard` for what
   the Architecture Guard would say on this evidence; `continuum_package_check`
   where a package is built; `continuum_icp_check` for a transition;
   `continuum_workflow_verify {packageDir}` for whether the record those facts
   live in verifies at all — when it does not, no state read from it is
   trustworthy. On drift, `reconcile-architecture` owns what to do: never
   hand-repair state it is built to reason about.

   **"The check did not run" is not "the check passed."** A guard invocation
   can come back without a judgment for reasons unrelated to the
   architecture: no credential, an unreachable Host, a route the deployment
   does not implement, a package never built (`package check` needs a
   `PACKAGE.json`). A git hook may let the commit through, and locally that is
   deliberate — it is still not evidence of conformance. So read the answer
   for WHICH outcome it is — PASS, FAIL, INDETERMINATE (exit 2, or exit 3
   where a judgment was required) or NOT APPLICABLE — say which you got, never
   report an absent answer. For an indeterminate one the honest report is
   "conformance was not established, because X", with X beside what you built.
4. **Report what was approved, what was built, and what still differs.** A
   difference you can name is a finding; one you cannot is a divergence.

### 7a. After implementation — verify, reassess, report

Run what actually exercises the implementation — the repository's own tests,
`continuum package run`, `continuum conformance check`, `continuum evidence
check --anchored-at <rev>`, `continuum package check`, `continuum guard ci`
(step 3 for reading its outcome) — and name each result as itself, at the
revision it was obtained at. What each establishes:
`docs/guides/CONFORMANCE-ASSESSMENT.md` §4.3.

Then reassess the model, report the level again, and say what keeps it honest:

> A reassessment of the same model establishes model conformance again. It
> establishes nothing about the implementation.

So **never** report L2, L3 or L4 because the same model was reassessed after
the code was written, however green the suite. No Continuum surface carries
this implementation evidence into an assessment today, and L3/L4 are a
*verifier capability* limit rather than a missing-evidence one — that guide's
§6. Report verification results **beside** the assessment: never blended into
one verdict, never described as a level.

Report in one place: the **assessed subject** and digest; the
**implementation revision**; the **attained level** with its specification,
profile, policy-baseline and evaluator identities; the **verification
results**; the **unresolved obligations** with their reason codes; the **blind
spots**; and the **lifecycle state** (`continuum icp ready <transition>`, else
`continuum ledger current`) — which no assessment changes.

## 8. When implementation outruns the model

This is the case the whole skill exists for, and it will happen: you are
building and the model does not answer something the code must answer. A name
it does not declare. An owner it does not name. A message with no publisher. A
rule the business model leaves open.

**Stop. Return to §1 and re-classify.** Then:

- **Do not** infer the answer and encode it in code.
- **Do not** edit the model to match what you were about to build.
- **Do not** widen the approved change because the addition is "nearly the
  same change". It was approved as what it was.
- **Do** say precisely what is missing, what the code needs from it and what
  the candidate answers are — then take it back through the gate.

Keep partial work: implement everything that does *not* depend on the missing
decision and say exactly what you left out and why. Scaling the work down is
the user's call. If the gap is real but nobody is ready to decide it, **record
it as a gap** rather than leaving it in your head — an open question with a
name is an asset; the same question inside an implementation is a defect
waiting to be found.

## 9. What this skill never does

- Writes code that depends on an unapproved model change, or implements a
  semantic change before an approval fact binds its transition.
- Edits `continuum/model/` or an authoritative `.bdl` source to unblock itself.
- Claims an implementation, behavioural or runtime level from a model
  reassessment, or presents a compiler attestation, a green suite or a
  `conformance check` row as a conformance level.
- Treats an attained level as an approval, or a level as a gate on
  implementation.
- Hands a person a chain of lifecycle commands, or any command readiness does
  not name as the awaited human step.
- Invents a `.bdl-change`, a BDL diff, a BDL apply, or a BDL identity.
- Adds an element, edge, owner or reaction to silence a compiler warning — not
  once, not to get a clean run, not ever.
- Infers technology. A `Resource [data-store]` is not a product name.
- Reports an architecture question answered from code, prose or a generated
  view when the model is what was asked about.
