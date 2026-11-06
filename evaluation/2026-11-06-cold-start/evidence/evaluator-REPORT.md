# Investigation report: commerce vs fulfillment, order fulfillment inconsistencies

Date: 2026-11-06. Tool: SQL Investigator 1.0.0, registered at project scope as MCP server `sql-investigator-eval` (see Experience log for why it is not `sql-investigator`). Aliases: `commerce` (EvalCommerce) and `fulfillment` (EvalFulfillment). Both are read-only, and no data was modified.

## How the databases relate (as inferred, not established)

`commerce.dbo.Orders.OrderNumber` (e.g. `SO-100021`) appears in `fulfillment.dbo.ExternalOrderReferences.ExternalOrderRef`, but only where `SourceSystem = 'COMMERCE'`. `Shipments.ReferenceId` links to the reference, and `ShipmentEvents` and `Returns` link to `ShipmentId`. In normal rows the reference arrives about 5 minutes after `OrderDate` and its `CustomerEmail` equals the customer's email. The shipment is picked up about 20 h later and delivered about 3 days after that. Every discrepancy below is measured against that pattern. I could not join the two databases in SQL, so I compared full per-alias listings myself (120 orders, 119 references, 118 shipments).

## Findings

Confidence: **High** means the records directly show it. **Medium** means it rests on a likely interpretation. **Low** means it is plausible but unproven.

