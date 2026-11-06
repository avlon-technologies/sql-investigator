# Baseline (Phase A)

**Taken:** 2026-11-06, 11:10–11:20 UTC · **Commit:** `984531a954f64e8f826546ee7d18c98a4c165c13` (`main`, clean working tree) · **By:** the implementation agent (Claude Code, Opus 5.5)

"Checked now" means the command was run during this mission at this commit. "Reported" means it is taken from earlier evidence (`docs/IMPLEMENTATION-REPORT.md`, git history) and was not re-run.

## Results

| Item | Result | Source |
|---|---|---|
| Working tree | clean, `main` = `origin/main` | checked now (`git status`) |
| Build `dotnet build -c Release` (warnings are errors) | 0 warnings, 0 errors | checked now |
| Unit tests | 123 passed, 0 failed | checked now |
| Integration tests (disposable SQL Server fixtures) | 18 passed, 0 failed | checked now |
| Acceptance `continuum package run continuum` | 12 passed, 0 failed, 12 executed | checked now |
| `continuum package check continuum` | `intact`, every check evaluated over the attested state | checked now |
| `continuum ledger current continuum` | checkpoint 0001 `initial`, acceptance fact `sha256:fdbddf00…` at `5fdd905`, **verified** | checked now |
| `continuum guard ci` (local) | PASS: lifecycle, architecture, implementation, hygiene all ok; implementation moved-conforming since `5fdd905` | checked now |
| S0 (initial accepted state) | **already recorded** at `5fdd905` by mike@avlon.ca (commit `9394525`) | checked now (ledger) and reported (git) |
| ADRs | ADR-001..006 **accepted** (Host, 20 events) | checked now (`continuum decision list`) |
| Decision ledger integrity | INTACT: 20 Host-signed lines verified offline (key `decisions-dev-2026-11-04`) | checked now (`continuum decision integrity`) |
| Decision export vs Host | each ADR proposed by `agent:claude-code` (delegation) and accepted by `agent:remote-mcp` (relay) under `an-approver-who-did-not-author-it` | checked now (`continuum decision verify`) |
| `continuum ci setup` | READY: Host variable set (organization), `CONTINUUM_HOST_API_KEY` set (organization; value not testable from a shell), feed token set | checked now |
| GitHub Actions: `.NET` | green on the last 3 pushes, on the self-hosted runner `sqlinv-local` | checked now (`gh run list`) |
| GitHub Actions: `Continuum` | **red on every run**: *"The job was not started because recent account payments have failed or your spending limit needs to be increased"*. The job targeted `ubuntu-latest`, so the architecture was **never evaluated** in CI | checked now (run 42207983213) |
| Outstanding gaps | 1 open: `continuum/gaps/2026-11-06-remove-connection-secret.md` | checked now |
| Distributable executable | **none**. No GitHub release and no release script; the README installs with `dotnet publish` from source (needs the SDK) | checked now (`gh release list`, repo) |
| Conformance | L1 attained; L2 undetermined (`precondition/no-correspondence-map`) | reported (implementation report), not re-run |
| Production data | none connected; BDL `SensitiveDataPolicy` still open | reported |

## What the baseline meant for the mission

- **S0 needed no action.** It already exists under the owner's own acceptance. Nothing in this mission re-records or alters it.
- **CI was not evaluating architecture.** The cause is billing for GitHub-hosted runners, not credentials. The owner had already moved the `.NET` job to the self-hosted runner for the same reason (commit `b2d5fe1`).
- **The gap was open** but marked as blocking nothing.
- **There was no release.** A cold-start evaluator could only install by building from source.
