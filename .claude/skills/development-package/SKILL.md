---
name: development-package
description: Produce a Continuum DEVELOPMENT PACKAGE - the implementation-ready handoff for an implementation agent - from a new architectural idea, an existing semantic model, or a model plus an .adl-change - a Goal, the compiled semantic ADL, technology bindings for one environment, executable Given/When/Then acceptance scenarios traced to the model, derived process-graph traces and an implementation contract, assembled and verified by `continuum package build` and `continuum package check`. Use when asked for a development package, a handoff package, an implementation-ready spec, a development contract for Claude Code or another coding agent, or to turn an architecture (or an architecture change) into something a developer can build and prove. Never implements the application.
---

# Development package (intent → verified handoff)

**Load the `continuum` skill first** — knowledge routing, tool routing,
diagnostics and evidence discipline live there. This file is the
packaging flow only. The guide for the package format and its
authority boundaries is `docs/guides/DEVELOPMENT-PACKAGE.md`; the
worked example is `docs/examples/development-package/conversation-room/`.

The deliverable is a **verified package**, never "a folder of documents":
`GOAL.md` · `model/<system>.adl` + manifest · `model/<env>.adl-binding.json`
· `acceptance/scenarios/*.scenario.json` · derived `IMPLEMENTATION.md`,
`acceptance/README.md`, traces and `PACKAGE.json` — built by
`continuum package build`, which refuses to write anything
until the model compiles, the bindings apply and are complete, and every
scenario reference resolves; `package run` then executes the scenarios.

**One layout** (`docs/design/DESIGN-Development-Package-Layout.md`): the
package has the same shape standalone and as a repository's `continuum/`.
`model/` holds the semantic state (source, manifest, bindings, claims, a
shipped change, the derived projection and proof); `acceptance/` the
executable specification; the root the spec, the Goal and the derived
documents. The spec names only choices — `name`, `environment`,
`controls`, a change to apply — everything else is discovered from its
canonical location; never write `continuum/` *inside* a package, and
never a semantic file at its root.

```
Goal          why                         prose; adds no claim the model does not make
semantic ADL  what the system means       AUTHORITY inside the package
bindings      how THIS environment does it  the only place a technology may appear
scenarios     what must be true, executably  each names what it verifies
implementation  outside the package
```

## 0. In five commands

```
continuum package build  <dir>                 create: discover model/, acceptance/, GOAL.md (spec name kebab-case); compile, bind, derive
continuum package check  <dir>                 verify: CURRENT, or the file that drifted
continuum package run    <dir>                 execute the suite here (a run, never an acceptance)
continuum package accept <dir> --as <you> --verified-against HEAD   the initial acceptance (§9.5)
continuum package export <dir> --out <dir>     export: the portable package, verified on its own
```

`realize` and `bind refresh` maintain binding state (§3); a change is a
transition (§5a). You run these and relay the verdicts; the runnable proof is
`tooling/lifecycle-demo/tiny-approval/`.

## 1. Classify the mode, then establish the state

| You were given | Mode | The state the package rests on |
|---|---|---|
| a goal, an idea, requirements | **new system** | author it with `author-architecture`, then continue here |
| a manifest + `.adl` | **existing model** | that model, compiled — identities preserved, nothing re-minted |
| a model + `.adl-change` | **change** | **state B = apply(A, Δ)** — never A |

Establish the state mechanically before authoring anything else — and
establish it for the state the package will rest on, which in change mode is
**B**:

```
continuum compile <manifest> --state --json                                  A
continuum compile <manifest> --apply <change> --emit-source <dir> --state --json   B, materialized
```

`--state` answers the **attestation** and the **changeState** every binding
and the package must pin; the Environment ids come from the same compile.
`--emit-source` writes B as authored `.adl` + manifest, which is what you then
author and build against — a change whose base does not pin A is refused as
`stale`, and that is reported, never built around. Errors stop here; warnings
are carried into the package and explained (`continuum_diagnostics` for each
id), never silenced. Over MCP the same answer is `continuum_resolve_state
{manifestPath, changePath?}`. (`continuum package resolve` is **toolchain-owned**
and is not a verb the client CLI serves — it refuses with that in so many
words.)

### 1a. Assess the state the package rests on

Assess it (`continuum` skill §5a) and carry the result into the package's
delivery, not into the model:

- assess **A** to know what you are starting from, and **B** — the
  materialized target — to know what you are handing over;
- separate findings that **block** (a level not attained for a reason this
  package will not fix) from the obligations **this build is meant to
  satisfy** (the `UNKNOWN`s whose evidence is the implementation, the
  citations, or the executed suite);
