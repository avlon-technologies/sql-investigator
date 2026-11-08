# macOS support: final report

2026-11-06. Mission: "SQL Investigator — macOS Support: Autonomous Implementation".

## Readiness

| Platform | Development-ready | Runtime-verified | Distribution-ready |
|---|---|---|---|
| Windows x64 | yes | yes (unchanged) | as before |
| macOS Apple silicon (osx-arm64) | **yes** | **yes**, on macOS 15.8.2 | **no** |
| macOS Intel (osx-x64) | **yes** | **yes**, on macOS 15.7.9 | **no** |

**Why macOS is Runtime-verified:** the published single-file executables passed four things on real Macs of both architectures:
- the unit tests;
- 19 integration tests against SQL Server;
- all 12 acceptance scenarios;
- an independent cold-start evaluation.

**Why it is not Distribution-ready:** the builds are ad-hoc signed. They are not signed with an Apple Developer ID and not notarized (item 10). Nothing here should be described as a production macOS release.

## 1. ADR-007: status and decision history

| Event | Act | By |
|---|---|---|
| 21 | ADR-007 proposed, "Support macOS; protect credentials with the operating system's per-user secret store" | this agent, under the owner's delegation |
| 22 | Checked: aligned, 7 of 7 | the Continuum Host |
| 23 | **Accepted**, option `macos` | ChatGPT (`agent:remote-mcp`), a party distinct from the proposer |
| 24 | **ADR-004 superseded by ADR-007**, with the reason stated | the owner, grant `owner`, signed by the Host |

- **Current state:** the decision space holds 7 decisions over 24 events: ADR-001 to ADR-003, ADR-005 to ADR-007 accepted, and ADR-004 superseded by ADR-007.
- **ADR-004's other clauses still stand.** The supersession acts on the whole decision, but ADR-007's accepted text restates ADR-004's store decision unchanged (System.Text.Json files with atomic replacement, one writing service per store, no SQLite). The reason recorded at event 24 says the same.
- **Not recorded:** ADR-007's implementation evidence is not on the Host yet. `decision evidence` needs a new delegation.

## 2. ADL, BDL and binding changes

All of these came through **transition `0001-macos-support`**:
- approved by mike@avlon.ca;
- accepted at `e7e272f` with 12 of 12 scenarios;
- adopted as checkpoint `history/0003-macos-support`.

| Layer | Change |
|---|---|
| ADL (`changes/macos-support.adl-change`) | **Descriptions only.** These four now name no OS mechanism: the deployable `6ae994a4`, the environment `a2eaf960`, the CredentialVault `783fa850` and the provisioning constraint `efc4bf63`. No element, edge, owner or constraint was added, removed or moved. The vault is still a secret-store that no service writes. State `def71c2d…` → `ff4a0287…` |
| BDL (`sql-investigator.bdl`) | **None.** The business meaning did not change |
| Bindings (`local-development.adl-binding.json`, plus the vocabulary claims) | The OS list is now `windows-x64 osx-arm64 osx-x64`. The vault technology `windows-dpapi-currentuser` became `os-user-secret-store`, with `windows: DPAPI-CurrentUser` and `macos: Keychain-login-generic-password`. The connection protection is `os-user-secret-store` |
| Acceptance | B01: the step `credential.dpapi-current-user` became `credential.user-protected` ("protected for the current operating-system user, readable only by that user"). It is bound per OS in `continuum/acceptance/adapter.mjs` |

## 3. Continuum compile and verification

- **Transition:**
  - `icp check` verified the transition against its base (transitionId `5b00b334…`, result state `ff4a0287…`).
  - `icp ready` then ran the suite (12 of 12) before acceptance.
  - `icp adopt` wrote checkpoint 0003.
- **Package:** `continuum package check continuum` reports `intact` (re-run today after the last commit), and the package was rebuilt at adoption.
- **Guard:** the hooks passed on every commit and push. Pre-push at `1633672` reported implementation "moved-conforming" against the accepted revision `e7e272f`.
- **Org CI:** passed on `94f8806` (.NET and Continuum). At the time of writing it was running on `1633672`, an evidence-only commit.
- **Not established:** a model reassessment says nothing about the implementation. The implementation evidence is the test results below, reported beside the model results, not as a conformance level.

