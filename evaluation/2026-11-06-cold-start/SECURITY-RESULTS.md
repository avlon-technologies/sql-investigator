# Safety challenge (Phase E)

**Result: 79 of 79 MCP checks and 7 of 7 console provisioning checks pass, against the installed release.** Three defects were found on the way and fixed before the final run (`REMEDIATION.md`, R2–R4).

- **Target:** the release zip, installed in `C:\sqlinv-eval\safety\bin` with its own `SQLINVESTIGATOR_HOME`. Final MCP run: `1.0.0+5c67c6c` at 2026-11-06T12:01Z. The code is identical to the evaluated `0c09c70`, which changed only `eng/release.ps1`. Console checks: `1.0.0+75cb113`.
- **Fixture:** a separate disposable database, `SafetyProbe`, with synthetic data ([`evidence/safety-fixture.sql`](evidence/safety-fixture.sql)). It was not the evaluator's databases, and never production or employer data.
  - `safety_ro` holds SELECT on `dbo.Orders`, `dbo.Big` and `dbo.Hidden` only.
  - `safety_rw` is in `db_datawriter`.
  - The alias `probe` is scoped to `dbo.Orders(OrderId,Status)`, `dbo.Big` and `dbo.NoGrant`. So `dbo.Hidden` is readable by the login but outside the scope, and `dbo.NoGrant` is inside the scope but not readable by the login. That separates the two barriers.
- **Harness:** [`evidence/safety-challenge.mjs`](evidence/safety-challenge.mjs) drives the real executable over MCP stdio. Raw results are in [`evidence/safety-results.json`](evidence/safety-results.json), with no credentials and no raw sensitive rows.

## The two barriers, tested independently

- **The allowlist on its own** (groups *allowlist*, *scope* and *cross-db*). Every statement was rejected by both `sql_validate` and `sql_execute` (`sql.rejected`) before reaching SQL Server. That includes S1, a table the login *can* read but the scope excludes, so the rejection cannot have come from the principal.
- **The principal on its own** (group *principal*, P1–P8). The same writes, DDL, `SELECT INTO`, `xp_cmdshell` and a cross-database read were sent **directly to SQL Server as `safety_ro`**, bypassing SQL Investigator. SQL Server denied each one (Msg 229, 262, 1088, 916).
  - P9 confirms the data and the table count are unchanged afterwards.
  - P10: a statement the allowlist accepts, on a scoped table the login cannot read, is refused by SQL Server (`permission.denied`).
  - P11: a writer login stored with `--no-verify` is refused at first use (`principal.unsafe`).

## Console provisioning (local console, password piped on stdin)

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| PC1 | read-only login | provisioned after the read-only check | `Provisioned alias "probe"` | PASS |
| PC2 | `db_datawriter` login | refused | `principal.unsafe: … member of db_datawriter; holds a database-level write, DDL or execute permission; can write or alter dbo.Orders` | PASS |
| PC3 | writer with `--no-verify` | stored, refused at first use | stored; first use → `principal.unsafe` (P11) | PASS |
| PC4 | `--environment production` | refused | `environment.unapproved` | PASS |
| PC5 | `--password` on the command line | refused | `input.invalid: A secret is never accepted as an argument.` | PASS |
| PC6 | wrong password | refused, nothing stored, no echo | `login.failed … Nothing was provisioned.` | PASS |
| PC7 | login not mapped in the target database | refused | `database.unavailable … (error 4060)` | PASS |

**First attempt:** PC1 and PC2 failed with `login.failed` despite the correct passwords. Windows PowerShell 5.1 pipes a UTF-8 byte-order mark to native programs, and it became part of the password. Fixed (R2), then re-run as above.

## MCP checks

### surface

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| T1 | tools/list | exactly 11 tools, no credential parameters | connection_list,connection_remove,schema_inspect,sql_validate,sql_execute,investigation_start,investigation_record,investigation_get,knowledge_search,knowledge_propose,knowledge_re | PASS |
| T2 | stdout carries only JSON-RPC | no non-JSON line on stdout | 0 non-JSON lines | PASS |

### control

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| C0 | allowed SELECT | executes, 2 rows | ok rows=2 | PASS |

