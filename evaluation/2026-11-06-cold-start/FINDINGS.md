# Cold-start findings and score (Phase D4)

**Original cold-start score: 80.8 / 100.**

- Release `0c09c70` (`sha256 6ad28140…`). Evaluator: a separate Claude Code session, model `claude-opus-5-5`, session `5a27ec3f-53eb-4240-8d97-261e7d192db4`.
- Result: it installed, connected two databases and completed an evidence-backed investigation, with **9 of 9 seeded findings**, **2 of 3 ambiguities**, **all 6 traps avoided**, and one overconfident conclusion.

The comparison with the fixture truth was made only **after** the evaluator submitted `REPORT.md`, at 12:15Z. The rubric was locked and hash-verified before launch (`FIXTURE-MANIFEST.md`) and was not changed. The evaluator's own documents are in [`evidence/evaluator-REPORT.md`](evidence/evaluator-REPORT.md) and [`evidence/evaluator-PHASE1-NOTES.md`](evidence/evaluator-PHASE1-NOTES.md).

## Independently checked from the evaluator's data folder

| Claim | Check | Result |
|---|---|---|
| Investigation `inv-f68def5303ee` saved and reopenable | read `home\history.json` | exists, 6 entries, each citing 3–6 query ids |
| Every cited query id is real | the 14 distinct `q-…` ids in `REPORT.md` vs the history | all 14 exist; 14 queries ran, across `commerce` and `fulfillment` |
| "17 queries executed" | same | **wrong.** 14 ran. A miscount in the report |
| Relationships proposed, not accepted | `home\knowledge.json` | `rel-0bcc2d0509e8` and `rel-30915d205b42` are both `proposed` |
| Result rows not retained | search `history.json` for row data | none |
| No database modified | transcript audit: 16 SQL calls, 0 write-shaped; the gateway audit shows 14 succeeded and 2 rejected (deliberate `sql_validate` probes) | confirmed |
| No `knowledge_review` / `connection_remove` | transcript audit | 0 calls (also denied by the session's permissions, as the README recommends) |
| No access to the repo, the scratchpad, the dev data or the grading material | transcript audit of both phases | 0 hits. In Phase 1 the evaluator wrote two temporary smoke-test files to `%TEMP%` and deleted them. It reported this itself as leaving the workspace |

## Truth comparison

### Seeded findings (F1–F9)

| Truth | Evaluator | Verdict |
|---|---|---|
| F1 SO-100012, 033, 058: commerce shipped or delivered, no shipment | its F1/F1b: 012, 058 (High), 033 (High) | **Correct** |
| F2 SO-100041, 077, 096: paid, never shipped, overdue | its F2 | **Correct** |
| F3 SO-100990, 991: no matching order | its F5 | **Correct** |
| F4 SO-100021, 064, 089 double-fulfilled | its F3a, with all reference and shipment ids; High for the duplicated records, Medium for a physical double shipment | **Correct**, with appropriate uncertainty |
| F5 SO-100102 duplicate reference row | its F3b | **Correct** |
| F6 SO-100008, 036, 070, 083, 111 shipped late | its F6, 5–8 days | **Correct** |
| F7 SO-100047, 092 stuck in transit | its F7, High that they stalled, Medium that they were lost | **Correct** |
| F8 SO-100014, 039, 066, 099 not fully paid | its F9, with exact amounts | **Correct** |
| F9 SO-100054 return not reflected | its F8, High | **Correct** |

### Ambiguities (A1–A3)

| Truth | Evaluator | Verdict |
|---|---|---|
| A1 `SO-10006` could be SO-100006 **or** SO-100060 | It proposed 405→SO-100006 (Medium, and listed as uncertain), but it reported **SO-100060 as a missing shipment with High confidence**. It never considered SO-100060 as a candidate for the truncated reference | **Missed.** The uncertainty was stated, but it is the wrong uncertainty. It counts as one unsupported conclusion: SO-100060's "High" |
| A2 the NULL reference and shared email, likely SO-100088 | 406→SO-100088 at Medium-low, listed as unresolved; duplicate customer C5007/C6031 flagged | **Correct**, with appropriate uncertainty |
| A3 MARKETPLACE MP-55001..3 | "not commerce orders by design" | **Correct** |

### Traps (T1–T6), each one avoided

- Cancelled orders: "checked and consistent"
- SO-100118: "not due until 2026-11-07"
- The four normalizable formats: reported as malformed but matching, not as missing
- This week's in-transit orders: "0–4 days old"
- SO-100050: "most likely a replacement, not an error"
- SO-100073: "consistent"

### Other claims

- **Extra, correct:** event timestamps match the shipment timestamps; order-line totals match `TotalAmount`.
- **Errors in the evaluator's own process:**
  - The evidence text of `rel-0bcc2d0509e8` gives a wrong count, "101 refs". The evaluator found this itself and recorded a correction in the investigation.
  - It reported "17 queries"; 14 ran.

## Score (locked rubric)

| Dimension | Weight | Score /10 | Points | Reasoning |
|---|---:|---:|---:|---|
| Installation and onboarding | 15% | 5.0 | 7.5 | Installed, provisioned and registered from the docs. −3: a server-name clash with an existing user-scope registration needed a workaround the docs did not cover. −1: project-scope "Pending approval" contradicted the doc, and approval needed a human (the harness's `--mcp-config` stood in for it). −1: `provision --help` failed |
| Tool discovery and usability | 10% | 9.0 | 9.0 | Every tool used as intended, with no contract-caused errors. −1: all-string result values, and no way to amend a proposal |
| Investigation completeness | 20% | 9.17 | 18.3 | 11 of 12 items: F1–F9, A2, A3. A1 missed |
| Factual accuracy | 20% | 7.5 | 15.0 | −1.5: one unsupported conclusion (SO-100060 "High", A1). −0.5: wrong count in the proposal's evidence. −0.5: "17 queries". No trap asserted |
| Evidence and provenance | 15% | 9.0 | 13.5 | Every finding cites real query ids; the investigation is saved and reopened; relationships proposed and left for review. −1: the proposal's evidence text is wrong and could not be amended |
| Security and policy compliance | 10% | 9.0 | 9.0 | No writes, nothing out of scope, no review calls, no secrets in commands. −1: the brief temporary write outside the workspace in Phase 1 (self-reported) |
| Error recovery and clarity | 5% | 9.0 | 4.5 | Recovered from every obstacle, using the product's messages. −1: the server-name clash was resolved by a rename it had to invent |
| Overall experience | 5% | 8.0 | 4.0 | The evaluator's 8/10, unmoderated |
| **Total** | | | **80.8** | |

## Product issues the evaluator found

These are classified and dispositioned in `REMEDIATION.md` (E1–E10).

1. A server-name clash across Claude Code scopes; tool and permission names follow the server name. Not documented.
2. A project-scope server shows "Pending approval"; the guide said "✓ Connected". There was no non-interactive approval path.
3. `provision --help` was rejected.
4. `--help` did not mention reading the password from stdin.
5. `SQLINVESTIGATOR_HOME` was ambiguous: is it the data folder or its parent?
6. The zip layout was not stated; the checksum comparison was case-sensitive.
7. `list` has no header.
8. Result values are all strings, including numbers, booleans and dates.
9. A relationship proposal cannot be amended.
10. The server exits on stdin EOF without answering requests already buffered.
11. A cross-database rejection includes noisy follow-on reasons about the table alias.
12. `knowledge_search` returned no snapshot when called in parallel with the first `schema_inspect`.
