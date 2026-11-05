---
name: reconcile-architecture
description: Interpret Architecture Guard evidence for a client repository whose continuum/ package, ledger or transitions have drifted, and carry the reconciliation as DRAFTS ONLY - the mechanical repair, the binding-only transition (the one case the tool adopts), the rebased transition, or a draft .adl-change derived by diff for a human to accept - never a verdict the tools did not derive. Use when a guard hook or CI blocked with package-drift, binding-drift, acceptance-drift, base-drift or unexplained, when `continuum reconcile plan` says anything but clean, when a pending transition no longer pins the current state, or when someone asks what to do about drift between the architecture and the implementation.
---

# Reconcile an architecture (evidence → draft, never acceptance)

**Load the `continuum` skill first** — knowledge routing, tool routing,
diagnostics and evidence discipline live there. This file is the
reconciliation flow only. The client surface is
`docs/guides/CLIENT-INSTALL.md` (§2 the guard, §2a reconciliation, §4
transitions, §5 the ledger); the categories are
`docs/guides/DEVELOPMENT-WORKFLOW.md` §4.

The deliverable is a **proposal a human can accept**, never an
accepted state: the guard gathers evidence, `continuum reconcile`
derives the disposition and scaffolds the draft, this workflow reads
both and explains them, and only `continuum reconcile adopt` (bindings
only), a transition driven through its lifecycle, or a human turn a
proposal into a checkpoint. **Implementation never silently becomes
architecture.**

**Drive every transition a reconciliation produces with the readiness loop**
(`continuum` skill §1b): `continuum icp ready <transition> --json` says the
next step, its actor and whether it is ready. You perform the `agent` steps
(check, rebase, materialize, commit, adopt); the person records the `human`
ones — one command at a time, the awaited one only, with one sentence on what
they are judging — and you read readiness again after each. Never hand over
`approve → materialize → accept → adopt` as a sequence.

## 1. Start from the evidence, not from the code

```
continuum guard pre-push --json          the full judgment: five outcomes (PASS, FAIL, INDETERMINATE — exit 2, or 3 where required — and NOT APPLICABLE), four dimensions, every finding
continuum reconcile plan continuum/      the evidence and the disposition, read-only
continuum package check continuum/       what drifted, file by file
continuum ledger verify continuum/       lineage, and whether the live state is recorded
```

With the MCP server connected, `continuum_guard {repoDir, hook}` and
`continuum_reconcile {packageDir}` return the same judgments read-only.
Quote the guard's `classification`, every failing finding, and the
plan's `disposition` verbatim. Neither says "semantic drift" — and
neither do you until a human has accepted a draft that says so.

Within the Guard's architecture dimension the reconciliation evidence is four rows, and none reads
application code: the **checkpoint delta** (which of semantic,
bindings, acceptance, derived moved since `history/NNNN`); the
**implementation delta** (files outside `continuum/` that changed since
the commit the checkpoint was verified against — a count and names);
**acceptance execution** on the fake adapter; the **recorded gaps**
under `continuum/gaps/`. Everything you conclude from them is an
interpretation and is labelled as one.

## 2. One disposition per plan

| `reconcile plan` says | What you do (the tool proves every step) |
|---|---|
| `clean` | nothing to reconcile; if the guard still blocks, the finding is elsewhere — quote it |
| `mechanical` — only derived files are stale | `continuum package repair continuum/`, then `ledger verify`; the rebuild is the proof. If the compile fails, the authored model is the defect: fix it as architecture (`change-architecture`), never by editing derived files |
| `binding-reconciliation` — bindings moved, the attestation did not | `continuum reconcile draft continuum/` → a binding-only transition; when `icp check` verifies it and it **reproduces the working tree**, `continuum reconcile adopt continuum/ --transition <draft> --verified-against HEAD` records it. This is the one automatable case; the tool proves same changeState, same binding digests, same inventory before adopting |
| `acceptance-draft` — the contract moved | `reconcile draft` scaffolds the acceptance-only transition; a changed contract is a changed meaning: a person approves and later accepts it, and you drive the rest (§1b of the `continuum` skill). Until then the implementation is wrong and the recorded suite is right |
| `semantic-draft` — the semantic state moved outside a transition | `reconcile draft` derives the `.adl-change` **by diff** between the checkpoint and the working tree (element identity; verified by apply) and labels it **DRAFT — requires acceptance**. `reconcile adopt` refuses it. You explain what it declares and what the evidence says; a human accepts or rejects |
| `no-checkpoint` | nothing recorded: verify and `continuum ledger record continuum/ --verified-against HEAD` first |
| guard `base-drift` — a pending transition pins another state | after the reconciliation is recorded: `continuum icp rebase <icp> --onto continuum/`; read the refusal — only the refused operations need a Δ', and you draft those |
| guard `unexplained` — a malformed artifact, or a failure no row accounts for | report it as a gap; propose nothing |

## 3. A semantic proposal is a draft with a name on it

Whether the draft came from `reconcile draft` (the working tree moved)
or from evidence that the model is silent (a failing scenario, a
recorded gap under `continuum/gaps/`, a human's report):

1. the `.adl-change` is the smallest one that declares it, pinned to
   the checkpoint's `changeState` (`continuum ledger current
   continuum/`), ids from `continuum_mint_ids`, never derived from a
   name, never a removed one reused — a derived draft already satisfies
   this; a hand-written one must;
2. it lives in a transition (`continuum icp init … --semantic changed`
   or the draft's directory) and `continuum icp check` derives whether
   it applies — you never write "verified";
3. `REASON.md` and your report carry **DRAFT — semantic reconciliation,
   requires acceptance**, the evidence it rests on, and what the
   implementation does meanwhile;
4. stop. Whether the draft proceeds is the person's call: it proceeds when
   they **approve** it — the first human step readiness awaits. Until that
   approval exists, do not materialize it as the sanctioned target and do not
   implement against it; after it, drive the transition with §1b.

Never invent architecture to make the evidence tidy: a claim the
evidence does not establish stays out of the draft and goes into the
report as unknown. A derived diff can only ever say what the working
tree already declares; if that is not what anyone intended, the right
proposal is to revert the tree to the checkpoint, not to accept.

## 4. Deliver

- the classification, the disposition and the findings, quoted;
- the disposition per finding, with the exact command that proves it;
- every draft, labelled as a draft, with its `icp check` result and
  whether it reproduces the working tree;
- what remains unexplained.

## 5. Scope limits — state them

You read the guard's evidence, the plan and the model; you do not read
application source to infer what the architecture "really" is — that
is recovery, and its output is also a draft. The implementation delta
is a count of files, never a reading of them. Without a toolchain, no
disposition may be called proven; label everything **UNCOMPILED**.
