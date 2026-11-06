# Execution log

All times are 2026-11-06 UTC. "Impl" is the implementation agent (this session). "Eval" is the independent evaluator session.

| Time | Who | What | Outcome |
|---|---|---|---|
| 11:10 | Impl | Baseline at `984531a` | Build and tests green; Continuum gate never ran (billing). `BASELINE.md` |
| 11:21 | Impl | Continuum gate moved to the self-hosted runner (`20da749`) | Failed: Restricted execution policy, WSL bash |
| 11:26 | Impl | CI fixes (`b1a4939`) | **First evaluated gate: PASS** ([42210211471](https://github.com/avlon-technologies/sql-investigator/actions/runs/42210211471)); `.NET` green ([42210211428](https://github.com/avlon-technologies/sql-investigator/actions/runs/42210211428)) |
| 11:30 | Impl | Gap disposition, `connection_remove` result and description, tests (`6cc3dd3`) | 125 unit tests pass |
| 11:33 | Impl | Seeded `EvalCommerce` and `EvalFulfillment` with random login passwords; derived the truth | Matches the design |
| 11:37 | Impl | **Locked** the truth, rubric and fixture by SHA-256 | `FIXTURE-MANIFEST.md` |
| 11:39 | Impl | `eng/release.ps1` and user guide (`304c0a9`); first release built | Reproducible within one checkout |
| 11:40 | Impl | Release validation on a clean folder, with .NET hidden | **Server crashed** (R1); fixed (`2c345db`) |
| 11:45 | Impl | CI release workflow (`db43be2`) | The CI hash differed from the working-copy build (R4) |
| 11:47 | Impl | First evaluator launch attempt | **Blocked** by the auto-mode safety check (nested agent with a shell) |
| 11:50 | Impl | Phase E provisioning challenges | Piped password failed (R2); fixed (`75cb113`) |
| 11:57 | Impl | Phase E MCP battery, run 1 | 76/79: U3 product defect (R3); B4 and K1 harness defect |
| 12:01 | Impl | Phase E run 2 on `5c67c6c` | **79/79** |
| 12:05 | Impl | Release from a clean worktree (`0c09c70`) | Local = CI = `6ad28140…` ([42215057504](https://github.com/avlon-technologies/sql-investigator/actions/runs/42215057504)) |
| 12:06 | Owner | Approved the evaluator launch when asked | — |
| 12:07–12:10 | **Eval** | Phase 1: verify, install, provision two aliases, register (session `5a27ec3f…`, model `claude-opus-5-5`, 22 turns, 184 s) | `READY FOR RESTART`. 12 onboarding problems, chiefly the server-name clash and project-scope approval |
| 12:11 | Impl | Audited the Phase 1 transcript | Clean. One self-reported `%TEMP%` write |
| 12:11–12:15 | **Eval** | Phase 2: discovery and investigation through `sql-investigator-eval` (35 turns, 221 s; 14 queries, 2 validate probes) | `DONE`. `REPORT.md`, investigation `inv-f68def5303ee`, 2 proposed relationships |
| 12:15 | Impl | Audited the Phase 2 transcript | Clean: 0 hits on forbidden material, 0 write-shaped SQL, 0 review calls, 0 secrets |
| 12:16 | Impl | Committed the truth (hashes match the lock); compared and scored | **80.8 / 100**. `FINDINGS.md` |
| 12:12–12:20 | Impl | Fixed the evaluator's findings E1–E8 (`6103f39`, `492e909`, `6551d99`) | 130 unit, 18 integration, 12/12 acceptance |
| 12:20 | Impl | Post-remediation release `6551d99`, `188ac758…` | — |
| 12:21 | Impl | Post-remediation evaluator launch | **Blocked** by the auto-mode safety check; owner asked again |

## Transcript audit method

[`evidence/`](evidence/) does not include the transcripts. They contain the DBA email the evaluator read, with fixture passwords, so they stay on the machine at `scratchpad\evaluator-logs\phase{1,2}.jsonl`.

An audit script checked every tool-call input for:

- paths outside the workspace
- the repository, the scratchpad, the dev data folder, the runner, transcripts and grading files
- write-shaped SQL
- `knowledge_review` / `connection_remove` calls
- any password

| Phase | Tool calls | Forbidden-material hits | Paths outside workspace | Write-shaped SQL | Review/remove calls | Secrets in calls |
|---|---|---|---|---|---|---|
| 1 | 21 (Read 2, PowerShell 14, Bash 3, Write 2) | 0 accesses. The 1 match was the notes quoting a path printed by `claude mcp list` | `%TEMP%` (self-reported smoke test), env-var references | 0 | 0 | 0 |
| 2 | 34 (MCP 31, PowerShell 1, ToolSearch 1, Write 1) | 0 | none | 0 of 16 | 0 | 0 |
| 12:22 | Owner | Approved the re-run when asked | — |
| 12:24–12:26 | **Eval 2** | Phase 1 on `6551d99` (session `ea4a1db3…`, `claude-opus-5-5`, 16 turns, 138 s) | `READY FOR RESTART`. Used the documented rename (`sql-investigator-dev`). The write of `.claude\settings.local.json` was refused; it did not work around it |
| 12:26–12:30 | **Eval 2** | Phase 2 (31 turns, 226 s; 13 queries) | `DONE`. `inv-e4a0f74843c4`, 3 proposals (one superseded by the evaluator) |
| 12:31 | Impl | Audited both transcripts; checked the figures against the fixture with the read-only logins | Clean, and the figures are correct |
| 12:33 | Impl | Scored with the locked rubric | **94.8 / 100** post-remediation. The original 80.8 is unchanged |
| 12:40 | Owner → Impl | At the owner's direction: `continuum package accept … --verified-against HEAD`, then `ledger record` | Fact `cd242e6a…` at `bdd65c2`, 12/12, checkpoint 0002 verified |
| 12:43 | Owner → Impl | At the owner's direction: `enabledMcpjsonServers` for `sql-investigator-dev` in the coldstart-2 workspace | Still "Pending approval" until the folder is trusted interactively |

## Post-remediation audit

| Phase | Tool calls | Forbidden-material hits | Paths outside workspace | Write-shaped SQL | Review/remove calls | Secrets in calls |
|---|---|---|---|---|---|---|
| 1 | 15 (Read 2, PowerShell 10, Write 3) | 0 accesses. The 1 match is the notes quoting the path `claude mcp list` printed | none written | 0 | 0 | 0 |
| 2 | 30 (MCP 28, ToolSearch 1, Write 1 for `REPORT.md`) | 0 | none | 0 of 13 | 0 | 0 |