## 4. Windows regression

On this PC (Windows 11, win-x64), against the local Docker SQL Server, at `94f8806`:
- 130 unit tests passed (3 macOS-only skipped);
- **19 of 19 integration tests**, including the new untrusted-certificate test;
- **12 of 12 acceptance scenarios**.

The Windows credential store (DPAPI CurrentUser, files restricted to the current user) is unchanged.

## 5. macOS Intel runtime (osx-x64, GitHub `macos-15-intel`, macOS 15.7.9)

| Run | Result |
|---|---|
| 42229416496 | 131 unit tests (2 Windows-only skipped); smoke test of the published executable 20 of 20 |
| 42232080041 | 131 unit, 18 of 18 integration, 12 of 12 acceptance |
| 42239946607 (after the TLS fix) | 131 unit, **19 of 19** integration, **12 of 12** acceptance |

## 6. macOS Apple silicon runtime (osx-arm64, GitHub `macos-15`, macOS 15.8.2)

Same runs, same counts as Intel: 131 unit tests, smoke test 20 of 20, integration 18 then 19 of 19, and acceptance 12 of 12 on each run.

## 7. SQL Server integration

- **The test server:** a disposable SQL Server 2022 Developer container (`mcr.microsoft.com/mssql/server:2022-latest`) in Azure Container Instances, Canada Central, in its own resource group, holding synthetic fixture data only.
  - It was reached over TLS by Microsoft.Data.SqlClient's managed stack on macOS.
  - The container, its repository secret and variable, and the local password files were deleted after each use. **The resource group no longer exists.**
- **Results:** integration and acceptance passed on both architectures (runs 42232080041 and 42239946607), and the local container passed on Windows.
- **Never used:** production data or a production connection.

## 8. Published executable validation

- **The build:** a self-contained single file (`PublishSingleFile`, native libraries included), a `Mach-O 64-bit executable` for arm64 and for x86_64, `Signature=adhoc`.
- **The smoke test** (`eng/macos-smoke.mjs`, 20 checks) covers:
  - start-up;
  - provisioning with the secret on stdin;
  - modes 700/600;
  - no secret under the data home;
  - a keychain item per alias;
  - another application's read refused (OSStatus -25293);
  - replace;
  - MCP stdio as protocol only, with no secret in the output;
  - removal (-25300 afterwards).
- **The acceptance run used the published executables**, not a debug build.
- **Packages:** run 42229416496 gives arm64 sha256 `00478012…` and x64 `c3e78d70…` (see RUNTIME-EVIDENCE.md). Since `94f8806` the tarball includes `MACOS.md`, and the `.sha256` file verifies with `shasum -a 256 -c`.

## 9. GitHub Actions minutes and cost

- **The org:** none. Its Enterprise Cloud trial refuses budgets ("customers on trial cannot create budgets", HTTP 403), so its Actions budget stayed at $0 with a hard stop.
  - The budget call was attempted only after the owner authorized it.
  - Billing settings were not changed otherwise.
- **The owner's personal repo:** every macOS run went through the temporary private repository `mikekangdev/sql-investigator-macos`, and every run was started by the owner.

| Run | Workflow | arm64 | x64 |
|---|---|---|---|
| 42228910509 | macOS (failed: a test bug) | 0.3 min | 1.6 min |
| 42229416496 | macOS | 0.6 | 2.4 |
| 42232080041 | macOS + SQL Server | 3.1 | 6.0 |
| 42237379805 | cold start (failed) | 0.5 | 1.4 |
| 42239939667 | cold start | 0.9 | 2.1 |
| 42239946607 | macOS + SQL Server | 3.3 | 5.9 |
| **Total** | | | **27.8 min of job time; about 34 billed minutes** (rounded up per job) |

