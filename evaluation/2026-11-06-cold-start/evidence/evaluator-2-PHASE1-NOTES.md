# Phase 1 notes: install and connect SQL Investigator 1.0.0

Date: 2026-11-06. Shell: Windows PowerShell 5.1. All work stayed inside `C:\sqlinv-eval\coldstart-2`.

## Result

| Step | Status |
|---|---|
| 1. Verify and install | Done. SHA-256 matches; installed to `C:\sqlinv-eval\coldstart-2\install`; `--version` = `1.0.0+6551d9992b8889d861fea4b7f920ef288135ad8f` |
| 2. Provision two aliases | Done. `commerce` -> EvalCommerce, `fulfillment` -> EvalFulfillment. Both passed the read-only check |
| 3. Register at project scope | Done, as **`sql-investigator-dev`** (not `sql-investigator`; see the name collision below). `.mcp.json` written with `SQLINVESTIGATOR_HOME` |
| 4. Confirm registration | `claude mcp get` / `claude mcp list` show it as `Pending approval`, which the README says is expected. A hand-run stdio smoke test returned 11 tools and both aliases |
| Pre-approval / permissions in `.claude\` | **Not done.** Writing to `.claude\settings.local.json` and `.claude\settings.json` was refused by the permission system (details below) |

## Commands run, in order, with outcomes

All `SqlInvestigator.exe` console commands ran with `$env:SQLINVESTIGATOR_HOME = "C:\sqlinv-eval\coldstart-2\home"` set in that PowerShell session.

1. `Get-ChildItem` of the project and `downloads\`, read `dba-email.txt` and the `.sha256` file. OK.
2. Read `downloads\SqlInvestigator-README.md`. OK.
3. In `downloads\`: `(Get-FileHash .\SqlInvestigator-1.0.0-win-x64.zip -Algorithm SHA256).Hash -eq (Get-Content .\SqlInvestigator-1.0.0-win-x64.zip.sha256).Split(' ')[0]`. Printed `True`. Expected hash `188ac758994b1357b255c2c9fd9e3eeffc25bd1c8cf3172e82017087d85971b8`.
4. `Expand-Archive ...zip -DestinationPath C:\sqlinv-eval\coldstart-2\install`, `Get-ChildItem $dir -Recurse | Unblock-File`, `SqlInvestigator.exe --version`. OK: `1.0.0+6551d999...`. As documented, the zip has no top-level folder.
5. Read `install\VERSION.txt` (1.0.0, commit 6551d999, self-contained .NET 10) and compared `install\README.md` with the downloaded README using `Compare-Object`: 0 differing lines. The byte sizes differ (14708 vs 14483), most likely line endings only.
6. `SqlInvestigator.exe --help` and `provision --help`. OK. The help is identical for both (see the observations).
7. Provisioned `commerce`. The password came from `dba-email.txt` through a pipeline (`Select-String` on the `password:` line, then `| & $exe provision ...`). It was never a program argument, never written to a new file, and it does not appear here:
   `provision --alias commerce --server "127.0.0.1,1433" --database EvalCommerce --user eval_commerce_ro --environment development --trust-server-certificate --scope dbo.Customers --scope dbo.Orders --scope dbo.OrderLines --scope dbo.Payments`
   The program printed the prompt, then "Checking that eval_commerce_ro can connect and is read-only...", then `Provisioned alias "commerce"`. Exit code 0.
8. Provisioned `fulfillment` the same way (the second `password:` line):
   `provision --alias fulfillment --server "127.0.0.1,1433" --database EvalFulfillment --user eval_fulfillment_ro --environment development --trust-server-certificate --scope dbo.ExternalOrderReferences --scope dbo.Shipments --scope dbo.ShipmentEvents --scope dbo.Returns`
   Provisioned. Exit code 0.
9. `SqlInvestigator.exe list` showed both aliases (sql auth, development, the full scope). `home\` now holds `catalog.json` and `vault\`.
10. `claude mcp list` in the project folder (before registering): **a `sql-investigator` server is already registered** at user scope, pointing at `C:\Users\kangm\AppData\Local\SqlInvestigator\bin\SqlInvestigator.exe`, and it shows `✔ Connected`. This is the other copy on this machine, and its config belongs to someone else.
11. `claude mcp add sql-investigator-dev --scope project -e SQLINVESTIGATOR_HOME=C:\sqlinv-eval\coldstart-2\home -- "C:\sqlinv-eval\coldstart-2\install\SqlInvestigator.exe"`. Output: "Added stdio MCP server sql-investigator-dev ... to project config". Exit code 0. `.mcp.json` contains the command, empty args and the env var.
12. **FAILED / refused:** creating `.claude\settings.local.json` with `{"enabledMcpjsonServers": ["sql-investigator-dev"]}` (the README's scripted approval) and `.claude\settings.json` with the README's recommended permissions, with every tool name changed to `mcp__sql-investigator-dev__...`. Both writes were rejected: "Claude requested permissions to write to ... but you haven't granted it yet." I did not work around this through the shell, because that would bypass the permission decision.
13. `claude mcp get sql-investigator-dev` showed Scope: Project config, Status: `⏸ Pending approval (run 'claude' to approve)`, the command, and the env var. `claude mcp list` showed `sql-investigator` (user, Connected) and `sql-investigator-dev` (Pending approval).
14. Smoke test by hand: started `install\SqlInvestigator.exe` with `SQLINVESTIGATOR_HOME` set, redirected stdin and stdout, and kept stdin open while sending JSON-RPC: `initialize`, `notifications/initialized`, `tools/list`, then `tools/call connection_list`.
    - initialize: protocol 2025-06-18, serverInfo `sql-investigator` 1.0.0, plus server instructions ("Call connection_list first, schema_inspect before writing SQL ...").
    - tools/list: **11 tools**: connection_list, connection_remove, schema_inspect, sql_validate, sql_execute, investigation_start, investigation_record, investigation_get, knowledge_search, knowledge_propose, knowledge_review.
    - connection_list: both `commerce` and `fulfillment` with their scopes, and no secrets. So the server sees the same `SQLINVESTIGATOR_HOME` as the console.
    - Closing stdin ended the process with exit code 0. stderr was empty.

## Problems, confusing points, assumptions and workarounds

1. **Server name collision (README section 3).** A user-scope `sql-investigator` already exists. I could not remove it, because the user config is not mine. I followed the README's other option and named this one `sql-investigator-dev`. As a result, Phase 2 tools will be `mcp__sql-investigator-dev__*`, not the `mcp__sql-investigator__*` names used throughout the README. The README's permissions block has to be rewritten by hand for the new name. The initialize response still reports serverInfo name `sql-investigator`, so only the registration name tells the two apart.
2. **Pre-approval could not be written.** The README says that for scripted use you add `enabledMcpjsonServers` to `.claude/settings.local.json`. My session was not allowed to write under `.claude\`. **A human is needed**, either to approve `sql-investigator-dev` at the "approve project MCP server" prompt when Claude Code restarts, or to add that setting (and, optionally, the permissions block) themselves. If neither happens, the server may still be "Pending approval" after the restart and its tools will not load.
3. **Non-interactive password.** The README documents piping the password on stdin and suggests a secret store such as `Get-Secret`. There is no secret store here, and the DBA sent the passwords in plain text in `dba-email.txt`. I piped each one straight out of that file with `Select-String`. That keeps it off the command line and out of new files, but it does depend on the email's layout: commerce is the first `password:` line and fulfillment the second. The README does not say whether a trailing CR/LF on piped input is trimmed. PowerShell 5.1 adds a newline and it worked, so it is handled.
4. **PowerShell 5.1 pipe encoding (assumption).** PS 5.1 sends piped strings to native programs as ASCII through `$OutputEncoding`. The passwords are ASCII (they contain `-` and `#`), so this was fine. A non-ASCII password would probably be corrupted, and the README does not mention this.
5. **`--trust-server-certificate`.** The DBA said the certificate is self-signed, and the README says to use this flag in that case. I passed it from the start, so I did not see the error you get without it.
6. **Scope choice (assumption).** I approved exactly the tables the DBA granted, with all columns. The README doesn't say how to find out which columns exist before provisioning.
7. **Help text.** `provision --help` prints the same global help as `--help`, not text specific to provision. That works, but the README suggests each command has its own help.
8. **README vs. actual install.** The README's `Get-FileHash` command assumes the current directory is the download folder, and it only implies this. The data files are created lazily. Only `catalog.json` and `vault\` exist so far, which matches the README.
9. **README inconsistency.** The tools table says `connection_remove` does *not* delete the stored password. The console `remove` does delete it. This is documented, but it is easy to miss, and uninstall depends on it.
10. **Verification limits.** The README's step 3 check (`/mcp` showing 11 tools) can't be done from the command line. The hand-run stdio session is the closest equivalent, and it passed.

## Needs a human

- Approve the project-scope MCP server `sql-investigator-dev` when Claude Code restarts in `C:\sqlinv-eval\coldstart-2`, or add `"enabledMcpjsonServers": ["sql-investigator-dev"]` to `.claude\settings.local.json`. My write to that file was refused.
- Optionally, add the permissions block (with `mcp__sql-investigator-dev__` names) to `.claude\settings.json` so the read-only tools don't prompt.
