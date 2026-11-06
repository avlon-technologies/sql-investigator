# macOS runtime evidence: ADR-007, transition 0001-macos-support

**Result:** the published macOS executables ran on real GitHub-hosted Macs, on Apple silicon and Intel.
- **Proven there:** the unit tests, the Keychain lifecycle, owner-only permissions, application-scoped Keychain access, and MCP stdio start-up.
- **Not proven yet on macOS:** SQL Server connectivity and the 12 acceptance scenarios. GitHub's macOS runners cannot host the SQL Server container, and no reachable test server was configured.

## Where it ran

- **Repository:** `mikekangdev/sql-investigator-macos`.
  - A temporary, private copy under the owner's personal account. The org's GitHub Enterprise trial refuses spending budgets ("customers on trial cannot create budgets", HTTP 403), so the org had no macOS minutes.
  - It held commit `5835cd8`: the org's main at `f8238ce`, plus one scratch commit that removed the three self-hosted-runner workflows.
- **Workflow:** `.github/workflows/macos.yml`, started by hand by the owner (`workflow_dispatch`, both architectures).

| Run | Commit | Result |
|---|---|---|
| [42228910509](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42228910509) | `2ed472a` (= `0ad21a3` + scratch) | Failed at unit tests on both: `The_default_data_home_on_macos_is_under_application_support` tried to delete a temp folder it never created (a test bug; 130 other tests passed). Fixed in `f8238ce`. Log: `run-42228910509-failed.log` |
| [42229416496](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42229416496) | `5835cd8` (= `f8238ce` + scratch) | **Passed on both.** Log: `run-42229416496.log` |

## Run 42229416496, per architecture

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

- **Established on macOS 15 (arm64 and x64):**
  - The secret store's create, read, update and delete work, and the item is application-scoped.
  - Folders are 0700 and files 0600, and the default data path is correct.
  - The published single-file executable starts, provisions, serves MCP over stdio and cleans up.
  - No secret appears in output, diagnostics or files.
- **Not established on macOS:**
  - SQL Server connectivity from macOS (Microsoft.Data.SqlClient's managed network stack). Everything the scenarios exercise against a live server: B02–B12 and the read-only principal barrier.
  - Behaviour on macOS 26.
  - An update of an ad-hoc-signed build re-prompting on a developer's Mac. The design expects it, and it fails closed headless, but it was not observed.
- **Readiness:** macOS is **Runtime-verified for everything except SQL connectivity**, and not Distribution-ready: the builds are ad-hoc signed, not Developer ID signed or notarized. Windows evidence is unchanged: 130 unit, 18 integration and 12/12 scenarios on this PC.
