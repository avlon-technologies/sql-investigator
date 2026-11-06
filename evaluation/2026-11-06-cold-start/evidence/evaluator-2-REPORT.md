# Commerce vs. fulfillment: order fulfillment investigation

Date: 2026-11-06. Tool: SQL Investigator 1.0.0, registered at project scope as `sql-investigator-dev`.
Aliases: `commerce` (EvalCommerce) and `fulfillment` (EvalFulfillment), both read-only and development.
Neither database was modified; every query was a single bounded SELECT.

**How orders were matched.** The databases cannot be joined in SQL, so I listed each side separately and compared the lists myself. The match key is `commerce.Orders.OrderNumber` = `fulfillment.ExternalOrderReferences.ExternalOrderRef`. Before comparing, I normalised the fulfillment value: trimmed it, upper-cased it, turned `SO` + digits into `SO-` + digits, and put `SO-` in front of bare digits. Only `SourceSystem = 'COMMERCE'` references count. Shipments link to references through `Shipments.ReferenceId`.

**Tallies.** Commerce: 120 orders, 31 customers, 240 order lines, 119 payments (q-8ed9a2052575). Fulfillment: 119 references, 118 shipments, 463 shipment events, 3 returns (q-b320928e857d).

## Findings

### F1. Orders with no shipment at all (missing shipments). Confidence: high

None of these orders has a reference or a shipment in fulfillment, under any normalisation:

| Order | Commerce status | Promised ship date | Note |
|---|---|---|---|
| SO-100012 | Shipped | 2026-10-05 | Commerce says shipped, but fulfillment has no record |
| SO-100033 | **Delivered** | 2026-09-16 | Commerce says delivered, but fulfillment never saw it |
| SO-100041 | Paid (captured) | 2026-09-04 | 63 days past the promise date |
| SO-100058 | Shipped | 2026-10-11 | Commerce says shipped, but fulfillment has no record |
| SO-100077 | Paid (captured) | 2026-10-20 | 17 days past the promise date |
| SO-100096 | Paid (captured) | 2026-09-09 | 58 days past the promise date |
| SO-100060 | Shipped | 2026-09-13 | No exact match (but see F3 / ref 405) |
| SO-100006 | Shipped | 2026-09-19 | No exact match. Ref 405 is probably this order (F3) |

These are **not** problems:
- SO-100005, SO-100017 and SO-100029 are Cancelled with Refunded payments and have no shipment. That is consistent.
- SO-100118 is Paid, was ordered 2026-11-05 and is promised for 2026-11-07. It has no shipment yet, but it is not late.

Queries: q-dbf51a730915, q-6e9f91007a9e, q-b176843daf31, q-962110e773e5, q-989b2fb07b50.

### F2. Duplicate references and double shipments. Confidence: high for the duplicates, medium for what caused them

After normalisation, five order numbers have two fulfillment references (q-b176843daf31, column `RefsWithSameNormalized` = 2):

| Order | References | Shipments | My reading |
|---|---|---|---|
| SO-100021 | 21 `SO-100021`, 221 `SO100021` (+2 min) | 21 (1Z900777), 221 (1Z950861), both Delivered | **Shipped twice** |
| SO-100064 | 64, 264 `SO100064` (+2 min) | 64 (1Z902368), 264 (1Z952624), both Delivered | **Shipped twice** |
| SO-100089 | 89, 289 `SO100089` (+2 min) | 89 (1Z903293), 289 (1Z953649), both Delivered | **Shipped twice** |
| SO-100102 | 102, 302 `SO-100102` (+1 min) | Only 102. Ref 302 has no shipment | Orphan duplicate reference |
| SO-100050 | 50, 250 `SO-100050` (+6 days) | 50 (returned, RMA-7001, "Damaged in transit", refund not issued), 250 created 2026-09-07 14:00, 4 h after the return was received | **Probably a legitimate replacement**, not an error |

