# Phase 1 notes: installing and connecting SQL Investigator 1.0.0

Date: 2026-11-06. Shell: Windows PowerShell 5.1 (plus Git Bash for one stdio test).
Install folder: `C:\sqlinv-eval\coldstart-1\install`. Data folder (`SQLINVESTIGATOR_HOME`): `C:\sqlinv-eval\coldstart-1\home`.

## Result

| Step | Status |
|---|---|
| 1. Verify and install | Done. SHA-256 matches; `--version` prints `1.0.0+0c09c708adc2d5ea808a8124bd39a626ae415a4b` |
| 2. Provision two aliases | Done. `commerce` (EvalCommerce) and `fulfillment` (EvalFulfillment), both passed the read-only check |
| 3. Register at project scope | Done, **under the name `sql-investigator-eval`** (not `sql-investigator`; see Problem 1) |
| 4. Confirm registration | Partly. `claude mcp get` shows it in project config with the right command and env, status **"Pending approval"**. A direct stdio MCP test of the same exe and env worked: it initialized, listed 11 tools and `connection_list` returned both aliases |

**For Phase 2:** the tools will be named `mcp__sql-investigator-eval__*`, not the `mcp__sql-investigator__*` names in the README. The user scope also has a different `sql-investigator` server (another copy, other data). **Do not use it.** It cannot see these aliases, and it belongs to someone else.

## Commands run, in order

