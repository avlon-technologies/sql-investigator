# SQL Investigator — user guide

SQL Investigator lets Claude Code investigate data in your SQL Server databases with bounded, read-only, audited queries. It is one local Windows program. Claude Code starts it as an MCP server, and you also run it at a console to set up connections.

This guide is the `README.md` inside the release zip. It covers installing, connecting databases, using SQL Investigator from Claude Code, and uninstalling.

## Requirements

- Windows 10 or 11, x64. The release is self-contained, so you do not need to install .NET.
- [Claude Code](https://docs.claude.com/en/docs/claude-code) (the `claude` command).
- Network access to a SQL Server (tested with SQL Server 2022) and, for each database, a **read-only SQL login**. See [Ask for a read-only login](#ask-for-a-read-only-login).
- Only **development or fixture** databases. SQL Investigator refuses to provision anything marked production. Production data needs your data owner's approval first.

## 1. Install

1. Download `SqlInvestigator-<version>-win-x64.zip` and its `.sha256` file.
2. Check the download. The two hashes must match:

   ```powershell
   (Get-FileHash .\SqlInvestigator-1.0.0-win-x64.zip -Algorithm SHA256).Hash
   Get-Content .\SqlInvestigator-1.0.0-win-x64.zip.sha256
   ```

3. Unzip it to a folder of your choice. The default used in this guide is `%LOCALAPPDATA%\Programs\SqlInvestigator`:

   ```powershell
   $dir = "$env:LOCALAPPDATA\Programs\SqlInvestigator"
   Expand-Archive .\SqlInvestigator-1.0.0-win-x64.zip -DestinationPath $dir
   Get-ChildItem $dir -Recurse | Unblock-File      # clear the "downloaded from the internet" mark
   & "$dir\SqlInvestigator.exe" --version          # prints e.g. 1.0.0+<commit>
   ```

`SqlInvestigator.exe --help` lists the console commands.

### Where it keeps data

SQL Investigator stores its data in `%LOCALAPPDATA%\SqlInvestigator\data`:

- `catalog.json`: connections
- `history.json`: investigations and the audit trail
- `knowledge.json`: schema snapshots and relationships
- `vault\`: encrypted passwords

Only your Windows account can read the folder. To keep the data somewhere else, set `SQLINVESTIGATOR_HOME` to a folder path. Use the same value both at the console and in the Claude Code registration (step 3), or the two will not see the same connections.

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
| `--trust-server-certificate` | Accept a self-signed TLS certificate. Typical for local and container SQL Servers; without it you get a certificate error |
| `--no-encrypt` | Do not require TLS |
| `--replace` | Re-provision an existing alias (new password, scope or server) |
| `--no-verify` | Skip the connection and read-only check, for offline set-up. The check then runs when the alias is first used |

The password is never accepted as a command-line argument. If you script provisioning, pipe it on standard input instead: the console reads one line when input is redirected.

**The login is checked before it is stored.** The console connects and refuses the login (`principal.unsafe`) if it can write, alter or execute, or if it is sysadmin, db_owner, db_datawriter or db_ddladmin. The check runs again the first time the server uses the alias.

Repeat for each database. Each database gets its own alias and its own read-only login.

```powershell
& "$dir\SqlInvestigator.exe" list                    # aliases, servers, scope (no passwords)
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

If you set `SQLINVESTIGATOR_HOME` or a budget (see [Limits](#limits)), pass it to the server with `-e`:

```powershell
claude mcp add sql-investigator --scope user -e SQLINVESTIGATOR_HOME=D:\sqlinv-data -- "$env:LOCALAPPDATA\Programs\SqlInvestigator\SqlInvestigator.exe"
```

Check the registration:

1. Run `claude mcp list`, which should show `sql-investigator ... ✓ Connected`.
2. Start, or restart, Claude Code. A server added while Claude Code is running is not loaded until the next start.
3. Type `/mcp`. Claude Code asks you to approve a project-scope server the first time.

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
| `connection_remove` | Removes an alias from the catalog. It does **not** delete the stored password; run `SqlInvestigator.exe remove --alias <name>` at a console for that |

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
| `alias.unknown` | The alias is not provisioned (`SqlInvestigator.exe list`), or the console and Claude Code use different `SQLINVESTIGATOR_HOME` values |
| `login.failed` | Wrong user or password. Re-provision with `--replace` |
| `server.unreachable` | Wrong server or port, firewall, or SQL Server not running. For a certificate error, add `--trust-server-certificate` |
| `principal.unsafe` | The login can do more than read. Ask for a SELECT-only login |
| `scope.unapproved` | The query names a table or column outside `--scope`. Re-provision with `--replace` and a wider scope if that access is approved |
| `sql.rejected` | Not one bounded SELECT; the reasons say what to change |
| `permission.denied` | SQL Server refused the read: the login lacks SELECT on that object |
| `database.unavailable` | The login is not mapped to a user in that database, or the database name is wrong |
| `environment.unapproved` | Only `fixture` and `development` are accepted |
| `credential.unavailable` | The stored password is missing, or was stored by another Windows user. Re-provision as yourself |
| `budget.*` | A limit was reached; see [Limits](#limits) |

## Uninstall

1. Remove each alias and its stored password: `SqlInvestigator.exe list`, then `SqlInvestigator.exe remove --alias <name>` for each.
2. `claude mcp remove sql-investigator --scope user`, or `--scope project` in that project.
3. Delete the program folder, for example `%LOCALAPPDATA%\Programs\SqlInvestigator`.
4. Delete the data folder, `%LOCALAPPDATA%\SqlInvestigator\data` (or your `SQLINVESTIGATOR_HOME`). This deletes your saved investigations and relationship knowledge too.

SQL Investigator writes nothing to the registry or outside these folders.