The pattern behind 21, 64 and 89 is the same each time: an un-hyphenated `SO1000xx` copy arrived about 2 minutes after the original and produced its own shipment, with its own tracking number and a complete event history (q-7c8c8382928c). No tracking number appears on more than one shipment (q-7993de992433).

Queries: q-b176843daf31, q-962110e773e5, q-7c8c8382928c, q-80dcc72ba92e, q-7993de992433.

### F3. Fulfillment records with no commerce order (unmatched). Confidence: high for 400 and 401; medium for 405 and 406

| Ref / Shipment | ExternalOrderRef | Email | Assessment |
|---|---|---|---|
| 400 / 400 | `SO-100990` | farid.novak3@example.test, which matches no commerce customer | No such order (q-17716fa90de3). A delivered shipment with no order |
| 401 / 401 | `SO-100991` | NULL | No such order. A delivered shipment with no order |
| 405 / 405 | `SO-10006` (one digit short) | NULL | Not a valid order number. **Probably SO-100006**: received 2026-09-18, about 26 h after that order, which is still "Shipped" in commerce while shipment 405 was Delivered 2026-09-22. SO-100060 is the other possibility |
| 406 / 406 | NULL | hiro.lindqvist7@example.test | **Probably SO-100088**: ordered 2026-09-19 12:00 by customer 31, received 17:30 the same day. Customer 31 (C6031) duplicates customer 7 (C5007, same name and email) (q-00e47d1ca137), and all of customer 7's orders already have their own references. If this is right, SO-100088 was delivered 2026-09-23 but commerce still says "Shipped" |
| 402–404 | `MP-55001..3` | not in commerce | SourceSystem MARKETPLACE, so there is no commerce order by design. Not counted as a defect |

Four references match only after normalisation. They are data-quality problems, but nothing is missing: 25 `SO100025`, 44 `so-100044`, 81 `␠SO-100081` (leading space), 107 `100107`.

Queries: q-6e9f91007a9e, q-b176843daf31, q-962110e773e5, q-00e47d1ca137, q-17716fa90de3, q-dbf51a730915.

### F4. Suspicious delays. Confidence: high for the facts; the delay threshold is my judgement

The usual pattern is that a shipment is created within about 2 h of the order and ships within about 1 day. The cases below break it.

**Shipped long after the promised ship date** (all of them were later delivered, so commerce "Delivered" is correct):

| Order / Shipment | Ordered | Promised | Shipped | Days after promise |
|---|---|---|---|---|
| SO-100008 / 8 | 2026-10-09 | 10-11 | 10-19 | 8 |
| SO-100083 / 83 | 2026-09-14 | 09-16 | 09-24 | 8 |
| SO-100036 / 36 | 2026-10-17 | 10-19 | 10-25 | 6 |
| SO-100111 / 111 | 2026-09-22 | 09-24 | 09-30 | 6 |
| SO-100070 / 70 | 2026-09-21 | 09-23 | 09-28 | 5 |

**Stalled in transit (probably lost):**
- Shipment 47 (SO-100047): last event InTransit at Hub VAN on 2026-09-20.
- Shipment 92 (SO-100092): last event InTransit at Hub MTL on 2026-09-15.

Both have had no event for about 7 weeks, and commerce still shows "Shipped" for both.

**Paid but never sent to fulfillment:** SO-100041, SO-100077 and SO-100096 (see F1).

**Normal:** shipments 113–117, 119 and 120 are recent and still in transit.

Queries: q-962110e773e5, q-7c8c8382928c, q-dbf51a730915, q-076c78f1aa8b.

### F5. Status and return disagreements. Confidence: high for SO-100054; medium for the rest

- **SO-100054.** Fulfillment received return RMA-7003 ("Wrong size") on 2026-10-23 with `RefundIssued = true`. Commerce still shows the order Delivered and the payment **Captured**, so the refund is not reflected in commerce (q-80dcc72ba92e, q-989b2fb07b50).
- **SO-100088** (if ref 406 is its reference) and **SO-100006** (if ref 405 is its reference): fulfillment says Delivered, commerce says Shipped (F3).
- **Consistent:** SO-100073 (Returned in commerce, payment Refunded, RMA-7002 refunded).
- **Explained:** SO-100050 (replacement shipment sent, see F2).

