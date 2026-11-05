---
name: recover-architecture
description: Conduct an evidence-driven architecture recovery of a repository, a git ref, or a PR snapshot - inspect the implementation, author ADL backed by citations, record what could not be established as gaps, and compile it through the `continuum recover` lifecycle to recovery completion, which is where the skill finishes. Acceptance into `continuum/model/` is a separate lifecycle transition it only offers. Use when asked to recover or extract the architecture from code, model a repo in ADL, generate ADL for a codebase, refresh a previous recovery, or determine what architecture state a PR or branch produces.
---

# Recover an architecture (repository → attested, evidence-backed recovery)

**Load the `continuum` skill first.** Authoring mechanics are
`author-architecture`; the evidence→construct mapping is
`continuum://guides/repository-to-source` (the `.adl` surface) or
`continuum://guides/repository-to-model` (adl-json). **Read
`continuum://guides/architecture-recovery` at the start of every recovery** —
the doctrine (evidence laws, inspection prompts, gap taxonomy, what each check
proves, the report shape). This file is the sequencing and the judgment calls.

The deliverable is an **attested, evidence-backed recovery of what one
repository establishes at one commit** — never a file that merely parses, never
a model of the code's structure, never the architecture you would have designed.

> **The invariant.** *Recovery records what the architecture appears to be;
> acceptance decides that it is what the repository asserts.* Separate
> lifecycle transitions: this skill performs the first and finishes. It never
> performs the second, never treats a repository as incomplete for not having
> taken it, and never lets tooling that presumes acceptance argue it into it.

## 0. The two halves

**You decide what the architecture is.** Continuum owns the lifecycle:
provenance, the authoritative compile, citation mechanics, hashing, manifest
closure, packaging, reproducibility. There is no scanner and there is not going
to be one. **Never hand-build what the lifecycle guarantees** — a provenance,
hash, attestation or verdict derived in prose swaps a checkable fact for a claim.

```bash
continuum recover init | resume      # a session, with provenance
#   YOU author continuum/recovery/model/, gaps/gaps.json, observations.json
continuum recover compile            # the authoritative compile; persists a proof
continuum recover evidence init      # the sidecar, scaffolded from the model's ids
continuum recover evidence check     # do the citations still hold?
continuum recover status             # where the session is; what is still missing
continuum recover inspect            # what the recovered model declares
continuum recover export             # §8 only: immutable, every file hashed
continuum recover verify <export>    # §8 only: recompiled from the export alone
```

Every command takes `--json`. **`status` says what to fix next — read it.**
`continuum recover --help` and `RECOVERY.md` in `continuum-cli` own the
mechanics. **The session is `continuum/recovery/`, never `continuum/model/`** —
a recovery never writes the latter, even to satisfy a tool that expects it.

## 1. Establish the state, and pin the snapshot

Look for **an accepted model** — `continuum_find` on the repository root (or a
glob for `continuum/model/*.adl-manifest.json`): if one exists, **compile and
read it first** (`understand-architecture`); it is an identity baseline. And
for **a prior session** — `continuum/recovery/`, the lineage carrier (§3).

`init` captures root, name, branch, revision, dirty state, timestamp and
toolchain pin from your own git. **Never fabricate any of it.** First recovery
→ `continuum recover init`; recovered before → `continuum recovery resume`
(§3). An existing session is **refused** by `init` rather than replaced: it is
provenance, and its ids are what a later recovery reuses.

**A dirty tree is provenance, not a failure.** It is recorded, the session says
the commit will not necessarily reproduce the recovery, and under §8 it cannot
reach `reproducible`. **Never clean a tree, and never restamp a session.**

Other snapshots, without disturbing the checkout — **a ref**:
`git -C <target> worktree add --detach <scratch>/snap <ref>`, removed when done.
**A PR**: fetch `pull/<N>/head`; head is that ref, base is
`git merge-base <default-branch> pr/<N>`, each in its own worktree; recover
both, then hand to `change-architecture` §5 — the verified
`architecture-change/1` ChangeSet between the two states is the PR's
architectural delta, and §3's continuity is what makes a rename a rename.

