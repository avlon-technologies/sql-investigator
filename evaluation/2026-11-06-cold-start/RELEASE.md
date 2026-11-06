# Release artifact (Phase C)

**Status: built, verified and reproducible. Not published.** No GitHub Release exists. The repository has no approved publication mechanism, and publishing is the owner's decision. The artifact is kept as a local build and as a CI workflow artifact.

## The artifact the cold-start evaluator received

| | |
|---|---|
| File | `SqlInvestigator-1.0.0-win-x64.zip` (271 files, about 41.5 MB) |
| SHA-256 | `6ad28140968646fc987a27312b04398d05e124afab53e6c616737dfbd8c6ad62` |
| Version | `1.0.0+0c09c708adc2…` (`--version`), plus `VERSION.txt` in the zip |
| Commit | `0c09c70` |
| SDK | .NET SDK 10.0.403 |
| Platform | Windows x64, **self-contained** .NET 10, so no .NET install is needed |
| Contents | the program and runtime, `README.md` (= `docs/USER-GUIDE.md`) and `VERSION.txt` |
| CI build | [Release build run 42215057504](https://github.com/avlon-technologies/sql-investigator/actions/runs/42215057504), artifact `SqlInvestigator-win-x64`, kept 30 days |

## Reproducibility

`eng/release.ps1 -Verify` builds twice and fails unless both zips hash the same. Each build:

- uses a **clean git worktree of HEAD**
- compiles deterministically with `ContinuousIntegrationBuild` path mapping
- uses the commit SHA as the source revision
- writes zip entries in ordinal order, each timestamped with the commit time

| Build of `0c09c70` | SHA-256 |
|---|---|
| Local, `C:\projects\…`, two builds | `6ad28140…c6ad62` (both) |
| CI self-hosted runner, `C:\actions-runner\…`, two builds | `6ad28140…c6ad62` (both) |

The local and CI builds match from different checkouts and paths. The earlier build of `db43be2` also matched between a fresh local worktree and CI (`f6492977…`). Only a build from the developer's working copy differed, because that copy had LF line endings where a clean checkout gets CRLF. That is why the script now always builds from a worktree (`0c09c70`).

Both builds ran on the same physical PC, because the CI runner is self-hosted there. Reproducibility on a different machine or SDK patch has not been tested.

## Validation on a clean environment

No clean Windows user profile was available: this session is not elevated, so it cannot create a local user. Equivalent isolation was used instead:

- a fresh folder outside the repo and the user profile (`C:\sqlinv-eval\smoke`, `C:\sqlinv-eval\safety`, and the evaluator's `C:\sqlinv-eval\coldstart-1`)
- a fresh `SQLINVESTIGATOR_HOME`
- **.NET removed from `PATH`**, with `DOTNET_ROOT` pointed at a folder that does not exist

| Check | Result |
|---|---|
| `Expand-Archive`, then `SqlInvestigator.exe --version` without .NET on PATH | prints `1.0.0+<sha>` |
| `SqlInvestigator.exe list` | `No aliases are provisioned.` |
| MCP `initialize` and `tools/list` over stdio | server `sql-investigator 1.0.0`, exactly the 11 tools, 0 stderr lines |
| `connection_list`, `connection_remove` (unknown alias) | `{"connections":[]}`, and `{"removed":false,"secretDeleted":false,…}` |
| Provisioning, all 79 safety checks, the cold-start evaluation | against this installed zip (see `SECURITY-RESULTS.md`, `EXECUTION-LOG.md`) |

**The first validation failed, and the failure was fixed before the evaluator started.** With the data folder under `C:\sqlinv-eval\…`, the MCP server crashed at start-up (`UnauthorizedAccessException` in `SecureFiles.EnsurePrivateDirectory`). Details are in `REMEDIATION.md`, R1.

## Installation, registration, uninstallation

These are in the zip's `README.md` (`docs/USER-GUIDE.md`):

- requirements
- checksum verification
- `Expand-Archive` and `Unblock-File`
- provisioning, and the read-only login to ask the DBA for
- `claude mcp add` at user or project scope, with `-e SQLINVESTIGATOR_HOME=…`
- the recommended permissions
- the tools, limits and troubleshooting by error code
- uninstall: `remove --alias` for each alias, `claude mcp remove`, then delete both folders

## To publish (owner)

```powershell
git checkout 0c09c70     # or the commit you choose
./eng/release.ps1 -Verify
gh release create v1.0.0 artifacts/release/SqlInvestigator-1.0.0-win-x64.zip artifacts/release/SqlInvestigator-1.0.0-win-x64.zip.sha256 `
  --title "SQL Investigator 1.0.0" --notes-file docs/USER-GUIDE.md
```

The repository is private, so a release is visible only to collaborators.
