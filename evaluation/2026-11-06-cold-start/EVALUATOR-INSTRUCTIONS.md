# Evaluator instructions and isolation (Phase D)

## Who the evaluator was

The evaluator was a **separate Claude Code process** (`claude -p`, Claude Code 2.1.319), started by [`evidence/run-evaluator.sh`](evidence/run-evaluator.sh), with a **fresh context**. It received only its prompt, which pointed at `TASK.md`. It had no access to the implementation conversation.

Its model is the one recorded in the `init` line of its transcript (see `EXECUTION-LOG.md`). It is the same model family as the implementation agent. That is a limitation: the evaluator is a different session with no shared context, not a different kind of reviewer.

Launching it needed the owner's go-ahead. The auto-mode safety check first blocked a nested agent with shell access, and the owner approved the launch in this session.

## What it received

All of it was in `C:\sqlinv-eval\coldstart-1`, outside the repository:

| File | What it is |
|---|---|
| `TASK.md` | the brief, copied in [`evidence/evaluator-TASK.md`](evidence/evaluator-TASK.md): machine constraints, Phase 1 (install and connect), Phase 2 (the manager's investigation request, verbatim from the mission), and the report format |
| `downloads/SqlInvestigator-1.0.0-win-x64.zip` and `.sha256` | the release, `sha256 6ad28140…`, commit `0c09c70` |
| `downloads/SqlInvestigator-README.md` | `docs/USER-GUIDE.md`, the same file as the README inside the zip |
| `downloads/dba-email.txt` | server, databases, the two read-only logins and the tables granted, written as a DBA would. Copied with the passwords removed in [`evidence/evaluator-dba-email.redacted.txt`](evidence/evaluator-dba-email.redacted.txt) |

## What it did not receive

- The repository, `README.md`, `VISION.md`, `CLAUDE.md`, implementation notes and reports
- The ADL and BDL models, the bindings, the development package, the ADRs
- The acceptance scenarios, the adapter, any test or assertion
- The fixture SQL, the fixture truth, the scoring rubric, any expected answer
- SQL statements to run or an investigation sequence

## Isolation measures

| Measure | How |
|---|---|
| Fresh context | a new `claude -p` process. Phase 2 resumes the evaluator's **own** Phase 1 session (`--resume`), which models a user restarting Claude Code after `claude mcp add` |
| No MCP servers it did not register | `--strict-mcp-config`. Phase 1 had none. Phase 2 loaded only the `.mcp.json` the evaluator wrote itself in Phase 1 |
| No user settings, hooks or permissions | `--setting-sources project,local` |
| Read access denied to anything that could leak the answer | deny rules on `C:\projects\**` (the repo), this session's scratchpad (fixture SQL, truth, rubric), this session's transcripts, `%LOCALAPPDATA%\SqlInvestigator\**` (the developer's install, data and the fixture admin password), `C:\actions-runner\**` and the smoke-test folder |
| Same tool permissions as the README recommends | Phase 2 allowed the nine read and record tools. `knowledge_review` and `connection_remove` were denied: those record a human's decision |
| Shell commands | Bash and PowerShell were allowed, because installing needs them. Path deny rules do not bind shell commands, so **both transcripts were audited** for any path outside the workspace (`EXECUTION-LOG.md`) |
| Clean environment | **No new Windows profile** (the session is not elevated). Instead: same Windows user, a fresh install folder and data folder (`SQLINVESTIGATOR_HOME`), project-scope registration only, and the user-level Claude Code config left untouched (the brief asked for this) |

## Where the evaluator had to differ from a real user

- **It could not type a password.** The brief told it to use a documented non-interactive way, and the guide documents piping the password on stdin. A human would type it at the prompt.
- **The brief told it to install in a non-default folder, use `SQLINVESTIGATOR_HOME` and register at project scope**, because the machine already has a developer copy. That exercises documented options a first-time user might not need.
- **The two databases share one SQL Server**, with separate logins and no cross-database rights. The databases are independently authorized, but the server is not independent.