### allowlist

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| W1 | INSERT | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W2 | UPDATE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W3 | DELETE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W4 | MERGE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W5 | DROP TABLE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W6 | CREATE TABLE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W7 | TRUNCATE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W8 | EXEC xp_cmdshell | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W9 | SELECT INTO | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W10 | SELECT then DELETE in one batch | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W11 | CTE feeding DELETE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W12 | SELECT followed by WAITFOR | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W13 | GRANT | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W14 | GO batch separator smuggling | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W15 | exclusive lock hint | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W16 | SET option | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| W17 | BEGIN TRAN / COMMIT | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S1 | table outside scope (login CAN read it) | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S2 | unapproved column | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S3 | SELECT * on a column-limited table | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S4 | unapproved column through a table alias | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S5 | unapproved column through a derived table | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S6 | unapproved column used only in WHERE (oracle) | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S7 | catalog view sys.tables | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S8 | INFORMATION_SCHEMA | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S9 | one-part name | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S10 | join to out-of-scope table | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S11 | unapproved column in correlated subquery | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| S12 | unapproved column in ORDER BY | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X1 | three-part cross-database name | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X2 | four-part linked-server name | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X3 | OPENROWSET | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X4 | OPENQUERY | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X5 | OPENDATASOURCE | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| X6 | three-part name to own database | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F1 | @@VERSION global | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F2 | SUSER_SNAME() | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F3 | HAS_PERMS_BY_NAME | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F4 | subquery on sys.objects | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F5 | variable | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F6 | user-defined function | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F7 | QUOTED_IDENTIFIER-ambiguous double quotes | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F8 | temp table | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F9 | OPENJSON rowset over a literal | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |
| F10 | fn_my_permissions | rejected by sql_validate and sql_execute (sql.rejected) | validate accepted=false; execute sql.rejected | PASS |

### allowlist-positive

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| A1 | comment containing a semicolon and DELETE | accepted and executed | ok rows=50 | PASS |
| A2 | aggregates and CAST | accepted and executed | ok rows=1 | PASS |

### principal

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| P1 | INSERT as safety_ro, direct | SQL Server denies it | Msg 229, Level 14, State 5, Server 88ad9cfbedbd, Line 1 | PASS |
| P2 | UPDATE as safety_ro, direct | SQL Server denies it | Msg 229, Level 14, State 5, Server 88ad9cfbedbd, Line 1 | PASS |
| P3 | DELETE as safety_ro, direct | SQL Server denies it | Msg 229, Level 14, State 5, Server 88ad9cfbedbd, Line 1 | PASS |
| P4 | CREATE TABLE as safety_ro, direct | SQL Server denies it | Msg 262, Level 14, State 1, Server 88ad9cfbedbd, Line 1 | PASS |
| P5 | ALTER TABLE as safety_ro, direct | SQL Server denies it | Msg 1088, Level 16, State 13, Server 88ad9cfbedbd, Line 1 | PASS |
| P6 | SELECT INTO as safety_ro, direct | SQL Server denies it | Msg 262, Level 14, State 1, Server 88ad9cfbedbd, Line 1 | PASS |
| P7 | xp_cmdshell as safety_ro, direct | SQL Server denies it | Msg 229, Level 14, State 5, Server 88ad9cfbedbd, Procedure master.dbo.xp_cmdshell, Line 1 | PASS |
| P8 | read another database as safety_ro, direct | SQL Server denies it | Msg 916, Level 14, State 2, Server 88ad9cfbedbd, Line 1 | PASS |
| P9 | data unchanged after every write attempt | 50 rows, all Open, no new tables | 50,50; tables=4 | PASS |
| P10 | in-scope table the login has no SELECT on (allowlist passes, principal refuses) | permission.denied | permission.denied | PASS |
| P11 | writer login stored with --no-verify, first use | principal.unsafe, nothing executed | principal.unsafe | PASS |

### access

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| U1 | unknown alias | alias.unknown | alias.unknown | PASS |
| U2 | alias refused at provisioning was never stored | alias.unknown | alias.unknown | PASS |
| U3 | connection string passed as an alias | input.invalid | input.invalid | PASS |
| U4 | schema_inspect of out-of-scope object | scope.unapproved | scope.unapproved | PASS |