## 2. Inspect before modelling; keep the three laws

Read the repository to locate the boundaries (guide §3); its §3.1 shape prompts
are **prompts, never facts about the repository in front of you**. The guide's
§2 owns the laws: **evidence before inference**; **recover the current
implementation, not the documented target** (a doc/code disagreement is a gap);
**local evidence only** — `A` importing `B` proves `A → B` and nothing about
`B → A`, about ownership, or about the two running together, so a reverse edge
is a gap rather than an inference.

**Recover architecture, not source structure.** Not every file, class or test
is an element. Sparse is acceptable; below the architectural floor, more
elements is worse. Produce the flat inventory (`author-architecture` §2) first.

### Semantic reconstruction checks (required before authoring)

Inspect **execution paths, not only declarations**. Build a compact working
evidence matrix for the architecturally significant behaviors and interfaces:
claim → source locations → observed implementation → confidence/uncertainty.
This is a reasoning aid, not a replacement for the recovery evidence sidecar or
the compiler's verdict. Apply these checks proportionately to the repository:

1. **Ownership and boundaries.** Trace HTTP handlers, jobs, events and other
   entry points to the module that actually performs each write. Distinguish
   entry points, code modules, logical responsibilities, deployment units and
   separately owned services. Multiple callers do **not** establish multiple
   writers/owners; a table grouping does **not** establish a separate physical
   database or transaction boundary. Before reporting shared-write coupling,
   identify the concrete write implementations and the transaction owner.
2. **Transactions and side effects.** Follow call chains into database
   transactions, external payment/messaging calls, and subsequent callbacks.
   Claim shared atomicity only when one demonstrable transaction encompasses
   the operations. Sequential calls that each open a transaction are **not**
   one transaction. Separate observed atomicity from intended consistency.
3. **State machines and exceptional paths.** Read the actual transition
   guards and privileged mutations. Enumerate allowed and forbidden transitions
   by actor/path. Trace cancellation, expiry, retries, asynchronous webhooks,
   and failure handlers before summarizing a business rule. Do not conflate
   "can be marked paid by payment code" with a general unpaid→paid transition.
4. **Public contracts.** Determine request/response shapes from serializers,
   response construction, schema validators and tests—not merely internal
   domain types or names. Check nesting, field names, nullability, omitted
   values, arrays, deleted-reference behavior, and nested view/quote fields.
   If no source establishes the exact wire shape, mark it uncertain rather
   than asserting a convenient DTO.
5. **Operational and security behavior.** Inspect effective configuration and
   tests for API-wide and per-route rate limits, headers/CSP, authorization,
   secrets handling and retention. Quote exact thresholds with their windows;
   do not invent retention or deployment policies. If tests assert a
   requirement not otherwise expressed, record the tested behavior and its
   evidential limits.
6. **Storage topology.** Distinguish physical databases from logical table
   groups; follow foreign keys and cross-table transactions before claiming
   independent data stores. A conceptual partition must not be described as a
   physically isolated store.

**Evidence and uncertainty:** Classify each consequential statement as
*observed*, *inferred*, or *unknown* in working notes and report contested
interpretations explicitly. Source citations prove that referenced bytes
exist; they do not prove the interpretation is semantically correct. Do not
label speculative architecture as implemented fact. Architectural diagrams
may still be completeness-first and include clearly identifiable inferred
or speculative elements; do not constrain diagram composition to only
verified boxes. The recovered **as-is model** must remain faithful to the
implementation and separate proposed redesigns from recovered facts.

**Adversarial pass:** Before declaring the recovery complete, attempt to
falsify each high-impact claim using a second reading of the actual call
path/response construction. Specifically ask: "Am I mistaking an entry point
for an owner, a sequence for a transaction, a proposed boundary for an actual
deployment, or an internal type for wire JSON?" Correct wrong claims in
place, preserving identities; move unresolved questions into gaps. Do not
reinterpret a compiler warning alone as proof of an implementation defect:
first test whether the recovered boundary caused it.