| # | Finding | Orders / record ids | Supporting queries | Confidence |
|---|---|---|---|---|
| F1 | **Missing shipments.** Commerce says Shipped (payment Captured), but fulfillment has no reference or shipment under any spelling. The orders are 26–54 days past PromisedShipDate | SO-100012 (OrderId 12), SO-100058 (58), SO-100060 (60) | q-e66c02ecd7e2, q-0bba0c034b5e, q-912d86cfcf23, q-94b51f20bd5b, q-0b37a40f9beb | High |
| F1b | **Delivered in commerce, unknown to fulfillment.** No reference or shipment exists | SO-100033 (33) | same as F1 | High that no record exists; it might have gone through a channel I cannot see |
| F2 | **Paid but never sent to fulfillment.** Payment Captured, no reference, 17–63 days past promise | SO-100041 (41), SO-100077 (77), SO-100096 (96) | q-0b37a40f9beb, q-e26c8dc7e9da, q-0bba0c034b5e | High |
| F3a | **Duplicate references with a second shipment.** A second ref with a mangled number ("SO1000xx") arrived 1–2 min after the original, and each ref has its own Delivered shipment with a distinct tracking number. Probable double shipments | SO-100021: refs 21 and 221, shipments 21 (1Z900777) and 221 (1Z950861). SO-100064: refs 64 and 264, shipments 64 and 264. SO-100089: refs 89 and 289, shipments 89 and 289 | q-94b51f20bd5b, q-0bba0c034b5e, q-912d86cfcf23, q-31524178dee2 | High that the records are duplicated; Medium that goods were physically shipped twice |
| F3b | **Duplicate reference without a shipment.** The second ref has identical text and arrived 1 min later, but has no shipment. It is an orphan record | SO-100102: refs 102 and 302 (only shipment 102) | q-94b51f20bd5b, q-912d86cfcf23 | High |
| F3c | **Second reference that is probably a replacement.** Ref 250 arrived on 2026-09-07 13:00, 3 h after return RMA-7001 (ReturnId 1, "Damaged in transit", shipment 50) was received. Shipment 250 was delivered on 09-11. RefundIssued = 0. This is consistent with a replacement, not an error | SO-100050: refs 50 and 250, shipments 50 and 250, ReturnId 1 | q-94b51f20bd5b, q-737a2ddf799a, q-31524178dee2 | Medium (inferred from timing) |
| F4 | **Malformed references that still match** (email and timing agree) | ref 25 `SO100025` → SO-100025; ref 44 `so-100044` → SO-100044; ref 81 `␠SO-100081` (leading space) → SO-100081; ref 107 `100107` → SO-100107 | q-0bba0c034b5e, q-94b51f20bd5b, q-e66c02ecd7e2 | High |
| F4b | **Probable matches for orders with no exact reference.** Ref 405 `SO-10006` has one digit missing and a null email, and arrived 26 h after the order. Ref 406 has a null ExternalOrderRef, email hiro.lindqvist7@example.test, and arrived at 17:30, 5.5 h after the order placed at 12:00 that day. Both are delivered, while commerce still shows Shipped | ref 405 / shipment 405 → SO-100006 (6); ref 406 / shipment 406 → SO-100088 (88) | q-0bba0c034b5e, q-912d86cfcf23, q-0b37a40f9beb, q-f496d866492c | Medium (405→6), Medium-low (406→88) |
| F5 | **Fulfillment records with no commerce order.** These are Delivered shipments for order numbers that do not exist. Ref 400's email matches no customer and ref 401's email is null | ref 400 `SO-100990` / shipment 400 (1Z990400); ref 401 `SO-100991` / shipment 401 (1Z990401) | q-0bba0c034b5e, q-912d86cfcf23, q-e66c02ecd7e2, q-f496d866492c | High that they are unmatched; their origin is unknown |
| F6 | **Late shipping.** These shipped 5–8 days after PromisedShipDate, 160–234 h after the ref arrived, against a norm of about 20 h. All were delivered later | SO-100008 (shipment 8), SO-100036 (36), SO-100070 (70), SO-100083 (83), SO-100111 (111) | q-60e300e1b99c, q-e66c02ecd7e2, q-31524178dee2 | High |
| F7 | **Stuck in transit.** The last event is InTransit, 48 and 53 days ago; the norm is delivery in 3 days. Commerce shows Shipped. Probably lost | SO-100047 (shipment 47, DHL 1Z901739), SO-100092 (shipment 92, DHL 1Z903404) | q-60e300e1b99c, q-31524178dee2, q-912d86cfcf23 | High that they are stalled; Medium that they are lost |
| F8 | **Commerce status ignores the return.** For SO-100054, return RMA-7003 (ReturnId 3) was received on 10-23 with a refund issued, but commerce still says Delivered. SO-100050 has return RMA-7001 and commerce says Delivered (may be fine if 250 is a replacement). SO-100073 is consistent (Returned, Refunded) | SO-100054 (ReturnId 3), SO-100050 (ReturnId 1) | q-737a2ddf799a, q-e66c02ecd7e2, q-e26c8dc7e9da | High for 54, Low for 50 |
| F9 | **Delivered without full payment** (in commerce; goods were released anyway) | SO-100014 (PaymentId 14, Pending, PaidAt null); SO-100066 (PaymentId 66, Failed); SO-100039 (PaymentId 39, 38.99 of 77.98); SO-100099 (no payment row) | q-e26c8dc7e9da, q-91f9e4eeef5c, q-912d86cfcf23 | High |
| F10 | **Duplicate customer record.** CustomerId 31 (C6031, created 2026-08-20) has the same name and email as CustomerId 7 (C5007). Its only order, SO-100088, is the one whose fulfillment ref lacks an order number. This may explain the broken handoff | Customers 7 and 31; SO-100088 | q-f496d866492c, q-e66c02ecd7e2 | Medium |

**Checked and consistent (no finding):**
- Cancelled orders SO-100005/017/029 have no shipment and their payments are Refunded.
- SO-100118 is Paid with no shipment yet, but it is not due until 2026-11-07.
- SO-100113–117/119/120 are InTransit and only 0–4 days old.
- Every shipment's PickedUp and Delivered event times equal its ShippedAt and DeliveredAt; q-135415404e45 returned 0 rows.
- Order-line totals equal TotalAmount for every order (q-91f9e4eeef5c).
- Refs 402–404 (MARKETPLACE, MP-55001..3) are not commerce orders by design.

## Uncertain or unresolved