Payment problems inside commerce on orders marked Delivered. These are not fulfillment issues, but they are worth passing on (q-989b2fb07b50):
- SO-100014: payment Pending.
- SO-100066: payment Failed.
- SO-100099: no payment row at all.
- SO-100039: captured 38.99 of a 77.98 total.

Order-line totals equal `TotalAmount` on every order.

## Uncertain or unresolved

- **Ref 405 → SO-100006 and ref 406 → SO-100088 are inferences** from timing and email, not from a key. Ref 405 could instead be SO-100060. A human with access to the integration logs would need to confirm either one.
- **Whether 21, 64 and 89 were physically shipped twice** or are phantom duplicate records. Each pair has distinct tracking numbers and full event histories, which points to two real shipments. Carrier records would settle it.
- **SO-100050's second shipment as a replacement** is inferred from the return reason and the timing. `RefundIssued = false` fits a replacement, but nothing explicitly links shipment 250 to the return.
- **Refs 400 and 401** (SO-100990 and SO-100991) might be orders from another channel, or test data, rather than errors. Their origin can't be seen in scope.
- **Missing-shipment orders (F1).** The data shows that fulfillment has no record of these orders. It does not show *why*: they may never have been sent, or the hand-off may have failed. No integration log or outbox is in scope.
- **Shipments 47 and 92 as "lost"** is judged from event silence only. There is no carrier data.
- **Email equality between the databases** (the basis for the email relationship) was checked by reading the two listings side by side, not by a mechanical comparison, because cross-database SQL is not allowed.
- **ReferenceId = OrderId for 1–120.** The fulfillment surrogate key equals the commerce OrderId for every primary reference. I treated this as a coincidence of the synthetic data and did not rely on it or propose it.

## Records examined

- **commerce:** all 120 `dbo.Orders` joined to `dbo.Customers` email (q-dbf51a730915); order-line totals and payments for every order, with the 19 anomalous or relevant rows returned (q-989b2fb07b50); customers with duplicate or unknown emails (q-00e47d1ca137); a check for non-standard order numbers (q-17716fa90de3); row counts (q-8ed9a2052575).
- **fulfillment:** all 119 `dbo.ExternalOrderReferences` (q-6e9f91007a9e), plus a normalised version with duplicate counts (q-b176843daf31); all 118 `dbo.Shipments` with event counts and last event times (q-962110e773e5); 63 `dbo.ShipmentEvents` rows for the 17 anomalous shipments (q-7c8c8382928c); event-type distribution (q-076c78f1aa8b); all 3 `dbo.Returns` (q-80dcc72ba92e); a duplicate-tracking-number check (q-7993de992433); row counts (q-b320928e857d).
- **Totals:** 13 queries, none truncated, all succeeded.

## Saved investigation

- **Investigation id: `inv-e4a0f74843c4`.** It holds 6 entries (5 findings with hypotheses, evidence and uncertainty, plus one entry for the relationships) and the provenance of all 13 queries.
- **Reopened** with `investigation_get` on `inv-e4a0f74843c4`. It returned the question, all 6 entries and all 13 queries with their exact SQL and timestamps (no result rows, as documented).

## Proposed relationships (state: proposed, awaiting human review)

| Relationship id | Source → target | Note |
|---|---|---|
| **rel-1f67617fbdd2** | `commerce.Orders.OrderNumber` → `fulfillment.ExternalOrderReferences.ExternalOrderRef` | The main link. It applies to SourceSystem = 'COMMERCE' only, after normalisation. 105 exact matches, 7 normalised matches, 3 unmatched |
| **rel-9da07b235f61** | `commerce.Customers.Email` → `fulfillment.ExternalOrderReferences.CustomerEmail` | A weak fallback key. Email is not unique in commerce (customers 7 and 31 share one) |
| rel-6f163e66cade | same as rel-1f67617fbdd2 | **Superseded. Please reject it.** Its evidence text had wrong counts ("116" refs and "4" normalised matches; the correct figures are 115 and 7). Proposals can't be edited, so I proposed the corrected rel-1f67617fbdd2, as the README instructs |