## 3. Identity continuity — the judgment the tools cannot make

Element ids are the architecture's stable identity; churn them and every later
comparison degrades into remove-and-add noise. **The committed
`continuum/recovery/` session is the lineage carrier**: `continuum recovery
resume` reopens it and the prior ids carry — delete it and the next recovery
mints fresh ids.

Resume carries the lineage and the prior ids (`identity.priorElements`) and
nothing more: the session opens with **no model and nothing compiled**, so it
asserts identity, never that the previous recovery is still right. Prior gaps
and evidence arrive under `identity.context` as **context only** — a gap at the
previous revision is not an open gap now, and a line number at the old revision
is not evidence at this one. **The previous export is immutable.**

Reuse a prior id where repository evidence says it is the same entity; mint the
rest with `continuum_mint_ids` in one batch (`recover inspect` reports how many
carried). Judge from evidence — same responsibility, endpoints, store — **never
from name equality**. Never derive an id from a name; never reuse a removed one.

| Repository evidence | Verdict |
|---|---|
| renamed or moved, responsibilities unchanged | same entity — **keep the id**; the rename *is* the change |
| a store re-platformed, same role | same entity — **keep the id** |
| one element split in two | one **keeps** the id (the primary responsibility); the other is new |
| two merged | the survivor **keeps** one id; the other is **removed**, never absorbed |
| same name, different responsibility | not the same entity: remove + add, fresh id |

Where the evidence does not settle it, prefer continuity and record the doubt.

## 4. Author incrementally

Author per `author-architecture` §5–§6 in containment order; manifest version
token = **the snapshot commit SHA**. Small increments: rationale, evidence,
compile, inspect, record what you could not establish. **Prefer narrow repair
over regenerating the model** — a regeneration silently reminted is §3's
failure. In PR mode, author the head side by editing a copy of the base side.

**Never invent architecture.** Where the repository establishes nothing, omit
the element: an absent claim is recoverable, a fabricated one is a defect.
**Never invent a causal claim** — a Reaction, a MessageChannel, a consumer, a
publisher or an owner is authored **only** where the code establishes it, and
an undeclared causal relation is the honest recovery.

## 5. Compile, status, inspect — continuously

`recover compile` persists the proof **even when it failed** — that is what a
proof is. Read architectural diagnostics as design review **of the recovery**:
`cross-context-resource-access` means the carve is wrong; `dead-event` and
`phantom-event` mark a message whose other side the repository did not
establish. Report them. **Never add the missing consumer, publisher or producer
to clear one** — declare `projects` only where the code visibly subscribes to
**every** Event in the view's `projectsFrom` set.

**A green compile is the floor, not the finish**: it proves the ADL is *legal*,
not that the architecture is complete, correct or evidenced. **Do not invent a
score or a completeness grade.** Report coverage as a numerator and a
denominator, both counted.

## 6. Evidence, and gaps

`recover evidence init` scaffolds the sidecar from the model's ids; attach
citations, then `recover evidence check`. **Write `derived` on every citation**
— `verified` is a *verifier's* to award by resolving anchored bytes; a sidecar
claiming it is refused. No verdict the tools did not produce. Checking is
mechanical and says nothing about whether your reading is right: **never
weaken a claim to make a citation pass**, and **never widen disclosure** beyond
the range that establishes the element (guide §5).

**Gaps are the expected state.** An empty ledger on a real repository means
nobody looked. A gap says what could not be established, what you looked at,
and what would close it (guide §4). **Recovery is not architecture review**: do
not fix implementation defects unless asked.

**Semantic fidelity gate:** After compiling and checking evidence, compare
the model's architecture boundaries, business rules, transactional claims,
public DTOs, and operational limits against the evidence matrix above.
Correct discrepancies and recompile/check evidence. Record unresolved
interpretations as gaps. A mechanically valid citation does not excuse an
incorrect description. Report the scope of this semantic review without
claiming tool-certified semantic verification.

