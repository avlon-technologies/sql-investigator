# Remediation (Phase F)

Every issue found during the mission is classified below and either fixed with regression coverage or recorded with a reason. None of the fixes needed an architecture change: no model, binding, acceptance scenario or ADR was touched, and `continuum package check` stayed intact throughout.

**When each issue was found matters.** R1–R4 were found by the implementation agent during release validation and the safety challenge, **before** the evaluator started. They are fixed in the release the evaluator received (`0c09c70`). E1 onwards come from the evaluator and are fixed **after** its run. The original cold-start result is preserved in `FINDINGS.md`.

## Found before the cold start (release validation and safety challenge)

| # | Issue | Class | Fix | Regression test | Commit |
|---|---|---|---|---|---|
| R1 | With `SQLINVESTIGATOR_HOME` under a folder that grants only Modify (any folder made directly under `C:\`), the MCP server **crashed at start-up** with `UnauthorizedAccessException`. `EnsurePrivateDirectory` always wrote the owner, which needs `WRITE_OWNER` | Product defect | Write the owner only when it is not already the current user. Any remaining failure becomes `store.inaccessible` with an action (on stderr at start-up), not an unhandled exception | `A_private_directory_can_be_made_under_a_folder_that_grants_only_modify` (fails before, passes after) | `2c345db` |
| R2 | A password piped from **Windows PowerShell 5.1** never worked: it sends a UTF-8 BOM, which became part of the stored password, so every login failed. The guide recommends piping | Product defect | Drop a leading U+FEFF from a piped secret | `A_piped_password_loses_a_leading_byte_order_mark` (fails before, passes after) | `75cb113` |
| R2b | `login.failed` said "the **stored** credential" during provisioning, before anything was stored, and its action did not say what to check | Product defect (message) | Neutral message; the action names the login and password and `--replace` | covered by PC6 in `SECURITY-RESULTS.md` | `75cb113` |
| R3 | Redaction ran over **serialized JSON text**. After `Password=` it swallowed the backslash of an escaped quote, so the document no longer parsed and the tool returned the SDK's generic "An error occurred invoking …" instead of the actionable error. Any row containing such text could fail the same way. **Fails closed:** no leak, nothing ran | Security-relevant product defect | Redact each string value and property name before serialization. A number or boolean equal to a registered secret becomes the mask | `An_error_quoting_a_connection_string_stays_an_actionable_redacted_error`, `Row_values_with_quotes_and_backslashes_after_a_password_key_keep_the_result_valid` (both fail before, pass after); safety check U3 | `5c67c6c` |
| R4 | The release zip from the **developer's working copy** differed from the CI build of the same commit: the working copy has LF endings where a checkout gets CRLF | Packaging defect | `eng/release.ps1` always builds from a clean worktree of HEAD; local and CI now give the same hash | the `Release build` workflow compares the hash (run 42215057504) | `0c09c70` |
| R5 | `connection_remove` described the retained secret only obliquely (the gap) | Documentation and tool-surface defect | Explicit description; `secretDeleted: false` and an `action` in the result | two unit tests (`GOVERNANCE.md`, B3) | `6cc3dd3` |

## CI and environment issues (not product defects)

| # | Issue | Class | Disposition |
|---|---|---|---|
| C1 | `Continuum` gate never started: GitHub billing refuses hosted runners | Evaluation environment / infrastructure | Moved to the self-hosted runner (`20da749`) |
| C2 | Both jobs failed after the runner restarted: Restricted PowerShell policy; `bash` resolved to WSL | Infrastructure | `PSExecutionPolicyPreference: Bypass` per job, and Git Bash first on PATH (`b1a4939`) |
| C3 | One acceptance run (`db43be2`, run 42212558840) failed b06 with "initialize (1) got no response in 60000 ms" while the PC was running two local release builds at once | Evaluation environment: flaky under load, not reproduced | The next runs at the same code passed. Watched in the final run (`FINAL-REPORT.md`) |
| C4 | Harness: the "slow" safety query finished in 0.9 s | Evaluation-harness defect | Predicate the optimizer cannot remove; thresholds unchanged |
| C5 | A commit (`6103f39`) was made locally with a failing new unit test: the assertion depended on the raw string's indentation | Process slip by the implementation agent | Fixed in `492e909` before anything was pushed, so CI never ran the failing state |

## Found by the cold-start evaluator (fixed after its run)

| # | Issue (evaluator's words, summarized) | Class | Disposition | Evidence |
|---|---|---|---|---|
| E1 | Server-name clash with an existing user-scope `sql-investigator`; tool and permission names follow the server name | Documentation defect | Guide: "Choose the server name with care". Says how names are formed, how to detect a clash with `claude mcp list`, and to rename or remove | `6103f39` |
| E2 | Project scope shows "Pending approval", not "✓ Connected"; no non-interactive approval | Documentation defect | Guide describes both states, the approval prompt, and `enabledMcpjsonServers` in `.claude/settings.local.json` | `6103f39` |
| E3 | `provision --help` rejected as an unknown option | Product defect (console) | `--help`, `-h` or `/?` anywhere prints the usage | `Help_after_a_command_prints_the_usage_and_mentions_stdin`; `6103f39`, `492e909` |
| E4 | `--help` says only "prompted for at the console", not stdin | Product defect (usage text) | Usage now says one line is read from redirected stdin | same test |
| E5 | `SQLINVESTIGATOR_HOME`: data folder or parent? | Documentation defect | Guide: it is the data folder itself, created for you, with no `data` subfolder | `6103f39` |
| E6 | Zip layout not stated; case-sensitive checksum comparison | Documentation defect | Guide: no top-level folder; a `-eq` one-liner that prints `True` | `6103f39` |
| E7 | `list` output has no header | Product usability | Usage and guide name the columns. The output format is unchanged, so scripts that parse it are not broken | `6103f39` |
| E8 | All result values are strings, and dates have a time part | Design, not a defect: lossless, invariant-culture values. **Documentation defect** that it was not said | Guide: "Result values are strings", with each type's rendering | this commit |
| E9 | A relationship proposal cannot be amended | **Architecture-level.** It would need a new message on `KnowledgeService`, which is a model change. Not pursued | Guide: propose a corrected relationship and reject the old one at review. Left for the owner as a possible `.adl-change` | — |
| E10 | The server exits at end of stdin without answering buffered requests | Product behaviour (the MCP SDK's stdio transport); it does not affect Claude Code | Documented in the guide. No change | — |
| E11 | A cross-database rejection adds noisy follow-on reasons about the table alias | Product usability, minor | Recorded, not changed: the reasons are accurate and the first one names the cause. Changing the validator's reason set was not worth the risk to a fail-closed component | — |
| E12 | `knowledge_search` returned no snapshot when called in parallel with the first `schema_inspect` | Evaluator usage: the calls raced. Not a defect; the next search showed it | No change | — |
| E13 | The evaluator wrote smoke-test files to `%TEMP%` | Evaluator limitation (self-reported) | Scored under security (−1) | `FINDINGS.md` |
| E14 | The evaluator miscounted its own queries and proposal evidence | Evaluator limitation | Scored under accuracy | `FINDINGS.md` |
| E15 | The evaluator took the truncated `SO-10006` as SO-100006 and reported SO-100060 missing with High confidence | Evaluator limitation (reasoning). The product returned the evidence needed to see both candidates | Scored. No product change | `FINDINGS.md` |

## Found by the post-remediation run (not fixed in this mission)

The post-remediation run (`FINDINGS.md`, second part) validated release `6551d99`. Changing the product after it would leave no validated release, so these are recorded, not fixed.

| # | Issue | Class | Disposition |
|---|---|---|---|
| N1 | The guide's tool and permission names all use `sql-investigator`. After the documented rename they have to be translated by hand | Documentation defect, minor | Open. Add a note next to the permissions block |
| N2 | The guide says `/mcp` shows 11 tools; with the recommended deny rules Claude Code shows 9. Both statements are in the guide, but they are not linked | Documentation defect, minor | Open |
| N3 | `provision --help` prints the global usage, not help for one command | Product usability, minor. It is the E3 fix working as designed | No change planned |
| N4 | Windows PowerShell 5.1 pipes strings as ASCII (`$OutputEncoding`), so a non-ASCII password would probably be corrupted. This is the evaluator's inference; it was not tested | Documentation gap (possible product defect) | Open. Test it, then document `$OutputEncoding = [Text.UTF8Encoding]::new($false)` or fix it |
| N5 | A proposal cannot be withdrawn, so a wrong one stays until review | Architecture-level, same as E9 | Left for the owner |
| N6 | Each result is sent twice, as JSON text and as `structuredContent` | Product behaviour (MCP compatibility for clients that read only text) | No change. Note it as a token cost |
| N7 | There is no helper for carrying values from one alias into an `IN (...)` on another | Product idea | Not pursued; it would widen the query surface |