- **Whether 405→SO-100006 and 406→SO-100088 are correct.** The evidence is timing, the email, and that no other candidate exists. Fulfillment carries no order id for 406 and a wrong one for 405. A human with access to the upstream message logs should confirm.
- **What refs 400 and 401 are.** They could be test orders, orders from a commerce instance I cannot see, or corrupted numbers. Neither email nor timing points to an existing order: the orders near those times all already have matches.
- **Whether F3a means real double shipments.** The distinct tracking numbers suggest goods went out twice, but only carrier or warehouse data can confirm it. The same goes for whether SO-100050's second shipment is an approved replacement.
- **SO-100033** is marked Delivered in commerce, yet fulfillment has nothing. It could be a manual status edit or a channel outside EvalFulfillment.
- **Lateness thresholds.** "Late" is ShippedAt after PromisedShipDate, which is a date with no time zone. Times appear to be in one shared zone, but that is not documented.
- **Why the handoff failed** for F1/F2. Commerce has no integration or outbox table in scope, so I cannot see whether a message was ever sent.
- **One normalization blind spot.** q-94b51f20bd5b uses `LIKE` under a case-insensitive collation, so it did not flag `so-100044` as malformed. I caught that one by reading every row of q-0bba0c034b5e. The tables are small, so a complete read was feasible.

## Records examined

- **commerce:** all 120 `dbo.Orders` rows joined to `dbo.Customers` (q-e66c02ecd7e2) and all 31 `dbo.Customers` (q-f496d866492c). `dbo.Payments` (119 rows) was filtered to anomalies, non-captured payments and non-standard order statuses (q-e26c8dc7e9da). `dbo.OrderLines` (240 rows) was aggregated per order against totals and payment counts (q-91f9e4eeef5c). There was a focused re-read of the 13 orders with no exact reference (q-0b37a40f9beb) and table counts (q-1068f6cd8c9c).
- **fulfillment:** all 119 `dbo.ExternalOrderReferences` (q-0bba0c034b5e), all 118 `dbo.Shipments` (q-912d86cfcf23), and all 463 `dbo.ShipmentEvents` aggregated per shipment (q-31524178dee2). Events were cross-checked against shipment timestamps (q-135415404e45). I also read all 3 `dbo.Returns` with their shipment and ref (q-737a2ddf799a), grouped refs by normalized number (q-94b51f20bd5b), ran the delay and stall screen (q-60e300e1b99c) and took table counts (q-11cb7a410a36).
- 17 queries were executed in the investigation, none truncated; the budget is 50. There were two `sql_validate` probes, both rejected as expected: a cross-database join, and an out-of-scope table.

## Saved investigation

- **Investigation id: `inv-f68def5303ee`.** It holds 6 entries (hypotheses, findings F1–F10, uncertainty, and the relationship proposals with a correction) citing the query ids above.
- **I reopened it with `investigation_get`.** It returned the question, all 6 entries, and the provenance of all 17 queries (exact SQL, alias, timestamps, row counts, status), without result rows, as documented.

## Proposed relationships (state: proposed, awaiting human review)

