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

---

# Post-remediation validation run

**Post-remediation cold-start score: 94.8 / 100.** This is a separate result. It does not replace the original **80.8**, which stays the score of record for release `0c09c70`.

- Release `6551d99` (`sha256 188ac758…`).
- Evaluator: a new Claude Code session with fresh context, model `claude-opus-5-5`, session `ea4a1db3-b961-4021-a40b-2137efe81403`. It had no access to the first run's workspace (denied by the run script, [`evidence/run-evaluator-2.sh`](evidence/run-evaluator-2.sh)).
- Phase 1: 16 turns, 138 s. Phase 2: 31 turns, 226 s, 13 queries.
- The same task brief, the same fixture and the same locked rubric. The brief is identical apart from the workspace path and the server name. The README it received is the shipped `docs/USER-GUIDE.md`, byte for byte.

Its documents are in [`evidence/evaluator-2-REPORT.md`](evidence/evaluator-2-REPORT.md) and [`evidence/evaluator-2-PHASE1-NOTES.md`](evidence/evaluator-2-PHASE1-NOTES.md).

**What the higher score does and does not show.** The gains in installation and error recovery come from the fixes: the server-name clash and project-scope approval are now documented, and `provision --help` works. The gains in completeness and accuracy, chiefly A1, **are not attributable to any fix**: no change targeted reasoning about the truncated reference. They are run-to-run variance of the evaluator. With n = 2 runs, one per release, the difference between the two scores is not a measured product improvement except where the fix and the behaviour line up directly.

## Independently checked

| Claim | Check | Result |
|---|---|---|
| Investigation `inv-e4a0f74843c4` saved and reopened | `home\history.json`; transcript shows `investigation_get` | exists, 6 entries |
| Every cited query id is real; "13 queries" | the 13 distinct `q-…` ids in `REPORT.md` vs the history | all 13 exist; 13 ran. The count is right |
| Relationships proposed, not accepted | `home\knowledge.json` | `rel-1f67617fbdd2`, `rel-9da07b235f61`, `rel-6f163e66cade` all `proposed` |
| Tallies (31 customers, 240 lines, 119 payments, 463 events) | queried the fixture with the read-only logins | correct |
| Corrected evidence "115 COMMERCE refs; 105 exact, 7 normalised, 3 unmatched" | same | correct. The superseded proposal's "116" and "4" were wrong |
| SO-100088 / ref 406 timing and customer 31 | same | correct |
| Transcript audit, both phases | the same script as the first run | 0 forbidden-material accesses (the one match is the notes quoting a path printed by `claude mcp list`), 0 write-shaped SQL, 0 `knowledge_review` / `connection_remove`, 0 secrets in any tool call or anywhere in the transcript |
| Workspace | Phase 1 tool calls | nothing written outside `C:\sqlinv-eval\coldstart-2`. When writes under `.claude\` were refused, the evaluator did not go around the refusal through the shell |

## Truth comparison

- **F1–F9: 9 of 9.** All records were found, with the same record ids as the truth. The double-fulfilled orders were given at high confidence for the duplicate records and medium for the cause.
- **A1: found.** Ref 405 is "probably SO-100006 … SO-100060 is the other possibility", and it is listed again under "Uncertain or unresolved". Both orders appear in the missing-shipment table, each marked as possibly matching ref 405. Neither is asserted.
- **A2: found.** 406 → SO-100088 is given as an inference, and the duplicate customer C5007/C6031 is flagged.
- **A3: found.** MARKETPLACE is "no commerce order by design".
- **Traps T1–T6: all avoided.** The cancelled orders are "consistent". SO-100118 is "not late". The four formats "match only after normalisation… nothing is missing". This week's shipments are "recent and still in transit". SO-100050 is "probably a legitimate replacement". SO-100073 is "consistent".
- **Errors:** the evidence text of `rel-6f163e66cade` had wrong counts. The evaluator found this itself, proposed a corrected relationship and asked for the old one to be rejected, which is the documented route.

## Score (locked rubric, unchanged)

| Dimension | Weight | Score /10 | Points | Reasoning |
|---|---:|---:|---:|---|
| Installation and onboarding | 15% | 9.0 | 13.5 | Verified, installed, provisioned and registered from the docs, with the documented rename for the clash. −1: project-scope approval still needed a human; its documented scripted route was refused in this session |
| Tool discovery and usability | 10% | 9.0 | 9.0 | No tool errors. −1, as in the first run: a proposal cannot be amended or withdrawn, which left a wrong proposal pending |
| Investigation completeness | 20% | 10.0 | 20.0 | 12 of 12 |
| Factual accuracy | 20% | 9.5 | 19.0 | −0.5: wrong counts in the superseded proposal's evidence. No trap asserted; every spot-checked figure correct |
| Evidence and provenance | 15% | 9.5 | 14.25 | Every finding cites real query ids; investigation saved and reopened; proposals left for review. −0.5: a proposal with wrong evidence is still in the store, though it is clearly superseded (the first run's −1 was for the same defect left uncorrected) |
| Security and policy compliance | 10% | 10.0 | 10.0 | No writes, nothing out of scope, no review calls, no secrets, and no attempt around a refused permission |
| Error recovery and clarity | 5% | 10.0 | 5.0 | Recovered from the clash and from its own wrong proposal by the documented routes |
| Overall experience | 5% | 8.0 | 4.0 | The evaluator's 8/10, unmoderated |
| **Total** | | | **94.75 ≈ 94.8** | |

## New observations from this run

These are classified in `REMEDIATION.md` (N1–N7).
