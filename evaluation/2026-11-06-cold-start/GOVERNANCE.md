# Governance closeout (Phase B)

## B1. Initial accepted state (S0)

**Already recorded before this mission. Nothing was re-recorded.**

| Fact | Value |
|---|---|
| Acceptance | `sha256:fdbddf00addb…` by **mike@avlon.ca** at `5fdd905713f0`, 2026-11-06T10:49:31Z, suite 12/12 |
| Ledger | checkpoint 0001 `initial`, `continuum ledger current continuum` → **verified**, "every check evaluated over the recorded state" |
| Package | changeState `sha256:def71c2de556c87d6fcde76ced19b541be633f83f7a19d97098a90742d156a3a`, attestation `sha256:df885cefc7aba2905bf00c676755cd0ec8b3ae6170da605b600ee44c2c7d3a77`; `continuum package check continuum` → **intact** |
| Commit | `9394525 continuum: record the initial accepted state (S0) and ledger checkpoint 0001` |

### The remediated revision is accepted (owner-directed)

The mission changed implementation code (see `REMEDIATION.md`) but not the model, bindings or acceptance scenarios. Until the acceptance below, `continuum guard ci` reported the implementation as **moved-conforming** from the accepted `5fdd905`.

During the mission the agent did **not** accept on its own initiative. The `continuum` CLI is signed in as the owner, so doing so would have been impersonation. At 12:40Z the owner directed it in the session ("Let's accept remediated code as a new continuum state"), and the agent then ran:

```powershell
continuum package accept continuum --as mike@avlon.ca --verified-against HEAD
continuum ledger record continuum --verified-against bdd65c293aa0
```

| Fact | Value |
|---|---|
| Acceptance | fact `sha256:cd242e6a43bc522bf81e75d95116c80d6d0a9e7a75412003778b094e84550e52` by **mike@avlon.ca**, 2026-11-06T12:40:42Z |
| Implementation | `bdd65c2`. Its program code is identical to the evaluated release `6551d99`; only `evaluation/` differs |
| Suite | 12/12, run by `accept` itself on a clean tree. The owner's unrelated uncommitted work was stashed for the run and restored afterwards |
| State | changeState `sha256:def71c2d…`, the same semantic state as S0. No model change |
| Ledger | checkpoint 0002, `continuum ledger current continuum` → **verified**, resting on this acceptance |

## B2. Continuum CI architecture gate

**Now evaluates architecture, and passes with a Host judgment.**

| | Before | After |
|---|---|---|
| Runner | `ubuntu-latest` (GitHub-hosted) | `[self-hosted, Windows, X64, sqlinv-local]`, the owner's existing runner (precedent: `b2d5fe1`) |
| Outcome | job never started: GitHub billing refused hosted jobs. Annotation: "recent account payments have failed or your spending limit needs to be increased" | `guard ci --require-judgment` → **PASS**, all four dimensions ok |
| Credentials | `CONTINUUM_HOST` (variable) and `CONTINUUM_HOST_API_KEY` (secret) were **already set at organization level**. Only names were checked; no value was read, printed or changed | unchanged |

Changes to `.github/workflows/continuum.yml` (`continuum ci setup` reports it as "managed, edited here", which is allowed):

1. `runs-on` moved to the self-hosted runner (`20da749`).
2. `PSExecutionPolicyPreference: Bypass` for the job's processes only. The runner had restarted without an override and the PC's policy is Restricted, so every step script was refused. No machine policy was changed.
3. A step puts Git Bash ahead of WSL's `C:\Windows\System32\bash.exe` on the job's PATH (`b1a4939`).

The same execution-policy fix was applied to `.github/workflows/dotnet.yml`.

Evidence:

- The first evaluated gate: [run 42210211471](https://github.com/avlon-technologies/sql-investigator/actions/runs/42210211471). Its step "Check the architecture against Host" ran with `CONTINUUM_HOST=https://continuum.dev.avlon.ca` and the API key masked, and printed `guard ci: PASS [package]`, with lifecycle, architecture, implementation and hygiene all ok.
- Every push since has a green `Continuum` run. The final runs are listed in `FINAL-REPORT.md`.
- `--require-judgment` means a green job is a Host judgment (exit 0), not a skipped evaluation (exits 2 and 3 fail the job).

**Owner follow-up (optional):** GitHub-hosted runners stay unavailable until the billing issue is fixed. Both gates now depend on the PC's runner being online.

## B3. Connection-removal gap

**Resolved, option 1, no model change.** Full text: `continuum/gaps/2026-11-06-remove-connection-secret.md`.

- **Consistent with the accepted model.** `CredentialVault` [secret-store] says the developer places the secret at the local console and "services only read it". `ConnectionRegistry` writes only `ConnectionCatalog`, with `on RemoveConnection -> none`. Deleting a blob is a write to a secret-store, which the model reserves to the developer at the console. Making MCP removal delete it would need a service `writes` edge to a secret-store, forbidden by `adl/architectural/secret-store-write`, and an ADR.
- **The ambiguity was in the tool surface, not the model.** The `connection_remove` description now says plainly that it does **not** delete the stored secret, and names `SqlInvestigator.exe remove --alias <alias>`. The result now carries `secretDeleted: false` and an `action` with that command. The README and the user guide say the same.
- **Regression tests:**
  - `Removing_an_alias_over_mcp_keeps_its_secret_until_the_console_removes_it`: the blob survives MCP removal, current-user-only, and the console deletes the orphan.
  - `Connection_remove_says_it_keeps_the_secret_and_names_the_console_command`.
- **Live check** against the installed release: `connection_remove` on an unknown alias returned `{"removed":false,"secretDeleted":false,"action":"…SqlInvestigator.exe remove --alias nope"}`.
- Commit `6cc3dd3`.

## Decisions and model

- No ADR was proposed, accepted, rejected or superseded. `continuum decision integrity` → INTACT (20 lines).
- No model, binding or acceptance file was edited. `continuum package check continuum` → intact after every commit, and the pre-commit and pre-push guard passed each time.
- The self-contained distribution stays within `BindLocalMCPServer` (`runtime: net10.0`, `os: windows-x64`, `transport: mcp-stdio`). The binding does not fix the deployment mode; "framework-dependent" was only an implementation note (`docs/IMPLEMENTATION-NOTES.md`). No binding was changed.
