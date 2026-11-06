# macOS cold-start evaluation, run 1: failed at provisioning

**Result:** an evaluator with no implementation knowledge failed to provision a database on both Macs. The cause was a product defect plus a documentation gap, both now fixed. A second run is needed before this evaluation can count as passed.

## How it ran

- **The evaluator:** a separate agent wrote `eng/coldstart/macos-coldstart.sh` and `eng/coldstart/mcp-client.mjs` from `docs/MACOS.md` and `docs/USER-GUIDE.md` alone. It had no access to the source, the tests or this session.
- **The harness:** `.github/workflows/macos-coldstart.yml`, run [42237379805](https://github.com/mikekangdev/sql-investigator-macos/actions/runs/42237379805) on 2026-11-06, in the owner's temporary personal repository.
  - It built the release tarball exactly as `macos.yml` does.
  - It created one disposable database of synthetic orders with a SELECT-only login on a disposable SQL Server 2022 container in Azure (since deleted).
  - It passed the evaluator only the tarball and the connection facts.
- **Claude Code** was not installed on the runner. The script played the MCP client itself (`mcp-client.mjs`), so `claude mcp add` was not exercised.

## Results (identical on both architectures)

| | osx-arm64 (macOS 15.8.2) | osx-x64 |
|---|---|---|
| PASS / FAIL / BLOCKED / INFO | 16 / 2 / 6 / 4 | 16 / 2 / 6 / 4 |
| Report | [coldstart-osx-arm64.md](coldstart-osx-arm64/coldstart-osx-arm64.md) | [coldstart-osx-x64.md](coldstart-osx-x64/coldstart-osx-x64.md) |

| Mission stage | Outcome |
|---|---|
| Installation | Passed: architecture matched, extracted, executable |
| First launch | Passed: no quarantine mark (as documented), ad-hoc signed, `--version` and `--help` work |
| Connection configuration | **Failed.** The safety checks passed: a password argument and `--environment production` are both refused, and the password never appears in output. The provision itself failed (below) |
| Secure credential storage | Blocked (nothing was provisioned) |
| Synthetic SQL Server data and a representative investigation | Blocked |
| Restart and reconnection | Blocked |
| Removal of stored credentials | Blocked for the alias. The uninstall steps passed, and no keychain items remained |

## Why provisioning failed

```
sql.error: SQL Server returned error -2146893019 for the query on alias "coldstart-main": A connection was
successfully established with the server, but then an error occurred during the pre-login handshake.
(provider: TCP Provider, error: 35 - An internal exception was caught) Nothing was provisioned.
  Correct the query and run it again; nothing is returned for a failed query.
```

- **The server's certificate:** the container uses a self-signed TLS certificate, so the connection needs `--trust-server-certificate`. The evaluator did not pass it.
- **Product defect:** the failure was reported as a query error, telling the user to "correct the query". The user guide promised a certificate error with `--trust-server-certificate` as the remedy. The evaluator's script would have retried with the flag on a certificate or `server.unreachable` error, but it got `sql.error`.
- **Documentation gap:** neither page said that container and local servers *need* the flag, or what the failure looks like.

## What was fixed

- **Error translation** (`src/SqlInvestigator.McpServer/Connections/SqlConnections.cs`):
  - A failed TLS handshake is now recognised by its certificate/SSPI error number (Windows) or its "pre-login handshake" text (macOS).
  - It is reported as `server.unreachable`: "The server answered, but the encrypted (TLS) connection could not be set up". The action names `--trust-server-certificate` for local, container and test servers, and installing the CA otherwise.
  - New integration test `An_untrusted_server_certificate_is_reported_with_the_trust_flag_as_the_remedy` reproduces it against the local container (Windows; 19 of 19 integration tests pass).
- **Documentation:**
  - `MACOS.md` and `USER-GUIDE.md` say when `--trust-server-certificate` is needed and what the failure says. There is a new troubleshooting row.
  - The tarball now includes `MACOS.md`.
  - The `.sha256` file holds the bare file name, so `shasum -a 256 -c` works, and the install step shows that check.
  - There is a `PATH` note.
  - The user guide's Windows-only wording is gone from the shared rows (`SqlInvestigator remove`, `credential.unavailable` on macOS).
  - Both pages say that console `remove` deletes the stored password even after `connection_remove`.

## Not fixed, still open

- **Re-run:** the evaluation has to run again on both architectures. That needs the owner to push to the personal repository and start the workflow, plus a new disposable SQL Server.
- **Claude Code:** it is not exercised on the runner. A true first-launch check of `claude mcp add` needs a Mac with Claude Code installed.
