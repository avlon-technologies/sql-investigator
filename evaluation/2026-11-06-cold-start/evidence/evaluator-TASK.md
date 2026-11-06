# Your task: evaluate SQL Investigator from a cold start

You are a developer who has just been told about **SQL Investigator**, a tool that lets Claude Code investigate SQL Server databases. You have never used it. Everything you have been given is in `downloads/`:

- `SqlInvestigator-1.0.0-win-x64.zip` and `.sha256`: the release you downloaded
- `SqlInvestigator-README.md`: the product's documentation, the same as the README inside the zip
- `dba-email.txt`: what your DBA sent you, two read-only logins

There is no one to ask. Use only the documentation and what the program tells you. If something is unclear, missing or wrong, **write it down**, work around it if you can, and keep going.

## This machine (shared, so please respect these)

- Install the program under `C:\sqlinv-eval\coldstart-1\install`, not the default location: another copy is in use on this machine.
- Keep SQL Investigator's data in `C:\sqlinv-eval\coldstart-1\home`, using the documented `SQLINVESTIGATOR_HOME` setting, everywhere you need it.
- Register the MCP server at **project scope** in `C:\sqlinv-eval\coldstart-1`. The user-level Claude Code configuration belongs to someone else, so do not change it.
- Stay inside `C:\sqlinv-eval\coldstart-1`. Do not read other folders on this machine, other programs' data, source code, or any other credentials.
- You may not modify either database. You have no administrator access to SQL Server, and do not look for any.
- You are running non-interactively, so you cannot type into a password prompt. If the documentation offers a non-interactive way to provide one, use it. Never pass a password as a program argument, copy it into a file you create, or put it in the report.

## Phase 1: install and connect (this session)

1. Verify and install the release.
2. Provision both databases as two aliases, at the console, as the documentation describes.
3. Register the MCP server with Claude Code at project scope.
4. Confirm the registration as far as you can from the command line.
5. Write `PHASE1-NOTES.md`. List every command you ran and its outcome, every failed command, confusing instruction, undocumented assumption and workaround, and anything you would have needed a human for.

Then stop and reply exactly `READY FOR RESTART`. An MCP server registered during a session is only loaded when Claude Code starts again, so your session will be restarted with it loaded, and you will continue in Phase 2.

## Phase 2: the investigation (after the restart)

1. Confirm that the SQL Investigator tools are available, and look at the schemas.
2. Then do this request from your manager:

> Our commerce and fulfillment systems appear to disagree about order fulfillment.
>
> Investigate both databases and determine which orders appear to have fulfillment inconsistencies.
>
> Identify missing shipments, unmatched records, duplicate references, and suspicious delays where the evidence supports those conclusions.
>
> Explain which records you examined, what queries support your findings, and which conclusions remain uncertain.
>
> Do not modify either database.
>
> Save the investigation so it can be revisited later.
>
> If you discover relationships between the databases, propose them for human review rather than assuming they are established facts.

How you investigate is up to you. Today is 2026-11-06.

3. Write `REPORT.md` in `C:\sqlinv-eval\coldstart-1` with these sections:
   - **Findings**: each with the order numbers and other record ids involved, the query ids that support it, and your confidence.
   - **Uncertain or unresolved**: what you could not establish, and why.
   - **Records examined**: what you looked at.
   - **Saved investigation**: its investigation id. Confirm you reopened it with `investigation_get`.
   - **Proposed relationships**: their relationship ids.
   - **Experience log**: installation and onboarding, tool discovery and usability, errors you hit and how you recovered, and documentation problems.
   - **Ratings** (1–10, with one line of reasons each): installation and onboarding, tool usability, documentation, error messages, and overall experience.

Then reply `DONE` followed by a short summary.