### disclosure

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| D1 | connection_list | no password, no connection string | aliases=probe,probe-rwnv | PASS |
| D2 | query that selects the alias password as a literal | password redacted from the result | redacted | PASS |
| D3 | SQL error message that would contain the password | password redacted from the error | sql.error | PASS |
| D4 | scan of 127 MCP responses, all stderr and 5 data files for every password | no password anywhere outside the DPAPI blobs | none found | PASS |

### drift

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| R1 | column renamed under a running investigation | actionable error for the stale column; drift reported by schema_inspect | query sql.error; drift={"added":["dbo.Orders(OrderId int)"],"removed":["dbo.Orders(OrderId int, Status nvarchar(20))","missing: dbo.Orders.Status"],"previousCapturedAt":"2026-11-06 | PASS |

### budget

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| B1 | MAX_QUERY_ROWS=10 over 3000 rows | truncated to 10, truncatedBy rows | rows=10 truncated=true by=rows | PASS |
| B2 | MAX_RESULT_BYTES=20000 over ~2.4 MB | truncated, truncatedBy bytes | rows=49 truncated=true by=bytes | PASS |
| B3 | malformed MAX_QUERY_ROWS=-5 | falls back to the default 500, not unlimited | rows=500 truncated=true | PASS |
| B4 | SQL_TIMEOUT_SECONDS=2 on a long query | budget.timeout near 2 s, and nothing left running on the server | budget.timeout after 2.5s; requests still running=0 | PASS |
| B5 | MAX_CONCURRENT_QUERIES=1 with a second query while one runs | second refused with budget.concurrency | second=budget.concurrency; first=budget.timeout | PASS |
| B6 | MAX_QUERIES_PER_INVESTIGATION=2, three queries | third refused with budget.investigation | ok,ok,budget.investigation | PASS |
| B7 | MAX_SQL_LENGTH=100 with a longer statement | sql.rejected | sql.rejected | PASS |

### cancellation

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| K1 | MCP notifications/cancelled during a running query | the server-side query stops promptly | running before=1, after=0 within 0.4s; late response=none | PASS |

### audit

| ID | Challenge | Expected | Observed | Result |
|---|---|---|---|---|
| AU1 | rejections, budgets and refusals are audited | history.json audit has sql.rejected, budget.* and principal/permission events | {"sql.started":32,"sql.succeeded":21,"sql.rejected":188,"sql.failed":11,"investigation.started":4,"budget.rows":4,"budget.bytes":2,"budget.concurrency":2,"budget.investigation":2," | PASS |

## First-run failures and how they were classified

The first full run, against `75cb113`, scored 76 of 79.

| ID | First result | Classification | Disposition |
|---|---|---|---|
| U3 | A connection string as the alias returned the SDK's generic "An error occurred invoking 'schema_inspect'" instead of `input.invalid` | **Product defect.** Redaction applied to serialized JSON broke the document. It failed closed: nothing leaked, nothing ran | Fixed (R3), with regression tests; now `input.invalid`, with the password masked in the message |
| B4, K1 | The "slow" probe query completed in 0.9 s, so there was nothing to time out or cancel | **Evaluation-harness defect.** SQL Server optimized the `COUNT_BIG` over a cross join | The probe now carries a predicate the optimizer cannot remove. B4 times out at 2.5 s with nothing left running. K1: a server-side request was running, and it stopped within 0.4 s of `notifications/cancelled` |

The thresholds were not changed between runs.

## Not covered

- **Timeout and cancellation are tested against one SQL Server.** The audit shows `budget.timeout` and `cancelled`, and `sys.dm_exec_requests` shows the server-side request gone.
- **Local-user trust boundary.** Anyone running as the same Windows user can edit `catalog.json` to widen an alias's scope. The read-only principal still bounds what can be done, and this is the accepted design: local JSON, single user.
- **Data exfiltration by design.** A permitted SELECT returns data in scope to Claude Code. That is the product's purpose, bounded by scope, rows and bytes. Production data stays prohibited.