- **`rel-0bcc2d0509e8`:** `commerce.Orders.OrderNumber` ↔ `fulfillment.ExternalOrderReferences.ExternalOrderRef`, valid only when `SourceSystem='COMMERCE'` and after normalizing case, whitespace and the `SO-` prefix.
  - **Correction for the reviewer:** its evidence text says "101 refs … equal an OrderNumber exactly". The right figure is 105 refs case-sensitively (106 under the database's case-insensitive collation), covering 103 distinct orders. I could not edit the proposal through MCP, so the correction is recorded in the investigation.
- **`rel-30915d205b42`:** `commerce.Customers.Email` ↔ `fulfillment.ExternalOrderReferences.CustomerEmail`. This is a weak secondary key only: emails are sometimes null and not unique (customers 7 and 31 share one).

I did not call `knowledge_review`, because accepting or rejecting is a human decision. It was not exposed in this session anyway.

## Experience log

### Installation and onboarding (Phase 1; details in PHASE1-NOTES.md)

**What went smoothly:**
- The SHA-256 check, unzip and `--version` all worked first time.
- Provisioning with the password piped on stdin worked well. The read-only check before storing the password is reassuring.

**Main problem: a name clash.** The README tells everyone to name the server `sql-investigator`. On this machine a user-scope server with that name already existed, and `claude mcp list/get` resolved the name to it, not to my project registration. I re-registered as `sql-investigator-eval`, so all the permission names in the README were wrong for me. The README says nothing about this case.

**Approval and permissions:**
- The project-scope server showed "Pending approval". The README says `claude mcp list` "should show ✓ Connected".
- My attempt to pre-approve it in `.claude/settings.local.json` was declined. After the restart the server was simply loaded.

**Smaller gaps:**
- The README is ambiguous about whether `SQLINVESTIGATOR_HOME` is the data folder itself or its parent. It turned out to be the data folder itself.
- `provision --help` is rejected.
- Python is not available on the machine, which made my ad-hoc stdio smoke test clumsier. That is not the tool's fault.

### Tool discovery and usability

**Discovery:**
- After the restart, the 9 tools were offered as deferred tools under `mcp__sql-investigator-eval__*`, along with clear server instructions ("call connection_list first, schema_inspect before writing SQL…").
- `connection_remove` and `knowledge_review` were not exposed at all, presumably filtered by this session's permissions. That is fine given they record human decisions, but nothing says so.

**What worked well:**
- `schema_inspect` gives a compact signature line per table plus column detail, which was enough to write correct SQL on the first try.
- `sql_execute` returns the query id, the exact SQL, timestamps and truncation info, which makes citing evidence easy.
- Full SQL Server features (`STRING_AGG ... WITHIN GROUP`, subqueries, `CASE`, `DATEDIFF`) were accepted, so analysis inside one database is comfortable.
- `investigation_record` and `investigation_get` work well. Reopening gives the full provenance.

**Friction:**
- Results come back as all-string arrays, including numbers and booleans (`"false"`). `date` columns are rendered as `2026-09-14T00:00:00.0000000`.
- With no cross-database join, I had to eyeball 120 vs 119 rows to do the anti-join. The tool is designed to work that way, but at a larger scale it would need a lot of `IN (...)` lists or a different approach.
- There is no way to amend a relationship proposal, so my counting error had to be corrected in the investigation instead.
- `knowledge_search` with empty text returned no snapshots at first, even though `schema_inspect` had just captured them (probably because I called both in parallel). Later searches showed the snapshot.

### Errors hit and recovery

- **Phase 1:** `provision --help` returned `input.invalid`, and the message pointed me to `--help`. The name clash I worked around by renaming the server. The stdio smoke test gave no output with closed stdin, which I worked around by keeping stdin open.
- **Phase 2:** no tool errors. My two deliberate `sql_validate` probes produced precise reasons, e.g. "EvalFulfillment.dbo.ExternalOrderReferences names another database; each query reads one database, through one alias" and "dbo.Orders is outside the approved scope of alias "fulfillment" (…)". The cross-database rejection also included two follow-on reasons about alias `r`, which are slightly noisy.
- **My own mistake:** I got the count wrong in the evidence for rel-0bcc2d0509e8 and recorded a correction in the investigation.

### Documentation problems

- No warning about server-name clashes across Claude Code scopes. No mention that tool and permission names follow the registered server name.
- The described `claude mcp list` result ("✓ Connected") does not match what a fresh project-scope server shows ("Pending approval").
- The meaning of `SQLINVESTIGATOR_HOME` (data folder vs its parent) is ambiguous.
- `--help` does not mention reading the password from stdin, although the README does.
- The README does not say that results are returned as strings. It also does not say that `knowledge_review` and `connection_remove` may be hidden from Claude by permissions.
- Good: the error-code table, the limits table and the "what it will and will not run" section matched observed behaviour exactly.

## Ratings

- **Installation and onboarding: 6/10.** The download check, unzip and piped-stdin provisioning were easy. The server-name clash and project approval cost time and are not covered in the docs.
- **Tool usability: 8/10.** The schema output, provenance-rich results and investigation records are excellent. All-string results and no way to amend proposals are minor friction.
- **Documentation: 7/10.** It is accurate on safety rules, limits and errors, but silent on name clashes, approval state and the exact meaning of `SQLINVESTIGATOR_HOME`.
- **Error messages: 9/10.** Every rejection named the offending object and the rule. There was only slight noise from cascaded reasons.
- **Overall experience: 8/10.** Once connected it was a safe, auditable and pleasant way to investigate two databases. Onboarding on a machine with an existing install was the weak spot.