- **Estimated cost:**
  - At GitHub's published macOS rate (about $0.06 a minute), about **$2**.
  - The personal plan's included minutes may cover it entirely. macOS minutes count ten times against the included quota, so 34 minutes use about 340 of it.
  - The actual charge was not read from billing, because the `gh` token lacks the `user` scope.
- **Azure:** two short-lived 2 vCPU / 4 GB containers, roughly 1.5 hours in all. Estimated **well under $1**.

## 10. Outstanding signing, notarization and distribution requirements

None of these is done. All are required before a production macOS release (`docs/MACOS.md`, "What a production release needs"):
1. Apple Developer Program membership and a Developer ID Application certificate on the build machine.
2. Signing with the hardened runtime, a secure timestamp and an entitlements file that allows JIT.
3. Notarization with `notarytool`. A bare executable cannot be stapled, so ship a signed `.pkg` and staple that.
4. A release pipeline that does 1 to 3 and publishes the artifacts somewhere users download from. Today builds exist only as workflow artifacts in a temporary personal repository. `eng/release.ps1` still packages Windows only.
5. Optional, and a new ADR: move to the data-protection keychain with an access group once there is a Team ID.

Until then, every update of an ad-hoc-signed build is a new application to the keychain, so each alias must be re-provisioned or re-allowed (documented).

## 11. Cold-start evaluation

- **The evaluator:** an agent with no implementation knowledge wrote its script from `docs/MACOS.md` and `docs/USER-GUIDE.md` only. The harness gave it only the release tarball and the facts of a synthetic database.
- **Run 1** (42237379805) **failed** at provisioning on both Macs. A TLS handshake failure against the self-signed server certificate was reported as "sql.error … Correct the query", and the docs never said that `--trust-server-certificate` is needed for container servers.
  - **The code fix:** the failure is now `server.unreachable` with the flag as the remedy, with an integration test on all three platforms.
  - **Six doc fixes:**
    - when the flag is needed;
    - `MACOS.md` in the tarball;
    - a `.sha256` file that `shasum -c` can verify;
    - a `PATH` note;
    - Windows-only wording removed;
    - console `remove` after `connection_remove`.
- **Run 2** (42239939667) **passed** with the evaluator's script unchanged: **82 PASS, 0 FAIL, 2 BLOCKED, 7 INFO on each architecture.** These stages passed:
  - installation (with the checksum);
  - first launch;
  - connection configuration (the evaluator followed the new error's advice);
  - secure credential storage (keychain item, 0700/0600, no plain text);
  - a representative investigation over MCP against synthetic data;
  - restart and reconnection with the history intact;
  - removal of stored credentials;
  - uninstall.
- **The two BLOCKED checks** are `claude mcp add` and `claude mcp remove`: Claude Code is not installed on the runners.
- **Details:** [coldstart/README.md](coldstart/README.md).

## 12. Remaining blockers and risks

| | Risk or gap | What would close it |
|---|---|---|
| Blocker for distribution | Not Developer ID signed, not notarized | Item 10 |
| Risk | Keychain access breaks on every update of an ad-hoc build (documented workaround) | Developer ID signing |
| Not verified | An interactive keychain prompt, or a locked login keychain, on a developer's own Mac; the runners use an unattended temporary keychain | A manual check on a developer Mac |
| Not verified | Claude Code's own registration on macOS (`claude mcp add`, `/mcp`) | The same manual check |
| Not verified | macOS 26; Gatekeeper's "Open Anyway" on a browser-downloaded build; `--windows-auth` (Kerberos) on macOS | Not claimed in the docs |
| CI home | macOS CI lives in a temporary personal repository because the org's trial has no Actions budget. `macos.yml` and `macos-coldstart.yml` are in the org repo, but cannot run there until it has a paid plan or budget | Activate the paid plan and set a budget, then run `macos.yml` in the org |
| Governance | ADR-007's implementation evidence not recorded on the Host (ADR-004 was superseded at event 24) | A new delegation for `decision evidence` |
| Housekeeping | The personal repository `mikekangdev/sql-investigator-macos` still exists (no secrets or variables left) | `gh auth refresh -s delete_repo`, then `gh repo delete mikekangdev/sql-investigator-macos --yes` |