1. `Get-ChildItem` of the workspace and `downloads`. Read `dba-email.txt`, the `.sha256` file and `SqlInvestigator-README.md`. OK.
2. `(Get-FileHash .\SqlInvestigator-1.0.0-win-x64.zip -Algorithm SHA256).Hash` and `Get-Content ...sha256`. Both are `6AD28140…C6AD62` (Get-FileHash prints uppercase, the file has lowercase). They match.
3. `Expand-Archive <zip> -DestinationPath C:\sqlinv-eval\coldstart-1\install`, then `Get-ChildItem $dir -Recurse | Unblock-File`. OK. The zip has no top-level folder, so `SqlInvestigator.exe` ends up directly in `install\`.
4. `install\SqlInvestigator.exe --version` printed `1.0.0+0c09c708…`. OK.
5. `SqlInvestigator.exe --help` printed the console command list, exit 0.
6. `SqlInvestigator.exe provision --help` **failed**: `input.invalid: Unknown or incomplete option "--help". Run SqlInvestigator --help.`, exit 1. There is no per-command help. The top-level `--help` covers it.
7. Compared `install\README.md` with `downloads\SqlInvestigator-README.md` (`Compare-Object`). Same content; the size difference is only line endings. Read `VERSION.txt` (commit 0c09c708, built 2026-11-06, self-contained .NET 10).
8. Provisioned commerce, with `$env:SQLINVESTIGATOR_HOME` set for that console session. I read the password out of `dba-email.txt` into a variable with `Select-String` and piped it on stdin, so it was never typed, passed as an argument or written to a new file:
   `$pw[0] | SqlInvestigator.exe provision --alias commerce --server "127.0.0.1,1433" --database EvalCommerce --user eval_commerce_ro --environment development --scope dbo.Customers --scope dbo.Orders --scope dbo.OrderLines --scope dbo.Payments --trust-server-certificate`
   It printed "Checking that eval_commerce_ro can connect and is read-only..." and then "Provisioned alias "commerce"". Exit 0.
9. Provisioned fulfillment the same way: `--alias fulfillment --database EvalFulfillment --user eval_fulfillment_ro`, scope `dbo.ExternalOrderReferences`, `dbo.Shipments`, `dbo.ShipmentEvents` and `dbo.Returns`, plus `--trust-server-certificate`. Exit 0.
10. `SqlInvestigator.exe list` showed both aliases (tab-separated, no header). The home folder now holds `catalog.json`, `vault\commerce.dpapi` and `vault\fulfillment.dpapi`.
11. `claude mcp add sql-investigator --scope project -e SQLINVESTIGATOR_HOME=C:\sqlinv-eval\coldstart-1\home -- "C:\sqlinv-eval\coldstart-1\install\SqlInvestigator.exe"`. It wrote `.mcp.json`. OK.
12. `claude mcp list` and `claude mcp get sql-investigator`. **Problem:** both resolved `sql-investigator` to a **user-scope** server at `C:\Users\kangm\AppData\Local\SqlInvestigator\bin\SqlInvestigator.exe`, which is not mine. `mcp list` printed a "Conflicting scopes" diagnostic.
13. `claude mcp remove sql-investigator -s project`. This removed only my project entry. OK.
14. `claude mcp add sql-investigator-eval --scope project -e SQLINVESTIGATOR_HOME=C:\sqlinv-eval\coldstart-1\home -- "C:\sqlinv-eval\coldstart-1\install\SqlInvestigator.exe"`. OK. `.mcp.json` now has only `sql-investigator-eval`, with the env var set.
15. `claude mcp get sql-investigator-eval` showed "Scope: Project config, Status: ⏸ Pending approval (run `claude` to approve)". `claude mcp list` showed the same status.
16. Stdio smoke test, attempt 1 (PowerShell, piping all JSON-RPC lines at once). **No output, exit 0.** The server apparently exits at end of stdin before it answers.
17. Attempt 2 (Bash, with `sleep` between messages so stdin stayed open), output to `%TEMP%`. It worked (12 KB of responses). I then tried to parse them with `python -I`. **Python is not installed**, and the same command deleted the temp files, so I re-ran the test. Writing to `%TEMP%` also briefly left the workspace, which I should have avoided.
18. Attempt 3: same as attempt 2, but output went to `coldstart-1\scratch\`, parsed with `ConvertFrom-Json`. Results: `initialize` → `sql-investigator 1.0.0`, protocol `2025-06-18`. `tools/list` → `connection_list`, `connection_remove`, `schema_inspect`, `sql_validate`, `sql_execute`, `investigation_start`, `investigation_record`, `investigation_get`, `knowledge_search`, `knowledge_propose`, `knowledge_review`. `connection_list` → both aliases with their scope, no secrets. stderr was empty.
19. Tried to create `.claude\settings.local.json` with `"enabledMcpjsonServers": ["sql-investigator-eval"]` and the README's recommended allow/ask permissions, renamed to `mcp__sql-investigator-eval__*`. **The write was declined by the permission system.** I did not retry. The server is still pending approval.
20. Removed `scratch\`, then re-ran `list` and `claude mcp get`. The state is unchanged.

## Problems, confusing points and workarounds

1. **Name clash with an existing user-scope server (main problem).** The README tells you to name the server `sql-investigator` in both scopes. It never says what happens if that name already exists in another scope. Here `claude mcp list/get` resolved the name to the user-scope copy, which uses a different exe and the default data folder, so it would not see my aliases. I could not remove it because it belongs to someone else.
   - **Workaround:** I registered under `sql-investigator-eval`. As a result, every permission name in the README (`mcp__sql-investigator__…`) is wrong for this setup.
   - The README should warn about this and say how tool names follow the server name.
2. **Project-scope approval needs a human.** The status is "Pending approval (run `claude` to approve)". The README mentions approval only as part of `/mcp` after a restart. It does not mention it for `claude mcp list`, and it gives no non-interactive way to approve. My attempt to pre-approve through project-local settings was declined.
   - Someone has to approve `sql-investigator-eval` when Claude Code restarts, either interactively or by adding `enabledMcpjsonServers`.
   - README step 3.1 says `claude mcp list` "should show ✓ Connected". For a fresh project-scope server it shows "Pending approval" instead.
3. **Non-interactive password.** The README says to "pipe it on standard input", but `--help` does not mention stdin at all; it says "prompted for at the console". Piping worked. The prompt text still prints, followed by a blank line.
   - Gap: the README gives no example of piping safely, for example from a secret store, so that the password does not end up in the command history.
4. **What `SQLINVESTIGATOR_HOME` points to.** The README says the default is `%LOCALAPPDATA%\SqlInvestigator\data` and that `SQLINVESTIGATOR_HOME` should be set "to a folder path". It does not say whether that folder is the data folder or its parent (with `\data` added).
   - Observed: files go directly into the folder (`home\catalog.json`, `home\vault\`), with no `data\` subfolder. The folder is created automatically.
   - `history.json` and `knowledge.json` do not exist yet. I assume they are created on first use.
5. **Unclear zip layout.** The README does not say whether the zip has a top-level folder. It has none, so `"$dir\SqlInvestigator.exe"` works.
6. **No `provision --help`.** It is rejected as `input.invalid`. The error message does point you to `--help`.
7. **Scope choice is an assumption.** The DBA email lists the grants per table, so I scoped each alias to exactly those four tables, with all columns. Nothing tells me whether all columns are appropriate; that was a judgement call.
8. **TLS.** The email says the certificate is self-signed, so I used `--trust-server-certificate` as the README suggests. I did not try without it.
9. **Running the server with stdin closed** (smoke test attempt 1): it exits without replying to requests that are already buffered. This doesn't matter for Claude Code. It does make quick command-line testing confusing, and it is undocumented.
10. **Hash case.** `Get-FileHash` prints uppercase and the `.sha256` file is lowercase. They match, but a naive string comparison would be case-sensitive. A small doc nit.
11. **`list` output** is tab-separated with no header, so the columns (authentication `sql`, timestamp) are not labelled.
12. **README inconsistency:** the tools table says `connection_remove` does **not** delete the stored password. The console `remove` deletes both alias and password. That is clear enough, but worth knowing.

## Needed a human for

- Approving the project-scope MCP server `sql-investigator-eval` on next Claude Code start (status "Pending approval"). Alternatively, allowing `enabledMcpjsonServers` plus the tool permissions in `.claude/settings.local.json`; that write was declined here.
- Deciding whether the existing user-scope `sql-investigator` should stay. I left it untouched, as instructed.

## Security notes

- The passwords were never passed as arguments, never written to a file I created, and are not in these notes. They went by stdin straight from `downloads\dba-email.txt`. They are stored DPAPI-encrypted in `home\vault\`.
- No database was modified. The provisioning check reported both logins as read-only.
- Files created in the workspace: `install\`, `home\`, `.mcp.json` and this file. `scratch\` was removed. The two `%TEMP%` files from step 17 were deleted.