- for each actionable finding carried into the handoff, give the reason code
  verbatim, the element ids from `warnings[].elements`, the evidence that
  would remove it, **which task produces that evidence**, and what the
  evaluator cannot establish at all —
  `docs/guides/CONFORMANCE-ASSESSMENT.md` §4.2, §5, §6;
- a level is **not** a build gate and **not** an approval. There is no
  minimum to clear, and `package build` does not consult one.

## 2. Author the Goal

`GOAL.md`: short, outcome-oriented, what success feels like. It must not
introduce a claim the model does not make; end it with the authority
line (the guide has the wording). If the user's intent needs a claim the
model lacks, that is an architecture change (`change-architecture`), not
a sentence in the Goal.

## 3. Author the bindings — technology lives only here

Do not transcribe pins or skeletons by hand — `continuum package realize
<dir> --environment <Name>` creates the pinned skeleton for an Environment
the model declares (no Binding entries: the technology is yours to write),
and after a semantic change `continuum package bind refresh <dir>` re-pins
every document, prunes Bindings whose target is gone and lists what is now
unbound (DEVELOPMENT-PACKAGE.md §6.2). `package repair` only repairs pins
and derived files; it never edits a Binding.

One `model/<environment>.adl-binding.json` per Environment the
package covers, pinning the attestation from §1 (`base.canonicalModel`),
plus a `*.vocabulary-claims.json` per vocabulary you name. Retrieve the
shape before writing it: `continuum://examples/order-platform.production.adl-binding.json`
and LANGUAGE-SPEC §16 (`continuum_knowledge {mode:"search", query:"binding document"}`).

- **Completeness is judged per Environment**: every owned Deployable,
  Resource, Agent, Event, MessageChannel and Service needs exactly one
  Binding — the build refuses an incomplete environment.
- Claims are the closed A.4.3 tokens only; a vocabulary is a dotted name
  (no hyphens); a technology identifier is free (`sqlite-wal`,
  `deepseek-chat`, `fake-model-provider`).
- **Secrets are locators, never values**: `{ "secretLocator": "env://DEEPSEEK_API_KEY" }`.
- **Fake provider first**: the local-development environment binds
  every Agent to the shipped fake provider; a real provider is a second
  environment's document. Provider changes (Anthropic → DeepSeek) are
  binding changes and must leave the attestation untouched.
- Budget and safety controls (`MODEL_PROVIDER=fake`, `MAX_AUTONOMOUS_TURNS`,
  `MAX_SESSION_MODEL_CALLS`, timeouts, retry ceiling, circuit breaker,
  pause/resume) go in the spec's `controls` as **recommended defaults**,
  labelled so — the architecture declares no number, and you invent none.

Mint Binding ids with `continuum_mint_ids`; never derive an id from a
name, never reuse a removed one.

## 4. Author the acceptance scenarios — executable, and traced

`acceptance/scenarios/<name>.scenario.json` (shape in the guide §4): a
kebab-case `name`, `verifies` (root-anchored qualified names or `#ids` —
Requirements, Services, Workflows, Agents, Messages, Guardrails,
BusinessRules), optionally the declared `test` element it realizes, the
`environment`, a `provider` script per Agent name (completion order by
`latency`, `output`, `silence`, `fail`, `failuresBeforeSuccess`,
`malformed`, a `budget`), and `given` / `when` / `then` as **step keys
with args** — the adapter binds keys, humans read `text`.

Scenarios prove **declared** behaviour: each claim traces to an element
(`verifies`), each ordering to a Reaction, a Workflow or a delivery
expectation. Where the model is silent (how a coordinator ranks, what a
malformed answer means beyond "the task failed") do not assert it — or
route it as a gap. Give the important scenarios a `trace.from` message
so the build derives the process-graph trace; an authored
`expectedTrace` is your expectation and is labelled as such.

A **reference adapter** (`acceptance/adapter.mjs`) may ship so the
package is self-verifying — an in-memory realization of the model's
services and workflow over the fake provider, labelled as not the
implementation. Without one, the build validates and does not execute,
and `PACKAGE.json` says `executed: false`.

## 5. Build, and read the refusal as evidence

```
continuum package build <dir>/development-package.json [--out <dir>]   (--out REQUIRED for a change)
continuum package check <dir> [--change <file>]
```

