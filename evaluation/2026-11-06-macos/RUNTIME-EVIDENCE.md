# macOS runtime evidence: ADR-007, transition 0001-macos-support

**Result:** the published macOS executables ran on real GitHub-hosted Macs, on Apple silicon and Intel, and passed everything on both architectures:
- the unit tests;
- the published-executable smoke test (Keychain lifecycle, owner-only permissions, application-scoped access, MCP stdio);
- the 18 SQL Server integration tests;
- **all 12 acceptance scenarios**, run against a disposable SQL Server in an Azure Container Instance (run 42232080041).

## Where it ran

- **Repository:** `mikekangdev/sql-investigator-macos`.
  - A temporary, private copy under the owner's personal account. The org's GitHub Enterprise trial refuses spending budgets ("customers on trial cannot create budgets", HTTP 403), so the org had no macOS minutes.
  - It held commit `5835cd8`: the org's main at `f8238ce`, plus one scratch commit that removed the three self-hosted-runner workflows.
- **Workflow:** `.github/workflows/macos.yml`, started by hand by the owner (`workflow_dispatch`, both architectures).

| Run | Commit | Result |
|---|---|---|
| [42228910509](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42228910509) | `2ed472a` (= `0ad21a3` + scratch) | Failed at unit tests on both: `The_default_data_home_on_macos_is_under_application_support` tried to delete a temp folder it never created (a test bug; 130 other tests passed). Fixed in `f8238ce`. Log: `run-42228910509-failed.log` |
| [42229416496](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42229416496) | `5835cd8` (= `f8238ce` + scratch) | **Passed on both**, without SQL Server (none configured). Log: `run-42229416496.log` |
| [42232080041](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42232080041) | `5835cd8` | **Passed on both, with SQL Server.** Integration tests 18/18 and acceptance 12/12 on each architecture. Log: `run-42232080041.log` |

## Run 42232080041 (with SQL Server)

| | osx-arm64 | osx-x64 |
|---|---|---|
| Unit tests | 131 passed, 0 failed, 2 skipped | 131 passed, 0 failed, 2 skipped |
| Smoke test of the published executable | all checks passed | all checks passed |
| Integration tests (SQL Server) | 18 passed, 0 failed (57 s) | 18 passed, 0 failed (1 min 1 s) |
| Acceptance scenarios, transition 0001 result, against the PUBLISHED executables | **12 passed, 0 failed**: b01–b12 | **12 passed, 0 failed**: b01–b12 |
| Job time | 3 min 6 s | 5 min 59 s |

**The test SQL Server:**
- **What it was:** `mcr.microsoft.com/mssql/server:2022-latest` (Developer edition, the same image as the local fixture), in an Azure Container Instance.
  - 2 vCPU, 4 GB, Canada Central.
  - Its own resource group, `rg-sqlinv-macos-ci-cc`, in the owner's subscription.
- **Data:** synthetic fixture data only.
- **The admin password:**
  - It was random and never placed on a command line.
  - It was passed to Azure as a secure value and to the run as a repository secret.
  - It does not appear in the saved log.
- **Lifetime:** created at 14:00 UTC and checked from this PC first (a fixture was created and dropped). The resource group, the repository secret and variable, and the local password file were deleted right after the run.

## Run 42229416496 (without SQL Server), per architecture

| | osx-arm64 | osx-x64 |
|---|---|---|
| Runner image | macos-15-arm64, macOS 15.8.2, `arm64` | macos-15-intel, macOS 15.7.9, `x86_64` |
| Build, warnings as errors (platform analyzer CA1416) | passed | passed |
| Unit tests | 131 passed, 0 failed, 2 skipped (Windows-only) | 131 passed, 0 failed, 2 skipped |
| Published executable | `Mach-O 64-bit executable arm64`, `Signature=adhoc` | `Mach-O 64-bit executable x86_64`, `Signature=adhoc` |
| Smoke test of the published executable (`eng/macos-smoke.mjs`) | 20 of 20 passed | 20 of 20 passed |
| Integration tests, acceptance scenarios | **not run**: no test SQL Server | **not run** |
| Package | `SqlInvestigator-1.0.0-osx-arm64.tar.gz` sha256 `00478012ef2563812f3aac02ff92467554706b8b2ddf7ff2783d3725f146b4ab` | `SqlInvestigator-1.0.0-osx-x64.tar.gz` sha256 `c3e78d70030156e3925c6f64c1e437ff70810929bc48ba9aafdbaca22065949c` |
| Job time | 36 s | 2 min 24 s |

The unit tests that ran only on macOS:
- `The_vault_keeps_a_keychain_item_per_alias_and_nothing_under_the_data_home`: create, read, replace and delete. Another data home cannot see the item, and nothing is written under the data home.
- `Data_folders_are_0700_and_store_files_0600_on_macos`: this includes tightening a pre-existing 0755 folder.
- `The_default_data_home_on_macos_is_under_application_support`.

The shared tests ran on macOS too: the atomic stores, current-user-only files (checked with `stat`), and the remove-secret path.

## Smoke-test checks (identical on both architectures)

1. **Start-up:** `--version` prints `1.0.0+5835cd81…`, and `--help` prints the console usage.
2. **Provisioning:** `provision`, with the secret on stdin and `--no-verify`, stores the alias. The console never prints the secret.
3. **Permissions and storage:**
   - The data home is mode `700`, and `catalog.json` is `600`.
   - No file under the data home contains the secret, and there is no `vault/` folder.
4. **Keychain item:** a login-keychain item exists for the alias (`mechanism: keychain-login`).
5. **Application-scoped access:** the fixture tool, a different application of the same user, gets **OSStatus -25293** (errSecAuthFailed) when it reads the item's data without approval.
6. **Replace:** `provision --replace` updates the item in place.
7. **MCP server:**
   - It starts over stdio and lists 11 tools.
   - `connection_list` shows the alias with no secret.
   - stdout carries JSON-RPC only, and no MCP output or diagnostic contains the secret.
8. **Removal:** `remove` deletes the item (OSStatus -25300 afterwards), and a second `remove` reports nothing to delete.

The CI keychain was a temporary keychain made the user's default: created, unlocked, then deleted at the end. That is how unattended Macs work. A developer's login keychain behaves the same way for item access control. Two things were not exercised: an interactive prompt on a developer's Mac, and a locked login keychain.

## What this does and does not establish

- **Established on macOS 15 (arm64 and x64), with the published single-file executables:**
  - The secret store's create, read, update and delete work, and the item is application-scoped.
  - Folders are 0700 and files 0600, and the default data path is correct.
  - MCP stdio is protocol-only.
  - SQL Server connectivity works through Microsoft.Data.SqlClient's managed stack, with TLS against the server's self-signed certificate.
  - Every behaviour the 12 acceptance scenarios specify holds: B01 `credential.user-protected` through the keychain; the SELECT-only allowlist and the read-only principal (B03, B04); budgets, timeout and cancellation (B05); cross-database correlation (B06); history (B07); relationship review (B08); redaction (B09); schema drift (B10); safe failures (B11); the Claude-only surface (B12).
- **Not established:**
  - Behaviour on macOS 26.
  - An interactive keychain prompt, or a locked login keychain, on a developer's Mac. Headless, the server is designed to fail closed, but neither case was observed.
  - Re-prompting after an update of an ad-hoc-signed build.
- **Readiness:**
  - **macOS (arm64, x64): Runtime-verified.** Not Distribution-ready: the builds are ad-hoc signed, not Developer ID signed or notarized.
  - **Windows:** unchanged. On this PC, 130 unit tests, 18 integration tests and the 12 scenarios of the transition's result pass.