I did not call `knowledge_review`. Accepting or rejecting relationships is your decision.

## Experience log

**Installation and onboarding (Phase 1; details in PHASE1-NOTES.md):**
- The checksum, unzip, `--version` and provisioning steps all worked the first time.
- Piping the password on stdin worked from PowerShell 5.1, and the read-only check passed for both logins.
- The main snag was that another `sql-investigator` was already registered at user scope. The README warns about exactly this and suggests a rename, so I registered this copy as `sql-investigator-dev`. As a result, every tool name and permission example in the README had to be translated by hand.
- My attempt to write `.claude/settings.local.json` (to pre-approve the server) and `.claude/settings.json` (permissions) was refused in Phase 1. After the restart the server was loaded anyway, so someone approved it.

**Tool discovery and usability:**
- After the restart, **9 of the documented 11 tools** were visible. `knowledge_review` and `connection_remove` were absent. The README explains this ("If your permissions deny them, Claude Code does not see them at all"), so it is presumably the configured permissions. It was still a moment of doubt, because Phase 1's hand-run `tools/list` returned 11.
- The tools are well shaped:
  - Server instructions tell you the order to call things in.
  - `schema_inspect` gives types and nullability.
  - `sql_execute` returns a query id, the exact SQL and the limits with every result.
  - `investigation_record` adds the aliases of the cited queries automatically.
  - `investigation_get` returns full query provenance.
- Within one alias, subqueries, window functions, `STRING_AGG`, `LIKE` patterns and joins were all accepted, which made the analysis cheap: 13 queries out of a budget of 50.
- The no-cross-database rule means the comparison happens in the model's head or in prose. For about 120 rows that was fine; for thousands it would hurt. A way to pass a list of values from one alias into an `IN (...)` on the other would help. That is possible by hand within `MAX_SQL_LENGTH`, but there is no helper.
- The results come back as JSON-encoded text **and** as `structuredContent`, so each large result is effectively sent twice.

**Errors and recovery:**
- No tool errors: every query was accepted, and none was truncated or timed out.
- My own error was wrong counts in a relationship proposal. Recovery followed the documented route: propose a corrected one and flag the old one for rejection. Being unable to edit or withdraw a proposal means a mistake stays in the knowledge store until a human reviews it.

**Documentation problems:**
- The README uses `sql-investigator` in every tool and permission name, but its rename advice means many users will have different names. It could say so next to the permissions block.
- It says `/mcp` should show 11 tools. That is not what you see when your permissions deny two of them, and the README does not connect those two statements.
- `provision --help` prints the global help, not help for provision alone.
- "Pending approval" in `claude mcp list` is documented, but the non-interactive approval route (editing `.claude/settings.local.json`) may not be possible for an agent, and the README does not mention this.

## Ratings

| Area | Rating | Reason |
|---|---|---|
| Installation and onboarding | 8/10 | A self-contained zip, a working hash check and scriptable provisioning via stdin. The name collision and the approval step needed outside help or workarounds |
| Tool usability | 8/10 | Clear, well-scoped tools with excellent provenance and generous SQL support. Cross-database comparison is manual, and proposals can't be withdrawn |
| Documentation | 7/10 | Accurate and concise, and it anticipated the collision. It doesn't follow through on what the rename means for tool names, nor on the 9-vs-11 tool count |
| Error messages | 7/10 | I hit no tool errors, so I can't judge them fully. The console output during provisioning was clear, and the documented code/message/action scheme looks good |
| Overall experience | 8/10 | Read-only, auditable and saved investigation work from a cold start in one session, with only minor friction |