The pipeline (guide §6): resolve → compile → refuse on errors, carry
warnings → bind (applies? complete?) → resolve every scenario reference
→ derive traces, `BEHAVIOUR.md` and `IMPLEMENTATION.md` → write `PACKAGE.json`
(identities, digests, file inventory; no timestamps); `build` never
executes — `package run <dir>` executes the scenarios on the fake
provider, and `check` reproduces and byte-compares. A refusal names
its stage and the engine's ids: a `stale` binding means the attestation
moved; `resolves to no element` means the scenario claims something the
model does not declare — fix the claim or change the architecture,
never the reference. `check` rebuilds and byte-compares every derived
file, and with `--change` proves `package.changeState == change.base.state`.

## 5a. In a client repository — the package is `continuum/`, same shape

When the user's repository uses Continuum (`continuum/toolchain.json`
exists, or `continuum install` is wanted), the package root is
`continuum/` — the layout inside is exactly the standalone one, with
the semantic state in `continuum/model/` (`docs/guides/CLIENT-INSTALL.md`).
Prefer the CLI verbs over hand layout:

```
continuum package init continuum --system <Name> --package <dotted.name>   a skeleton that builds; author into model/
continuum package build continuum/development-package.json                  regenerate every derived file
continuum package run continuum                                             execute the scenarios (never part of build)
continuum package check continuum/                                          before trusting or handing off
continuum package accept continuum --as <you> --verified-against HEAD       the initial acceptance (§9.5)
continuum ledger record continuum --verified-against HEAD                   the checkpoint, resting on it
```

**A resulting package after a transition** is never assembled by hand:
`continuum icp materialize continuum/transitions/<id> --base continuum`
builds it through the full pipeline into the transition's own `result/` (the
next state stages inside its transition, never at a repository-root sibling)
and executes the complete suite — as the sanctioned target only once a person
has approved the transition, with `--analysis` building the same package for
inspection before that. Adoption (`continuum icp adopt <transition>
--verified-against HEAD`, when readiness says it is the awaited act) makes
that result current, carries `ledger.json`, `history/`, `transitions/` and
`gaps/`, and records the checkpoint with its transition in one atomic swap;
there is no adoption without the record. **Which act may happen next, and who performs it, is
`continuum icp ready <transition>`'s answer and never a sequence restated
here** — drive it with the readiness loop (`continuum` skill §1b) and hand
the person one command at a time. Say which checkpoint the package now is
(`continuum ledger current`) — the sequence is a label; the digests are the
identity.

With the MCP server connected, the same verdicts are one call away and
read-only: `continuum_package_check {packageDir, changePath?}` and
`continuum_resolve_state {manifestPath, changePath?}` relay the CLI's
`--json` verbatim. A path argument means the machine the surface reads — which
is yours on the local bridge and not yours on a hosted one (`continuum` skill
§0).

## 6. Deliver

- **the export, when the package leaves the repository**: `continuum
  package export <dir> --out <dir>` — exactly the inventory, never the
  workspace (`gaps/`, ledger, `transitions/`, pins); refused unless the
  source checks; verified on its own with the same attestation and
  changeState. The receiver needs only the `continuum` CLI: `package
  check`, `package build`, `package run` on the export alone;
- the package directory and `PACKAGE.json` — say "verified" only because
  `build` returned ok; without a toolchain, label everything **UNCOMPILED**;
- the attestation and changeState, the environment, the scenario count
  and execution result, the warnings explained;
- **the assessment of the state being handed over** (§1a): the subject
  digest, the attained level, and the specification / profile / policy /
  evaluator identities — plus the obligations the implementer's work is
  expected to satisfy, each with its reason code and producing task. Say
  plainly that this is **model** conformance and establishes nothing about an
  implementation that does not yet exist;
- the behavioural projection, read before handing off — `BEHAVIOUR.md`,
  or `continuum preview <spec>` for a state not yet built (and
  `--change <file>` for a proposed change): walk the owner through the
  order the model establishes, the steps it leaves unordered (their
  concurrency is an implementation policy to record, never architecture)
  and each underspecified-causality finding, which is either declared
  now or recorded as a gap — "valid, but not how I want it to behave"
  is decided here, before implementation cost;
- what the package deliberately leaves to the implementation
  (policies the model does not declare) and the gap protocol the
  contract prescribes (`continuum/gaps/<date>-<slug>.md`, then an
  `.adl-change` pinned to this changeState);
- assumptions and open questions — outside every artifact.

## 7. Scope limits — state them

This skill does not implement, deploy, or generate application code.
The semantic model carries no technology, no budget number and no test
runner: those are bindings, controls and the shipped acceptance runtime.
The derived documents are never authority — if `IMPLEMENTATION.md` and
the `.adl` disagree, the tool has a defect and the model governs. ADL 1.x
is frozen: an intent the language cannot express is recorded as a gap,
never modelled around.
