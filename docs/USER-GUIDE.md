# SQL Investigator — user guide

SQL Investigator lets Claude Code investigate data in your SQL Server databases with bounded, read-only, audited queries. It is one local program for Windows or macOS. Claude Code starts it as an MCP server, and you also run it at a console to set up connections.

This guide is the `README.md` inside the release zip. The examples use Windows paths and PowerShell; on a Mac, read [SQL Investigator on macOS](MACOS.md) alongside it (install, keychain, data folder, troubleshooting). It covers installing, connecting databases, using SQL Investigator from Claude Code, and uninstalling.

## Requirements

- Windows 10 or 11, x64, or macOS 15 or later on Apple silicon or Intel ([macOS notes](MACOS.md)). The release is self-contained, so you do not need to install .NET.
- [Claude Code](https://docs.claude.com/en/docs/claude-code) (the `claude` command).
- Network access to a SQL Server (tested with SQL Server 2022) and, for each database, a **read-only SQL login**. See [Ask for a read-only login](#ask-for-a-read-only-login).
- Only **development or fixture** databases. SQL Investigator refuses to provision anything marked production. Production data needs your data owner's approval first.

## 1. Install

1. Download `SqlInvestigator-<version>-win-x64.zip` and its `.sha256` file.
2. Check the download. This prints `True` when the hash matches. PowerShell's `-eq` ignores case, which matters because `Get-FileHash` prints uppercase and the `.sha256` file is lowercase:

   ```powershell
   (Get-FileHash .\SqlInvestigator-1.0.0-win-x64.zip -Algorithm SHA256).Hash -eq (Get-Content .\SqlInvestigator-1.0.0-win-x64.zip.sha256).Split(' ')[0]
   ```

3. Unzip it to a folder of your choice. The zip has no top-level folder, so `SqlInvestigator.exe` lands directly in the folder you choose. This guide uses `%LOCALAPPDATA%\Programs\SqlInvestigator`:

   ```powershell
   $dir = "$env:LOCALAPPDATA\Programs\SqlInvestigator"
   Expand-Archive .\SqlInvestigator-1.0.0-win-x64.zip -DestinationPath $dir
   Get-ChildItem $dir -Recurse | Unblock-File      # clear the "downloaded from the internet" mark
   & "$dir\SqlInvestigator.exe" --version          # prints e.g. 1.0.0+<commit>
   ```

`SqlInvestigator.exe --help` lists the console commands and options. So does `--help` after any command, such as `provision --help`.

### Where it keeps data

SQL Investigator stores its data in `%LOCALAPPDATA%\SqlInvestigator\data`:

- `catalog.json`: connections
- `history.json`: investigations and the audit trail
- `knowledge.json`: schema snapshots and relationships
- `vault\`: encrypted passwords

Only your Windows account can read the folder. The files are created when first needed: `catalog.json` when you provision, the others on first use. To keep the data somewhere else, set `SQLINVESTIGATOR_HOME` to the data folder itself. The files go directly into it, with no `data` subfolder, and the folder is created for you. Use the same value both at the console and in the Claude Code registration (step 3), or the two will not see the same connections.

```powershell
$env:SQLINVESTIGATOR_HOME = "D:\sqlinv-data"   # optional; this console session only
```

## 2. Connect a database (at your console, not in Claude Code)

Connections and passwords never pass through Claude Code or chat. You add each one yourself:

```powershell
& "$dir\SqlInvestigator.exe" provision --alias commerce `
    --server "sqldev01,1433" --database Commerce --user commerce_ro `
    --environment development `
    --scope dbo.Orders --scope "dbo.Customers(CustomerId,Region)"
# Password for commerce_ro on sqldev01,1433/Commerce:   (typed, not shown)
```

| Option | Meaning |
|---|---|
| `--alias <name>` | The name Claude Code uses for this database: a letter, then up to 63 letters, digits, `-` or `_` |
| `--server <host[,port]>` | SQL Server address, for example `localhost`, `127.0.0.1,1433` or `sqldev01\INST` |
| `--database <name>` | The database |
| `--user <login>` | A SQL login. You are prompted for its password. Alternatively `--windows-auth` uses your Windows account |
| `--environment fixture\|development` | Required. `production` is refused |
| `--scope <schema.object>` | Required, repeatable. **The only objects that can be queried.** `dbo.Orders` approves all its columns; `dbo.Orders(OrderId,Status)` approves only those |
| `--trust-server-certificate` | Accept a self-signed TLS certificate. **Needed for local, container and most test SQL Servers**, which use one. Without it, `provision` fails with `server.unreachable` and a message that the encrypted (TLS) connection could not be set up. Leave it off for a server whose certificate this machine trusts |
| `--no-encrypt` | Do not require TLS |
| `--replace` | Re-provision an existing alias (new password, scope or server) |
| `--no-verify` | Skip the connection and read-only check, for offline set-up. The check then runs when the alias is first used |

The password is never accepted as a command-line argument. If you script provisioning, pipe it on standard input instead: when input is redirected, the console reads one line and still prints the prompt. Pipe it from something that keeps it out of your command history, such as a secret store, rather than typing it into the command:

```powershell
# PowerShell SecretManagement, if you use it; any command that writes the password to stdout works
Get-Secret commerce_ro -AsPlainText | & "$dir\SqlInvestigator.exe" provision --alias commerce ...
```

**The login is checked before it is stored.** The console connects and refuses the login (`principal.unsafe`) if it can write, alter or execute, or if it is sysadmin, db_owner, db_datawriter or db_ddladmin. The check runs again the first time the server uses the alias.

Repeat for each database. Each database gets its own alias and its own read-only login.

```powershell
& "$dir\SqlInvestigator.exe" list                    # tab-separated: alias, server/database, auth, environment, scope, provisioned at
& "$dir\SqlInvestigator.exe" remove --alias commerce # remove the alias AND delete its stored password
```

### Ask for a read-only login

Give your DBA something like this, one login per database, with SELECT on just the tables you need:

```sql
CREATE LOGIN commerce_ro WITH PASSWORD = '<strong password>';
USE Commerce;
CREATE USER commerce_ro FOR LOGIN commerce_ro;
GRANT SELECT ON dbo.Orders TO commerce_ro;
GRANT SELECT ON dbo.Customers TO commerce_ro;
```

## 3. Register it with Claude Code

For all your projects (user scope):

```powershell
claude mcp add sql-investigator --scope user -- "$env:LOCALAPPDATA\Programs\SqlInvestigator\SqlInvestigator.exe"
```

For one project only (writes `.mcp.json` in the current folder, which you can commit or keep local):

```powershell
claude mcp add sql-investigator --scope project -- "$env:LOCALAPPDATA\Programs\SqlInvestigator\SqlInvestigator.exe"
```

**Choose the server name with care.** The name (`sql-investigator` above) becomes part of every tool name, as in `mcp__<name>__sql_execute`. If `claude mcp list` already shows a `sql-investigator` in another scope (for example, another copy with other data), the two collide and Claude Code uses only one of them. Either remove the one you do not want, or give this one another name, such as `sql-investigator-dev`, and use that name in the permission entries below.

If you set `SQLINVESTIGATOR_HOME` or a budget (see [Limits](#limits)), pass it to the server with `-e`:

```powershell
claude mcp add sql-investigator --scope user -e SQLINVESTIGATOR_HOME=D:\sqlinv-data -- "$env:LOCALAPPDATA\Programs\SqlInvestigator\SqlInvestigator.exe"
```

Check the registration:

1. Run `claude mcp list`. A user-scope server shows `✓ Connected`. A new project-scope server shows `Pending approval` until you approve it. That is expected.
2. Start, or restart, Claude Code in that folder. A server added while Claude Code is running is not loaded until the next start. For a project-scope server, Claude Code asks you to approve it: accept.
3. Type `/mcp` and check that the server is connected with 11 tools.

To approve a project-scope server without the prompt, for example for scripted use, add `"enabledMcpjsonServers": ["sql-investigator"]` to `.claude/settings.local.json` in that project.

Recommended permissions (`.claude/settings.json` in your project, or `~/.claude/settings.json`). They let the read-only tools run without asking and ask before the two tools that record your decisions:

```json
{
  "permissions": {
    "allow": [
      "mcp__sql-investigator__connection_list", "mcp__sql-investigator__schema_inspect",
      "mcp__sql-investigator__sql_validate", "mcp__sql-investigator__sql_execute",
      "mcp__sql-investigator__investigation_start", "mcp__sql-investigator__investigation_record",
      "mcp__sql-investigator__investigation_get", "mcp__sql-investigator__knowledge_search",
      "mcp__sql-investigator__knowledge_propose"
    ],
    "ask": ["mcp__sql-investigator__connection_remove", "mcp__sql-investigator__knowledge_review"]
  }
}
```

## 4. Investigate

Ask Claude Code a question in plain language, for example:

> Using sql-investigator, which orders in `commerce` have no shipment in `fulfillment`? Save the investigation.

Claude Code uses these tools:

| Tool | What it does |
|---|---|
| `connection_list` | Your aliases and their approved scope (never a password) |
| `schema_inspect` | Approved tables and columns of an alias, and whether the schema changed since it was last seen |
| `sql_validate` | Would this SELECT be accepted? If not, why |
| `sql_execute` | Runs one bounded SELECT on one alias. Returns a query id, the exact SQL, timestamps, rows, and whether the result was truncated |
| `investigation_start` / `investigation_record` / `investigation_get` | Save the question, hypotheses, findings and uncertainty, citing query ids. Reopen it later by id. Result rows are not stored |
| `knowledge_search` / `knowledge_propose` | Look up schema snapshots and relationships; propose a relationship between databases with its evidence |
| `knowledge_review` | Records **your** verdict (accepted or rejected) on a proposed relationship. Claude Code should only call it when you have decided |
| `connection_remove` | Removes an alias from the catalog. It does **not** delete the stored password; run `SqlInvestigator remove --alias <name>` (`SqlInvestigator.exe` on Windows) at a console for that. This still works after `connection_remove`: it deletes the password even when the alias is no longer in the catalog |

Good to know:

- **Result values are strings.** Each row is a list of strings in invariant culture, so decimals and big integers come back exactly. `NULL` is `null`, `bit` is `"true"`/`"false"`, and dates and times are ISO 8601; a `date` column comes back as midnight, for example `2026-09-14T00:00:00.0000000`. `schema_inspect` gives each column's SQL type.
- **`knowledge_review` and `connection_remove` record your decisions.** With the recommended permissions, Claude Code asks you before using them. If your permissions deny them, Claude Code does not see them at all.
- **A proposal cannot be edited.** To correct a proposed relationship, propose a corrected one; when you review them, reject the old one with a note.
- **The server answers only while its input is open.** When you test it by hand with piped JSON-RPC, it stops at end of input without answering requests still in flight. Keep input open, as Claude Code does.

What it will and will not run:

- **Exactly one SELECT per call**, over the approved tables and columns of one alias, using two-part names (`dbo.Orders`) and built-in functions only. `SELECT *` is not allowed on a table whose scope lists columns.
- **No** INSERT/UPDATE/DELETE, DDL, EXEC, variables, temp tables, `SELECT INTO`, multiple statements, three- or four-part names, linked servers, `OPENROWSET` or `OPENQUERY`. They are rejected with the reason (`sql.rejected`) and audited.
- **Databases are not joined in SQL.** To compare two databases, Claude Code queries each alias separately and compares the results itself.
- Every query also runs inside a transaction that is always rolled back, and the read-only login is the final barrier.

### Limits

Set limits as environment variables on the server, with `-e` in `claude mcp add`. A malformed value falls back to the default, never to unlimited.

| Variable | Default | When exceeded |
|---|---|---|
| `SQL_TIMEOUT_SECONDS` | 15 | Query cancelled, `budget.timeout` |
| `MAX_QUERY_ROWS` | 500 | Result truncated, `truncatedBy: "rows"` |
| `MAX_RESULT_BYTES` | 1048576 | Result truncated, `truncatedBy: "bytes"` |
| `MAX_CONCURRENT_QUERIES` | 2 | Refused, `budget.concurrency` |
| `MAX_QUERIES_PER_INVESTIGATION` | 50 | Refused, `budget.investigation` |
| `CONNECT_TIMEOUT_SECONDS` | 10 | `server.unreachable` |
| `MAX_SQL_LENGTH` | 20000 | `sql.rejected` |
| `SCHEMA_MAX_AGE_SECONDS` | 900 | The schema is recaptured |
| `HISTORY_RETENTION_DAYS` | 90 | Older investigations are dropped |

Diagnostics go to stderr as JSON lines (`SQLINVESTIGATOR_LOG_LEVEL`, default `Information`). Passwords are redacted from every result and log.

## Troubleshooting

Every error has a `code`, a `message` and an `action` that says what to do.

| Code | Usual cause |
|---|---|
| `alias.unknown` | The alias is not provisioned (`SqlInvestigator list`), or the console and Claude Code use different `SQLINVESTIGATOR_HOME` values |
| `login.failed` | Wrong user or password. Re-provision with `--replace` |
| `server.unreachable` | Wrong server or port, firewall, or SQL Server not running. If the message says the **encrypted (TLS) connection could not be set up**, the server answered but its certificate is not trusted: for a local, container or test server, provision again with `--trust-server-certificate` |
| `principal.unsafe` | The login can do more than read. Ask for a SELECT-only login |
| `scope.unapproved` | The query names a table or column outside `--scope`. Re-provision with `--replace` and a wider scope if that access is approved |
| `sql.rejected` | Not one bounded SELECT; the reasons say what to change |
| `permission.denied` | SQL Server refused the read: the login lacks SELECT on that object |
| `database.unavailable` | The login is not mapped to a user in that database, or the database name is wrong |
| `environment.unapproved` | Only `fixture` and `development` are accepted |
| `credential.unavailable` | The stored password is missing or cannot be read: on Windows, it was stored by another Windows user; on macOS, the keychain item belongs to an earlier build or the keychain is locked ([macOS notes](MACOS.md#troubleshooting)). Re-provision as yourself |
| `budget.*` | A limit was reached; see [Limits](#limits) |

## Uninstall

1. Remove each alias and its stored password: `SqlInvestigator list`, then `SqlInvestigator remove --alias <name>` for each (`SqlInvestigator.exe` on Windows).
2. `claude mcp remove sql-investigator --scope user`, or `--scope project` in that project.
3. Delete the program folder, for example `%LOCALAPPDATA%\Programs\SqlInvestigator`.
4. Delete the data folder, `%LOCALAPPDATA%\SqlInvestigator\data` (or your `SQLINVESTIGATOR_HOME`). This deletes your saved investigations and relationship knowledge too.

SQL Investigator writes nothing to the registry or outside these folders.
