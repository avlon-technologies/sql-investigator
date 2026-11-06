# macOS cold-start evaluation

**Result: passed on both architectures in run 2. 82 PASS, 0 FAIL, 2 BLOCKED, 7 INFO on each Mac.** Run 1 failed at provisioning. It found a product defect and a documentation gap, and both were fixed before run 2 (commit `94f8806`). The two BLOCKED checks are the `claude mcp add` and `claude mcp remove` steps: Claude Code is not installed on the runner.

## How it ran

- **The evaluator:** a separate agent wrote `eng/coldstart/macos-coldstart.sh` and `eng/coldstart/mcp-client.mjs` from `docs/MACOS.md` and `docs/USER-GUIDE.md` alone. It had no access to the source, the tests or the implementing session.
  - The script was **not changed** between the runs. Only the product and its docs changed.
- **The harness:** `.github/workflows/macos-coldstart.yml`, run in the owner's temporary personal repository `mikekangdev/sql-investigator-macos`.
  - It builds the release tarball exactly as `macos.yml` does.
  - It creates one disposable database of synthetic orders, with a SELECT-only login on `dbo.Orders`. The server was a disposable SQL Server 2022 container in Azure, deleted after each run.
  - It hands the evaluator only the tarball and the connection facts.
- **Claude Code:** not installed on the runner. The script acts as the MCP client itself (`mcp-client.mjs`). It starts the server with no arguments over stdio, exactly as the Claude Code registration does.

| Run | Commit | Result per architecture | Reports |
|---|---|---|---|
| [42237379805](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42237379805) | `2aded06` + scratch | **Failed:** 16 PASS, 2 FAIL, 6 BLOCKED, 4 INFO | [arm64](run1/coldstart-osx-arm64/coldstart-osx-arm64.md), [x64](run1/coldstart-osx-x64/coldstart-osx-x64.md) |
| [42239939667](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42239939667) | `94f8806` + scratch | **Passed:** 82 PASS, 0 FAIL, 2 BLOCKED, 7 INFO | [arm64](run2/coldstart-osx-arm64/coldstart-osx-arm64.md) (macOS 15.8.2), [x64](run2/coldstart-osx-x64/coldstart-osx-x64.md) (macOS 15.7.9) |

## Run 2, by mission stage (identical on both architectures)

| Stage | Outcome |
|---|---|
| Installation | Passed: the SHA-256 matches the `.sha256` file; the tarball matches the Mac's architecture; it extracts to `~/Applications/SqlInvestigator` |
| First launch | Passed: no quarantine mark (as documented for `gh` downloads), ad-hoc signed, `--version` and `--help` work |
| Connection configuration | Passed. A password argument and `--environment production` are both refused. The first attempt failed with the new `server.unreachable` "encrypted (TLS) connection could not be set up" message; the evaluator followed its advice, added `--trust-server-certificate`, and provisioning succeeded. `list` shows the alias |
| Secure credential storage | Passed: one generic-password item "SQL Investigator: coldstart-main" in the default keychain, with the documented account, kind and service. Another program cannot read it unattended. The data folder is 0700 and owned by the user, store files are 0600, there is no `vault` folder, and the password appears nowhere in the data folder, in plain text or base64 |
| Synthetic SQL Server data and a representative investigation | Passed, over MCP stdio: 11 tools; `connection_list`, `schema_inspect`, `sql_validate` (accepts a bounded SELECT, rejects `DELETE`); `investigation_start`, `sql_execute` (5 rows, with a query id), `investigation_record` and `investigation_get`. stdout is JSON-RPC only, and the password appears in no result and no stderr line |
| Restart and reconnection | Passed: a new server process still has the alias and the investigation. A new query works and is appended to the same investigation |
| Removal of stored credentials | Passed. Console `remove` deletes the keychain item, the alias disappears, and `sql_execute` is then refused with `alias.unknown` while history stays readable. `connection_remove` (the MCP tool) removes an alias but leaves its item, as documented, and console `remove` then deletes that orphaned item |
| Uninstall | Passed, apart from `claude mcp remove` (BLOCKED). No SqlInvestigator keychain items remain. The remaining files the script lists are the harness's own build output and the repository checkout, not the installation |

## Run 1: why provisioning failed, and what was fixed

```
sql.error: SQL Server returned error -2146893019 for the query on alias "coldstart-main": A connection was
successfully established with the server, but then an error occurred during the pre-login handshake.
(provider: TCP Provider, error: 35 - An internal exception was caught) Nothing was provisioned.
  Correct the query and run it again; nothing is returned for a failed query.
```

- **The cause:** the container's TLS certificate is self-signed, so the connection needs `--trust-server-certificate`.
- **Product defect:** the failure was reported as a query error. The fix (`SqlConnections.cs`, `SqlFailures.IsEncryptionFailure`):
  - recognises a failed TLS handshake by its certificate/SSPI error number (Windows) or its "pre-login handshake" text (macOS);
  - reports it as `server.unreachable`, with `--trust-server-certificate` as the remedy for local, container and test servers.
  - The new integration test `An_untrusted_server_certificate_is_reported_with_the_trust_flag_as_the_remedy` passes on Windows, on macOS arm64 and on macOS x64 (19 of 19 integration tests on each, run 42239946607).
- **Documentation gaps, now fixed:**
  - when the flag is needed;
  - `MACOS.md` in the tarball;
  - a `.sha256` file that works with `shasum -c`;
  - a `PATH` note;
  - Windows-only wording in rows that apply to both platforms;
  - console `remove` after `connection_remove`.

## Not covered

- **Claude Code's own registration** (`claude mcp add`, `/mcp`, `claude mcp remove`) on a Mac. That needs a Mac with Claude Code installed.
- **A browser-downloaded, quarantined tarball:** Gatekeeper's "Open Anyway" path was not exercised.
- **A developer's own login keychain:** the runner uses an unattended temporary keychain, so an interactive approval dialog was not seen.