## 7. Recovery completion — the end of the skill

A recovery is **complete** when the session holds a current authoritative
compile, a citation sidecar whose entries resolve, a gap ledger, and the
observations. **`recover status` is the arbiter.** That is success and the
whole deliverable: a recovered repository is not incomplete, not pending, not
waiting on anything, and the absence of `continuum/model/` is the *correct*
state for a repository that has not accepted an architecture. **Commit the
session** (the lineage carrier) on the owner's say-so. **Do not export** —
exports belong to §8. A complete *session* is never a claim that the recovered
architecture is complete or correct.

## 8. The Continuum workspace only — export and handoff

**A named exception for the Continuum project's own repositories, and it does
not apply to a client.** Continuum does not install accepted architecture into
its own source repositories; their recoveries are exported with
`recover export`, verified independently with `recover verify` (a successful
export is not a successful verification), and imported into
`continuum-architecture`. **Applicability comes from known ecosystem context,
never from discovery.** Not certain? Then this does not apply: finish at §7 and
ask.

The procedure, the refusals, the provenance precision rule and the
import-is-not-reconciliation boundary:
`docs/guides/ARCHITECTURE-RECOVERY.md` §8a
(`continuum://guides/architecture-recovery`).

## 9. Deliver, and be decisive

Finish with the guide's §8 report: **Recovery · Architecture recovered · Key
evidence · Findings · Gaps**, and in a §8 repository **Verification · Import ·
Boundary**. State findings rather than fixing them, give every result **as the
tool reported it**, and report the observable surface and the evidence
coverage, never an element count alone. **Report completion as completion.**
Scope, stated: **one commit** — a recovery says nothing about history or
runtime; **one package** per repository; **no bindings, no presentation**.

Run the deterministic steps yourself and carry the recovery to completion —
and, under §8, through export, verify and import — unless a **concrete
blocking failure** stops you. Do not stop at a plan, and do not hand the user
commands you could have run. Do not fabricate architecture, evidence or a
verdict · do not pass over a warning · do not start reconciliation · **do not
perform acceptance, and do not report a complete recovery as anything less
than complete.**

## 10. After recovery — acceptance is a separate transition

**Optional, and the user's decision.** Offer it once — *the recovery is
complete; accepting it into `continuum/model/` is a separate decision the
recovery does not need* — and take no for an answer. On a yes, the command is
`continuum recover accept <export dir>` (it takes an export: a repository
accepting its own recovery exports for a consumer that exists).

- **No accepted model yet** — add `--initialize`: the one case where recovered
  bytes land directly. `--apply` installs them at `model/` and records the
  first checkpoint, whose `origin` names the export. **Do not author
  `continuum/model/` by hand** — that discards the recovery's element
  identities and provenance. Acceptance does not author
  `development-package.json` or `GOAL.md` (intent, not testimony);
  `development-package` finishes the package once an environment exists.
- **A model is already accepted** — the same command without `--initialize`
  derives a change against it and writes a **draft transition**; applying it is
  `continuum icp approve` and then `continuum icp materialize` (or
  `--analysis` first, to inspect).

**Read the refusals rather than working around them.** An `unknown` gap blocks
acceptance until a human disposes of it (`--gap <id>=recovered|accepted|defer:<why>`)
— never dispose on the user's behalf. A dirty tree is refused unless
`--allow-dirty`. **Do not argue from tooling**: a guard or CI gate that demands
`continuum/model/` presumes an acceptance this repository has not made; a
recovery-only repository failing it is a misconfiguration of the check, and
the acceptance decision stays untouched by it.

Once a model **is** accepted, offer **the CI gate** — copy
`<continuum>/tooling/ci/continuum-architecture.yml` into the target's
`.github/workflows/`, pinning `CONTINUUM_REF` — and **closing the discovery
loop**: a note in the target's `CLAUDE.md` naming the `continuum` MCP server
and the `understand-architecture`, `change-architecture` and
`recover-architecture` skills, so later sessions read the model instead of
re-deriving it from code.
