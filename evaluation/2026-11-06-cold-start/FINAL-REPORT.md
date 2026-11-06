# Final report: governance closeout, release and cold-start evaluation

Date: 2026-11-06. Repository `avlon-technologies/sql-investigator`, branch `main`.

## Overall: PARTIAL

The engineering, safety and evaluation work is complete and evidenced. It is PARTIAL for four reasons, each needing the owner or a human:

- The remediated code has not been accepted as a new Continuum state.
- The release is not published.
- Project-scope MCP approval still needs a human in an interactive session.
- No clean Windows user profile was available for the install test.

Nothing here was accepted, published or approved on the owner's behalf.

## What each status means here

| Status | Scope | Evidence |
|---|---|---|
| **Implemented** | The `connection_remove` clarification, fixes R1–R5, the release script and workflow, the user guide, and fixes E1–E8 | `REMEDIATION.md`; commits below |
| **Tested** | 130 unit tests, 18 integration tests and 12/12 acceptance scenarios at `6551d99`. The safety challenge passed 79/79 MCP checks and 7/7 console checks on the installed release. Each fix has a regression test that fails before and passes after | `SECURITY-RESULTS.md`; [.NET run 42217030860](https://github.com/avlon-technologies/sql-investigator/actions/runs/42217030860) |
| **Independently validated** | Two cold-start evaluations, each by a separate Claude Code session with fresh context and no access to the repo, the grading material or the implementation's data | `FINDINGS.md`; `EXECUTION-LOG.md` |
| **Governance accepted** | Only **S0** (`sha256:fdbddf00…` at `5fdd905`, by mike@avlon.ca, recorded before this mission). The remediated code is **moved-conforming**, not accepted. ADRs 001–006 were already accepted; decision integrity is INTACT | `GOVERNANCE.md` |
| **Released** | **Built, reproducible, not published.** The zip exists as a local build and a CI workflow artifact. There is no GitHub Release | `RELEASE.md` |

## Governance

- **S0** already existed: acceptance at `5fdd905`, ledger checkpoint 0001 **verified**, and `continuum package check` **intact**. It was not re-recorded.
- **Continuum CI now evaluates architecture.** The gate had never run, because GitHub billing refuses hosted runners. It runs on the self-hosted runner `sqlinv-local` with `guard ci --require-judgment`. The first evaluated PASS was [run 42210211471](https://github.com/avlon-technologies/sql-investigator/actions/runs/42210211471). It also passed at `6551d99`: [run 42217031144](https://github.com/avlon-technologies/sql-investigator/actions/runs/42217031144).
- **Connection-removal gap resolved** with option 1 and no model change. MCP removal keeps the secret, as the accepted model requires. The tool now says so: `secretDeleted: false` and an `action` naming `SqlInvestigator.exe remove`. See `continuum/gaps/2026-11-06-remove-connection-secret.md`.
- **No model, binding, scenario or ADR was changed**, and no derived file was hand-edited.

## Engineering

| Fix | What it was | Found by |
|---|---|---|
| R1 | Server crashed at start-up when its data folder was under `C:\` | release validation |
| R2 | A password piped from PowerShell 5.1 never worked (byte-order mark) | safety challenge |
| R3 | Redaction applied to JSON text could corrupt a result. It failed closed, with no leak | safety challenge |
| R4 | The release built from a working copy was not reproducible | release validation |
| R5 | `connection_remove` was ambiguous about the stored secret | the gap |
| E1–E8 | Onboarding documentation, `--help`, and the usage text | the first cold-start evaluator |

R1–R4 were fixed before the first evaluator started. E1–E8 were fixed after it, and checked by the second.

**Release:** `eng/release.ps1 -Verify` builds twice from a clean worktree of HEAD and fails unless both zips have the same hash.

| Release | SHA-256 | Local | CI |
|---|---|---|---|
| `0c09c70`, evaluated first | `6ad28140968646fc987a27312b04398d05e124afab53e6c616737dfbd8c6ad62` | ✓ | [42215057504](https://github.com/avlon-technologies/sql-investigator/actions/runs/42215057504) ✓ |
| `6551d99`, post-remediation | `188ac758994b1357b255c2c9fd9e3eeffc25bd1c8cf3172e82017087d85971b8` | ✓ | [42218752716](https://github.com/avlon-technologies/sql-investigator/actions/runs/42218752716) ✓ |

## Cold start

| | Original | Post-remediation |
|---|---|---|
| Release | `0c09c70` | `6551d99` |
| Evaluator session | `5a27ec3f…` (claude-opus-5-5) | `ea4a1db3…` (claude-opus-5-5) |
| Seeded findings | 9 / 9 | 9 / 9 |
| Ambiguities | 2 / 3 (A1 missed: SO-100060 asserted as missing at High confidence) | 3 / 3 |
| Traps asserted | 0 / 6 | 0 / 6 |
| Installation | 5 / 10: undocumented name clash, approval, `--help` | 9 / 10: approval still needed a human |
| Transcript audit | clean | clean |
| **Score (locked rubric)** | **80.8** | **94.8** |

The rubric and the truth were locked by SHA-256 before the first launch and were not changed. Each evaluator was launched once. The fixture and brief were not tuned between runs, apart from the workspace path and server name.

**Read the 94.8 with care.** The installation and recovery gains follow from the fixes. The A1 gain does not: no fix targeted it, so it is evaluator variance, and two runs cannot separate product change from variance.

## Security

- **79/79 MCP safety checks and 7/7 console checks** passed against the installed release.
- The checks cover:
  - the allowlist and the read-only principal, each tested on its own
  - scope
  - cross-database access
  - limits
  - redaction
  - system functions and catalog views
  - secrets in arguments
  - removal semantics
- Neither evaluator attempted a write. Neither called `knowledge_review` or `connection_remove`, accessed the repo or the grading files, or had a secret in a tool call.
- All databases were disposable synthetic fixtures. No production or employer data was used. No credentials or result rows are committed. The transcripts stay on the machine, because they contain the fixture passwords the evaluator read from its brief.

## Remaining blockers (owner actions only)

1. **Accept the remediated revision.** This is optional. Without it, the guard stays moved-conforming, which is a PASS.
   ```powershell
   continuum package run continuum
   continuum package accept continuum --as mike@avlon.ca --verified-against HEAD
   ```
2. **Publish the release**, if you want one. No publication route exists yet. The CI artifact expires after 30 days.
3. **Approve the project-scope MCP server** in an interactive Claude Code session. An agent cannot do it for itself. Both evaluations used `--mcp-config` in its place.
4. **Clean-profile install.** Optionally, install once on a fresh Windows user profile. This session was not elevated, so it could not create one.
5. **GitHub billing.** Hosted runners stay unavailable, so both CI gates depend on the PC's self-hosted runner being online.
6. **Optional model change:** a way to amend or withdraw a relationship proposal (E9 and N5) would need an `.adl-change` on `KnowledgeService`.

Open minor items from the second run are N1, N2 and N4 in `REMEDIATION.md`. N4 is an untested inference that PowerShell 5.1 would corrupt a non-ASCII piped password.

## The three most important findings

1. **Redaction could corrupt results (R3).** Masking secrets in serialized JSON text broke the JSON. The tool then returned a generic error instead of the actionable one, and any result row with such text could fail the same way. It failed closed, with no leak. It is now fixed by redacting values before serialization, with regression tests.
2. **Two installation defects would have stopped real users (R1, R2).** The server crashed when its data folder sat directly under `C:\`. The documented way to pipe a password from Windows PowerShell 5.1 never worked. Both were found by installing the release, not by the test suite, and both were fixed before the evaluator ran.
3. **Onboarding is the weakest part of the product, not investigation.** Both evaluators found every seeded inconsistency. The largest point loss was installation: a server-name clash with another registration, and a project-scope approval that needs a human. The documentation fixes took installation from 5/10 to 9/10. The approval step remains.
